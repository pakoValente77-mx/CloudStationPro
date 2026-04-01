using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Npgsql;
using Dapper;
using System.IO.Compression;
using System.Text;

namespace CloudStationWeb.Controllers
{
    // DTOs
    public class MisStationDto
    {
        public string IdAsignado { get; set; } = "";
        public string IdSatelital { get; set; } = "";
        public string Nombre { get; set; } = "";
    }

    public class MisSensorDto
    {
        public string NumeroSensor { get; set; } = "";
        public string Nombre { get; set; } = "";
        public int Decimales { get; set; }
        public int Periodo { get; set; }
    }

    public class MisDatoDto
    {
        public DateTime Ts { get; set; }
        public string DcpId { get; set; } = "";
        public string IdAsignado { get; set; } = "";
        public string SensorId { get; set; } = "";
        public string Variable { get; set; } = "";
        public float Valor { get; set; }
    }

    public class MisHeaderDto
    {
        public string DcpId { get; set; } = "";
        public DateTime TimestampMsg { get; set; }
        public short? SignalStrength { get; set; }
        public short? Channel { get; set; }
    }

    [Authorize]
    public class MisExportController : Controller
    {
        private readonly string _sqlConn;
        private readonly string _pgConn;
        private readonly ILogger<MisExportController> _logger;

        public MisExportController(IConfiguration config, ILogger<MisExportController> logger)
        {
            _sqlConn = config.GetConnectionString("SqlServer")!;
            _pgConn = config.GetConnectionString("PostgreSQL")!;
            _logger = logger;
        }

        // GET: /MisExport
        public IActionResult Index()
        {
            return View();
        }

