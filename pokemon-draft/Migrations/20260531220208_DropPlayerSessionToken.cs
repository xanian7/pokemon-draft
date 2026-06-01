using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonDraft.Migrations
{
    /// <inheritdoc />
    public partial class DropPlayerSessionToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionToken",
                table: "Players");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SessionToken",
                table: "Players",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
