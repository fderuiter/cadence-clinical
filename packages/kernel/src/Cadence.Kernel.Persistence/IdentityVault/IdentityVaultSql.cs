using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cadence.Kernel.Persistence.IdentityVault;

/// <summary>
/// The IdentityVault schema (spec §9.2, ADR-2253, ADR-2458): <c>identity_vault.identity_records</c> holds sealed direct
/// identifiers and <c>identity_vault.participant_links</c> the cross-study links (K-IDN-2). Both tables are
/// tenant-scoped with forced Row-Level Security, like the kernel's. The application role has no privilege on either
/// table: it reaches them only through the <c>SECURITY DEFINER</c> functions below, which act on the current tenant
/// alone, so a dump or a query as the application role reads no identifier, sealed or not, and a vault write still runs
/// in the command's transaction. The tables are not in the EF model; kernel persistence calls the functions.
/// </summary>
internal static class IdentityVaultSql
{
    /// <summary>The vault's schema.</summary>
    public const string Schema = "identity_vault";

    /// <summary>SQLSTATE raised when a write targets an erased record.</summary>
    public const string ErasedSqlState = "KV001";

    /// <summary>SQLSTATE raised when a participant is linked to a second person.</summary>
    public const string LinkConflictSqlState = "KV002";

    /// <summary>The vault's tables.</summary>
    public static readonly string[] Tables = ["identity_records", "participant_links"];

    /// <summary>Every vault function with its argument types, as <c>GRANT EXECUTE</c> names them.</summary>
    public static readonly string[] Functions =
    [
        "put_record(text, uuid, text, bytea, bytea, bytea, timestamptz)",
        "shred_record(text, uuid, timestamptz)",
        "get_record(text, uuid)",
        "find_subject_by_blind_index(text, bytea)",
        "put_link(uuid, uuid, timestamptz)",
        "linked_person(uuid)",
        "linked_participants(uuid)",
    ];

    private const string CurrentTenant = "kernel.current_tenant_id()";

