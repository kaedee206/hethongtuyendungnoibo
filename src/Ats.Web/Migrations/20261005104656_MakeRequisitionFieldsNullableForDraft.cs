using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class MakeRequisitionFieldsNullableForDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_job_requisitions_departments_department_id",
                table: "job_requisitions");

            migrationBuilder.DropForeignKey(
                name: "fk_job_requisitions_job_positions_job_position_id",
                table: "job_requisitions");

            migrationBuilder.AlterColumn<Guid>(
                name: "job_position_id",
                table: "job_requisitions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "department_id",
                table: "job_requisitions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "fk_job_requisitions_departments_department_id",
                table: "job_requisitions",
                column: "department_id",
                principalTable: "departments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_job_requisitions_job_positions_job_position_id",
                table: "job_requisitions",
                column: "job_position_id",
                principalTable: "job_positions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_job_requisitions_departments_department_id",
                table: "job_requisitions");

            migrationBuilder.DropForeignKey(
                name: "fk_job_requisitions_job_positions_job_position_id",
                table: "job_requisitions");

            migrationBuilder.AlterColumn<Guid>(
                name: "job_position_id",
                table: "job_requisitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "department_id",
                table: "job_requisitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_job_requisitions_departments_department_id",
                table: "job_requisitions",
                column: "department_id",
                principalTable: "departments",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_job_requisitions_job_positions_job_position_id",
                table: "job_requisitions",
                column: "job_position_id",
                principalTable: "job_positions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
