using System.Data;
using CloudStationWeb.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CloudStationWeb.Controllers
{
    /// <summary>
    /// API REST compatible con grijalva-core-service y grijalva-automatic-station-service (Spring Boot).
    /// Endpoints idénticos para que la migración sea transparente al cliente.
    /// Autenticación: Header X-Api-Key ó JWT Bearer con rol ApiConsumer/SuperAdmin/Administrador.
    ///
    /// === CORE-SERVICE (scg-ws) ===
    /// GET /api/get/station/by/central-id/{centralId}/class/{clazz}/type/{type}
    /// GET /api/get/station/hydro-model/by/sub-basin/{subBasinId}
    /// GET /api/get/dam/by/id/{damId}
    /// GET /api/get/dam/by/central/{centralId}
    /// GET /api/get/sub-basin/by/id/{id}
    /// GET /api/get/central/by/id/{id}
    /// GET /api/get/elevation-capacity/by/central/{centralId}/elevation/{elevation}
    /// GET /api/get/elevation-capacity/by/central/{centralId}/capacity/{capacity}
    /// GET /api/get/station-report/records/by/station/{stationId}/date/{date}/hour/{hour}
    /// GET /api/get/dam-behavior/central-id/{centralId}/date/{date}
    /// GET /api/get/dam-behavior/primary-flow-spending/by/central-id/{centralId}/date/{date}/hour/{hour}
    ///
    /// === AUTOMATIC-STATIONS-CONNECTOR ===
    /// GET /api/get/accumulative-rain/by/id/{stationId}/date/{date}/hour/{hour}
    /// GET /api/get/accumulative-rain/by/assignedId/{assignedId}/vendorId/{vendorId}/date/{date}/hour/{hour}
    ///
    /// === RAIN-FORECAST-SERVICE ===
    /// GET /v1/forecast/last
    /// GET /v1/forecast/date/{date}
    /// GET /v1/record/forecast-date/{date}/sub-basin-id/{subBasinId}/dates/{start}/{end}
    /// </summary>
    [ApiController]
    public class StationsApiController : ControllerBase
    {
        private readonly string _sqlServerConn;
        private readonly string _pgConn;
        private readonly string _apiKey;
        private readonly ILogger<StationsApiController> _logger;

        // Metadatos completos de las 5 centrales (cascada Grijalva)
        private static readonly Dictionary<int, CentralMeta> Centrales = new()
        {
            [1] = new(1, null, 1, 1, "ANG", "K02", "ANG", "C.H. Angostura",
                       5, 900, 4.1, 16.848, -93.535, 1),
            [2] = new(2, 1, 1, 2, "CHI", "K03", "CHI", "C.H. Chicoasén",
                       8, 2400, 3.25, 16.933, -93.148, 2),
            [3] = new(3, 2, 1, 3, "MAL", "K05", "MAL", "C.H. Malpaso",
                       6, 1080, 4.6, 17.163, -93.580, 3),
            [4] = new(4, 3, 1, 4, "JGR", "K18", "JGR", "C.H. Juan Grijalva",
                       0, 0, 0, 17.208, -93.510, 4),
            [5] = new(5, 4, 1, 5, "PEN", "K04", "PEN", "C.H. Peñitas",
                       4, 420, 4.8, 17.369, -93.530, 5)
        };

        // Dam metadata — id matches centralId for cascada Grijalva
        private static readonly Dictionary<int, DamData> Dams = new()
        {
            [1] = new(1, 1, "ANG", "Angostura",     542.10f, 539.00f, 510, 11115f, 6554f, 17669f, 22000f, false, 1.0m, "daily"),
            [2] = new(2, 2, "CHI", "Chicoasén",      400.00f, 395.00f, 378, 1194f,  383f,  1577f,  574f,  true,  1.0m, "hourly"),
            [3] = new(3, 3, "MAL", "Malpaso",         192.00f, 189.70f, 163, 8641f,  4862f, 13503f, 32854f,true,  1.0m, "daily"),
            [4] = new(4, 4, "JGR", "Tapón Juan Grijalva", 105.50f, 100.00f, 87, 0f, 0f, 1666f, 0f, true, 1.0m, "daily"),
            [5] = new(5, 5, "PEN", "Peñitas",         99.20f,  95.10f,  84, 804f,   467f,  1271f,  1868f, true,  1.0m, "daily")
        };

        // SubBasin metadata
        private static readonly Dictionary<int, SubBasinData> SubBasins = new()
        {
            [1] = new(1, 1, "ANG", "Angostura",         0.15m, 0, new[] { 6, 12, 18, 24 }),
            [2] = new(2, 1, "MMT", "Medio Mezcalapa",   0.30m, 2, new[] { 6, 12, 18, 24 }),
            [3] = new(3, 1, "MPS", "Medio-Bajo Grijalva", 0.15m, 4, new[] { 6, 12, 18, 24 }),
            [4] = new(4, 1, "JGR", "Juan Grijalva",     0.10m, 2, new[] { 6, 12, 18, 24 }),
            [5] = new(5, 1, "PEA", "Peñitas",           0.20m, 2, new[] { 6, 12, 18, 24 })
        };

        // Station IDs → centralId mapping (convencionales tipo 'E' embalse)
        private static readonly Dictionary<int, StationMapping> StationsByCentralAndClass = new()
        {
            [1]  = new(1,  "ANG-E-C", "ANG-01", "Angostura Convencional",       'C', 'E', 1, 1, 0.15m, "16.848", "-93.535"),
            [2]  = new(2,  "ANG-E-A", "ANG-02", "Angostura Automática",         'A', 'E', 1, 1, 0.15m, "16.848", "-93.535"),
            [3]  = new(3,  "CHI-E-C", "CHI-01", "Chicoasén Convencional",       'C', 'E', 2, 2, 0.30m, "16.933", "-93.148"),
            [4]  = new(4,  "CHI-E-A", "CHI-02", "Chicoasén Automática",         'A', 'E', 2, 2, 0.30m, "16.933", "-93.148"),
            [5]  = new(5,  "MAL-E-C", "MAL-01", "Malpaso Convencional",         'C', 'E', 3, 3, 0.15m, "17.163", "-93.580"),
            [6]  = new(6,  "MAL-E-A", "MAL-02", "Malpaso Automática",           'A', 'E', 3, 3, 0.15m, "17.163", "-93.580"),
            [7]  = new(7,  "JGR-E-C", "JGR-01", "Juan Grijalva Convencional",   'C', 'E', 4, 4, 0.10m, "17.208", "-93.510"),
            [8]  = new(8,  "JGR-E-A", "JGR-02", "Juan Grijalva Automática",     'A', 'E', 4, 4, 0.10m, "17.208", "-93.510"),
            [9]  = new(9,  "PEN-E-C", "PEN-01", "Peñitas Convencional",         'C', 'E', 5, 5, 0.20m, "17.369", "-93.530"),
            [10] = new(10, "PEN-E-A", "PEN-02", "Peñitas Automática",           'A', 'E', 5, 5, 0.20m, "17.369", "-93.530")
        };

        // Mapeo presa Spring Boot name → nombre en funvasos_horario
        private static readonly Dictionary<int, string> CentralToPresaFunVasos = new()
        {
            [1] = "Angostura",
            [2] = "Chicoasén",
            [3] = "Malpaso",
            [4] = "Tapón Juan Grijalva",
            [5] = "Peñitas"
        };

        public StationsApiController(IConfiguration config, ILogger<StationsApiController> logger)
        {
            _sqlServerConn = config.GetConnectionString("SqlServer") ?? "";
            _pgConn = config.GetConnectionString("PostgreSQL") ?? "";
            _apiKey = config["ImageStore:ApiKey"] ?? "pih-default-key-change-me";
            _logger = logger;
        }

        // =====================================================================
        // CORE-SERVICE: Station Endpoints
        // =====================================================================

        /// <summary>
        /// GET /api/get/station/by/central-id/{centralId}/class/{clazz}/type/{type}
        /// Busca estación por centralId, clase (A=Automática, C=Convencional) y tipo (E=Embalse, H=Hidrométrica).
        /// </summary>
        [HttpGet("api/get/station/by/central-id/{centralId}/class/{clazz}/type/{stationType}")]
        public IActionResult GetStationByCentralClassType(int centralId, char clazz, char stationType)
        {
            if (!ValidateAuth()) return Unauthorized();

            var station = StationsByCentralAndClass.Values
                .FirstOrDefault(s => s.CentralId == centralId && s.Clazz == clazz && s.Type == stationType);

            if (station == null) return NotFound();

            return Ok(MapStation(station));
        }

        /// <summary>
        /// GET /api/get/station/hydro-model/by/sub-basin/{subBasinId}
        /// Retorna todas las estaciones usadas por el modelo hidrológico de una subcuenca.
        /// </summary>
        [HttpGet("api/get/station/hydro-model/by/sub-basin/{subBasinId}")]
        public IActionResult GetHydroModelStations(int subBasinId)
        {
            if (!ValidateAuth()) return Unauthorized();

            var stations = StationsByCentralAndClass.Values
                .Where(s => s.SubBasinId == subBasinId)
                .Select(MapStation)
                .ToList();

            return Ok(stations);
        }

        // =====================================================================
        // CORE-SERVICE: Dam Endpoints
        // =====================================================================

        /// <summary>
        /// GET /api/get/dam/by/id/{damId}
        /// </summary>
        [HttpGet("api/get/dam/by/id/{damId}")]
        public IActionResult GetDamById(int damId)
        {
            if (!ValidateAuth()) return Unauthorized();
            if (!Dams.TryGetValue(damId, out var dam)) return NotFound();
            return Ok(MapDam(dam));
        }

        /// <summary>
        /// GET /api/get/dam/by/central/{centralId}
        /// </summary>
        [HttpGet("api/get/dam/by/central/{centralId}")]
        public IActionResult GetDamByCentral(int centralId)
        {
            if (!ValidateAuth()) return Unauthorized();
            var dam = Dams.Values.FirstOrDefault(d => d.CentralId == centralId);
            if (dam == null) return NotFound();
            return Ok(MapDam(dam));
        }

        // =====================================================================
        // CORE-SERVICE: SubBasin Endpoints
        // =====================================================================

        /// <summary>
        /// GET /api/get/sub-basin/by/id/{id}
        /// Retorna subcuenca con coeficientes HUI y tiempos de transferencia.
        /// </summary>
        [HttpGet("api/get/sub-basin/by/id/{id}")]
        public async Task<IActionResult> GetSubBasinById(int id)
        {
            if (!ValidateAuth()) return Unauthorized();
            if (!SubBasins.TryGetValue(id, out var sb)) return NotFound();

            // Cargar HUI coefficients desde PostgreSQL
            List<decimal> hui = new();
            int? previousDaysNumber = null;
            try
            {
                using var db = new NpgsqlConnection(_pgConn);
                var rows = await db.QueryAsync<decimal>(
                    "SELECT coefficient FROM hydro_model.hui_coefficients WHERE cuenca_code = @Code ORDER BY hour_index",
                    new { Code = sb.Clave });
                hui = rows.ToList();
            }
            catch { /* fallback to empty */ }

            return Ok(new
            {
                id = sb.Id,
                idCuenca = sb.IdCuenca,
                clave = sb.Clave,
                nombre = sb.Nombre,
                inputFactor = sb.InputFactor,
                transferTime = sb.TransferTime,
                hoursRead = sb.HoursRead,
                hui,
                previousDaysNumber
            });
        }

        // =====================================================================
        // CORE-SERVICE: Central Endpoints
        // =====================================================================

        /// <summary>
        /// GET /api/get/central/by/id/{id}
        /// </summary>
        [HttpGet("api/get/central/by/id/{id}")]
        public IActionResult GetCentralById(int id)
        {
            if (!ValidateAuth()) return Unauthorized();
            if (!Centrales.TryGetValue(id, out var c)) return NotFound();

            return Ok(new
            {
                id = c.Id,
                previousCentralId = c.PreviousCentralId,
                idCuenca = c.IdCuenca,
                idSubcuenca = c.IdSubcuenca,
                clave20 = c.Clave20,
                claveCenace = c.ClaveCenace,
                claveSap = c.ClaveSap,
                nombre = c.Nombre,
                unidades = c.Unidades,
                capacidadInstalada = c.CapacidadInstalada,
                consumoEspecifico = c.ConsumoEspecifico,
                latitud = c.Latitud,
                longitud = c.Longitud,
                orden = c.Orden
            });
        }

        // =====================================================================
        // CORE-SERVICE: Elevation-Capacity Endpoints
        // =====================================================================

        /// <summary>
        /// GET /api/get/elevation-capacity/by/central/{centralId}/elevation/{elevation}
        /// Busca capacidad por elevación en curvas elevación-capacidad.
        /// </summary>
        [HttpGet("api/get/elevation-capacity/by/central/{centralId}/elevation/{elevation}")]
        public async Task<IActionResult> GetElevationCapacityByElevation(int centralId, float elevation)
        {
            if (!ValidateAuth()) return Unauthorized();

            var damName = GetDamNameByCentral(centralId);
            if (damName == null) return NotFound();

            using var db = new NpgsqlConnection(_pgConn);
            // Interpolación: buscar los dos puntos más cercanos
            var points = (await db.QueryAsync<ElevCapRow>(
                @"SELECT elevation, capacity_mm3, area_km2, specific_consumption
                  FROM hydro_model.elevation_capacity
                  WHERE dam_name = @Dam ORDER BY elevation",
                new { Dam = damName })).ToList();

            if (points.Count == 0) return NotFound();

            var result = InterpolateByElevation(points, elevation, centralId);
            return Ok(result);
        }

        /// <summary>
        /// GET /api/get/elevation-capacity/by/central/{centralId}/capacity/{capacity}
        /// Busca elevación por capacidad (inverso).
        /// </summary>
        [HttpGet("api/get/elevation-capacity/by/central/{centralId}/capacity/{capacity}")]
        public async Task<IActionResult> GetElevationCapacityByCapacity(int centralId, double capacity)
        {
            if (!ValidateAuth()) return Unauthorized();

            var damName = GetDamNameByCentral(centralId);
            if (damName == null) return NotFound();

            using var db = new NpgsqlConnection(_pgConn);
            var points = (await db.QueryAsync<ElevCapRow>(
                @"SELECT elevation, capacity_mm3, area_km2, specific_consumption
                  FROM hydro_model.elevation_capacity
                  WHERE dam_name = @Dam ORDER BY elevation",
                new { Dam = damName })).ToList();

            if (points.Count == 0) return NotFound();

            var result = InterpolateByCapacity(points, capacity, centralId);
            return Ok(result);
        }

        // =====================================================================
        // CORE-SERVICE: Station Report Endpoints
        // =====================================================================

        /// <summary>
        /// GET /api/get/station-report/records/by/station/{stationId}/date/{dateValue}/hour/{hour}
        /// Retorna registro horario convencional (elevación, generación, gasto, precipitación).
        /// Datos de funvasos_horario.
        /// </summary>
        [HttpGet("api/get/station-report/records/by/station/{stationId}/date/{dateValue}/hour/{hour}")]
        public async Task<IActionResult> GetStationReportRecord(int stationId, string dateValue, int hour)
        {
            if (!ValidateAuth()) return Unauthorized();

            // Map stationId → presa name → funvasos_horario
            var station = StationsByCentralAndClass.GetValueOrDefault(stationId);
            var centralId = station?.CentralId ?? stationId;
            if (!CentralToPresaFunVasos.TryGetValue(centralId, out var presaName))
                return NotFound();

            if (!DateTime.TryParse(dateValue, out var date))
                return BadRequest(new { error = "Invalid date format" });

            using var db = new NpgsqlConnection(_pgConn);
            var row = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT hora, elevacion, almacenamiento, aportaciones_q,
                         extracciones_turb_q, extracciones_total_q, generacion, num_unidades
                  FROM public.funvasos_horario
                  WHERE presa = @Presa AND ts::date = @Date AND hora = @Hour",
                new { Presa = presaName, Date = date, Hour = (short)hour });

            if (row == null) return NotFound();

            return Ok(new
            {
                id = stationId,
                hour = (int)(short)row.hora,
                elevation = row.elevacion != null ? (decimal?)Convert.ToDecimal(row.elevacion) : null,
                scale = row.almacenamiento != null ? (decimal?)Convert.ToDecimal(row.almacenamiento) : null,
                powerGeneration = row.generacion != null ? (decimal?)Convert.ToDecimal(row.generacion) : null,
                spent = row.extracciones_total_q != null ? (decimal?)Convert.ToDecimal(row.extracciones_total_q) : null,
                precipitation = (float?)null,
                unitsWorking = row.num_unidades != null ? (int?)(short)row.num_unidades : null
            });
        }

        // =====================================================================
        // CORE-SERVICE: Dam Behavior Endpoints
        // =====================================================================

        /// <summary>
        /// GET /api/get/dam-behavior/central-id/{centralId}/date/{date}
        /// Comportamiento de presa: todas las horas de un día. Datos de funvasos_horario.
        /// </summary>
        [HttpGet("api/get/dam-behavior/central-id/{centralId}/date/{dateValue}")]
        public async Task<IActionResult> GetDamBehavior(int centralId, string dateValue)
        {
            if (!ValidateAuth()) return Unauthorized();
            if (!CentralToPresaFunVasos.TryGetValue(centralId, out var presaName))
                return NotFound();
            if (!DateTime.TryParse(dateValue, out var date))
                return BadRequest(new { error = "Invalid date format" });

            using var db = new NpgsqlConnection(_pgConn);
            var rows = await db.QueryAsync<dynamic>(
                @"SELECT ts, hora, elevacion, almacenamiento, diferencia,
                         aportaciones_q, aportaciones_v,
                         extracciones_turb_q, extracciones_turb_v,
                         extracciones_vert_q, extracciones_vert_v,
                         extracciones_total_q, extracciones_total_v,
                         generacion, num_unidades, aportacion_promedio
                  FROM public.funvasos_horario
                  WHERE presa = @Presa AND ts::date = @Date
                  ORDER BY hora",
                new { Presa = presaName, Date = date });

            var behaviors = rows.Select(r => new
            {
                dateTime = r.ts != null ? ((DateTime)r.ts).ToString("yyyy-MM-dd'T'HH:mm:ss") : null,
                hour = (int)(short)r.hora,
                elevation = SafeDecimal(r.elevacion),
                utilCapacity = SafeDecimal(r.almacenamiento),
                specificSpend = (decimal?)null,
                diffCapacity = SafeDecimal(r.diferencia),
                inputSpending = SafeDecimal(r.aportaciones_q),
                inputVolume = SafeDecimal(r.aportaciones_v),
                turbineSpending = SafeDecimal(r.extracciones_turb_q),
                turbineVolume = SafeDecimal(r.extracciones_turb_v),
                chuteSpending = SafeDecimal(r.extracciones_vert_q),
                chuteVolume = SafeDecimal(r.extracciones_vert_v),
                totalSpending = SafeDecimal(r.extracciones_total_q),
                totalVolume = SafeDecimal(r.extracciones_total_v),
                generation = SafeDecimal(r.generacion),
                unitsWorking = r.num_unidades != null ? (int?)(short)r.num_unidades : null,
                inputAverage = SafeDecimal(r.aportacion_promedio)
            });

            return Ok(behaviors);
        }

        /// <summary>
        /// GET /api/get/dam-behavior/primary-flow-spending/by/central-id/{centralId}/date/{dateValue}/hour/{hour}
        /// Gasto de flujo primario (extracción total a una hora específica).
        /// </summary>
        [HttpGet("api/get/dam-behavior/primary-flow-spending/by/central-id/{centralId}/date/{dateValue}/hour/{hour}")]
        public async Task<IActionResult> GetPrimaryFlowSpending(int centralId, string dateValue, int hour)
        {
            if (!ValidateAuth()) return Unauthorized();
            if (!CentralToPresaFunVasos.TryGetValue(centralId, out var presaName))
                return NotFound();
            if (!DateTime.TryParse(dateValue, out var date))
                return BadRequest(new { error = "Invalid date format" });

            using var db = new NpgsqlConnection(_pgConn);
            var val = await db.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT extracciones_total_q FROM public.funvasos_horario
                  WHERE presa = @Presa AND ts::date = @Date AND hora = @Hour",
                new { Presa = presaName, Date = date, Hour = (short)hour });

            if (val == null) return NotFound();
            return Ok(val.Value);
        }

        // =====================================================================
        // AUTOMATIC-STATIONS-CONNECTOR: Accumulative Rain
        // =====================================================================

        /// <summary>
        /// GET /api/get/accumulative-rain/by/id/{stationId}/date/{dateValue}/hour/{hour}
        /// Lluvia acumulada de estación automática por ID numérico.
        /// </summary>
        [HttpGet("api/get/accumulative-rain/by/id/{stationId}/date/{dateValue}/hour/{hour}")]
        public async Task<IActionResult> GetAccumRainById(int stationId, string dateValue, int hour)
        {
            if (!ValidateAuth()) return Unauthorized();

            // map stationId to assignedId
            var station = StationsByCentralAndClass.GetValueOrDefault(stationId);
            var assignedId = station?.AssignedId;
            if (string.IsNullOrEmpty(assignedId))
                return NotFound();

            if (!DateTime.TryParse(dateValue, out var date))
                return BadRequest(new { error = "Invalid date format" });

            return await GetAccumRainInternal(assignedId, null, date, hour);
        }

        /// <summary>
        /// GET /api/get/accumulative-rain/by/assignedId/{assignedId}/vendorId/{vendorId}/date/{dateValue}/hour/{hour}
        /// Lluvia acumulada por ID asignado + ID satelital (vendorId = dcp_id GOES).
        /// </summary>
        [HttpGet("api/get/accumulative-rain/by/assignedId/{assignedId}/vendorId/{vendorId}/date/{dateValue}/hour/{hour}")]
        public async Task<IActionResult> GetAccumRainByAssignedAndVendor(
            string assignedId, string vendorId, string dateValue, int hour)
        {
            if (!ValidateAuth()) return Unauthorized();
            if (!DateTime.TryParse(dateValue, out var date))
                return BadRequest(new { error = "Invalid date format" });

            return await GetAccumRainInternal(assignedId, vendorId, date, hour);
        }

        // =====================================================================
        // RAIN-FORECAST-SERVICE: Forecast Endpoints
        // =====================================================================

        /// <summary>
        /// GET /v1/forecast/last
        /// Último pronóstico disponible.
        /// </summary>
        [HttpGet("v1/forecast/last")]
        public async Task<IActionResult> GetLastForecast()
        {
            if (!ValidateAuth()) return Unauthorized();

            using var db = new NpgsqlConnection(_pgConn);
            var row = await db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT forecast_date, updated_at FROM rain_forecast.forecast ORDER BY forecast_date DESC LIMIT 1");

            if (row == null) return Ok(Array.Empty<object>());

            return Ok(new[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    date = ((DateTime)row.forecast_date).ToString("yyyy-MM-dd"),
                    timestamp = row.updated_at != null ? ((DateTime)row.updated_at).ToString("o") : null,
                    lastUpdate = row.updated_at != null ? ((DateTime)row.updated_at).ToString("o") : null
                }
            });
        }

        /// <summary>
        /// GET /v1/forecast/date/{strDate}
        /// Pronóstico por fecha.
        /// </summary>
        [HttpGet("v1/forecast/date/{strDate}")]
        public async Task<IActionResult> GetForecastByDate(string strDate)
        {
            if (!ValidateAuth()) return Unauthorized();
            if (!DateTime.TryParse(strDate, out var date))
                return BadRequest(new { error = "Invalid date" });

            using var db = new NpgsqlConnection(_pgConn);
            var row = await db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT forecast_date, updated_at FROM rain_forecast.forecast WHERE forecast_date::date = @D",
                new { D = date });

            if (row == null) return NotFound();

            return Ok(new
            {
                id = Guid.NewGuid(),
                date = ((DateTime)row.forecast_date).ToString("yyyy-MM-dd"),
                timestamp = row.updated_at != null ? ((DateTime)row.updated_at).ToString("o") : null,
                lastUpdate = row.updated_at != null ? ((DateTime)row.updated_at).ToString("o") : null
            });
        }

        /// <summary>
        /// GET /v1/record/forecast-date/{strDate}/sub-basin-id/{subBasinId}/dates/{startIsoDate}/{endIsoDate}
        /// Registros de lluvia pronosticada para una subcuenca en rango de fechas.
        /// </summary>
        [HttpGet("v1/record/forecast-date/{strDate}/sub-basin-id/{subBasinId}/dates/{startIsoDate}/{endIsoDate}")]
        public async Task<IActionResult> GetRainRecords(string strDate, int subBasinId, string startIsoDate, string endIsoDate)
        {
            if (!ValidateAuth()) return Unauthorized();

            if (!DateTime.TryParse(strDate, out var forecastDate))
                return BadRequest(new { error = "Invalid forecast date" });

            // Map subBasinId → cuenca_code
            var sb = SubBasins.GetValueOrDefault(subBasinId);
            if (sb == null) return NotFound();

            DateTimeOffset startDt, endDt;
            try
            {
                startDt = DateTimeOffset.Parse(startIsoDate);
                endDt = DateTimeOffset.Parse(endIsoDate);
            }
            catch
            {
                return BadRequest(new { error = "Invalid start/end ISO dates" });
            }

            using var db = new NpgsqlConnection(_pgConn);
            var rows = await db.QueryAsync<dynamic>(
                @"SELECT ts, cuenca_code, rain_mm, latitude, longitude
                  FROM rain_forecast.rain_record
                  WHERE forecast_date = @FD
                    AND cuenca_code = @Code
                    AND ts >= @Start AND ts <= @End
                  ORDER BY ts",
                new
                {
                    FD = forecastDate,
                    Code = sb.Clave,
                    Start = startDt.UtcDateTime,
                    End = endDt.UtcDateTime
                });

            var records = rows.Select(r => new
            {
                id = Guid.NewGuid(),
                subBasinId,
                forecastId = Guid.NewGuid(),
                dateTime = ((DateTime)r.ts).ToString("o"),
                latitude = r.latitude != null ? Convert.ToDecimal(r.latitude) : 0m,
                longitude = r.longitude != null ? Convert.ToDecimal(r.longitude) : 0m,
                rain = r.rain_mm != null ? Convert.ToDecimal(r.rain_mm) : 0m
            });

            return Ok(records);
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private async Task<IActionResult> GetAccumRainInternal(string assignedId, string? vendorId, DateTime date, int hour)
        {
            using var db = new NpgsqlConnection(_pgConn);

            // Obtener dcp_id: usar vendorId si disponible, si no buscar por id_asignado
            string? dcpId = vendorId;
            if (string.IsNullOrEmpty(dcpId))
            {
                dcpId = await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT dcp_id FROM public.resumen_horario WHERE id_asignado = @Id LIMIT 1",
                    new { Id = assignedId });
            }
            if (string.IsNullOrEmpty(dcpId)) dcpId = assignedId;

            // Acumular precipitación del día hasta la hora indicada
            var startTs = date.Date;
            var endTs = date.Date.AddHours(hour);

            var accumRain = await db.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT SUM(COALESCE(acumulado, suma, 0))
                  FROM public.resumen_horario
                  WHERE dcp_id = @DcpId
                    AND variable = 'precipitación'
                    AND ts >= @Start AND ts < @End",
                new { DcpId = dcpId, Start = startTs, End = endTs });

            return Ok(new
            {
                dateTime = endTs.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                rain = accumRain ?? 0m
            });
        }

        private bool ValidateAuth()
        {
            if (Request.Headers.TryGetValue("X-Api-Key", out var key) &&
                string.Equals(key, _apiKey, StringComparison.Ordinal))
                return true;

            if (User.Identity?.IsAuthenticated == true &&
                (User.IsInRole("ApiConsumer") || User.IsInRole("SuperAdmin") || User.IsInRole("Administrador")))
                return true;

            return false;
        }

        private static object MapStation(StationMapping s) => new
        {
            id = s.Id,
            vendorId = s.VendorId,
            centralId = s.CentralId,
            code = s.Code,
            assignedId = s.AssignedId,
            name = s.Name,
            clazz = s.Clazz,
            type = s.Type,
            subBasinId = s.SubBasinId,
            weighingInput = s.WeighingInput,
            latitude = s.Latitude,
            longitude = s.Longitude
        };

        private static object MapDam(DamData d) => new
        {
            id = d.Id,
            centralId = d.CentralId,
            code = d.Code,
            description = d.Description,
            nameValue = d.NameValue,
            namoValue = d.NamoValue,
            naminoValue = d.NaminoValue,
            usefulVolume = d.UsefulVolume,
            offVolume = d.OffVolume,
            totalVolume = d.TotalVolume,
            inputArea = d.InputArea,
            hasPreviousDam = d.HasPreviousDam,
            huiFactor = d.HuiFactor,
            modelType = d.ModelType
        };

        private static string? GetDamNameByCentral(int centralId)
        {
            return centralId switch
            {
                1 => "Angostura",
                2 => "Chicoasen",
                3 => "Malpaso",
                4 => "JGrijalva",
                5 => "Penitas",
                _ => null
            };
        }

        private static object InterpolateByElevation(List<ElevCapRow> points, float elevation, int centralId)
        {
            if (elevation <= points[0].Elevation)
                return new { id = 0, centralId, elevation = points[0].Elevation, capacity = points[0].CapacityMm3, capacityArea = points[0].AreaKm2, specificSpend = points[0].SpecificConsumption };
            if (elevation >= points[^1].Elevation)
                return new { id = 0, centralId, elevation = points[^1].Elevation, capacity = points[^1].CapacityMm3, capacityArea = points[^1].AreaKm2, specificSpend = points[^1].SpecificConsumption };

            for (int i = 1; i < points.Count; i++)
            {
                if (elevation <= points[i].Elevation)
                {
                    float ratio = (elevation - points[i - 1].Elevation) / (points[i].Elevation - points[i - 1].Elevation);
                    float cap = points[i - 1].CapacityMm3 + ratio * (points[i].CapacityMm3 - points[i - 1].CapacityMm3);
                    float? area = points[i - 1].AreaKm2.HasValue && points[i].AreaKm2.HasValue
                        ? points[i - 1].AreaKm2.Value + ratio * (points[i].AreaKm2.Value - points[i - 1].AreaKm2.Value)
                        : null;
                    float? spec = points[i - 1].SpecificConsumption.HasValue && points[i].SpecificConsumption.HasValue
                        ? points[i - 1].SpecificConsumption.Value + ratio * (points[i].SpecificConsumption.Value - points[i - 1].SpecificConsumption.Value)
                        : null;
                    return new { id = 0, centralId, elevation, capacity = (decimal)Math.Round(cap, 4), capacityArea = area.HasValue ? (decimal?)Math.Round(area.Value, 4) : null, specificSpend = spec.HasValue ? (decimal?)Math.Round(spec.Value, 4) : null };
                }
            }
            return new { id = 0, centralId, elevation, capacity = 0m, capacityArea = (decimal?)null, specificSpend = (decimal?)null };
        }

        private static object InterpolateByCapacity(List<ElevCapRow> points, double capacity, int centralId)
        {
            if (capacity <= points[0].CapacityMm3)
                return new { id = 0, centralId, elevation = (decimal)points[0].Elevation, capacity = (decimal)points[0].CapacityMm3, capacityArea = points[0].AreaKm2.HasValue ? (decimal?)points[0].AreaKm2 : null, specificSpend = points[0].SpecificConsumption.HasValue ? (decimal?)points[0].SpecificConsumption : null };
            if (capacity >= points[^1].CapacityMm3)
                return new { id = 0, centralId, elevation = (decimal)points[^1].Elevation, capacity = (decimal)points[^1].CapacityMm3, capacityArea = points[^1].AreaKm2.HasValue ? (decimal?)points[^1].AreaKm2 : null, specificSpend = points[^1].SpecificConsumption.HasValue ? (decimal?)points[^1].SpecificConsumption : null };

            for (int i = 1; i < points.Count; i++)
            {
                if (capacity <= points[i].CapacityMm3)
                {
                    float ratio = (float)((capacity - points[i - 1].CapacityMm3) / (points[i].CapacityMm3 - points[i - 1].CapacityMm3));
                    float elev = points[i - 1].Elevation + ratio * (points[i].Elevation - points[i - 1].Elevation);
                    return new { id = 0, centralId, elevation = (decimal)Math.Round(elev, 2), capacity = (decimal)Math.Round(capacity, 4), capacityArea = (decimal?)null, specificSpend = (decimal?)null };
                }
            }
            return new { id = 0, centralId, elevation = 0m, capacity = (decimal)capacity, capacityArea = (decimal?)null, specificSpend = (decimal?)null };
        }

        private static decimal? SafeDecimal(object? val) => val != null ? (decimal?)Convert.ToDecimal(val) : null;

        // =====================================================================
        // HYDRO-MODEL-SERVICE: Concentrated Model Endpoints
        // =====================================================================

        /// <summary>
        /// GET /api/get/request-input/{dateValue}
        /// Genera la estructura de inputs de usuario (horas desde date 00:00 hasta date + 14 días).
        /// </summary>
        [HttpGet("api/get/request-input/{dateValue}")]
        public IActionResult GetRequestInput(string dateValue)
        {
            if (!ValidateAuth()) return Unauthorized();
            if (!DateTime.TryParse(dateValue, out var date))
                return BadRequest(new { error = "Invalid date format" });

            const int FORECAST_DAYS = 14;
            var zeroDate = date.Date;
            var lastDate = zeroDate.AddDays(FORECAST_DAYS);
            var inputs = new List<object>();

            for (var dt = zeroDate; dt < lastDate; dt = dt.AddHours(1))
            {
                inputs.Add(new
                {
                    dateTime = dt.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                    date = dt.ToString("yyyy-MM-dd"),
                    hour = dt.Hour,
                    extraction = (decimal?)null,
                    extractionPreviousDam = (decimal?)null
                });
            }

            return Ok(new { userInput = inputs });
        }

        /// <summary>
        /// POST /api/post/concentrated/
        /// Ejecuta el modelo hidrológico concentrado (diario o horario).
        /// Body: { damId, initialDate, userInput: [{ dateTime, date, hour, extraction, extractionPreviousDam }], drainBase, drainNumber }
        /// </summary>
        [HttpPost("api/post/concentrated/")]
        public async Task<IActionResult> PostConcentratedModel([FromBody] HydroModelRequestDto request)
        {
            if (!ValidateAuth()) return Unauthorized();

            // 1. Resolve Dam
            if (!Dams.TryGetValue(request.DamId, out var dam))
                return StatusCode(502, new { error = $"There is no Dam with id: [{request.DamId}]", code = 502 });

            // 2. Resolve Central
            if (!Centrales.TryGetValue(dam.CentralId, out var central))
                return StatusCode(502, new { error = $"There is no Central with id: [{dam.CentralId}]", code = 502 });

            // 3. Resolve SubBasin
            if (!SubBasins.TryGetValue(central.IdSubcuenca, out var subBasin))
                return StatusCode(502, new { error = $"There is no sub-basin with id: [{central.IdSubcuenca}]", code = 502 });

            // 4. Load HUI coefficients from PostgreSQL
            List<decimal> hui;
            try
            {
                using var db = new NpgsqlConnection(_pgConn);
                hui = (await db.QueryAsync<decimal>(
                    "SELECT coefficient FROM hydro_model.hui_coefficients WHERE cuenca_code = @Code ORDER BY hour_index",
                    new { Code = subBasin.Clave })).ToList();
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = $"Failed to load HUI: {ex.Message}", code = 502 });
            }
            if (hui.Count == 0)
                return StatusCode(502, new { error = $"No HUI coefficients for sub-basin: {subBasin.Clave}", code = 502 });

            // Apply huiFactor if set
            if (dam.HuiFactor != 1.0m)
                hui = hui.Select(h => h * dam.HuiFactor).ToList();

            // 5. Load elevation-capacity curve
            List<ElevCapRow> elevCapCurve;
            try
            {
                var damName = GetDamNameByCentral(dam.CentralId);
                using var db = new NpgsqlConnection(_pgConn);
                elevCapCurve = (await db.QueryAsync<ElevCapRow>(
                    @"SELECT elevation, capacity_mm3, area_km2, specific_consumption
                      FROM hydro_model.elevation_capacity WHERE dam_name = @Dam ORDER BY elevation",
                    new { Dam = damName })).ToList();
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = $"Failed to load elevation-capacity curve: {ex.Message}", code = 502 });
            }

            var zeroDate = request.InitialDate.Date;

            // 6. Determine model type
            if (dam.ModelType == "daily" || dam.ModelType == "DC")
            {
                var result = await RunDailyModel(request, dam, central, subBasin, hui, elevCapCurve, zeroDate);
                return Ok(result);
            }
            else if (dam.ModelType == "hourly" || dam.ModelType == "HC")
            {
                var result = await RunHourlyModel(request, dam, central, subBasin, hui, elevCapCurve, zeroDate);
                return Ok(result);
            }

            return StatusCode(502, new { error = $"Unknown model type: {dam.ModelType}", code = 502 });
        }

        // =====================================================================
        // Daily Concentrated Model
        // =====================================================================

        private async Task<object> RunDailyModel(
            HydroModelRequestDto request, DamData dam, CentralMeta central, SubBasinData subBasin,
            List<decimal> hui, List<ElevCapRow> elevCapCurve, DateTime zeroDate)
        {
            const int NUM_FORECAST_DAYS = 9;
            const float DRAIN_COEFFICIENT = 0.15f;
            const float BASE_SPENT = 100f;

            var previousDays = subBasin.TransferTime; // Use TransferTime as previousDaysNumber
            var startDate = zeroDate.AddDays(-previousDays);
            var endDate = zeroDate.AddDays(NUM_FORECAST_DAYS);

            // Build records for each day in range
            var records = new List<HydroRecord>();
            for (var d = startDate; d <= endDate; d = d.AddDays(1))
                records.Add(new HydroRecord { Date = d, Hour = 0 });

            // Populate real data (days before zeroDate)
            var stations = StationsByCentralAndClass.Values
                .Where(s => s.SubBasinId == subBasin.Id).ToList();

            using var db = new NpgsqlConnection(_pgConn);
            await db.OpenAsync();

            foreach (var rec in records)
            {
                bool isReal = rec.Date < zeroDate;
                bool isZeroDay = rec.Date == zeroDate;

                if (isReal)
                {
                    // Get rain: sum weighted precipitation from stations
                    rec.Rain = await GetDailyWeightedRain(db, stations, rec.Date);
                    // Get elevation from funvasos at hour 6
                    rec.Elevation = await GetElevationFromFunvasos(db, dam.CentralId, rec.Date, 6);
                    // Get total capacity from elevation
                    if (rec.Elevation.HasValue)
                    {
                        var cap = InterpolateCapacityValue(elevCapCurve, (float)rec.Elevation.Value);
                        rec.TotalCapacity = cap + (decimal)dam.OffVolume;
                    }
                    // Get extraction (primary flow spending)
                    rec.Extraction = await GetPrimaryFlow(db, dam.CentralId, rec.Date, 6);
                    rec.ExtractionPreviousDam = central.PreviousCentralId.HasValue
                        ? await GetPrimaryFlow(db, central.PreviousCentralId.Value, rec.Date, 6)
                        : 0m;
                    rec.IsForecast = false;
                }
                else if (isZeroDay)
                {
                    // Get forecast rain average
                    rec.Rain = await GetDailyForecastRainAverage(db, subBasin.Clave, zeroDate, rec.Date);
                    // Get elevation & capacity at zeroDate
                    rec.Elevation = await GetElevationFromFunvasos(db, dam.CentralId, rec.Date, 6);
                    if (rec.Elevation.HasValue)
                    {
                        var cap = InterpolateCapacityValue(elevCapCurve, (float)rec.Elevation.Value);
                        rec.TotalCapacity = cap + (decimal)dam.OffVolume;
                    }
                    rec.IsForecast = true;
                    // Apply user input extraction
                    ApplyUserInput(rec, request.UserInput);
                }
                else
                {
                    // Forecast day — rain from forecast service
                    rec.Rain = await GetDailyForecastRainAverage(db, subBasin.Clave, zeroDate, rec.Date);
                    rec.IsForecast = true;
                    ApplyUserInput(rec, request.UserInput);
                }
            }

            // Apply HUI convolution → effective basin flow
            var peHuiMatrix = new List<(DateTime histDate, DateTime rainDate, decimal peHui)>();
            foreach (var rec in records)
            {
                for (int i = 0; i < hui.Count; i++)
                {
                    var rainDate = rec.Date.AddDays(i);
                    if (rainDate > endDate) break;
                    peHuiMatrix.Add((rec.Date, rainDate, hui[i] * (rec.Rain ?? 0)));
                }
            }

            // Calculate effective flow for each day
            foreach (var rec in records)
            {
                var sum = peHuiMatrix
                    .Where(p => p.rainDate == rec.Date)
                    .Sum(p => p.peHui);
                rec.BasinInput = sum * (decimal)DRAIN_COEFFICIENT + (decimal)BASE_SPENT;
            }

            // Propagate total capacity forward for forecast days
            for (int i = 1; i < records.Count; i++)
            {
                var row = records[i];
                if (!row.IsForecast) continue;
                var prev = records[i - 1];
                if (!prev.TotalCapacity.HasValue) continue;

                var extraction = FromVolumeTo24HCap(prev.Extraction ?? 0);
                var basinInput = FromVolumeTo24HCap(prev.BasinInput ?? 0);
                var prevDam = FromVolumeTo24HCap(prev.ExtractionPreviousDam ?? 0);

                row.TotalCapacity = prev.TotalCapacity.Value + basinInput + prevDam - extraction;
            }

            // Convert total capacity → elevation for forecast days
            foreach (var rec in records.Where(r => r.IsForecast && r.TotalCapacity.HasValue))
            {
                var elev = InterpolateElevationValue(elevCapCurve, (float)(rec.TotalCapacity.Value - (decimal)dam.OffVolume));
                rec.Elevation = (decimal)elev;
            }

            return new
            {
                subBasinId = subBasin.Id,
                modelType = "daily",
                date = zeroDate.ToString("yyyy-MM-dd"),
                dam = MapDam(dam),
                records = records.Select(r => new
                {
                    date = r.Date.ToString("yyyy-MM-dd"),
                    hour = r.Hour,
                    rain = r.Rain,
                    extractionPreviousDam = r.ExtractionPreviousDam,
                    elevation = r.Elevation,
                    extraction = r.Extraction,
                    basinInput = r.BasinInput,
                    totalCapacity = r.TotalCapacity,
                    isForecast = r.IsForecast
                }).OrderBy(r => r.date).ThenBy(r => r.hour)
            };
        }

        // =====================================================================
        // Hourly Concentrated Model
        // =====================================================================

        private async Task<object> RunHourlyModel(
            HydroModelRequestDto request, DamData dam, CentralMeta central, SubBasinData subBasin,
            List<decimal> hui, List<ElevCapRow> elevCapCurve, DateTime zeroDate)
        {
            var now = DateTime.Now.AddHours(-1);
            var previousDays = subBasin.TransferTime;
            var startDate = zeroDate.AddDays(-previousDays);
            var endDate = zeroDate.AddDays(14);

            // Build hourly records
            var records = new List<HydroRecord>();
            for (var dt = startDate; dt < endDate; dt = dt.AddHours(1))
                records.Add(new HydroRecord { Date = dt.Date, Hour = dt.Hour });

            var stations = StationsByCentralAndClass.Values
                .Where(s => s.SubBasinId == subBasin.Id).ToList();

            // Load forecast rain points
            List<RainPointRow> forecastPoints;
            using (var db = new NpgsqlConnection(_pgConn))
            {
                forecastPoints = (await db.QueryAsync<RainPointRow>(
                    @"SELECT ts, rain_mm, latitude, longitude FROM rain_forecast.rain_record
                      WHERE forecast_date = @FD AND cuenca_code = @Code
                        AND ts >= @Start AND ts <= @End
                      ORDER BY ts",
                    new { FD = zeroDate, Code = subBasin.Clave,
                          Start = zeroDate, End = endDate })).ToList();
            }

            using var dbConn = new NpgsqlConnection(_pgConn);
            await dbConn.OpenAsync();

            foreach (var rec in records)
            {
                var dt = rec.Date.AddHours(rec.Hour);
                bool isReal = dt <= now;

                if (isReal)
                {
                    // Accumulative rain average from automatic stations
                    rec.Rain = await GetHourlyRainAverage(dbConn, stations, rec.Date, rec.Hour);

                    // Elevation from funvasos at hour+1
                    rec.Elevation = await GetElevationFromFunvasos(dbConn, dam.CentralId, rec.Date, rec.Hour + 1);

                    // Extraction (primary flow) at hour+1
                    rec.Extraction = await GetPrimaryFlow(dbConn, dam.CentralId, rec.Date, rec.Hour + 1);
                    rec.ExtractionPreviousDam = central.PreviousCentralId.HasValue
                        ? await GetPrimaryFlow(dbConn, central.PreviousCentralId.Value, rec.Date, rec.Hour + 1)
                        : 0m;

                    // Total capacity from elevation
                    if (rec.Elevation.HasValue)
                    {
                        var cap = InterpolateCapacityValue(elevCapCurve, (float)rec.Elevation.Value);
                        rec.TotalCapacity = cap + (decimal)dam.OffVolume;
                    }
                    rec.IsForecast = false;
                }
                else
                {
                    // Forecast: average rain from forecast points at this hour
                    var matchTs = new DateTime(rec.Date.Year, rec.Date.Month, rec.Date.Day, rec.Hour, 0, 0, DateTimeKind.Utc);
                    var points = forecastPoints.Where(p => p.Ts.Year == matchTs.Year &&
                        p.Ts.Month == matchTs.Month && p.Ts.Day == matchTs.Day &&
                        p.Ts.Hour == matchTs.Hour).ToList();
                    if (points.Count > 0)
                        rec.Rain = points.Average(p => p.RainMm);
                    else
                        rec.Rain = 0;

                    rec.IsForecast = true;
                    ApplyUserInput(rec, request.UserInput);
                }
            }

            // PeHUI matrix (hourly version with PE formula)
            var drainNumber = request.DrainNumber ?? 80m;
            var drainBase = request.DrainBase ?? 0m;
            var endDt = new DateTime(zeroDate.Year, zeroDate.Month, zeroDate.Day, 23, 59, 59).AddDays(14);

            var peHuiMatrix = new List<(DateTime histDt, DateTime rainDt, decimal peHui)>();
            foreach (var rec in records)
            {
                var currentDt = rec.Date.AddHours(rec.Hour);
                var rain = rec.Rain ?? 0;
                var pe = PeFormula(drainNumber, rain);
                for (int i = 0; i < hui.Count; i++)
                {
                    var rainDt = currentDt.AddHours(i);
                    if (rainDt > endDt) break;
                    peHuiMatrix.Add((currentDt, rainDt, hui[i] * pe));
                }
            }

            // Total drain per hour
            foreach (var rec in records)
            {
                var currentDt = rec.Date.AddHours(rec.Hour);
                var directDrain = peHuiMatrix
                    .Where(p => p.rainDt.Date == rec.Date && p.rainDt.Hour == rec.Hour)
                    .Sum(p => p.peHui);
                rec.BasinInput = directDrain + drainBase;
            }

            // Propagate total capacity forward
            var sorted = records.OrderBy(r => r.Date).ThenBy(r => r.Hour).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                var row = sorted[i];
                if (!row.IsForecast) continue;
                var prev = sorted[i - 1];
                if (!prev.TotalCapacity.HasValue) continue;

                var extraction = FromVolumeToHourlyCap(prev.Extraction ?? 0);
                var basinInput = FromVolumeToHourlyCap(prev.BasinInput ?? 0);
                var prevDam = FromVolumeToHourlyCap(prev.ExtractionPreviousDam ?? 0);

                row.TotalCapacity = prev.TotalCapacity.Value + basinInput + prevDam - extraction;
            }

            // Elevation from total capacity (forecast only)
            foreach (var rec in sorted.Where(r => r.IsForecast && r.TotalCapacity.HasValue))
            {
                var utilCap = rec.TotalCapacity!.Value - (decimal)dam.OffVolume;
                var elev = InterpolateElevationValue(elevCapCurve, (float)utilCap);
                rec.Elevation = (decimal)elev;
            }

            return new
            {
                subBasinId = subBasin.Id,
                modelType = "hourly",
                date = zeroDate.ToString("yyyy-MM-dd"),
                dam = MapDam(dam),
                records = sorted.Select(r => new
                {
                    date = r.Date.ToString("yyyy-MM-dd"),
                    hour = r.Hour,
                    rain = r.Rain,
                    extractionPreviousDam = r.ExtractionPreviousDam,
                    elevation = r.Elevation,
                    extraction = r.Extraction,
                    basinInput = r.BasinInput,
                    totalCapacity = r.TotalCapacity,
                    isForecast = r.IsForecast
                })
            };
        }

        // =====================================================================
        // Hydro Model Helpers
        // =====================================================================

        private static decimal PeFormula(decimal drainCoefficient, decimal rain)
        {
            if (rain <= 0 || drainCoefficient <= 0) return 0;
            var firstFactor = 508m / drainCoefficient;
            var numerator = rain - firstFactor + 5.08m;
            if (numerator <= 0) return 0;
            numerator = numerator * numerator;
            var denominator = rain + 2032m / drainCoefficient - 20.32m;
            if (denominator <= 0) return 0;
            return numerator / denominator;
        }

        private static decimal FromVolumeTo24HCap(decimal volume)
        {
            // mm3 = 1_000_000, time = 24*3600 = 86400
            return volume * 86400m / 1_000_000m;
        }

        private static decimal FromVolumeToHourlyCap(decimal volume)
        {
            // mm3 = 1_000_000, time = 3600
            return volume * 3600m / 1_000_000m;
        }

        private static decimal InterpolateCapacityValue(List<ElevCapRow> points, float elevation)
        {
            if (points.Count == 0) return 0;
            if (elevation <= points[0].Elevation) return (decimal)points[0].CapacityMm3;
            if (elevation >= points[^1].Elevation) return (decimal)points[^1].CapacityMm3;
            for (int i = 1; i < points.Count; i++)
            {
                if (elevation <= points[i].Elevation)
                {
                    float ratio = (elevation - points[i - 1].Elevation) / (points[i].Elevation - points[i - 1].Elevation);
                    return (decimal)(points[i - 1].CapacityMm3 + ratio * (points[i].CapacityMm3 - points[i - 1].CapacityMm3));
                }
            }
            return 0;
        }

        private static float InterpolateElevationValue(List<ElevCapRow> points, float capacity)
        {
            if (points.Count == 0) return 0;
            if (capacity <= points[0].CapacityMm3) return points[0].Elevation;
            if (capacity >= points[^1].CapacityMm3) return points[^1].Elevation;
            for (int i = 1; i < points.Count; i++)
            {
                if (capacity <= points[i].CapacityMm3)
                {
                    float ratio = (capacity - points[i - 1].CapacityMm3) / (points[i].CapacityMm3 - points[i - 1].CapacityMm3);
                    return points[i - 1].Elevation + ratio * (points[i].Elevation - points[i - 1].Elevation);
                }
            }
            return 0;
        }

        private async Task<decimal> GetDailyWeightedRain(NpgsqlConnection db, List<StationMapping> stations, DateTime date)
        {
            decimal totalRain = 0;
            foreach (var st in stations)
            {
                var precip = await db.QueryFirstOrDefaultAsync<decimal?>(
                    @"SELECT SUM(COALESCE(acumulado, suma, 0))
                      FROM public.resumen_horario
                      WHERE dcp_id = @Dcp AND variable = 'precipitación'
                        AND ts::date = @D AND EXTRACT(HOUR FROM ts) <= 6",
                    new { Dcp = st.VendorId, D = date });
                totalRain += (precip ?? 0) * st.WeighingInput;
            }
            return totalRain;
        }

        private async Task<decimal> GetHourlyRainAverage(NpgsqlConnection db, List<StationMapping> stations, DateTime date, int hour)
        {
            if (stations.Count == 0) return 0;
            decimal sum = 0;
            foreach (var st in stations)
            {
                var endTs = date.AddHours(hour);
                var rain = await db.QueryFirstOrDefaultAsync<decimal?>(
                    @"SELECT SUM(COALESCE(acumulado, suma, 0))
                      FROM public.resumen_horario
                      WHERE dcp_id = @Dcp AND variable = 'precipitación'
                        AND ts >= @St AND ts < @En",
                    new { Dcp = st.VendorId, St = date, En = endTs });
                sum += rain ?? 0;
            }
            return sum / stations.Count;
        }

        private async Task<decimal?> GetElevationFromFunvasos(NpgsqlConnection db, int centralId, DateTime date, int hour)
        {
            if (!CentralToPresaFunVasos.TryGetValue(centralId, out var presa)) return null;
            return await db.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT elevacion FROM public.funvasos_horario
                  WHERE presa = @P AND ts::date = @D AND hora = @H",
                new { P = presa, D = date, H = (short)hour });
        }

        private async Task<decimal> GetPrimaryFlow(NpgsqlConnection db, int centralId, DateTime date, int hour)
        {
            if (!CentralToPresaFunVasos.TryGetValue(centralId, out var presa)) return 0;
            return await db.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT extracciones_total_q FROM public.funvasos_horario
                  WHERE presa = @P AND ts::date = @D AND hora = @H",
                new { P = presa, D = date, H = (short)hour }) ?? 0;
        }

        private async Task<decimal> GetDailyForecastRainAverage(NpgsqlConnection db, string cuencaCode, DateTime forecastDate, DateTime targetDate)
        {
            // Get all forecast rain records for targetDate, average by hour then sum
            var rows = await db.QueryAsync<RainPointRow>(
                @"SELECT ts, rain_mm FROM rain_forecast.rain_record
                  WHERE forecast_date = @FD AND cuenca_code = @Code
                    AND ts::date = @TD
                  ORDER BY ts",
                new { FD = forecastDate, Code = cuencaCode, TD = targetDate });

            var list = rows.ToList();
            if (list.Count == 0) return 0;

            // Group by hour, average each group, sum
            var hourlyAvg = list.GroupBy(r => r.Ts.Hour)
                .Sum(g => g.Average(r => r.RainMm));
            return (decimal)hourlyAvg;
        }

        private static void ApplyUserInput(HydroRecord rec, List<UserInputDto>? userInput)
        {
            if (userInput == null) return;
            var match = userInput.FirstOrDefault(u =>
                u.Date != null && DateTime.TryParse(u.Date, out var ud) && ud.Date == rec.Date);
            if (match == null) return;
            if (match.Extraction.HasValue) rec.Extraction = match.Extraction.Value;
            if (match.ExtractionPreviousDam.HasValue) rec.ExtractionPreviousDam = match.ExtractionPreviousDam.Value;
        }

        // =====================================================================
        // Records / DTOs
        // =====================================================================

        private record CentralMeta(int Id, int? PreviousCentralId, int IdCuenca, int IdSubcuenca,
            string Clave20, string ClaveCenace, string ClaveSap, string Nombre,
            int Unidades, int CapacidadInstalada, double ConsumoEspecifico,
            double Latitud, double Longitud, int Orden);

        private record DamData(int Id, int CentralId, string Code, string Description,
            float NameValue, float NamoValue, int NaminoValue,
            float UsefulVolume, float OffVolume, float TotalVolume, float InputArea,
            bool HasPreviousDam, decimal HuiFactor, string ModelType);

        private record SubBasinData(int Id, int IdCuenca, string Clave, string Nombre,
            decimal InputFactor, int TransferTime, int[] HoursRead);

        private record StationMapping(int Id, string VendorId, string AssignedId, string Name,
            char Clazz, char Type, int CentralId, int SubBasinId, decimal WeighingInput,
            string Latitude, string Longitude)
        {
            public string Code => VendorId;
        };

        private class ElevCapRow
        {
            public float Elevation { get; set; }
            public float CapacityMm3 { get; set; }
            public float? AreaKm2 { get; set; }
            public float? SpecificConsumption { get; set; }
        }

        private class HydroRecord
        {
            public DateTime Date { get; set; }
            public int Hour { get; set; }
            public decimal? Rain { get; set; }
            public decimal? Elevation { get; set; }
            public decimal? TotalCapacity { get; set; }
            public decimal? Extraction { get; set; }
            public decimal? ExtractionPreviousDam { get; set; }
            public decimal? BasinInput { get; set; }
            public bool IsForecast { get; set; }
        }

        private class RainPointRow
        {
            public DateTime Ts { get; set; }
            public decimal RainMm { get; set; }
            public float? Latitude { get; set; }
            public float? Longitude { get; set; }
        }

        public class HydroModelRequestDto
        {
            public int DamId { get; set; }
            public DateTime InitialDate { get; set; }
            public List<UserInputDto>? UserInput { get; set; }
            public decimal? DrainBase { get; set; }
            public decimal? DrainNumber { get; set; }
        }

        public class UserInputDto
        {
            public string? DateTime { get; set; }
            public string? Date { get; set; }
            public int? Hour { get; set; }
            public decimal? Extraction { get; set; }
            public decimal? ExtractionPreviousDam { get; set; }
        }
    }
}
