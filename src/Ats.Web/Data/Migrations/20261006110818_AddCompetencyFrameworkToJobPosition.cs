using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetencyFrameworkToJobPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lock_reason",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "locked_at",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "locked_by",
                table: "candidates");

            migrationBuilder.AddColumn<string>(
                name: "job_title",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "salary_band_explanation",
                table: "job_requisitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "competency_framework_id",
                table: "job_positions",
                type: "uuid",
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

            migrationBuilder.CreateTable(
                name: "company_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_name = table.Column<string>(type: "text", nullable: false),
                    headline = table.Column<string>(type: "text", nullable: false),
                    about_text = table.Column<string>(type: "text", nullable: false),
                    engineering_culture = table.Column<string>(type: "text", nullable: false),
                    tech_stack_json = table.Column<string>(type: "text", nullable: false),
                    proof_metrics_json = table.Column<string>(type: "text", nullable: false),
                    perks_json = table.Column<string>(type: "text", nullable: false),
                    headquarters_address = table.Column<string>(type: "text", nullable: false),
                    contact_email = table.Column<string>(type: "text", nullable: false),
                    phone_contact = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "competency_frameworks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_competency_frameworks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recruitment_catalogs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_type = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recruitment_catalogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "competency_criteria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competency_framework_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    rubric1 = table.Column<string>(type: "text", nullable: true),
                    rubric2 = table.Column<string>(type: "text", nullable: true),
                    rubric3 = table.Column<string>(type: "text", nullable: true),
                    rubric4 = table.Column<string>(type: "text", nullable: true),
                    rubric5 = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competency_criteria", x => x.id);
                    table.ForeignKey(
                        name: "fk_competency_criteria_competency_frameworks_competency_framew",
                        column: x => x.competency_framework_id,
                        principalTable: "competency_frameworks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "interview_question_banks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    competency = table.Column<string>(type: "text", nullable: false),
                    competency_framework_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competency_criterion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    difficulty = table.Column<string>(type: "text", nullable: false),
                    suggested_answer = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_interview_question_banks", x => x.id);
                    table.ForeignKey(
                        name: "fk_interview_question_banks_competency_criteria_competency_cri",
                        column: x => x.competency_criterion_id,
                        principalTable: "competency_criteria",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_interview_question_banks_competency_frameworks_competency_f",
                        column: x => x.competency_framework_id,
                        principalTable: "competency_frameworks",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_positions_competency_framework_id",
                table: "job_positions",
                column: "competency_framework_id");

            migrationBuilder.CreateIndex(
                name: "ix_competency_criteria_competency_framework_id",
                table: "competency_criteria",
                column: "competency_framework_id");

            migrationBuilder.CreateIndex(
                name: "ix_competency_frameworks_code",
                table: "competency_frameworks",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interview_question_banks_competency_criterion_id",
                table: "interview_question_banks",
                column: "competency_criterion_id");

            migrationBuilder.CreateIndex(
                name: "ix_interview_question_banks_competency_framework_id",
                table: "interview_question_banks",
                column: "competency_framework_id");

            migrationBuilder.AddForeignKey(
                name: "fk_job_positions_competency_frameworks_competency_framework_id",
                table: "job_positions",
                column: "competency_framework_id",
                principalTable: "competency_frameworks",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_job_positions_competency_frameworks_competency_framework_id",
                table: "job_positions");

            migrationBuilder.DropTable(
                name: "company_profiles");

            migrationBuilder.DropTable(
                name: "interview_question_banks");

            migrationBuilder.DropTable(
                name: "recruitment_catalogs");

            migrationBuilder.DropTable(
                name: "competency_criteria");

            migrationBuilder.DropTable(
                name: "competency_frameworks");

            migrationBuilder.DropIndex(
                name: "ix_job_positions_competency_framework_id",
                table: "job_positions");

            migrationBuilder.DropColumn(
                name: "job_title",
                table: "users");

            migrationBuilder.DropColumn(
                name: "salary_band_explanation",
                table: "job_requisitions");

            migrationBuilder.DropColumn(
                name: "competency_framework_id",
                table: "job_positions");

            migrationBuilder.DropColumn(
                name: "level",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "path",
                table: "departments");

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
        }
    }
}
