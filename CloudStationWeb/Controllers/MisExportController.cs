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
        public string? RawMessage { get; set; }
        public string? FailureCode { get; set; }
        public string? FrequencyOffset { get; set; }
        public string? ModIndex { get; set; }
        public string? DataQuality { get; set; }
        public string? Spacecraft { get; set; }
        public string? DataSource { get; set; }
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
        public async Task<IActionResult> GetStations(bool onlyCfe = true)
        {
            try
            {
                using var db = new SqlConnection(_sqlConn);
                var sql = @"
                    SELECT e.IdAsignado, ISNULL(g.IdSatelital, '') AS IdSatelital, e.Nombre
                    FROM Estacion e
                    LEFT JOIN DatosGOES g ON g.IdEstacion = e.Id";
                if (onlyCfe)
                {
                    sql += @"
                    JOIN Organismo o ON e.IdOrganismo = o.Id
                    WHERE e.Activo = 1 AND e.GOES = 1 AND o.Nombre = N'Comisión Federal de Electricidad'";
                }
                else
                {
                    sql += @"
                    WHERE e.Activo = 1 AND e.GOES = 1";
                }
                sql += " ORDER BY e.Nombre";
                var stations = await db.QueryAsync<MisStationDto>(sql);
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
                           h.signal_strength AS ""SignalStrength"", h.channel AS ""Channel"",
                           h.raw_message AS ""RawMessage"",
                           h.failure_code AS ""FailureCode"",
                           h.frequency_offset AS ""FrequencyOffset"",
                           h.mod_index AS ""ModIndex"",
                           h.data_quality AS ""DataQuality"",
                           h.spacecraft AS ""Spacecraft"",
                           h.data_source AS ""DataSource""
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
                            sensorMap.GetValueOrDefault(idAsig, new List<MisSensorDto>()),
                            hdr.RawMessage,
                            hdr.FailureCode, hdr.FrequencyOffset,
                            hdr.ModIndex, hdr.DataQuality,
                            hdr.Spacecraft, hdr.DataSource);

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
            List<MisDatoDto> datos, List<MisSensorDto> sensores,
            string? rawMessage = null,
            string? failureCode = null, string? frequencyOffset = null,
            string? modIndex = null, string? dataQuality = null,
            string? spacecraft = null, string? dataSource = null)
        {
            var sb = new StringBuilder();

            // Header: DCP_ID,DD/MM/YYYY HH:MM:SS,+XXdBm,Good Msg!,CCC
            string sigStr = signalStrength.HasValue ? $"+{signalStrength.Value}dBm" : "+00dBm";
            string chStr = channel.HasValue ? channel.Value.ToString().PadLeft(3, '0') : "000";
            sb.AppendLine($"{dcpId},{txTime:dd/MM/yyyy HH:mm:ss},{sigStr},Good Msg!,{chStr}");

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

            // Footer: raw DOMSAT header
            if (!string.IsNullOrEmpty(rawMessage))
                sb.AppendLine($"{{{rawMessage}}}");
            else
                sb.AppendLine($"{{{BuildGoesHeader(dcpId, txTime, signalStrength, channel, failureCode, frequencyOffset, modIndex, dataQuality, spacecraft, dataSource, datos.Count)}}}");

            return sb.ToString();
        }

        /// <summary>
        /// Reconstructs a GOES DCP header string from individual fields.
        /// Format: PPPPPPPPYYDDDHHMMSSFSSO MQQQCCCSSSDDDDD
        /// Example: 1563D3FA23206064001G48+0NN055EXE00116
        /// </summary>
        private string BuildGoesHeader(string dcpId, DateTime txTime,
            short? signalStrength, short? channel,
            string? failureCode, string? frequencyOffset,
            string? modIndex, string? dataQuality,
            string? spacecraft, string? dataSource,
            int dataCount)
        {
            // DCP Address (8 chars, uppercase hex)
            string addr = (dcpId ?? "").PadRight(8).Substring(0, 8).ToUpper();

            // Date: YYDDDHHMMSS (year 2-digit, julian day 3-digit, HHMMSS)
            int julianDay = txTime.DayOfYear;
            string dateStr = $"{txTime:yy}{julianDay:D3}{txTime:HHmmss}";

            // Failure code (1 char: G=Good, ?=Questionable, etc.)
            string fail = !string.IsNullOrEmpty(failureCode) ? failureCode.Substring(0, 1) : "G";

            // Signal strength (2 digits)
            string sig = signalStrength.HasValue ? signalStrength.Value.ToString("D2") : "00";

            // Frequency offset (e.g. "+0", "-1")
            string freq = !string.IsNullOrEmpty(frequencyOffset) ? frequencyOffset : "+0";

            // Modulation index (1 char: N=Normal)
            string mod = !string.IsNullOrEmpty(modIndex) ? modIndex.Substring(0, 1) : "N";

            // Data quality (1 char: N=Normal)
            string qual = !string.IsNullOrEmpty(dataQuality) ? dataQuality.Substring(0, 1) : "N";

            // Channel (3 digits)
            string ch = channel.HasValue ? channel.Value.ToString("D3") : "000";

            // Spacecraft + Data Source (e.g. "EXE")
            string src = "";
            if (!string.IsNullOrEmpty(spacecraft)) src += spacecraft;
            if (!string.IsNullOrEmpty(dataSource)) src += dataSource;
            if (string.IsNullOrEmpty(src)) src = "EXE";

            // Data length (5 digits, approximate from record count)
            string dataLen = (dataCount * 10).ToString("D5");

            return $"{addr}{dateStr}{fail}{sig}{freq}{mod}{qual}{ch}{src}{dataLen}";
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
