using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Models;
using CloudStationWeb.Data;
using Npgsql;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace CloudStationWeb.Controllers
{
    public class GoesMonitoringController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        private readonly string _connectionString;

        public GoesMonitoringController(IConfiguration configuration, ApplicationDbContext context)
        {
            _configuration = configuration;
            _context = context;
            _connectionString = _configuration.GetConnectionString("PostgreSQL") ?? "";
        }

        public IActionResult Index()
        {
            return View();
        }

        private async Task<Dictionary<string, string>> GetStationNamesMap()
        {
            try
            {
               return await _context.Database
                    .SqlQueryRaw<StationNameDto>(@"
                        SELECT g.IdSatelital, e.Nombre 
                        FROM DatosGOES g
                        JOIN Estacion e ON g.IdEstacion = e.Id
                    ")
                    .ToDictionaryAsync(s => s.IdSatelital, s => s.Nombre);
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        [HttpGet]
        public async Task<IActionResult> ListTables()
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var tables = new List<string>();
                using (var cmd = new NpgsqlCommand(@"
                    SELECT table_name 
                    FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    ORDER BY table_name", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        tables.Add(reader.GetString(0));
                    }
                }

                return Json(new { tables, count = tables.Count });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DescribeTable(string tableName = "bitacora_goes")
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var columns = new List<object>();
                using (var cmd = new NpgsqlCommand($@"
                    SELECT column_name, data_type, is_nullable
                    FROM information_schema.columns 
                    WHERE table_name = '{tableName}' 
                    ORDER BY ordinal_position", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        columns.Add(new {
                            name = reader.GetString(0),
                            type = reader.GetString(1),
                            nullable = reader.GetString(2)
                        });
                    }
                }

                // Get sample row
                object? sampleRow = null;
                using (var cmd = new NpgsqlCommand($"SELECT * FROM {tableName} LIMIT 1", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                        sampleRow = row;
                    }
                }

                // Get row count
                long rowCount = 0;
                using (var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {tableName}", conn))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    rowCount = result != null ? Convert.ToInt64(result) : 0;
                }

                return Json(new { tableName, columns, sampleRow, rowCount });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTransmissionStats(string period = "24h")
        {
            try
            {
                var hours = period switch
                {
                    "7d" => 168,
                    "30d" => 720,
                    _ => 24
                };

                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Get authoritative station list from SQL Server
                var stationNamesMap = await GetStationNamesMap();
                var validStationIds = stationNamesMap.Keys.ToArray();

                var stats = new TransmissionStats();
                stats.TotalStations = validStationIds.Length;

                if (stats.TotalStations == 0)
                {
                    return Json(stats);
                }

                // Estaciones activas (con transmisiones exitosas en el período)
                using (var cmd = new NpgsqlCommand($@"
                    SELECT COUNT(DISTINCT dcp_id) 
                    FROM bitacora_goes 
                    WHERE timestamp_utc > NOW() - INTERVAL '{hours} hours' 
                      AND exito = true
                      AND dcp_id = ANY(@ids)", conn))
                {
                    cmd.Parameters.AddWithValue("ids", validStationIds);
                    var result = await cmd.ExecuteScalarAsync();
                    stats.ActiveStations = result != null ? Convert.ToInt32(result) : 0;
                }

                // Estaciones silenciosas
                stats.SilentStations = stats.TotalStations - stats.ActiveStations;

                // Total de transmisiones exitosas recibidas
                using (var cmd = new NpgsqlCommand($@"
                    SELECT COUNT(*) 
                    FROM bitacora_goes 
                    WHERE timestamp_utc > NOW() - INTERVAL '{hours} hours' 
                      AND exito = true
                      AND dcp_id = ANY(@ids)", conn))
                {
                    cmd.Parameters.AddWithValue("ids", validStationIds);
                    var result = await cmd.ExecuteScalarAsync();
                    stats.TotalTransmissionsLast24h = result != null ? Convert.ToInt32(result) : 0;
                }

                // Transmisiones esperadas (asumiendo 1 TX por hora por estación)
                stats.ExpectedTransmissionsLast24h = stats.TotalStations * hours;

                // Tasa de recepción
                if (stats.ExpectedTransmissionsLast24h > 0)
                {
                    stats.ReceptionRate = Math.Round(
                        (decimal)stats.TotalTransmissionsLast24h / stats.ExpectedTransmissionsLast24h * 100, 
                        1
                    );
                }

                return Json(stats);
            }
            catch (Exception ex)
            {
                // Return empty stats if table doesn't exist or other error
                return Json(new TransmissionStats
                {
                    TotalStations = 0,
                    ActiveStations = 0,
                    SilentStations = 0,
                    ReceptionRate = 0,
                    TotalTransmissionsLast24h = 0,
                    ExpectedTransmissionsLast24h = 0
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetStationHealth(string period = "24h", string filter = "all")
        {
            try
            {
                var hours = period switch
                {
                    "7d" => 168,
                    "30d" => 720,
                    _ => 24
                };

                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // 1. Obtener lista autoritativa de SQL Server
                var stationNamesMap = await GetStationNamesMap();
                var validIds = stationNamesMap.Keys.ToArray();

                if (validIds.Length == 0)
                {
                    return Json(new List<StationHealth>());
                }

                var healthList = new List<StationHealth>();

                // 2. Obtener estadísticas de TimescaleDB solo para esas estaciones
                using (var cmd = new NpgsqlCommand($@"
                    SELECT 
                        dcp_id,
                        MAX(timestamp_utc) as last_tx,
                        COUNT(*) FILTER (WHERE exito = true) as tx_count
                    FROM bitacora_goes
                    WHERE timestamp_utc > NOW() - INTERVAL '{hours} hours'
                      AND dcp_id = ANY(@ids)
                    GROUP BY dcp_id", conn))
                {
                    cmd.Parameters.AddWithValue("ids", validIds);
                    
                    var statsMap = new Dictionary<string, (DateTime? lastTx, int txCount)>();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            statsMap[reader.GetString(0)] = (
                                reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1),
                                reader.GetInt32(2)
                            );
                        }
                    }

                    // 3. Cruzar datos: La base es SQL Server
                    foreach (var entry in stationNamesMap)
                    {
                        var dcpId = entry.Key;
                        var stationName = entry.Value;
                        
                        var lastTx = statsMap.ContainsKey(dcpId) ? statsMap[dcpId].lastTx : null;
                        var txCount = statsMap.ContainsKey(dcpId) ? statsMap[dcpId].txCount : 0;
                        var expectedTx = hours;

                        var healthScore = expectedTx > 0 ? (decimal)txCount / expectedTx * 100 : 0;
                        
                        string status;
                        string statusColor;
                        
                        if (healthScore >= 90) { status = "Operativa"; statusColor = "#22c55e"; }
                        else if (healthScore >= 50) { status = "Atención"; statusColor = "#eab308"; }
                        else if (healthScore > 0) { status = "Falla"; statusColor = "#ef4444"; }
                        else { status = "Sin Datos"; statusColor = "#6b7280"; }

                        var hoursSinceTx = lastTx.HasValue 
                            ? (int)(DateTime.UtcNow - lastTx.Value).TotalHours 
                            : 9999;

                        var health = new StationHealth
                        {
                            StationId = dcpId,
                            StationName = stationName,
                            LastTransmission = lastTx,
                            TransmissionsLast24h = txCount,
                            ExpectedTransmissions = expectedTx,
                            HealthScore = Math.Round(healthScore, 1),
                            Status = status,
                            StatusColor = statusColor,
                            HoursSinceLastTx = hoursSinceTx
                        };

                        // Aplicar filtro
                        if (filter == "all" ||
                            (filter == "active" && status == "Operativa") ||
                            (filter == "attention" && status == "Atención") ||
                            (filter == "failed" && (status == "Falla" || status == "Sin Datos")))
                        {
                            healthList.Add(health);
                        }
                    }
                }

                // Ordenar por última transmisión
                healthList = healthList.OrderByDescending(h => h.LastTransmission).ToList();

                return Json(healthList);
            }
            catch (Exception ex)
            {
                // Return error for debugging
                return Json(new { error = ex.Message, stack = ex.StackTrace });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTransmissionTimeline(string period = "24h")
        {
            try
            {
                var hours = period switch
                {
                    "7d" => 168,
                    "30d" => 720,
                    _ => 24
                };

                var bucketSize = period switch
                {
                    "7d" => "hour",  // Agrupar por hora para 7 días
                    "30d" => "day",   // Agrupar por día para 30 días
                    _ => "hour"       // Agrupar por hora para 24h
                };

                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var timeline = new List<TransmissionTimelinePoint>();

                // Obtener nombres de estaciones
                var stationNames = await GetStationNamesMap();
                var validIds = stationNames.Keys.ToArray();

                if (validIds.Length == 0)
                {
                    return Json(new List<TransmissionTimelinePoint>());
                }

                using (var cmd = new NpgsqlCommand($@"
                    SELECT 
                        dcp_id,
                        DATE_TRUNC('{bucketSize}', timestamp_utc) as bucket,
                        COUNT(*) FILTER (WHERE exito = true) as tx_count
                    FROM bitacora_goes
                    WHERE timestamp_utc > NOW() - INTERVAL '{hours} hours'
                      AND dcp_id = ANY(@ids)
                    GROUP BY dcp_id, bucket
                    ORDER BY dcp_id, bucket", conn))
                {
                    cmd.Parameters.AddWithValue("ids", validIds);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var stationId = reader.GetString(0);
                        var name = stationNames.ContainsKey(stationId) ? stationNames[stationId] : stationId;

                        timeline.Add(new TransmissionTimelinePoint
                        {
                            StationId = stationId,
                            StationName = name,
                            Timestamp = reader.GetDateTime(1),
                            Received = reader.GetInt32(2) > 0,
                            TransmissionCount = reader.GetInt32(2)
                        });
                    }
                }

                return Json(timeline);
            }
            catch (Exception ex)
            {
                return Json(new List<TransmissionTimelinePoint>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTransmissionTrend(string period = "24h")
        {
            try
            {
                var hours = period switch
                {
                    "7d" => 168,
                    "30d" => 720,
                    _ => 24
                };

                var bucketSize = period switch
                {
                    "7d" => "hour",  // Agrupar por hora para 7 días
                    "30d" => "day",   // Agrupar por día para 30 días
                    _ => "hour"       // Agrupar por hora para 24h
                };

                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Get authoritative list
                var stationNames = await GetStationNamesMap();
                var validIds = stationNames.Keys.ToArray();
                var trend = new List<TransmissionTrendPoint>();

                if (validIds.Length == 0)
                {
                    return Json(trend);
                }

                using (var cmd = new NpgsqlCommand($@"
                    SELECT 
                        DATE_TRUNC('{bucketSize}', timestamp_utc) as bucket,
                        COUNT(*) FILTER (WHERE exito = true) as tx_count
                    FROM bitacora_goes
                    WHERE timestamp_utc > NOW() - INTERVAL '{hours} hours'
                      AND dcp_id = ANY(@ids)
                    GROUP BY bucket
                    ORDER BY bucket", conn))
                {
                    cmd.Parameters.AddWithValue("ids", validIds);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var receivedCount = reader.GetInt32(1);
                        var expectedCount = validIds.Length; // 1 TX por estación por bucket
                        var successRate = expectedCount > 0 
                            ? Math.Round((decimal)receivedCount / expectedCount * 100, 1) 
                            : 0;

                        trend.Add(new TransmissionTrendPoint
                        {
                            Hour = reader.GetDateTime(0),
                            ReceivedCount = receivedCount,
                            ExpectedCount = expectedCount,
                            SuccessRate = successRate
                        });
                    }
                }

                return Json(trend);
            }
            catch (Exception ex)
            {
                return Json(new List<TransmissionTrendPoint>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetStationDetail(string stationId, string period = "24h")
        {
            try
            {
                var hours = period switch
                {
                    "7d" => 168,
                    "30d" => 720,
                    _ => 24
                };

                var bucketSize = period switch
                {
                    "7d" => "hour",
                    "30d" => "day",
                    _ => "hour"
                };

                // Get station name
                var stationNames = await GetStationNamesMap();
                var stationName = stationNames.ContainsKey(stationId) ? stationNames[stationId] : stationId;

                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var detail = new List<object>();

                using (var cmd = new NpgsqlCommand($@"
                    SELECT 
                        DATE_TRUNC('{bucketSize}', timestamp_utc) as bucket,
                        COUNT(*) as total,
                        COUNT(*) FILTER (WHERE exito = true) as exitosas,
                        COUNT(*) FILTER (WHERE exito = false) as fallidas
                    FROM bitacora_goes
                    WHERE dcp_id = @stationId
                      AND timestamp_utc > NOW() - INTERVAL '{hours} hours'
                    GROUP BY bucket
                    ORDER BY bucket", conn))
                {
                    cmd.Parameters.AddWithValue("stationId", stationId);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        detail.Add(new
                        {
                            hour = reader.GetDateTime(0),
                            total = reader.GetInt32(1),
                            exitosas = reader.GetInt32(2),
                            fallidas = reader.GetInt32(3)
                        });
                    }
                }

                return Json(new { stationName, stationId, data = detail });
            }
            catch (Exception ex)
            {
                return Json(new { stationName = stationId, stationId, data = new List<object>(), error = ex.Message });
            }
        }

    }
}
