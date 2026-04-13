using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerwerWeb.Migrations
{
    /// <inheritdoc />
    public partial class DodajTabeleFolderow_Final : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FolderyMonitorowane",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sciezka = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NazwaWyswietlana = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderyMonitorowane", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolderyMonitorowane");
        }
    }
}
