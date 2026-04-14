using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CloudStationWeb.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SettingsController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public SettingsController(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        public IActionResult Index()
        {
            var model = new SettingsViewModel
            {
                // Connection Strings
                SqlServer = _config.GetConnectionString("SqlServer") ?? "",
                PostgreSQL = _config.GetConnectionString("PostgreSQL") ?? "",

                // Email
                SmtpHost = _config["Email:Smtp:Host"] ?? "",
                SmtpPort = int.TryParse(_config["Email:Smtp:Port"], out var p) ? p : 587,
                SmtpUsername = _config["Email:Smtp:Username"] ?? "",
                SmtpPassword = _config["Email:Smtp:Password"] ?? "",
                SmtpFrom = _config["Email:Smtp:From"] ?? "",
                SmtpFromName = _config["Email:Smtp:FromName"] ?? "",
                SmtpEnableSsl = bool.TryParse(_config["Email:Smtp:EnableSsl"], out var ssl) && ssl,

                // Firebase
                FirebaseCredentialsPath = _config["Firebase:CredentialsPath"] ?? "",

                // Gemini
                GeminiApiKey = _config["Gemini:ApiKey"] ?? "",
                GeminiModel = _config["Gemini:Model"] ?? "",

                // DeepSeek
                DeepSeekApiKey = _config["DeepSeek:ApiKey"] ?? "",
                DeepSeekModel = _config["DeepSeek:Model"] ?? "",
                DeepSeekEndpoint = _config["DeepSeek:Endpoint"] ?? "",

                // Azure OpenAI
                AzureOpenAIEndpoint = _config["AzureOpenAI:Endpoint"] ?? "",
                AzureOpenAIApiKey = _config["AzureOpenAI:ApiKey"] ?? "",
                AzureOpenAIDeployment = _config["AzureOpenAI:DeploymentName"] ?? "",
                AzureOpenAIApiVersion = _config["AzureOpenAI:ApiVersion"] ?? "",

                // ImageStore (Local)
                ImageStorePath = _config["ImageStore:Path"] ?? "",
                ImageStoreApiKey = _config["ImageStore:ApiKey"] ?? "",
                ImageStoreBaseUrl = _config["ImageStore:BaseUrl"] ?? "",

                // Early Warning
                EarlyWarningEnabled = bool.TryParse(_config["EarlyWarning:Enabled"], out var ew) && ew,
                EarlyWarningIntervalSeconds = int.TryParse(_config["EarlyWarning:IntervalSeconds"], out var ei) ? ei : 300,
                EarlyWarningCooldownMinutes = int.TryParse(_config["EarlyWarning:CooldownMinutes"], out var ec) ? ec : 60,

                // JWT
                JwtKey = _config["Jwt:Key"] ?? "",
                JwtIssuer = _config["Jwt:Issuer"] ?? "",
                JwtAudience = _config["Jwt:Audience"] ?? "",
                JwtExpireHours = int.TryParse(_config["Jwt:ExpireHours"], out var jh) ? jh : 24
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Save([FromBody] SettingsViewModel model)
        {
            try
            {
                var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
                if (!System.IO.File.Exists(appSettingsPath))
                    return Json(new { success = false, message = "appsettings.json not found" });

                var json = System.IO.File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Build new settings preserving structure
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
                var newJson = new Dictionary<string, object>();

                // Copy all existing keys
                foreach (var kv in dict)
                {
                    newJson[kv.Key] = kv.Value;
                }

                // Override sections
                newJson["ConnectionStrings"] = new Dictionary<string, string>
                {
                    ["SqlServer"] = model.SqlServer,
                    ["PostgreSQL"] = model.PostgreSQL
                };

                newJson["Email"] = new Dictionary<string, object>
                {
                    ["Smtp"] = new Dictionary<string, object>
                    {
                        ["Host"] = model.SmtpHost,
                        ["Port"] = model.SmtpPort,
                        ["Username"] = model.SmtpUsername,
                        ["Password"] = model.SmtpPassword,
                        ["From"] = model.SmtpFrom,
                        ["FromName"] = model.SmtpFromName,
                        ["EnableSsl"] = model.SmtpEnableSsl
                    }
                };

                newJson["Firebase"] = new Dictionary<string, string>
                {
                    ["CredentialsPath"] = model.FirebaseCredentialsPath
                };

                newJson["Gemini"] = new Dictionary<string, string>
                {
                    ["ApiKey"] = model.GeminiApiKey,
                    ["Model"] = model.GeminiModel
                };

                newJson["DeepSeek"] = new Dictionary<string, string>
                {
                    ["ApiKey"] = model.DeepSeekApiKey,
                    ["Model"] = model.DeepSeekModel,
                    ["Endpoint"] = model.DeepSeekEndpoint
                };

                newJson["AzureOpenAI"] = new Dictionary<string, string>
                {
                    ["Endpoint"] = model.AzureOpenAIEndpoint,
                    ["ApiKey"] = model.AzureOpenAIApiKey,
                    ["DeploymentName"] = model.AzureOpenAIDeployment,
                    ["ApiVersion"] = model.AzureOpenAIApiVersion
                };

                newJson["ImageStore"] = new Dictionary<string, string>
                {
                    ["Path"] = model.ImageStorePath,
                    ["ApiKey"] = model.ImageStoreApiKey,
                    ["BaseUrl"] = model.ImageStoreBaseUrl
                };

                newJson["EarlyWarning"] = new Dictionary<string, object>
                {
                    ["Enabled"] = model.EarlyWarningEnabled,
                    ["IntervalSeconds"] = model.EarlyWarningIntervalSeconds,
                    ["CooldownMinutes"] = model.EarlyWarningCooldownMinutes
                };

                newJson["Jwt"] = new Dictionary<string, object>
                {
                    ["Key"] = model.JwtKey,
                    ["Issuer"] = model.JwtIssuer,
                    ["Audience"] = model.JwtAudience,
                    ["ExpireHours"] = model.JwtExpireHours
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                var newJsonStr = JsonSerializer.Serialize(newJson, options);
                System.IO.File.WriteAllText(appSettingsPath, newJsonStr);

                return Json(new { success = true, message = "Configuración guardada. Reinicie la aplicación para aplicar los cambios." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult TestSmtp([FromBody] SmtpTestRequest req)
        {
            try
            {
                using var client = new System.Net.Mail.SmtpClient(req.Host, req.Port)
                {
                    Credentials = new System.Net.NetworkCredential(req.Username, req.Password),
                    EnableSsl = req.EnableSsl,
                    Timeout = 10000
                };

                var message = new System.Net.Mail.MailMessage
                {
                    From = new System.Net.Mail.MailAddress(req.From, req.FromName),
                    Subject = "Test - Plataforma Integral Hidrometeorológica",
                    Body = "<h3>Prueba de correo</h3><p>Si recibes este mensaje, la configuración SMTP es correcta.</p>",
                    IsBodyHtml = true
                };
                message.To.Add(req.Username);
                client.Send(message);

                return Json(new { success = true, message = "Correo de prueba enviado correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }

    public class SettingsViewModel
    {
        // Connection Strings
        public string SqlServer { get; set; } = "";
        public string PostgreSQL { get; set; } = "";

        // SMTP
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public string SmtpUsername { get; set; } = "";
        public string SmtpPassword { get; set; } = "";
        public string SmtpFrom { get; set; } = "";
        public string SmtpFromName { get; set; } = "";
        public bool SmtpEnableSsl { get; set; }

        // Firebase
        public string FirebaseCredentialsPath { get; set; } = "";

        // Gemini
        public string GeminiApiKey { get; set; } = "";
        public string GeminiModel { get; set; } = "";

        // DeepSeek
        public string DeepSeekApiKey { get; set; } = "";
        public string DeepSeekModel { get; set; } = "";
        public string DeepSeekEndpoint { get; set; } = "";

        // Azure OpenAI
        public string AzureOpenAIEndpoint { get; set; } = "";
        public string AzureOpenAIApiKey { get; set; } = "";
        public string AzureOpenAIDeployment { get; set; } = "";
        public string AzureOpenAIApiVersion { get; set; } = "";

        // ImageStore (Local)
        public string ImageStorePath { get; set; } = "";
        public string ImageStoreApiKey { get; set; } = "";
        public string ImageStoreBaseUrl { get; set; } = "";

        // Early Warning
        public bool EarlyWarningEnabled { get; set; }
        public int EarlyWarningIntervalSeconds { get; set; } = 300;
        public int EarlyWarningCooldownMinutes { get; set; } = 60;

        // JWT
        public string JwtKey { get; set; } = "";
        public string JwtIssuer { get; set; } = "";
        public string JwtAudience { get; set; } = "";
        public int JwtExpireHours { get; set; } = 24;
    }

    public class SmtpTestRequest
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string From { get; set; } = "";
        public string FromName { get; set; } = "";
        public bool EnableSsl { get; set; }
    }
}
