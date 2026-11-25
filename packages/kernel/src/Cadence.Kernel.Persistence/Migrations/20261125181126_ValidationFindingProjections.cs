using System;

using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Materializes current validation finding states in <c>kernel.validation_finding_projections</c> (ADR-2462 §5) with
/// forced Row-Level Security (K-ID-2), composite tenant foreign keys (K-ID-1), composite query indexes, and a
/// deterministic SQL backfill over existing tenants and studies.
/// </summary>
public partial class ValidationFindingProjections : Migration
{
    internal const string BackfillSql = """
        DO $backfill$
        DECLARE
            v_prev_tenant text := pg_catalog.current_setting('cadence.tenant_id', true);
            v_tenant_id uuid;
        BEGIN
            FOR v_tenant_id IN SELECT "kernel".tenant_ids() LOOP
                PERFORM pg_catalog.set_config('cadence.tenant_id', v_tenant_id::text, true);

                INSERT INTO "kernel"."validation_finding_projections" (
                    tenant_id,
                    finding_id,
                    study_id,
                    stage,
                    pack_key,
                    dataset,
                    rule_id,
                    target_kind,
                    target_key,
                    variable,
                    severity,
                    status,
                    blocks,
                    classification,
                    decision,
                    decided_at,
                    decision_in_force,
                    first_detected_run_id,
                    first_detected_at,
                    last_detected_run_id,
                    last_detected_at,
                    resolved_by_run_id,
                    latest_clean_run_at,
                    projection_version
                )
                WITH ordered_runs AS (
                    SELECT
                        r.tenant_id,
                        r.study_id,
                        r.id AS run_id,
                        r.stage,
                        r.created_at AS recorded_at,
                        ROW_NUMBER() OVER (
                            PARTITION BY r.tenant_id, r.study_id
                            ORDER BY r.created_at ASC, r.id ASC
                        ) AS pos
                    FROM "kernel"."validation_runs" r
                    WHERE r.tenant_id = v_tenant_id
                ),
                run_coverage AS (
                    SELECT
                        r.tenant_id,
                        r.study_id,
                        r.run_id,
                        r.stage,
                        r.recorded_at,
                        r.pos,
                        p.pack_key,
                        ds.dataset
                    FROM ordered_runs r
                    JOIN "kernel"."validation_run_packs" p
                        ON p.tenant_id = r.tenant_id AND p.run_id = r.run_id
                    JOIN "kernel"."validation_run_datasets" ds
                        ON ds.tenant_id = r.tenant_id AND ds.run_id = r.run_id
                ),
                finding_detections AS (
                    SELECT
                        d.tenant_id,
                        d.study_id,
                        d.finding_id,
                        d.run_id,
                        r.recorded_at,
                        r.pos,
                        d.stage,
                        d.pack_key,
                        d.dataset,
                        d.rule_id,
                        d.target_kind,
                        d.target_key,
                        d.variable,
                        d.severity,
                        MIN(r.pos) OVER (PARTITION BY d.tenant_id, d.finding_id) AS first_pos,
                        MAX(r.pos) OVER (PARTITION BY d.tenant_id, d.finding_id) AS last_pos
                    FROM "kernel"."validation_detections" d
                    JOIN ordered_runs r
                        ON r.tenant_id = d.tenant_id AND r.run_id = d.run_id
                    WHERE d.tenant_id = v_tenant_id
                ),
                finding_boundaries AS (
                    SELECT
                        l.tenant_id,
                        l.finding_id,
                        l.study_id,
                        l.stage,
                        l.pack_key,
                        l.dataset,
                        l.rule_id,
                        l.target_kind,
                        l.target_key,
                        l.variable,
                        l.severity,
                        f.run_id AS first_detected_run_id,
                        f.recorded_at AS first_detected_at,
                        l.run_id AS last_detected_run_id,
                        l.recorded_at AS last_detected_at,
                        l.first_pos,
                        l.last_pos
                    FROM finding_detections l
                    JOIN finding_detections f
                        ON f.tenant_id = l.tenant_id
                        AND f.finding_id = l.finding_id
                        AND f.pos = l.first_pos
                    WHERE l.pos = l.last_pos
                ),
                finding_derived AS (
                    SELECT
                        b.*,
                        vf.classification,
                        vf.decision,
                        vf.decided_at,
                        cov_after.resolved_by_run_id,
                        clean_any.latest_clean_run_at,
                        CASE
                            WHEN cov_after.resolved_by_run_id IS NOT NULL THEN FALSE
                            WHEN vf.decision IS NOT NULL
                                AND vf.decided_at IS NOT NULL
                                AND (prior_res.prior_resolved_at IS NULL OR vf.decided_at >= prior_res.prior_resolved_at)
                            THEN TRUE
                            ELSE FALSE
                        END AS decision_in_force
                    FROM finding_boundaries b
                    LEFT JOIN "kernel"."validation_findings" vf
                        ON vf.tenant_id = b.tenant_id AND vf.id = b.finding_id
                    LEFT JOIN LATERAL (
                        SELECT c.run_id AS resolved_by_run_id
                        FROM run_coverage c
                        WHERE c.tenant_id = b.tenant_id
                            AND c.study_id = b.study_id
                            AND c.stage = b.stage
                            AND c.pack_key = b.pack_key
                            AND c.dataset = b.dataset
                            AND c.pos > b.last_pos
                        ORDER BY c.pos ASC
                        LIMIT 1
                    ) cov_after ON TRUE
                    LEFT JOIN LATERAL (
                        SELECT c.recorded_at AS prior_resolved_at
                        FROM run_coverage c
                        WHERE c.tenant_id = b.tenant_id
                            AND c.study_id = b.study_id
                            AND c.stage = b.stage
                            AND c.pack_key = b.pack_key
                            AND c.dataset = b.dataset
                            AND c.pos > b.first_pos
                            AND c.pos < b.last_pos
                            AND NOT EXISTS (
                                SELECT 1
                                FROM "kernel"."validation_detections" dx
                                WHERE dx.tenant_id = b.tenant_id
                                    AND dx.finding_id = b.finding_id
                                    AND dx.run_id = c.run_id
                            )
                        ORDER BY c.pos DESC
                        LIMIT 1
                    ) prior_res ON TRUE
                    LEFT JOIN LATERAL (
                        SELECT c.recorded_at AS latest_clean_run_at
                        FROM run_coverage c
                        WHERE c.tenant_id = b.tenant_id
                            AND c.study_id = b.study_id
                            AND c.stage = b.stage
                            AND c.pack_key = b.pack_key
                            AND c.dataset = b.dataset
                            AND c.pos > b.first_pos
                            AND NOT EXISTS (
                                SELECT 1
                                FROM "kernel"."validation_detections" dx
                                WHERE dx.tenant_id = b.tenant_id
                                    AND dx.finding_id = b.finding_id
                                    AND dx.run_id = c.run_id
                            )
                        ORDER BY c.pos DESC
                        LIMIT 1
                    ) clean_any ON TRUE
                )
                SELECT
                    fd.tenant_id,
                    fd.finding_id,
                    fd.study_id,
                    fd.stage,
                    fd.pack_key,
                    fd.dataset,
                    fd.rule_id,
                    fd.target_kind,
                    fd.target_key,
                    fd.variable,
                    fd.severity,
                    CASE
                        WHEN fd.resolved_by_run_id IS NOT NULL THEN 'Resolved'
                        WHEN fd.decision_in_force THEN fd.decision
                        ELSE 'Open'
                    END AS status,
                    (
                        fd.stage <> 'Collection'
                        AND fd.severity = 'Error'
                        AND (
                            CASE
                                WHEN fd.resolved_by_run_id IS NOT NULL THEN 'Resolved'
                                WHEN fd.decision_in_force THEN fd.decision
                                ELSE 'Open'
                            END
                        ) IN ('Open', 'Investigating')
                    ) AS blocks,
                    fd.classification,
                    fd.decision,
                    fd.decided_at,
                    fd.decision_in_force,
                    fd.first_detected_run_id,
                    fd.first_detected_at,
                    fd.last_detected_run_id,
                    fd.last_detected_at,
                    fd.resolved_by_run_id,
                    fd.latest_clean_run_at,
                    1 AS projection_version
                FROM finding_derived fd
                ON CONFLICT (tenant_id, finding_id) DO UPDATE SET
                    study_id = EXCLUDED.study_id,
                    stage = EXCLUDED.stage,
                    pack_key = EXCLUDED.pack_key,
                    dataset = EXCLUDED.dataset,
                    rule_id = EXCLUDED.rule_id,
                    target_kind = EXCLUDED.target_kind,
                    target_key = EXCLUDED.target_key,
                    variable = EXCLUDED.variable,
                    severity = EXCLUDED.severity,
                    status = EXCLUDED.status,
                    blocks = EXCLUDED.blocks,
                    classification = EXCLUDED.classification,
                    decision = EXCLUDED.decision,
                    decided_at = EXCLUDED.decided_at,
                    decision_in_force = EXCLUDED.decision_in_force,
                    first_detected_run_id = EXCLUDED.first_detected_run_id,
                    first_detected_at = EXCLUDED.first_detected_at,
                    last_detected_run_id = EXCLUDED.last_detected_run_id,
                    last_detected_at = EXCLUDED.last_detected_at,
                    resolved_by_run_id = EXCLUDED.resolved_by_run_id,
                    latest_clean_run_at = EXCLUDED.latest_clean_run_at,
                    projection_version = "kernel"."validation_finding_projections".projection_version + 1;
            END LOOP;

            PERFORM pg_catalog.set_config('cadence.tenant_id', COALESCE(v_prev_tenant, ''), true);
        END
        $backfill$;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "validation_finding_projections",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                finding_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                pack_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                rule_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                target_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                variable = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                blocks = table.Column<bool>(type: "boolean", nullable: false),
                classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                decision_in_force = table.Column<bool>(type: "boolean", nullable: false),
                first_detected_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                first_detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_detected_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                last_detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                resolved_by_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                latest_clean_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                projection_version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_validation_finding_projections", x => new { x.tenant_id, x.finding_id });
                table.ForeignKey(
                    name: "FK_validation_finding_projections_studies_tenant_id_study_id",
                    columns: x => new { x.tenant_id, x.study_id },
                    principalSchema: "kernel",
                    principalTable: "studies",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_finding_projections_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_finding_projections_validation_detections_tenant~",
                    columns: x => new { x.tenant_id, x.first_detected_run_id, x.finding_id },
                    principalSchema: "kernel",
                    principalTable: "validation_detections",
                    principalColumns: new[] { "tenant_id", "run_id", "finding_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_finding_projections_validation_detections_tenan~1",
                    columns: x => new { x.tenant_id, x.last_detected_run_id, x.finding_id },
                    principalSchema: "kernel",
                    principalTable: "validation_detections",
                    principalColumns: new[] { "tenant_id", "run_id", "finding_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_validation_finding_projections_validation_runs_tenant_id_re~",
                    columns: x => new { x.tenant_id, x.resolved_by_run_id },
                    principalSchema: "kernel",
                    principalTable: "validation_runs",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_validation_finding_projections_tenant_id_first_detected_run~",
            schema: "kernel",
            table: "validation_finding_projections",
            columns: new[] { "tenant_id", "first_detected_run_id", "finding_id" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_finding_projections_tenant_id_last_detected_run_~",
            schema: "kernel",
            table: "validation_finding_projections",
            columns: new[] { "tenant_id", "last_detected_run_id", "finding_id" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_finding_projections_tenant_id_resolved_by_run_id",
            schema: "kernel",
            table: "validation_finding_projections",
            columns: new[] { "tenant_id", "resolved_by_run_id" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_finding_projections_tenant_id_study_id_blocks",
            schema: "kernel",
            table: "validation_finding_projections",
            columns: new[] { "tenant_id", "study_id", "blocks" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_finding_projections_tenant_id_study_id_stage_pac~",
            schema: "kernel",
            table: "validation_finding_projections",
            columns: new[] { "tenant_id", "study_id", "stage", "pack_key", "dataset" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_finding_projections_tenant_id_study_id_status_se~",
            schema: "kernel",
            table: "validation_finding_projections",
            columns: new[] { "tenant_id", "study_id", "status", "severity" });

        migrationBuilder.CreateIndex(
            name: "IX_validation_finding_projections_tenant_id_study_id_target_key",
            schema: "kernel",
            table: "validation_finding_projections",
            columns: new[] { "tenant_id", "study_id", "target_key" });

        migrationBuilder.EnableTenantIsolation("kernel", "validation_finding_projections");
        migrationBuilder.Sql(BackfillSql);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableTenantIsolation("kernel", "validation_finding_projections");

        migrationBuilder.DropTable(
            name: "validation_finding_projections",
            schema: "kernel");
    }
}
