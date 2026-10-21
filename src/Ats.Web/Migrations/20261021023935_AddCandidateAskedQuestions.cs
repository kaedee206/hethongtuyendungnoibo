using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateAskedQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_interview_question_banks_competency_criteria_competency_cri",
                table: "interview_question_banks");

            migrationBuilder.DropForeignKey(
                name: "fk_interview_question_banks_competency_frameworks_competency_f",
                table: "interview_question_banks");

            migrationBuilder.AddColumn<string>(
                name: "expected_answer",
                table: "interview_question_banks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "good_answer_rubric",
                table: "interview_question_banks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "job_position_id",
                table: "interview_question_banks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "key_points",
                table: "interview_question_banks",
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

            migrationBuilder.CreateTable(
                name: "candidate_asked_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_round = table.Column<int>(type: "integer", nullable: false),
                    asked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_asked_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_candidate_asked_questions_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_candidate_asked_questions_interview_question_banks_question",
                        column: x => x.question_id,
                        principalTable: "interview_question_banks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_candidate_asked_questions_interviews_interview_id",
                        column: x => x.interview_id,
                        principalTable: "interviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_candidate_asked_questions_users_asked_by_user_id",
                        column: x => x.asked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "ix_interview_question_banks_job_position_id",
                table: "interview_question_banks",
                column: "job_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_interview_evaluations_reopened_by_user_id",
                table: "interview_evaluations",
                column: "reopened_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_asked_questions_asked_by_user_id",
                table: "candidate_asked_questions",
                column: "asked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_asked_questions_candidate_id",
                table: "candidate_asked_questions",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_asked_questions_candidate_id_interview_id_questio",
                table: "candidate_asked_questions",
                columns: new[] { "candidate_id", "interview_id", "question_id" });

            migrationBuilder.CreateIndex(
                name: "ix_candidate_asked_questions_interview_id",
                table: "candidate_asked_questions",
                column: "interview_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_asked_questions_question_id",
                table: "candidate_asked_questions",
                column: "question_id");

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
                name: "fk_interview_question_banks_competency_criteria_competency_cri",
                table: "interview_question_banks",
                column: "competency_criterion_id",
                principalTable: "competency_criteria",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_interview_question_banks_competency_frameworks_competency_f",
                table: "interview_question_banks",
                column: "competency_framework_id",
                principalTable: "competency_frameworks",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_interview_question_banks_job_positions_job_position_id",
                table: "interview_question_banks",
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
                name: "fk_interview_question_banks_competency_criteria_competency_cri",
                table: "interview_question_banks");

            migrationBuilder.DropForeignKey(
                name: "fk_interview_question_banks_competency_frameworks_competency_f",
                table: "interview_question_banks");

            migrationBuilder.DropForeignKey(
                name: "fk_interview_question_banks_job_positions_job_position_id",
                table: "interview_question_banks");

            migrationBuilder.DropTable(
                name: "candidate_asked_questions");

            migrationBuilder.DropTable(
                name: "evaluation_items");

            migrationBuilder.DropTable(
                name: "evaluation_forms");

            migrationBuilder.DropIndex(
                name: "ix_interview_question_banks_job_position_id",
                table: "interview_question_banks");

            migrationBuilder.DropIndex(
                name: "ix_interview_evaluations_reopened_by_user_id",
                table: "interview_evaluations");

            migrationBuilder.DropColumn(
                name: "expected_answer",
                table: "interview_question_banks");

            migrationBuilder.DropColumn(
                name: "good_answer_rubric",
                table: "interview_question_banks");

            migrationBuilder.DropColumn(
                name: "job_position_id",
                table: "interview_question_banks");

            migrationBuilder.DropColumn(
                name: "key_points",
                table: "interview_question_banks");

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

            migrationBuilder.AddForeignKey(
                name: "fk_interview_question_banks_competency_criteria_competency_cri",
                table: "interview_question_banks",
                column: "competency_criterion_id",
                principalTable: "competency_criteria",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_interview_question_banks_competency_frameworks_competency_f",
                table: "interview_question_banks",
                column: "competency_framework_id",
                principalTable: "competency_frameworks",
                principalColumn: "id");
        }
    }
}
