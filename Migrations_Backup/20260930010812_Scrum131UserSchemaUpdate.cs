using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class Scrum131UserSchemaUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "activation_token",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "users",
                type: "uuid",
                nullable: true);



            migrationBuilder.CreateIndex(
                name: "ix_users_department",
                table: "users",
                column: "department");

            migrationBuilder.CreateIndex(
                name: "ix_users_full_name",
                table: "users",
                column: "full_name");

            migrationBuilder.CreateIndex(
                name: "ix_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.AddForeignKey(
                name: "fk_users_roles_role_id",
                table: "users",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_roles_role_id",
                table: "users");



            migrationBuilder.DropIndex(
                name: "ix_users_department",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_full_name",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_role_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "activation_token",
                table: "users");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "users");
        }
    }
}
