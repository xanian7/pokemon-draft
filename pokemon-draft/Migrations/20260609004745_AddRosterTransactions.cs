using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonDraft.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RosterTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeagueCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PokemonId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterTransactions_Leagues_LeagueCode",
                        column: x => x.LeagueCode,
                        principalTable: "Leagues",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RosterTransactions_LeagueCode",
                table: "RosterTransactions",
                column: "LeagueCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RosterTransactions");
        }
    }
}
