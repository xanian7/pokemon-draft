using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonDraft.Migrations
{
    /// <inheritdoc />
    public partial class AddReplayScoutingData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HeldItem",
                table: "ReplayPokemonStats",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MovesJson",
                table: "ReplayPokemonStats",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Nature",
                table: "ReplayPokemonStats",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AnalysisVersion",
                table: "ReplayGames",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeldItem",
                table: "ReplayPokemonStats");

            migrationBuilder.DropColumn(
                name: "MovesJson",
                table: "ReplayPokemonStats");

            migrationBuilder.DropColumn(
                name: "Nature",
                table: "ReplayPokemonStats");

            migrationBuilder.DropColumn(
                name: "AnalysisVersion",
                table: "ReplayGames");
        }
    }
}
