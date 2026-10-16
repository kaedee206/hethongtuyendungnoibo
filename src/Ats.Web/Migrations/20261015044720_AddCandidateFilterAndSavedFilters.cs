using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateFilterAndSavedFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_sla_enabled",
                table: "pipeline_stages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "sla_days",
                table: "pipeline_stages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_over_quota",
                table: "job_requisitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "over_quota_approved_at",
                table: "job_requisitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "over_quota_approved_by_id",
                table: "job_requisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "over_quota_reason",
                table: "job_requisitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                table: "job_postings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unpublish_reason",
                table: "job_postings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cover_letter",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_sla_overdue",
                table: "applications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sla_overdue_alerted_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stage_entered_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "withdraw_reason",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "withdraw_token",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "withdrawn_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "candidate_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    note_type = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_candidate_notes_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_candidate_notes_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "candidate_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_name = table.Column<string>(type: "text", nullable: false),
                    color_code = table.Column<string>(type: "text", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_candidate_tags_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_candidate_tags_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "department_headcount_budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    target_headcount = table.Column<int>(type: "integer", nullable: false),
                    salary_budget = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department_headcount_budgets", x => x.id);
                    table.ForeignKey(
                        name: "fk_department_headcount_budgets_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requisition_handover_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_recruiter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_recruiter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handover_type = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    transferred_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transferred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_handover_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_requisition_handover_histories_job_requisitions_requisition",
                        column: x => x.requisition_id,
                        principalTable: "job_requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_requisition_handover_histories_users_from_recruiter_id",
                        column: x => x.from_recruiter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_requisition_handover_histories_users_to_recruiter_id",
                        column: x => x.to_recruiter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_requisition_handover_histories_users_transferred_by_id",
                        column: x => x.transferred_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requisition_recruiters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recruiter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_recruiters", x => x.id);
                    table.ForeignKey(
                        name: "fk_requisition_recruiters_job_requisitions_requisition_id",
                        column: x => x.requisition_id,
                        principalTable: "job_requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_requisition_recruiters_users_assigned_by_id",
                        column: x => x.assigned_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_requisition_recruiters_users_recruiter_id",
                        column: x => x.recruiter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saved_candidate_filters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    criteria_json = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_candidate_filters", x => x.id);
                    table.ForeignKey(
                        name: "fk_saved_candidate_filters_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "talent_pool_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_stage_reached = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    target_job_title = table.Column<string>(type: "text", nullable: false),
                    skills = table.Column<string>(type: "text", nullable: true),
                    experience_years = table.Column<int>(type: "integer", nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_talent_pool_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_talent_pool_candidates_applications_source_application_id",
                        column: x => x.source_application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_talent_pool_candidates_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_talent_pool_candidates_users_added_by_user_id",
                        column: x => x.added_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_requisitions_over_quota_approved_by_id",
                table: "job_requisitions",
                column: "over_quota_approved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_notes_author_user_id",
                table: "candidate_notes",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_notes_candidate_id",
                table: "candidate_notes",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_tags_candidate_id",
                table: "candidate_tags",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_tags_created_by_user_id",
                table: "candidate_tags",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_department_headcount_budgets_department_id_year",
                table: "department_headcount_budgets",
                columns: new[] { "department_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisition_handover_histories_from_recruiter_id",
                table: "requisition_handover_histories",
                column: "from_recruiter_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_handover_histories_requisition_id",
                table: "requisition_handover_histories",
                column: "requisition_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_handover_histories_to_recruiter_id",
                table: "requisition_handover_histories",
                column: "to_recruiter_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_handover_histories_transferred_by_id",
                table: "requisition_handover_histories",
                column: "transferred_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_recruiters_assigned_by_id",
                table: "requisition_recruiters",
                column: "assigned_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_recruiters_recruiter_id",
                table: "requisition_recruiters",
                column: "recruiter_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_recruiters_requisition_id_recruiter_id",
                table: "requisition_recruiters",
                columns: new[] { "requisition_id", "recruiter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_saved_candidate_filters_user_id_name",
                table: "saved_candidate_filters",
                columns: new[] { "user_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_talent_pool_candidates_added_by_user_id",
                table: "talent_pool_candidates",
                column: "added_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_talent_pool_candidates_candidate_id",
                table: "talent_pool_candidates",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_talent_pool_candidates_source_application_id",
                table: "talent_pool_candidates",
                column: "source_application_id");

            migrationBuilder.CreateIndex(
                name: "ix_talent_pool_candidates_status",
                table: "talent_pool_candidates",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_talent_pool_candidates_target_job_title",
                table: "talent_pool_candidates",
                column: "target_job_title");

            migrationBuilder.AddForeignKey(
                name: "fk_job_requisitions_users_over_quota_approved_by_id",
                table: "job_requisitions",
                column: "over_quota_approved_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_job_requisitions_users_over_quota_approved_by_id",
                table: "job_requisitions");

            migrationBuilder.DropTable(
                name: "candidate_notes");

            migrationBuilder.DropTable(
                name: "candidate_tags");

            migrationBuilder.DropTable(
                name: "department_headcount_budgets");

            migrationBuilder.DropTable(
                name: "requisition_handover_histories");

            migrationBuilder.DropTable(
                name: "requisition_recruiters");

            migrationBuilder.DropTable(
                name: "saved_candidate_filters");

            migrationBuilder.DropTable(
                name: "talent_pool_candidates");

            migrationBuilder.DropIndex(
                name: "ix_job_requisitions_over_quota_approved_by_id",
                table: "job_requisitions");

            migrationBuilder.DropColumn(
                name: "is_sla_enabled",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "sla_days",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "is_over_quota",
                table: "job_requisitions");

            migrationBuilder.DropColumn(
                name: "over_quota_approved_at",
                table: "job_requisitions");

            migrationBuilder.DropColumn(
                name: "over_quota_approved_by_id",
                table: "job_requisitions");

            migrationBuilder.DropColumn(
                name: "over_quota_reason",
                table: "job_requisitions");

            migrationBuilder.DropColumn(
                name: "closed_at",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "unpublish_reason",
                table: "job_postings");

            migrationBuilder.DropColumn(
                name: "cover_letter",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "is_sla_overdue",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "sla_overdue_alerted_at",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "stage_entered_at",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "withdraw_reason",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "withdraw_token",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "withdrawn_at",
                table: "applications");
        }
    }
}
