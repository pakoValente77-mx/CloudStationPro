using System.Data;
using Dapper;
using Npgsql;

namespace CloudStationWeb.Services
{
    /// <summary>
    /// Servicio de simulación hidrológica: toma lluvia pronosticada + datos reales de FunVasos
    /// y calcula escenarios de nivel/almacenamiento para las 5 presas de la cascada Grijalva.
    /// Basado en el modelo HUI (Hidrograma Unitario Instantáneo) y fórmula SCS-NRCS.
    /// </summary>
    public class HydroForecastService
    {
        private readonly string _pgConn;
        private readonly ILogger<HydroForecastService> _logger;

        // Mapeo cuenca → presa (para agrupar lluvia)
        private static readonly Dictionary<string, string> CuencaDam = new()
        {
            ["ang"] = "Angostura",
            ["mmt"] = "Chicoasen",
            ["mps"] = "Malpaso",
            ["pea"] = "Penitas"
        };

        // Orden de cascada
        private static readonly string[] CascadeOrder = { "Angostura", "Chicoasen", "Malpaso", "JGrijalva", "Penitas" };

        public HydroForecastService(IConfiguration config, ILogger<HydroForecastService> logger)
        {
            _pgConn = config.GetConnectionString("PostgreSQL") ?? "";
            _logger = logger;
        }

        /// <summary>
        /// Ejecuta simulación completa: obtiene condiciones iniciales reales + pronóstico de lluvia
        /// y calcula la evolución de nivel/almacenamiento por hora para las próximas 72h.
        /// </summary>
        public async Task<object> GetInputDataAsync(int horizonHours = 72)
        {
            using var db = new NpgsqlConnection(_pgConn);
            await db.OpenAsync();

            var initialConditions = await GetInitialConditionsAsync(db);
            var rainForecast = await GetRainForecastByCuencaAsync(db, horizonHours);
            var forecastDate = await GetLatestForecastDateAsync(db);
            var damParams = await LoadDamParamsAsync(db);

            // Condiciones iniciales por presa
            var dams = new List<object>();
            foreach (var damName in CascadeOrder)
            {
                var init = initialConditions.GetValueOrDefault(damName);
                var param = damParams.GetValueOrDefault(damName);
                var cuenca = param?.CuencaCode ?? "";
                var rain = rainForecast.GetValueOrDefault(cuenca);
                double totalRain = rain?.Sum(r => r.RainMm) ?? 0;

                dams.Add(new
                {
                    damName,
                    cuencaCode = cuenca,
                    elevacion = init != null ? Math.Round(init.Elevacion, 2) : (double?)null,
                    almacenamiento = init != null ? Math.Round(init.AlmacenamientoMm3, 2) : (double?)null,
                    aportacionQ = init != null ? Math.Round(init.AportacionQ, 2) : (double?)null,
                    extraccionQ = init != null ? Math.Round(init.ExtraccionHorariaMm3 * 1e6 / 3600.0, 2) : (double?)null,
                    fechaBase = init?.FechaBase.ToString("yyyy-MM-dd"),
                    ultimaHora = init?.UltimaHora,
                    totalRainMm = Math.Round(totalRain, 2),
                    curveNumber = param?.CurveNumber ?? 0,
                    drainCoeff = param?.DrainCoefficient ?? 0
                });
            }

            // Resumen lluvia horaria por cuenca (primeras horas)
            var rainSummary = new List<object>();
            foreach (var kv in rainForecast)
            {
                rainSummary.Add(new
                {
                    cuencaCode = kv.Key,
                    hours = kv.Value.Select(r => new
                    {
                        time = r.Time.ToString("yyyy-MM-dd HH:mm"),
                        rainMm = Math.Round(r.RainMm, 2)
                    })
                });
            }

            return new
            {
                success = true,
                forecastDate = forecastDate?.ToString("yyyy-MM-dd"),
                horizonHours,
                dams,
                rainByCuenca = rainSummary
            };
        }

