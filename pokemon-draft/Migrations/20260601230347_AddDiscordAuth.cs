using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonDraft.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscordAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_GoogleId",
                table: "AppUsers");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "AppUsers",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "DiscordId",
                table: "AppUsers",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_DiscordId",
                table: "AppUsers",
                column: "DiscordId",
                unique: true,
                filter: "[DiscordId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_GoogleId",
                table: "AppUsers",
                column: "GoogleId",
                unique: true,
                filter: "[GoogleId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_DiscordId",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_GoogleId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "DiscordId",
                table: "AppUsers");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "AppUsers",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_GoogleId",
                table: "AppUsers",
                column: "GoogleId",
                unique: true);
        }
    }
}
