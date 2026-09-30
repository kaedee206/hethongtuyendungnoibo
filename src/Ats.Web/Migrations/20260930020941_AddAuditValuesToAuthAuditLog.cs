using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditValuesToAuthAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "new_values",
                table: "auth_audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "old_values",
                table: "auth_audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "performed_by",
                table: "auth_audit_logs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "new_values",
                table: "auth_audit_logs");

            migrationBuilder.DropColumn(
                name: "old_values",
                table: "auth_audit_logs");

            migrationBuilder.DropColumn(
                name: "performed_by",
                table: "auth_audit_logs");
        }
    }
}