        // GET: /MisExport/GetStations
        [HttpGet]
        public async Task<IActionResult> GetStations()
        {
            try
            {
                using var db = new SqlConnection(_sqlConn);
                var stations = await db.QueryAsync<MisStationDto>(@"
                    SELECT e.IdAsignado, ISNULL(g.IdSatelital, '') AS IdSatelital, e.Nombre
                    FROM Estacion e
                    LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id
                    WHERE e.Activo = 1 AND e.GOES = 1
                    ORDER BY e.Nombre");
                return Json(stations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading stations for MIS export");
                return Json(new { error = ex.Message });
            }
        }

        // GET: /MisExport/GetSensors?idAsignado=XXX
        [HttpGet]
        public async Task<IActionResult> GetSensors(string idAsignado)
        {
            try
            {
                using var db = new SqlConnection(_sqlConn);
                var sensors = await db.QueryAsync<MisSensorDto>(@"
                    SELECT 
                        RIGHT('0000' + CAST(s.NumeroSensor AS VARCHAR), 4) AS NumeroSensor,
                        ts.Nombre,
                        ISNULL(s.PuntoDecimal, 2) AS Decimales,
                        s.PeriodoMuestra AS Periodo
                    FROM Sensor s
                    INNER JOIN Estacion e ON s.IdEstacion = e.Id
                    INNER JOIN TipoSensor ts ON s.IdTipoSensor = ts.Id
                    WHERE e.IdAsignado = @IdAsignado AND s.Activo = 1
                    ORDER BY s.NumeroSensor", new { IdAsignado = idAsignado });
                return Json(sensors);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // GET: /MisExport/Preview?idAsignado=XXX&fechaInicio=2026-03-01&fechaFin=2026-03-02
        [HttpGet]
        public async Task<IActionResult> Preview(string? idAsignado, DateTime fechaInicio, DateTime fechaFin)
        {
            try
            {
                if ((fechaFin - fechaInicio).TotalDays > 31)
                    return Json(new { error = "El rango máximo es 31 días." });

                using var conn = new NpgsqlConnection(_pgConn);
                await conn.OpenAsync();

                string whereStation = string.IsNullOrEmpty(idAsignado) ? "" : "AND d.id_asignado = @IdAsignado";

                var sql = $@"
                    SELECT d.id_asignado AS ""idAsignado"", d.dcp_id AS ""dcpId"",
                           COUNT(*) AS ""registros"",
                           MIN(d.ts) AS ""primerRegistro"",
                           MAX(d.ts) AS ""ultimoRegistro""
                    FROM dcp_datos d
                    WHERE d.ts >= @FechaInicio AND d.ts < @FechaFin
                      AND d.valido = true
                      {whereStation}
                    GROUP BY d.id_asignado, d.dcp_id
                    ORDER BY d.id_asignado";

                var preview = await conn.QueryAsync(sql, new
                {
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin.AddDays(1),
                    IdAsignado = idAsignado ?? ""
                });

                return Json(preview);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // GET: /MisExport/Download?idAsignado=XXX&fechaInicio=2026-03-01&fechaFin=2026-03-02
        [HttpGet]
        public async Task<IActionResult> Download(string? idAsignado, DateTime fechaInicio, DateTime fechaFin)
        {
            try
            {
                if ((fechaFin - fechaInicio).TotalDays > 31)
                    return BadRequest("El rango máximo es 31 días.");

                // 1. Load sensor metadata from SQL Server
                var sensorMap = await LoadSensorMetadata(idAsignado);

                // 2. Load id_asignado → dcp_id mapping
                var idMap = await LoadIdMap(idAsignado);

                // 3. Fetch GOES transmissions from dcp_headers (the real Tx timestamps)
                using var conn = new NpgsqlConnection(_pgConn);
                await conn.OpenAsync();

                // Build dcp_id filter from idAsignado
                string whereDcp = "";
                if (!string.IsNullOrEmpty(idAsignado) && idMap.TryGetValue(idAsignado, out var dcpIdFilter))
                    whereDcp = "AND h.dcp_id = @DcpId";

                var headersSql = $@"
                    SELECT h.dcp_id AS ""DcpId"", h.timestamp_msg AS ""TimestampMsg"",
                           h.signal_strength AS ""SignalStrength"", h.channel AS ""Channel""
                    FROM dcp_headers h
                    WHERE h.timestamp_msg >= @FechaInicio AND h.timestamp_msg < @FechaFin
                      {whereDcp}
                    ORDER BY h.dcp_id, h.timestamp_msg";

                var headers = (await conn.QueryAsync<MisHeaderDto>(headersSql, new
                {
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin.AddDays(1),
                    DcpId = !string.IsNullOrEmpty(idAsignado) && idMap.ContainsKey(idAsignado) ? idMap[idAsignado] : ""
                })).ToList();

                if (headers.Count == 0)
                    return BadRequest("No hay transmisiones GOES para el rango seleccionado.");

                // 4. Fetch all data for the range
                string whereStation = string.IsNullOrEmpty(idAsignado) ? "" : "AND d.id_asignado = @IdAsignado";

                var dataSql = $@"
                    SELECT d.ts AS ""Ts"", d.dcp_id AS ""DcpId"", d.id_asignado AS ""IdAsignado"",
                           d.sensor_id AS ""SensorId"", d.variable AS ""Variable"", d.valor AS ""Valor""
                    FROM dcp_datos d
                    WHERE d.ts >= @FechaInicio AND d.ts < @FechaFin
                      AND d.valido = true
                      {whereStation}
                    ORDER BY d.dcp_id, d.ts, d.sensor_id";

                var datos = (await conn.QueryAsync<MisDatoDto>(dataSql, new
                {
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin.AddDays(1),
                    IdAsignado = idAsignado ?? ""
                })).ToList();

                // Index data by dcp_id for fast lookup
                var dataByDcp = datos.GroupBy(d => d.DcpId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(d => d.Ts).ToList());

                // Reverse map: dcp_id → id_asignado
                var reverseIdMap = idMap.ToDictionary(kv => kv.Value, kv => kv.Key);

                // 5. Generate ZIP — one .mis per GOES transmission
                using var memStream = new MemoryStream();
                using (var archive = new ZipArchive(memStream, ZipArchiveMode.Create, true))
                {
                    foreach (var hdr in headers)
                    {
                        var txTime = hdr.TimestampMsg;
                        var dcpId = hdr.DcpId ?? "";
                        var idAsig = reverseIdMap.GetValueOrDefault(dcpId, "");

                        // Find data belonging to this transmission:
                        // Data with ts <= txTime and ts > (txTime - 1 hour)
                        if (!dataByDcp.TryGetValue(dcpId, out var stationData))
                            continue;

                        var txData = stationData
                            .Where(d => d.Ts <= txTime && d.Ts > txTime.AddHours(-1))
                            .ToList();

                        if (txData.Count == 0) continue;

                        if (string.IsNullOrEmpty(idAsig) && txData.Count > 0)
                            idAsig = txData[0].IdAsignado ?? "";

                        string misContent = GenerateMisContent(
                            dcpId, idAsig, txTime,
                            hdr.SignalStrength, hdr.Channel,
                            txData,
                            sensorMap.GetValueOrDefault(idAsig, new List<MisSensorDto>()));

                        // Path: YYYY/MM/DD/DCPID_YYYYMMDDHHMMEX.mis  (using Tx time)
                        string entryName = $"{txTime:yyyy}/{txTime:MM}/{txTime:dd}/{dcpId}_{txTime:yyyyMMddHHmm}EX.mis";
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                        writer.Write(misContent);
                    }
                }

                memStream.Position = 0;
                string zipName = string.IsNullOrEmpty(idAsignado)
                    ? $"MIS_TODAS_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.zip"
                    : $"MIS_{idAsignado}_{fechaInicio:yyyyMMdd}_{fechaFin:yyyyMMdd}.zip";

                return File(memStream.ToArray(), "application/zip", zipName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating MIS export");
                return BadRequest($"Error: {ex.Message}");
            }
        }

        private string GenerateMisContent(string dcpId, string idAsignado, DateTime txTime,
            short? signalStrength, short? channel,
            List<MisDatoDto> datos, List<MisSensorDto> sensores)
        {
            var sb = new StringBuilder();

            // Header: DCP_ID,DD/MM/YYYY HH:MM:SS,+XXdBm,Recovered,CCC
            string sigStr = signalStrength.HasValue ? $"+{signalStrength.Value}dBm" : "+00dBm";
            string chStr = channel.HasValue ? channel.Value.ToString().PadLeft(3, '0') : "000";
            sb.AppendLine($"{dcpId},{txTime:dd/MM/yyyy HH:mm:ss},{sigStr},Recovered,{chStr}");

            // Group data by sensor
            var bySensor = datos
                .GroupBy(d => d.SensorId)
                .OrderBy(g => g.Key);

            foreach (var sensorGroup in bySensor)
            {
                string sensorId = sensorGroup.Key.PadLeft(4, '0');
                var sensorMeta = sensores.FirstOrDefault(s => s.NumeroSensor == sensorId);

                sb.AppendLine($"<STATION>{idAsignado}</STATION><SENSOR>{sensorId}</SENSOR><DATEFORMAT>YYYYMMDD</DATEFORMAT>");

                foreach (var d in sensorGroup.OrderByDescending(d => d.Ts))
                {
                    string dateStr = d.Ts.ToString("yyyyMMdd");
                    string timeStr = d.Ts.ToString("HH:mm:ss");
                    // Format value: use decimals from sensor meta, default 2
                    int dec = sensorMeta?.Decimales ?? 2;
                    string valorStr = Math.Round(d.Valor, dec).ToString($"F{dec}").TrimEnd('0').TrimEnd('.');
                    if (string.IsNullOrEmpty(valorStr) || valorStr == "-") valorStr = "0";
                    sb.AppendLine($"{dateStr};{timeStr};{valorStr}");
                }
            }

            // Footer: recovered data has no raw DOMSAT
            sb.AppendLine("{RECOVERED_FROM_TIMESCALEDB}");

            return sb.ToString();
        }

        private async Task<Dictionary<string, List<MisSensorDto>>> LoadSensorMetadata(string? idAsignado)
        {
            using var db = new SqlConnection(_sqlConn);
            string whereClause = string.IsNullOrEmpty(idAsignado) ? "" : "AND e.IdAsignado = @IdAsignado";

            var sensors = await db.QueryAsync<dynamic>($@"
                SELECT e.IdAsignado,
                       RIGHT('0000' + CAST(s.NumeroSensor AS VARCHAR), 4) AS NumeroSensor,
                       ts.Nombre,
                       ISNULL(s.PuntoDecimal, 2) AS Decimales,
                       s.PeriodoMuestra AS Periodo
                FROM Sensor s
                INNER JOIN Estacion e ON s.IdEstacion = e.Id
                INNER JOIN TipoSensor ts ON s.IdTipoSensor = ts.Id
                WHERE s.Activo = 1 AND e.Activo = 1
                {whereClause}", new { IdAsignado = idAsignado ?? "" });

            var result = new Dictionary<string, List<MisSensorDto>>();
            foreach (var s in sensors)
            {
                string key = (string)s.IdAsignado;
                if (!result.ContainsKey(key))
                    result[key] = new List<MisSensorDto>();
                result[key].Add(new MisSensorDto
                {
                    NumeroSensor = (string)s.NumeroSensor,
                    Nombre = (string)s.Nombre,
                    Decimales = (int)s.Decimales,
                    Periodo = (int)s.Periodo
                });
            }
            return result;
        }

        private async Task<Dictionary<string, string>> LoadIdMap(string? idAsignado)
        {
            // Returns: IdAsignado → DcpId (IdSatelital)
            using var db = new SqlConnection(_sqlConn);
            string whereClause = string.IsNullOrEmpty(idAsignado) ? "" : "AND e.IdAsignado = @IdAsignado";

            var rows = await db.QueryAsync<dynamic>($@"
                SELECT e.IdAsignado, g.IdSatelital
                FROM Estacion e
                INNER JOIN DatosGOES g ON g.IdEstacion = e.Id
                WHERE e.Activo = 1 AND g.IdSatelital IS NOT NULL AND g.IdSatelital <> ''
                {whereClause}", new { IdAsignado = idAsignado ?? "" });

            return rows.ToDictionary(r => (string)r.IdAsignado, r => (string)r.IdSatelital);
        }
    }
}
