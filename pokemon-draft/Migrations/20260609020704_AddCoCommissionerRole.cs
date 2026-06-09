using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonDraft.Migrations
{
    /// <inheritdoc />
    public partial class AddCoCommissionerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCoCommissioner",
                table: "Players",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCoCommissioner",
                table: "Players");
        }
    }
}
