using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <summary>
    /// #Misfits Add - Store Discord OAuth links on existing player accounts.
    /// </summary>
    public partial class MisfitsDiscordLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "discord_id",
                table: "player",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_discord_id",
                table: "player",
                column: "discord_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_player_discord_id",
                table: "player");

            migrationBuilder.DropColumn(
                name: "discord_id",
                table: "player");
        }
    }
}
