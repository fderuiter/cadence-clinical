using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Randomization;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Randomization;

/// <summary>
/// Randomisation on the existing engines (#5607, ADR-2640). A scheme version is drafted, then approved by a second
/// person, which generates its permuted-block list from a cryptographic source and stores every slot encrypted. A
/// participant is randomised only when enrolled, with a current consent, at an activated site and under an approved
/// scheme; the allocation's arm is stored encrypted, a kit of that arm at the site is assigned when the scheme needs
/// one, and the audit entries name the allocation and kit but never the arm.
/// @req:PRD-SUB-001
/// </summary>
public sealed class RandomizationApplicationService(
    IRandomizationRepository randomization,
    ISubjectRepository subjects,
    IConsentRepository consents,
    ISiteComplianceRepository compliance,
    IAllocationProtector protector,
    IAuditLogWriter auditLog,
    ICurrentAuditContextAccessor auditContext,
    IClock clock) : IRandomizationApplicationService
{
    private const string SiteActivationMilestone = "SITE_ACTIVATION";

    /// <inheritdoc />
    public async Task<Result<RandomizationSchemeResponse>> DraftSchemeAsync(RandomizationSchemeDraftRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RandomizationMethodCodec.TryParse(request.Method, out var method))
        {
            return RandomizationErrors.InvalidScheme("method must be 'permuted_block' or 'minimization'.");
        }

        StudyCode studyCode = request.StudyId;
        string reason = auditContext.ChangeReason;
        var drafted = await randomization.ExecuteInTransactionAsync(async token =>
        {
            await randomization.LockStudyAsync(studyCode, token).ConfigureAwait(false);
            int version = await randomization.GetLatestVersionAsync(studyCode, token).ConfigureAwait(false) + 1;
            var result = RandomizationScheme.Draft(
                studyCode, version, method, request.Arms ?? [],
                request.StratificationFactors?.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value),
                request.BlockSizes, request.SlotsPerStratum ?? 0, request.PreferredArmProbability ?? 0.8, request.RequiresKit,
                auditContext.UserId, reason, clock);
            if (result.IsFailure)
            {
                return result;
            }

            var scheme = result.Value!;
            await randomization.AddSchemeAsync(scheme, token).ConfigureAwait(false);
            await auditLog.WriteAsync(nameof(RandomizationScheme), scheme.Id.ToString(), "DRAFT_RANDOMIZATION_SCHEME",
                auditContext.UserId, reason, oldValues: null, SchemeAuditValues(scheme), token).ConfigureAwait(false);
            await randomization.SaveChangesAsync(token).ConfigureAwait(false);
            return result;
        }, ct).ConfigureAwait(false);

        return drafted.IsSuccess ? RandomizationSchemeResponse.From(drafted.Value!) : drafted.Error!;
    }

    /// <inheritdoc />
    public async Task<Result<RandomizationSchemeResponse>> ApproveSchemeAsync(Guid schemeId, string changeReason, CancellationToken ct)
    {
        var existing = await randomization.GetSchemeAsync(schemeId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return RandomizationErrors.SchemeNotFound();
        }

        var preCheck = existing.CanApprove(auditContext.UserId);
        if (preCheck.IsFailure)
        {
            return preCheck.Error!;
        }

        IReadOnlyList<RandomizationListSlot> slots = existing.Method == RandomizationMethod.PermutedBlock
            ? GenerateList(existing)
            : [];

        var studyCode = existing.StudyCode;
        var approved = await randomization.ExecuteInTransactionAsync<Result<RandomizationScheme>>(async token =>
        {
            await randomization.LockSchemeAsync(schemeId, token).ConfigureAwait(false);
            var scheme = await randomization.GetSchemeAsync(schemeId, token).ConfigureAwait(false);
            if (scheme is null)
            {
                return RandomizationErrors.SchemeNotFound();
            }

            var allowed = scheme.CanApprove(auditContext.UserId);
            if (allowed.IsFailure)
            {
                return allowed.Error!;
            }

            if (slots.Count > 0)
            {
                await randomization.AddListSlotsAsync(slots, token).ConfigureAwait(false);
            }

            await randomization.LockStudyAsync(studyCode, token).ConfigureAwait(false);

            // The previous version is retired and saved first: the database holds at most one approved version per study.
            var previous = await randomization.GetApprovedSchemeAsync(studyCode, token).ConfigureAwait(false);
            if (previous is not null)
            {
                previous.Retire($"Superseded by randomisation scheme version {scheme.Version}: {changeReason}", clock);
                await auditLog.WriteAsync(nameof(RandomizationScheme), previous.Id.ToString(), "RETIRE_RANDOMIZATION_SCHEME",
                    auditContext.UserId, previous.ReasonForChange, oldValues: null, newValues: null, token).ConfigureAwait(false);
                await randomization.SaveChangesAsync(token).ConfigureAwait(false);
            }

            var approval = scheme.Approve(auditContext.UserId, changeReason, clock);
            if (approval.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Scheme {scheme.Id} failed approval after passing pre-checks under lock: {approval.Error?.Code}");
            }

            await auditLog.WriteAsync(nameof(RandomizationScheme), scheme.Id.ToString(), "APPROVE_RANDOMIZATION_SCHEME",
                auditContext.UserId, changeReason, oldValues: null, SchemeAuditValues(scheme), token).ConfigureAwait(false);
            await randomization.SaveChangesAsync(token).ConfigureAwait(false);
            return scheme;
        }, ct).ConfigureAwait(false);

        return approved.IsSuccess ? RandomizationSchemeResponse.From(approved.Value!) : approved.Error!;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RandomizationSchemeResponse>>> ListSchemesAsync(string? studyId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return DomainError.Validation("STUDY_ID_REQUIRED", "study_id is required to list randomisation schemes.");
        }

        StudyCode studyCode;
        try
        {
            studyCode = new StudyCode(studyId);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_STUDY_CODE", ex.Message);
        }

        var schemes = await randomization.ListSchemesAsync(studyCode, ct).ConfigureAwait(false);
        return schemes.Select(RandomizationSchemeResponse.From).ToList();
    }

    /// <inheritdoc />
    public async Task<Result<RandomizationResponse>> RandomizeAsync(
        string subjectIdOrCode, RandomizeSubjectRequest request, string changeReason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var probe = await ResolveSubjectAsync(subjectIdOrCode, ct).ConfigureAwait(false);
        if (probe is null)
        {
            return SubjectErrors.NotFound(subjectIdOrCode);
        }

        // PRD-SSU-003: randomisation at a site that is not activated is refused with an audit entry that names the
        // caller; ICH E6(R3) 2.8: no trial procedure without a current informed consent.
        var gate = await compliance.GetAsync(probe.StudyCode, probe.SiteCode, SiteActivationMilestone, ct).ConfigureAwait(false);
        if (gate is not { IsComplete: true })
        {
            return await RefuseAsync(probe, "BLOCKED_RANDOMIZATION_SITE", changeReason,
                RandomizationErrors.SiteNotActivated(probe.SiteCode?.Value), ct).ConfigureAwait(false);
        }

        if (!await consents.HasCurrentConsentAsync(probe.SubjectCode, probe.StudyCode, ct).ConfigureAwait(false))
        {
            return await RefuseAsync(probe, "BLOCKED_RANDOMIZATION_CONSENT", changeReason,
                RandomizationErrors.ConsentRequired(), ct).ConfigureAwait(false);
        }

        var factors = (request.StratificationFactors ?? [])
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value?.Trim() ?? string.Empty, StringComparer.Ordinal);
        var subjectId = probe.Id;
        var studyCode = probe.StudyCode;

        return await randomization.ExecuteInTransactionAsync<Result<RandomizationResponse>>(async token =>
        {
            await randomization.LockStudyAsync(studyCode, token).ConfigureAwait(false);

            // Loaded inside the unit of work: a retried attempt starts from a cleared change tracker.
            var subject = await subjects.GetByIdAsync(subjectId.ToString(), token).ConfigureAwait(false);
            if (subject is null)
            {
                return SubjectErrors.NotFound(subjectIdOrCode);
            }

            if (subject.RandomizationId is not null
                || await randomization.GetAllocationForSubjectAsync(subject.Id, token).ConfigureAwait(false) is not null)
            {
                return RandomizationErrors.AlreadyRandomized();
            }

            if (subject.Status != SubjectState.Enrolled)
            {
                return RandomizationErrors.NotEnrolled(SubjectStateCodec.ToCode(subject.Status));
            }

            var scheme = await randomization.GetApprovedSchemeAsync(studyCode, token).ConfigureAwait(false);
            if (scheme is null)
            {
                return RandomizationErrors.NoApprovedScheme(studyCode.Value);
            }

            var stratum = scheme.StratumFor(factors);
            if (stratum.IsFailure)
            {
                return stratum.Error!;
            }

            // Decide the arm and the kit before changing anything, so a refusal leaves nothing staged.
            var allocationId = Guid.NewGuid();
            var draw = await DrawAsync(scheme, subject, stratum.Value!, factors, token).ConfigureAwait(false);
            if (draw.IsFailure)
            {
                return draw.Error!;
            }

            var (arm, sequence, slot) = draw.Value;
            DateTimeOffset now = clock.UtcNow;
            IpKitInventoryItem? kit = null;
            if (scheme.RequiresKit)
            {
                kit = await FindKitAsync(subject, arm, now, token).ConfigureAwait(false);
                if (kit is null)
                {
                    return RandomizationErrors.KitUnavailable(subject.SiteCode?.Value);
                }
            }

            var allocation = RandomizationAllocation.Record(
                allocationId, scheme, subject, stratum.Value!, factors, sequence,
                protector.Protect(arm, RandomizationAllocation.ArmPurpose(allocationId)), slot?.Id, kit?.KitNumber,
                auditContext.UserId, now, changeReason);
            slot?.Consume(allocationId);
            kit?.AssignTo(allocationId, subject.SubjectCode.Value, now, changeReason);
            var randomized = subject.Randomize(allocationId, kit?.KitNumber,
                factors.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal));
            if (randomized.IsFailure)
            {
                return randomized.Error!;
            }

            subject.ReasonForChange = changeReason;
            await randomization.AddAllocationAsync(allocation, token).ConfigureAwait(false);

            var auditValues = new Dictionary<string, object?>
            {
                ["randomization_id"] = allocationId,
                ["scheme_version"] = scheme.Version,
                ["stratum"] = stratum.Value,
                ["sequence"] = sequence,
                ["kit_number"] = kit?.KitNumber,
            };
            await auditLog.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "RANDOMIZE_SUBJECT",
                auditContext.UserId, changeReason, oldValues: null, auditValues, token).ConfigureAwait(false);
            if (kit is not null)
            {
                await auditLog.WriteAsync(nameof(IpKitInventoryItem), kit.Id.ToString(), "ASSIGN_KIT",
                    auditContext.UserId, changeReason, oldValues: null,
                    new Dictionary<string, object?> { ["randomization_id"] = allocationId, ["subject_id"] = subject.SubjectCode.Value },
                    token).ConfigureAwait(false);
            }

            await randomization.SaveChangesAsync(token).ConfigureAwait(false);
            return new RandomizationResponse(allocationId, subject.SubjectCode, studyCode, scheme.Version,
                stratum.Value!, kit?.KitNumber, SubjectStateCodec.ToCode(subject.Status), now);
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RevealedAllocation?> RevealAsync(string subjectIdOrCode, CancellationToken ct)
    {
        var subject = await ResolveSubjectAsync(subjectIdOrCode, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return null;
        }

        var allocation = await randomization.GetAllocationForSubjectAsync(subject.Id, ct).ConfigureAwait(false);
        return allocation is null
            ? null
            : new RevealedAllocation(protector.Reveal(allocation.ArmCiphertext, RandomizationAllocation.ArmPurpose(allocation.Id)),
                allocation.KitNumber);
    }

    private async Task<Result<(string Arm, int Sequence, RandomizationListSlot? Slot)>> DrawAsync(
        RandomizationScheme scheme, ClinicalSubject subject, string stratum, IReadOnlyDictionary<string, string> factors,
        CancellationToken ct)
    {
        if (scheme.Method == RandomizationMethod.PermutedBlock)
        {
            var slot = await randomization.GetNextFreeSlotAsync(scheme.Id, stratum, ct).ConfigureAwait(false);
            if (slot is null)
            {
                return RandomizationErrors.ListExhausted(stratum);
            }

            string arm = protector.Reveal(slot.ArmCiphertext, RandomizationListSlot.ArmPurpose(scheme.Id, slot.StratumKey, slot.Sequence));
            return (arm, slot.Sequence, slot);
        }

        // Minimisation balances over every earlier allocation in the study; the random element draws a fresh
        // cryptographic seed, so no stored or default seed lets anyone predict the next arm.
        var arms = scheme.Arms();
        var history = await randomization.ListAllocationsAsync(subject.StudyCode, ct).ConfigureAwait(false);
        var historicalSubjects = history
            .Select(a => new Dictionary<string, object>
            {
                ["assigned_arm"] = protector.Reveal(a.ArmCiphertext, RandomizationAllocation.ArmPurpose(a.Id)),
                ["stratification_factors"] = a.StratificationFactors().ToDictionary(kv => kv.Key, kv => (object)kv.Value),
            })
            .ToList();
        var config = new MinimizationConfig(
            ArmsRatios: arms.ToDictionary(kv => kv.Key, kv => kv.Value),
            StratificationFactors: scheme.StratificationFactors().Keys.ToList(),
            PPreferred: scheme.PreferredArmProbability,
            Seed: RandomNumberGenerator.GetInt32(int.MaxValue));
        var result = MinimizationEngine.Allocate(config, new SubjectAllocationRequest(
            subject.StudyCode.Value, subject.SubjectCode.Value, subject.SiteCode?.Value ?? string.Empty,
            factors.ToDictionary(kv => kv.Key, kv => (object)kv.Value), AllSubjectFactors: historicalSubjects));
        return (result.AssignedArm, history.Count, null);
    }

    private async Task<IpKitInventoryItem?> FindKitAsync(ClinicalSubject subject, string arm, DateTimeOffset at, CancellationToken ct)
    {
        if (subject.SiteCode is not { } siteCode)
        {
            return null;
        }

        var kits = await randomization.ListAssignableKitsAsync(subject.StudyCode, siteCode, at, ct).ConfigureAwait(false);
        return kits.FirstOrDefault(k =>
            k.TreatmentArmCiphertext is { } label
            && string.Equals(protector.Reveal(label, IpKitInventoryItem.ArmPurpose(k.StudyId, k.KitNumber)), arm, StringComparison.Ordinal));
    }

    private async Task<Result<RandomizationResponse>> RefuseAsync(
        ClinicalSubject subject, string action, string changeReason, DomainError error, CancellationToken ct)
    {
        await auditLog.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), action, auditContext.UserId, changeReason,
            oldValues: null, new Dictionary<string, object?> { ["error"] = error.Code }, ct).ConfigureAwait(false);
        await randomization.SaveChangesAsync(ct).ConfigureAwait(false);
        return error;
    }

    private async Task<ClinicalSubject?> ResolveSubjectAsync(string subjectIdOrCode, CancellationToken ct) =>
        await subjects.GetByIdAsync(subjectIdOrCode, ct).ConfigureAwait(false)
            ?? await subjects.GetByCodeAsync(subjectIdOrCode, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<StudyCode?> GetSchemeStudyAsync(Guid schemeId, CancellationToken ct = default)
    {
        var scheme = await randomization.GetSchemeAsync(schemeId, ct).ConfigureAwait(false);
        return scheme?.StudyCode;
    }

    /// <inheritdoc />
    public async Task<(StudyCode StudyId, SiteCode? SiteId)?> GetSubjectScopeAsync(string subjectIdOrCode, CancellationToken ct = default)
    {
        var subject = await ResolveSubjectAsync(subjectIdOrCode, ct).ConfigureAwait(false);
        return subject is null ? null : (subject.StudyCode, subject.SiteCode);
    }

    /// <summary>
    /// The scheme's permuted-block list: <see cref="RandomizationScheme.SlotsPerStratum"/> slots per stratum from
    /// blocks of the scheme's sizes, each block's size and shuffle drawn from <see cref="RandomNumberGenerator"/>, each
    /// slot's arm encrypted for its own position.
    /// </summary>
    private List<RandomizationListSlot> GenerateList(RandomizationScheme scheme)
    {
        var arms = scheme.Arms().ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var sizes = scheme.BlockSizes();
        int maxBlockSize = sizes.Count > 0 ? sizes[^1] : 0;
        var strata = scheme.Strata();
        var batch = new (string Arm, string Purpose)[scheme.ListSize];
        var sequence = new List<string>(scheme.SlotsPerStratum + maxBlockSize);

        int index = 0;
        foreach (string stratum in strata)
        {
            sequence.Clear();
            while (sequence.Count < scheme.SlotsPerStratum)
            {
                int size = sizes[RandomNumberGenerator.GetInt32(sizes.Count)];
                sequence.AddRange(BlockRandomizationEngine.GeneratePermutedBlock(arms, size, RandomNumberGenerator.GetInt32));
            }

            for (int i = 0; i < scheme.SlotsPerStratum; i++)
            {
                batch[index++] = (sequence[i], RandomizationListSlot.ArmPurpose(scheme.Id, stratum, i));
            }
        }

        IReadOnlyList<string> ciphertexts = protector.ProtectBatch(batch);
        var slots = new List<RandomizationListSlot>(scheme.ListSize);
        int ciphertextIndex = 0;
        foreach (string stratum in strata)
        {
            for (int slotSequence = 0; slotSequence < scheme.SlotsPerStratum; slotSequence++)
            {
                slots.Add(RandomizationListSlot.Create(scheme.Id, stratum, slotSequence, ciphertexts[ciphertextIndex++]));
            }
        }

        return slots;
    }

    private static Dictionary<string, object?> SchemeAuditValues(RandomizationScheme scheme) => new()
    {
        ["version"] = scheme.Version,
        ["method"] = RandomizationMethodCodec.ToCode(scheme.Method),
        ["status"] = scheme.Status.ToString().ToUpperInvariant(),
        ["configuration_hash"] = scheme.ConfigurationHash,
        ["list_size"] = scheme.ListSize.ToString(CultureInfo.InvariantCulture),
    };
}
