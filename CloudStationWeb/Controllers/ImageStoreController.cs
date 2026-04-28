using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace CloudStationWeb.Controllers
{
    /// <summary>
    /// Almacén local de imágenes — reemplaza Azure Blob Storage.
    /// - POST /api/images/{category}   → Upload (requiere API key)
    /// - GET  /api/images/{category}/{filename}  → Descarga pública
    /// - GET  /api/images/{category}/latest?prefix=xxx  → Última imagen por prefijo
    /// - DELETE /api/images/{category}/{filename}  → Eliminar (requiere API key)
    /// </summary>
    [Route("api/images")]
    [ApiController]
    public class ImageStoreController : ControllerBase
    {
        private readonly string _storageRoot;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly ILogger<ImageStoreController> _logger;

        private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "unidades", "charts", "lluvia", "general"
        };

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".pdf"
        };

        public ImageStoreController(IConfiguration config, ILogger<ImageStoreController> logger)
        {
            _storageRoot = config["ImageStore:Path"] ?? Path.Combine(AppContext.BaseDirectory, "ImageStore");
            _apiKey = config["ImageStore:ApiKey"] ?? "pih-default-key-change-me";
            _baseUrl = config["ImageStore:BaseUrl"] ?? "";
            _logger = logger;
        }

        /// <summary>
        /// Subir imagen. Header: X-Api-Key.
        /// POST /api/images/{category}?name=opcional.png
        /// Body: multipart/form-data con campo "file"
        /// </summary>
        [HttpPost("{category}")]
        public async Task<IActionResult> Upload(string category, IFormFile file, [FromQuery] string? name = null)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            if (!AllowedCategories.Contains(category))
                return BadRequest(new { error = $"Categoría '{category}' no permitida. Válidas: {string.Join(", ", AllowedCategories)}" });

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No se recibió archivo" });

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
                return BadRequest(new { error = $"Extensión '{extension}' no permitida" });

            // Sanitizar nombre
            var fileName = string.IsNullOrWhiteSpace(name)
                ? file.FileName
                : name;
            fileName = SanitizeFileName(fileName);

            var categoryDir = Path.Combine(_storageRoot, category);
            Directory.CreateDirectory(categoryDir);

            var filePath = Path.Combine(categoryDir, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var url = GetImageUrl(category, fileName);
            _logger.LogInformation("[ImageStore] Uploaded: {Category}/{FileName} ({Size} bytes)", category, fileName, file.Length);

            return Ok(new
            {
                success = true,
                url,
                category,
                fileName,
                size = file.Length
            });
        }

        /// <summary>
        /// Descargar imagen — requiere autenticación (FIX CVE-M2).
        /// GET /api/images/{category}/{fileName}
        /// </summary>
        [Authorize]
        [HttpGet("{category}/{fileName}")]
        public IActionResult Download(string category, string fileName)
        {
            if (!AllowedCategories.Contains(category))
                return NotFound();

            fileName = SanitizeFileName(fileName);
            var filePath = Path.Combine(_storageRoot, category, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "Imagen no encontrada" });

            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out var contentType))
                contentType = "application/octet-stream";

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, contentType, enableRangeProcessing: true);
        }

        /// <summary>
        /// Obtener la última imagen por prefijo en una categoría — requiere autenticación (FIX CVE-M2).
        /// GET /api/images/{category}/latest?prefix=reporte_lluvia_1_1_
        /// </summary>
        [Authorize]
        [HttpGet("{category}/latest")]
        public IActionResult GetLatest(string category, [FromQuery] string prefix)
        {
            if (!AllowedCategories.Contains(category))
                return NotFound();

            if (string.IsNullOrWhiteSpace(prefix))
                return BadRequest(new { error = "Se requiere el parámetro 'prefix'" });

            prefix = SanitizeFileName(prefix);
            var categoryDir = Path.Combine(_storageRoot, category);
            if (!Directory.Exists(categoryDir))
                return NotFound(new { error = "No hay imágenes en esta categoría" });

            var latest = Directory.GetFiles(categoryDir)
                .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest == null)
                return NotFound(new { error = $"No se encontró imagen con prefijo '{prefix}'" });

            var fileName = Path.GetFileName(latest);
            var url = GetImageUrl(category, fileName);

            return Ok(new { url, fileName, category });
        }

        /// <summary>
        /// Listar imágenes en una categoría.
        /// GET /api/images/{category}?prefix=opcional
        /// </summary>
        [HttpGet("{category}")]
        public IActionResult List(string category, [FromQuery] string? prefix = null)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            if (!AllowedCategories.Contains(category))
                return NotFound();

            var categoryDir = Path.Combine(_storageRoot, category);
            if (!Directory.Exists(categoryDir))
                return Ok(new { category, files = Array.Empty<object>() });

            var files = Directory.GetFiles(categoryDir)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return string.IsNullOrEmpty(prefix) || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                })
                .Select(f =>
                {
                    var info = new FileInfo(f);
                    var name = info.Name;
                    return new
                    {
                        fileName = name,
                        url = GetImageUrl(category, name),
                        size = info.Length,
                        lastModified = info.LastWriteTimeUtc
                    };
                })
                .OrderByDescending(f => f.lastModified)
                .ToList();

            return Ok(new { category, count = files.Count, files });
        }

        /// <summary>
        /// Eliminar imagen.
        /// DELETE /api/images/{category}/{fileName}
        /// </summary>
        [HttpDelete("{category}/{fileName}")]
        public IActionResult Delete(string category, string fileName)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            if (!AllowedCategories.Contains(category))
                return NotFound();

            fileName = SanitizeFileName(fileName);
            var filePath = Path.Combine(_storageRoot, category, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "Imagen no encontrada" });

            System.IO.File.Delete(filePath);
            _logger.LogInformation("[ImageStore] Deleted: {Category}/{FileName}", category, fileName);

            return Ok(new { success = true, message = $"Eliminada: {category}/{fileName}" });
        }

        // ---- Helpers ----

        private bool ValidateApiKey()
        {
            return Request.Headers.TryGetValue("X-Api-Key", out var key) &&
                   string.Equals(key, _apiKey, StringComparison.Ordinal);
        }

        private string GetImageUrl(string category, string fileName)
        {
            if (!string.IsNullOrEmpty(_baseUrl))
                return $"{_baseUrl.TrimEnd('/')}/api/images/{category}/{fileName}";

            return $"{Request.Scheme}://{Request.Host}/api/images/{category}/{fileName}";
        }

        private static string SanitizeFileName(string name)
        {
            // Remove path traversal and invalid chars
            name = Path.GetFileName(name);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
