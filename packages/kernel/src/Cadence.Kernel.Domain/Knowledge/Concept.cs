using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Domain.Knowledge;

/// <summary>
/// The kernel's key of clinical meaning (spec §6.1, ADR-2252). A CDISC Biomedical Concept is identified by its CDISC
/// concept code; a sponsor concept lives in the tenant's namespace. A change of label or definition is a new version,
/// and every version stays resolvable in <see cref="Revisions"/>, so a <see cref="ConceptRef"/> never changes meaning.
/// </summary>
public class Concept : AggregateRoot<ConceptId>
{
    private readonly List<ConceptRevision> _revisions = [];

    public TenantId TenantId { get; private set; }
    public ConceptOrigin Origin { get; private set; }

    /// <summary>The CDISC concept code of a Biomedical Concept; null for a sponsor concept.</summary>
    public string? ExternalCode { get; private set; }

    public ConceptKind Kind { get; private set; }
    public ConceptStatus Status { get; private set; }

    /// <summary>The current version; the newest entry in <see cref="Revisions"/>.</summary>
    public int Version { get; private set; }

    /// <summary>The current version's label (also held in its revision), so the audit trail shows label changes.</summary>
    public string PreferredLabel { get; private set; } = string.Empty;

    /// <summary>The current version's definition.</summary>
    public string Definition { get; private set; } = string.Empty;

    public IReadOnlyList<ConceptRevision> Revisions => _revisions;

    public ConceptRef Current => new(Id, Version);

    protected Concept() { }

    /// <summary>Defines a concept at version 1, or returns why it cannot be defined.</summary>
    public static CommandResult<Concept> Define(
        ConceptId id,
        TenantId tenantId,
        ConceptOrigin origin,
        string? externalCode,
        ConceptKind kind,
        string preferredLabel,
        string definition,
        DateTimeOffset recordedAt)
    {
        if (string.IsNullOrWhiteSpace(preferredLabel) || string.IsNullOrWhiteSpace(definition))
        {
            return KnowledgeErrors.ConceptLabelRequired;
        }

        var hasCode = !string.IsNullOrWhiteSpace(externalCode);
        if (hasCode != (origin == ConceptOrigin.CdiscBiomedicalConcept))
        {
            return KnowledgeErrors.ExternalCodeMismatch;
        }

        var concept = new Concept
        {
            Id = id,
            TenantId = tenantId,
            Origin = origin,
            ExternalCode = hasCode ? externalCode!.Trim() : null,
            Kind = kind,
            Status = ConceptStatus.Active,
        };
        concept.AddRevision(preferredLabel, definition, recordedAt);
        return CommandResult<Concept>.Success(concept);
    }

    /// <summary>Records a new version with a changed label or definition.</summary>
    public KernelError? Revise(string preferredLabel, string definition, DateTimeOffset recordedAt)
    {
        if (Status == ConceptStatus.Deprecated)
        {
            return KnowledgeErrors.ConceptDeprecated;
        }

        if (string.IsNullOrWhiteSpace(preferredLabel) || string.IsNullOrWhiteSpace(definition))
        {
            return KnowledgeErrors.ConceptLabelRequired;
        }

        if (string.Equals(preferredLabel.Trim(), PreferredLabel, StringComparison.Ordinal)
            && string.Equals(definition.Trim(), Definition, StringComparison.Ordinal))
        {
            return KnowledgeErrors.ConceptUnchanged;
        }

        AddRevision(preferredLabel, definition, recordedAt);
        return null;
    }

    /// <summary>Deprecates the concept. Its versions stay resolvable; new references and mappings are refused.</summary>
    public KernelError? Deprecate()
    {
        if (Status == ConceptStatus.Deprecated)
        {
            return KnowledgeErrors.ConceptDeprecated;
        }

        Status = ConceptStatus.Deprecated;
        return null;
    }

    private void AddRevision(string preferredLabel, string definition, DateTimeOffset recordedAt)
    {
        Version = _revisions.Count == 0 ? 1 : _revisions.Max(r => r.Version) + 1;
        PreferredLabel = preferredLabel.Trim();
        Definition = definition.Trim();
        _revisions.Add(new ConceptRevision(TenantId, Id, Version, PreferredLabel, Definition, recordedAt));
    }
}

/// <summary>One version of a <see cref="Concept"/>: what a <see cref="ConceptRef"/> to it means. Insert-only.</summary>
public class ConceptRevision
{
    public TenantId TenantId { get; private set; }
    public ConceptId ConceptId { get; private set; }
    public int Version { get; private set; }
    public string PreferredLabel { get; private set; } = string.Empty;
    public string Definition { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; private set; }

    protected ConceptRevision() { }

    internal ConceptRevision(TenantId tenantId, ConceptId conceptId, int version, string preferredLabel, string definition, DateTimeOffset recordedAt)
    {
        TenantId = tenantId;
        ConceptId = conceptId;
        Version = version;
        PreferredLabel = preferredLabel;
        Definition = definition;
        RecordedAt = recordedAt;
    }
}

/// <summary>
/// A mapping from one concept version to a code in a pinned code-system version, with its relation, review status and
/// evidence (spec §6.1). Only confirmed mappings resolve an incoming code to a concept.
/// </summary>
public class ConceptMapping : AggregateRoot<ConceptMappingId>
{
    public TenantId TenantId { get; private set; }
    public ConceptId ConceptId { get; private set; }
    public int ConceptVersion { get; private set; }
    public CodeSystemVersionId CodeSystemVersionId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public MappingRelation Relation { get; private set; }
    public MappingStatus Status { get; private set; }
    public string Evidence { get; private set; } = string.Empty;

    public ConceptRef Concept => new(ConceptId, ConceptVersion);

    public Coding Target => new(CodeSystemVersionId, Code);

    protected ConceptMapping() { }

    /// <summary>Creates a mapping, proposed or already confirmed.</summary>
    public static CommandResult<ConceptMapping> Create(
        ConceptMappingId id, TenantId tenantId, ConceptRef concept, Coding target, MappingRelation relation, string evidence, bool confirmed)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return KnowledgeErrors.EvidenceRequired;
        }

        return CommandResult<ConceptMapping>.Success(new ConceptMapping
        {
            Id = id,
            TenantId = tenantId,
            ConceptId = concept.ConceptId,
            ConceptVersion = concept.Version,
            CodeSystemVersionId = target.CodeSystemVersionId,
            Code = target.Code.Trim(),
            Relation = relation,
            Status = confirmed ? MappingStatus.Confirmed : MappingStatus.Proposed,
            Evidence = evidence.Trim(),
        });
    }

    /// <summary>
    /// Proposed → Confirmed or Rejected; Confirmed → Rejected. A rejected mapping is final: map again to replace it.
    /// </summary>
    public KernelError? Review(MappingStatus status)
    {
        var allowed = (Status, status) switch
        {
            (MappingStatus.Proposed, MappingStatus.Confirmed) => true,
            (MappingStatus.Proposed, MappingStatus.Rejected) => true,
            (MappingStatus.Confirmed, MappingStatus.Rejected) => true,
            _ => false,
        };
        if (!allowed)
        {
            return KnowledgeErrors.MappingTransitionInvalid.WithMessage($"A {Status} mapping cannot become {status}.");
        }

        Status = status;
        return null;
    }
}
