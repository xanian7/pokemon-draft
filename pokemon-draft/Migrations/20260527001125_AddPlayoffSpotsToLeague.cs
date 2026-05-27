using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonDraft.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayoffSpotsToLeague : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlayoffSpots",
                table: "Leagues",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayoffSpots",
                table: "Leagues");
        }
    }
}
