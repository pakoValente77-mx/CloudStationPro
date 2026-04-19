using CloudStationWeb.Data;
using CloudStationWeb.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CloudStationWeb.Controllers
{
    /// <summary>
    /// Catálogo dinámico de reportes — CRUD para definiciones de reportes del bot Centinela.
    /// GET    /api/reports           → Lista todos los reportes activos
    /// GET    /api/reports/all       → Lista todos (incluyendo inactivos)
    /// GET    /api/reports/{id}      → Detalle de un reporte
    /// POST   /api/reports           → Crear reporte (requiere API key)
    /// PUT    /api/reports/{id}      → Actualizar reporte (requiere API key)
    /// DELETE /api/reports/{id}      → Eliminar reporte (requiere API key)
    /// </summary>
    [Route("api/reports")]
    [ApiController]
    public class ReportCatalogController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly string _apiKey;
        private readonly ILogger<ReportCatalogController> _logger;

        public ReportCatalogController(ApplicationDbContext db, IConfiguration config, ILogger<ReportCatalogController> logger)
        {
            _db = db;
            _apiKey = config["ImageStore:ApiKey"] ?? "pih-default-key-change-me";
            _logger = logger;
        }

        /// <summary>Lista reportes activos, ordenados por SortOrder.</summary>
        [HttpGet]
        public async Task<IActionResult> GetActive()
        {
            var reports = await _db.ReportDefinitions
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder)
                .ToListAsync();
            return Ok(reports);
        }

        /// <summary>Lista todos los reportes (activos e inactivos).</summary>
        [HttpGet("all")]
        public async Task<IActionResult> GetAll()
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            var reports = await _db.ReportDefinitions
                .OrderBy(r => r.SortOrder)
                .ToListAsync();
            return Ok(reports);
        }

        /// <summary>Detalle de un reporte por Id.</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var report = await _db.ReportDefinitions.FindAsync(id);
            if (report == null)
                return NotFound(new { error = $"Reporte {id} no encontrado" });
            return Ok(report);
        }

        /// <summary>Crear un nuevo reporte.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ReportDefinitionDto dto)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            if (await _db.ReportDefinitions.AnyAsync(r => r.Command == dto.Command))
                return Conflict(new { error = $"Ya existe un reporte con comando '{dto.Command}'" });

            var report = new ReportDefinition
            {
                Command = dto.Command,
                ContentType = dto.ContentType ?? "image",
                Title = dto.Title,
                Description = dto.Description,
                Category = dto.Category ?? "unidades",
                BlobName = dto.BlobName,
                LatestPrefix = dto.LatestPrefix,
                Caption = dto.Caption,
                IsActive = dto.IsActive ?? true,
                SortOrder = dto.SortOrder ?? 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.ReportDefinitions.Add(report);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Reporte creado: {Command} → {Title}", report.Command, report.Title);
            return CreatedAtAction(nameof(GetById), new { id = report.Id }, report);
        }

        /// <summary>Actualizar un reporte existente.</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ReportDefinitionDto dto)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            var report = await _db.ReportDefinitions.FindAsync(id);
            if (report == null)
                return NotFound(new { error = $"Reporte {id} no encontrado" });

            if (!string.IsNullOrEmpty(dto.Command) && dto.Command != report.Command)
            {
                if (await _db.ReportDefinitions.AnyAsync(r => r.Command == dto.Command && r.Id != id))
                    return Conflict(new { error = $"Ya existe otro reporte con comando '{dto.Command}'" });
                report.Command = dto.Command;
            }

            if (dto.ContentType != null) report.ContentType = dto.ContentType;
            if (dto.Title != null) report.Title = dto.Title;
            if (dto.Description != null) report.Description = dto.Description;
            if (dto.Category != null) report.Category = dto.Category;
            if (dto.BlobName != null) report.BlobName = dto.BlobName;
            if (dto.LatestPrefix != null) report.LatestPrefix = dto.LatestPrefix;
            if (dto.Caption != null) report.Caption = dto.Caption;
            if (dto.IsActive.HasValue) report.IsActive = dto.IsActive.Value;
            if (dto.SortOrder.HasValue) report.SortOrder = dto.SortOrder.Value;
            report.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Reporte actualizado: {Id} {Command}", report.Id, report.Command);
            return Ok(report);
        }

        /// <summary>Eliminar un reporte.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { error = "API key inválida" });

            var report = await _db.ReportDefinitions.FindAsync(id);
            if (report == null)
                return NotFound(new { error = $"Reporte {id} no encontrado" });

            _db.ReportDefinitions.Remove(report);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Reporte eliminado: {Id} {Command}", report.Id, report.Command);
            return Ok(new { message = $"Reporte '{report.Command}' eliminado" });
        }

        private bool ValidateApiKey()
        {
            if (!Request.Headers.TryGetValue("X-Api-Key", out var key))
                return false;
            return string.Equals(key, _apiKey, StringComparison.Ordinal);
        }
    }

    public class ReportDefinitionDto
    {
        public string Command { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public string? BlobName { get; set; }
        public string? LatestPrefix { get; set; }
        public string? Caption { get; set; }
        public bool? IsActive { get; set; }
        public int? SortOrder { get; set; }
    }
}
