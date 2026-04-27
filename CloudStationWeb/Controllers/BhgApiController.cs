using Microsoft.AspNetCore.Mvc;
using CloudStationWeb.Services;

namespace CloudStationWeb.Controllers
{
    /// <summary>
    /// API para recibir y consultar archivos BHG (Boletín Hidrológico Grijalva).
    ///
    /// POST /api/bhg/upload     → Sube un archivo BHG Excel al inbox
    /// GET  /api/bhg/presas     → Consulta datos de presas por mes/año
    /// GET  /api/bhg/estaciones → Consulta datos de estaciones por mes/año
    /// GET  /api/bhg/archivos   → Lista archivos procesados
    /// </summary>
    [Route("api/bhg")]
    [ApiController]
    public class BhgApiController : ControllerBase
    {
        private readonly BhgService _service;
        private readonly string _apiKey;
        private readonly ILogger<BhgApiController> _logger;

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".xlsx", ".xls", ".xlsm"
        };

        public BhgApiController(BhgService service, IConfiguration config, ILogger<BhgApiController> logger)
        {
            _service = service;
            _apiKey = config["ImageStore:ApiKey"] ?? "pih-default-key-change-me";
            _logger = logger;
        }

        [HttpPost("upload")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No se recibió archivo" });

            var ext = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(ext))
                return BadRequest(new { error = $"Extensión '{ext}' no permitida. Use .xlsx, .xls o .xlsm" });

            var tempPath = Path.Combine(Path.GetTempPath(), $"bhg_{Guid.NewGuid()}{ext}");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var result = await _service.ParseAndStoreAsync(tempPath, file.FileName);

                if (result.Errors.Any())
                {
                    _logger.LogWarning("BHG API: error procesando '{File}': {Error}", file.FileName, string.Join("; ", result.Errors));
                    return BadRequest(new { success = false, fileName = file.FileName, errors = result.Errors });
                }

                _logger.LogInformation("BHG API: archivo '{File}' importado ({PresaRows} presas, {EstRows} estaciones)",
                    file.FileName, result.PresaRows, result.EstacionRows);

                return Ok(new
                {
                    success = true,
                    fileName = file.FileName,
                    date = result.Fecha.ToString("yyyy-MM-dd"),
                    presaRows = result.PresaRows,
                    estacionRows = result.EstacionRows,
                    message = "Archivo BHG procesado e importado correctamente."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BHG API: error procesando '{File}'", file.FileName);
                return StatusCode(500, new { error = "Error interno procesando el archivo", detail = ex.Message });
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }

        [HttpGet("presas")]
        public async Task<IActionResult> GetPresas([FromQuery] int? mes = null, [FromQuery] int? anio = null)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            var vm = await _service.GetDataAsync(mes, anio);
            return Ok(new
            {
                mes = vm.Mes,
                anio = vm.Anio,
                mesNombre = vm.MesNombre,
                count = vm.Presas.Count,
                presas = vm.Presas.Select(p => new
                {
                    ts = p.Ts.ToString("yyyy-MM-dd"),
                    presa = p.Presa,
                    nivel = p.Nivel,
                    curvaGuia = p.CurvaGuia,
                    diffCurvaGuia = p.DiffCurvaGuia,
                    volAlmacenado = p.VolAlmacenado,
                    pctNamo = p.PctLlenadoNamo,
                    pctName = p.PctLlenadoName,
                    aportacionVol = p.AportacionVol,
                    aportacionQ = p.AportacionQ,
                    extraccionVol = p.ExtraccionVol,
                    extraccionQ = p.ExtraccionQ,
                    generacionGwh = p.GeneracionGwh,
                    factorPlanta = p.FactorPlanta
                })
            });
        }

        [HttpGet("estaciones")]
        public async Task<IActionResult> GetEstaciones([FromQuery] int? mes = null, [FromQuery] int? anio = null)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            var vm = await _service.GetDataAsync(mes, anio);
            return Ok(new
            {
                mes = vm.Mes,
                anio = vm.Anio,
                count = vm.Estaciones.Count,
                estaciones = vm.Estaciones.Select(e => new
                {
                    ts = e.Ts.ToString("yyyy-MM-dd"),
                    estacion = e.Estacion,
                    subcuenca = e.Subcuenca,
                    precip24h = e.Precip24h,
                    precipAcum = e.PrecipAcumMensual,
                    escala = e.Escala,
                    gasto = e.Gasto,
                    evaporacion = e.Evaporacion,
                    tempMax = e.TempMax,
                    tempMin = e.TempMin,
                    tempAmb = e.TempAmb
                })
            });
        }

        [HttpGet("archivos")]
        public async Task<IActionResult> GetArchivos()
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            var vm = await _service.GetDataAsync();
            return Ok(new
            {
                count = vm.Archivos.Count,
                archivos = vm.Archivos.Select(a => new
                {
                    id = a.Id,
                    fecha = a.Fecha.ToString("yyyy-MM-dd"),
                    nombre = a.NombreArchivo,
                    procesado = a.ProcesadoTs.ToString("yyyy-MM-dd HH:mm"),
                    mes = a.Mes,
                    anio = a.Anio,
                    dias = a.DiasConDatos,
                    estaciones = a.NumEstaciones
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
