using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Web.Migrations;

public partial class Scrum43CandidateScreening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "color_code",
            table: "recruitment_catalogs",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "catalog_id",
            table: "candidate_tags",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            INSERT INTO recruitment_catalogs
                (id, catalog_type, code, name, color_code, description, display_order, is_active, is_system,
                 created_at, updated_at, created_by_id, updated_by_id, is_deleted, deleted_at)
            SELECT gen_random_uuid(), 'CANDIDATE_TAG',
                   'LEGACY_' || md5(lower(trim(t.tag_name))),
                   min(trim(t.tag_name)),
                   coalesce(min(t.color_code) FILTER (WHERE t.color_code ~ '^#[0-9A-Fa-f]{6}$'), '#059669'),
                   NULL, 0, TRUE, FALSE, now(), now(), NULL, NULL, FALSE, NULL
            FROM candidate_tags t
            WHERE trim(t.tag_name) <> ''
              AND NOT EXISTS (
                  SELECT 1 FROM recruitment_catalogs c
                  WHERE c.catalog_type = 'CANDIDATE_TAG'
                    AND lower(trim(c.name)) = lower(trim(t.tag_name))
              )
            GROUP BY lower(trim(t.tag_name));

            INSERT INTO recruitment_catalogs
                (id, catalog_type, code, name, color_code, description, display_order, is_active, is_system,
                 created_at, updated_at, created_by_id, updated_by_id, is_deleted, deleted_at)
            SELECT gen_random_uuid(), 'CANDIDATE_TAG', defaults.code, defaults.name, defaults.color_code,
                   NULL, defaults.display_order, TRUE, FALSE, now(), now(), NULL, NULL, FALSE, NULL
            FROM (VALUES
                ('TAG_POTENTIAL', 'Tiềm năng cao', '#059669', 1),
                ('TAG_SENIOR_DOTNET', 'Senior .NET', '#2563EB', 2),
                ('TAG_ENGLISH', 'Tiếng Anh tốt', '#D97706', 3),
                ('TAG_INTERVIEW_AGAIN', 'Cần phỏng vấn lại', '#7C3AED', 4),
                ('TAG_FUTURE', 'Chờ đợt tuyển sau', '#0D9488', 5)
            ) AS defaults(code, name, color_code, display_order)
            WHERE NOT EXISTS (
                SELECT 1 FROM recruitment_catalogs c
                WHERE c.catalog_type = 'CANDIDATE_TAG'
                  AND lower(trim(c.name)) = lower(trim(defaults.name))
            );

            UPDATE candidate_tags t
            SET catalog_id = c.id
            FROM recruitment_catalogs c
            WHERE c.catalog_type = 'CANDIDATE_TAG'
              AND lower(trim(c.name)) = lower(trim(t.tag_name));

            WITH duplicates AS (
                SELECT id,
                       row_number() OVER (PARTITION BY candidate_id, catalog_id ORDER BY created_at, id) AS row_number
                FROM candidate_tags
                WHERE is_deleted = FALSE
            )
            UPDATE candidate_tags t
            SET is_deleted = TRUE, deleted_at = now(), updated_at = now()
            FROM duplicates d
            WHERE t.id = d.id AND d.row_number > 1;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "catalog_id",
            table: "candidate_tags",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "content",
            table: "candidate_notes",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.DropIndex(
            name: "ix_candidate_tags_candidate_id",
            table: "candidate_tags");

        migrationBuilder.DropColumn(
            name: "color_code",
            table: "candidate_tags");

        migrationBuilder.DropColumn(
            name: "tag_name",
            table: "candidate_tags");

        migrationBuilder.CreateIndex(
            name: "ix_candidate_tags_candidate_id_catalog_id",
            table: "candidate_tags",
            columns: new[] { "candidate_id", "catalog_id" },
            unique: true,
            filter: "\"is_deleted\" = false");

        migrationBuilder.CreateIndex(
            name: "ix_candidate_tags_catalog_id",
            table: "candidate_tags",
            column: "catalog_id");

        migrationBuilder.AddForeignKey(
            name: "fk_candidate_tags_recruitment_catalogs_catalog_id",
            table: "candidate_tags",
            column: "catalog_id",
            principalTable: "recruitment_catalogs",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX ux_recruitment_catalogs_candidate_tag_normalized_name
            ON recruitment_catalogs (lower(trim(name)))
            WHERE catalog_type = 'CANDIDATE_TAG' AND is_deleted = FALSE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS ux_recruitment_catalogs_candidate_tag_normalized_name;
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_candidate_tags_recruitment_catalogs_catalog_id",
            table: "candidate_tags");

        migrationBuilder.DropIndex(
            name: "ix_candidate_tags_candidate_id_catalog_id",
            table: "candidate_tags");

        migrationBuilder.DropIndex(
            name: "ix_candidate_tags_catalog_id",
            table: "candidate_tags");

        migrationBuilder.AddColumn<string>(
            name: "tag_name",
            table: "candidate_tags",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "color_code",
            table: "candidate_tags",
            type: "text",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE candidate_tags t
            SET tag_name = c.name, color_code = c.color_code
            FROM recruitment_catalogs c
            WHERE c.id = t.catalog_id;
            """);

        migrationBuilder.DropColumn(
            name: "catalog_id",
            table: "candidate_tags");

        migrationBuilder.AlterColumn<string>(
            name: "content",
            table: "candidate_notes",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(4000)",
            oldMaxLength: 4000);

        migrationBuilder.DropColumn(
            name: "color_code",
            table: "recruitment_catalogs");

        migrationBuilder.CreateIndex(
            name: "ix_candidate_tags_candidate_id",
            table: "candidate_tags",
            column: "candidate_id");
    }
}