        /// <summary>
        /// Ejecuta simulación completa.
        /// </summary>
        public async Task<HydroSimulationResult> RunSimulationAsync(
            int horizonHours = 72,
            Dictionary<string, double>? userExtractions = null,
            Dictionary<string, List<double>>? extractionSchedule = null,
            Dictionary<string, List<double>>? aportationSchedule = null,
            Dictionary<string, double>? userDrainCoefficients = null,
            Dictionary<string, double>? userCurveNumbers = null)
        {
            var result = new HydroSimulationResult
            {
                GeneratedAt = DateTime.UtcNow,
                HorizonHours = horizonHours
            };

            using var db = new NpgsqlConnection(_pgConn);
            await db.OpenAsync();

            // 1. Cargar curvas elevación-capacidad
            var curves = await LoadElevationCurvesAsync(db);

            // 2. Cargar parámetros del modelo por presa
            var damParams = await LoadDamParamsAsync(db);

            // 3. Cargar coeficientes HUI
            var huiCoeffs = await LoadHuiCoefficientsAsync(db);

            // 4. Obtener condiciones iniciales reales (último dato FunVasos)
            var initialConditions = await GetInitialConditionsAsync(db);

            // 5. Obtener pronóstico de lluvia por cuenca (próximas 72h)
            var rainForecast = await GetRainForecastByCuencaAsync(db, horizonHours);

            // 6. Fecha del pronóstico
            result.ForecastDate = await GetLatestForecastDateAsync(db);

            // 7. Simular cada presa en orden de cascada
            foreach (var damName in CascadeOrder)
            {
                var param = damParams.GetValueOrDefault(damName);
                if (param == null) continue;

                var cuencaCode = param.CuencaCode;
                var curvesForDam = curves.GetValueOrDefault(damName) ?? new List<ElevCapPoint>();
                var hui = huiCoeffs.GetValueOrDefault(cuencaCode) ?? new List<double> { 1.0 };
                var initCond = initialConditions.GetValueOrDefault(damName);
                var rain = rainForecast.GetValueOrDefault(cuencaCode) ?? new List<HourlyRain>();

                // Override coeficientes si el usuario los editó
                double effectiveDrainCoeff = param.DrainCoefficient;
                double effectiveCN = param.CurveNumber;
                if (userDrainCoefficients != null && userDrainCoefficients.TryGetValue(damName, out double userDC))
                    effectiveDrainCoeff = userDC;
                if (userCurveNumbers != null && userCurveNumbers.TryGetValue(damName, out double userCN))
                    effectiveCN = userCN;

                // Obtener extracciones de la presa aguas arriba (si aplica)
                List<double>? upstreamExtractions = null;
                if (param.HasPreviousDam && param.PreviousDamName != null)
                {
                    var prevSim = result.DamSimulations.FirstOrDefault(s => s.DamName == param.PreviousDamName);
                    if (prevSim != null)
                    {
                        upstreamExtractions = prevSim.HourlyData.Select(h => h.ExtractionMm3).ToList();
                    }
                }

                // Override extracción si el usuario editó el valor
                // Prioridad: extractionSchedule (por día) > userExtractions (constante)
                double? userExtractionMm3 = null;
                List<double>? hourlyExtractionScheduleMm3 = null;

                if (extractionSchedule != null && extractionSchedule.TryGetValue(damName, out var dailySchedule) && dailySchedule.Count > 0)
                {
                    hourlyExtractionScheduleMm3 = new List<double>();
                    foreach (var dailyQms in dailySchedule)
                    {
                        double mm3PerHour = dailyQms * 3600.0 / 1e6;
                        for (int hh = 0; hh < 24; hh++)
                            hourlyExtractionScheduleMm3.Add(mm3PerHour);
                    }
                }
                else if (userExtractions != null && userExtractions.TryGetValue(damName, out double userQ))
                {
                    userExtractionMm3 = userQ * 3600.0 / 1e6;
                }

                // Aportaciones manuales por día → expandir a horario (m³/s → Mm³/h)
                List<double>? hourlyAportationMm3 = null;
                if (aportationSchedule != null && aportationSchedule.TryGetValue(damName, out var dailyAport) && dailyAport.Count > 0)
                {
                    hourlyAportationMm3 = new List<double>();
                    foreach (var dailyQms in dailyAport)
                    {
                        double mm3PerHour = dailyQms * 3600.0 / 1e6;
                        for (int hh = 0; hh < 24; hh++)
                            hourlyAportationMm3.Add(mm3PerHour);
                    }
                }

                var sim = SimulateDam(damName, param, curvesForDam, hui, initCond, rain,
                    upstreamExtractions, horizonHours, userExtractionMm3, hourlyExtractionScheduleMm3,
                    hourlyAportationMm3, effectiveDrainCoeff, effectiveCN);
                result.DamSimulations.Add(sim);
            }

            return result;
        }