    public const string Create = $$"""
        CREATE SCHEMA identity_vault;
        REVOKE ALL ON SCHEMA identity_vault FROM PUBLIC;

        CREATE TABLE identity_vault.identity_records (
            tenant_id uuid NOT NULL REFERENCES kernel.tenants (id) ON DELETE RESTRICT,
            layer text NOT NULL CHECK (layer IN ('person', 'site_local')),
            subject_id uuid NOT NULL,
            key_id text NULL CHECK (char_length(key_id) <= 64),
            wrapped_data_key bytea NULL,
            ciphertext bytea NULL,
            blind_index bytea NULL,
            recorded_at timestamptz NOT NULL,
            erased_at timestamptz NULL,
            PRIMARY KEY (tenant_id, layer, subject_id),
            CONSTRAINT ck_identity_records_sealed_until_erased CHECK (
                (erased_at IS NULL AND key_id IS NOT NULL AND wrapped_data_key IS NOT NULL AND ciphertext IS NOT NULL)
                OR (erased_at IS NOT NULL AND key_id IS NULL AND wrapped_data_key IS NULL AND ciphertext IS NULL AND blind_index IS NULL))
        );
        CREATE INDEX ix_identity_records_blind_index ON identity_vault.identity_records (tenant_id, layer, blind_index)
            WHERE blind_index IS NOT NULL;

        CREATE TABLE identity_vault.participant_links (
            tenant_id uuid NOT NULL,
            participant_id uuid NOT NULL,
            person_id uuid NOT NULL REFERENCES kernel.people (id) ON DELETE RESTRICT,
            linked_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id, participant_id),
            FOREIGN KEY (tenant_id, participant_id) REFERENCES kernel.participants (tenant_id, id) ON DELETE RESTRICT
        );
        CREATE INDEX ix_participant_links_person ON identity_vault.participant_links (tenant_id, person_id);

        ALTER TABLE identity_vault.identity_records ENABLE ROW LEVEL SECURITY;
        ALTER TABLE identity_vault.identity_records FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON identity_vault.identity_records
            USING (tenant_id = {{CurrentTenant}}) WITH CHECK (tenant_id = {{CurrentTenant}});
        ALTER TABLE identity_vault.participant_links ENABLE ROW LEVEL SECURITY;
        ALTER TABLE identity_vault.participant_links FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON identity_vault.participant_links
            USING (tenant_id = {{CurrentTenant}}) WITH CHECK (tenant_id = {{CurrentTenant}});

        CREATE FUNCTION identity_vault.require_tenant() RETURNS uuid
        LANGUAGE plpgsql STABLE SET search_path = pg_catalog, pg_temp AS $fn$
        DECLARE t uuid := {{CurrentTenant}};
        BEGIN
            IF t IS NULL THEN
                RAISE EXCEPTION 'identity_vault: no tenant is set on this connection' USING ERRCODE = '42501';
            END IF;
            RETURN t;
        END
        $fn$;
        REVOKE ALL ON FUNCTION identity_vault.require_tenant() FROM PUBLIC;

        CREATE FUNCTION identity_vault.put_record(
            p_layer text, p_subject uuid, p_key_id text, p_wrapped_data_key bytea, p_ciphertext bytea, p_blind_index bytea,
            p_recorded_at timestamptz) RETURNS void
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
        DECLARE t uuid := identity_vault.require_tenant();
        BEGIN
            INSERT INTO identity_vault.identity_records AS r
                (tenant_id, layer, subject_id, key_id, wrapped_data_key, ciphertext, blind_index, recorded_at)
            VALUES (t, p_layer, p_subject, p_key_id, p_wrapped_data_key, p_ciphertext, p_blind_index, p_recorded_at)
            ON CONFLICT (tenant_id, layer, subject_id) DO UPDATE
                SET key_id = excluded.key_id, wrapped_data_key = excluded.wrapped_data_key, ciphertext = excluded.ciphertext,
                    blind_index = excluded.blind_index, recorded_at = excluded.recorded_at
                WHERE r.erased_at IS NULL;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'identity_vault: the record was erased' USING ERRCODE = '{{ErasedSqlState}}';
            END IF;
        END
        $fn$;

        CREATE FUNCTION identity_vault.shred_record(p_layer text, p_subject uuid, p_erased_at timestamptz) RETURNS void
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
        DECLARE t uuid := identity_vault.require_tenant();
        BEGIN
            UPDATE identity_vault.identity_records
                SET key_id = NULL, wrapped_data_key = NULL, ciphertext = NULL, blind_index = NULL, erased_at = p_erased_at
                WHERE tenant_id = t AND layer = p_layer AND subject_id = p_subject AND erased_at IS NULL;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'identity_vault: no unerased record to shred' USING ERRCODE = '{{ErasedSqlState}}';
            END IF;
        END
        $fn$;

        CREATE FUNCTION identity_vault.get_record(p_layer text, p_subject uuid)
            RETURNS TABLE (key_id text, wrapped_data_key bytea, ciphertext bytea, blind_index bytea, recorded_at timestamptz, erased_at timestamptz)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
            SELECT r.key_id, r.wrapped_data_key, r.ciphertext, r.blind_index, r.recorded_at, r.erased_at
            FROM identity_vault.identity_records r
            WHERE r.tenant_id = identity_vault.require_tenant() AND r.layer = p_layer AND r.subject_id = p_subject
        $fn$;

        CREATE FUNCTION identity_vault.find_subject_by_blind_index(p_layer text, p_blind_index bytea) RETURNS uuid
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
            SELECT r.subject_id
            FROM identity_vault.identity_records r
            WHERE r.tenant_id = identity_vault.require_tenant()
              AND r.layer = p_layer
              AND r.blind_index = p_blind_index
              AND r.erased_at IS NULL
            ORDER BY r.recorded_at ASC, r.subject_id ASC
            LIMIT 1
        $fn$;

        CREATE FUNCTION identity_vault.put_link(p_participant uuid, p_person uuid, p_linked_at timestamptz) RETURNS void
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
        DECLARE t uuid := identity_vault.require_tenant();
        BEGIN
            INSERT INTO identity_vault.participant_links (tenant_id, participant_id, person_id, linked_at)
            VALUES (t, p_participant, p_person, p_linked_at)
            ON CONFLICT (tenant_id, participant_id) DO NOTHING;
            IF NOT EXISTS (SELECT 1 FROM identity_vault.participant_links l
                           WHERE l.tenant_id = t AND l.participant_id = p_participant AND l.person_id = p_person) THEN
                RAISE EXCEPTION 'identity_vault: the participant is linked to another person' USING ERRCODE = '{{LinkConflictSqlState}}';
            END IF;
        END
        $fn$;

        CREATE FUNCTION identity_vault.linked_person(p_participant uuid) RETURNS uuid
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
            SELECT l.person_id FROM identity_vault.participant_links l
            WHERE l.tenant_id = identity_vault.require_tenant() AND l.participant_id = p_participant
        $fn$;

        CREATE FUNCTION identity_vault.linked_participants(p_person uuid) RETURNS SETOF uuid
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
            SELECT l.participant_id FROM identity_vault.participant_links l
            WHERE l.tenant_id = identity_vault.require_tenant() AND l.person_id = p_person
            ORDER BY l.participant_id
        $fn$;

        REVOKE ALL ON FUNCTION identity_vault.put_record(text, uuid, text, bytea, bytea, bytea, timestamptz) FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity_vault.shred_record(text, uuid, timestamptz) FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity_vault.get_record(text, uuid) FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity_vault.find_subject_by_blind_index(text, bytea) FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity_vault.put_link(uuid, uuid, timestamptz) FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity_vault.linked_person(uuid) FROM PUBLIC;
        REVOKE ALL ON FUNCTION identity_vault.linked_participants(uuid) FROM PUBLIC;
        """;

