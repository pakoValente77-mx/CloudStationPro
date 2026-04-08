using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using CloudStationWeb.Models;
using CloudStationWeb.Hubs;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CloudStationWeb.Controllers
{
    [Authorize(AuthenticationSchemes = "Identity.Application," + JwtBearerDefaults.AuthenticationScheme)]
    public class ChatController : Controller
    {
        private readonly string _sqlConn;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IWebHostEnvironment _env;

        public ChatController(IConfiguration config, IHubContext<ChatHub> hubContext, IWebHostEnvironment env)
        {
            _sqlConn = config.GetConnectionString("SqlServer")!;
            _hubContext = hubContext;
            _env = env;
        }

        // GET: /Chat
        public IActionResult Index()
        {
            return View();
        }

        // GET: /Chat/DesktopApp — Download desktop chat client
        [HttpGet]
        [AllowAnonymous]
        public IActionResult DesktopApp()
        {
            // Search multiple possible locations for desktop app installers
            var possibleDirs = new[]
            {
                Path.Combine(_env.ContentRootPath, "..", "ChatDesktop", "dist"),      // Dev: CloudStationWeb/../ChatDesktop/dist
                Path.Combine(_env.ContentRootPath, "ChatDesktop", "dist"),            // Prod: junto al exe
                Path.Combine(_env.ContentRootPath, "dist"),                           // Prod: dist/ directo
            };
            
            // Detect platform from User-Agent
            var ua = Request.Headers["User-Agent"].ToString().ToLower();
            bool isMac = ua.Contains("macintosh") || ua.Contains("mac os");
            
            // Prioritize by client platform
            string[] searchPatterns = isMac
                ? new[] { "*.dmg", "*.zip", "*.exe", "*.AppImage" }
                : new[] { "*.exe", "*.zip", "*.dmg", "*.AppImage" };
            
            foreach (var pattern in searchPatterns)
            {
                foreach (var distDir in possibleDirs)
                {
                    if (Directory.Exists(distDir))
                    {
                        var files = Directory.GetFiles(distDir, pattern);
                        if (files.Length > 0)
                        {
                            var file = files[0];
                            var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
                            return File(stream, "application/octet-stream", Path.GetFileName(file));
                        }
                    }
                }
            }

            // Fallback: serve the source as a ZIP for npm start
            var chatDesktopDir = Path.Combine(_env.ContentRootPath, "..", "ChatDesktop");
            if (!Directory.Exists(chatDesktopDir))
                return NotFound("La aplicación de escritorio no está disponible. Copie la carpeta ChatDesktop/dist/ junto al ejecutable.");

            var memStream = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(memStream, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                var filesToInclude = new[] { "package.json", "main.js", "preload.js", "index.html", "renderer.js", "styles.css" };
                foreach (var f in filesToInclude)
                {
                    var path = Path.Combine(chatDesktopDir, f);
                    if (System.IO.File.Exists(path))
                    {
                        var entry = archive.CreateEntry($"CloudStationChat/{f}");
                        using var writer = new StreamWriter(entry.Open());
                        writer.Write(System.IO.File.ReadAllText(path));
                    }
                }
                // Assets
                var iconPath = Path.Combine(chatDesktopDir, "assets", "icon.png");
                if (System.IO.File.Exists(iconPath))
                {
                    var entry = archive.CreateEntry("CloudStationChat/assets/icon.png");
                    using var entryStream = entry.Open();
                    using var iconStream = new FileStream(iconPath, FileMode.Open, FileAccess.Read);
                    iconStream.CopyTo(entryStream);
                }
                // README
                var readmeEntry = archive.CreateEntry("CloudStationChat/LEEME.txt");
                using var readmeWriter = new StreamWriter(readmeEntry.Open());
                readmeWriter.Write("CloudStation Chat - Cliente de Escritorio\n" +
                    "==========================================\n\n" +
                    "Requisitos: Node.js 18+\n\n" +
                    "Instalación:\n" +
                    "  1. Descomprimir esta carpeta\n" +
                    "  2. Abrir terminal en la carpeta CloudStationChat\n" +
                    "  3. Ejecutar: npm install\n" +
                    "  4. Ejecutar: npm start\n\n" +
                    "Para crear instalador:\n" +
                    "  npm run build:win   (Windows)\n" +
                    "  npm run build:mac   (macOS)\n" +
                    "  npm run build:linux (Linux)\n");
            }

            memStream.Position = 0;
            return File(memStream.ToArray(), "application/zip", "CloudStationChat.zip");
        }

        // GET: /Chat/OnlineUsers — HTTP endpoint for mobile apps
        [HttpGet]
        public IActionResult OnlineUsers()
        {
            return Json(Hubs.ChatHub.GetOnlineUsersStatic());
        }

        // GET: /Chat/History?room=general&limit=50
        [HttpGet]
        public async Task<IActionResult> History(string room = "general", int limit = 50, DateTime? since = null)
        {
            using var db = new SqlConnection(_sqlConn);
            string sql;
            object parameters;

            if (since.HasValue)
            {
                sql = @"SELECT TOP (@Limit) Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp,
                               FileName, FileUrl, FileSize, FileType
                        FROM ChatMessages
                        WHERE Room = @Room AND Timestamp >= @Since
                        ORDER BY Timestamp ASC";
                parameters = new { Room = room, Limit = limit, Since = since.Value };
            }
            else
            {
                sql = @"SELECT * FROM (
                            SELECT TOP (@Limit) Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp,
                                   FileName, FileUrl, FileSize, FileType
                            FROM ChatMessages
                            WHERE Room = @Room
                            ORDER BY Timestamp DESC
                        ) sub ORDER BY Timestamp ASC";
                parameters = new { Room = room, Limit = limit };
            }

            var messages = await db.QueryAsync<ChatMessage>(sql, parameters);
            return Json(messages);
        }

        // GET: /Chat/Rooms
        [HttpGet]
        public async Task<IActionResult> Rooms()
        {
            var userName = User.Identity?.Name ?? "";
            using var db = new SqlConnection(_sqlConn);
            var rooms = await db.QueryAsync<dynamic>(@"
                SELECT Room AS room, COUNT(*) AS messageCount, MAX(Timestamp) AS lastActivity
                FROM ChatMessages
                WHERE Room NOT LIKE 'dm:%' 
                   OR Room LIKE @Pattern1 
                   OR Room LIKE @Pattern2
                GROUP BY Room
                ORDER BY MAX(Timestamp) DESC", 
                new { Pattern1 = $"dm:{userName}:%", Pattern2 = $"dm:%:{userName}" });
            return Json(rooms);
        }

        // POST: /Chat/UploadFile — file upload for chat
        [HttpPost]
        [RequestSizeLimit(200_000_000)] // 200 MB
        public async Task<IActionResult> UploadFile(IFormFile file, string room)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No se recibió archivo." });

            if (string.IsNullOrWhiteSpace(room))
                room = "general";

            var userName = User.Identity?.Name ?? "anonymous";

            // Store in ChatUploads/YYYY-MM/guid_filename
            var monthDir = DateTime.UtcNow.ToString("yyyy-MM");
            var safeFileName = Path.GetFileName(file.FileName);
            var uniqueName = $"{Guid.NewGuid():N}_{safeFileName}";
            var relDir = Path.Combine("ChatUploads", monthDir);
            var absDir = Path.Combine(_env.ContentRootPath, relDir);
            Directory.CreateDirectory(absDir);

            var absPath = Path.Combine(absDir, uniqueName);
            using (var fs = new FileStream(absPath, FileMode.Create))
            {
                await file.CopyToAsync(fs);
            }

            var fileUrl = $"/Chat/DownloadFile?path={Uri.EscapeDataString(Path.Combine(relDir, uniqueName))}";

            // Build and persist message
            var msg = new ChatMessage
            {
                Id = Guid.NewGuid(),
                ChatId = Guid.NewGuid(),
                Room = room,
                UserId = userName,
                UserName = userName,
                FullName = userName,
                Message = $"📎 {safeFileName}",
                Timestamp = DateTime.UtcNow,
                FileName = safeFileName,
                FileUrl = fileUrl,
                FileSize = file.Length,
                FileType = file.ContentType ?? "application/octet-stream"
            };

            try
            {
                using var db = new SqlConnection(_sqlConn);
                await db.ExecuteAsync(@"
                    INSERT INTO ChatMessages (Id, ChatId, Room, UserId, UserName, FullName, Message, Timestamp,
                                              FileName, FileUrl, FileSize, FileType)
                    VALUES (@Id, @ChatId, @Room, @UserId, @UserName, @FullName, @Message, @Timestamp,
                            @FileName, @FileUrl, @FileSize, @FileType)", msg);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }

            // Broadcast via SignalR
            if (room.StartsWith("dm:"))
            {
                var parts = room.Split(':');
                if (parts.Length == 3)
                {
                    await _hubContext.Clients.Group($"user:{parts[1]}").SendAsync("ReceiveMessage", msg);
                    await _hubContext.Clients.Group($"user:{parts[2]}").SendAsync("ReceiveMessage", msg);
                }
            }
            else
            {
                await _hubContext.Clients.Group(room).SendAsync("ReceiveMessage", msg);
            }

            return Json(new { success = true, message = msg });
        }

        // GET: /Chat/DownloadFile?path=ChatUploads/2026-04/guid_file.pdf
        [HttpGet]
        public IActionResult DownloadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return NotFound();

            // Sanitize: prevent directory traversal
            var normalized = Path.GetFullPath(Path.Combine(_env.ContentRootPath, path));
            var allowedRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "ChatUploads"));
            if (!normalized.StartsWith(allowedRoot))
                return Forbid();

            if (!System.IO.File.Exists(normalized))
                return NotFound();

            var fileName = Path.GetFileName(normalized);
            // Remove the GUID prefix from filename for download
            var idx = fileName.IndexOf('_');
            var downloadName = idx > 0 ? fileName.Substring(idx + 1) : fileName;

            var contentType = "application/octet-stream";
            var ext = Path.GetExtension(downloadName).ToLowerInvariant();
            var mimeMap = new Dictionary<string, string>
            {
                { ".pdf", "application/pdf" }, { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" },
                { ".png", "image/png" }, { ".gif", "image/gif" }, { ".webp", "image/webp" },
                { ".doc", "application/msword" }, { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                { ".xls", "application/vnd.ms-excel" }, { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
                { ".zip", "application/zip" }, { ".txt", "text/plain" }, { ".csv", "text/csv" },
                { ".mp4", "video/mp4" }, { ".mp3", "audio/mpeg" }
            };
            if (mimeMap.TryGetValue(ext, out var mime)) contentType = mime;

            var stream = new FileStream(normalized, FileMode.Open, FileAccess.Read);
            return File(stream, contentType, downloadName);
        }
    }
}
