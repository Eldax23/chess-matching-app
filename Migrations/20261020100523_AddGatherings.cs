using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace ChessApp.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGatherings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gatherings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gatherings", x => x.id);
                    table.CheckConstraint("ck_time_window", "ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_gatherings_users_host_id",
                        column: x => x.host_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gathering_members",
                columns: table => new
                {
                    gathering_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gathering_members", x => new { x.gathering_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_gathering_members_gatherings_gathering_id",
                        column: x => x.gathering_id,
                        principalTable: "gatherings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_gathering_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "join_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gathering_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    approved_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_join_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_join_requests_gatherings_gathering_id",
                        column: x => x.gathering_id,
                        principalTable: "gatherings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_join_requests_users_requester_id",
                        column: x => x.requester_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "join_request_rejections",
                columns: table => new
                {
                    join_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_join_request_rejections", x => new { x.join_request_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_join_request_rejections_join_requests_join_request_id",
                        column: x => x.join_request_id,
                        principalTable: "join_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gathering_members_user_id",
                table: "gathering_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_gatherings_ends_at",
                table: "gatherings",
                column: "ends_at",
                filter: "status IN ('open', 'gathered')");

            migrationBuilder.CreateIndex(
                name: "ix_gatherings_host_id",
                table: "gatherings",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "ix_gatherings_location",
                table: "gatherings",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "ix_join_requests_gathering_id",
                table: "join_requests",
                column: "gathering_id");

            migrationBuilder.CreateIndex(
                name: "ix_join_requests_gathering_id_requester_id",
                table: "join_requests",
                columns: new[] { "gathering_id", "requester_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_join_requests_requester_id",
                table: "join_requests",
                column: "requester_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gathering_members");

            migrationBuilder.DropTable(
                name: "join_request_rejections");

            migrationBuilder.DropTable(
                name: "join_requests");

            migrationBuilder.DropTable(
                name: "gatherings");
        }
    }
}
