using CloudStationWeb.Hubs;
using CloudStationWeb.Models;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CloudStationWeb.Services
{
    /// <summary>
    /// Background service that monitors average 24h precipitation per cuenca.
    /// When average exceeds 1 mm, sends push notification + chat message with
    /// station-level detail. Runs every hour. Alerts persist until condition clears.
    /// </summary>
    public class PrecipitationAlertService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<PrecipitationAlertService> _logger;
        private readonly PushNotificationService _pushService;
        private readonly IHubContext<ChatHub> _hubContext;

        private readonly string _sqlConn;
        private readonly string _pgConn;

        // Check interval: every hour
        private readonly int _intervalMinutes;
        // Threshold: 1 mm average precipitation in 24h per cuenca
        private readonly float _thresholdMm;
        // Alert room in chat
        private const string AlertRoom = "alertas-precipitacion";

        // Track active alerts per cuenca to avoid re-sending until condition changes
        // Key = cuenca name, Value = last alert time
        private readonly Dictionary<string, DateTime> _activeAlerts = new(StringComparer.OrdinalIgnoreCase);

        // Cooldown: re-send detail update every N hours while alert is active
        private readonly int _updateHours;

        // Enable/disable flag (default: disabled on startup)
        private bool _enabled;

        public PrecipitationAlertService(
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            ILogger<PrecipitationAlertService> logger,
            PushNotificationService pushService,
            IHubContext<ChatHub> hubContext)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
            _pushService = pushService;
            _hubContext = hubContext;

            _sqlConn = config.GetConnectionString("SqlServer")!;
            _pgConn = config.GetConnectionString("PostgreSQL")!;

            _intervalMinutes = config.GetValue("PrecipitationAlert:IntervalMinutes", 60);
            _thresholdMm = config.GetValue("PrecipitationAlert:ThresholdMm", 1.0f);
            _updateHours = config.GetValue("PrecipitationAlert:UpdateHours", 3);
            _enabled = config.GetValue("PrecipitationAlert:Enabled", false);
        }

        public bool IsEnabled => _enabled;
        public int IntervalMinutes => _intervalMinutes;
        public float ThresholdMm => _thresholdMm;

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            _logger.LogInformation("PrecipitationAlertService {Status}", enabled ? "ENABLED" : "DISABLED");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "PrecipitationAlertService started. Interval={I}min, Threshold={T}mm",
                _intervalMinutes, _thresholdMm);

            // Wait for app startup
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_enabled)
                {
                    try
                    {
                        await EvaluatePrecipitationAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in PrecipitationAlert evaluation cycle");
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);
            }
        }

        private async Task EvaluatePrecipitationAsync(CancellationToken ct)
        {
            _logger.LogInformation("PrecipitationAlert: Starting evaluation cycle");

            // 1. Get CFE stations with cuenca from SQL Server
            List<StationCuenca> stations;
            using (var sqlDb = new SqlConnection(_sqlConn))
            {
                stations = (await sqlDb.QueryAsync<StationCuenca>(@"
                    SELECT e.IdAsignado, e.Nombre,
                           ISNULL(c.Nombre, '') AS Cuenca,
                           ISNULL(sc.Nombre, '') AS Subcuenca
                    FROM Estacion e
                    LEFT JOIN Cuenca c ON e.IdCuenca = c.Id
                    LEFT JOIN Subcuenca sc ON e.IdSubcuenca = sc.Id
                    LEFT JOIN Organismo o ON e.IdOrganismo = o.Id
                    WHERE e.Activo = 1 AND e.Visible = 1
                      AND o.Nombre = 'Comisión Federal de Electricidad'
                      AND e.IdAsignado IS NOT NULL
                      AND c.Nombre IS NOT NULL AND c.Nombre <> 'Indefinida'")).ToList();
            }

            if (stations.Count == 0) return;

            var stationIds = stations
                .Where(s => !string.IsNullOrEmpty(s.IdAsignado))
                .Select(s => s.IdAsignado!.Trim())
                .Distinct()
                .ToArray();

            // 2. Get 24h accumulated precipitation per station from PostgreSQL
            var desde = DateTime.UtcNow.AddHours(-24);
            List<(string id_asignado, float precip_mm)> precipRows;
            using (var pgDb = new NpgsqlConnection(_pgConn))
            {
                precipRows = (await pgDb.QueryAsync<(string id_asignado, float precip_mm)>(@"
                    SELECT id_asignado, COALESCE(SUM(acumulado), 0)::real AS precip_mm
                    FROM resumen_horario
                    WHERE variable = 'precipitación'
                      AND ts >= @Desde
                      AND id_asignado = ANY(@Ids)
                      AND acumulado >= 0 AND acumulado <= 200
                    GROUP BY id_asignado",
                    new { Desde = desde, Ids = stationIds })).ToList();
            }

            var precipByStation = precipRows.ToDictionary(
                r => r.id_asignado, r => r.precip_mm, StringComparer.OrdinalIgnoreCase);

            // 3. Group by cuenca and calculate averages
            var cuencaGroups = stations
                .GroupBy(s => s.Cuenca!, StringComparer.OrdinalIgnoreCase);

            var alertedCuencas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in cuencaGroups)
            {
                var cuencaName = group.Key;
                var stationsInCuenca = group.ToList();

                var stationDetails = stationsInCuenca.Select(s =>
                {
                    var hasData = precipByStation.TryGetValue(s.IdAsignado?.Trim() ?? "", out var precip);
                    return new StationPrecipDetail
                    {
                        IdAsignado = s.IdAsignado ?? "",
                        Nombre = s.Nombre ?? "",
                        Subcuenca = s.Subcuenca ?? "",
                        PrecipMm = hasData ? precip : 0,
                        ConDato = hasData && precip > 0
                    };
                }).ToList();

                var valoresConDato = stationDetails.Where(s => s.ConDato).Select(s => s.PrecipMm).ToList();
                float promedio = valoresConDato.Count > 0 ? valoresConDato.Average() : 0;

                if (promedio >= _thresholdMm)
                {
                    alertedCuencas.Add(cuencaName);

                    // Check if we should send (new alert or update interval reached)
                    bool shouldSend = false;
                    if (!_activeAlerts.ContainsKey(cuencaName))
                    {
                        // New alert
                        shouldSend = true;
                        _logger.LogWarning(
                            "PrecipitationAlert: NEW alert for cuenca '{Cuenca}' - avg={Avg:F1}mm (threshold={T}mm)",
                            cuencaName, promedio, _thresholdMm);
                    }
                    else
                    {
                        // Already alerted; send update every N hours
                        var lastAlert = _activeAlerts[cuencaName];
                        if (DateTime.UtcNow - lastAlert >= TimeSpan.FromHours(_updateHours))
                        {
                            shouldSend = true;
                            _logger.LogInformation(
                                "PrecipitationAlert: UPDATE alert for cuenca '{Cuenca}' - avg={Avg:F1}mm",
                                cuencaName, promedio);
                        }
                    }

                    if (shouldSend)
                    {
                        _activeAlerts[cuencaName] = DateTime.UtcNow;

                        var messageText = BuildAlertMessage(cuencaName, promedio, stationDetails);
                        await SendChatAlertAsync(cuencaName, promedio, messageText);
                        await SendPushAlertAsync(cuencaName, promedio, stationDetails.Count(s => s.ConDato));
                    }
                }
            }

            // 4. Clear alerts for cuencas that dropped below threshold
            var cuencasToRemove = _activeAlerts.Keys
                .Where(k => !alertedCuencas.Contains(k))
                .ToList();

            foreach (var cuenca in cuencasToRemove)
            {
                _activeAlerts.Remove(cuenca);
                _logger.LogInformation(
                    "PrecipitationAlert: condition cleared for cuenca '{Cuenca}'", cuenca);

                // Send "all clear" message to chat
                var clearMsg = $"✅ Cuenca {cuenca}: precipitación promedio 24h ha bajado por debajo de {_thresholdMm} mm. Condición normalizada.";
                await SendChatAlertAsync(cuenca, 0, clearMsg, isResolved: true);
            }

            _logger.LogInformation(
                "PrecipitationAlert: Evaluation complete. Active alerts: {Count} cuencas",
                _activeAlerts.Count);
        }

        private string BuildAlertMessage(string cuencaName, float promedio, List<StationPrecipDetail> stations)
        {
            var stationsWithData = stations
                .Where(s => s.ConDato)
                .OrderByDescending(s => s.PrecipMm)
                .ToList();

            var maxPrecip = stationsWithData.FirstOrDefault()?.PrecipMm ?? 0;
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"));

            var lines = new List<string>
            {
                $"🌧️ ALERTA PRECIPITACIÓN - Cuenca: {cuencaName}",
                $"📅 {localTime:dd/MMM/yyyy HH:mm} hrs (hora centro)",
                $"📊 Promedio 24h: {promedio:F1} mm | Máximo: {maxPrecip:F1} mm",
                $"📡 Estaciones reportando: {stationsWithData.Count}/{stations.Count}",
                "",
                "📋 Detalle por estación (últimas 24h):",
                "─────────────────────────────"
            };

            // Group by subcuenca
            var bySubcuenca = stationsWithData
                .GroupBy(s => string.IsNullOrEmpty(s.Subcuenca) ? "(Sin subcuenca)" : s.Subcuenca);

            foreach (var subGroup in bySubcuenca.OrderBy(g => g.Key))
            {
                lines.Add($"  🏔️ {subGroup.Key}:");
                foreach (var st in subGroup.OrderByDescending(s => s.PrecipMm))
                {
                    var bar = st.PrecipMm >= 10 ? "🔴" : st.PrecipMm >= 5 ? "🟠" : st.PrecipMm >= 2 ? "🟡" : "🟢";
                    lines.Add($"    {bar} {st.Nombre} ({st.IdAsignado}): {st.PrecipMm:F1} mm");
                }
            }

            // Also list stations without data
            var sinDato = stations.Where(s => !s.ConDato).ToList();
            if (sinDato.Count > 0)
            {
                lines.Add("");
                lines.Add($"⚠️ Sin dato ({sinDato.Count}): {string.Join(", ", sinDato.Select(s => s.Nombre))}");
            }

            return string.Join("\n", lines);
        }

        private async Task SendChatAlertAsync(string cuencaName, float promedio, string messageText, bool isResolved = false)
        {
            try
            {
                var msg = new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ChatId = Guid.NewGuid(),
                    Room = AlertRoom,
                    UserId = "system",
                    UserName = "Sistema-Alertas",
                    FullName = "Sistema de Alertas de Precipitación",
                    Message = messageText,
                    Timestamp = DateTime.UtcNow
                };

                // Persist to DB
                using var db = new SqlConnection(_sqlConn);
                await db.ExecuteAsync(@"
                    INSERT INTO ChatMessages (Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp,
                                              FileName, FileUrl, FileSize, FileType)
                    VALUES (@Id, @ChatId, @Room, @UserId, @UserName, @FullName, @Message, @Timestamp,
                            @FileName, @FileUrl, @FileSize, @FileType)", msg);

                // Broadcast via SignalR to alert room + general
                await _hubContext.Clients.Group(AlertRoom).SendAsync("ReceiveMessage", msg);

                // Also send a summary to "general" room
                var summaryMsg = new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ChatId = Guid.NewGuid(),
                    Room = "general",
                    UserId = "system",
                    UserName = "Sistema-Alertas",
                    FullName = "Sistema de Alertas de Precipitación",
                    Message = isResolved
                        ? $"✅ Cuenca {cuencaName}: condición de precipitación normalizada."
                        : $"🌧️ ALERTA: Cuenca {cuencaName} - Promedio 24h: {promedio:F1} mm. Ver detalles en sala #alertas-precipitacion",
                    Timestamp = DateTime.UtcNow
                };

                await db.ExecuteAsync(@"
                    INSERT INTO ChatMessages (Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp,
                                              FileName, FileUrl, FileSize, FileType)
                    VALUES (@Id, @ChatId, @Room, @UserId, @UserName, @FullName, @Message, @Timestamp,
                            @FileName, @FileUrl, @FileSize, @FileType)", summaryMsg);

                await _hubContext.Clients.Group("general").SendAsync("ReceiveMessage", summaryMsg);

                _logger.LogInformation("PrecipitationAlert: Chat message sent for cuenca '{Cuenca}'", cuencaName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending chat alert for cuenca '{Cuenca}'", cuencaName);
            }
        }

        private async Task SendPushAlertAsync(string cuencaName, float promedio, int estacionesReportando)
        {
            if (!_pushService.IsConfigured) return;

            try
            {
                var title = $"🌧️ Precipitación - {cuencaName}";
                var body = $"Promedio 24h: {promedio:F1} mm ({estacionesReportando} estaciones). Detalle en chat #alertas-precipitacion";
                var data = new Dictionary<string, string>
                {
                    ["type"] = "precipitation_alert",
                    ["room"] = AlertRoom,
                    ["cuenca"] = cuencaName,
                    ["promedio"] = promedio.ToString("F1")
                };

                // Send to "alertas" topic (all subscribed users)
                await _pushService.SendToTopicAsync("alertas", title, body, data);

                // Also send to all registered devices
                using var db = new SqlConnection(_sqlConn);
                var tokens = (await db.QueryAsync<string>(
                    "SELECT Token FROM DeviceTokens WHERE LastSeen > DATEADD(DAY, -30, GETUTCDATE())"))
                    .ToList();

                foreach (var token in tokens)
                {
                    await _pushService.SendToDeviceAsync(token, title, body, data);
                }

                _logger.LogInformation(
                    "PrecipitationAlert: Push sent for cuenca '{Cuenca}' to {Count} devices",
                    cuencaName, tokens.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending push alert for cuenca '{Cuenca}'", cuencaName);
            }
        }

        // DTOs
        private class StationCuenca
        {
            public string? IdAsignado { get; set; }
            public string? Nombre { get; set; }
            public string? Cuenca { get; set; }
            public string? Subcuenca { get; set; }
        }

        private class StationPrecipDetail
        {
            public string IdAsignado { get; set; } = "";
            public string Nombre { get; set; } = "";
            public string Subcuenca { get; set; } = "";
            public float PrecipMm { get; set; }
            public bool ConDato { get; set; }
        }
    }
}
