using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessApp.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddHasBoardToGatheringMembersAndJoinRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_board",
                table: "join_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_board",
                table: "gathering_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing members and pending requests keep showing what their
            // profile said, since that's what the app showed before
            migrationBuilder.Sql(@"
                UPDATE gathering_members AS m SET has_board = u.has_board
                FROM users AS u WHERE u.id = m.user_id;
                UPDATE join_requests AS r SET has_board = u.has_board
                FROM users AS u WHERE u.id = r.requester_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_board",
                table: "join_requests");

            migrationBuilder.DropColumn(
                name: "has_board",
                table: "gathering_members");
        }
    }
}
