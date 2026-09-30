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

