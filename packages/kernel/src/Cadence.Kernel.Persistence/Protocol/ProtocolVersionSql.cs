using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Kernel.Persistence.Protocol;

/// <summary>
/// DDL that repeats the protocol version lifecycle (spec §6.2) in PostgreSQL, whoever the database user is. A trigger
/// raises SQLSTATE <see cref="ViolationSqlState"/> when:
/// <list type="bullet">
/// <item>a row is inserted other than as a draft without a hash or attestations, or deleted or truncated;</item>
/// <item>a version's identity (tenant, study, number, parent) changes;</item>
/// <item>content or amendment changes after the version left draft;</item>
/// <item>the status moves other than <c>Draft→InReview→Draft|Approved|Withdrawn</c>, <c>Approved→Superseded|Withdrawn</c>;</item>
/// <item>the content hash is missing outside draft, present in draft, or changes after review;</item>
/// <item>an approval or withdrawal lacks its attestation (and reason), or a terminal version changes at all.</item>
/// </list>
/// </summary>
internal static class ProtocolVersionSql
{
    public const string Table = "protocol_versions";

    public const string FunctionName = "enforce_protocol_version_lifecycle";

    /// <summary>SQLSTATE raised when a change breaks the lifecycle.</summary>
    public const string ViolationSqlState = "KA002";

    public static string CreateLifecycleGuards(string schema)
    {
        var s = SqlIdentifier.Quote(schema);
        var table = $"{s}.{SqlIdentifier.Quote(Table)}";
        return $$"""
            CREATE OR REPLACE FUNCTION {{s}}.{{FunctionName}}() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
                    RAISE EXCEPTION 'K-PROT: protocol versions are never deleted (%)', TG_OP USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF TG_OP = 'INSERT' THEN
                    IF NEW.status <> 'Draft' OR NEW.content_hash IS NOT NULL
                        OR NEW.approval_attestation_id IS NOT NULL OR NEW.withdrawal_attestation_id IS NOT NULL
                        OR NEW.withdrawal_reason IS NOT NULL THEN
                        RAISE EXCEPTION 'K-PROT: a protocol version starts as a draft without a hash or attestations'
                            USING ERRCODE = '{{ViolationSqlState}}';
                    END IF;
                    RETURN NEW;
                END IF;

                IF OLD.status IN ('Superseded', 'Withdrawn') THEN
                    RAISE EXCEPTION 'K-PROT: protocol version % is % and final', OLD.id, OLD.status USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF NEW.id <> OLD.id OR NEW.tenant_id <> OLD.tenant_id OR NEW.study_id <> OLD.study_id
                    OR NEW.number <> OLD.number OR NEW.parent_version_id IS DISTINCT FROM OLD.parent_version_id THEN
                    RAISE EXCEPTION 'K-PROT: the identity of protocol version % cannot change', OLD.id USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF OLD.status <> 'Draft'
                    AND (NEW.content IS DISTINCT FROM OLD.content OR NEW.amendment IS DISTINCT FROM OLD.amendment) THEN
                    RAISE EXCEPTION 'K-PROT: protocol version % is % and its content is frozen', OLD.id, OLD.status
                        USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF NEW.status <> OLD.status AND (OLD.status, NEW.status) NOT IN (
                    ('Draft', 'InReview'), ('InReview', 'Draft'), ('InReview', 'Approved'), ('InReview', 'Withdrawn'),
                    ('Approved', 'Superseded'), ('Approved', 'Withdrawn')) THEN
                    RAISE EXCEPTION 'K-PROT: protocol version % cannot move from % to %', OLD.id, OLD.status, NEW.status
                        USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF (NEW.status = 'Draft') <> (NEW.content_hash IS NULL)
                    OR (OLD.status <> 'Draft' AND NEW.status <> 'Draft' AND NEW.content_hash IS DISTINCT FROM OLD.content_hash) THEN
                    RAISE EXCEPTION 'K-PROT: the content hash of protocol version % is set on review and never changes after', OLD.id
                        USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                IF (OLD.approval_attestation_id IS NOT NULL AND NEW.approval_attestation_id IS DISTINCT FROM OLD.approval_attestation_id)
                    OR (NEW.status IN ('Approved', 'Superseded') AND NEW.approval_attestation_id IS NULL)
                    OR (NEW.status = 'Withdrawn' AND (NEW.withdrawal_attestation_id IS NULL OR NEW.withdrawal_reason IS NULL))
                    OR (NEW.status <> 'Withdrawn' AND (NEW.withdrawal_attestation_id IS NOT NULL OR NEW.withdrawal_reason IS NOT NULL)) THEN
                    RAISE EXCEPTION 'K-PROT: approval and withdrawal of protocol version % need their attestation', OLD.id
                        USING ERRCODE = '{{ViolationSqlState}}';
                END IF;

                RETURN NEW;
            END;
            $fn$;

            CREATE TRIGGER trg_protocol_versions_lifecycle BEFORE INSERT OR UPDATE OR DELETE ON {{table}}
                FOR EACH ROW EXECUTE FUNCTION {{s}}.{{FunctionName}}();
            CREATE TRIGGER trg_protocol_versions_no_truncate BEFORE TRUNCATE ON {{table}}
                FOR EACH STATEMENT EXECUTE FUNCTION {{s}}.{{FunctionName}}();
            """;
    }

    public static string DropLifecycleGuards(string schema)
    {
        var s = SqlIdentifier.Quote(schema);
        var table = $"{s}.{SqlIdentifier.Quote(Table)}";
        return $"""
            DROP TRIGGER IF EXISTS trg_protocol_versions_no_truncate ON {table};
            DROP TRIGGER IF EXISTS trg_protocol_versions_lifecycle ON {table};
            DROP FUNCTION IF EXISTS {s}.{FunctionName}();
            """;
    }
}

/// <summary>Migration helpers for <see cref="ProtocolVersionSql"/>.</summary>
internal static class ProtocolVersionMigrationBuilderExtensions
{
    public static void CreateProtocolVersionLifecycleGuards(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(ProtocolVersionSql.CreateLifecycleGuards(schema));

    public static void DropProtocolVersionLifecycleGuards(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(ProtocolVersionSql.DropLifecycleGuards(schema));
}
