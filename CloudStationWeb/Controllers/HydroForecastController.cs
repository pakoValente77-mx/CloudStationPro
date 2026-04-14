using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    [Authorize]
    public class HydroForecastController : Controller
    {
        private readonly HydroForecastService _hydroService;
        private readonly FunVasosService _funVasosService;
        private readonly ILogger<HydroForecastController> _logger;

        // NAMO / NAME / NAMINO de referencia
        private static readonly Dictionary<string, (double namo, double name, double namino)> DamRefLevels = new()
        {
            ["Angostura"] = (539.00, 542.10, 510.40),
            ["Chicoasen"] = (395.00, 400.00, 378.50),
            ["Malpaso"] = (189.70, 192.00, 163.00),
            ["JGrijalva"] = (100.00, 105.50, 87.00),
            ["Penitas"] = (95.10, 99.20, 84.50)
        };

        private static readonly Dictionary<string, string> PresaToHydro = new()
        {
            ["Angostura"] = "Angostura",
            ["Chicoasén"] = "Chicoasen",
            ["Malpaso"] = "Malpaso",
            ["Tapón Juan Grijalva"] = "JGrijalva",
            ["Peñitas"] = "Penitas"
        };

        private static readonly Dictionary<string, string> DamDisplayNames = new()
        {
            ["Angostura"] = "Angostura",
            ["Chicoasen"] = "Chicoasén",
            ["Malpaso"] = "Malpaso",
            ["JGrijalva"] = "Tapón Juan Grijalva",
            ["Penitas"] = "Peñitas"
        };

        public HydroForecastController(HydroForecastService hydroService, FunVasosService funVasosService, ILogger<HydroForecastController> logger)
        {
            _hydroService = hydroService;
            _funVasosService = funVasosService;
            _logger = logger;
        }

        /// <summary>
        /// API: /HydroForecast/GetInputData?horizonHours=72
        /// Devuelve los datos de entrada que alimentan la simulación.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetInputData(int horizonHours = 72)
        {
            try
            {
                if (horizonHours < 1) horizonHours = 24;
                if (horizonHours > 360) horizonHours = 360;
                var data = await _hydroService.GetInputDataAsync(horizonHours);
                return Json(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching hydro input data");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// API: /HydroForecast/RunSimulation
        /// Ejecuta la simulación hidrológica con extracciones opcionales editadas.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunSimulation([FromBody] SimulationRequest request)
        {
            try
            {
                int horizonHours = request?.HorizonHours ?? 72;
                if (horizonHours < 1) horizonHours = 24;
                if (horizonHours > 360) horizonHours = 360; // máximo 15 días

                var extractions = request?.Extractions ?? new Dictionary<string, double>();
                var extractionSchedule = request?.ExtractionSchedule;
                var aportationSchedule = request?.AportationSchedule;
                var drainCoefficients = request?.DrainCoefficients;
                var curveNumbers = request?.CurveNumbers;
                var result = await _hydroService.RunSimulationAsync(
                    horizonHours, extractions, extractionSchedule,
                    aportationSchedule, drainCoefficients, curveNumbers);

                // Formatear para el frontend
                var response = new
                {
                    success = true,
                    generatedAt = result.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    forecastDate = result.ForecastDate?.ToString("yyyy-MM-dd"),
                    horizonHours = result.HorizonHours,
                    dams = result.DamSimulations.Select(d => new
                    {
                        damName = d.DamName,
                        cuencaCode = d.CuencaCode,
                        initialElev = Math.Round(d.InitialElevation, 2),
                        finalElev = Math.Round(d.FinalElevation, 2),
                        maxElev = Math.Round(d.MaxElevation, 2),
                        minElev = Math.Round(d.MinElevation, 2),
                        initialStorage = Math.Round(d.InitialStorageMm3, 2),
                        finalStorage = Math.Round(d.FinalStorageMm3, 2),
                        deltaElev = Math.Round(d.FinalElevation - d.InitialElevation, 2),
                        deltaStorage = Math.Round(d.FinalStorageMm3 - d.InitialStorageMm3, 2),
                        hourly = d.HourlyData.Select(h => new
                        {
                            time = h.Time.ToString("yyyy-MM-dd HH:mm"),
                            hour = h.Hour,
                            elev = Math.Round(h.Elevation, 2),
                            storage = Math.Round(h.StorageMm3, 2),
                            basinInput = Math.Round(h.BasinInputMm3, 4),
                            upstreamInput = Math.Round(h.UpstreamInputMm3, 4),
                            extraction = Math.Round(h.ExtractionMm3, 4),
                            rain = Math.Round(h.RainMm, 2)
                        })
                    })
                };

                return Json(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running hydro simulation");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// API: /HydroForecast/GetTrendData?realDays=5&forecastHours=72
        /// Datos reales + pronóstico combinados para gráficas de tendencia.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTrendData(int realDays = 5, int forecastHours = 72)
        {
            try
            {
                realDays = Math.Clamp(realDays, 1, 30);
                forecastHours = Math.Clamp(forecastHours, 1, 360);

                // 1. Datos reales de FunVasos
                var endDate = DateTime.UtcNow.Date.AddDays(1);
                var startDate = endDate.AddDays(-realDays);
                var realData = await _funVasosService.GetDataAsync(startDate, endDate);

                // 2. Pronóstico
                var forecast = await _hydroService.RunSimulationAsync(forecastHours);

                // 3. Combinar por presa
                var dams = new List<object>();
                foreach (var sim in forecast.DamSimulations)
                {
                    var levels = DamRefLevels.GetValueOrDefault(sim.DamName);
                    var displayName = DamDisplayNames.GetValueOrDefault(sim.DamName, sim.DamName);

                    var realPresa = realData.Presas.FirstOrDefault(p =>
                        PresaToHydro.GetValueOrDefault(p.Presa) == sim.DamName);

                    // Series reales
                    var realSeries = new List<object>();
                    if (realPresa != null)
                    {
                        foreach (var d in realPresa.Datos)
                        {
                            realSeries.Add(new
                            {
                                time = d.Ts.AddHours(d.Hora).ToString("yyyy-MM-ddTHH:mm"),
                                elevation = d.Elevacion.HasValue ? Math.Round(d.Elevacion.Value, 2) : (double?)null,
                                storageMm3 = d.Almacenamiento.HasValue ? Math.Round(d.Almacenamiento.Value, 2) : (double?)null,
                                aportacionQ = d.AportacionesQ.HasValue ? Math.Round(d.AportacionesQ.Value, 2) : (double?)null,
                                extractionQ = d.ExtraccionesTotalQ.HasValue ? Math.Round(d.ExtraccionesTotalQ.Value, 2) : (double?)null,
                                isForecast = false
                            });
                        }
                    }

                    // Series pronóstico
                    var forecastSeries = sim.HourlyData.Select(h => new
                    {
                        time = h.Time.ToString("yyyy-MM-ddTHH:mm"),
                        elevation = Math.Round(h.Elevation, 2),
                        storageMm3 = Math.Round(h.StorageMm3, 2),
                        aportacionQ = Math.Round((h.BasinInputMm3 + h.UpstreamInputMm3) * 1e6 / 3600.0, 2),
                        extractionQ = Math.Round(h.ExtractionMm3 * 1e6 / 3600.0, 2),
                        isForecast = true
                    });

                    dams.Add(new
                    {
                        damName = sim.DamName,
                        displayName,
                        namo = levels.namo,
                        name_e = levels.name,
                        namino = levels.namino,
                        initialElev = Math.Round(sim.InitialElevation, 2),
                        finalElev = Math.Round(sim.FinalElevation, 2),
                        deltaElev = Math.Round(sim.FinalElevation - sim.InitialElevation, 2),
                        series = realSeries.Concat<object>(forecastSeries).ToList()
                    });
                }

                return Json(new
                {
                    success = true,
                    generatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm"),
                    realDays,
                    forecastHours,
                    forecastDate = forecast.ForecastDate?.ToString("yyyy-MM-dd"),
                    dams
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching trend data");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    public class SimulationRequest
    {
        public int HorizonHours { get; set; } = 72;
        /// <summary>Extracción constante por presa (m³/s). Compatibilidad hacia atrás.</summary>
        public Dictionary<string, double>? Extractions { get; set; }
        /// <summary>Extracción variable por presa y día: { "Angostura": [120.5, 130.0, ...], ... } (m³/s por día)</summary>
        public Dictionary<string, List<double>>? ExtractionSchedule { get; set; }
        /// <summary>Aportación variable por presa y día (m³/s por día). Reemplaza la aportación calculada por lluvia.</summary>
        public Dictionary<string, List<double>>? AportationSchedule { get; set; }
        /// <summary>Coeficientes de escurrimiento editados por el usuario (por presa).</summary>
        public Dictionary<string, double>? DrainCoefficients { get; set; }
        /// <summary>Curva Number editados por el usuario (por presa).</summary>
        public Dictionary<string, double>? CurveNumbers { get; set; }
    }
}
