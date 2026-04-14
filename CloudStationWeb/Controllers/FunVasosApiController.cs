using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    /// <summary>
    /// API para recibir archivos FunVasos (FIN Excel) desde programas externos.
    /// Valida, parsea e inserta en TimescaleDB. NO copia al DocumentRepository.
    ///
    /// POST /api/funvasos/upload   → Sube y procesa un archivo FIN Excel
    /// POST /api/funvasos/validate → Solo valida el archivo sin insertarlo
    /// GET  /api/funvasos/dates    → Lista fechas disponibles en la BD
    /// GET  /api/funvasos/data     → Consulta datos por fecha
    /// </summary>
    [Route("api/funvasos")]
    [ApiController]
    public class FunVasosApiController : ControllerBase
    {
        private readonly FunVasosService _service;
        private readonly string _apiKey;
        private readonly ILogger<FunVasosApiController> _logger;

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".xlsx", ".xls", ".xlsm"
        };

        public FunVasosApiController(FunVasosService service, IConfiguration config, ILogger<FunVasosApiController> logger)
        {
            _service = service;
            _apiKey = config["ImageStore:ApiKey"] ?? "pih-default-key-change-me";
            _logger = logger;
        }

        /// <summary>
        /// Sube y procesa un archivo FIN Excel.
        /// Header: X-Api-Key
        /// Body: multipart/form-data con campo "file"
        /// </summary>
        [HttpPost("upload")]
        [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No se recibió archivo" });

            var ext = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(ext))
                return BadRequest(new { error = $"Extensión '{ext}' no permitida. Use .xlsx, .xls o .xlsm" });

            // Guardar a archivo temporal
            var tempPath = Path.Combine(Path.GetTempPath(), $"funvasos_{Guid.NewGuid()}{ext}");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Parsear e insertar en BD
                var (rowsInserted, date, errors) = await _service.ParseAndStoreAsync(tempPath);

                if (errors.Count > 0 && rowsInserted == 0)
                {
                    _logger.LogWarning("FunVasos API: archivo rechazado '{File}': {Errors}",
                        file.FileName, string.Join("; ", errors));
                    return BadRequest(new
                    {
                        success = false,
                        fileName = file.FileName,
                        errors
                    });
                }

                _logger.LogInformation("FunVasos API: procesado '{File}' → {Rows} registros, fecha {Date:yyyy-MM-dd}",
                    file.FileName, rowsInserted, date);

                return Ok(new
                {
                    success = true,
                    fileName = file.FileName,
                    date = date.ToString("yyyy-MM-dd"),
                    rowsInserted,
                    warnings = errors.Count > 0 ? errors : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FunVasos API: error procesando '{File}'", file.FileName);
                return StatusCode(500, new { error = "Error interno procesando el archivo", detail = ex.Message });
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }

        /// <summary>
        /// Solo valida el archivo sin insertarlo en la BD.
        /// Header: X-Api-Key
        /// </summary>
        [HttpPost("validate")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> Validate(IFormFile file)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No se recibió archivo" });

            var ext = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(ext))
                return BadRequest(new { error = $"Extensión '{ext}' no permitida" });

            var tempPath = Path.Combine(Path.GetTempPath(), $"funvasos_val_{Guid.NewGuid()}{ext}");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Extraer fecha sin insertar
                var date = _service.ExtractDateFromFile(tempPath);
                if (date == null)
                    return BadRequest(new { valid = false, error = "No se pudo leer la hoja 'FIN' o extraer la fecha" });

                // Parsear sin insertar — usamos ParseAndStoreAsync internamente
                // pero como solo queremos validar, extraemos la fecha del filename también
                var fileDate = FunVasosService.ExtractDateFromFilename(file.FileName);

                return Ok(new
                {
                    valid = true,
                    fileName = file.FileName,
                    dateFromExcel = date.Value.ToString("yyyy-MM-dd"),
                    dateFromFilename = fileDate?.ToString("yyyy-MM-dd"),
                    sizeBytes = file.Length
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { valid = false, error = ex.Message });
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }

        /// <summary>
        /// Lista fechas disponibles con datos FunVasos.
        /// Header: X-Api-Key
        /// </summary>
        [HttpGet("dates")]
        public async Task<IActionResult> GetDates()
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            var dates = await _service.GetAvailableDatesAsync();
            return Ok(new
            {
                count = dates.Count,
                dates = dates.Select(d => d.ToString("yyyy-MM-dd"))
            });
        }

        /// <summary>
        /// Consulta datos FunVasos por fecha.
        /// Header: X-Api-Key
        /// GET /api/funvasos/data?fecha=2026-04-10
        /// </summary>
        [HttpGet("data")]
        public async Task<IActionResult> GetData([FromQuery] string? fecha = null)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            DateTime? dt = null;
            if (!string.IsNullOrEmpty(fecha) && DateTime.TryParse(fecha, out var parsed))
                dt = parsed.Date;

            var vm = await _service.GetDataAsync(dt, dt);

            return Ok(new
            {
                fecha = vm.FechaInicio.ToString("yyyy-MM-dd"),
                presas = vm.Presas.Select(p => new
                {
                    presa = p.Presa,
                    ultimaElevacion = p.UltimaElevacion,
                    ultimoAlmacenamiento = p.UltimoAlmacenamiento,
                    totalGeneracion = p.TotalGeneracion,
                    ultimaHora = p.UltimaHora,
                    registros = p.Datos.Count
                })
            });
        }

        private bool ValidateApiKey()
        {
            if (!Request.Headers.TryGetValue("X-Api-Key", out var key))
                return false;
            return string.Equals(key, _apiKey, StringComparison.Ordinal);
        }
    }
}