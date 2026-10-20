using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace ChessApp.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceProposalsAndMatchesWithGatherings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reviews pointed at matches, which are dropped below along with
            // proposals and stats: the app no longer tracks games. Nothing is
            // deployed yet, so the old match reviews go too.
            migrationBuilder.Sql("DELETE FROM reviews;");

            migrationBuilder.DropForeignKey(
                name: "fk_reviews_matches_match_id",
                table: "reviews");

            migrationBuilder.DropTable(
                name: "proposals");

            migrationBuilder.DropTable(
                name: "user_stats");

            migrationBuilder.DropTable(
                name: "matches");

            migrationBuilder.DropIndex(
                name: "ix_reviews_match_id_reviewer_id",
                table: "reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rating_range",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "rating",
                table: "reviews");

            migrationBuilder.RenameColumn(
                name: "match_id",
                table: "reviews",
                newName: "gathering_id");

            migrationBuilder.AddColumn<bool>(
                name: "no_show",
                table: "reviews",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tags",
                table: "reviews",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_gathering_id_reviewer_id_reviewee_id",
                table: "reviews",
                columns: new[] { "gathering_id", "reviewer_id", "reviewee_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_reviews_gatherings_gathering_id",
                table: "reviews",
                column: "gathering_id",
                principalTable: "gatherings",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_reviews_gatherings_gathering_id",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_reviews_gathering_id_reviewer_id_reviewee_id",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "no_show",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "reviews");

            migrationBuilder.RenameColumn(
                name: "gathering_id",
                table: "reviews",
                newName: "match_id");

            migrationBuilder.AddColumn<int>(
                name: "rating",
                table: "reviews",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player1id = table.Column<Guid>(type: "uuid", nullable: false),
                    player2id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()"),
                    location = table.Column<Point>(type: "geography (point, 4326)", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    pgn = table.Column<string>(type: "text", nullable: true),
                    played_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    played_with_board = table.Column<bool>(type: "boolean", nullable: true),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    time_control = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matches", x => x.id);
                    table.ForeignKey(
                        name: "fk_matches_users_player1id",
                        column: x => x.player1id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_matches_users_player2id",
                        column: x => x.player2id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_stats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    avg_opponent_rating = table.Column<int>(type: "integer", nullable: true),
                    last_stats_update = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()"),
                    longest_streak = table.Column<int>(type: "integer", nullable: false),
                    total_draws = table.Column<int>(type: "integer", nullable: false),
                    total_losses = table.Column<int>(type: "integer", nullable: false),
                    total_matches_played = table.Column<int>(type: "integer", nullable: false),
                    total_wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_stats", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_stats_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proposer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()"),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    meeting_location = table.Column<Point>(type: "geography (point, 4326)", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    responded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposals", x => x.id);
                    table.CheckConstraint("ck_different_users", "proposer_id != receiver_id");
                    table.ForeignKey(
                        name: "fk_proposals_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_proposals_users_proposer_id",
                        column: x => x.proposer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_proposals_users_receiver_id",
                        column: x => x.receiver_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_match_id_reviewer_id",
                table: "reviews",
                columns: new[] { "match_id", "reviewer_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_rating_range",
                table: "reviews",
                sql: "rating >= 1 AND rating <= 5");

            migrationBuilder.CreateIndex(
                name: "ix_matches_played_at",
                table: "matches",
                column: "played_at");

            migrationBuilder.CreateIndex(
                name: "ix_matches_player1id",
                table: "matches",
                column: "player1id");

            migrationBuilder.CreateIndex(
                name: "ix_matches_player2id",
                table: "matches",
                column: "player2id");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_expires_at",
                table: "proposals",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_match_id",
                table: "proposals",
                column: "match_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_proposals_meeting_location",
                table: "proposals",
                column: "meeting_location")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_proposer_id",
                table: "proposals",
                column: "proposer_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_receiver_id_status",
                table: "proposals",
                columns: new[] { "receiver_id", "status" },
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_status",
                table: "proposals",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_user_stats_user_id",
                table: "user_stats",
                column: "user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_reviews_matches_match_id",
                table: "reviews",
                column: "match_id",
                principalTable: "matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
