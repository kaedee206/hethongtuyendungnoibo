using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferRevisionRejectionAndTalentPoolFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "expected_salary",
                table: "job_offers",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_offer_id",
                table: "job_offers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rejected_at",
                table: "job_offers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_notes",
                table: "job_offers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rejection_reason_category",
                table: "job_offers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "job_offers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

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

            migrationBuilder.AddColumn<bool>(
                name: "is_in_talent_pool",
                table: "candidates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "talent_pool_added_at",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "talent_pool_notes",
                table: "candidates",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "evaluation_forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    total_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    snapshot_version = table.Column<string>(type: "text", nullable: false),
                    overall_recommendation = table.Column<string>(type: "text", nullable: true),
                    general_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_forms", x => x.id);
                    table.ForeignKey(
                        name: "fk_evaluation_forms_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_evaluation_forms_interviews_interview_id",
                        column: x => x.interview_id,
                        principalTable: "interviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_evaluation_forms_users_evaluator_id",
                        column: x => x.evaluator_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criteria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criteria_name = table.Column<string>(type: "text", nullable: false),
                    criteria_description = table.Column<string>(type: "text", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_evaluation_items_evaluation_forms_evaluation_form_id",
                        column: x => x.evaluation_form_id,
                        principalTable: "evaluation_forms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_parent_offer_id",
                table: "job_offers",
                column: "parent_offer_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_offers_status",
                table: "job_offers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_interview_evaluations_reopened_by_user_id",
                table: "interview_evaluations",
                column: "reopened_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_forms_application_id",
                table: "evaluation_forms",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_forms_evaluator_id",
                table: "evaluation_forms",
                column: "evaluator_id");

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_forms_interview_id",
                table: "evaluation_forms",
                column: "interview_id");

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_forms_interview_id_evaluator_id",
                table: "evaluation_forms",
                columns: new[] { "interview_id", "evaluator_id" });

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_items_evaluation_form_id",
                table: "evaluation_items",
                column: "evaluation_form_id");

            migrationBuilder.AddForeignKey(
                name: "fk_interview_evaluations_users_reopened_by_user_id",
                table: "interview_evaluations",
                column: "reopened_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_job_offers_job_offers_parent_offer_id",
                table: "job_offers",
                column: "parent_offer_id",
                principalTable: "job_offers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_interview_evaluations_users_reopened_by_user_id",
                table: "interview_evaluations");

            migrationBuilder.DropForeignKey(
                name: "fk_job_offers_job_offers_parent_offer_id",
                table: "job_offers");

            migrationBuilder.DropTable(
                name: "evaluation_items");

            migrationBuilder.DropTable(
                name: "evaluation_forms");

            migrationBuilder.DropIndex(
                name: "ix_job_offers_parent_offer_id",
                table: "job_offers");

            migrationBuilder.DropIndex(
                name: "ix_job_offers_status",
                table: "job_offers");

            migrationBuilder.DropIndex(
                name: "ix_interview_evaluations_reopened_by_user_id",
                table: "interview_evaluations");

            migrationBuilder.DropColumn(
                name: "expected_salary",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "parent_offer_id",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "rejection_notes",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "rejection_reason_category",
                table: "job_offers");

            migrationBuilder.DropColumn(
                name: "version",
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

            migrationBuilder.DropColumn(
                name: "is_in_talent_pool",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "talent_pool_added_at",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "talent_pool_notes",
                table: "candidates");
        }
    }
}
