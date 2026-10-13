using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateStageAndRejectionReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "stage",
                table: "applications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "rejection_reasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_rejection_reasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "candidate_stage_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_stage = table.Column<int>(type: "integer", nullable: true),
                    to_stage = table.Column<int>(type: "integer", nullable: false),
                    rejection_reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_stage_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_candidate_stage_histories_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_candidate_stage_histories_rejection_reasons_rejection_reaso",
                        column: x => x.rejection_reason_id,
                        principalTable: "rejection_reasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_candidate_stage_histories_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_applications_rejection_reason_id",
                table: "applications",
                column: "rejection_reason_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_stage_histories_application_id",
                table: "candidate_stage_histories",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_stage_histories_changed_by_user_id",
                table: "candidate_stage_histories",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_stage_histories_created_at",
                table: "candidate_stage_histories",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_stage_histories_rejection_reason_id",
                table: "candidate_stage_histories",
                column: "rejection_reason_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_stage_histories_to_stage",
                table: "candidate_stage_histories",
                column: "to_stage");

            migrationBuilder.CreateIndex(
                name: "ix_rejection_reasons_code",
                table: "rejection_reasons",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rejection_reasons_display_order",
                table: "rejection_reasons",
                column: "display_order");

            migrationBuilder.CreateIndex(
                name: "ix_rejection_reasons_is_active",
                table: "rejection_reasons",
                column: "is_active");

            migrationBuilder.AddForeignKey(
                name: "fk_applications_rejection_reasons_rejection_reason_id",
                table: "applications",
                column: "rejection_reason_id",
                principalTable: "rejection_reasons",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_applications_rejection_reasons_rejection_reason_id",
                table: "applications");

            migrationBuilder.DropTable(
                name: "candidate_stage_histories");

            migrationBuilder.DropTable(
                name: "rejection_reasons");

            migrationBuilder.DropIndex(
                name: "ix_applications_rejection_reason_id",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "stage",
                table: "applications");
        }
    }
}