    public const string CreateBlindIndexLookup = """
        CREATE OR REPLACE FUNCTION identity_vault.find_subject_by_blind_index(p_layer text, p_blind_index bytea) RETURNS uuid
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
            SELECT r.subject_id
            FROM identity_vault.identity_records r
            WHERE r.tenant_id = identity_vault.require_tenant()
              AND r.layer = p_layer
              AND r.blind_index = p_blind_index
              AND r.erased_at IS NULL
            ORDER BY r.recorded_at ASC, r.subject_id ASC
            LIMIT 1
        $fn$;
        REVOKE ALL ON FUNCTION identity_vault.find_subject_by_blind_index(text, bytea) FROM PUBLIC;
        DO $grant$
        DECLARE r record;
        BEGIN
            FOR r IN
                SELECT DISTINCT grantee
                FROM information_schema.routine_privileges
                WHERE specific_schema = 'identity_vault'
                  AND routine_name = 'get_record'
                  AND privilege_type = 'EXECUTE'
                  AND grantee NOT IN ('PUBLIC', current_user)
            LOOP
                EXECUTE format('GRANT EXECUTE ON FUNCTION identity_vault.find_subject_by_blind_index(text, bytea) TO %I', r.grantee);
            END LOOP;
        END
        $grant$;
        """;

    public const string DropBlindIndexLookup = "DROP FUNCTION IF EXISTS identity_vault.find_subject_by_blind_index(text, bytea);";

    /// <summary>SQLSTATE raised when the migration finds direct identifiers on <c>kernel.people</c>.</summary>
    public const string PeopleHoldIdentifiersSqlState = "KV003";

    /// <summary>
    /// Refuses to drop the identifier columns of <c>kernel.people</c> while any row holds a value in them. No kernel
    /// command ever wrote them, so they hold nothing to move, and this proves it at migration time: the columns are
    /// empty, or hold the placeholders the migration's <c>Down</c> writes (the id as the e-mail, blank names), or the
    /// migration fails and drops nothing. Row security is switched off for the check, so a migration role that cannot
    /// see every row fails instead of passing on the rows it can see.
    /// </summary>
    public const string RequireNoPersonIdentifiers = $$"""
        DO $guard$
        BEGIN
            PERFORM set_config('row_security', 'off', true);
            IF EXISTS (SELECT 1 FROM kernel.people p
                       WHERE p.email NOT IN ('', p.id::text) OR p.first_name <> '' OR p.last_name <> '') THEN
                RAISE EXCEPTION 'kernel.people holds direct identifiers; move them into the IdentityVault before this migration'
                    USING ERRCODE = '{{PeopleHoldIdentifiersSqlState}}';
            END IF;
            PERFORM set_config('row_security', 'on', true);
        END
        $guard$;
        """;

    public const string Drop = "DROP SCHEMA IF EXISTS identity_vault CASCADE;";

    /// <summary>
    /// Grants <paramref name="quotedRole"/> the vault functions and nothing else: no table privilege in the vault schema.
    /// Part of <see cref="KernelDatabase.GrantAppRoleSql"/>.
    /// </summary>
    public static string GrantSql(string quotedRole) =>
        $"GRANT USAGE ON SCHEMA {Schema} TO {quotedRole};\n"
        + $"REVOKE ALL ON ALL TABLES IN SCHEMA {Schema} FROM {quotedRole};\n"
        + string.Concat(Array.ConvertAll(Functions, f => $"GRANT EXECUTE ON FUNCTION {Schema}.{f} TO {quotedRole};\n"));

    /// <summary>Applies one staged write through the vault functions, on <paramref name="db"/>'s connection and transaction.</summary>
    public static Task ApplyAsync(KernelDbContext db, IIdentityVaultWrite write, DateTimeOffset recordedAt, CancellationToken cancellationToken) =>
        write switch
        {
            PutIdentityRecord put => db.Database.ExecuteSqlAsync(
                $"SELECT identity_vault.put_record({IdentitySealer.LayerToken(put.Layer)}, {put.SubjectId}, {put.Sealed.KeyId}, {put.Sealed.WrappedDataKey}, {put.Sealed.Ciphertext}, {put.Sealed.BlindIndex}, {recordedAt})",
                cancellationToken),
            ShredIdentityRecord shred => db.Database.ExecuteSqlAsync(
                $"SELECT identity_vault.shred_record({IdentitySealer.LayerToken(shred.Layer)}, {shred.SubjectId}, {recordedAt})",
                cancellationToken),
            PutParticipantLink link => db.Database.ExecuteSqlAsync(
                $"SELECT identity_vault.put_link({link.ParticipantId.Value}, {link.PersonId.Value}, {recordedAt})",
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown identity vault write."),
        };
}

/// <summary>Migration helpers for <see cref="IdentityVaultSql"/>.</summary>
internal static class IdentityVaultMigrationBuilderExtensions
{
    public static void CreateIdentityVault(this MigrationBuilder migrationBuilder) => migrationBuilder.Sql(IdentityVaultSql.Create);

    public static void DropIdentityVault(this MigrationBuilder migrationBuilder) => migrationBuilder.Sql(IdentityVaultSql.Drop);

    public static void CreateIdentityVaultBlindIndexLookup(this MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(IdentityVaultSql.CreateBlindIndexLookup);

    public static void DropIdentityVaultBlindIndexLookup(this MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(IdentityVaultSql.DropBlindIndexLookup);

    public static void RequireNoPersonIdentifiers(this MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(IdentityVaultSql.RequireNoPersonIdentifiers);
}
