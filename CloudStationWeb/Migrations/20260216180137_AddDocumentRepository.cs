using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudStationWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRepository : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RequiredDaily = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StoredPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UploadedById = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsLatest = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentEntries_AspNetUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentEntries_DocumentProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "DocumentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntryId = table.Column<int>(type: "int", nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentAuditLogs_DocumentEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "DocumentEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentAuditLogs_DocumentProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "DocumentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAuditLogs_EntryId",
                table: "DocumentAuditLogs",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAuditLogs_ProductId",
                table: "DocumentAuditLogs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAuditLogs_Timestamp",
                table: "DocumentAuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEntries_ProductId_IsLatest",
                table: "DocumentEntries",
                columns: new[] { "ProductId", "IsLatest" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEntries_UploadedAt",
                table: "DocumentEntries",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEntries_UploadedById",
                table: "DocumentEntries",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentProducts_Code",
                table: "DocumentProducts",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentAuditLogs");

            migrationBuilder.DropTable(
                name: "DocumentEntries");

            migrationBuilder.DropTable(
                name: "DocumentProducts");
        }
    }
}
