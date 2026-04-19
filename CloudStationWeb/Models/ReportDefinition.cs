using System.ComponentModel.DataAnnotations;

namespace CloudStationWeb.Models
{
    public class ReportDefinition
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Comando del bot, e.g. "/1", "/2", etc.</summary>
        [Required, MaxLength(20)]
        public string Command { get; set; } = string.Empty;

        /// <summary>image, chart, text, document</summary>
        [Required, MaxLength(50)]
        public string ContentType { get; set; } = "image";

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>Categoría en ImageStore: unidades, charts, lluvia, general</summary>
        [Required, MaxLength(50)]
        public string Category { get; set; } = "unidades";

        /// <summary>Nombre fijo del blob, e.g. "9c8a7f42-...png"</summary>
        [MaxLength(500)]
        public string? BlobName { get; set; }

        /// <summary>Prefijo para búsqueda dinámica (reportes de lluvia)</summary>
        [MaxLength(200)]
        public string? LatestPrefix { get; set; }

        /// <summary>Emoji + texto mostrado al usuario</summary>
        [MaxLength(500)]
        public string? Caption { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
