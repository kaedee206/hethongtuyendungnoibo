using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddRequisitionDescriptionAndRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "job_description",
                table: "job_requisitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requirements",
                table: "job_requisitions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "job_description",
                table: "job_requisitions");

            migrationBuilder.DropColumn(
                name: "requirements",
                table: "job_requisitions");
        }
    }
}
