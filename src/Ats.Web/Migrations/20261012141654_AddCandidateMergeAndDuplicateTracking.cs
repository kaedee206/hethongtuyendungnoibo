using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateMergeAndDuplicateTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_merged",
                table: "candidates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "merged_at",
                table: "candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_candidate_id",
                table: "candidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "candidate_merge_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secondary_candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    merge_reason = table.Column<string>(type: "text", nullable: true),
                    moved_application_ids_json = table.Column<string>(type: "text", nullable: false),
                    moved_resume_ids_json = table.Column<string>(type: "text", nullable: false),
                    snapshot_secondary_data_json = table.Column<string>(type: "text", nullable: true),
                    is_undone = table.Column<bool>(type: "boolean", nullable: false),
                    undone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    undone_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_candidate_merge_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_candidate_merge_histories_candidates_primary_candidate_id",
                        column: x => x.primary_candidate_id,
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_candidate_merge_histories_candidates_secondary_candidate_id",
                        column: x => x.secondary_candidate_id,
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_candidate_merge_histories_users_merged_by_user_id",
                        column: x => x.merged_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_candidate_merge_histories_users_undone_by_user_id",
                        column: x => x.undone_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_candidates_is_merged",
                table: "candidates",
                column: "is_merged");

            migrationBuilder.CreateIndex(
                name: "ix_candidates_merged_into_candidate_id",
                table: "candidates",
                column: "merged_into_candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidates_phone",
                table: "candidates",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_merge_histories_merged_at",
                table: "candidate_merge_histories",
                column: "merged_at");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_merge_histories_merged_by_user_id",
                table: "candidate_merge_histories",
                column: "merged_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_merge_histories_primary_candidate_id",
                table: "candidate_merge_histories",
                column: "primary_candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_merge_histories_secondary_candidate_id",
                table: "candidate_merge_histories",
                column: "secondary_candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_merge_histories_undone_by_user_id",
                table: "candidate_merge_histories",
                column: "undone_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_candidates_candidates_merged_into_candidate_id",
                table: "candidates",
                column: "merged_into_candidate_id",
                principalTable: "candidates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_candidates_candidates_merged_into_candidate_id",
                table: "candidates");

            migrationBuilder.DropTable(
                name: "candidate_merge_histories");

            migrationBuilder.DropIndex(
                name: "ix_candidates_is_merged",
                table: "candidates");

            migrationBuilder.DropIndex(
                name: "ix_candidates_merged_into_candidate_id",
                table: "candidates");

            migrationBuilder.DropIndex(
                name: "ix_candidates_phone",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "is_merged",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "merged_at",
                table: "candidates");

            migrationBuilder.DropColumn(
                name: "merged_into_candidate_id",
                table: "candidates");
        }
    }
}
