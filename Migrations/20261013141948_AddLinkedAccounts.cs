using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessApp.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "linked_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    profile_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    title = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ratings = table.Column<string>(type: "jsonb", nullable: false),
                    games_played = table.Column<int>(type: "integer", nullable: false),
                    wins = table.Column<int>(type: "integer", nullable: false),
                    losses = table.Column<int>(type: "integer", nullable: false),
                    draws = table.Column<int>(type: "integer", nullable: false),
                    platform_joined_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    platform_last_seen_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    connected_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_synced_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linked_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_linked_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_linked_accounts_last_synced_at",
                table: "linked_accounts",
                column: "last_synced_at");

            migrationBuilder.CreateIndex(
                name: "ix_linked_accounts_platform_external_id",
                table: "linked_accounts",
                columns: new[] { "platform", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_linked_accounts_user_id_platform",
                table: "linked_accounts",
                columns: new[] { "user_id", "platform" },
                unique: true);

            // Lichess usernames used to be free text; only OAuth-verified links count now
            migrationBuilder.Sql(
                "UPDATE users SET lichess_username = NULL, lichess_url = NULL, lichess_rating = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "linked_accounts");
        }
    }
}
