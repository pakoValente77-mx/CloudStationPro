using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CloudStationWeb.Hubs;
using CloudStationWeb.Services;
using System.Text.Json;

namespace CloudStationWeb.Controllers
{
    /// <summary>
    /// Webhook para integrar Centinela con Telegram, WhatsApp y otros servicios.
    /// - POST /api/webhook/telegram   → Recibe updates de Telegram Bot API
    /// - POST /api/webhook/whatsapp   → Recibe mensajes de WhatsApp Business API
    /// - POST /api/webhook/send       → Enviar mensaje desde servicio externo al chat
    /// - GET  /api/webhook/telegram   → Verificación de webhook de Telegram
    /// </summary>
    [Route("api/webhook")]
    [ApiController]
    public class WebhookController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly CentinelaBotService _centinelaBot;
        private readonly ILogger<WebhookController> _logger;
        private readonly string _apiKey;
        private readonly string _telegramToken;
        private readonly string _telegramChatId;
        private readonly string _imageStoreRoot;
        private readonly string _baseUrl;

        public WebhookController(
            IConfiguration config,
            IHubContext<ChatHub> hubContext,
            CentinelaBotService centinelaBot,
            ILogger<WebhookController> logger)
        {
            _config = config;
            _hubContext = hubContext;
            _centinelaBot = centinelaBot;
            _logger = logger;
            _apiKey = config["ImageStore:ApiKey"] ?? "pih-default-key-change-me";
            _telegramToken = config["Webhook:Telegram:BotToken"] ?? "";
            _telegramChatId = config["Webhook:Telegram:DefaultChatId"] ?? "";
            _imageStoreRoot = config["ImageStore:Path"] ?? Path.Combine(AppContext.BaseDirectory, "ImageStore");
            _baseUrl = config["ImageStore:BaseUrl"] ?? "";
        }

        // ==================== TELEGRAM ====================

        /// <summary>
        /// Verificación de webhook de Telegram (GET).
        /// </summary>
        [HttpGet("telegram")]
        public IActionResult TelegramVerify()
        {
            return Ok("Webhook activo");
        }

        /// <summary>
        /// Recibe updates de Telegram Bot API.
        /// POST /api/webhook/telegram
        /// </summary>
        [HttpPost("telegram")]
        public async Task<IActionResult> TelegramUpdate()
        {
            if (string.IsNullOrEmpty(_telegramToken))
                return Ok(); // Silently ignore if not configured

            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                var update = JsonSerializer.Deserialize<JsonElement>(body);

                if (!update.TryGetProperty("message", out var message))
                    return Ok();

                var chatId = message.GetProperty("chat").GetProperty("id").GetInt64().ToString();
                var text = message.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                var fromUser = message.TryGetProperty("from", out var from)
                    ? from.TryGetProperty("first_name", out var fn) ? fn.GetString() ?? "Telegram" : "Telegram"
                    : "Telegram";

                _logger.LogInformation("[Webhook/Telegram] From {User}: {Text}", fromUser, text);

                if (string.IsNullOrWhiteSpace(text))
                    return Ok();

                // Procesar con Centinela
                var response = await _centinelaBot.ProcessMessageAsync(text, $"telegram:{fromUser}");

                // Responder a Telegram
                await SendTelegramMessage(chatId, response.Message, response.FileUrl);

                // También enviar al chat interno de PIH
                await BroadcastToChatAsync("telegram", fromUser, text, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Webhook/Telegram] Error processing update");
            }

