using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;
using Cadence.Security.Authentication;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>EF Core <see cref="IRandomizationRepository"/> over the shared Execution context (#5607).</summary>
public sealed class RandomizationRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), IRandomizationRepository
{
    private const int FastUnnestBatchSize = 4_096;
    private const int GeneralUnnestBatchSize = 2_048;

    private const string NpgsqlSingleSchemeBulkInsertSlotsSql = """
        INSERT INTO randomization_list_slots ("Id", "SchemeId", "StratumKey", "Sequence", "ArmCiphertext", "AllocationId", tenant_id, _version)
        SELECT u.id, @scheme_id, u.stratum_key, u.seq, u.arm_ciphertext, CAST(NULL AS uuid), @tenant_id, 0
        FROM unnest(CAST(@ids AS uuid[]), CAST(@stratum_keys AS varchar[]), CAST(@sequences AS integer[]), CAST(@arm_ciphertexts AS text[]))
            AS u(id, stratum_key, seq, arm_ciphertext);
        """;

    private const string NpgsqlGeneralBulkInsertSlotsSql = """
        INSERT INTO randomization_list_slots ("Id", "SchemeId", "StratumKey", "Sequence", "ArmCiphertext", "AllocationId", tenant_id, _version)
        SELECT u.id, u.scheme_id, u.stratum_key, u.seq, u.arm_ciphertext, u.allocation_id, @tenant_id, 0
        FROM unnest(CAST(@ids AS uuid[]), CAST(@scheme_ids AS uuid[]), CAST(@stratum_keys AS varchar[]), CAST(@sequences AS integer[]), CAST(@arm_ciphertexts AS text[]), CAST(@allocation_ids AS uuid[]))
            AS u(id, scheme_id, stratum_key, seq, arm_ciphertext, allocation_id);
        """;

    private const string RelationalInsertSlotSql = """
        INSERT INTO randomization_list_slots ("Id", "SchemeId", "StratumKey", "Sequence", "ArmCiphertext", "AllocationId", tenant_id, _version)
        VALUES (@id, @scheme_id, @stratum_key, @sequence, @arm_ciphertext, @allocation_id, @tenant_id, 0);
        """;

    private static readonly ProductFamily[] ExecutionFamilies =
    [
        ProductFamily.Capture,
        ProductFamily.Allocate,
        ProductFamily.Vigil,
        ProductFamily.Lens,
        ProductFamily.Orbit,
    ];

