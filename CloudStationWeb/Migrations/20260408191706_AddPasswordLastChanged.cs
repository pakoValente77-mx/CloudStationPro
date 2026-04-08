using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudStationWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordLastChanged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordLastChanged",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordLastChanged",
                table: "AspNetUsers");
        }
    }
}
