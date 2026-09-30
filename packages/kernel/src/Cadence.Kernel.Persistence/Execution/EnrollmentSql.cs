using Cadence.Kernel.Persistence.Bitemporal;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Kernel.Persistence.Execution;

/// <summary>
/// DDL that repeats the enrollment rules (spec §7.4) in PostgreSQL, whoever the database user is.
/// <list type="bullet">
/// <item>A trigger on <c>enrollments</c> raises SQLSTATE <see cref="ViolationSqlState"/> when a row is inserted other
/// than in Screening without a number or withdrawal, is deleted or truncated, changes its identity (participant, study,
/// study site, subject code), leaves a terminal status, moves off the state machine or backwards in valid time,
/// changes an assigned number, or is withdrawn without its kind and attestation.</item>
/// <item>A deferred exclusion constraint on <c>enrollment_status_periods</c> keeps a participant to one active
/// enrollment per study site at any valid instant, among the rows Cadence currently asserts.</item>
/// </list>
/// The status history's own K-TIME-1 and K-TIME-2 guards come from <see cref="BitemporalSql"/>.
/// </summary>
internal static class EnrollmentSql
{
    public const string Table = "enrollments";

    public const string PeriodsTable = "enrollment_status_periods";

    public const string FunctionName = "enforce_enrollment_lifecycle";

    public const string ActiveGuardName = "ex_enrollment_status_periods_one_active";

    /// <summary>SQLSTATE raised when a change breaks the enrollment rules.</summary>
    public const string ViolationSqlState = "KE001";

    public static string CreateGuards(string schema)
    {
        var s = SqlIdentifier.Quote(schema);
        var table = $"{s}.{SqlIdentifier.Quote(Table)}";
        var periods = $"{s}.{SqlIdentifier.Quote(PeriodsTable)}";
        return $$"""
            CREATE OR REPLACE FUNCTION {{s}}.{{FunctionName}}() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                    RAISE EXCEPTION 'K-ENR: enrollments are never deleted (%)', TG_OP USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF TG_OP = 'INSERT' THEN
                    IF NEW.status <> 'Screening' OR NEW.enrollment_number IS NOT NULL
                        OR NEW.withdrawal_kind IS NOT NULL OR NEW.withdrawal_attestation_id IS NOT NULL THEN
                        RAISE EXCEPTION 'K-ENR: an enrollment starts in Screening without a number or withdrawal'
                            USING ERRCODE = '{{ViolationSqlState}}';
                    END IF;
                    RETURN NEW;
                END IF;

                IF OLD.status IN ('ScreenFailed', 'Completed', 'Withdrawn') THEN
                    RAISE EXCEPTION 'K-ENR: enrollment % is % and final', OLD.id, OLD.status USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF NEW.id <> OLD.id OR NEW.tenant_id <> OLD.tenant_id OR NEW.participant_id <> OLD.participant_id
                    OR NEW.study_id <> OLD.study_id OR NEW.study_site_id <> OLD.study_site_id
                    OR NEW.subject_code <> OLD.subject_code THEN
                    RAISE EXCEPTION 'K-ENR: the identity of enrollment % cannot change', OLD.id USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF NEW.status <> OLD.status AND (OLD.status, NEW.status) NOT IN (
                    ('Screening', 'ScreenFailed'), ('Screening', 'Enrolled'), ('Screening', 'Withdrawn'),
                    ('Enrolled', 'Randomized'), ('Enrolled', 'Active'), ('Enrolled', 'Withdrawn'),
                    ('Randomized', 'Active'), ('Randomized', 'Withdrawn'),
                    ('Active', 'Completed'), ('Active', 'Withdrawn')) THEN
                    RAISE EXCEPTION 'K-ENR: enrollment % cannot move from % to %', OLD.id, OLD.status, NEW.status
                        USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF (NEW.status = OLD.status AND NEW.status_since <> OLD.status_since)
                    OR (NEW.status <> OLD.status AND NEW.status_since <= OLD.status_since) THEN
                    RAISE EXCEPTION 'K-ENR: enrollment % changes status only forwards in valid time', OLD.id
                        USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF (OLD.enrollment_number IS NOT NULL AND NEW.enrollment_number IS DISTINCT FROM OLD.enrollment_number)
                    OR (OLD.enrollment_number IS NULL AND NEW.enrollment_number IS NOT NULL
                        AND NOT (OLD.status = 'Screening' AND NEW.status = 'Enrolled'))
                    OR (NEW.status = 'Enrolled' AND NEW.enrollment_number IS NULL) THEN
                    RAISE EXCEPTION 'K-ENR: enrollment % is numbered once, on entering Enrolled', OLD.id
                        USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF (NEW.status = 'Withdrawn') <> (NEW.withdrawal_kind IS NOT NULL AND NEW.withdrawal_attestation_id IS NOT NULL)
                    OR (NEW.status <> 'Withdrawn' AND (NEW.withdrawal_kind IS NOT NULL OR NEW.withdrawal_attestation_id IS NOT NULL)) THEN
                    RAISE EXCEPTION 'K-ENR: withdrawal of enrollment % needs its kind and attestation', OLD.id
                        USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                RETURN NEW;
            END;
            $fn$;

            CREATE TRIGGER trg_enrollments_lifecycle BEFORE INSERT OR UPDATE OR DELETE ON {{table}}
                FOR EACH ROW EXECUTE FUNCTION {{s}}.{{FunctionName}}();
            CREATE TRIGGER trg_enrollments_no_truncate BEFORE TRUNCATE ON {{table}}
                FOR EACH STATEMENT EXECUTE FUNCTION {{s}}.{{FunctionName}}();

            ALTER TABLE {{periods}} ADD CONSTRAINT {{ActiveGuardName}}
                EXCLUDE USING gist (tenant_id WITH =, participant_id WITH =, study_site_id WITH =,
                    {{BitemporalEntityTypeBuilderExtensions.ValidPeriodColumn}} WITH &&, {{BitemporalEntityTypeBuilderExtensions.RecordedPeriodColumn}} WITH &&)
                WHERE (status IN ('Screening', 'Enrolled', 'Randomized', 'Active'))
                DEFERRABLE INITIALLY DEFERRED;
            """;
    }

    public static string DropGuards(string schema)
    {
        var s = SqlIdentifier.Quote(schema);
        var table = $"{s}.{SqlIdentifier.Quote(Table)}";
        var periods = $"{s}.{SqlIdentifier.Quote(PeriodsTable)}";
        return $"""
            ALTER TABLE {periods} DROP CONSTRAINT IF EXISTS {ActiveGuardName};
            DROP TRIGGER IF EXISTS trg_enrollments_no_truncate ON {table};
            DROP TRIGGER IF EXISTS trg_enrollments_lifecycle ON {table};
            DROP FUNCTION IF EXISTS {s}.{FunctionName}();
            """;
    }
}

/// <summary>Migration helpers for <see cref="EnrollmentSql"/>.</summary>
internal static class EnrollmentMigrationBuilderExtensions
{
    public static void CreateEnrollmentGuards(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(EnrollmentSql.CreateGuards(schema));

    public static void DropEnrollmentGuards(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(EnrollmentSql.DropGuards(schema));
}