    public async Task LockStudyAsync(StudyCode studyCode, CancellationToken cancellationToken)
    {
        EnsureRelationalTransaction(nameof(LockStudyAsync), "randomisation");
        if (!Context.Database.IsNpgsql())
        {
            return;
        }

        await Context.Database.ExecuteSqlInterpolatedAsync(AdvisoryLockSql.ForRandomization(studyCode), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task LockSchemeAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        EnsureRelationalTransaction(nameof(LockSchemeAsync), "scheme");
        if (!Context.Database.IsNpgsql())
        {
            return;
        }

        await Context.Database.ExecuteSqlInterpolatedAsync(AdvisoryLockSql.ForRandomizationScheme(schemeId), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RandomizationScheme>> ListSchemesAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
        await Context.RandomizationSchemes.AsNoTracking()
            .Where(s => s.StudyCode == studyCode)
            .OrderByDescending(s => s.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<RandomizationScheme?> GetSchemeAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        DetachUnchangedScheme(schemeId);
        return Context.RandomizationSchemes.SingleOrDefaultAsync(s => s.Id == schemeId, cancellationToken);
    }

    public async Task<RandomizationScheme?> GetApprovedSchemeAsync(StudyCode studyCode, CancellationToken cancellationToken)
    {
        var alreadyTrackedUnchanged = Context.ChangeTracker.Entries<RandomizationScheme>()
            .Where(e => e.State == EntityState.Unchanged && e.Entity.StudyCode == studyCode)
            .Select(e => e.Entity.Id)
            .ToHashSet();

        var approved = await Context.RandomizationSchemes.SingleOrDefaultAsync(
                s => s.StudyCode == studyCode && s.Status == RandomizationSchemeStatus.Approved,
                cancellationToken)
            .ConfigureAwait(false);

        if (approved is not null && alreadyTrackedUnchanged.Contains(approved.Id))
        {
            await Context.Entry(approved).ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (approved.Status != RandomizationSchemeStatus.Approved)
            {
                return null;
            }
        }

        return approved;
    }

    public async Task<int> GetLatestVersionAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
        await Context.RandomizationSchemes.AsNoTracking()
            .Where(s => s.StudyCode == studyCode)
            .Select(s => (int?)s.Version)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

    public async Task AddSchemeAsync(RandomizationScheme scheme, CancellationToken cancellationToken) =>
        await Context.RandomizationSchemes.AddAsync(scheme, cancellationToken).ConfigureAwait(false);

    public async Task AddListSlotsAsync(IReadOnlyCollection<RandomizationListSlot> slots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            return;
        }

        Guid tenantId = await ResolveVerifiedTenantForSlotWriteAsync(cancellationToken).ConfigureAwait(false);

        if (Context.Database.IsNpgsql())
        {
            EnsureRelationalTransaction(nameof(AddListSlotsAsync), "slot staging");
            await BulkInsertNpgsqlSlotsAsync(slots, tenantId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (Context.Database.IsRelational())
        {
            EnsureRelationalTransaction(nameof(AddListSlotsAsync), "slot staging");
            await BulkInsertRelationalSlotsAsync(slots, tenantId, cancellationToken).ConfigureAwait(false);
            return;
        }

        await InsertInMemorySlotsUntrackedAsync(slots, cancellationToken).ConfigureAwait(false);
    }

    public Task<RandomizationListSlot?> GetNextFreeSlotAsync(Guid schemeId, string stratumKey, CancellationToken cancellationToken) =>
        Context.RandomizationListSlots
            .Where(s => s.SchemeId == schemeId && s.StratumKey == stratumKey && s.AllocationId == null)
            .OrderBy(s => s.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<RandomizationAllocation?> GetAllocationForSubjectAsync(SubjectId subjectId, CancellationToken cancellationToken) =>
        Context.RandomizationAllocations.AsNoTracking().SingleOrDefaultAsync(a => a.SubjectId == subjectId, cancellationToken);

    public async Task<IReadOnlyList<RandomizationAllocation>> ListAllocationsAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
        await Context.RandomizationAllocations.AsNoTracking()
            .Where(a => a.StudyCode == studyCode)
            .OrderBy(a => a.AllocatedAt)
            .ThenBy(a => a.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAllocationAsync(RandomizationAllocation allocation, CancellationToken cancellationToken) =>
        await Context.RandomizationAllocations.AddAsync(allocation, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<IpKitInventoryItem>> ListAssignableKitsAsync(StudyCode studyCode, SiteCode siteCode, DateTimeOffset at,
        CancellationToken cancellationToken) =>
        await Context.IpKitInventoryItems
            .Where(k => k.StudyId == studyCode.Value
                && k.SiteId == siteCode.Value
                && k.Status == IpKitInventoryItem.ActiveAtSiteStatus
                && k.AssignedRandomizationId == null
                && k.TreatmentArmCiphertext != null
                && k.ExpirationDate > at)
            .OrderBy(k => k.ExpirationDate)
            .ThenBy(k => k.KitNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private void EnsureRelationalTransaction(string methodName, string lockOrOperationLabel)
    {
        if (Context.Database.IsRelational() && Context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                $"{methodName} must run inside {nameof(ExecuteInTransactionAsync)}, which holds the {lockOrOperationLabel} lock until commit.");
        }
    }

    private void DetachUnchangedScheme(Guid schemeId)
    {
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<RandomizationScheme>? match = null;
        foreach (var entry in Context.ChangeTracker.Entries<RandomizationScheme>())
        {
            if (entry.State == EntityState.Unchanged && entry.Entity.Id == schemeId)
            {
                match = entry;
                break;
            }
        }

        if (match is not null)
        {
            match.State = EntityState.Detached;
        }
    }

    private async ValueTask<Guid> ResolveVerifiedTenantForSlotWriteAsync(CancellationToken cancellationToken)
    {
        var ext = Context.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        if (ext?.TenantAccessor is null)
        {
            return Guid.Empty;
        }

        EnforceCommercialPosture(ext.TenantAccessor);

        Guid? resolvedTenantId = await ext.TenantAccessor.GetCurrentTenantIdAsync(cancellationToken).ConfigureAwait(false);
        if (resolvedTenantId is { } activeTenant && activeTenant != Guid.Empty)
        {
            return activeTenant;
        }

        if (!ext.IsRelational && !ext.TenantAccessor.HasHttpRequestWithoutTenant)
        {
            return Guid.Empty;
        }

        throw new InvalidOperationException(
            "K-ID-2: cannot persist tenant-scoped standalone entities without an active verified tenant.");
    }

    private static void EnforceCommercialPosture(ICurrentTenantAccessor accessor)
    {
        if (accessor.IsSystemSuppressed)
        {
            return;
        }

        if (accessor.IsCurrentTenantReadOnly && !accessor.IsHttpWriteExemptFromExpiry)
        {
            throw new InvalidOperationException(
                $"{KernelErrors.SubscriptionExpired.Code}: cannot persist tenant-scoped standalone entities while workspace subscription is expired.");
        }

        if (!accessor.IsHttpProductFamilyExempt
            && accessor.CurrentTenantProductFamilies is { } activeFamilies
            && !HasAnyExecutionFamily(activeFamilies))
        {
            string requiredList = string.Join(", ", ExecutionFamilies.Select(static f => f.ToString()));
            throw new InvalidOperationException(
                $"{CommercialErrors.ProductFamilyNotEntitled.Code}: cannot persist tenant-scoped standalone entities in {nameof(ExecutionDbContext)} without an entitled product family ({requiredList}).");
        }
    }

    private static bool HasAnyExecutionFamily(IReadOnlyList<string> activeFamilies)
    {
        for (int i = 0; i < ExecutionFamilies.Length; i++)
        {
            string expected = ExecutionFamilies[i].ToString();
            for (int j = 0; j < activeFamilies.Count; j++)
            {
                if (string.Equals(activeFamilies[j], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private Task BulkInsertNpgsqlSlotsAsync(
        IReadOnlyCollection<RandomizationListSlot> slots,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        TryGetSingleUnallocatedSchemeId(slots, out Guid schemeId)
            ? BulkInsertSingleSchemeNpgsqlSlotsAsync(slots, schemeId, tenantId, cancellationToken)
            : BulkInsertGeneralNpgsqlSlotsAsync(slots, tenantId, cancellationToken);

    private static bool TryGetSingleUnallocatedSchemeId(
        IReadOnlyCollection<RandomizationListSlot> slots,
        out Guid schemeId)
    {
        schemeId = Guid.Empty;
        bool hasFirst = false;
        foreach (RandomizationListSlot slot in slots)
        {
            if (slot.AllocationId is not null)
            {
                return false;
            }

            if (!hasFirst)
            {
                schemeId = slot.SchemeId;
                hasFirst = true;
            }
            else if (slot.SchemeId != schemeId)
            {
                return false;
            }
        }

        return hasFirst;
    }

    private async Task BulkInsertSingleSchemeNpgsqlSlotsAsync(
        IReadOnlyCollection<RandomizationListSlot> slots,
        Guid schemeId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)Context.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)Context.Database.CurrentTransaction!.GetDbTransaction();

        int bufferCapacity = Math.Min(slots.Count, FastUnnestBatchSize);
        var ids = new Guid[bufferCapacity];
        var stratumKeys = new string[bufferCapacity];
        var sequences = new int[bufferCapacity];
        var armCiphertexts = new string[bufferCapacity];

        await using var command = new NpgsqlCommand(NpgsqlSingleSchemeBulkInsertSlotsSql, connection, transaction);
        var idsParam = new NpgsqlParameter<Guid[]> { ParameterName = "ids" };
        var stratumKeysParam = new NpgsqlParameter<string[]> { ParameterName = "stratum_keys" };
        var sequencesParam = new NpgsqlParameter<int[]> { ParameterName = "sequences" };
        var armCiphertextsParam = new NpgsqlParameter<string[]> { ParameterName = "arm_ciphertexts" };
        var schemeIdParam = new NpgsqlParameter<Guid> { ParameterName = "scheme_id", TypedValue = schemeId };
        var tenantParam = new NpgsqlParameter<Guid> { ParameterName = "tenant_id", TypedValue = tenantId };

        command.Parameters.Add(idsParam);
        command.Parameters.Add(stratumKeysParam);
        command.Parameters.Add(sequencesParam);
        command.Parameters.Add(armCiphertextsParam);
        command.Parameters.Add(schemeIdParam);
        command.Parameters.Add(tenantParam);
        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        int countInBatch = 0;
        foreach (RandomizationListSlot slot in slots)
        {
            ids[countInBatch] = slot.Id;
            stratumKeys[countInBatch] = slot.StratumKey;
            sequences[countInBatch] = slot.Sequence;
            armCiphertexts[countInBatch] = slot.ArmCiphertext;
            countInBatch++;

            if (countInBatch == bufferCapacity)
            {
                idsParam.TypedValue = ids;
                stratumKeysParam.TypedValue = stratumKeys;
                sequencesParam.TypedValue = sequences;
                armCiphertextsParam.TypedValue = armCiphertexts;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                countInBatch = 0;
            }
        }

        if (countInBatch > 0)
        {
            idsParam.TypedValue = ids[..countInBatch];
            stratumKeysParam.TypedValue = stratumKeys[..countInBatch];
            sequencesParam.TypedValue = sequences[..countInBatch];
            armCiphertextsParam.TypedValue = armCiphertexts[..countInBatch];
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task BulkInsertGeneralNpgsqlSlotsAsync(
        IReadOnlyCollection<RandomizationListSlot> slots,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)Context.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)Context.Database.CurrentTransaction!.GetDbTransaction();

        int bufferCapacity = Math.Min(slots.Count, GeneralUnnestBatchSize);
        var ids = new Guid[bufferCapacity];
        var schemeIds = new Guid[bufferCapacity];
        var stratumKeys = new string[bufferCapacity];
        var sequences = new int[bufferCapacity];
        var armCiphertexts = new string[bufferCapacity];
        var allocationIds = new Guid?[bufferCapacity];

        await using var command = new NpgsqlCommand(NpgsqlGeneralBulkInsertSlotsSql, connection, transaction);
        var idsParam = new NpgsqlParameter<Guid[]> { ParameterName = "ids" };
        var schemeIdsParam = new NpgsqlParameter<Guid[]> { ParameterName = "scheme_ids" };
        var stratumKeysParam = new NpgsqlParameter<string[]> { ParameterName = "stratum_keys" };
        var sequencesParam = new NpgsqlParameter<int[]> { ParameterName = "sequences" };
        var armCiphertextsParam = new NpgsqlParameter<string[]> { ParameterName = "arm_ciphertexts" };
        var allocationIdsParam = new NpgsqlParameter<Guid?[]> { ParameterName = "allocation_ids" };
        var tenantParam = new NpgsqlParameter<Guid> { ParameterName = "tenant_id", TypedValue = tenantId };

        command.Parameters.Add(idsParam);
        command.Parameters.Add(schemeIdsParam);
        command.Parameters.Add(stratumKeysParam);
        command.Parameters.Add(sequencesParam);
        command.Parameters.Add(armCiphertextsParam);
        command.Parameters.Add(allocationIdsParam);
        command.Parameters.Add(tenantParam);
        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        int countInBatch = 0;
        foreach (RandomizationListSlot slot in slots)
        {
            ids[countInBatch] = slot.Id;
            schemeIds[countInBatch] = slot.SchemeId;
            stratumKeys[countInBatch] = slot.StratumKey;
            sequences[countInBatch] = slot.Sequence;
            armCiphertexts[countInBatch] = slot.ArmCiphertext;
            allocationIds[countInBatch] = slot.AllocationId;
            countInBatch++;

            if (countInBatch == bufferCapacity)
            {
                idsParam.TypedValue = ids;
                schemeIdsParam.TypedValue = schemeIds;
                stratumKeysParam.TypedValue = stratumKeys;
                sequencesParam.TypedValue = sequences;
                armCiphertextsParam.TypedValue = armCiphertexts;
                allocationIdsParam.TypedValue = allocationIds;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                countInBatch = 0;
            }
        }

        if (countInBatch > 0)
        {
            idsParam.TypedValue = ids[..countInBatch];
            schemeIdsParam.TypedValue = schemeIds[..countInBatch];
            stratumKeysParam.TypedValue = stratumKeys[..countInBatch];
            sequencesParam.TypedValue = sequences[..countInBatch];
            armCiphertextsParam.TypedValue = armCiphertexts[..countInBatch];
            allocationIdsParam.TypedValue = allocationIds[..countInBatch];
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task BulkInsertRelationalSlotsAsync(
        IReadOnlyCollection<RandomizationListSlot> slots,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        DbConnection connection = Context.Database.GetDbConnection();
        DbTransaction transaction = Context.Database.CurrentTransaction!.GetDbTransaction();

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RelationalInsertSlotSql;

        DbParameter idParam = AddParameter(command, "@id");
        DbParameter schemeIdParam = AddParameter(command, "@scheme_id");
        DbParameter stratumKeyParam = AddParameter(command, "@stratum_key");
        DbParameter sequenceParam = AddParameter(command, "@sequence");
        DbParameter armCiphertextParam = AddParameter(command, "@arm_ciphertext");
        DbParameter allocationIdParam = AddParameter(command, "@allocation_id");
        DbParameter tenantParam = AddParameter(command, "@tenant_id");
        tenantParam.Value = tenantId;

        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        foreach (RandomizationListSlot slot in slots)
        {
            idParam.Value = slot.Id;
            schemeIdParam.Value = slot.SchemeId;
            stratumKeyParam.Value = slot.StratumKey;
            sequenceParam.Value = slot.Sequence;
            armCiphertextParam.Value = slot.ArmCiphertext;
            allocationIdParam.Value = (object?)slot.AllocationId ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static DbParameter AddParameter(DbCommand command, string name)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        command.Parameters.Add(parameter);
        return parameter;
    }

    private async Task InsertInMemorySlotsUntrackedAsync(
        IReadOnlyCollection<RandomizationListSlot> slots,
        CancellationToken cancellationToken)
    {
        bool previousAutoDetect = Context.ChangeTracker.AutoDetectChangesEnabled;
        Context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            await Context.RandomizationListSlots.AddRangeAsync(slots, cancellationToken).ConfigureAwait(false);
            await Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            foreach (RandomizationListSlot slot in slots)
            {
                Context.Entry(slot).State = EntityState.Detached;
            }
        }
        finally
        {
            Context.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetect;
        }
    }
}