        /// <summary>
        /// Simula una presa individual: balance hídrico hora a hora.
        /// TC(t) = TC(t-1) + Aportación_cuenca + Aportación_upstream - Extracción
        /// </summary>
        private DamSimulation SimulateDam(
            string damName,
            DamParam param,
            List<ElevCapPoint> curves,
            List<double> hui,
            InitialCondition? init,
            List<HourlyRain> rain,
            List<double>? upstreamExtractions,
            int horizonHours,
            double? userExtractionMm3 = null,
            List<double>? hourlyExtractionScheduleMm3 = null,
            List<double>? hourlyAportationMm3 = null,
            double? overrideDrainCoeff = null,
            double? overrideCN = null)
        {
            var sim = new DamSimulation { DamName = damName, CuencaCode = param.CuencaCode };

            // Condición inicial
            double currentStorageMm3 = init?.AlmacenamientoMm3 ?? GetMidCapacity(curves);
            double currentElevation = init?.Elevacion ?? InterpolateElevation(curves, currentStorageMm3);
            double baseExtractionMm3 = userExtractionMm3 ?? init?.ExtraccionHorariaMm3 ?? param.DrainBase * 3600.0 / 1e6;

            sim.InitialElevation = currentElevation;
            sim.InitialStorageMm3 = currentStorageMm3;

            var startTime = DateTime.UtcNow;
            if (init?.UltimaHora != null)
            {
                startTime = init.FechaBase.AddHours(init.UltimaHora.Value);
            }

            // Aplicar HUI a la lluvia para obtener escurrimiento por hora
            double cn = overrideCN ?? param.CurveNumber;
            double dc = overrideDrainCoeff ?? param.DrainCoefficient;
            var runoffByHour = ApplyHUI(rain, hui, cn, dc, horizonHours);

            for (int h = 0; h < horizonHours; h++)
            {
                var hourTime = startTime.AddHours(h + 1);

                // Aportación por escurrimiento de cuenca propia (HUI convolucionado)
                // Si hay aportación manual, reemplaza la calculada por lluvia
                double basinInputMm3;
                if (hourlyAportationMm3 != null && h < hourlyAportationMm3.Count)
                    basinInputMm3 = hourlyAportationMm3[h];
                else
                    basinInputMm3 = h < runoffByHour.Count ? runoffByHour[h] : 0;

                // Aportación de presa aguas arriba
                double upstreamInputMm3 = 0;
                if (upstreamExtractions != null && h < upstreamExtractions.Count)
                {
                    // El caudal extraído de la presa anterior llega como aportación
                    // con un desfase (transfer_time_hours)
                    int delayH = param.TransferTimeHours;
                    int sourceH = h - delayH;
                    if (sourceH >= 0 && sourceH < upstreamExtractions.Count)
                    {
                        upstreamInputMm3 = upstreamExtractions[sourceH];
                    }
                }

                // Extracción: usar schedule horario si disponible, si no constante
                double extractionMm3;
                if (hourlyExtractionScheduleMm3 != null && h < hourlyExtractionScheduleMm3.Count)
                    extractionMm3 = hourlyExtractionScheduleMm3[h];
                else
                    extractionMm3 = baseExtractionMm3;

                // Balance hídrico
                double newStorage = currentStorageMm3 + basinInputMm3 + upstreamInputMm3 - extractionMm3;

                // Limitar a rango válido de la presa
                double minCap = curves.Count > 0 ? curves[0].CapacityMm3 : 0;
                double maxCap = curves.Count > 0 ? curves[^1].CapacityMm3 : newStorage;
                newStorage = Math.Clamp(newStorage, minCap, maxCap);

                double newElev = InterpolateElevation(curves, newStorage);

                sim.HourlyData.Add(new HourlySimPoint
                {
                    Time = hourTime,
                    Hour = h + 1,
                    StorageMm3 = newStorage,
                    Elevation = newElev,
                    BasinInputMm3 = basinInputMm3,
                    UpstreamInputMm3 = upstreamInputMm3,
                    ExtractionMm3 = extractionMm3,
                    RainMm = h < rain.Count ? rain[h].RainMm : 0
                });

                currentStorageMm3 = newStorage;
                currentElevation = newElev;
            }

            sim.FinalElevation = currentElevation;
            sim.FinalStorageMm3 = currentStorageMm3;
            sim.MaxElevation = sim.HourlyData.Max(d => d.Elevation);
            sim.MinElevation = sim.HourlyData.Min(d => d.Elevation);

            return sim;
        }

