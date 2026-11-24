using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class FactionCharacterRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "faction_character_rank",
                columns: table => new
                {
                    faction_character_rank_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    player_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    character_name = table.Column<string>(type: "TEXT", nullable: false),
                    ladder = table.Column<string>(type: "TEXT", nullable: false),
                    held_rank = table.Column<string>(type: "TEXT", nullable: true),
                    granted_rank = table.Column<string>(type: "TEXT", nullable: true),
                    grant_capped = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faction_character_rank", x => x.faction_character_rank_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_faction_character_rank_player_id_character_name_ladder",
                table: "faction_character_rank",
                columns: new[] { "player_id", "character_name", "ladder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "faction_character_rank");
        }
    }
}
