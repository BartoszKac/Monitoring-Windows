using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerwerWeb.Migrations
{
    /// <inheritdoc />
    public partial class DodajPolaKomputera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionId",
                table: "Komputery",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NazwaStudenta",
                table: "Komputery",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Online",
                table: "Komputery",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "OstatnioWidziany",
                table: "Komputery",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "Komputery");

            migrationBuilder.DropColumn(
                name: "NazwaStudenta",
                table: "Komputery");

            migrationBuilder.DropColumn(
                name: "Online",
                table: "Komputery");

            migrationBuilder.DropColumn(
                name: "OstatnioWidziany",
                table: "Komputery");
        }
    }
}
