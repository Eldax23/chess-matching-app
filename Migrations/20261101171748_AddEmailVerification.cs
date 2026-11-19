using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessApp.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "email_verification_code_expires_at",
                table: "users",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_verification_code_hash",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "email_verification_code_sent_at",
                table: "users",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "email_verification_failed_attempts",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "email_verification_requested_at",
                table: "users",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "email_verified_at",
                table: "users",
                type: "timestamp without time zone",
                nullable: true);

            // Players who signed up with Lichess already had their email
            // confirmed there. Everyone else is asked for a code the next time
            // they open the app; their week-long cleanup clock only starts
            // when that first code is sent.
            migrationBuilder.Sql("UPDATE users SET email_verified_at = NOW() WHERE password_hash = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_verification_code_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verification_code_hash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verification_code_sent_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verification_failed_attempts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verification_requested_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verified_at",
                table: "users");
        }
    }
}
