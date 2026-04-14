using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CloudStationWeb.Models;

namespace CloudStationWeb.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // Ensure permissions table exists (Manual DDL for SQL Server)
            string createTableSql = @"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='UserProductPermissions' AND xtype='U')
                BEGIN
                    CREATE TABLE [UserProductPermissions] (
                        [UserId] nvarchar(450) NOT NULL,
                        [ProductId] int NOT NULL,
                        CONSTRAINT [PK_UserProductPermissions] PRIMARY KEY ([UserId], [ProductId]),
                        CONSTRAINT [FK_UserProductPermissions_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_UserProductPermissions_DocumentProducts_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [DocumentProducts] ([Id]) ON DELETE CASCADE
                    );
                END";
            await context.Database.ExecuteSqlRawAsync(createTableSql);

            // Create roles
            string[] roles = { "SuperAdmin", "Administrador", "Operador", "Visualizador", "SoloVasos", "ApiConsumer" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // Seed default CentroTrabajo
            if (!await context.CentrosTrabajo.AnyAsync())
            {
                context.CentrosTrabajo.Add(new CentroTrabajo
                {
                    Nombre = "SUBGERENCIA HIDROGRIJALVA E HIDROMETRÍA",
                    Activo = true
                });
                await context.SaveChangesAsync();
            }

            // Create super user
            const string adminUserName = "administrador";
            const string adminPassword = "Cfe2026##";
            const string adminEmail = "admin@cloudstation.local";

            var adminUser = await userManager.FindByNameAsync(adminUserName);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminUserName,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    FullName = "Administrador del Sistema",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "SuperAdmin");
                }
                else
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    throw new Exception($"Error creating admin user: {errors}");
                }
            }
            else
            {
                // Ensure existing admin has SuperAdmin role
                if (!await userManager.IsInRoleAsync(adminUser, "SuperAdmin"))
                {
                    await userManager.AddToRoleAsync(adminUser, "SuperAdmin");
                }
            }

            // Seed document products
            var products = new[]
            {
                new { Code = "boletin", Prefix = "BHG", Name = "Boletín Hidrometeorológico de Generación", Desc = "Reporte diario hidrometeorológico de generación" },
                new { Code = "vasos", Prefix = "FIN", Name = "Funcionamiento de Vasos", Desc = "Reporte diario de niveles, almacenamiento y operación de vasos/presas" },
                new { Code = "red_telemetrica", Prefix = "NRED", Name = "Red Telemétrica", Desc = "Reporte diario de la red telemétrica de estaciones" }
            };

            foreach (var p in products)
            {
                var existing = await context.DocumentProducts.FirstOrDefaultAsync(dp => dp.Code == p.Code);
                if (existing == null)
                {
                    // Try to find by old code (migration from red_hidro)
                    if (p.Code == "red_telemetrica")
                        existing = await context.DocumentProducts.FirstOrDefaultAsync(dp => dp.Code == "red_hidro");

                    if (existing != null)
                    {
                        existing.Code = p.Code;
                        existing.Name = p.Name;
                        existing.Description = p.Desc;
                        existing.FilePrefix = p.Prefix;
                    }
                    else
                    {
                        context.DocumentProducts.Add(new DocumentProduct
                        {
                            Code = p.Code,
                            Name = p.Name,
                            Description = p.Desc,
                            FilePrefix = p.Prefix,
                            IsActive = true,
                            RequiredDaily = true,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    // Update existing products
                    existing.Name = p.Name;
                    existing.Description = p.Desc;
                    existing.FilePrefix = p.Prefix;
                }
            }
            await context.SaveChangesAsync();
        }
    }
}
