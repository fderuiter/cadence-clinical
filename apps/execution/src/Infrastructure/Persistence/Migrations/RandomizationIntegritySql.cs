namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <summary>
/// Database guards for randomisation (#5607): <c>randomization_allocations</c> is insert-only, and a
/// <c>randomization_list_slots</c> row can only move its <c>AllocationId</c> from null to a value once; its arm,
/// stratum and position never change and it is never deleted.
/// </summary>
public static class RandomizationIntegritySql
{
    public const string Create = """
        CREATE OR REPLACE FUNCTION execution_randomization_allocation_immutable() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'randomization_allocations is insert-only (%)', TG_OP USING ERRCODE = 'integrity_constraint_violation';
        END;
        $$;

        CREATE TRIGGER randomization_allocations_immutable
            BEFORE UPDATE OR DELETE ON randomization_allocations
            FOR EACH ROW EXECUTE FUNCTION execution_randomization_allocation_immutable();

        CREATE OR REPLACE FUNCTION execution_randomization_slot_consume_once() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'DELETE' THEN
                RAISE EXCEPTION 'randomization_list_slots rows are never deleted' USING ERRCODE = 'integrity_constraint_violation';
            END IF;
            IF OLD."AllocationId" IS NOT NULL
                OR NEW."AllocationId" IS NULL
                OR NEW."Id" <> OLD."Id"
                OR NEW."SchemeId" <> OLD."SchemeId"
                OR NEW."StratumKey" <> OLD."StratumKey"
                OR NEW."Sequence" <> OLD."Sequence"
                OR NEW."ArmCiphertext" <> OLD."ArmCiphertext" THEN
                RAISE EXCEPTION 'a randomization list slot can only be consumed, once' USING ERRCODE = 'integrity_constraint_violation';
            END IF;
            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER randomization_list_slots_consume_once
            BEFORE UPDATE OR DELETE ON randomization_list_slots
            FOR EACH ROW EXECUTE FUNCTION execution_randomization_slot_consume_once();
        """;

    public const string Drop = """
        DROP TRIGGER IF EXISTS randomization_list_slots_consume_once ON randomization_list_slots;
        DROP FUNCTION IF EXISTS execution_randomization_slot_consume_once();
        DROP TRIGGER IF EXISTS randomization_allocations_immutable ON randomization_allocations;
        DROP FUNCTION IF EXISTS execution_randomization_allocation_immutable();
        """;
}
