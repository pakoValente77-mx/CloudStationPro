using CloudStationWeb.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CloudStationWeb.Services
{
    /// <summary>
    /// Background service that periodically evaluates sensor readings against configured
    /// thresholds (UmbralAlertas) and sends email alerts automatically.
    /// Inspired by grijalva-early-warning-service watcher logic.
    /// </summary>
    public class EarlyWarningService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<EarlyWarningService> _logger;

        // Default: check every 5 minutes
        private int _intervalSeconds = 300;
        // Don't re-alert same threshold within cooldown period (default 60 min)
        private int _cooldownMinutes = 60;
        private bool _enabled = true;

        // Track last alert time per threshold to avoid spam
        private readonly Dictionary<long, DateTime> _lastAlertTime = new();

        public EarlyWarningService(
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            ILogger<EarlyWarningService> logger)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;

            _intervalSeconds = config.GetValue("EarlyWarning:IntervalSeconds", 300);
            _cooldownMinutes = config.GetValue("EarlyWarning:CooldownMinutes", 60);
            _enabled = config.GetValue("EarlyWarning:Enabled", true);
        }

        public EarlyWarningConfigDto GetConfig() => new()
        {
            Enabled = _enabled,
            IntervalSeconds = _intervalSeconds,
            CooldownMinutes = _cooldownMinutes
        };

        public void UpdateConfig(bool enabled, int intervalSeconds, int cooldownMinutes)
        {
            _enabled = enabled;
            _intervalSeconds = Math.Max(30, intervalSeconds);
            _cooldownMinutes = Math.Max(1, cooldownMinutes);
            _logger.LogInformation("EarlyWarning config updated: enabled={E}, interval={I}s, cooldown={C}min",
                _enabled, _intervalSeconds, _cooldownMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("EarlyWarningService started. Interval={I}s, Cooldown={C}min",
                _intervalSeconds, _cooldownMinutes);

            // Wait a bit for the app to fully start
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_enabled)
                {
                    try
                    {
                        await EvaluateThresholdsAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in EarlyWarning evaluation cycle");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
            }
        }

        private async Task EvaluateThresholdsAsync(CancellationToken ct)
        {
            var sqlConn = _config.GetConnectionString("SqlServer")!;
            var pgConn = _config.GetConnectionString("PostgreSQL")!;

            // 1. Get all active thresholds with their station/sensor context from SQL Server
            List<UmbralConContexto> thresholds;
            using (var db = new SqlConnection(sqlConn))
            {
                thresholds = (await db.QueryAsync<UmbralConContexto>(@"
                    SELECT u.Id, u.IdSensor, u.Umbral, u.Operador, u.Nombre, u.Activo, u.Periodo,
                           s.Id AS IdEstacion, s.Nombre AS NombreSensor, s.Variable,
                           e.Id AS IdEstacion, e.Nombre AS NombreEstacion, e.IdSatelital AS DcpId
                    FROM UmbralAlertas u
                    INNER JOIN Sensor s ON u.IdSensor = s.Id
                    INNER JOIN Estacion e ON s.IdEstacion = e.Id
                    WHERE u.Activo = 1 AND e.Activo = 1
                ")).ToList();
            }

            if (thresholds.Count == 0) return;

            // 2. Get latest readings from PostgreSQL (ultimas_mediciones)
            Dictionary<string, (decimal valor, DateTime ts)> latestReadings;
            using (var pg = new NpgsqlConnection(pgConn))
            {
                var rows = await pg.QueryAsync<dynamic>(@"
                    SELECT dcp_id, variable, valor, ts 
                    FROM public.ultimas_mediciones
                    WHERE ts > NOW() - INTERVAL '2 hours'
                ");
                latestReadings = rows
                    .GroupBy(r => $"{(string)r.dcp_id}|{(string)r.variable}")
                    .ToDictionary(
                        g => g.Key,
                        g => (valor: (decimal)Convert.ToDecimal(g.First().valor),
                              ts: (DateTime)g.First().ts)
                    );
            }

            // 3. Evaluate each threshold
            var triggeredAlerts = new List<AlertRecord>();
            var now = DateTime.UtcNow;

            foreach (var t in thresholds)
            {
                if (t.Umbral == null || string.IsNullOrEmpty(t.DcpId)) continue;

                // Find matching reading
                var key = $"{t.DcpId}|{t.Variable ?? ""}";
                if (!latestReadings.TryGetValue(key, out var reading)) continue;

                // Evaluate operator
                bool triggered = t.Operador switch
                {
                    ">=" => reading.valor >= t.Umbral.Value,
                    ">" => reading.valor > t.Umbral.Value,
                    "<=" => reading.valor <= t.Umbral.Value,
                    "<" => reading.valor < t.Umbral.Value,
                    "=" => reading.valor == t.Umbral.Value,
                    _ => reading.valor >= t.Umbral.Value // default: >=
                };

                if (!triggered) continue;

                // Cooldown check: don't re-alert within the cooldown period
                var cooldown = t.Periodo > 0 ? t.Periodo.Value : _cooldownMinutes;
                if (_lastAlertTime.TryGetValue(t.Id, out var lastTime) &&
                    (now - lastTime).TotalMinutes < cooldown)
                {
                    continue;
                }

                var nivel = t.Operador?.Contains(">") == true
                    ? (reading.valor >= t.Umbral.Value * 1.1m ? "CRÍTICA" : "ADVERTENCIA")
                    : (reading.valor <= t.Umbral.Value * 0.9m ? "CRÍTICA" : "ADVERTENCIA");

                triggeredAlerts.Add(new AlertRecord
                {
                    IdSensor = t.IdSensor,
                    IdUmbral = t.Id,
                    NombreEstacion = t.NombreEstacion,
                    NombreSensor = t.NombreSensor,
                    NombreUmbral = t.Nombre,
                    Variable = t.Variable,
                    ValorMedido = reading.valor,
                    ValorUmbral = t.Umbral.Value,
                    Operador = t.Operador,
                    Nivel = nivel,
                    FechaAlerta = now
                });

                _lastAlertTime[t.Id] = now;
            }

            if (triggeredAlerts.Count == 0)
            {
                _logger.LogDebug("EarlyWarning cycle: {Count} thresholds evaluated, no alerts triggered", thresholds.Count);
                return;
            }

            _logger.LogWarning("EarlyWarning: {Count} alert(s) triggered!", triggeredAlerts.Count);

            // 4. Send alerts
            using var scope = _scopeFactory.CreateScope();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var pushService = scope.ServiceProvider.GetService<PushNotificationService>();

            // Get all users with confirmed email
            var recipients = userManager.Users
                .Where(u => u.Email != null && u.EmailConfirmed)
                .Select(u => u.Email!)
                .ToList();

            foreach (var alert in triggeredAlerts)
            {
                var subject = $"⚠️ Alerta {alert.Nivel}: {alert.NombreEstacion} - {alert.Variable}";
                var htmlBody = BuildAlertHtml(alert);

                var sent = 0;
                var errors = new List<string>();
                foreach (var email in recipients)
                {
                    try
                    {
                        await emailSender.SendEmailAsync(email, subject, htmlBody);
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{email}: {ex.Message}");
                    }
                }

                alert.Enviada = true;
                alert.FechaEnvio = DateTime.UtcNow;
                alert.CorreosEnviados = sent;

                _logger.LogWarning("Alert sent: {Station}/{Variable} = {Value} ({Operator} {Threshold}) → {Sent} emails",
                    alert.NombreEstacion, alert.Variable, alert.ValorMedido,
                    alert.Operador, alert.ValorUmbral, sent);

                // Send push notification if Firebase is configured
                if (pushService != null)
                {
                    try
                    {
                        await pushService.SendToTopicAsync(
                            "alertas",
                            $"⚠️ {alert.Nivel}: {alert.NombreEstacion}",
                            $"{alert.Variable}: {alert.ValorMedido} ({alert.Operador} {alert.ValorUmbral})"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending push notification for alert");
                    }
                }
            }

            // 5. Persist alert records to SQL Server
            try
            {
                using var db = new SqlConnection(sqlConn);
                foreach (var alert in triggeredAlerts)
                {
                    await db.ExecuteAsync(@"
                        INSERT INTO AlertRecord 
                            (IdSensor, IdUmbral, NombreEstacion, NombreSensor, NombreUmbral, 
                             Variable, ValorMedido, ValorUmbral, Operador, Nivel,
                             FechaAlerta, FechaEnvio, CorreosEnviados, Enviada)
                        VALUES 
                            (@IdSensor, @IdUmbral, @NombreEstacion, @NombreSensor, @NombreUmbral,
                             @Variable, @ValorMedido, @ValorUmbral, @Operador, @Nivel,
                             @FechaAlerta, @FechaEnvio, @CorreosEnviados, @Enviada)", alert);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error persisting alert records");
            }
        }

        private static string BuildAlertHtml(AlertRecord alert)
        {
            var color = alert.Nivel == "CRÍTICA" ? "#e74c3c" : "#f39c12";
            return $@"
            <div style='font-family:Arial,sans-serif; max-width:600px; margin:0 auto;'>
                <div style='background:{color}; color:#fff; padding:16px 20px; border-radius:8px 8px 0 0;'>
                    <h2 style='margin:0;'>⚠️ Alerta {alert.Nivel}</h2>
                </div>
                <div style='background:#1a1a2e; color:#e0e0e0; padding:20px; border-radius:0 0 8px 8px;'>
                    <table style='width:100%; border-collapse:collapse;'>
                        <tr><td style='padding:8px; color:#aaa;'>Estación:</td>
                            <td style='padding:8px; font-weight:bold;'>{alert.NombreEstacion}</td></tr>
                        <tr><td style='padding:8px; color:#aaa;'>Sensor:</td>
                            <td style='padding:8px;'>{alert.NombreSensor}</td></tr>
                        <tr><td style='padding:8px; color:#aaa;'>Variable:</td>
                            <td style='padding:8px;'>{alert.Variable}</td></tr>
                        <tr><td style='padding:8px; color:#aaa;'>Valor Medido:</td>
                            <td style='padding:8px; font-size:1.3em; color:{color}; font-weight:bold;'>{alert.ValorMedido}</td></tr>
                        <tr><td style='padding:8px; color:#aaa;'>Umbral ({alert.Operador}):</td>
                            <td style='padding:8px;'>{alert.ValorUmbral} ({alert.NombreUmbral})</td></tr>
                        <tr><td style='padding:8px; color:#aaa;'>Fecha/Hora:</td>
                            <td style='padding:8px;'>{alert.FechaAlerta:dd/MMM/yyyy HH:mm} UTC</td></tr>
                    </table>
                    <p style='margin-top:16px; font-size:0.85em; color:#888;'>
                        Este correo fue generado automáticamente por el Sistema de Alertas Tempranas — PIH Grijalva
                    </p>
                </div>
            </div>";
        }
    }
}
