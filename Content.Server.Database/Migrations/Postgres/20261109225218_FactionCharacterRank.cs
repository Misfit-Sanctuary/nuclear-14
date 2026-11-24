using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
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
                    faction_character_rank_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_name = table.Column<string>(type: "text", nullable: false),
                    ladder = table.Column<string>(type: "text", nullable: false),
                    held_rank = table.Column<string>(type: "text", nullable: true),
                    granted_rank = table.Column<string>(type: "text", nullable: true),
                    grant_capped = table.Column<bool>(type: "boolean", nullable: false)
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
