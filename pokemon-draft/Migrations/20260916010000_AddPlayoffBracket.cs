using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonDraft.Migrations;

public partial class AddPlayoffBracket : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlayoffBrackets",
            columns: table => new
            {
                LeagueCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlayoffBrackets", x => x.LeagueCode);
                table.ForeignKey("FK_PlayoffBrackets_Leagues_LeagueCode", x => x.LeagueCode,
                    "Leagues", "Code", onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "PlayoffBrackets");
}
