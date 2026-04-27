using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CloudStationWeb.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
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
        private readonly string _jwtKey;
        private readonly string _jwtIssuer;
        private readonly string _jwtAudience;
        private readonly ILogger<StationsApiController> _logger;

        public StationsApiController(IConfiguration config, ILogger<StationsApiController> logger)
        {
            _sqlServerConn = config.GetConnectionString("SqlServer") ?? "";
            _pgConn = config.GetConnectionString("PostgreSQL") ?? "";
            _apiKey = config["ImageStore:ApiKey"] ?? "pih-default-key-change-me";
            _jwtKey = config["Jwt:Key"] ?? "";
            _jwtIssuer = config["Jwt:Issuer"] ?? "CloudStationWeb";
            _jwtAudience = config["Jwt:Audience"] ?? "CloudStationAPI";
            _logger = logger;
        }

        // =====================================================================
        // DB Loaders — Centrales, Dams, SubBasins from PostgreSQL
        // =====================================================================

        private async Task<Dictionary<int, CentralMeta>> LoadCentralesAsync()
        {
            using var db = new NpgsqlConnection(_pgConn);
            var rows = await db.QueryAsync<dynamic>(
                @"SELECT id, previous_central_id, id_cuenca, id_subcuenca,
                         clave20, clave_cenace, clave_sap, nombre,
                         unidades, capacidad_instalada, consumo_especifico,
                         latitud, longitud, orden
                  FROM hydro_model.central_params ORDER BY orden");
            return rows.ToDictionary(
                r => (int)r.id,
                r => new CentralMeta(
                    (int)r.id, r.previous_central_id != null ? (int?)r.previous_central_id : null,
                    (int)r.id_cuenca, (int)r.id_subcuenca,
                    (string)r.clave20, (string)r.clave_cenace, (string)r.clave_sap, (string)r.nombre,
                    (int)r.unidades, (int)r.capacidad_instalada, (double)r.consumo_especifico,
                    (double)r.latitud, (double)r.longitud, (int)r.orden));
        }

        private async Task<Dictionary<int, DamData>> LoadDamsAsync()
        {
            using var db = new NpgsqlConnection(_pgConn);
            var rows = await db.QueryAsync<dynamic>(
                @"SELECT cascade_order, code, description,
                         name_value, namo_value, namino_value,
                         useful_volume, off_volume, total_volume, input_area,
                         has_previous_dam, hui_factor, model_type
                  FROM hydro_model.dam_params ORDER BY cascade_order");
            return rows.ToDictionary(
                r => (int)r.cascade_order,
                r => new DamData(
                    (int)r.cascade_order, (int)r.cascade_order,
                    (string)(r.code ?? ""), (string)(r.description ?? ""),
                    (float)(r.name_value ?? 0f), (float)(r.namo_value ?? 0f), (int)(r.namino_value ?? 0),
                    (float)(r.useful_volume ?? 0f), (float)(r.off_volume ?? 0f),
                    (float)(r.total_volume ?? 0f), (float)(r.input_area ?? 0f),
                    (bool)(r.has_previous_dam ?? false),
                    r.hui_factor != null ? (decimal)(float)r.hui_factor : 1.0m,
                    (string)(r.model_type ?? "daily")));
        }

        private async Task<Dictionary<int, SubBasinData>> LoadSubBasinsAsync()
        {
            using var db = new NpgsqlConnection(_pgConn);
            var rows = await db.QueryAsync<dynamic>(
                @"SELECT cascade_order, sub_basin_code, sub_basin_name,
                         input_factor, transfer_time_hours, hours_read
                  FROM hydro_model.dam_params ORDER BY cascade_order");
            return rows.ToDictionary(
                r => (int)r.cascade_order,
                r => new SubBasinData(
                    (int)r.cascade_order, 1,
                    (string)(r.sub_basin_code ?? ""),
                    (string)(r.sub_basin_name ?? ""),
                    r.input_factor != null ? (decimal)(float)r.input_factor : 0m,
                    (int)(r.transfer_time_hours ?? 0),
                    r.hours_read is int[] arr ? arr : new[] { 6, 12, 18, 24 }));
        }

        private async Task<string?> GetPresaNameByCentralAsync(int centralId)
        {
            using var db = new NpgsqlConnection(_pgConn);
            return await db.QueryFirstOrDefaultAsync<string>(
                "SELECT description FROM hydro_model.dam_params WHERE cascade_order = @Id",
                new { Id = centralId });
        }

        private async Task<string?> GetDamNameByCentralAsync(int centralId)
        {
            using var db = new NpgsqlConnection(_pgConn);
            return await db.QueryFirstOrDefaultAsync<string>(
                "SELECT dam_name FROM hydro_model.dam_params WHERE cascade_order = @Id",
                new { Id = centralId });
        }

        // =====================================================================
        // CORE-SERVICE: Station Endpoints
        // =====================================================================

        /// <summary>GET /api/get/station/all — Todas las estaciones activas (de la base de datos).</summary>
        [HttpGet("api/get/station/all")]
        public async Task<IActionResult> GetAllStations()
        {
            if (!ValidateAuth()) return Unauthorized();
            var stations = await GetDbStationsAsync();
            return Ok(stations);
        }

        /// <summary>GET /api/get/station/conventional/all — Estaciones convencionales cargadas desde Excel BHG.</summary>
        [HttpGet("api/get/station/conventional/all")]
        public async Task<IActionResult> GetConventionalStations()
        {
            if (!ValidateAuth()) return Unauthorized();
            var stations = await GetBhgConventionalStationsAsync();
            return Ok(stations);
        }

        /// <summary>GET /api/get/station/automatic/all — Estaciones con telemetría GOES (automáticas).</summary>
        [HttpGet("api/get/station/automatic/all")]
        public async Task<IActionResult> GetAutomaticStations()
        {
            if (!ValidateAuth()) return Unauthorized();
            var stations = await GetDbStationsAsync(goesFilter: true);
            return Ok(stations);
        }

        /// <summary>GET /api/get/station/by/id/{stationId} — Estación por IdAsignado.</summary>
        [HttpGet("api/get/station/by/id/{stationId}")]
        public async Task<IActionResult> GetStationById(string stationId)
        {
            if (!ValidateAuth()) return Unauthorized();
            using var db = new SqlConnection(_sqlServerConn);
            var row = await db.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT e.Id AS DatabaseId, e.IdAsignado, e.Nombre, e.Latitud, e.Longitud,
                       e.IdCuenca, e.IdSubcuenca, e.Etiqueta, e.EsPresa,
                       e.GOES, e.GPRS, e.RADIO,
                       g.IdSatelital, o.Nombre AS Organismo
                FROM Estacion e
                LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                WHERE e.Visible = 1 AND e.Activo = 1 AND e.IdAsignado = @Id",
                new { Id = stationId });
            if (row == null) return NotFound();
            return Ok(MapDbStation(row));
        }

        /// <summary>
        /// GET /api/get/station/by/central-id/{centralId}/class/{clazz}/type/{type}
        /// Busca estación por centralId, clase (A=Automática, C=Convencional) y tipo (E=Embalse, H=Hidrométrica).
        /// </summary>
        [HttpGet("api/get/station/by/central-id/{centralId}/class/{clazz}/type/{stationType}")]
        public async Task<IActionResult> GetStationByCentralClassType(int centralId, char clazz, char stationType)
        {
            if (!ValidateAuth()) return Unauthorized();

            // Buscar estaciones de presa por centralId en SQL Server
            using var db = new SqlConnection(_sqlServerConn);
            var goesFilter = clazz == 'A'; // A=Automática (GOES), C=Convencional
            var rows = await db.QueryAsync<dynamic>(@"
                SELECT e.Id AS DatabaseId, e.IdAsignado, e.Nombre, e.Latitud, e.Longitud,
                       e.IdCuenca, e.IdSubcuenca, e.Etiqueta, e.EsPresa,
                       e.GOES, e.GPRS, e.RADIO,
                       g.IdSatelital, o.Nombre AS Organismo
                FROM Estacion e
                LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                WHERE e.Visible = 1 AND e.Activo = 1 AND e.EsPresa = 1
                      AND e.IdSubcuenca = @SubId
                      AND (@Goes IS NULL OR e.GOES = @Goes)",
                new { SubId = centralId, Goes = goesFilter ? (bool?)true : false });
            var station = rows.FirstOrDefault();
            if (station == null) return NotFound();
            return Ok(MapDbStation(station));
        }

        /// <summary>
        /// GET /api/get/station/hydro-model/by/sub-basin/{subBasinId}
        /// Retorna todas las estaciones usadas por el modelo hidrológico de una subcuenca.
        /// </summary>
        [HttpGet("api/get/station/hydro-model/by/sub-basin/{subBasinId}")]
        public async Task<IActionResult> GetHydroModelStations(int subBasinId)
        {
            if (!ValidateAuth()) return Unauthorized();

            using var db = new SqlConnection(_sqlServerConn);
            var rows = await db.QueryAsync<dynamic>(@"
                SELECT e.Id AS DatabaseId, e.IdAsignado, e.Nombre, e.Latitud, e.Longitud,
                       e.IdCuenca, e.IdSubcuenca, e.Etiqueta, e.EsPresa,
                       e.GOES, e.GPRS, e.RADIO,
                       g.IdSatelital, o.Nombre AS Organismo
                FROM Estacion e
                LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                WHERE e.Visible = 1 AND e.Activo = 1 AND e.IdSubcuenca = @SubId",
                new { SubId = subBasinId });
            return Ok(rows.Select(r => MapDbStation(r)).ToList());
        }

        // =====================================================================
        // CORE-SERVICE: Dam Endpoints
        // =====================================================================

        /// <summary>GET /api/get/dam/all — Todas las presas.</summary>
        [HttpGet("api/get/dam/all")]
        public async Task<IActionResult> GetAllDams()
        {
            if (!ValidateAuth()) return Unauthorized();
            var dams = await LoadDamsAsync();
            return Ok(dams.Values.Select(MapDam));
        }

        /// <summary>
        /// GET /api/get/dam/by/id/{damId}
        /// </summary>
        [HttpGet("api/get/dam/by/id/{damId}")]
        public async Task<IActionResult> GetDamById(int damId)
        {
            if (!ValidateAuth()) return Unauthorized();
            var dams = await LoadDamsAsync();
            if (!dams.TryGetValue(damId, out var dam)) return NotFound();
            return Ok(MapDam(dam));
        }

        /// <summary>
        /// GET /api/get/dam/by/central/{centralId}
        /// </summary>
        [HttpGet("api/get/dam/by/central/{centralId}")]
        public async Task<IActionResult> GetDamByCentral(int centralId)
        {
            if (!ValidateAuth()) return Unauthorized();
            var dams = await LoadDamsAsync();
            var dam = dams.Values.FirstOrDefault(d => d.CentralId == centralId);
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
            var subBasins = await LoadSubBasinsAsync();
            if (!subBasins.TryGetValue(id, out var sb)) return NotFound();

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
        public async Task<IActionResult> GetCentralById(int id)
        {
            if (!ValidateAuth()) return Unauthorized();
            var centrales = await LoadCentralesAsync();
            if (!centrales.TryGetValue(id, out var c)) return NotFound();

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

            var damName = await GetDamNameByCentralAsync(centralId);
            if (damName == null) return NotFound();

            using var db = new NpgsqlConnection(_pgConn);
            // Interpolación: buscar los dos puntos más cercanos
            var points = (await db.QueryAsync<ElevCapRow>(
                @"SELECT elevation AS Elevation, capacity_mm3 AS CapacityMm3, area_km2 AS AreaKm2, specific_consumption AS SpecificConsumption
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

            var damName = await GetDamNameByCentralAsync(centralId);
            if (damName == null) return NotFound();

            using var db = new NpgsqlConnection(_pgConn);
            var points = (await db.QueryAsync<ElevCapRow>(
                @"SELECT elevation AS Elevation, capacity_mm3 AS CapacityMm3, area_km2 AS AreaKm2, specific_consumption AS SpecificConsumption
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
        /// GET /api/get/station-report/records/by/station-id/{stationId}/date/{dateValue}
        /// Retorna todos los registros horarios de un día para una estación (funvasos_horario).
        /// stationId puede ser un centralId (1-5) para presas del modelo hidrológico.
        /// </summary>
        [HttpGet("api/get/station-report/records/by/station-id/{stationId}/date/{dateValue}")]
        public async Task<IActionResult> GetStationReportAllHours(string stationId, string dateValue)
        {
            if (!ValidateAuth()) return Unauthorized();

            // Try to map stationId as integer centralId for hydro-model presas
            int centralId = 0;
            if (int.TryParse(stationId, out var numId))
                centralId = numId;

            var presaName = await GetPresaNameByCentralAsync(centralId);
            if (presaName == null)
                return NotFound(new { error = "Solo se soportan estaciones de presas (centralId 1-5) para este endpoint." });

            if (!DateTime.TryParse(dateValue, out var date))
                return BadRequest(new { error = "Formato de fecha inválido. Use yyyy-MM-dd" });

            using var db = new NpgsqlConnection(_pgConn);
            var rows = await db.QueryAsync<dynamic>(
                @"SELECT hora, elevacion, almacenamiento, aportaciones_q,
                         extracciones_turb_q, extracciones_total_q, generacion, num_unidades
                  FROM public.funvasos_horario
                  WHERE presa = @Presa AND ts::date = @Date
                  ORDER BY hora",
                new { Presa = presaName, Date = date });

            var records = rows.Select(r => new
            {
                id = stationId,
                hour = (int)(short)r.hora,
                elevation = SafeDecimal(r.elevacion),
                scale = SafeDecimal(r.almacenamiento),
                powerGeneration = SafeDecimal(r.generacion),
                spent = SafeDecimal(r.extracciones_total_q),
                turbineSpent = SafeDecimal(r.extracciones_turb_q),
                input = SafeDecimal(r.aportaciones_q),
                unitsWorking = r.num_unidades != null ? (int?)(short)r.num_unidades : null
            });

            return Ok(records);
        }

        /// <summary>
        /// GET /api/get/station-report/records/by/station/{stationId}/date/{dateValue}/hour/{hour}
        /// Retorna registro horario convencional (elevación, generación, gasto, precipitación).
        /// Datos de funvasos_horario.
        /// </summary>
        [HttpGet("api/get/station-report/records/by/station/{stationId}/date/{dateValue}/hour/{hour}")]
        public async Task<IActionResult> GetStationReportRecord(int stationId, string dateValue, int hour)
        {
            if (!ValidateAuth()) return Unauthorized();

            var presaName = await GetPresaNameByCentralAsync(stationId);
            if (presaName == null) return NotFound();

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
            var presaName = await GetPresaNameByCentralAsync(centralId);
            if (presaName == null) return NotFound();
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
        /// GET /api/get/dam-behavior/date/{dateValue}/central-id/{centralId}
        /// Ruta alternativa de comportamiento de presa (parámetros en orden date, central-id).
        /// </summary>
        [HttpGet("api/get/dam-behavior/date/{dateValue}/central-id/{centralId}")]
        public async Task<IActionResult> GetDamBehaviorAlt(string dateValue, int centralId)
        {
            return await GetDamBehavior(centralId, dateValue);
        }

        /// <summary>
        /// GET /api/get/dam-behavior/primary-flow-spending/by/central-id/{centralId}/date/{dateValue}/hour/{hour}
        /// Gasto de flujo primario (extracción total a una hora específica).
        /// </summary>
        [HttpGet("api/get/dam-behavior/primary-flow-spending/by/central-id/{centralId}/date/{dateValue}/hour/{hour}")]
        public async Task<IActionResult> GetPrimaryFlowSpending(int centralId, string dateValue, int hour)
        {
            if (!ValidateAuth()) return Unauthorized();
            var presaName = await GetPresaNameByCentralAsync(centralId);
            if (presaName == null) return NotFound();
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
        // AUTOMATIC-STATIONS-CONNECTOR: Sensor Endpoints
        // =====================================================================

        /// <summary>
        /// GET /automatic-station/api/get/sensor/by/station-id/{stationId}
        /// Retorna los sensores (variables) disponibles para una estación automática.
        /// stationId = IdAsignado de la estación.
        /// </summary>
        [HttpGet("automatic-station/api/get/sensor/by/station-id/{stationId}")]
        public async Task<IActionResult> GetSensorsByStationId(string stationId)
        {
            if (!ValidateAuth()) return Unauthorized();

            // Resolve station from SQL Server to get DCP ID
            using var sqlDb = new SqlConnection(_sqlServerConn);
            var stationRow = await sqlDb.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT e.IdAsignado, e.Nombre, g.IdSatelital
                FROM Estacion e
                LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                WHERE e.Visible = 1 AND e.Activo = 1 AND e.IdAsignado = @Id",
                new { Id = stationId });
            if (stationRow == null) return NotFound();

            string assignedId = stationRow.IdAsignado ?? stationId;
            string? dcpId = stationRow.IdSatelital;
            string stationName = stationRow.Nombre ?? stationId;

            using var db = new NpgsqlConnection(_pgConn);
            // Try by id_asignado first, then by dcp_id
            var variables = (await db.QueryAsync<(string variable, int cnt)>(
                @"SELECT variable, COUNT(*)::int as cnt
                  FROM public.resumen_horario
                  WHERE id_asignado = @Id OR dcp_id = @Dcp
                  GROUP BY variable ORDER BY variable",
                new { Id = assignedId, Dcp = dcpId ?? assignedId })).ToList();

            int sensorNumber = 1;
            var sensors = variables.Select(v => new
            {
                sensorNumber = sensorNumber++,
                variable = v.variable,
                assignedId,
                dcpId,
                stationId,
                stationName,
                totalRecords = v.cnt
            });

            return Ok(sensors);
        }

        /// <summary>
        /// GET /automatic-station/api/get/sensor-value/by/assigned-id/{assignedId}/sensor-number/{sensorNumber}/date/{dateValue}/hour/{hour}
        /// Retorna el valor de un sensor específico para una estación, fecha y hora.
        /// sensorNumber: 1=precipitación, 2=nivel/elevación, 3=temperatura, etc. según orden alfabético.
        /// </summary>
        [HttpGet("automatic-station/api/get/sensor-value/by/assigned-id/{assignedId}/sensor-number/{sensorNumber}/date/{dateValue}/hour/{hour}")]
        public async Task<IActionResult> GetSensorValue(string assignedId, int sensorNumber, string dateValue, int hour)
        {
            if (!ValidateAuth()) return Unauthorized();
            if (!DateTime.TryParse(dateValue, out var date))
                return BadRequest(new { error = "Formato de fecha inválido. Use yyyy-MM-dd" });

            using var db = new NpgsqlConnection(_pgConn);

            // Obtener la variable que corresponde al sensorNumber (ordenado alfabéticamente)
            var variables = (await db.QueryAsync<string>(
                @"SELECT DISTINCT variable FROM public.resumen_horario
                  WHERE id_asignado = @Id ORDER BY variable",
                new { Id = assignedId })).ToList();

            if (sensorNumber < 1 || sensorNumber > variables.Count)
                return NotFound(new { error = $"Sensor #{sensorNumber} no existe. Sensores disponibles: 1-{variables.Count}" });

            var variable = variables[sensorNumber - 1];
            var ts = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, TimeSpan.Zero);

            var row = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT ts, variable, acumulado, suma, maximo, minimo, promedio
                  FROM public.resumen_horario
                  WHERE id_asignado = @Id AND variable = @Var AND ts = @Ts",
                new { Id = assignedId, Var = variable, Ts = ts });

            if (row == null) return NotFound();

            return Ok(new
            {
                assignedId,
                sensorNumber,
                variable,
                dateTime = ((DateTimeOffset)row.ts).ToString("yyyy-MM-dd'T'HH:mm:ss"),
                value = SafeDecimal(row.acumulado) ?? SafeDecimal(row.suma) ?? SafeDecimal(row.promedio),
                accumulated = SafeDecimal(row.acumulado),
                sum = SafeDecimal(row.suma),
                max = SafeDecimal(row.maximo),
                min = SafeDecimal(row.minimo),
                average = SafeDecimal(row.promedio)
            });
        }

        // =====================================================================
        // AUTOMATIC-STATIONS-CONNECTOR: Accumulative Rain
        // =====================================================================

        /// <summary>
        /// GET /api/get/accumulative-rain/by/id/{stationId}/date/{dateValue}/hour/{hour}
        /// Lluvia acumulada de estación automática por IdAsignado.
        /// </summary>
        [HttpGet("api/get/accumulative-rain/by/id/{stationId}/date/{dateValue}/hour/{hour}")]
        public async Task<IActionResult> GetAccumRainById(string stationId, string dateValue, int hour)
        {
            if (!ValidateAuth()) return Unauthorized();

            if (!DateTime.TryParse(dateValue, out var date))
                return BadRequest(new { error = "Invalid date format" });

            // stationId is IdAsignado — resolve DCP ID from SQL Server if needed
            using var sqlDb = new SqlConnection(_sqlServerConn);
            var dcpId = await sqlDb.QueryFirstOrDefaultAsync<string>(
                "SELECT g.IdSatelital FROM Estacion e LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id WHERE e.IdAsignado = @Id",
                new { Id = stationId });

            return await GetAccumRainInternal(stationId, dcpId, date, hour);
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
                "SELECT id, forecast_date, last_update FROM rain_forecast.forecast ORDER BY forecast_date DESC LIMIT 1");

            if (row == null) return Ok(Array.Empty<object>());

            return Ok(new[]
            {
                new
                {
                    id = (Guid)row.id,
                    date = row.forecast_date.ToString("yyyy-MM-dd"),
                    timestamp = row.last_update != null ? ((DateTime)row.last_update).ToString("o") : null,
                    lastUpdate = row.last_update != null ? ((DateTime)row.last_update).ToString("o") : null
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
                "SELECT id, forecast_date, last_update FROM rain_forecast.forecast WHERE forecast_date::date = @D",
                new { D = date });

            if (row == null) return NotFound();

            return Ok(new
            {
                id = (Guid)row.id,
                date = row.forecast_date.ToString("yyyy-MM-dd"),
                timestamp = row.last_update != null ? ((DateTime)row.last_update).ToString("o") : null,
                lastUpdate = row.last_update != null ? ((DateTime)row.last_update).ToString("o") : null
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

            // Map subBasinId → cuenca_code from DB
            var subBasins = await LoadSubBasinsAsync();
            var sb = subBasins.GetValueOrDefault(subBasinId);
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

        // =====================================================================
        // DB Station queries
        // =====================================================================

        /// <summary>
        /// Consulta estaciones reales desde SQL Server (tabla Estacion).
        /// goesFilter: null = todas, true = solo GOES, false = sin GOES.
        /// </summary>
        private async Task<List<object>> GetDbStationsAsync(bool? goesFilter = null)
        {
            using var db = new SqlConnection(_sqlServerConn);
            var sql = @"
                SELECT e.Id AS DatabaseId, e.IdAsignado, e.Nombre, e.Latitud, e.Longitud,
                       e.IdCuenca, e.IdSubcuenca, e.Etiqueta, e.EsPresa,
                       e.GOES, e.GPRS, e.RADIO,
                       g.IdSatelital, o.Nombre AS Organismo
                FROM Estacion e
                LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                WHERE e.Visible = 1 AND e.Activo = 1";
            if (goesFilter == true)
                sql += " AND e.GOES = 1";
            else if (goesFilter == false)
                sql += " AND (e.GOES = 0 OR e.GOES IS NULL)";
            sql += " ORDER BY e.Nombre";

            var rows = await db.QueryAsync<dynamic>(sql);
            return rows.Select(r => MapDbStation(r)).ToList();
        }

        /// <summary>
        /// Consulta estaciones convencionales reales desde la carga BHG.
        /// Estas estaciones nacen del Excel BHG y se almacenan en TimescaleDB.
        /// </summary>
        private async Task<List<object>> GetBhgConventionalStationsAsync()
        {
            using var db = new NpgsqlConnection(_pgConn);
            var rows = await db.QueryAsync<dynamic>(@"
                WITH latest_station AS (
                    SELECT estacion, subcuenca, MAX(ts) AS last_ts
                    FROM bhg_estacion_diario
                    WHERE estacion IS NOT NULL AND BTRIM(estacion) <> ''
                    GROUP BY estacion, subcuenca
                ),
                ranked_station AS (
                    SELECT estacion, subcuenca, last_ts,
                           ROW_NUMBER() OVER (
                               PARTITION BY estacion
                               ORDER BY last_ts DESC, subcuenca NULLS LAST
                           ) AS rn
                    FROM latest_station
                )
                SELECT estacion, subcuenca, last_ts
                FROM ranked_station
                WHERE rn = 1
                ORDER BY subcuenca NULLS LAST, estacion");

            return rows.Select(r => MapBhgStation(r)).ToList();
        }

        private static object MapDbStation(dynamic r) => new
        {
            id = (string?)(r.IdAsignado),
            databaseId = ((Guid)r.DatabaseId).ToString(),
            name = (string?)(r.Nombre),
            latitude = (double?)r.Latitud,
            longitude = (double?)r.Longitud,
            dcpId = (string?)(r.IdSatelital),
            organismo = (string?)(r.Organismo),
            label = (string?)(r.Etiqueta),
            isDam = (bool)(r.EsPresa ?? false),
            goes = (bool)(r.GOES ?? false),
            gprs = (bool)(r.GPRS ?? false),
            radio = (bool)(r.RADIO ?? false),
            type = (bool)(r.GOES ?? false) ? "automatic" : "conventional"
        };

        private static object MapBhgStation(dynamic r)
        {
            string stationName = (string)r.estacion;
            string? subBasin = (string?)r.subcuenca;
            string lastReportDate = r.last_ts switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd"),
                DateOnly d => d.ToString("yyyy-MM-dd"),
                _ => Convert.ToString(r.last_ts) ?? ""
            };

            return new
            {
                id = stationName,
                assignedId = stationName,
                name = stationName,
                clazz = "C",
                type = "conventional",
                centralId = (int?)null,
                subBasinId = subBasin,
                subBasinName = subBasin,
                latitude = (double?)null,
                longitude = (double?)null,
                dcpId = (string?)null,
                source = "BHG",
                lastReportDate
            };
        }

        private bool ValidateAuth()
        {
            // 1. API Key
            if (Request.Headers.TryGetValue("X-Api-Key", out var key) &&
                string.Equals(key, _apiKey, StringComparison.Ordinal))
                return true;

            // 2. Cookie/session auth
            if (User.Identity?.IsAuthenticated == true &&
                (User.IsInRole("ApiConsumer") || User.IsInRole("SuperAdmin") || User.IsInRole("Administrador")))
                return true;

            // 3. JWT Bearer token (manual validation — controller has no [Authorize])
            if (Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                var bearer = authHeader.ToString();
                if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = bearer["Bearer ".Length..].Trim();
                    try
                    {
                        var handler = new JwtSecurityTokenHandler();
                        var validationParams = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = _jwtIssuer,
                            ValidateAudience = true,
                            ValidAudience = _jwtAudience,
                            ValidateLifetime = true,
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey)),
                            ValidateIssuerSigningKey = true
                        };
                        var principal = handler.ValidateToken(token, validationParams, out _);
                        if (principal.IsInRole("ApiConsumer") || principal.IsInRole("SuperAdmin") || principal.IsInRole("Administrador"))
                            return true;
                    }
                    catch { /* token inválido o expirado */ }
                }
            }

            return false;
        }

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

            // 1. Resolve Dam from DB
            var dams = await LoadDamsAsync();
            if (!dams.TryGetValue(request.DamId, out var dam))
                return StatusCode(502, new { error = $"There is no Dam with id: [{request.DamId}]", code = 502 });

            // 2. Resolve Central from DB
            var centrales = await LoadCentralesAsync();
            if (!centrales.TryGetValue(dam.CentralId, out var central))
                return StatusCode(502, new { error = $"There is no Central with id: [{dam.CentralId}]", code = 502 });

            // 3. Resolve SubBasin from DB
            var subBasins = await LoadSubBasinsAsync();
            if (!subBasins.TryGetValue(central.IdSubcuenca, out var subBasin))
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
                var damName = await GetDamNameByCentralAsync(dam.CentralId);
                using var db = new NpgsqlConnection(_pgConn);
                elevCapCurve = (await db.QueryAsync<ElevCapRow>(
                    @"SELECT elevation AS Elevation, capacity_mm3 AS CapacityMm3, area_km2 AS AreaKm2, specific_consumption AS SpecificConsumption
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
            // Cargar estaciones reales de la subcuenca desde SQL Server
            var stationInfos = await LoadRainStationsAsync(subBasin.Id);

            using var db = new NpgsqlConnection(_pgConn);
            await db.OpenAsync();

            foreach (var rec in records)
            {
                bool isReal = rec.Date < zeroDate;
                bool isZeroDay = rec.Date == zeroDate;

                if (isReal)
                {
                    // Get rain: sum weighted precipitation from stations
                    rec.Rain = await GetDailyWeightedRain(db, stationInfos, rec.Date);
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

            var stationInfos = await LoadRainStationsAsync(subBasin.Id);

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
                    rec.Rain = await GetHourlyRainAverage(dbConn, stationInfos, rec.Date, rec.Hour);

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

        private async Task<List<RainStationInfo>> LoadRainStationsAsync(int subBasinId)
        {
            using var db = new SqlConnection(_sqlServerConn);
            var rows = await db.QueryAsync<dynamic>(@"
                SELECT g.IdSatelital, e.IdAsignado
                FROM Estacion e
                LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                WHERE e.Visible = 1 AND e.Activo = 1 AND e.GOES = 1
                      AND e.IdSubcuenca = @SubId",
                new { SubId = subBasinId });
            return rows.Select(r => new RainStationInfo
            {
                DcpId = (string?)(r.IdSatelital) ?? (string)(r.IdAsignado),
                IdAsignado = (string)(r.IdAsignado)
            }).ToList();
        }

        private async Task<decimal> GetDailyWeightedRain(NpgsqlConnection db, List<RainStationInfo> stations, DateTime date)
        {
            if (stations.Count == 0) return 0;
            decimal totalRain = 0;
            foreach (var st in stations)
            {
                var precip = await db.QueryFirstOrDefaultAsync<decimal?>(
                    @"SELECT SUM(COALESCE(acumulado, suma, 0))
                      FROM public.resumen_horario
                      WHERE (dcp_id = @Dcp OR id_asignado = @Asig) AND variable = 'precipitación'
                        AND ts::date = @D AND EXTRACT(HOUR FROM ts) <= 6",
                    new { Dcp = st.DcpId, Asig = st.IdAsignado, D = date });
                totalRain += precip ?? 0;
            }
            return totalRain / stations.Count;
        }

        private async Task<decimal> GetHourlyRainAverage(NpgsqlConnection db, List<RainStationInfo> stations, DateTime date, int hour)
        {
            if (stations.Count == 0) return 0;
            decimal sum = 0;
            foreach (var st in stations)
            {
                var endTs = date.AddHours(hour);
                var rain = await db.QueryFirstOrDefaultAsync<decimal?>(
                    @"SELECT SUM(COALESCE(acumulado, suma, 0))
                      FROM public.resumen_horario
                      WHERE (dcp_id = @Dcp OR id_asignado = @Asig) AND variable = 'precipitación'
                        AND ts >= @St AND ts < @En",
                    new { Dcp = st.DcpId, Asig = st.IdAsignado, St = date, En = endTs });
                sum += rain ?? 0;
            }
            return sum / stations.Count;
        }

        private async Task<decimal?> GetElevationFromFunvasos(NpgsqlConnection db, int centralId, DateTime date, int hour)
        {
            var presa = await GetPresaNameByCentralIdAsync(db, centralId);
            if (presa == null) return null;
            return await db.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT elevacion FROM public.funvasos_horario
                  WHERE presa = @P AND ts::date = @D AND hora = @H",
                new { P = presa, D = date, H = (short)hour });
        }

        private async Task<decimal> GetPrimaryFlow(NpgsqlConnection db, int centralId, DateTime date, int hour)
        {
            var presa = await GetPresaNameByCentralIdAsync(db, centralId);
            if (presa == null) return 0;
            return await db.QueryFirstOrDefaultAsync<decimal?>(
                @"SELECT extracciones_total_q FROM public.funvasos_horario
                  WHERE presa = @P AND ts::date = @D AND hora = @H",
                new { P = presa, D = date, H = (short)hour }) ?? 0;
        }

        private static async Task<string?> GetPresaNameByCentralIdAsync(NpgsqlConnection db, int centralId)
        {
            return await db.QueryFirstOrDefaultAsync<string>(
                "SELECT description FROM hydro_model.dam_params WHERE cascade_order = @Id",
                new { Id = centralId });
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

        private class RainStationInfo
        {
            public string DcpId { get; set; } = "";
            public string IdAsignado { get; set; } = "";
        }

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
