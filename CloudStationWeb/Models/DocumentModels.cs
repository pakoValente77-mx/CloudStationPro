using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloudStationWeb.Models
{
    public class DocumentProduct
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(20)]
        public string FilePrefix { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(1000)]
        public string StoragePath { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public bool RequiredDaily { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<DocumentEntry> Entries { get; set; } = new List<DocumentEntry>();
    }

    public class DocumentEntry
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public DocumentProduct Product { get; set; } = null!;

        [Required, MaxLength(500)]
        public string FileName { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string StoredFileName { get; set; } = string.Empty;

        [Required, MaxLength(1000)]
        public string StoredPath { get; set; } = string.Empty;

        public long FileSize { get; set; }

        [MaxLength(100)]
        public string ContentType { get; set; } = string.Empty;

        [Required]
        public string UploadedById { get; set; } = string.Empty;

        [ForeignKey("UploadedById")]
        public ApplicationUser UploadedBy { get; set; } = null!;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public bool IsLatest { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }

    public class DocumentAuditLog
    {
        [Key]
        public int Id { get; set; }

        public int? EntryId { get; set; }

        [ForeignKey("EntryId")]
        public DocumentEntry? Entry { get; set; }

        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public DocumentProduct Product { get; set; } = null!;

        [Required, MaxLength(50)]
        public string Action { get; set; } = string.Empty; // Upload, Replace, Download

        [Required]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string UserName { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(500)]
        public string? Details { get; set; }
    }

    public class UserProductPermission
    {
        public string UserId { get; set; } = string.Empty;
        
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; } = null!;

        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public DocumentProduct Product { get; set; } = null!;
    }
}
