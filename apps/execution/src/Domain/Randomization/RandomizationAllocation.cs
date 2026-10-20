using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Randomization;

/// <summary>
/// One participant's randomisation: the scheme version, stratum and position that allocated them, the kit assigned,
/// who randomised them and when (#5607). The arm is held only as <see cref="ArmCiphertext"/>, encrypted under the
/// randomisation key and bound to this allocation's id, so a database reader, an audit trail reader or a blinded
/// caller sees no arm. A participant has at most one allocation (unique index), and allocations are never updated.
/// @req:PRD-SUB-001
/// </summary>
public sealed class RandomizationAllocation : AggregateRoot<Guid>
{
    public Guid SchemeId { get; private set; }
    public int SchemeVersion { get; private set; }
    public StudyCode StudyCode { get; private set; } = default!;
    public SiteCode? SiteCode { get; private set; }
    public SubjectId SubjectId { get; private set; }
    public string StratumKey { get; private set; } = string.Empty;

    /// <summary>The stratification factor values the participant was randomised with, as JSON (they are not blinded).</summary>
    public string StratificationFactorsJson { get; private set; } = "{}";

    /// <summary>Position in the stratum's list (permuted block) or in the study's allocation order (minimisation).</summary>
    public int Sequence { get; private set; }

    /// <summary>The allocated arm, encrypted for purpose <see cref="ArmPurpose"/>.</summary>
    public string ArmCiphertext { get; private set; } = string.Empty;

    /// <summary>The list slot consumed (permuted-block schemes only).</summary>
    public Guid? ListSlotId { get; private set; }

    public string? KitNumber { get; private set; }
    public string AllocatedBy { get; private set; } = string.Empty;
    public DateTimeOffset AllocatedAt { get; private set; }

    private RandomizationAllocation() { } // EF Core

    /// <summary>The stratification factor values the participant was randomised with.</summary>
    public IReadOnlyDictionary<string, string> StratificationFactors() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(StratificationFactorsJson) ?? [];

    /// <summary>The encryption purpose an allocation's arm is bound to, so a ciphertext copied to another row fails to decrypt.</summary>
    public static string ArmPurpose(Guid allocationId) => $"execution.randomization.allocation:{allocationId:D}";

    /// <summary>Records an allocation whose arm the caller has already encrypted for <see cref="ArmPurpose"/> of <paramref name="id"/>.</summary>
    public static RandomizationAllocation Record(
        Guid id,
        RandomizationScheme scheme,
        ClinicalSubject subject,
        string stratumKey,
        IReadOnlyDictionary<string, string> stratificationFactors,
        int sequence,
        string armCiphertext,
        Guid? listSlotId,
        string? kitNumber,
        string allocatedBy,
        DateTimeOffset allocatedAt,
        string reasonForChange)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(stratificationFactors);
        return new RandomizationAllocation
        {
            Id = id,
            SchemeId = scheme.Id,
            SchemeVersion = scheme.Version,
            StudyCode = subject.StudyCode,
            SiteCode = subject.SiteCode,
            SubjectId = subject.Id,
            StratumKey = stratumKey,
            StratificationFactorsJson = JsonSerializer.Serialize(new SortedDictionary<string, string>(
                stratificationFactors.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal)),
            Sequence = sequence,
            ArmCiphertext = armCiphertext,
            ListSlotId = listSlotId,
            KitNumber = kitNumber,
            AllocatedBy = allocatedBy,
            AllocatedAt = allocatedAt,
            CreatedBy = allocatedBy,
            ReasonForChange = reasonForChange,
        };
    }
}

/// <summary>
/// One slot of a permuted-block list, generated when the scheme version is approved (#5607). The arm is encrypted and
/// bound to the slot's scheme, stratum and position, so slots cannot be swapped or reordered without failing to
/// decrypt. Slots are consumed in sequence order and never reused. The list is not itself audited row by row: the
/// approval entry records its size, and each consumption is audited through its <see cref="RandomizationAllocation"/>.
/// @req:PRD-SUB-001
/// </summary>
public sealed class RandomizationListSlot
{
    public Guid Id { get; private set; }
    public Guid SchemeId { get; private set; }
    public string StratumKey { get; private set; } = string.Empty;
    public int Sequence { get; private set; }
    public string ArmCiphertext { get; private set; } = string.Empty;
    public Guid? AllocationId { get; private set; }

    private RandomizationListSlot() { } // EF Core

    /// <summary>The encryption purpose of a slot's arm.</summary>
    public static string ArmPurpose(Guid schemeId, string stratumKey, int sequence) =>
        $"execution.randomization.slot:{schemeId:D}:{stratumKey}:{sequence}";

    public static RandomizationListSlot Create(Guid schemeId, string stratumKey, int sequence, string armCiphertext) =>
        new()
        {
            Id = Guid.NewGuid(),
            SchemeId = schemeId,
            StratumKey = stratumKey,
            Sequence = sequence,
            ArmCiphertext = armCiphertext,
        };

    /// <summary>Marks the slot used by <paramref name="allocationId"/>.</summary>
    public void Consume(Guid allocationId)
    {
        if (AllocationId is not null)
            throw new InvalidOperationException($"List slot {Id} is already consumed.");
        AllocationId = allocationId;
    }
}
