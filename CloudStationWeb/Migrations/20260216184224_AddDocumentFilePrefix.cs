using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudStationWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentFilePrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilePrefix",
                table: "DocumentProducts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilePrefix",
                table: "DocumentProducts");
        }
    }
}
