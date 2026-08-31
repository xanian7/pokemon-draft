using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonDraft.Migrations
{
    /// <inheritdoc />
    public partial class AddReplayAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplayGames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchupId = table.Column<int>(type: "int", nullable: false),
                    GameNumber = table.Column<int>(type: "int", nullable: false),
                    ReplayUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShowdownPlayer1 = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShowdownPlayer2 = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WinnerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnalyzedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplayGames_Matchups_MatchupId",
                        column: x => x.MatchupId,
                        principalTable: "Matchups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReplayPokemonStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReplayGameId = table.Column<int>(type: "int", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PokemonId = table.Column<int>(type: "int", nullable: true),
                    PokemonName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kos = table.Column<int>(type: "int", nullable: false),
                    Deaths = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplayPokemonStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplayPokemonStats_ReplayGames_ReplayGameId",
                        column: x => x.ReplayGameId,
                        principalTable: "ReplayGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplayGames_MatchupId_GameNumber",
                table: "ReplayGames",
                columns: new[] { "MatchupId", "GameNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplayPokemonStats_PlayerId",
                table: "ReplayPokemonStats",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplayPokemonStats_PokemonId",
                table: "ReplayPokemonStats",
                column: "PokemonId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplayPokemonStats_ReplayGameId",
                table: "ReplayPokemonStats",
                column: "ReplayGameId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplayPokemonStats");

            migrationBuilder.DropTable(
                name: "ReplayGames");
        }
    }
}