        /// <summary>
        /// Aplica HUI (Hidrograma Unitario Instantáneo) a la lluvia efectiva.
        /// PE (SCS-NRCS): PE = (P - 0.2*S)² / (P + 0.8*S), donde S = 25400/CN - 254
        /// Escurrimiento = convolución(PE, HUI) × coeficiente_drenaje
        /// </summary>
        private List<double> ApplyHUI(List<HourlyRain> rain, List<double> hui, double cn, double drainCoeff, int horizonHours)
        {
            var result = new List<double>(new double[horizonHours]);

            // S (retención máxima potencial) en mm
            double s = (25400.0 / cn) - 254.0;
            double ia = 0.2 * s; // Abstracción inicial

            // Calcular lluvia efectiva (PE) por hora usando SCS-NRCS
            var effectiveRain = new List<double>();
            double cumulativeP = 0;
            double cumulativePE = 0;

            for (int i = 0; i < horizonHours; i++)
            {
                double p = i < rain.Count ? rain[i].RainMm : 0;
                cumulativeP += p;

                double pe = 0;
                if (cumulativeP > ia)
                {
                    double totalPE = Math.Pow(cumulativeP - ia, 2) / (cumulativeP + 0.8 * s);
                    pe = totalPE - cumulativePE;
                    cumulativePE = totalPE;
                }
                effectiveRain.Add(pe);
            }

            // Convolución: PE × HUI para obtener escurrimiento
            for (int i = 0; i < effectiveRain.Count; i++)
            {
                for (int j = 0; j < hui.Count; j++)
                {
                    int t = i + j;
                    if (t < horizonHours)
                    {
                        // Escurrimiento en Mill. m³ (PE en mm × área implícita × coef)
                        // Conversión: mm → m³/s → Mill. m³/hora
                        // PE(mm) × Área(km²) × 1000 / 3600 = m³/s
                        // m³/s × 3600 / 1e6 = Mill. m³/hora
                        // Factor simplificado con drainCoeff
                        result[t] += effectiveRain[i] * hui[j] * drainCoeff;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Interpola elevación a partir de almacenamiento usando las curvas elevación-capacidad.
        /// </summary>
        private double InterpolateElevation(List<ElevCapPoint> curves, double storageMm3)
        {
            if (curves.Count == 0) return 0;
            if (storageMm3 <= curves[0].CapacityMm3) return curves[0].Elevation;
            if (storageMm3 >= curves[^1].CapacityMm3) return curves[^1].Elevation;

            for (int i = 1; i < curves.Count; i++)
            {
                if (storageMm3 <= curves[i].CapacityMm3)
                {
                    double ratio = (storageMm3 - curves[i - 1].CapacityMm3) /
                                   (curves[i].CapacityMm3 - curves[i - 1].CapacityMm3);
                    return curves[i - 1].Elevation + ratio * (curves[i].Elevation - curves[i - 1].Elevation);
                }
            }
            return curves[^1].Elevation;
        }

        private double GetMidCapacity(List<ElevCapPoint> curves)
        {
            if (curves.Count == 0) return 0;
            return (curves[0].CapacityMm3 + curves[^1].CapacityMm3) / 2.0;
        }

        #region Data Loading

        private async Task<Dictionary<string, List<ElevCapPoint>>> LoadElevationCurvesAsync(NpgsqlConnection db)
        {
            var rows = await db.QueryAsync<ElevCapPoint>(
                "SELECT dam_name AS DamName, elevation AS Elevation, capacity_mm3 AS CapacityMm3 FROM hydro_model.elevation_capacity ORDER BY dam_name, elevation");
            return rows.GroupBy(r => r.DamName).ToDictionary(g => g.Key, g => g.ToList());
        }

        private async Task<Dictionary<string, DamParam>> LoadDamParamsAsync(NpgsqlConnection db)
        {
            var rows = await db.QueryAsync<DamParam>(@"
                SELECT dam_name AS DamName, cuenca_code AS CuencaCode, model_type AS ModelType,
                       drain_coefficient AS DrainCoefficient, drain_base AS DrainBase,
                       curve_number AS CurveNumber, transfer_time_hours AS TransferTimeHours,
                       has_previous_dam AS HasPreviousDam, previous_dam_name AS PreviousDamName,
                       cascade_order AS CascadeOrder
                FROM hydro_model.dam_params ORDER BY cascade_order");
            return rows.ToDictionary(r => r.DamName);
        }

        private async Task<Dictionary<string, List<double>>> LoadHuiCoefficientsAsync(NpgsqlConnection db)
        {
            var rows = await db.QueryAsync<(string cuenca_code, int hour_index, double coefficient)>(
                "SELECT cuenca_code, hour_index, coefficient FROM hydro_model.hui_coefficients ORDER BY cuenca_code, hour_index");
            return rows.GroupBy(r => r.cuenca_code)
                       .ToDictionary(g => g.Key, g => g.Select(r => r.coefficient).ToList());
        }

        private async Task<Dictionary<string, InitialCondition>> GetInitialConditionsAsync(NpgsqlConnection db)
        {
            var result = new Dictionary<string, InitialCondition>();
            // Mapeo presa FunVasos → dam_name en hydro_model
            var presaMap = new Dictionary<string, string>
            {
                ["Angostura"] = "Angostura",
                ["Chicoasén"] = "Chicoasen",
                ["Malpaso"] = "Malpaso",
                ["Tapón Juan Grijalva"] = "JGrijalva",
                ["Peñitas"] = "Penitas"
            };

            // Obtener último registro por presa de funvasos_horario
            var rows = await db.QueryAsync<dynamic>(@"
                SELECT DISTINCT ON (presa)
                    presa, ts, hora, elevacion, almacenamiento,
                    extracciones_total_q, aportaciones_q
                FROM public.funvasos_horario
                WHERE elevacion IS NOT NULL
                ORDER BY presa, ts DESC, hora DESC");

            foreach (var row in rows)
            {
                string presa = row.presa;
                if (!presaMap.TryGetValue(presa, out var damName)) continue;

                double extractionQ = row.extracciones_total_q != null ? (double)row.extracciones_total_q : 0;
                double extractionMm3 = extractionQ * 3600.0 / 1e6; // m³/s → Mill.m³/hora

                result[damName] = new InitialCondition
                {
                    Elevacion = row.elevacion != null ? (double)row.elevacion : 0,
                    AlmacenamientoMm3 = row.almacenamiento != null ? (double)row.almacenamiento : 0,
                    ExtraccionHorariaMm3 = extractionMm3,
                    AportacionQ = row.aportaciones_q != null ? (double)row.aportaciones_q : 0,
                    UltimaHora = row.hora != null ? (int)(short)row.hora : 0,
                    FechaBase = row.ts
                };
            }

            return result;
        }

        private async Task<Dictionary<string, List<HourlyRain>>> GetRainForecastByCuencaAsync(NpgsqlConnection db, int horizonHours)
        {
            var result = new Dictionary<string, List<HourlyRain>>();
            var now = DateTime.UtcNow;

            // Obtener último forecast disponible
            var latestDate = await db.QueryFirstOrDefaultAsync<DateTime?>(
                "SELECT MAX(forecast_date::timestamp) FROM rain_forecast.forecast");
            if (latestDate == null) return result;

            // Promedios horarios de lluvia por cuenca
            var rows = await db.QueryAsync<HourlyRain>(@"
                SELECT time_bucket('1 hour', ts) AS Time,
                       cuenca_code AS CuencaCode,
                       AVG(rain_mm) AS RainMm
                FROM rain_forecast.rain_record
                WHERE forecast_date = @D
                  AND ts >= @Now AND ts <= @End
                GROUP BY time_bucket('1 hour', ts), cuenca_code
                ORDER BY cuenca_code, Time",
                new { D = latestDate.Value, Now = now, End = now.AddHours(horizonHours) });

            foreach (var group in rows.GroupBy(r => r.CuencaCode))
            {
                result[group.Key] = group.ToList();
            }

            return result;
        }

        private async Task<DateTime?> GetLatestForecastDateAsync(NpgsqlConnection db)
        {
            return await db.QueryFirstOrDefaultAsync<DateTime?>(
                "SELECT MAX(forecast_date::timestamp) FROM rain_forecast.forecast");
        }

        #endregion

        #region DTOs

        public class ElevCapPoint
        {
            public string DamName { get; set; } = "";
            public double Elevation { get; set; }
            public double CapacityMm3 { get; set; }
        }

        public class DamParam
        {
            public string DamName { get; set; } = "";
            public string CuencaCode { get; set; } = "";
            public string ModelType { get; set; } = "daily";
            public double DrainCoefficient { get; set; } = 0.15;
            public double DrainBase { get; set; } = 100;
            public double CurveNumber { get; set; } = 75;
            public int TransferTimeHours { get; set; }
            public bool HasPreviousDam { get; set; }
            public string? PreviousDamName { get; set; }
            public int CascadeOrder { get; set; }
        }

        public class InitialCondition
        {
            public double Elevacion { get; set; }
            public double AlmacenamientoMm3 { get; set; }
            public double ExtraccionHorariaMm3 { get; set; }
            public double AportacionQ { get; set; }
            public int? UltimaHora { get; set; }
            public DateTime FechaBase { get; set; }
        }

        public class HourlyRain
        {
            public DateTime Time { get; set; }
            public string CuencaCode { get; set; } = "";
            public double RainMm { get; set; }
        }

        // === Modelos de resultado ===

        public class HydroSimulationResult
        {
            public DateTime GeneratedAt { get; set; }
            public DateTime? ForecastDate { get; set; }
            public int HorizonHours { get; set; }
            public List<DamSimulation> DamSimulations { get; set; } = new();
        }

        public class DamSimulation
        {
            public string DamName { get; set; } = "";
            public string CuencaCode { get; set; } = "";
            public double InitialElevation { get; set; }
            public double InitialStorageMm3 { get; set; }
            public double FinalElevation { get; set; }
            public double FinalStorageMm3 { get; set; }
            public double MaxElevation { get; set; }
            public double MinElevation { get; set; }
            public List<HourlySimPoint> HourlyData { get; set; } = new();
        }

        public class HourlySimPoint
        {
            public DateTime Time { get; set; }
            public int Hour { get; set; }
            public double StorageMm3 { get; set; }
            public double Elevation { get; set; }
            public double BasinInputMm3 { get; set; }
            public double UpstreamInputMm3 { get; set; }
            public double ExtractionMm3 { get; set; }
            public double RainMm { get; set; }
        }

        #endregion
    }
}
