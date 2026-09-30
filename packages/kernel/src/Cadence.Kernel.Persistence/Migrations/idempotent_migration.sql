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

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    DROP TABLE kernel.audit_log_entries;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TABLE kernel.audit_chain_heads (
        tenant_id uuid NOT NULL,
        last_sequence bigint NOT NULL,
        last_hash character varying(64) NOT NULL,
        CONSTRAINT "PK_audit_chain_heads" PRIMARY KEY (tenant_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TABLE kernel.audit_entries (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        sequence bigint NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        actor_id character varying(256) NOT NULL,
        action character varying(64) NOT NULL,
        aggregate_type character varying(200),
        aggregate_id character varying(200),
        aggregate_version bigint,
        reason_for_change character varying(2000) NOT NULL,
        activity_id uuid NOT NULL,
        changes text NOT NULL,
        predecessor_hash character varying(64) NOT NULL,
        hash character varying(64) NOT NULL,
        CONSTRAINT "PK_audit_entries" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TABLE kernel.domain_events (
        event_id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        type character varying(200) NOT NULL,
        schema_version integer NOT NULL,
        occurred_valid timestamp with time zone,
        recorded timestamp with time zone NOT NULL,
        actor_id character varying(256) NOT NULL,
        activity_id uuid NOT NULL,
        correlation_id uuid NOT NULL,
        causation_id uuid,
        payload jsonb NOT NULL,
        CONSTRAINT "PK_domain_events" PRIMARY KEY (event_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TABLE kernel.idempotency_records (
        tenant_id uuid NOT NULL,
        idempotency_key character varying(200) NOT NULL,
        command_type character varying(200) NOT NULL,
        request_hash character varying(64) NOT NULL,
        result_json text NOT NULL,
        activity_id uuid NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_idempotency_records" PRIMARY KEY (tenant_id, idempotency_key)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TABLE kernel.outbox_messages (
        id uuid NOT NULL,
        event_id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        type character varying(200) NOT NULL,
        envelope jsonb NOT NULL,
        enqueued_at timestamp with time zone NOT NULL,
        dispatched_at timestamp with time zone,
        attempts integer NOT NULL,
        CONSTRAINT "PK_outbox_messages" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TABLE kernel.provenance_activities (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        command_type character varying(200) NOT NULL,
        actor_id character varying(256) NOT NULL,
        actor_kind character varying(32) NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        reason_for_change character varying(2000) NOT NULL,
        idempotency_key character varying(200) NOT NULL,
        outcome character varying(32) NOT NULL,
        aggregate_type character varying(200),
        aggregate_id character varying(200),
        correlation_id uuid NOT NULL,
        causation_id uuid,
        CONSTRAINT "PK_provenance_activities" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE INDEX "IX_audit_entries_activity_id" ON kernel.audit_entries (activity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE UNIQUE INDEX "IX_audit_entries_tenant_id_sequence" ON kernel.audit_entries (tenant_id, sequence);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE INDEX "IX_domain_events_activity_id" ON kernel.domain_events (activity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE INDEX "IX_domain_events_tenant_id_recorded" ON kernel.domain_events (tenant_id, recorded);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE INDEX "IX_outbox_messages_enqueued_at" ON kernel.outbox_messages (enqueued_at) WHERE dispatched_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE UNIQUE INDEX "IX_outbox_messages_event_id" ON kernel.outbox_messages (event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE INDEX "IX_provenance_activities_correlation_id" ON kernel.provenance_activities (correlation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE INDEX "IX_provenance_activities_tenant_id_recorded_at" ON kernel.provenance_activities (tenant_id, recorded_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE OR REPLACE FUNCTION "kernel".enforce_insert_only() RETURNS trigger
    LANGUAGE plpgsql AS $fn$
    BEGIN
        RAISE EXCEPTION 'K-AUDIT: %.% is insert-only; % is not allowed',
            TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = 'KA001';
    END;
    $fn$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TRIGGER "trg_audit_entries_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."audit_entries"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_audit_entries_no_truncate" BEFORE TRUNCATE ON "kernel"."audit_entries"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TRIGGER "trg_domain_events_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."domain_events"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_domain_events_no_truncate" BEFORE TRUNCATE ON "kernel"."domain_events"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TRIGGER "trg_provenance_activities_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."provenance_activities"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_provenance_activities_no_truncate" BEFORE TRUNCATE ON "kernel"."provenance_activities"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    CREATE TRIGGER "trg_idempotency_records_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."idempotency_records"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_idempotency_records_no_truncate" BEFORE TRUNCATE ON "kernel"."idempotency_records"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929223332_CommandPipeline') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260929223332_CommandPipeline', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929234442_OutboxRelayAndChainSeals') THEN
    ALTER TABLE kernel.outbox_messages ADD last_error character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929234442_OutboxRelayAndChainSeals') THEN
    ALTER TABLE kernel.outbox_messages ADD next_attempt_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929234442_OutboxRelayAndChainSeals') THEN
    CREATE TABLE kernel.audit_chain_seals (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        sequence bigint NOT NULL,
        hash character varying(64) NOT NULL,
        sealed_at timestamp with time zone NOT NULL,
        sealed_by character varying(256) NOT NULL,
        CONSTRAINT "PK_audit_chain_seals" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929234442_OutboxRelayAndChainSeals') THEN
    CREATE INDEX "IX_outbox_messages_tenant_id_enqueued_at" ON kernel.outbox_messages (tenant_id, enqueued_at) WHERE dispatched_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929234442_OutboxRelayAndChainSeals') THEN
    CREATE UNIQUE INDEX "IX_audit_chain_seals_tenant_id_sequence" ON kernel.audit_chain_seals (tenant_id, sequence);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929234442_OutboxRelayAndChainSeals') THEN
    CREATE TRIGGER "trg_audit_chain_seals_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."audit_chain_seals"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_audit_chain_seals_no_truncate" BEFORE TRUNCATE ON "kernel"."audit_chain_seals"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260929234442_OutboxRelayAndChainSeals') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260929234442_OutboxRelayAndChainSeals', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.sites DROP CONSTRAINT "FK_sites_organizations_institution_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.studies DROP CONSTRAINT "FK_studies_organizations_sponsor_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.study_sites DROP CONSTRAINT "FK_study_sites_sites_site_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.study_sites DROP CONSTRAINT "FK_study_sites_studies_study_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    DROP INDEX kernel."IX_study_sites_site_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    DROP INDEX kernel."IX_study_sites_tenant_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    DROP INDEX kernel."IX_studies_sponsor_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    DROP INDEX kernel."IX_sites_tenant_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.study_sites ADD CONSTRAINT "AK_study_sites_tenant_id_id" UNIQUE (tenant_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.studies ADD CONSTRAINT "AK_studies_tenant_id_id" UNIQUE (tenant_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.sites ADD CONSTRAINT "AK_sites_tenant_id_id" UNIQUE (tenant_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.organizations ADD CONSTRAINT "AK_organizations_tenant_id_id" UNIQUE (tenant_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    CREATE TABLE kernel.tenant_memberships (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        person_id uuid NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_tenant_memberships" PRIMARY KEY (id),
        CONSTRAINT "AK_tenant_memberships_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_tenant_memberships_people_person_id" FOREIGN KEY (person_id) REFERENCES kernel.people (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_tenant_memberships_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    CREATE INDEX "IX_study_sites_tenant_id_site_id" ON kernel.study_sites (tenant_id, site_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    CREATE INDEX "IX_study_sites_tenant_id_study_id" ON kernel.study_sites (tenant_id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    CREATE INDEX "IX_studies_tenant_id_sponsor_id" ON kernel.studies (tenant_id, sponsor_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    CREATE INDEX "IX_sites_tenant_id_institution_id" ON kernel.sites (tenant_id, institution_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    CREATE INDEX "IX_tenant_memberships_person_id" ON kernel.tenant_memberships (person_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    CREATE UNIQUE INDEX "IX_tenant_memberships_tenant_id_person_id" ON kernel.tenant_memberships (tenant_id, person_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.sites ADD CONSTRAINT "FK_sites_organizations_tenant_id_institution_id" FOREIGN KEY (tenant_id, institution_id) REFERENCES kernel.organizations (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.studies ADD CONSTRAINT "FK_studies_organizations_tenant_id_sponsor_id" FOREIGN KEY (tenant_id, sponsor_id) REFERENCES kernel.organizations (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.study_sites ADD CONSTRAINT "FK_study_sites_sites_tenant_id_site_id" FOREIGN KEY (tenant_id, site_id) REFERENCES kernel.sites (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE kernel.study_sites ADD CONSTRAINT "FK_study_sites_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    CREATE OR REPLACE FUNCTION "kernel".current_tenant_id() RETURNS uuid
    LANGUAGE sql STABLE PARALLEL SAFE AS $fn$
        SELECT NULLIF(pg_catalog.current_setting('cadence.tenant_id', true), '')::uuid
    $fn$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."tenants" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."tenants" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."tenants"
        USING (id = "kernel".current_tenant_id())
        WITH CHECK (id = "kernel".current_tenant_id());
    CREATE POLICY tenant_directory ON "kernel"."tenants" FOR SELECT
        USING (pg_catalog.current_setting('cadence.tenant_directory', true) = 'on'
           AND pg_catalog.pg_has_role(current_user, (SELECT c.relowner FROM pg_catalog.pg_class c WHERE c.oid = 'kernel.tenants'::regclass), 'MEMBER'));
    CREATE OR REPLACE FUNCTION "kernel".tenant_ids() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
    SET cadence.tenant_directory = 'on'
    AS $fn$
        SELECT t.id FROM "kernel"."tenants" t ORDER BY t.id
    $fn$;
    REVOKE ALL ON FUNCTION "kernel".tenant_ids() FROM PUBLIC;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."people" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."people" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "person_visibility" ON "kernel"."people"
        USING (EXISTS (SELECT 1 FROM "kernel"."tenant_memberships" m WHERE m.person_id = "people".id))
        WITH CHECK ("kernel".current_tenant_id() IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."organizations" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."organizations" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."organizations"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."sites" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."sites" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."sites"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."studies" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."studies" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."studies"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."study_sites" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."study_sites" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."study_sites"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."tenant_memberships" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."tenant_memberships" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."tenant_memberships"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."provenance_activities" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."provenance_activities" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."provenance_activities"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."domain_events" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."domain_events" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."domain_events"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."audit_entries" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."audit_entries" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."audit_entries"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."audit_chain_heads" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."audit_chain_heads" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."audit_chain_heads"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."audit_chain_seals" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."audit_chain_seals" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."audit_chain_seals"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."outbox_messages" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."outbox_messages" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."outbox_messages"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    ALTER TABLE "kernel"."idempotency_records" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."idempotency_records" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."idempotency_records"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930001034_TenantIsolation') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930001034_TenantIsolation', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE TABLE kernel.code_systems (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        uri character varying(512) NOT NULL,
        name character varying(255) NOT NULL,
        publisher character varying(255) NOT NULL,
        license character varying(32) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_code_systems" PRIMARY KEY (id),
        CONSTRAINT "AK_code_systems_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_code_systems_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE TABLE kernel.concepts (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        origin character varying(32) NOT NULL,
        external_code character varying(64),
        kind character varying(32) NOT NULL,
        status character varying(32) NOT NULL,
        version integer NOT NULL,
        preferred_label character varying(500) NOT NULL,
        definition text NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_concepts" PRIMARY KEY (id),
        CONSTRAINT "AK_concepts_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_concepts_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE TABLE kernel.code_system_versions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code_system_id uuid NOT NULL,
        version character varying(64) NOT NULL,
        released_on date NOT NULL,
        package_sha256 character(64) NOT NULL,
        package_file_name character varying(255) NOT NULL,
        package_source_uri character varying(1024) NOT NULL,
        package_byte_length bigint NOT NULL,
        package_format character varying(64) NOT NULL,
        entry_count integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_code_system_versions" PRIMARY KEY (id),
        CONSTRAINT "AK_code_system_versions_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_code_system_versions_code_systems_tenant_id_code_system_id" FOREIGN KEY (tenant_id, code_system_id) REFERENCES kernel.code_systems (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_code_system_versions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE TABLE kernel.concept_revisions (
        tenant_id uuid NOT NULL,
        concept_id uuid NOT NULL,
        version integer NOT NULL,
        preferred_label character varying(500) NOT NULL,
        definition text NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_concept_revisions" PRIMARY KEY (tenant_id, concept_id, version),
        CONSTRAINT "FK_concept_revisions_concepts_tenant_id_concept_id" FOREIGN KEY (tenant_id, concept_id) REFERENCES kernel.concepts (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE TABLE kernel.code_system_entries (
        tenant_id uuid NOT NULL,
        code_system_version_id uuid NOT NULL,
        ordinal integer NOT NULL,
        code character varying(64) NOT NULL,
        display character varying(1000) NOT NULL,
        group_code character varying(64),
        submission_value character varying(255),
        definition text,
        synonyms text[] NOT NULL,
        CONSTRAINT "PK_code_system_entries" PRIMARY KEY (tenant_id, code_system_version_id, ordinal),
        CONSTRAINT "FK_code_system_entries_code_system_versions_tenant_id_code_sys~" FOREIGN KEY (tenant_id, code_system_version_id) REFERENCES kernel.code_system_versions (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE TABLE kernel.concept_mappings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        concept_id uuid NOT NULL,
        concept_version integer NOT NULL,
        code_system_version_id uuid NOT NULL,
        code character varying(64) NOT NULL,
        relation character varying(32) NOT NULL,
        status character varying(32) NOT NULL,
        evidence character varying(2000) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_concept_mappings" PRIMARY KEY (id),
        CONSTRAINT "AK_concept_mappings_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_concept_mappings_code_system_versions_tenant_id_code_system~" FOREIGN KEY (tenant_id, code_system_version_id) REFERENCES kernel.code_system_versions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_concept_mappings_concept_revisions_tenant_id_concept_id_con~" FOREIGN KEY (tenant_id, concept_id, concept_version) REFERENCES kernel.concept_revisions (tenant_id, concept_id, version) ON DELETE RESTRICT,
        CONSTRAINT "FK_concept_mappings_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE INDEX "IX_code_system_entries_tenant_id_code_system_version_id_code" ON kernel.code_system_entries (tenant_id, code_system_version_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE UNIQUE INDEX "IX_code_system_entries_tenant_id_code_system_version_id_group_~" ON kernel.code_system_entries (tenant_id, code_system_version_id, group_code, code) NULLS NOT DISTINCT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE UNIQUE INDEX "IX_code_system_versions_tenant_id_code_system_id_version" ON kernel.code_system_versions (tenant_id, code_system_id, version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE UNIQUE INDEX "IX_code_systems_tenant_id_uri" ON kernel.code_systems (tenant_id, uri);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE INDEX "IX_concept_mappings_tenant_id_code_system_version_id_code" ON kernel.concept_mappings (tenant_id, code_system_version_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE INDEX "IX_concept_mappings_tenant_id_concept_id" ON kernel.concept_mappings (tenant_id, concept_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE INDEX "IX_concept_mappings_tenant_id_concept_id_concept_version" ON kernel.concept_mappings (tenant_id, concept_id, concept_version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE UNIQUE INDEX "IX_concepts_tenant_id_external_code" ON kernel.concepts (tenant_id, external_code) WHERE external_code IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    ALTER TABLE "kernel"."code_systems" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."code_systems" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."code_systems"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    ALTER TABLE "kernel"."code_system_versions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."code_system_versions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."code_system_versions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    ALTER TABLE "kernel"."code_system_entries" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."code_system_entries" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."code_system_entries"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    ALTER TABLE "kernel"."concepts" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."concepts" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."concepts"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    ALTER TABLE "kernel"."concept_revisions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."concept_revisions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."concept_revisions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    ALTER TABLE "kernel"."concept_mappings" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."concept_mappings" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."concept_mappings"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE TRIGGER "trg_code_system_versions_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."code_system_versions"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_code_system_versions_no_truncate" BEFORE TRUNCATE ON "kernel"."code_system_versions"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE TRIGGER "trg_code_system_entries_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."code_system_entries"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_code_system_entries_no_truncate" BEFORE TRUNCATE ON "kernel"."code_system_entries"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    CREATE TRIGGER "trg_concept_revisions_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."concept_revisions"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_concept_revisions_no_truncate" BEFORE TRUNCATE ON "kernel"."concept_revisions"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930004914_ConceptRegistry') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930004914_ConceptRegistry', '10.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TABLE kernel.library_items (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        kind character varying(64) NOT NULL,
        key character varying(128) NOT NULL,
        item_version integer NOT NULL,
        title character varying(500) NOT NULL,
        content text NOT NULL,
        content_hash character varying(71) NOT NULL,
        standard_version_ids uuid[] NOT NULL,
        status character varying(32) NOT NULL,
        approval_attestation_id uuid,
        deprecation_attestation_id uuid,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_library_items" PRIMARY KEY (id),
        CONSTRAINT "AK_library_items_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_library_items_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TABLE kernel.standard_versions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        standard_code character varying(64) NOT NULL,
        version character varying(64) NOT NULL,
        name character varying(500) NOT NULL,
        publisher character varying(255) NOT NULL,
        source character varying(1000),
        effective_date date,
        content_hash character varying(71) NOT NULL,
        status character varying(32) NOT NULL,
        approval_attestation_id uuid,
        deprecation_attestation_id uuid,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_standard_versions" PRIMARY KEY (id),
        CONSTRAINT "AK_standard_versions_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_standard_versions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TABLE kernel.study_standards_profiles (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        standard_version_ids uuid[] NOT NULL,
        controlled_terminology text[] NOT NULL,
        library_pins uuid[] NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_study_standards_profiles" PRIMARY KEY (id),
        CONSTRAINT "AK_study_standards_profiles_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_study_standards_profiles_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_study_standards_profiles_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TABLE kernel.standard_datasets (
        tenant_id uuid NOT NULL,
        standard_version_id uuid NOT NULL,
        name character varying(64) NOT NULL,
        label character varying(500) NOT NULL,
        class character varying(255),
        structure character varying(1000),
        ordinal integer NOT NULL,
        CONSTRAINT "PK_standard_datasets" PRIMARY KEY (tenant_id, standard_version_id, name),
        CONSTRAINT "FK_standard_datasets_standard_versions_tenant_id_standard_vers~" FOREIGN KEY (tenant_id, standard_version_id) REFERENCES kernel.standard_versions (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TABLE kernel.standard_terminology (
        tenant_id uuid NOT NULL,
        standard_version_id uuid NOT NULL,
        code_system character varying(64) NOT NULL,
        version character varying(64) NOT NULL,
        CONSTRAINT "PK_standard_terminology" PRIMARY KEY (tenant_id, standard_version_id, code_system, version),
        CONSTRAINT "FK_standard_terminology_standard_versions_tenant_id_standard_v~" FOREIGN KEY (tenant_id, standard_version_id) REFERENCES kernel.standard_versions (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TABLE kernel.standard_variables (
        tenant_id uuid NOT NULL,
        standard_version_id uuid NOT NULL,
        dataset_name character varying(64) NOT NULL,
        name character varying(64) NOT NULL,
        label character varying(500) NOT NULL,
        ordinal integer NOT NULL,
        data_type character varying(16) NOT NULL,
        role character varying(255),
        core character varying(16) NOT NULL,
        codelists text[] NOT NULL,
        value_list text[] NOT NULL,
        described_value_domain character varying(255),
        description text,
        CONSTRAINT "PK_standard_variables" PRIMARY KEY (tenant_id, standard_version_id, dataset_name, name),
        CONSTRAINT "FK_standard_variables_standard_datasets_tenant_id_standard_ver~" FOREIGN KEY (tenant_id, standard_version_id, dataset_name) REFERENCES kernel.standard_datasets (tenant_id, standard_version_id, name) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TABLE kernel.standard_value_level_definitions (
        tenant_id uuid NOT NULL,
        standard_version_id uuid NOT NULL,
        ordinal integer NOT NULL,
        dataset character varying(64) NOT NULL,
        variable character varying(64) NOT NULL,
        "where" jsonb NOT NULL,
        data_type character varying(16) NOT NULL,
        length integer,
        codelists text[] NOT NULL,
        value_list text[] NOT NULL,
        description text,
        CONSTRAINT "PK_standard_value_level_definitions" PRIMARY KEY (tenant_id, standard_version_id, ordinal),
        CONSTRAINT "FK_standard_value_level_definitions_standard_variables_tenant_~" FOREIGN KEY (tenant_id, standard_version_id, dataset, variable) REFERENCES kernel.standard_variables (tenant_id, standard_version_id, dataset_name, name) ON DELETE RESTRICT,
        CONSTRAINT "FK_standard_value_level_definitions_standard_versions_tenant_i~" FOREIGN KEY (tenant_id, standard_version_id) REFERENCES kernel.standard_versions (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE UNIQUE INDEX "IX_library_items_tenant_id_kind_key_item_version" ON kernel.library_items (tenant_id, kind, key, item_version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE INDEX "IX_standard_value_level_definitions_tenant_id_standard_version~" ON kernel.standard_value_level_definitions (tenant_id, standard_version_id, dataset, variable);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE UNIQUE INDEX "IX_standard_versions_tenant_id_standard_code_version" ON kernel.standard_versions (tenant_id, standard_code, version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE UNIQUE INDEX "IX_study_standards_profiles_tenant_id_study_id" ON kernel.study_standards_profiles (tenant_id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    ALTER TABLE "kernel"."standard_versions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."standard_versions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."standard_versions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    ALTER TABLE "kernel"."standard_datasets" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."standard_datasets" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."standard_datasets"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    ALTER TABLE "kernel"."standard_variables" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."standard_variables" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."standard_variables"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    ALTER TABLE "kernel"."standard_value_level_definitions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."standard_value_level_definitions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."standard_value_level_definitions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    ALTER TABLE "kernel"."standard_terminology" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."standard_terminology" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."standard_terminology"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    ALTER TABLE "kernel"."library_items" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."library_items" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."library_items"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    ALTER TABLE "kernel"."study_standards_profiles" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."study_standards_profiles" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."study_standards_profiles"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TRIGGER "trg_standard_datasets_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."standard_datasets"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_standard_datasets_no_truncate" BEFORE TRUNCATE ON "kernel"."standard_datasets"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TRIGGER "trg_standard_variables_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."standard_variables"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_standard_variables_no_truncate" BEFORE TRUNCATE ON "kernel"."standard_variables"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TRIGGER "trg_standard_value_level_definitions_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."standard_value_level_definitions"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_standard_value_level_definitions_no_truncate" BEFORE TRUNCATE ON "kernel"."standard_value_level_definitions"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TRIGGER "trg_standard_terminology_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."standard_terminology"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_standard_terminology_no_truncate" BEFORE TRUNCATE ON "kernel"."standard_terminology"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE OR REPLACE FUNCTION "kernel".enforce_immutable_columns() RETURNS trigger
    LANGUAGE plpgsql AS $fn$
    DECLARE
        col text;
    BEGIN
        FOREACH col IN ARRAY TG_ARGV LOOP
            IF (to_jsonb(OLD) -> col) IS DISTINCT FROM (to_jsonb(NEW) -> col) THEN
                RAISE EXCEPTION 'K-STD: %.%.% is fixed once registered', TG_TABLE_SCHEMA, TG_TABLE_NAME, col
                    USING ERRCODE = 'KS001';
            END IF;
        END LOOP;
        RETURN NEW;
    END;
    $fn$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TRIGGER "trg_standard_versions_immutable" BEFORE UPDATE ON "kernel"."standard_versions"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_immutable_columns('id', 'tenant_id', 'standard_code', 'version', 'name', 'publisher', 'source', 'effective_date', 'content_hash', 'created_at', 'created_by');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    CREATE TRIGGER "trg_library_items_immutable" BEFORE UPDATE ON "kernel"."library_items"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_immutable_columns('id', 'tenant_id', 'kind', 'key', 'item_version', 'title', 'content', 'content_hash', 'standard_version_ids', 'created_at', 'created_by');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930014016_StandardsRegistry') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930014016_StandardsRegistry', '10.0.11');
    END IF;
END $EF$;
COMMIT;

