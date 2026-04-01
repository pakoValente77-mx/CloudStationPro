using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudStationWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganismoAndCentroTrabajo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CentroTrabajoId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganismoId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CentrosTrabajo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CentrosTrabajo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CentroTrabajoId",
                table: "AspNetUsers",
                column: "CentroTrabajoId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_CentrosTrabajo_CentroTrabajoId",
                table: "AspNetUsers",
                column: "CentroTrabajoId",
                principalTable: "CentrosTrabajo",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_CentrosTrabajo_CentroTrabajoId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "CentrosTrabajo");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CentroTrabajoId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CentroTrabajoId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OrganismoId",
                table: "AspNetUsers");
        }
    }
}
