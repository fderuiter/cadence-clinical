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
    VALUES ('20260929191437_InitialKernelIdentity', '10.0.15');
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
    VALUES ('20260929195617_BitemporalFoundation', '10.0.15');
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
    VALUES ('20260929223332_CommandPipeline', '10.0.15');
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
    VALUES ('20260929234442_OutboxRelayAndChainSeals', '10.0.15');
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
    VALUES ('20260930001034_TenantIsolation', '10.0.15');
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
    VALUES ('20260930004914_ConceptRegistry', '10.0.15');
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
    VALUES ('20260930014016_StandardsRegistry', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    CREATE TABLE kernel.attestations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        actor_id character varying(255) NOT NULL,
        meaning character varying(32) NOT NULL,
        target_kind character varying(64) NOT NULL,
        target_id uuid NOT NULL,
        target_content_hash character varying(71) NOT NULL,
        authenticated_at timestamp with time zone NOT NULL,
        method character varying(64) NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        reason character varying(1000) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_attestations" PRIMARY KEY (id),
        CONSTRAINT "AK_attestations_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_attestations_target_content_hash CHECK (target_content_hash ~ '^sha256:[0-9a-f]{64}$'),
        CONSTRAINT "FK_attestations_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    CREATE TABLE kernel.protocol_versions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        number integer NOT NULL,
        parent_version_id uuid,
        status character varying(16) NOT NULL,
        content text NOT NULL,
        content_hash character varying(71),
        amendment jsonb,
        approval_attestation_id uuid,
        withdrawal_attestation_id uuid,
        withdrawal_reason character varying(1000),
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_protocol_versions" PRIMARY KEY (id),
        CONSTRAINT "AK_protocol_versions_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_protocol_versions_content_hash CHECK (content_hash IS NULL OR content_hash ~ '^sha256:[0-9a-f]{64}$'),
        CONSTRAINT ck_protocol_versions_number CHECK (number > 0),
        CONSTRAINT "FK_protocol_versions_attestations_tenant_id_approval_attestati~" FOREIGN KEY (tenant_id, approval_attestation_id) REFERENCES kernel.attestations (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_protocol_versions_attestations_tenant_id_withdrawal_attesta~" FOREIGN KEY (tenant_id, withdrawal_attestation_id) REFERENCES kernel.attestations (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_protocol_versions_protocol_versions_tenant_id_parent_versio~" FOREIGN KEY (tenant_id, parent_version_id) REFERENCES kernel.protocol_versions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_protocol_versions_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_protocol_versions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    CREATE INDEX "IX_attestations_tenant_id_target_kind_target_id" ON kernel.attestations (tenant_id, target_kind, target_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    CREATE INDEX "IX_protocol_versions_tenant_id_approval_attestation_id" ON kernel.protocol_versions (tenant_id, approval_attestation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    CREATE INDEX "IX_protocol_versions_tenant_id_parent_version_id" ON kernel.protocol_versions (tenant_id, parent_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    CREATE UNIQUE INDEX "IX_protocol_versions_tenant_id_study_id_number" ON kernel.protocol_versions (tenant_id, study_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    CREATE INDEX "IX_protocol_versions_tenant_id_withdrawal_attestation_id" ON kernel.protocol_versions (tenant_id, withdrawal_attestation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    ALTER TABLE "kernel"."attestations" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."attestations" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."attestations"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    ALTER TABLE "kernel"."protocol_versions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."protocol_versions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."protocol_versions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    CREATE TRIGGER "trg_attestations_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."attestations"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_attestations_no_truncate" BEFORE TRUNCATE ON "kernel"."attestations"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    CREATE OR REPLACE FUNCTION "kernel".enforce_protocol_version_lifecycle() RETURNS trigger
    LANGUAGE plpgsql AS $fn$
    BEGIN
        IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
            RAISE EXCEPTION 'K-PROT: protocol versions are never deleted (%)', TG_OP USING ERRCODE = 'KA002';
        END IF;

        IF TG_OP = 'INSERT' THEN
            IF NEW.status <> 'Draft' OR NEW.content_hash IS NOT NULL
                OR NEW.approval_attestation_id IS NOT NULL OR NEW.withdrawal_attestation_id IS NOT NULL
                OR NEW.withdrawal_reason IS NOT NULL THEN
                RAISE EXCEPTION 'K-PROT: a protocol version starts as a draft without a hash or attestations'
                    USING ERRCODE = 'KA002';
            END IF;
            RETURN NEW;
        END IF;

        IF OLD.status IN ('Superseded', 'Withdrawn') THEN
            RAISE EXCEPTION 'K-PROT: protocol version % is % and final', OLD.id, OLD.status USING ERRCODE = 'KA002';
        END IF;

        IF NEW.id <> OLD.id OR NEW.tenant_id <> OLD.tenant_id OR NEW.study_id <> OLD.study_id
            OR NEW.number <> OLD.number OR NEW.parent_version_id IS DISTINCT FROM OLD.parent_version_id THEN
            RAISE EXCEPTION 'K-PROT: the identity of protocol version % cannot change', OLD.id USING ERRCODE = 'KA002';
        END IF;

        IF OLD.status <> 'Draft'
            AND (NEW.content IS DISTINCT FROM OLD.content OR NEW.amendment IS DISTINCT FROM OLD.amendment) THEN
            RAISE EXCEPTION 'K-PROT: protocol version % is % and its content is frozen', OLD.id, OLD.status
                USING ERRCODE = 'KA002';
        END IF;

        IF NEW.status <> OLD.status AND (OLD.status, NEW.status) NOT IN (
            ('Draft', 'InReview'), ('InReview', 'Draft'), ('InReview', 'Approved'), ('InReview', 'Withdrawn'),
            ('Approved', 'Superseded'), ('Approved', 'Withdrawn')) THEN
            RAISE EXCEPTION 'K-PROT: protocol version % cannot move from % to %', OLD.id, OLD.status, NEW.status
                USING ERRCODE = 'KA002';
        END IF;

        IF (NEW.status = 'Draft') <> (NEW.content_hash IS NULL)
            OR (OLD.status <> 'Draft' AND NEW.status <> 'Draft' AND NEW.content_hash IS DISTINCT FROM OLD.content_hash) THEN
            RAISE EXCEPTION 'K-PROT: the content hash of protocol version % is set on review and never changes after', OLD.id
                USING ERRCODE = 'KA002';
        END IF;

        IF (OLD.approval_attestation_id IS NOT NULL AND NEW.approval_attestation_id IS DISTINCT FROM OLD.approval_attestation_id)
            OR (NEW.status IN ('Approved', 'Superseded') AND NEW.approval_attestation_id IS NULL)
            OR (NEW.status = 'Withdrawn' AND (NEW.withdrawal_attestation_id IS NULL OR NEW.withdrawal_reason IS NULL))
            OR (NEW.status <> 'Withdrawn' AND (NEW.withdrawal_attestation_id IS NOT NULL OR NEW.withdrawal_reason IS NOT NULL)) THEN
            RAISE EXCEPTION 'K-PROT: approval and withdrawal of protocol version % need their attestation', OLD.id
                USING ERRCODE = 'KA002';
        END IF;

        RETURN NEW;
    END;
    $fn$;

    CREATE TRIGGER trg_protocol_versions_lifecycle BEFORE INSERT OR UPDATE OR DELETE ON "kernel"."protocol_versions"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_protocol_version_lifecycle();
    CREATE TRIGGER trg_protocol_versions_no_truncate BEFORE TRUNCATE ON "kernel"."protocol_versions"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_protocol_version_lifecycle();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930032422_ProtocolVersionsAndAttestations') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930032422_ProtocolVersionsAndAttestations', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930040415_KernelSearchIndexes') THEN
    CREATE INDEX IF NOT EXISTS "ix_organizations_search" ON "kernel"."organizations" USING gin ((to_tsvector('simple'::regconfig, coalesce((code)::text, '') || ' ' || coalesce((name)::text, ''))));
    CREATE INDEX IF NOT EXISTS "ix_sites_search" ON "kernel"."sites" USING gin ((to_tsvector('simple'::regconfig, coalesce((site_code)::text, '') || ' ' || coalesce((name)::text, ''))));
    CREATE INDEX IF NOT EXISTS "ix_studies_search" ON "kernel"."studies" USING gin ((to_tsvector('simple'::regconfig, coalesce((study_code)::text, '') || ' ' || coalesce((title)::text, ''))));
    CREATE INDEX IF NOT EXISTS "ix_concepts_search" ON "kernel"."concepts" USING gin ((to_tsvector('simple'::regconfig, coalesce((external_code)::text, '') || ' ' || coalesce((preferred_label)::text, ''))));
    CREATE INDEX IF NOT EXISTS "ix_code_systems_search" ON "kernel"."code_systems" USING gin ((to_tsvector('simple'::regconfig, coalesce((NULL)::text, '') || ' ' || coalesce((name)::text, ''))));
    CREATE INDEX IF NOT EXISTS "ix_code_system_entries_search" ON "kernel"."code_system_entries" USING gin ((to_tsvector('simple'::regconfig, coalesce((code)::text, '') || ' ' || coalesce((display)::text, ''))));
    CREATE INDEX IF NOT EXISTS "ix_standard_versions_search" ON "kernel"."standard_versions" USING gin ((to_tsvector('simple'::regconfig, coalesce((standard_code)::text, '') || ' ' || coalesce((name)::text, ''))));
    CREATE INDEX IF NOT EXISTS "ix_library_items_search" ON "kernel"."library_items" USING gin ((to_tsvector('simple'::regconfig, coalesce((key)::text, '') || ' ' || coalesce((title)::text, ''))));

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930040415_KernelSearchIndexes') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930040415_KernelSearchIndexes', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    ALTER TABLE kernel.study_sites ADD CONSTRAINT "AK_study_sites_tenant_id_id_study_id" UNIQUE (tenant_id, id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE TABLE kernel.participants (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_participants" PRIMARY KEY (id),
        CONSTRAINT "AK_participants_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_participants_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE TABLE kernel.enrollments (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        participant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        study_site_id uuid NOT NULL,
        subject_code character varying(64) NOT NULL,
        status character varying(16) NOT NULL,
        status_since timestamp with time zone NOT NULL,
        enrollment_number integer,
        withdrawal_kind character varying(32),
        withdrawal_attestation_id uuid,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_enrollments" PRIMARY KEY (id),
        CONSTRAINT "AK_enrollments_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "AK_enrollments_tenant_id_id_participant_id_study_site_id" UNIQUE (tenant_id, id, participant_id, study_site_id),
        CONSTRAINT ck_enrollments_number CHECK (enrollment_number IS NULL OR enrollment_number > 0),
        CONSTRAINT ck_enrollments_numbered CHECK ((status IN ('Screening', 'ScreenFailed') AND enrollment_number IS NULL) OR (status IN ('Enrolled', 'Randomized', 'Active', 'Completed') AND enrollment_number IS NOT NULL) OR status = 'Withdrawn'),
        CONSTRAINT "FK_enrollments_attestations_tenant_id_withdrawal_attestation_id" FOREIGN KEY (tenant_id, withdrawal_attestation_id) REFERENCES kernel.attestations (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_enrollments_participants_tenant_id_participant_id" FOREIGN KEY (tenant_id, participant_id) REFERENCES kernel.participants (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_enrollments_study_sites_tenant_id_study_site_id_study_id" FOREIGN KEY (tenant_id, study_site_id, study_id) REFERENCES kernel.study_sites (tenant_id, id, study_id) ON DELETE RESTRICT,
        CONSTRAINT "FK_enrollments_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE TABLE kernel.enrollment_status_periods (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        enrollment_id uuid NOT NULL,
        participant_id uuid NOT NULL,
        study_site_id uuid NOT NULL,
        status character varying(16) NOT NULL,
        recorded_period tstzrange NOT NULL,
        valid_period tstzrange NOT NULL,
        CONSTRAINT "PK_enrollment_status_periods" PRIMARY KEY (id),
        CONSTRAINT "FK_enrollment_status_periods_enrollments_tenant_id_enrollment_~" FOREIGN KEY (tenant_id, enrollment_id, participant_id, study_site_id) REFERENCES kernel.enrollments (tenant_id, id, participant_id, study_site_id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE INDEX "IX_enrollment_status_periods_tenant_id_enrollment_id" ON kernel.enrollment_status_periods (tenant_id, enrollment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE INDEX "IX_enrollment_status_periods_tenant_id_enrollment_id_participa~" ON kernel.enrollment_status_periods (tenant_id, enrollment_id, participant_id, study_site_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE INDEX "IX_enrollments_tenant_id_participant_id" ON kernel.enrollments (tenant_id, participant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE UNIQUE INDEX "IX_enrollments_tenant_id_study_id_enrollment_number" ON kernel.enrollments (tenant_id, study_id, enrollment_number) WHERE enrollment_number IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE UNIQUE INDEX "IX_enrollments_tenant_id_study_id_subject_code" ON kernel.enrollments (tenant_id, study_id, subject_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE INDEX "IX_enrollments_tenant_id_study_site_id_study_id" ON kernel.enrollments (tenant_id, study_site_id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE INDEX "IX_enrollments_tenant_id_withdrawal_attestation_id" ON kernel.enrollments (tenant_id, withdrawal_attestation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    ALTER TABLE "kernel"."enrollment_status_periods" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."enrollment_status_periods" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."enrollment_status_periods"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    ALTER TABLE "kernel"."enrollments" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."enrollments" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."enrollments"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    ALTER TABLE "kernel"."participants" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."participants" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."participants"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    ALTER TABLE "kernel"."enrollment_status_periods" ADD CONSTRAINT "ex_enrollment_status_periods_single_truth"
        EXCLUDE USING gist ("tenant_id" WITH =, "enrollment_id" WITH =, "valid_period" WITH &&, "recorded_period" WITH &&)
        DEFERRABLE INITIALLY DEFERRED;
    CREATE TRIGGER "trg_enrollment_status_periods_append_only" BEFORE UPDATE OR DELETE ON "kernel"."enrollment_status_periods"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    CREATE TRIGGER "trg_enrollment_status_periods_no_truncate" BEFORE TRUNCATE ON "kernel"."enrollment_status_periods"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    CREATE OR REPLACE FUNCTION "kernel".enforce_enrollment_lifecycle() RETURNS trigger
    LANGUAGE plpgsql AS $fn$
    BEGIN
        IF TG_OP IN ('DELETE', 'TRUNCATE') THEN
            RAISE EXCEPTION 'K-ENR: enrollments are never deleted (%)', TG_OP USING ERRCODE = 'KE001';
        END IF;

        IF TG_OP = 'INSERT' THEN
            IF NEW.status <> 'Screening' OR NEW.enrollment_number IS NOT NULL
                OR NEW.withdrawal_kind IS NOT NULL OR NEW.withdrawal_attestation_id IS NOT NULL THEN
                RAISE EXCEPTION 'K-ENR: an enrollment starts in Screening without a number or withdrawal'
                    USING ERRCODE = 'KE001';
            END IF;
            RETURN NEW;
        END IF;

        IF OLD.status IN ('ScreenFailed', 'Completed', 'Withdrawn') THEN
            RAISE EXCEPTION 'K-ENR: enrollment % is % and final', OLD.id, OLD.status USING ERRCODE = 'KE001';
        END IF;

        IF NEW.id <> OLD.id OR NEW.tenant_id <> OLD.tenant_id OR NEW.participant_id <> OLD.participant_id
            OR NEW.study_id <> OLD.study_id OR NEW.study_site_id <> OLD.study_site_id
            OR NEW.subject_code <> OLD.subject_code THEN
            RAISE EXCEPTION 'K-ENR: the identity of enrollment % cannot change', OLD.id USING ERRCODE = 'KE001';
        END IF;

        IF NEW.status <> OLD.status AND (OLD.status, NEW.status) NOT IN (
            ('Screening', 'ScreenFailed'), ('Screening', 'Enrolled'), ('Screening', 'Withdrawn'),
            ('Enrolled', 'Randomized'), ('Enrolled', 'Active'), ('Enrolled', 'Withdrawn'),
            ('Randomized', 'Active'), ('Randomized', 'Withdrawn'),
            ('Active', 'Completed'), ('Active', 'Withdrawn')) THEN
            RAISE EXCEPTION 'K-ENR: enrollment % cannot move from % to %', OLD.id, OLD.status, NEW.status
                USING ERRCODE = 'KE001';
        END IF;

        IF (NEW.status = OLD.status AND NEW.status_since <> OLD.status_since)
            OR (NEW.status <> OLD.status AND NEW.status_since <= OLD.status_since) THEN
            RAISE EXCEPTION 'K-ENR: enrollment % changes status only forwards in valid time', OLD.id
                USING ERRCODE = 'KE001';
        END IF;

        IF (OLD.enrollment_number IS NOT NULL AND NEW.enrollment_number IS DISTINCT FROM OLD.enrollment_number)
            OR (OLD.enrollment_number IS NULL AND NEW.enrollment_number IS NOT NULL
                AND NOT (OLD.status = 'Screening' AND NEW.status = 'Enrolled'))
            OR (NEW.status = 'Enrolled' AND NEW.enrollment_number IS NULL) THEN
            RAISE EXCEPTION 'K-ENR: enrollment % is numbered once, on entering Enrolled', OLD.id
                USING ERRCODE = 'KE001';
        END IF;

        IF (NEW.status = 'Withdrawn') <> (NEW.withdrawal_kind IS NOT NULL AND NEW.withdrawal_attestation_id IS NOT NULL)
            OR (NEW.status <> 'Withdrawn' AND (NEW.withdrawal_kind IS NOT NULL OR NEW.withdrawal_attestation_id IS NOT NULL)) THEN
            RAISE EXCEPTION 'K-ENR: withdrawal of enrollment % needs its kind and attestation', OLD.id
                USING ERRCODE = 'KE001';
        END IF;

        RETURN NEW;
    END;
    $fn$;

    CREATE TRIGGER trg_enrollments_lifecycle BEFORE INSERT OR UPDATE OR DELETE ON "kernel"."enrollments"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_enrollment_lifecycle();
    CREATE TRIGGER trg_enrollments_no_truncate BEFORE TRUNCATE ON "kernel"."enrollments"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_enrollment_lifecycle();

    ALTER TABLE "kernel"."enrollment_status_periods" ADD CONSTRAINT ex_enrollment_status_periods_one_active
        EXCLUDE USING gist (tenant_id WITH =, participant_id WITH =, study_site_id WITH =,
            valid_period WITH &&, recorded_period WITH &&)
        WHERE (status IN ('Screening', 'Enrolled', 'Randomized', 'Active'))
        DEFERRABLE INITIALLY DEFERRED;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930043230_ParticipantsAndEnrollments') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930043230_ParticipantsAndEnrollments', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    CREATE TABLE kernel.lineage_edges (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        from_kind character varying(48) NOT NULL,
        from_id uuid NOT NULL,
        from_version bigint NOT NULL,
        to_kind character varying(48) NOT NULL,
        to_id uuid NOT NULL,
        to_version bigint NOT NULL,
        kind character varying(32) NOT NULL,
        activity_id uuid NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        valid_from timestamp with time zone,
        CONSTRAINT "PK_lineage_edges" PRIMARY KEY (id),
        CONSTRAINT "FK_lineage_edges_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    CREATE TABLE kernel.lineage_nodes (
        tenant_id uuid NOT NULL,
        kind character varying(48) NOT NULL,
        node_id uuid NOT NULL,
        version bigint NOT NULL,
        derived boolean NOT NULL,
        activity_id uuid NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_lineage_nodes" PRIMARY KEY (tenant_id, node_id, kind, version),
        CONSTRAINT "FK_lineage_nodes_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    CREATE UNIQUE INDEX "IX_lineage_edges_tenant_id_from_id_from_kind_from_version_to_i~" ON kernel.lineage_edges (tenant_id, from_id, from_kind, from_version, to_id, to_kind, to_version, kind);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    CREATE INDEX "IX_lineage_edges_tenant_id_to_id_to_kind_to_version" ON kernel.lineage_edges (tenant_id, to_id, to_kind, to_version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    CREATE INDEX "IX_lineage_nodes_tenant_id_derived" ON kernel.lineage_nodes (tenant_id, derived);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    ALTER TABLE "kernel"."lineage_nodes" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."lineage_nodes" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."lineage_nodes"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    ALTER TABLE "kernel"."lineage_edges" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."lineage_edges" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."lineage_edges"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    CREATE TRIGGER "trg_lineage_nodes_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."lineage_nodes"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_lineage_nodes_no_truncate" BEFORE TRUNCATE ON "kernel"."lineage_nodes"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    CREATE TRIGGER "trg_lineage_edges_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."lineage_edges"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_lineage_edges_no_truncate" BEFORE TRUNCATE ON "kernel"."lineage_edges"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930045723_LineageStore') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930045723_LineageStore', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930053132_KernelActivityStream') THEN
    CREATE TABLE kernel.activity_entries (
        tenant_id uuid NOT NULL,
        event_id uuid NOT NULL,
        recorded timestamp with time zone NOT NULL,
        occurred_valid timestamp with time zone,
        actor_id character varying(255) NOT NULL,
        type character varying(128) NOT NULL,
        family character varying(32) NOT NULL,
        subject_kind character varying(48),
        subject character varying(128),
        scope text[] NOT NULL,
        correlation_id uuid NOT NULL,
        CONSTRAINT "PK_activity_entries" PRIMARY KEY (tenant_id, event_id),
        CONSTRAINT "FK_activity_entries_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930053132_KernelActivityStream') THEN
    CREATE INDEX "IX_activity_entries_scope" ON kernel.activity_entries USING gin (scope);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930053132_KernelActivityStream') THEN
    CREATE INDEX "IX_activity_entries_tenant_id_recorded_event_id" ON kernel.activity_entries (tenant_id, recorded DESC, event_id DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930053132_KernelActivityStream') THEN
    ALTER TABLE "kernel"."activity_entries" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."activity_entries" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."activity_entries"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930053132_KernelActivityStream') THEN
    CREATE TRIGGER "trg_activity_entries_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."activity_entries"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_activity_entries_no_truncate" BEFORE TRUNCATE ON "kernel"."activity_entries"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930053132_KernelActivityStream') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930053132_KernelActivityStream', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930055034_ArtifactsAndVersions') THEN
    CREATE TABLE kernel.artifacts (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        kind character varying(32) NOT NULL,
        study_id uuid,
        title character varying(500) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_artifacts" PRIMARY KEY (id),
        CONSTRAINT "AK_artifacts_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_artifacts_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_artifacts_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930055034_ArtifactsAndVersions') THEN
    CREATE TABLE kernel.artifact_versions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        artifact_id uuid NOT NULL,
        number integer NOT NULL,
        content_hash character varying(71) NOT NULL,
        size_bytes bigint NOT NULL,
        media_type character varying(255) NOT NULL,
        storage_ref character varying(1024) NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_artifact_versions" PRIMARY KEY (id),
        CONSTRAINT "AK_artifact_versions_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_artifact_versions_content_hash CHECK (content_hash ~ '^sha256:[0-9a-f]{64}$'),
        CONSTRAINT ck_artifact_versions_number CHECK (number > 0),
        CONSTRAINT ck_artifact_versions_size CHECK (size_bytes > 0),
        CONSTRAINT "FK_artifact_versions_artifacts_tenant_id_artifact_id" FOREIGN KEY (tenant_id, artifact_id) REFERENCES kernel.artifacts (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930055034_ArtifactsAndVersions') THEN
    CREATE UNIQUE INDEX "IX_artifact_versions_tenant_id_artifact_id_content_hash" ON kernel.artifact_versions (tenant_id, artifact_id, content_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930055034_ArtifactsAndVersions') THEN
    CREATE UNIQUE INDEX "IX_artifact_versions_tenant_id_artifact_id_number" ON kernel.artifact_versions (tenant_id, artifact_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930055034_ArtifactsAndVersions') THEN
    CREATE INDEX "IX_artifacts_tenant_id_study_id_kind" ON kernel.artifacts (tenant_id, study_id, kind);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930055034_ArtifactsAndVersions') THEN
    ALTER TABLE "kernel"."artifact_versions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."artifact_versions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."artifact_versions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930055034_ArtifactsAndVersions') THEN
    ALTER TABLE "kernel"."artifacts" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."artifacts" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."artifacts"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930055034_ArtifactsAndVersions') THEN
    CREATE TRIGGER "trg_artifact_versions_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."artifact_versions"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_artifact_versions_no_truncate" BEFORE TRUNCATE ON "kernel"."artifact_versions"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930055034_ArtifactsAndVersions') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930055034_ArtifactsAndVersions', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930063002_TenantCodeDirectory') THEN
    CREATE OR REPLACE FUNCTION "kernel".tenant_id_by_code(tenant_code text) RETURNS uuid
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
    SET cadence.tenant_directory = 'on'
    AS $fn$
        SELECT t.id FROM "kernel"."tenants" t WHERE t.code = tenant_code AND t.is_active
    $fn$;
    REVOKE ALL ON FUNCTION "kernel".tenant_id_by_code(text) FROM PUBLIC;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930063002_TenantCodeDirectory') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930063002_TenantCodeDirectory', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    ALTER TABLE kernel.provenance_activities ADD "authorization" jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    CREATE TABLE kernel.roles (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(32) NOT NULL,
        name character varying(200) NOT NULL,
        capabilities text[] NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_roles" PRIMARY KEY (id),
        CONSTRAINT "AK_roles_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_roles_capabilities CHECK (cardinality(capabilities) > 0),
        CONSTRAINT "FK_roles_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    CREATE TABLE kernel.grants (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        grantee_kind character varying(32) NOT NULL,
        grantee_id character varying(256) NOT NULL,
        capability character varying(96),
        role_id uuid,
        scope_level character varying(16) NOT NULL,
        scope_target_id uuid,
        scope_country character varying(2),
        with_grant_option boolean NOT NULL,
        parent_grant_id uuid,
        issued_by character varying(300) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_grants" PRIMARY KEY (id),
        CONSTRAINT "AK_grants_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_grants_scope CHECK ((scope_level = 'Tenant' AND scope_target_id IS NULL AND scope_country IS NULL) OR (scope_level IN ('Organization', 'Study', 'StudySite') AND scope_target_id IS NOT NULL AND scope_country IS NULL) OR (scope_level = 'StudyCountry' AND scope_target_id IS NOT NULL AND scope_country ~ '^[A-Z]{2}$')),
        CONSTRAINT ck_grants_target CHECK ((capability IS NULL) <> (role_id IS NULL)),
        CONSTRAINT "FK_grants_grants_tenant_id_parent_grant_id" FOREIGN KEY (tenant_id, parent_grant_id) REFERENCES kernel.grants (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_grants_roles_tenant_id_role_id" FOREIGN KEY (tenant_id, role_id) REFERENCES kernel.roles (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_grants_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    CREATE TABLE kernel.grant_periods (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        grant_id uuid NOT NULL,
        recorded_period tstzrange NOT NULL,
        valid_period tstzrange NOT NULL,
        CONSTRAINT "PK_grant_periods" PRIMARY KEY (id),
        CONSTRAINT "FK_grant_periods_grants_tenant_id_grant_id" FOREIGN KEY (tenant_id, grant_id) REFERENCES kernel.grants (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    CREATE INDEX "IX_grant_periods_tenant_id_grant_id" ON kernel.grant_periods (tenant_id, grant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    CREATE INDEX "IX_grants_tenant_id_grantee_kind_grantee_id" ON kernel.grants (tenant_id, grantee_kind, grantee_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    CREATE INDEX "IX_grants_tenant_id_parent_grant_id" ON kernel.grants (tenant_id, parent_grant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    CREATE INDEX "IX_grants_tenant_id_role_id" ON kernel.grants (tenant_id, role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    CREATE UNIQUE INDEX "IX_roles_tenant_id_code" ON kernel.roles (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    ALTER TABLE "kernel"."grant_periods" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."grant_periods" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."grant_periods"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    ALTER TABLE "kernel"."grants" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."grants" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."grants"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    ALTER TABLE "kernel"."roles" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."roles" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."roles"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    ALTER TABLE "kernel"."grant_periods" ADD CONSTRAINT "ex_grant_periods_single_truth"
        EXCLUDE USING gist ("tenant_id" WITH =, "grant_id" WITH =, "valid_period" WITH &&, "recorded_period" WITH &&)
        DEFERRABLE INITIALLY DEFERRED;
    CREATE TRIGGER "trg_grant_periods_append_only" BEFORE UPDATE OR DELETE ON "kernel"."grant_periods"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    CREATE TRIGGER "trg_grant_periods_no_truncate" BEFORE TRUNCATE ON "kernel"."grant_periods"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    CREATE TRIGGER "trg_roles_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."roles"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_roles_no_truncate" BEFORE TRUNCATE ON "kernel"."roles"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930065947_GrantsRolesAndDelegation') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930065947_GrantsRolesAndDelegation', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930080359_WorkQueueDefinitions') THEN
    CREATE TABLE kernel.work_queue_definitions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(64) NOT NULL,
        version integer NOT NULL,
        title character varying(200) NOT NULL,
        capability character varying(100) NOT NULL,
        verb_class character varying(16) NOT NULL,
        source character varying(100) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_work_queue_definitions" PRIMARY KEY (id),
        CONSTRAINT "AK_work_queue_definitions_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_work_queue_definitions_version CHECK (version > 0),
        CONSTRAINT "FK_work_queue_definitions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930080359_WorkQueueDefinitions') THEN
    CREATE UNIQUE INDEX "IX_work_queue_definitions_tenant_id_code_version" ON kernel.work_queue_definitions (tenant_id, code, version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930080359_WorkQueueDefinitions') THEN
    ALTER TABLE "kernel"."work_queue_definitions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."work_queue_definitions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."work_queue_definitions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930080359_WorkQueueDefinitions') THEN
    CREATE TRIGGER "trg_work_queue_definitions_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."work_queue_definitions"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_work_queue_definitions_no_truncate" BEFORE TRUNCATE ON "kernel"."work_queue_definitions"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930080359_WorkQueueDefinitions') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930080359_WorkQueueDefinitions', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TABLE kernel.validation_runs (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        stage character varying(32) NOT NULL,
        as_of date NOT NULL,
        standard character varying(64),
        standard_version character varying(64),
        agency character varying(64),
        started_at timestamp with time zone NOT NULL,
        completed_at timestamp with time zone NOT NULL,
        detection_count integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_validation_runs" PRIMARY KEY (id),
        CONSTRAINT "AK_validation_runs_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_validation_runs_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_validation_runs_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TABLE kernel.validation_findings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        decision character varying(32),
        decided_at timestamp with time zone,
        decided_by character varying(255),
        decision_text character varying(4000),
        decided_on_run uuid,
        attestation_id uuid,
        classification character varying(32),
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_validation_findings" PRIMARY KEY (id),
        CONSTRAINT "AK_validation_findings_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_validation_findings_attestations_tenant_id_attestation_id" FOREIGN KEY (tenant_id, attestation_id) REFERENCES kernel.attestations (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_validation_findings_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_validation_findings_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_validation_findings_validation_runs_tenant_id_decided_on_run" FOREIGN KEY (tenant_id, decided_on_run) REFERENCES kernel.validation_runs (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TABLE kernel.validation_run_datasets (
        tenant_id uuid NOT NULL,
        run_id uuid NOT NULL,
        dataset character varying(64) NOT NULL,
        CONSTRAINT "PK_validation_run_datasets" PRIMARY KEY (tenant_id, run_id, dataset),
        CONSTRAINT "FK_validation_run_datasets_validation_runs_tenant_id_run_id" FOREIGN KEY (tenant_id, run_id) REFERENCES kernel.validation_runs (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TABLE kernel.validation_run_inputs (
        tenant_id uuid NOT NULL,
        run_id uuid NOT NULL,
        input character varying(128) NOT NULL,
        CONSTRAINT "PK_validation_run_inputs" PRIMARY KEY (tenant_id, run_id, input),
        CONSTRAINT "FK_validation_run_inputs_validation_runs_tenant_id_run_id" FOREIGN KEY (tenant_id, run_id) REFERENCES kernel.validation_runs (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TABLE kernel.validation_run_packs (
        tenant_id uuid NOT NULL,
        run_id uuid NOT NULL,
        pack_key character varying(128) NOT NULL,
        library_item_id uuid NOT NULL,
        pack_version integer NOT NULL,
        content_hash character varying(71) NOT NULL,
        CONSTRAINT "PK_validation_run_packs" PRIMARY KEY (tenant_id, run_id, pack_key),
        CONSTRAINT "FK_validation_run_packs_library_items_tenant_id_library_item_id" FOREIGN KEY (tenant_id, library_item_id) REFERENCES kernel.library_items (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_validation_run_packs_validation_runs_tenant_id_run_id" FOREIGN KEY (tenant_id, run_id) REFERENCES kernel.validation_runs (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TABLE kernel.validation_detections (
        tenant_id uuid NOT NULL,
        run_id uuid NOT NULL,
        finding_id uuid NOT NULL,
        study_id uuid NOT NULL,
        stage character varying(32) NOT NULL,
        pack_key character varying(128) NOT NULL,
        pack_item_id uuid NOT NULL,
        rule_id character varying(64) NOT NULL,
        rule_version integer NOT NULL,
        severity character varying(16) NOT NULL,
        target_kind character varying(32) NOT NULL,
        dataset character varying(64) NOT NULL,
        target_key character varying(200) NOT NULL,
        variable character varying(64),
        message character varying(2000) NOT NULL,
        expected character varying(2000),
        observed jsonb NOT NULL,
        CONSTRAINT "PK_validation_detections" PRIMARY KEY (tenant_id, run_id, finding_id),
        CONSTRAINT "FK_validation_detections_validation_run_datasets_tenant_id_run~" FOREIGN KEY (tenant_id, run_id, dataset) REFERENCES kernel.validation_run_datasets (tenant_id, run_id, dataset) ON DELETE RESTRICT,
        CONSTRAINT "FK_validation_detections_validation_run_packs_tenant_id_run_id~" FOREIGN KEY (tenant_id, run_id, pack_key) REFERENCES kernel.validation_run_packs (tenant_id, run_id, pack_key) ON DELETE RESTRICT,
        CONSTRAINT "FK_validation_detections_validation_runs_tenant_id_run_id" FOREIGN KEY (tenant_id, run_id) REFERENCES kernel.validation_runs (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE INDEX "IX_validation_detections_tenant_id_finding_id" ON kernel.validation_detections (tenant_id, finding_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE INDEX "IX_validation_detections_tenant_id_run_id_dataset" ON kernel.validation_detections (tenant_id, run_id, dataset);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE INDEX "IX_validation_detections_tenant_id_run_id_pack_key" ON kernel.validation_detections (tenant_id, run_id, pack_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE INDEX "IX_validation_detections_tenant_id_study_id_stage" ON kernel.validation_detections (tenant_id, study_id, stage);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE INDEX "IX_validation_findings_tenant_id_attestation_id" ON kernel.validation_findings (tenant_id, attestation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE INDEX "IX_validation_findings_tenant_id_decided_on_run" ON kernel.validation_findings (tenant_id, decided_on_run);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE INDEX "IX_validation_findings_tenant_id_study_id" ON kernel.validation_findings (tenant_id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE INDEX "IX_validation_run_packs_tenant_id_library_item_id" ON kernel.validation_run_packs (tenant_id, library_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE INDEX "IX_validation_runs_tenant_id_study_id_stage_created_at" ON kernel.validation_runs (tenant_id, study_id, stage, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    ALTER TABLE "kernel"."validation_runs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."validation_runs" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."validation_runs"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    ALTER TABLE "kernel"."validation_run_packs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."validation_run_packs" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."validation_run_packs"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    ALTER TABLE "kernel"."validation_run_datasets" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."validation_run_datasets" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."validation_run_datasets"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    ALTER TABLE "kernel"."validation_run_inputs" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."validation_run_inputs" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."validation_run_inputs"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    ALTER TABLE "kernel"."validation_detections" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."validation_detections" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."validation_detections"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    ALTER TABLE "kernel"."validation_findings" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."validation_findings" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."validation_findings"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TRIGGER "trg_validation_runs_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."validation_runs"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_validation_runs_no_truncate" BEFORE TRUNCATE ON "kernel"."validation_runs"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TRIGGER "trg_validation_run_packs_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."validation_run_packs"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_validation_run_packs_no_truncate" BEFORE TRUNCATE ON "kernel"."validation_run_packs"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TRIGGER "trg_validation_run_datasets_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."validation_run_datasets"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_validation_run_datasets_no_truncate" BEFORE TRUNCATE ON "kernel"."validation_run_datasets"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TRIGGER "trg_validation_run_inputs_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."validation_run_inputs"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_validation_run_inputs_no_truncate" BEFORE TRUNCATE ON "kernel"."validation_run_inputs"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TRIGGER "trg_validation_detections_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."validation_detections"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_validation_detections_no_truncate" BEFORE TRUNCATE ON "kernel"."validation_detections"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    CREATE TRIGGER "trg_validation_findings_immutable" BEFORE UPDATE ON "kernel"."validation_findings"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_immutable_columns('id', 'tenant_id', 'study_id', 'created_at', 'created_by');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930090624_ValidationRunsAndFindings') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930090624_ValidationRunsAndFindings', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    ALTER TABLE kernel.grants DROP CONSTRAINT ck_grants_scope;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    CREATE TABLE kernel.effectivities (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        protocol_version_id uuid NOT NULL,
        scope_level character varying(16) NOT NULL,
        scope_country character varying(2),
        scope_study_site_id uuid,
        status character varying(32) NOT NULL,
        status_since timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_effectivities" PRIMARY KEY (id),
        CONSTRAINT "AK_effectivities_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_effectivities_scope CHECK ((scope_level = 'Study' AND scope_country IS NULL AND scope_study_site_id IS NULL) OR (scope_level = 'Country' AND scope_country IS NOT NULL AND scope_country ~ '^[A-Z]{2}$' AND scope_study_site_id IS NULL) OR (scope_level = 'StudySite' AND scope_country IS NULL AND scope_study_site_id IS NOT NULL)),
        CONSTRAINT "FK_effectivities_protocol_versions_tenant_id_protocol_version_~" FOREIGN KEY (tenant_id, protocol_version_id) REFERENCES kernel.protocol_versions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_effectivities_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_effectivities_study_sites_tenant_id_scope_study_site_id_stu~" FOREIGN KEY (tenant_id, scope_study_site_id, study_id) REFERENCES kernel.study_sites (tenant_id, id, study_id) ON DELETE RESTRICT,
        CONSTRAINT "FK_effectivities_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    CREATE TABLE kernel.effectivity_status_periods (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        effectivity_id uuid NOT NULL,
        status character varying(32) NOT NULL,
        evidence uuid[] NOT NULL,
        recorded_period tstzrange NOT NULL,
        valid_period tstzrange NOT NULL,
        CONSTRAINT "PK_effectivity_status_periods" PRIMARY KEY (id),
        CONSTRAINT "FK_effectivity_status_periods_effectivities_tenant_id_effectiv~" FOREIGN KEY (tenant_id, effectivity_id) REFERENCES kernel.effectivities (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    ALTER TABLE kernel.grants ADD CONSTRAINT ck_grants_scope CHECK ((scope_level = 'Tenant' AND scope_target_id IS NULL AND scope_country IS NULL) OR (scope_level IN ('Organization', 'Study', 'StudySite') AND scope_target_id IS NOT NULL AND scope_country IS NULL) OR (scope_level = 'StudyCountry' AND scope_target_id IS NOT NULL AND scope_country IS NOT NULL AND scope_country ~ '^[A-Z]{2}$'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    CREATE UNIQUE INDEX "IX_effectivities_tenant_id_protocol_version_id_scope_level_sco~" ON kernel.effectivities (tenant_id, protocol_version_id, scope_level, scope_country, scope_study_site_id) NULLS NOT DISTINCT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    CREATE INDEX "IX_effectivities_tenant_id_scope_study_site_id_study_id" ON kernel.effectivities (tenant_id, scope_study_site_id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    CREATE INDEX "IX_effectivities_tenant_id_study_id" ON kernel.effectivities (tenant_id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    CREATE INDEX "IX_effectivity_status_periods_tenant_id_effectivity_id" ON kernel.effectivity_status_periods (tenant_id, effectivity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    ALTER TABLE "kernel"."effectivity_status_periods" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."effectivity_status_periods" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."effectivity_status_periods"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    ALTER TABLE "kernel"."effectivities" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."effectivities" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."effectivities"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    ALTER TABLE "kernel"."effectivity_status_periods" ADD CONSTRAINT "ex_effectivity_status_periods_single_truth"
        EXCLUDE USING gist ("tenant_id" WITH =, "effectivity_id" WITH =, "valid_period" WITH &&, "recorded_period" WITH &&)
        DEFERRABLE INITIALLY DEFERRED;
    CREATE TRIGGER "trg_effectivity_status_periods_append_only" BEFORE UPDATE OR DELETE ON "kernel"."effectivity_status_periods"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    CREATE TRIGGER "trg_effectivity_status_periods_no_truncate" BEFORE TRUNCATE ON "kernel"."effectivity_status_periods"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930094042_EffectivityAndStudySiteActivation') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930094042_EffectivityAndStudySiteActivation', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930102009_ResolvedTerminologyBindings') THEN
    ALTER TABLE kernel.study_standards_profiles ADD controlled_terminology_versions uuid[] NOT NULL DEFAULT ARRAY[]::uuid[];
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930102009_ResolvedTerminologyBindings') THEN
    ALTER TABLE kernel.standard_terminology ADD code_system_version_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930102009_ResolvedTerminologyBindings') THEN
    CREATE INDEX "IX_standard_terminology_tenant_id_code_system_version_id" ON kernel.standard_terminology (tenant_id, code_system_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930102009_ResolvedTerminologyBindings') THEN
    ALTER TABLE kernel.standard_terminology ADD CONSTRAINT "FK_standard_terminology_code_system_versions_tenant_id_code_sy~" FOREIGN KEY (tenant_id, code_system_version_id) REFERENCES kernel.code_system_versions (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930102009_ResolvedTerminologyBindings') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930102009_ResolvedTerminologyBindings', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930110610_IdentityVault') THEN
    DO $guard$
    BEGIN
        PERFORM set_config('row_security', 'off', true);
        IF EXISTS (SELECT 1 FROM kernel.people p
                   WHERE p.email NOT IN ('', p.id::text) OR p.first_name <> '' OR p.last_name <> '') THEN
            RAISE EXCEPTION 'kernel.people holds direct identifiers; move them into the IdentityVault before this migration'
                USING ERRCODE = 'KV003';
        END IF;
        PERFORM set_config('row_security', 'on', true);
    END
    $guard$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930110610_IdentityVault') THEN
    DROP INDEX kernel."IX_people_email";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930110610_IdentityVault') THEN
    ALTER TABLE kernel.people DROP COLUMN email;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930110610_IdentityVault') THEN
    ALTER TABLE kernel.people DROP COLUMN first_name;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930110610_IdentityVault') THEN
    ALTER TABLE kernel.people DROP COLUMN last_name;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930110610_IdentityVault') THEN
    ALTER TABLE kernel.tenants ADD residency character varying(8);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930110610_IdentityVault') THEN
    ALTER TABLE kernel.tenants ADD CONSTRAINT ck_tenants_residency CHECK (residency IS NULL OR residency ~ '^[A-Z]{2,8}$');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930110610_IdentityVault') THEN
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
        USING (tenant_id = kernel.current_tenant_id()) WITH CHECK (tenant_id = kernel.current_tenant_id());
    ALTER TABLE identity_vault.participant_links ENABLE ROW LEVEL SECURITY;
    ALTER TABLE identity_vault.participant_links FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON identity_vault.participant_links
        USING (tenant_id = kernel.current_tenant_id()) WITH CHECK (tenant_id = kernel.current_tenant_id());

    CREATE FUNCTION identity_vault.require_tenant() RETURNS uuid
    LANGUAGE plpgsql STABLE SET search_path = pg_catalog, pg_temp AS $fn$
    DECLARE t uuid := kernel.current_tenant_id();
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
            RAISE EXCEPTION 'identity_vault: the record was erased' USING ERRCODE = 'KV001';
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
            RAISE EXCEPTION 'identity_vault: no unerased record to shred' USING ERRCODE = 'KV001';
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

    CREATE FUNCTION identity_vault.put_link(p_participant uuid, p_person uuid, p_linked_at timestamptz) RETURNS void
    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
    DECLARE t uuid := identity_vault.require_tenant();
    BEGIN
        INSERT INTO identity_vault.participant_links (tenant_id, participant_id, person_id, linked_at)
        VALUES (t, p_participant, p_person, p_linked_at)
        ON CONFLICT (tenant_id, participant_id) DO NOTHING;
        IF NOT EXISTS (SELECT 1 FROM identity_vault.participant_links l
                       WHERE l.tenant_id = t AND l.participant_id = p_participant AND l.person_id = p_person) THEN
            RAISE EXCEPTION 'identity_vault: the participant is linked to another person' USING ERRCODE = 'KV002';
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
    REVOKE ALL ON FUNCTION identity_vault.put_link(uuid, uuid, timestamptz) FROM PUBLIC;
    REVOKE ALL ON FUNCTION identity_vault.linked_person(uuid) FROM PUBLIC;
    REVOKE ALL ON FUNCTION identity_vault.linked_participants(uuid) FROM PUBLIC;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930110610_IdentityVault') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930110610_IdentityVault', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE TABLE kernel.qualification_requirements (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        capability character varying(96) NOT NULL,
        qualification_code character varying(64) NOT NULL,
        protocol_version_id uuid,
        scope_level character varying(16) NOT NULL,
        scope_target_id uuid,
        scope_country character varying(2),
        defined_by character varying(300) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_qualification_requirements" PRIMARY KEY (id),
        CONSTRAINT "AK_qualification_requirements_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_qualification_requirements_code CHECK (qualification_code ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
        CONSTRAINT ck_qualification_requirements_scope CHECK ((scope_level = 'Tenant' AND scope_target_id IS NULL AND scope_country IS NULL) OR (scope_level IN ('Organization', 'Study', 'StudySite') AND scope_target_id IS NOT NULL AND scope_country IS NULL) OR (scope_level = 'StudyCountry' AND scope_target_id IS NOT NULL AND scope_country IS NOT NULL AND scope_country ~ '^[A-Z]{2}$')),
        CONSTRAINT "FK_qualification_requirements_protocol_versions_tenant_id_prot~" FOREIGN KEY (tenant_id, protocol_version_id) REFERENCES kernel.protocol_versions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_qualification_requirements_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE TABLE kernel.qualifications (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        holder_kind character varying(32) NOT NULL,
        holder_id character varying(256) NOT NULL,
        kind character varying(32) NOT NULL,
        code character varying(64) NOT NULL,
        protocol_version_id uuid,
        evidence_artifact_version_id uuid NOT NULL,
        recorded_by character varying(300) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_qualifications" PRIMARY KEY (id),
        CONSTRAINT "AK_qualifications_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_qualifications_code CHECK (code ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
        CONSTRAINT ck_qualifications_protocol_version CHECK ((kind = 'ProtocolTraining') = (protocol_version_id IS NOT NULL)),
        CONSTRAINT "FK_qualifications_artifact_versions_tenant_id_evidence_artifac~" FOREIGN KEY (tenant_id, evidence_artifact_version_id) REFERENCES kernel.artifact_versions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_qualifications_protocol_versions_tenant_id_protocol_version~" FOREIGN KEY (tenant_id, protocol_version_id) REFERENCES kernel.protocol_versions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_qualifications_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE TABLE kernel.qualification_requirement_periods (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        requirement_id uuid NOT NULL,
        recorded_period tstzrange NOT NULL,
        valid_period tstzrange NOT NULL,
        CONSTRAINT "PK_qualification_requirement_periods" PRIMARY KEY (id),
        CONSTRAINT "FK_qualification_requirement_periods_qualification_requirement~" FOREIGN KEY (tenant_id, requirement_id) REFERENCES kernel.qualification_requirements (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE TABLE kernel.qualification_periods (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        qualification_id uuid NOT NULL,
        recorded_period tstzrange NOT NULL,
        valid_period tstzrange NOT NULL,
        CONSTRAINT "PK_qualification_periods" PRIMARY KEY (id),
        CONSTRAINT "FK_qualification_periods_qualifications_tenant_id_qualificatio~" FOREIGN KEY (tenant_id, qualification_id) REFERENCES kernel.qualifications (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE INDEX "IX_qualification_periods_tenant_id_qualification_id" ON kernel.qualification_periods (tenant_id, qualification_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE INDEX "IX_qualification_requirement_periods_tenant_id_requirement_id" ON kernel.qualification_requirement_periods (tenant_id, requirement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE INDEX "IX_qualification_requirements_tenant_id_capability" ON kernel.qualification_requirements (tenant_id, capability);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE INDEX "IX_qualification_requirements_tenant_id_protocol_version_id" ON kernel.qualification_requirements (tenant_id, protocol_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE INDEX "IX_qualifications_tenant_id_evidence_artifact_version_id" ON kernel.qualifications (tenant_id, evidence_artifact_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE INDEX "IX_qualifications_tenant_id_holder_kind_holder_id" ON kernel.qualifications (tenant_id, holder_kind, holder_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    CREATE INDEX "IX_qualifications_tenant_id_protocol_version_id" ON kernel.qualifications (tenant_id, protocol_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    ALTER TABLE "kernel"."qualification_periods" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."qualification_periods" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."qualification_periods"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    ALTER TABLE "kernel"."qualification_requirement_periods" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."qualification_requirement_periods" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."qualification_requirement_periods"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    ALTER TABLE "kernel"."qualification_requirements" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."qualification_requirements" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."qualification_requirements"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    ALTER TABLE "kernel"."qualifications" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."qualifications" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."qualifications"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    ALTER TABLE "kernel"."qualification_periods" ADD CONSTRAINT "ex_qualification_periods_single_truth"
        EXCLUDE USING gist ("tenant_id" WITH =, "qualification_id" WITH =, "valid_period" WITH &&, "recorded_period" WITH &&)
        DEFERRABLE INITIALLY DEFERRED;
    CREATE TRIGGER "trg_qualification_periods_append_only" BEFORE UPDATE OR DELETE ON "kernel"."qualification_periods"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    CREATE TRIGGER "trg_qualification_periods_no_truncate" BEFORE TRUNCATE ON "kernel"."qualification_periods"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    ALTER TABLE "kernel"."qualification_requirement_periods" ADD CONSTRAINT "ex_qualification_requirement_periods_single_truth"
        EXCLUDE USING gist ("tenant_id" WITH =, "requirement_id" WITH =, "valid_period" WITH &&, "recorded_period" WITH &&)
        DEFERRABLE INITIALLY DEFERRED;
    CREATE TRIGGER "trg_qualification_requirement_periods_append_only" BEFORE UPDATE OR DELETE ON "kernel"."qualification_requirement_periods"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    CREATE TRIGGER "trg_qualification_requirement_periods_no_truncate" BEFORE TRUNCATE ON "kernel"."qualification_requirement_periods"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_bitemporal_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930133015_Qualifications') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930133015_Qualifications', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930142430_Proposals') THEN
    CREATE TABLE kernel.proposals (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        intent character varying(2000) NOT NULL,
        plan_artifact_version_id uuid NOT NULL,
        affected text[] NOT NULL,
        simulated_impact character varying(71),
        requested_capabilities text[] NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        proposed_at timestamp with time zone NOT NULL,
        proposed_by_id character varying(256) NOT NULL,
        proposed_by_kind character varying(32) NOT NULL,
        model_id character varying(200),
        content_hash character varying(71) NOT NULL,
        status character varying(16) NOT NULL,
        decided_at timestamp with time zone,
        decided_by_id character varying(256),
        decided_by_kind character varying(32),
        approval_attestation_id uuid,
        decision_reason character varying(1000),
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_proposals" PRIMARY KEY (id),
        CONSTRAINT "AK_proposals_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_proposals_affected CHECK (cardinality(affected) <= 200),
        CONSTRAINT ck_proposals_content_hash CHECK (content_hash ~ '^sha256:[0-9a-f]{64}$'),
        CONSTRAINT ck_proposals_decision CHECK ((status = 'Open' AND decided_at IS NULL AND decided_by_id IS NULL AND decided_by_kind IS NULL AND approval_attestation_id IS NULL AND decision_reason IS NULL) OR (status = 'Approved' AND decided_at IS NOT NULL AND decided_by_id IS NOT NULL AND decided_by_kind = 'Person' AND approval_attestation_id IS NOT NULL) OR (status IN ('Rejected', 'Withdrawn') AND decided_at IS NOT NULL AND decided_by_id IS NOT NULL AND decided_by_kind IS NOT NULL AND approval_attestation_id IS NULL AND decision_reason IS NOT NULL)),
        CONSTRAINT ck_proposals_expiry CHECK (expires_at > proposed_at AND expires_at <= proposed_at + interval '90 days'),
        CONSTRAINT ck_proposals_intent CHECK (char_length(intent) BETWEEN 1 AND 2000),
        CONSTRAINT ck_proposals_model CHECK ((proposed_by_kind = 'AutomationAgent' AND model_id IS NOT NULL AND char_length(model_id) > 0) OR (proposed_by_kind <> 'AutomationAgent' AND model_id IS NULL)),
        CONSTRAINT ck_proposals_requested_capabilities CHECK (cardinality(requested_capabilities) BETWEEN 1 AND 20),
        CONSTRAINT ck_proposals_simulated_impact CHECK (simulated_impact IS NULL OR simulated_impact ~ '^sha256:[0-9a-f]{64}$'),
        CONSTRAINT "FK_proposals_artifact_versions_tenant_id_plan_artifact_version~" FOREIGN KEY (tenant_id, plan_artifact_version_id) REFERENCES kernel.artifact_versions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_proposals_attestations_tenant_id_approval_attestation_id" FOREIGN KEY (tenant_id, approval_attestation_id) REFERENCES kernel.attestations (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_proposals_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_proposals_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930142430_Proposals') THEN
    CREATE INDEX "IX_proposals_tenant_id_approval_attestation_id" ON kernel.proposals (tenant_id, approval_attestation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930142430_Proposals') THEN
    CREATE INDEX "IX_proposals_tenant_id_plan_artifact_version_id" ON kernel.proposals (tenant_id, plan_artifact_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930142430_Proposals') THEN
    CREATE INDEX "IX_proposals_tenant_id_study_id_status" ON kernel.proposals (tenant_id, study_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930142430_Proposals') THEN
    ALTER TABLE "kernel"."proposals" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."proposals" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."proposals"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930142430_Proposals') THEN
    CREATE TRIGGER "trg_proposals_immutable" BEFORE UPDATE ON "kernel"."proposals"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_immutable_columns('id', 'tenant_id', 'study_id', 'intent', 'plan_artifact_version_id', 'affected', 'simulated_impact', 'requested_capabilities', 'expires_at', 'proposed_at', 'proposed_by_id', 'proposed_by_kind', 'model_id', 'content_hash', 'created_at', 'created_by');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20260930142430_Proposals') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260930142430_Proposals', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006120000_AddFormVersionSnapshots') THEN
    CREATE TABLE kernel.form_version_snapshots (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        protocol_version_id uuid NOT NULL,
        protocol_version character varying(64) NOT NULL,
        form_id character varying(128) NOT NULL,
        form_hash character varying(71) NOT NULL,
        open_rosa_xml text NOT NULL,
        ddf_ir jsonb NOT NULL,
        odm_xml text,
        recorded_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_form_version_snapshots" PRIMARY KEY (id),
        CONSTRAINT ck_form_version_snapshots_form_hash CHECK (form_hash ~ '^sha256:[0-9a-f]{64}$'),
        CONSTRAINT "FK_form_version_snapshots_protocol_versions_tenant_id_protoc~" FOREIGN KEY (tenant_id, protocol_version_id) REFERENCES kernel.protocol_versions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_form_version_snapshots_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_form_version_snapshots_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006120000_AddFormVersionSnapshots') THEN
    CREATE INDEX "IX_form_version_snapshots_tenant_id_protocol_version_id" ON kernel.form_version_snapshots (tenant_id, protocol_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006120000_AddFormVersionSnapshots') THEN
    CREATE UNIQUE INDEX "IX_form_version_snapshots_tenant_id_study_id_protocol_versio~" ON kernel.form_version_snapshots (tenant_id, study_id, protocol_version, form_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006120000_AddFormVersionSnapshots') THEN
    ALTER TABLE "kernel"."form_version_snapshots" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."form_version_snapshots" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."form_version_snapshots"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006120000_AddFormVersionSnapshots') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261006120000_AddFormVersionSnapshots', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    ALTER TABLE kernel.proposals ADD confidence_score double precision;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    ALTER TABLE kernel.proposals ADD escalated_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    ALTER TABLE kernel.proposals ADD escalated_by_id character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    ALTER TABLE kernel.proposals ADD escalated_by_kind character varying(32);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    ALTER TABLE kernel.proposals ADD escalated_to character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    ALTER TABLE kernel.proposals ADD escalation_policy character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    ALTER TABLE kernel.proposals ADD escalation_reason character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    ALTER TABLE kernel.proposals ADD is_escalated boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    ALTER TABLE kernel.proposals ADD CONSTRAINT ck_proposals_confidence_score CHECK (confidence_score IS NULL OR (confidence_score >= 0.0 AND confidence_score <= 1.0));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261006192109_ProposalConfidenceAndEscalation') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261006192109_ProposalConfidenceAndEscalation', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    CREATE TABLE kernel.observations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        enrollment_id uuid NOT NULL,
        participant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        concept_id uuid NOT NULL,
        concept_version integer NOT NULL,
        observed_at timestamp with time zone NOT NULL,
        value character varying(4000) NOT NULL,
        revision integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_observations" PRIMARY KEY (id),
        CONSTRAINT "AK_observations_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_observations_revision CHECK (revision > 0),
        CONSTRAINT "FK_observations_enrollments_tenant_id_enrollment_id" FOREIGN KEY (tenant_id, enrollment_id) REFERENCES kernel.enrollments (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_observations_participants_tenant_id_participant_id" FOREIGN KEY (tenant_id, participant_id) REFERENCES kernel.participants (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_observations_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_observations_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    CREATE TABLE kernel.observation_locks (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        observation_id uuid NOT NULL,
        enrollment_id uuid NOT NULL,
        locked_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_observation_locks" PRIMARY KEY (id),
        CONSTRAINT "FK_observation_locks_observations_tenant_id_observation_id" FOREIGN KEY (tenant_id, observation_id) REFERENCES kernel.observations (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_observation_locks_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    CREATE TABLE kernel.observation_revisions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        observation_id uuid NOT NULL,
        number integer NOT NULL,
        value character varying(4000) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        recorded_by character varying(255) NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_observation_revisions" PRIMARY KEY (id),
        CONSTRAINT ck_observation_revisions_number CHECK (number > 0),
        CONSTRAINT "FK_observation_revisions_observations_tenant_id_observation_id" FOREIGN KEY (tenant_id, observation_id) REFERENCES kernel.observations (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    CREATE UNIQUE INDEX "IX_observation_locks_tenant_id_observation_id" ON kernel.observation_locks (tenant_id, observation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    CREATE UNIQUE INDEX "IX_observation_revisions_tenant_id_observation_id_number" ON kernel.observation_revisions (tenant_id, observation_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    CREATE INDEX "IX_observations_tenant_id_enrollment_id" ON kernel.observations (tenant_id, enrollment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    CREATE INDEX "IX_observations_tenant_id_participant_id_concept_id" ON kernel.observations (tenant_id, participant_id, concept_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    CREATE INDEX "IX_observations_tenant_id_study_id" ON kernel.observations (tenant_id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    ALTER TABLE "kernel"."observations" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."observations" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."observations"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    ALTER TABLE "kernel"."observation_revisions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."observation_revisions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."observation_revisions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    ALTER TABLE "kernel"."observation_locks" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."observation_locks" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."observation_locks"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    CREATE TRIGGER "trg_observation_revisions_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."observation_revisions"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_observation_revisions_no_truncate" BEFORE TRUNCATE ON "kernel"."observation_revisions"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261015191056_ObservationsAndLocks') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261015191056_ObservationsAndLocks', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261019215800_TypedObservationValues') THEN
    ALTER TABLE kernel.observations
        ALTER COLUMN value TYPE jsonb USING jsonb_build_object('type', 'text', 'value', value);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261019215800_TypedObservationValues') THEN
    ALTER TABLE kernel.observation_revisions
        ALTER COLUMN value TYPE jsonb USING jsonb_build_object('type', 'text', 'value', value);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261019215800_TypedObservationValues') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261019215800_TypedObservationValues', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    DROP POLICY IF EXISTS "tenant_isolation" ON "kernel"."observation_locks";
    ALTER TABLE "kernel"."observation_locks" NO FORCE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."observation_locks" DISABLE ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    DROP TABLE kernel.observation_locks;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    CREATE TABLE kernel.assertions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        kind character varying(32) NOT NULL,
        study_id uuid NOT NULL,
        enrollment_id uuid,
        target_kind character varying(64) NOT NULL,
        target_id uuid NOT NULL,
        target_revision integer,
        text character varying(4000),
        retracts_id uuid,
        attestation_id uuid,
        asserted_by character varying(255) NOT NULL,
        asserted_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_assertions" PRIMARY KEY (id),
        CONSTRAINT "AK_assertions_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT ck_assertions_lock_attested CHECK (kind NOT IN ('Locked', 'Frozen') OR attestation_id IS NOT NULL),
        CONSTRAINT ck_assertions_target_revision CHECK (target_revision IS NULL OR target_revision > 0),
        CONSTRAINT "FK_assertions_assertions_tenant_id_retracts_id" FOREIGN KEY (tenant_id, retracts_id) REFERENCES kernel.assertions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_assertions_attestations_tenant_id_attestation_id" FOREIGN KEY (tenant_id, attestation_id) REFERENCES kernel.attestations (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_assertions_enrollments_tenant_id_enrollment_id" FOREIGN KEY (tenant_id, enrollment_id) REFERENCES kernel.enrollments (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_assertions_studies_tenant_id_study_id" FOREIGN KEY (tenant_id, study_id) REFERENCES kernel.studies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_assertions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    CREATE INDEX "IX_assertions_tenant_id_attestation_id" ON kernel.assertions (tenant_id, attestation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    CREATE INDEX "IX_assertions_tenant_id_enrollment_id" ON kernel.assertions (tenant_id, enrollment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    CREATE UNIQUE INDEX "IX_assertions_tenant_id_retracts_id" ON kernel.assertions (tenant_id, retracts_id) WHERE retracts_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    CREATE INDEX "IX_assertions_tenant_id_study_id" ON kernel.assertions (tenant_id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    CREATE INDEX "IX_assertions_tenant_id_target_id_kind" ON kernel.assertions (tenant_id, target_id, kind);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    ALTER TABLE "kernel"."assertions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."assertions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."assertions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    CREATE TRIGGER "trg_assertions_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."assertions"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_assertions_no_truncate" BEFORE TRUNCATE ON "kernel"."assertions"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000021_AssertionsAndScopedLocks') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261020000021_AssertionsAndScopedLocks', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    CREATE TABLE kernel.collection_instances (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        study_id uuid NOT NULL,
        study_site_id uuid NOT NULL,
        enrollment_id uuid NOT NULL,
        participant_id uuid NOT NULL,
        protocol_version_id uuid NOT NULL,
        collection_spec_id character varying(128) NOT NULL,
        planned_activity_id character varying(128),
        collected_at timestamp with time zone NOT NULL,
        opened_by character varying(255) NOT NULL,
        opened_at timestamp with time zone NOT NULL,
        last_saved_at timestamp with time zone NOT NULL,
        saves integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_collection_instances" PRIMARY KEY (id),
        CONSTRAINT "AK_collection_instances_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_collection_instances_enrollments_tenant_id_enrollment_id_pa~" FOREIGN KEY (tenant_id, enrollment_id, participant_id, study_site_id) REFERENCES kernel.enrollments (tenant_id, id, participant_id, study_site_id) ON DELETE RESTRICT,
        CONSTRAINT "FK_collection_instances_protocol_versions_tenant_id_protocol_v~" FOREIGN KEY (tenant_id, protocol_version_id) REFERENCES kernel.protocol_versions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_collection_instances_study_sites_tenant_id_study_site_id_st~" FOREIGN KEY (tenant_id, study_site_id, study_id) REFERENCES kernel.study_sites (tenant_id, id, study_id) ON DELETE RESTRICT,
        CONSTRAINT "FK_collection_instances_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    CREATE TABLE kernel.collection_instance_members (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        collection_instance_id uuid NOT NULL,
        item_id character varying(128) NOT NULL,
        observation_id uuid NOT NULL,
        joined_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_collection_instance_members" PRIMARY KEY (id),
        CONSTRAINT "FK_collection_instance_members_collection_instances_tenant_id_~" FOREIGN KEY (tenant_id, collection_instance_id) REFERENCES kernel.collection_instances (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_collection_instance_members_observations_tenant_id_observat~" FOREIGN KEY (tenant_id, observation_id) REFERENCES kernel.observations (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    CREATE UNIQUE INDEX "IX_collection_instance_members_tenant_id_collection_instance_i~" ON kernel.collection_instance_members (tenant_id, collection_instance_id, item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    CREATE UNIQUE INDEX "IX_collection_instance_members_tenant_id_observation_id" ON kernel.collection_instance_members (tenant_id, observation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    CREATE INDEX "IX_collection_instances_tenant_id_enrollment_id_participant_id~" ON kernel.collection_instances (tenant_id, enrollment_id, participant_id, study_site_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    CREATE INDEX "IX_collection_instances_tenant_id_protocol_version_id" ON kernel.collection_instances (tenant_id, protocol_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    CREATE INDEX "IX_collection_instances_tenant_id_study_site_id_study_id" ON kernel.collection_instances (tenant_id, study_site_id, study_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    ALTER TABLE "kernel"."collection_instances" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."collection_instances" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."collection_instances"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    ALTER TABLE "kernel"."collection_instance_members" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."collection_instance_members" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."collection_instance_members"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    CREATE TRIGGER "trg_collection_instance_members_insert_only" BEFORE UPDATE OR DELETE ON "kernel"."collection_instance_members"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_insert_only();
    CREATE TRIGGER "trg_collection_instance_members_no_truncate" BEFORE TRUNCATE ON "kernel"."collection_instance_members"
        FOR EACH STATEMENT EXECUTE FUNCTION "kernel".enforce_insert_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020000022_CollectionInstances') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261020000022_CollectionInstances', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020031614_AgentRequesterAndProposalExecution') THEN
    ALTER TABLE kernel.provenance_activities ADD proposal_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020031614_AgentRequesterAndProposalExecution') THEN
    ALTER TABLE kernel.provenance_activities ADD requested_by character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020031614_AgentRequesterAndProposalExecution') THEN
    ALTER TABLE kernel.proposals ADD requested_by_id character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020031614_AgentRequesterAndProposalExecution') THEN
    CREATE INDEX "IX_provenance_activities_tenant_id_proposal_id" ON kernel.provenance_activities (tenant_id, proposal_id) WHERE proposal_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020031614_AgentRequesterAndProposalExecution') THEN
    ALTER TABLE kernel.proposals ADD CONSTRAINT ck_proposals_requested_by CHECK ((proposed_by_kind = 'AutomationAgent' AND requested_by_id IS NOT NULL AND char_length(requested_by_id) > 0) OR (proposed_by_kind <> 'AutomationAgent' AND requested_by_id IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020031614_AgentRequesterAndProposalExecution') THEN
    DROP TRIGGER IF EXISTS "trg_proposals_immutable" ON "kernel"."proposals";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020031614_AgentRequesterAndProposalExecution') THEN
    CREATE TRIGGER "trg_proposals_immutable" BEFORE UPDATE ON "kernel"."proposals"
        FOR EACH ROW EXECUTE FUNCTION "kernel".enforce_immutable_columns('id', 'tenant_id', 'study_id', 'intent', 'plan_artifact_version_id', 'affected', 'simulated_impact', 'requested_capabilities', 'expires_at', 'proposed_at', 'proposed_by_id', 'proposed_by_kind', 'model_id', 'content_hash', 'created_at', 'created_by', 'requested_by_id', 'confidence_score');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261020031614_AgentRequesterAndProposalExecution') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261020031614_AgentRequesterAndProposalExecution', '10.0.15');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261120152149_CommercialSubscriptionsAndEntitlements') THEN
    CREATE TABLE kernel.subscriptions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        tier character varying(32) NOT NULL,
        status character varying(32) NOT NULL,
        started_at timestamp with time zone NOT NULL,
        trial_ends_at timestamp with time zone,
        expired_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_subscriptions" PRIMARY KEY (id),
        CONSTRAINT "AK_subscriptions_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_subscriptions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261120152149_CommercialSubscriptionsAndEntitlements') THEN
    CREATE TABLE kernel.product_entitlements (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        subscription_id uuid NOT NULL,
        family character varying(32) NOT NULL,
        is_enabled boolean NOT NULL,
        granted_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        created_by character varying(255) NOT NULL,
        reason_for_change character varying(1000) NOT NULL,
        version_index bigint NOT NULL,
        CONSTRAINT "PK_product_entitlements" PRIMARY KEY (id),
        CONSTRAINT "AK_product_entitlements_tenant_id_id" UNIQUE (tenant_id, id),
        CONSTRAINT "FK_product_entitlements_subscriptions_tenant_id_subscription_id" FOREIGN KEY (tenant_id, subscription_id) REFERENCES kernel.subscriptions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT "FK_product_entitlements_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES kernel.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261120152149_CommercialSubscriptionsAndEntitlements') THEN
    CREATE UNIQUE INDEX "IX_product_entitlements_tenant_id_family" ON kernel.product_entitlements (tenant_id, family);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261120152149_CommercialSubscriptionsAndEntitlements') THEN
    CREATE INDEX "IX_product_entitlements_tenant_id_subscription_id" ON kernel.product_entitlements (tenant_id, subscription_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261120152149_CommercialSubscriptionsAndEntitlements') THEN
    CREATE UNIQUE INDEX "IX_subscriptions_tenant_id" ON kernel.subscriptions (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261120152149_CommercialSubscriptionsAndEntitlements') THEN
    ALTER TABLE "kernel"."subscriptions" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."subscriptions" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."subscriptions"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261120152149_CommercialSubscriptionsAndEntitlements') THEN
    ALTER TABLE "kernel"."product_entitlements" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE "kernel"."product_entitlements" FORCE ROW LEVEL SECURITY;
    CREATE POLICY "tenant_isolation" ON "kernel"."product_entitlements"
        USING (tenant_id = "kernel".current_tenant_id())
        WITH CHECK (tenant_id = "kernel".current_tenant_id());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM kernel.__ef_migrations_history WHERE "MigrationId" = '20261120152149_CommercialSubscriptionsAndEntitlements') THEN
    INSERT INTO kernel.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261120152149_CommercialSubscriptionsAndEntitlements', '10.0.15');
    END IF;
END $EF$;
COMMIT;

