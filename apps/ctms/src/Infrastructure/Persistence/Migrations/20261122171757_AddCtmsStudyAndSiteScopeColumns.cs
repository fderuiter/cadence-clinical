using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCtmsStudyAndSiteScopeColumns : Migration
{
    private static readonly string[] BackfillRlsTables =
    [
        "ctms_audit_logs",
        "ctms_clinical_queries",
        "ctms_cra_allocations",
        "ctms_delegations",
        "ctms_generated_letters",
        "ctms_investigator_grants",
        "ctms_monitoring_visits",
        "ctms_recruitment_records",
        "ctms_regulatory_forms",
        "ctms_site_greenlight_gates",
        "ctms_site_milestones",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SiteId",
            table: "ctms_generated_letters",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "StudyId",
            table: "ctms_generated_letters",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "StudyId",
            table: "ctms_delegations",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "SiteId",
            table: "ctms_audit_logs",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StudyId",
            table: "ctms_audit_logs",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        foreach (string table in BackfillRlsTables)
        {
            migrationBuilder.Sql($"""ALTER TABLE "ctms"."{table}" NO FORCE ROW LEVEL SECURITY;""");
        }

        migrationBuilder.Sql(
            """
            UPDATE "ctms"."ctms_generated_letters" l
            SET
                "StudyId" = CASE WHEN l."StudyId" = '' THEN v."StudyId" ELSE l."StudyId" END,
                "SiteId" = CASE WHEN l."SiteId" = '' THEN v."SiteId" ELSE l."SiteId" END
            FROM "ctms"."ctms_monitoring_visits" v
            WHERE v.tenant_id = l.tenant_id
              AND v."Id" = l."VisitId"
              AND (l."StudyId" = '' OR l."SiteId" = '');

            UPDATE "ctms"."ctms_generated_letters" l
            SET
                "StudyId" = CASE WHEN l."StudyId" = '' THEN split_part(l."PdfObjectKey", '/', 2) ELSE l."StudyId" END,
                "SiteId" = CASE WHEN l."SiteId" = '' THEN split_part(l."PdfObjectKey", '/', 3) ELSE l."SiteId" END
            WHERE l."PdfObjectKey" IS NOT NULL
              AND split_part(l."PdfObjectKey", '/', 1) = 'letters'
              AND split_part(l."PdfObjectKey", '/', 2) <> ''
              AND split_part(l."PdfObjectKey", '/', 3) <> ''
              AND (l."StudyId" = '' OR l."SiteId" = '');

            WITH site_studies AS (
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_site_greenlight_gates" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_site_milestones" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_monitoring_visits" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_cra_allocations" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_clinical_queries" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_investigator_grants" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_recruitment_records" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_regulatory_forms" WHERE "SiteId" IS NOT NULL AND "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_generated_letters" WHERE "SiteId" <> '' AND "StudyId" <> ''
            ),
            unique_site_study AS (
                SELECT tenant_id, "SiteId", MIN("StudyId") AS resolved_study_id
                FROM site_studies
                GROUP BY tenant_id, "SiteId"
                HAVING COUNT(DISTINCT "StudyId") = 1
            )
            UPDATE "ctms"."ctms_delegations" d
            SET "StudyId" = u.resolved_study_id
            FROM unique_site_study u
            WHERE d.tenant_id = u.tenant_id
              AND d."SiteId" = u."SiteId"
              AND d."StudyId" = '';

            UPDATE "ctms"."ctms_audit_logs" a
            SET
                "SiteId" = COALESCE(
                    a."SiteId",
                    substring(a."Details" FROM '(?:at|for) site ([A-Za-z0-9_-]+)'),
                    substring(a."Details" FROM 'in study [A-Za-z0-9_-]+ site ([A-Za-z0-9_-]+)\.')),
                "StudyId" = COALESCE(
                    a."StudyId",
                    substring(a."Details" FROM '(?:in|for) study ([A-Za-z0-9_-]+)'))
            WHERE a."SiteId" IS NULL OR a."StudyId" IS NULL;

            WITH site_studies AS (
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_site_greenlight_gates" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_site_milestones" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_monitoring_visits" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_cra_allocations" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_delegations" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_clinical_queries" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_investigator_grants" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_recruitment_records" WHERE "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_regulatory_forms" WHERE "SiteId" IS NOT NULL AND "SiteId" <> '' AND "StudyId" <> ''
                UNION
                SELECT tenant_id, "SiteId", "StudyId" FROM "ctms"."ctms_generated_letters" WHERE "SiteId" <> '' AND "StudyId" <> ''
            ),
            unique_site_study AS (
                SELECT tenant_id, "SiteId", MIN("StudyId") AS resolved_study_id
                FROM site_studies
                GROUP BY tenant_id, "SiteId"
                HAVING COUNT(DISTINCT "StudyId") = 1
            )
            UPDATE "ctms"."ctms_audit_logs" a
            SET "StudyId" = u.resolved_study_id
            FROM unique_site_study u
            WHERE a.tenant_id = u.tenant_id
              AND a."SiteId" = u."SiteId"
              AND a."StudyId" IS NULL;
            """);

        foreach (string table in BackfillRlsTables)
        {
            migrationBuilder.Sql($"""ALTER TABLE "ctms"."{table}" FORCE ROW LEVEL SECURITY;""");
        }

        migrationBuilder.CreateIndex(
            name: "IX_ctms_generated_letters_SiteId",
            table: "ctms_generated_letters",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_generated_letters_StudyId",
            table: "ctms_generated_letters",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegations_StudyId",
            table: "ctms_delegations",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_audit_logs_SiteId",
            table: "ctms_audit_logs",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_audit_logs_StudyId",
            table: "ctms_audit_logs",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ctms_generated_letters_SiteId",
            table: "ctms_generated_letters");

        migrationBuilder.DropIndex(
            name: "IX_ctms_generated_letters_StudyId",
            table: "ctms_generated_letters");

        migrationBuilder.DropIndex(
            name: "IX_ctms_delegations_StudyId",
            table: "ctms_delegations");

        migrationBuilder.DropIndex(
            name: "IX_ctms_audit_logs_SiteId",
            table: "ctms_audit_logs");

        migrationBuilder.DropIndex(
            name: "IX_ctms_audit_logs_StudyId",
            table: "ctms_audit_logs");

        migrationBuilder.DropColumn(
            name: "SiteId",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "StudyId",
            table: "ctms_generated_letters");

        migrationBuilder.DropColumn(
            name: "StudyId",
            table: "ctms_delegations");

        migrationBuilder.DropColumn(
            name: "SiteId",
            table: "ctms_audit_logs");

        migrationBuilder.DropColumn(
            name: "StudyId",
            table: "ctms_audit_logs");
    }
}
