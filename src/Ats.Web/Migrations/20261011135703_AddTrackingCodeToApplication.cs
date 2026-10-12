using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackingCodeToApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "job_postings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_salary",
                table: "job_positions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_salary",
                table: "job_positions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tracking_code",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_applications_tracking_code",
                table: "applications",
                column: "tracking_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_applications_tracking_code",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "code",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "max_salary",
                table: "job_positions");

            migrationBuilder.DropColumn(
                name: "min_salary",
                table: "job_positions");

            migrationBuilder.DropColumn(
                name: "tracking_code",
                table: "applications");
        }
    }
}
