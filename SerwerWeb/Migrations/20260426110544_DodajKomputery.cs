using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerwerWeb.Migrations
{
    /// <inheritdoc />
    public partial class DodajKomputery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolderyUzytkownikow");

            migrationBuilder.AddColumn<string>(
                name: "NazwaKomputera",
                table: "Zdarzenia",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Komputery",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NazwaKomputera = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Opis = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataDodania = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Komputery", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FolderyKomputerow",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KomputerId = table.Column<int>(type: "int", nullable: false),
                    Sciezka = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NazwaWyswietlana = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataDodania = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderyKomputerow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FolderyKomputerow_Komputery_KomputerId",
                        column: x => x.KomputerId,
                        principalTable: "Komputery",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolderyKomputerow_KomputerId",
                table: "FolderyKomputerow",
                column: "KomputerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolderyKomputerow");

            migrationBuilder.DropTable(
                name: "Komputery");

            migrationBuilder.DropColumn(
                name: "NazwaKomputera",
                table: "Zdarzenia");

            migrationBuilder.CreateTable(
                name: "FolderyUzytkownikow",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DataDodania = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NazwaStudenta = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NazwaWyswietlana = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sciezka = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderyUzytkownikow", x => x.Id);
                });
        }
    }
}
