using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudStationWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddEsTrabajadorCFEFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepartamentoExterno",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmpresaExterna",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EsTrabajadorCFE",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepartamentoExterno",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmpresaExterna",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EsTrabajadorCFE",
                table: "AspNetUsers");
        }
    }
}
