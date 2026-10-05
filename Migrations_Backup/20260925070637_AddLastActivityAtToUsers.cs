using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddLastActivityAtToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_activity_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_activity_at",
                table: "users");
        }
    }
}
