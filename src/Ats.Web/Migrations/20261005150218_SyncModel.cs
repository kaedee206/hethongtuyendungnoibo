using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "code",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "description",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "is_system",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "lock_reason",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "locked_at",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "locked_by",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "locked_until",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "name",
                table: "candidates");

            migrationBuilder.AddColumn<string>(
                name: "job_title",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "level",
                table: "departments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "path",
                table: "departments",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "job_title",
                table: "users");

            migrationBuilder.DropColumn(
                name: "level",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "path",
                table: "departments");

            migrationBuilder.AddColumn<int>(
                name: "code",
                table: "candidates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "candidates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_system",
                table: "candidates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "lock_reason",
                table: "candidates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_at",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "locked_by",
                table: "candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "candidates",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
