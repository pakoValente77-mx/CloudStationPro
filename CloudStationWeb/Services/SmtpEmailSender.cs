using System.Net;
using System.Net.Mail;

namespace CloudStationWeb.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            try
            {
                var smtpSection = _config.GetSection("Email:Smtp");
                var host = smtpSection["Host"];
                if (!int.TryParse(smtpSection["Port"], out var port)) port = 587;
                var username = smtpSection["Username"];
                var password = smtpSection["Password"];
                var fromEmail = smtpSection["From"] ?? username;
                var fromName = smtpSection["FromName"] ?? "Plataforma Integral Hidrometeorológica";
                if (!bool.TryParse(smtpSection["EnableSsl"], out var enableSsl)) enableSsl = true; // FIX CVE-I2: default seguro = TLS habilitado

                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username))
                {
                    _logger.LogWarning("SMTP not configured. Email not sent to {Email}. Subject: {Subject}", toEmail, subject);
                    return;
                }

                using var client = new SmtpClient(host, port)
                {
                    Credentials = new NetworkCredential(username, password),
                    EnableSsl = enableSsl,
                    Timeout = 15000
                };

                var message = new MailMessage
                {
                    From = new MailAddress(fromEmail!, fromName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                message.To.Add(toEmail);

                await client.SendMailAsync(message);
                _logger.LogInformation("Email sent successfully to {Email}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            }
        }
    }
}
