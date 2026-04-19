using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CloudStationWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddReportDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Command = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LatestPrefix = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportDefinitions", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ReportDefinitions",
                columns: new[] { "Id", "BlobName", "Caption", "Category", "Command", "ContentType", "CreatedAt", "Description", "IsActive", "LatestPrefix", "SortOrder", "Title", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "9c8a7f42-3d91-4e01-a3fa-0d2e5b1c6f7d.png", "📊 Reporte de Unidades actualizado.", "unidades", "/1", "image", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, 1, "Reporte de Unidades", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, "6f3b2c91-91df-41b6-9a1e-c3f0d0c8e24a.png", "📊 Captura del Power Monitoring.", "unidades", "/2", "image", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, 2, "Power Monitoring", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, "b7e1f9c3-8a2d-4f5d-9c3a-7f1f6e7a2c01.png", "📊 Gráfica de potencia.", "unidades", "/3", "image", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, 3, "Gráfica de Potencia", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 4, "e1a5f734-9c2e-4b3b-8d5a-6f7e1d2c9b8f.png", "📊 Condición de embalses.", "unidades", "/4", "image", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, 4, "Condición de Embalses", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 5, "d42f3e19-b89c-4f02-90d4-3e7f4a6d2c01.png", "📊 Aportaciones por cuenca propia.", "unidades", "/5", "image", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, 5, "Aportaciones por Cuenca Propia", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 6, "reporte_lluvia_1_1_638848218556433423.png", "📊 CFE SPH Grijalva - Reporte de lluvias 24 horas.", "unidades", "/6", "image", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "reporte_lluvia_1_1_", 6, "Reporte de Lluvias 24h", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 7, "reporte_lluvia_1_2_638848218556433423.png", "📊 CFE SPH Grijalva - Reporte parcial de lluvias.", "unidades", "/7", "image", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "reporte_lluvia_1_2_", 7, "Reporte Parcial de Lluvias", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportDefinitions_Command",
                table: "ReportDefinitions",
                column: "Command",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportDefinitions_IsActive",
                table: "ReportDefinitions",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportDefinitions");
        }
    }
}
