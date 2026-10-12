using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralToApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "referral_note",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "referrer_user_id",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_applications_referrer_user_id",
                table: "applications",
                column: "referrer_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_applications_users_referrer_user_id",
                table: "applications",
                column: "referrer_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_applications_users_referrer_user_id",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "ix_applications_referrer_user_id",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "referral_note",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "referrer_user_id",
                table: "applications");
        }
    }
}
