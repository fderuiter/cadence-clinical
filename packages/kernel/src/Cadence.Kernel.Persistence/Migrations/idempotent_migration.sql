DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'kernel') THEN
        CREATE SCHEMA kernel;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS kernel.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'kernel') THEN
            CREATE SCHEMA kernel;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE TABLE kernel.audit_log_entries (
        id uuid NOT NULL,
        entity_name character varying(255) NOT NULL,
        entity_id character varying(255) NOT NULL,
        action character varying(50) NOT NULL,
        timestamp timestamp with time zone NOT NULL,
        user_id character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        changes_json text,
        predecessor_hash character varying(64) NOT NULL,
        block_hash character varying(64) NOT NULL,
        CONSTRAINT "PK_audit_log_entries" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE TABLE kernel.people (
        id uuid NOT NULL,
        email character varying(320) NOT NULL,
        first_name character varying(255) NOT NULL,
        last_name character varying(255) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_people" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE TABLE kernel.tenants (
        id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(255) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_tenants" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE TABLE kernel.organizations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(64) NOT NULL,
        name character varying(255) NOT NULL,
        kind character varying(32) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_organizations" PRIMARY KEY (id),
        CONSTRAINT "FK_organizations_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE TABLE kernel.sites (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        institution_id uuid NOT NULL,
        site_code character varying(64) NOT NULL,
        name character varying(255) NOT NULL,
        country character varying(2) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_sites" PRIMARY KEY (id),
        CONSTRAINT "FK_sites_organizations_institution_id" FOREIGN KEY (institution_id) REFERENCES kernel.organizations (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_sites_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE TABLE kernel.studies (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        sponsor_id uuid NOT NULL,
        study_code character varying(64) NOT NULL,
        title character varying(1000) NOT NULL,
        phase character varying(32) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_studies" PRIMARY KEY (id),
        CONSTRAINT "FK_studies_organizations_sponsor_id" FOREIGN KEY (sponsor_id) REFERENCES kernel.organizations (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_studies_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE TABLE kernel.study_sites (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        site_id uuid NOT NULL,
        status character varying(32) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_study_sites" PRIMARY KEY (id),
        CONSTRAINT "FK_study_sites_sites_site_id" FOREIGN KEY (site_id) REFERENCES kernel.sites (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_study_sites_studies_study_id" FOREIGN KEY (study_id) REFERENCES kernel.studies (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_study_sites_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE INDEX "IX_audit_log_entries_entity_name_timestamp" ON kernel.audit_log_entries (entity_name, timestamp);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE UNIQUE INDEX "IX_organizations_tenant_id_code" ON kernel.organizations (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE UNIQUE INDEX "IX_people_email" ON kernel.people (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE UNIQUE INDEX "IX_sites_institution_id_site_code" ON kernel.sites (institution_id, site_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE INDEX "IX_sites_tenant_id" ON kernel.sites (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE INDEX "IX_studies_sponsor_id" ON kernel.studies (sponsor_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE UNIQUE INDEX "IX_studies_tenant_id_study_code" ON kernel.studies (tenant_id, study_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE INDEX "IX_study_sites_site_id" ON kernel.study_sites (site_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE UNIQUE INDEX "IX_study_sites_study_id_site_id" ON kernel.study_sites (study_id, site_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE INDEX "IX_study_sites_tenant_id" ON kernel.study_sites (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    CREATE UNIQUE INDEX "IX_tenants_code" ON kernel.tenants (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929191437_InitialKernelIdentity') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260929191437_InitialKernelIdentity', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929195617_BitemporalFoundation') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'kernel') THEN
            CREATE SCHEMA kernel;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929195617_BitemporalFoundation') THEN
    CREATE EXTENSION IF NOT EXISTS btree_gist;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929195617_BitemporalFoundation') THEN
    CREATE OR REPLACE FUNCTION "kernel".enforce_bitemporal_append_only() RETURNS trigger
    LANGUAGE plpgsql AS $fn$
    BEGIN
        IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
            RAISE EXCEPTION 'K-TIME-1: %.% is bitemporal and append-only; % is not allowed',
                TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = 'KT001';
        END IF;
        IF (to_jsonb(NEW) - 'recorded_period' - 'version_index') IS DISTINCT FROM (to_jsonb(OLD) - 'recorded_period' - 'version_index') THEN
            RAISE EXCEPTION 'K-TIME-1: %.% rows are never updated; record a new row and close recorded_period instead',
                TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = 'KT001';
        END IF;
        IF NOT upper_inf(OLD.recorded_period)
            OR upper_inf(NEW.recorded_period)
            OR isempty(NEW.recorded_period)
            OR lower(NEW.recorded_period) IS DISTINCT FROM lower(OLD.recorded_period) THEN
            RAISE EXCEPTION 'K-TIME-1: %.% may only close an open recorded_period at a later instant',
                TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = 'KT001';
        END IF;
        RETURN NEW;
    END;
    $fn$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929195617_BitemporalFoundation') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260929195617_BitemporalFoundation', '10.0.11');
    END IF;
END $EF$;
COMMIT;

