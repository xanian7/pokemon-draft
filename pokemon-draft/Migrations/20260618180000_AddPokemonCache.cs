using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PokemonDraft.Data;

#nullable disable

namespace PokemonDraft.Migrations
{
    [DbContext(typeof(DraftDbContext))]
    [Migration("20260618180000_AddPokemonCache")]
    public partial class AddPokemonCache : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PokemonCache",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    SpeciesId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SpriteUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TypesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Bst = table.Column<int>(type: "int", nullable: false),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MegaFormsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PokemonCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PokemonCache_Name",
                table: "PokemonCache",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PokemonCache_SpeciesId",
                table: "PokemonCache",
                column: "SpeciesId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PokemonCache");
        }
    }
}
