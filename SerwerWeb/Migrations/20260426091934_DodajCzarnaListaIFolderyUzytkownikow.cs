using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerwerWeb.Migrations
{
    /// <inheritdoc />
    public partial class DodajCzarnaListaIFolderyUzytkownikow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CzarnaLista",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NazwaStudenta = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Powod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataDodania = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CzarnaLista", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FolderyUzytkownikow",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NazwaStudenta = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sciezka = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NazwaWyswietlana = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataDodania = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderyUzytkownikow", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CzarnaLista");

            migrationBuilder.DropTable(
                name: "FolderyUzytkownikow");
        }
    }
}