            return Ok();
        }

        // ==================== WHATSAPP ====================

        /// <summary>
        /// Verificación de webhook de WhatsApp Business API (GET).
        /// </summary>
        [HttpGet("whatsapp")]
        public IActionResult WhatsAppVerify([FromQuery(Name = "hub.mode")] string? mode,
                                             [FromQuery(Name = "hub.verify_token")] string? token,
                                             [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            var verifyToken = _config["Webhook:WhatsApp:VerifyToken"] ?? "";
            if (mode == "subscribe" && token == verifyToken)
                return Ok(challenge);
            return Forbid();
        }

        /// <summary>
        /// Recibe mensajes de WhatsApp Business API (Cloud API).
        /// POST /api/webhook/whatsapp
        /// </summary>
        [HttpPost("whatsapp")]
        public async Task<IActionResult> WhatsAppUpdate()
        {
            var whatsAppToken = _config["Webhook:WhatsApp:AccessToken"] ?? "";
            if (string.IsNullOrEmpty(whatsAppToken))
                return Ok();

            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                var payload = JsonSerializer.Deserialize<JsonElement>(body);

                // Navegar la estructura de Cloud API
                if (!payload.TryGetProperty("entry", out var entries))
                    return Ok();

                foreach (var entry in entries.EnumerateArray())
                {
                    if (!entry.TryGetProperty("changes", out var changes))
                        continue;

                    foreach (var change in changes.EnumerateArray())
                    {
                        if (!change.TryGetProperty("value", out var value))
                            continue;
                        if (!value.TryGetProperty("messages", out var messages))
                            continue;

                        foreach (var msg in messages.EnumerateArray())
                        {
                            var msgType = msg.TryGetProperty("type", out var mt) ? mt.GetString() : "";
                            if (msgType != "text") continue;

                            var text = msg.GetProperty("text").GetProperty("body").GetString() ?? "";
                            var phone = msg.GetProperty("from").GetString() ?? "";
                            var phoneId = value.TryGetProperty("metadata", out var meta)
                                ? meta.GetProperty("phone_number_id").GetString() ?? ""
                                : "";

                            _logger.LogInformation("[Webhook/WhatsApp] From {Phone}: {Text}", phone, text);

                            // Procesar con Centinela
                            var response = await _centinelaBot.ProcessMessageAsync(text, $"whatsapp:{phone}");

                            // Responder a WhatsApp
                            await SendWhatsAppMessage(whatsAppToken, phoneId, phone, response.Message, response.FileUrl);

                            // También enviar al chat interno
                            await BroadcastToChatAsync("whatsapp", phone, text, response);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Webhook/WhatsApp] Error processing update");
            }

            return Ok();
        }

        // ==================== API GENÉRICA ====================

        /// <summary>
        /// Enviar mensaje al chat de Centinela desde servicio externo.
        /// POST /api/webhook/send
        /// Body: { "message": "texto", "imageUrl": "opcional", "room": "centinela" }
        /// Header: X-Api-Key
        /// </summary>
        [HttpPost("send")]
        public async Task<IActionResult> SendFromExternal([FromBody] ExternalMessage msg)
        {
            if (!Request.Headers.TryGetValue("X-Api-Key", out var key) ||
                !string.Equals(key, _apiKey, StringComparison.Ordinal))
                return Unauthorized(new { error = "API key inválida" });

            if (string.IsNullOrWhiteSpace(msg.Message))
                return BadRequest(new { error = "Se requiere 'message'" });

            var room = msg.Room ?? CentinelaBotService.BotRoom;
            var source = msg.Source ?? "external";

            var chatMsg = new
            {
                Id = Guid.NewGuid(),
                ChatId = Guid.NewGuid(),
                Room = room,
                UserId = $"webhook-{source}",
                UserName = msg.UserName ?? source,
                FullName = msg.FullName ?? $"Webhook ({source})",
                Message = msg.Message,
                Timestamp = DateTime.UtcNow,
                FileName = msg.FileName,
                FileUrl = msg.ImageUrl,
                FileSize = (long?)null,
                FileType = !string.IsNullOrEmpty(msg.ImageUrl) ? "image/png" : (string?)null
            };

            await _hubContext.Clients.Group(room).SendAsync("ReceiveMessage", chatMsg);
            _logger.LogInformation("[Webhook/Send] {Source} → {Room}: {Msg}", source, room, msg.Message);

            return Ok(new { success = true });
        }

        // ==================== NOTIFICAR (push a Telegram/WhatsApp) ====================

        /// <summary>
        /// Enviar notificación a Telegram y/o WhatsApp desde servicio externo.
        /// POST /api/webhook/notify
        /// Body: { "message": "texto", "imageUrl": "opcional", "targets": ["telegram","whatsapp"] }
        /// Header: X-Api-Key
        /// </summary>
        [HttpPost("notify")]
        public async Task<IActionResult> Notify([FromBody] NotifyRequest req)
        {
            if (!Request.Headers.TryGetValue("X-Api-Key", out var key) ||
                !string.Equals(key, _apiKey, StringComparison.Ordinal))
                return Unauthorized(new { error = "API key inválida" });

            var results = new Dictionary<string, string>();
            var targets = req.Targets ?? new[] { "telegram" };

            foreach (var target in targets)
            {
                try
                {
                    switch (target.ToLower())
                    {
                        case "telegram":
                            if (!string.IsNullOrEmpty(_telegramToken) && !string.IsNullOrEmpty(_telegramChatId))
                            {
                                await SendTelegramMessage(_telegramChatId, req.Message, req.ImageUrl);
                                results["telegram"] = "sent";
                            }
                            else results["telegram"] = "not configured";
                            break;

                        case "whatsapp":
                            var waToken = _config["Webhook:WhatsApp:AccessToken"] ?? "";
                            var waPhone = _config["Webhook:WhatsApp:DefaultPhoneId"] ?? "";
                            var waTo = _config["Webhook:WhatsApp:DefaultRecipient"] ?? "";
                            if (!string.IsNullOrEmpty(waToken) && !string.IsNullOrEmpty(waPhone))
                            {
                                await SendWhatsAppMessage(waToken, waPhone, waTo, req.Message, req.ImageUrl);
                                results["whatsapp"] = "sent";
                            }
                            else results["whatsapp"] = "not configured";
                            break;

                        default:
                            results[target] = "unknown target";
                            break;
                    }
                }
                catch (Exception ex)
                {
                    results[target] = $"error: {ex.Message}";
                }
            }

            return Ok(new { success = true, results });
        }

        // ==================== Helpers ====================

        private async Task SendTelegramMessage(string chatId, string text, string? imageUrl = null)
        {
            using var http = new HttpClient();

            if (!string.IsNullOrEmpty(imageUrl))
            {
                // Enviar foto con caption
                var url = $"https://api.telegram.org/bot{_telegramToken}/sendPhoto";
                var form = new MultipartFormDataContent
                {
                    { new StringContent(chatId), "chat_id" },
                    { new StringContent(imageUrl), "photo" },
                    { new StringContent(text), "caption" },
                    { new StringContent("HTML"), "parse_mode" }
                };
                await http.PostAsync(url, form);
            }
            else
            {
                var url = $"https://api.telegram.org/bot{_telegramToken}/sendMessage";
                var form = new MultipartFormDataContent
                {
                    { new StringContent(chatId), "chat_id" },
                    { new StringContent(text), "text" },
                    { new StringContent("HTML"), "parse_mode" }
                };
                await http.PostAsync(url, form);
            }
        }

        private async Task SendWhatsAppMessage(string accessToken, string phoneId, string to, string text, string? imageUrl = null)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            var url = $"https://graph.facebook.com/v18.0/{phoneId}/messages";

            object payload;
            if (!string.IsNullOrEmpty(imageUrl))
            {
                payload = new
                {
                    messaging_product = "whatsapp",
                    to,
                    type = "image",
                    image = new { link = imageUrl, caption = text }
                };
            }
            else
            {
                payload = new
                {
                    messaging_product = "whatsapp",
                    to,
                    type = "text",
                    text = new { body = text }
                };
            }

            var json = JsonSerializer.Serialize(payload);
            await http.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        }

        private async Task BroadcastToChatAsync(string platform, string user, string originalText, BotResponse response)
        {
            var room = CentinelaBotService.BotRoom;

            // Mensaje del usuario externo
            var userMsg = new
            {
                Id = Guid.NewGuid(),
                ChatId = Guid.NewGuid(),
                Room = room,
                UserId = $"{platform}:{user}",
                UserName = $"{platform}:{user}",
                FullName = $"{platform.ToUpper()} - {user}",
                Message = originalText,
                Timestamp = DateTime.UtcNow,
                FileName = (string?)null,
                FileUrl = (string?)null,
                FileSize = (long?)null,
                FileType = (string?)null
            };
            await _hubContext.Clients.Group(room).SendAsync("ReceiveMessage", userMsg);

            // Respuesta del bot
            var botMsg = new
            {
                Id = Guid.NewGuid(),
                ChatId = Guid.NewGuid(),
                Room = room,
                UserId = CentinelaBotService.BotUserId,
                UserName = CentinelaBotService.BotUserName,
                FullName = CentinelaBotService.BotFullName,
                Message = response.Message,
                Timestamp = DateTime.UtcNow,
                FileName = response.FileName,
                FileUrl = response.FileUrl,
                FileSize = response.FileSize,
                FileType = response.FileType
            };
            await _hubContext.Clients.Group(room).SendAsync("ReceiveMessage", botMsg);
        }
    }

    // ---- DTOs ----

    public class ExternalMessage
    {
        public string Message { get; set; } = "";
        public string? ImageUrl { get; set; }
        public string? FileName { get; set; }
        public string? Room { get; set; }
        public string? Source { get; set; }
        public string? UserName { get; set; }
        public string? FullName { get; set; }
    }

    public class NotifyRequest
    {
        public string Message { get; set; } = "";
        public string? ImageUrl { get; set; }
        public string[]? Targets { get; set; }
    }
}
