using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Models;
using CloudStationWeb.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CloudStationWeb.Controllers
{
    [Authorize(Roles = "SuperAdmin,Administrador")]
    public class NotificationController : Controller
    {
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<NotificationController> _logger;
        private readonly string _sqlConn;
        private readonly string _pgConn;

        public NotificationController(
            IEmailSender emailSender,
            IConfiguration config,
            UserManager<ApplicationUser> userManager,
            ILogger<NotificationController> logger)
        {
            _emailSender = emailSender;
            _config = config;
            _userManager = userManager;
            _logger = logger;
            _sqlConn = config.GetConnectionString("SqlServer")!;
            _pgConn = config.GetConnectionString("PostgreSQL")!;
        }

        // GET: /Notification
        public IActionResult Index()
        {
            ViewBag.SmtpHost = _config["Email:Smtp:Host"];
            ViewBag.SmtpPort = _config["Email:Smtp:Port"];
            ViewBag.SmtpFrom = _config["Email:Smtp:FromAddress"];
            ViewBag.SmtpSsl = _config["Email:Smtp:EnableSsl"];
            return View();
        }

        // GET: /Notification/GetRecipients
        [HttpGet]
        public async Task<IActionResult> GetRecipients()
        {
            var users = _userManager.Users
                .Where(u => u.Email != null && u.EmailConfirmed)
                .Select(u => new { u.Id, u.UserName, u.Email })
                .ToList();

            return Json(users);
        }

        // POST: /Notification/SendCustom
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendCustom([FromBody] SendMailRequest request)
        {
            try
            {
                if (request.Recipients == null || !request.Recipients.Any())
                    return Json(new { success = false, message = "No se especificaron destinatarios" });

                if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.HtmlBody))
                    return Json(new { success = false, message = "Asunto y cuerpo son requeridos" });

                var sent = 0;
                var errors = new List<string>();

                foreach (var email in request.Recipients)
                {
                    try
                    {
                        await _emailSender.SendEmailAsync(email, request.Subject, WrapHtmlTemplate(request.Subject, request.HtmlBody));
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{email}: {ex.Message}");
                        _logger.LogError(ex, "Error enviando a {Email}", email);
                    }
                }

                return Json(new { success = true, sent, errors });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en SendCustom");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Notification/SendAlert
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendAlert([FromBody] AlertNotificationRequest request)
        {
            try
            {
                // Get station info
                string stationName = request.StationName ?? "Estación desconocida";
                string variable = request.Variable ?? "nivel";
                double value = request.Value;
                double threshold = request.Threshold;
                string timestamp = request.Timestamp ?? DateTime.Now.ToString("dd/MMM/yyyy HH:mm");

                string alertColor = value >= threshold ? "#e74c3c" : "#f39c12";
                string alertLevel = value >= threshold ? "CRÍTICA" : "ADVERTENCIA";

                string subject = $"⚠️ Alerta {alertLevel}: {stationName} - {variable}";
                string htmlBody = $@"
                    <div style='text-align:center; margin-bottom:20px;'>
                        <span style='display:inline-block; background:{alertColor}; color:white; padding:8px 24px; border-radius:4px; font-size:18px; font-weight:bold;'>
                            ALERTA {alertLevel}
                        </span>
                    </div>
                    <table style='width:100%; border-collapse:collapse; margin:16px 0;'>
                        <tr style='border-bottom:1px solid #ddd;'>
                            <td style='padding:10px; font-weight:bold; width:40%;'>Estación</td>
                            <td style='padding:10px;'>{stationName}</td>
                        </tr>
                        <tr style='border-bottom:1px solid #ddd; background:#f9f9f9;'>
                            <td style='padding:10px; font-weight:bold;'>Variable</td>
                            <td style='padding:10px;'>{variable}</td>
                        </tr>
                        <tr style='border-bottom:1px solid #ddd;'>
                            <td style='padding:10px; font-weight:bold;'>Valor Registrado</td>
                            <td style='padding:10px; color:{alertColor}; font-size:20px; font-weight:bold;'>{value:F2}</td>
                        </tr>
                        <tr style='border-bottom:1px solid #ddd; background:#f9f9f9;'>
                            <td style='padding:10px; font-weight:bold;'>Umbral</td>
                            <td style='padding:10px;'>{threshold:F2}</td>
                        </tr>
                        <tr>
                            <td style='padding:10px; font-weight:bold;'>Fecha/Hora</td>
                            <td style='padding:10px;'>{timestamp}</td>
                        </tr>
                    </table>
                    <p style='color:#666; font-size:13px; margin-top:20px;'>
                        Este es un mensaje automático del sistema de monitoreo. No responda a este correo.
                    </p>";

                // Get all confirmed users
                var recipients = _userManager.Users
                    .Where(u => u.Email != null && u.EmailConfirmed)
                    .Select(u => u.Email!)
                    .ToList();

                if (!recipients.Any())
                    return Json(new { success = false, message = "No hay destinatarios con email confirmado" });

                var sent = 0;
                foreach (var email in recipients)
                {
                    try
                    {
                        await _emailSender.SendEmailAsync(email, subject, WrapHtmlTemplate(subject, htmlBody));
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error enviando alerta a {Email}", email);
                    }
                }

                return Json(new { success = true, sent, total = recipients.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en SendAlert");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Notification/SendTestEmail
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendTestEmail()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user?.Email == null)
                    return Json(new { success = false, message = "Usuario actual no tiene email" });

                string subject = "✅ Prueba de correo - PIH";
                string body = @"
                    <p>Este es un correo de prueba enviado desde la <strong>Plataforma Integral Hidrometeorológica</strong>.</p>
                    <p>Si recibes este mensaje, la configuración SMTP está funcionando correctamente.</p>
                    <p style='color:#666; font-size:13px;'>Fecha: " + DateTime.Now.ToString("dd/MMM/yyyy HH:mm:ss") + "</p>";

                await _emailSender.SendEmailAsync(user.Email, subject, WrapHtmlTemplate(subject, body));

                return Json(new { success = true, message = $"Correo de prueba enviado a {user.Email}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en SendTestEmail");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Notification/GetDamStatus
        [HttpGet]
        public async Task<IActionResult> GetDamStatus()
        {
            try
            {
                using var db = new NpgsqlConnection(_pgConn);
                var data = await db.QueryAsync<dynamic>(@"
                    SELECT dcp_id, variable, valor, ts
                    FROM public.ultimas_mediciones
                    WHERE variable ILIKE '%nivel%'
                    ORDER BY ts DESC
                    LIMIT 20");

                return Json(new { success = true, data });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string WrapHtmlTemplate(string title, string bodyContent)
        {
            return $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='font-family: Arial, sans-serif; background:#f4f4f4; padding:20px;'>
  <div style='max-width:600px; margin:0 auto; background:white; border-radius:8px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.1);'>
    <div style='background:#1a252f; color:white; padding:20px; text-align:center;'>
      <h2 style='margin:0;'>🌊 Plataforma Integral Hidrometeorológica</h2>
      <p style='margin:4px 0 0; opacity:0.8; font-size:13px;'>Sistema Cuenca Grijalva</p>
    </div>
    <div style='padding:24px;'>
      <h3 style='color:#1a252f; margin-top:0;'>{title}</h3>
      {bodyContent}
    </div>
    <div style='background:#f8f8f8; padding:12px 24px; text-align:center; font-size:12px; color:#999;'>
      CFE — Subgerencia Regional de Generación Hidroeléctrica Grijalva
    </div>
  </div>
</body>
</html>";
        }
    }

    public class SendMailRequest
    {
        public List<string> Recipients { get; set; } = new();
        public string Subject { get; set; } = "";
        public string HtmlBody { get; set; } = "";
    }

    public class AlertNotificationRequest
    {
        public string? StationName { get; set; }
        public string? Variable { get; set; }
        public double Value { get; set; }
        public double Threshold { get; set; }
        public string? Timestamp { get; set; }
    }
}
