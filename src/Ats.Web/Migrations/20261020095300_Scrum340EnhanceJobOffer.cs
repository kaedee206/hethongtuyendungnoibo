using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class Scrum340EnhanceJobOffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expiration_date",
                table: "job_offers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_outside_band",
                table: "job_offers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "job_position_id",
                table: "job_offers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_title",
                table: "job_offers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "job_offers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "salary_justification",
                table: "job_offers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_location",
                table: "job_offers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_reopened",
                table: "interview_evaluations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reopen_reason",
                table: "interview_evaluations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reopened_at",
                table: "interview_evaluations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reopened_by_user_id",
                table: "interview_evaluations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_job_position_id",
                table: "job_offers",
                column: "job_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_status",
                table: "job_offers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_interview_evaluations_reopened_by_user_id",
                table: "interview_evaluations",
                column: "reopened_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_interview_evaluations_users_reopened_by_user_id",
                table: "interview_evaluations",
                column: "reopened_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_job_offers_job_positions_job_position_id",
                table: "job_offers",
                column: "job_position_id",
                principalTable: "job_positions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_interview_evaluations_users_reopened_by_user_id",
                table: "interview_evaluations");

            migrationBuilder.DropForeignKey(
                name: "fk_job_offers_job_positions_job_position_id",
                table: "job_offers");

            migrationBuilder.DropIndex(
                name: "ix_interview_evaluations_reopened_by_user_id",
                table: "interview_evaluations");

            migrationBuilder.DropIndex(
                name: "ix_job_offers_status",
                table: "job_offers");

            migrationBuilder.DropIndex(
                name: "ix_job_offers_job_position_id",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "expiration_date",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "is_outside_band",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "job_position_id",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "job_title",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "salary_justification",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "work_location",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "is_reopened",
                table: "interview_evaluations");

            migrationBuilder.DropColumn(
                name: "reopen_reason",
                table: "interview_evaluations");

            migrationBuilder.DropColumn(
                name: "reopened_at",
                table: "interview_evaluations");

            migrationBuilder.DropColumn(
                name: "reopened_by_user_id",
                table: "interview_evaluations");
        }
    }
}
