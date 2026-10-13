using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessApp.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddChessComLinking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_linked_accounts_platform_external_id",
                table: "linked_accounts");

            migrationBuilder.AddColumn<bool>(
                name: "is_verified",
                table: "linked_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every link so far came through Lichess OAuth
            migrationBuilder.Sql("UPDATE linked_accounts SET is_verified = true WHERE platform = 'lichess';");

            // chess.com usernames used to be free text saved without fetching
            // anything; they are now set only by linking (IntegrationsController)
            migrationBuilder.Sql(
                "UPDATE users SET chess_com_username = NULL, chess_com_url = NULL, chess_com_rating = NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_linked_accounts_platform_external_id",
                table: "linked_accounts",
                columns: new[] { "platform", "external_id" },
                unique: true,
                filter: "is_verified = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_linked_accounts_platform_external_id",
                table: "linked_accounts");

            migrationBuilder.DropColumn(
                name: "is_verified",
                table: "linked_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_linked_accounts_platform_external_id",
                table: "linked_accounts",
                columns: new[] { "platform", "external_id" },
                unique: true);
        }
    }
}
