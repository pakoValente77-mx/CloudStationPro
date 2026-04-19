using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CloudStationWeb.Models;

namespace CloudStationWeb.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<DocumentProduct> DocumentProducts { get; set; } = null!;
        public DbSet<DocumentEntry> DocumentEntries { get; set; } = null!;
        public DbSet<DocumentAuditLog> DocumentAuditLogs { get; set; } = null!;
        public DbSet<UserProductPermission> UserProductPermissions { get; set; } = null!;
        public DbSet<LoginAudit> LoginAudits { get; set; } = null!;
        public DbSet<CentroTrabajo> CentrosTrabajo { get; set; } = null!;
        public DbSet<ReportDefinition> ReportDefinitions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<UserProductPermission>(entity =>
            {
                entity.HasKey(p => new { p.UserId, p.ProductId });
            });

            builder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(u => u.FullName).HasMaxLength(200);
                entity.HasOne(u => u.CentroTrabajo)
                      .WithMany()
                      .HasForeignKey(u => u.CentroTrabajoId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<DocumentProduct>(entity =>
            {
                entity.HasIndex(p => p.Code).IsUnique();
            });

            builder.Entity<DocumentEntry>(entity =>
            {
                entity.HasIndex(e => new { e.ProductId, e.IsLatest });
                entity.HasIndex(e => e.UploadedAt);
            });

            builder.Entity<DocumentAuditLog>(entity =>
            {
                entity.HasIndex(l => l.Timestamp);
                entity.HasIndex(l => l.ProductId);
            });

            builder.Entity<LoginAudit>(entity =>
            {
                entity.HasIndex(l => l.Timestamp);
                entity.HasIndex(l => l.UserId);
                entity.HasIndex(l => l.UserName);
            });

            builder.Entity<ReportDefinition>(entity =>
            {
                entity.HasIndex(r => r.Command).IsUnique();
                entity.HasIndex(r => r.IsActive);

                entity.HasData(
                    new ReportDefinition { Id = 1, Command = "/1", ContentType = "image", Title = "Reporte de Unidades", Category = "unidades", BlobName = "9c8a7f42-3d91-4e01-a3fa-0d2e5b1c6f7d.png", Caption = "📊 Reporte de Unidades actualizado.", SortOrder = 1, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new ReportDefinition { Id = 2, Command = "/2", ContentType = "image", Title = "Power Monitoring", Category = "unidades", BlobName = "6f3b2c91-91df-41b6-9a1e-c3f0d0c8e24a.png", Caption = "📊 Captura del Power Monitoring.", SortOrder = 2, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new ReportDefinition { Id = 3, Command = "/3", ContentType = "image", Title = "Gráfica de Potencia", Category = "unidades", BlobName = "b7e1f9c3-8a2d-4f5d-9c3a-7f1f6e7a2c01.png", Caption = "📊 Gráfica de potencia.", SortOrder = 3, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new ReportDefinition { Id = 4, Command = "/4", ContentType = "image", Title = "Condición de Embalses", Category = "unidades", BlobName = "e1a5f734-9c2e-4b3b-8d5a-6f7e1d2c9b8f.png", Caption = "📊 Condición de embalses.", SortOrder = 4, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new ReportDefinition { Id = 5, Command = "/5", ContentType = "image", Title = "Aportaciones por Cuenca Propia", Category = "unidades", BlobName = "d42f3e19-b89c-4f02-90d4-3e7f4a6d2c01.png", Caption = "📊 Aportaciones por cuenca propia.", SortOrder = 5, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new ReportDefinition { Id = 6, Command = "/6", ContentType = "image", Title = "Reporte de Lluvias 24h", Category = "unidades", BlobName = "reporte_lluvia_1_1_638848218556433423.png", LatestPrefix = "reporte_lluvia_1_1_", Caption = "📊 CFE SPH Grijalva - Reporte de lluvias 24 horas.", SortOrder = 6, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new ReportDefinition { Id = 7, Command = "/7", ContentType = "image", Title = "Reporte Parcial de Lluvias", Category = "unidades", BlobName = "reporte_lluvia_1_2_638848218556433423.png", LatestPrefix = "reporte_lluvia_1_2_", Caption = "📊 CFE SPH Grijalva - Reporte parcial de lluvias.", SortOrder = 7, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
                );
            });
        }
    }
}
