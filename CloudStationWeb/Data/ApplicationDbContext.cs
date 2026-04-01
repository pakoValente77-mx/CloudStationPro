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
        }
    }
}
