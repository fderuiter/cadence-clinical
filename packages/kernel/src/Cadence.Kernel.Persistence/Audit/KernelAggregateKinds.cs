using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using Cadence.Kernel.References;

namespace Cadence.Kernel.Persistence.Audit;

/// <summary>
/// Maps kernel and module <c>AggregateType</c> names stored in <c>kernel.audit_entries</c> and
/// <c>kernel.provenance_activities</c> to canonical <see cref="NodeKinds"/> and back without new write projections.
/// </summary>
internal static class KernelAggregateKinds
{
    private static readonly FrozenDictionary<string, string> KindByAggregateName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Tenant"] = NodeKinds.Tenant,
        ["Organization"] = NodeKinds.Organization,
        ["Person"] = NodeKinds.Person,
        ["Site"] = NodeKinds.Site,
        ["Study"] = NodeKinds.Study,
        ["StudySite"] = NodeKinds.StudySite,
        ["Participant"] = NodeKinds.Participant,
        ["Enrollment"] = NodeKinds.Enrollment,
        ["ProtocolVersion"] = NodeKinds.ProtocolVersion,
        ["ProtocolElement"] = NodeKinds.ProtocolElement,
        ["Observation"] = NodeKinds.Observation,
        ["Assertion"] = NodeKinds.Assertion,
        ["CollectionInstance"] = NodeKinds.CollectionInstance,
        ["EcrfFormInstance"] = NodeKinds.CollectionInstance,
        ["Attestation"] = NodeKinds.Attestation,
        ["Artifact"] = NodeKinds.Artifact,
        ["ArtifactVersion"] = NodeKinds.ArtifactVersion,
        ["Role"] = NodeKinds.Role,
        ["RoleDefinition"] = NodeKinds.Role,
        ["Grant"] = NodeKinds.Grant,
        ["Qualification"] = NodeKinds.Qualification,
        ["QualificationRequirement"] = NodeKinds.QualificationRequirement,
        ["Effectivity"] = NodeKinds.Effectivity,
        ["TenantMembership"] = NodeKinds.TenantMembership,
        ["CodeSystem"] = NodeKinds.CodeSystem,
        ["CodeSystemVersion"] = NodeKinds.CodeSystemVersion,
        ["Concept"] = NodeKinds.Concept,
        ["ConceptMapping"] = NodeKinds.ConceptMapping,
        ["StandardVersion"] = NodeKinds.StandardVersion,
        ["LibraryItem"] = NodeKinds.LibraryItem,
        ["StudyStandardsProfile"] = NodeKinds.StudyStandardsProfile,
        ["WorkQueueDefinition"] = NodeKinds.WorkQueueDefinition,
        ["ValidationRun"] = NodeKinds.ValidationRun,
        ["ValidationFinding"] = NodeKinds.ValidationFinding,
        ["Proposal"] = NodeKinds.Proposal,
        ["FormVersionSnapshot"] = NodeKinds.FormVersionSnapshot,
        ["FormQuery"] = NodeKinds.FormQuery,
        ["Subscription"] = NodeKinds.Subscription,
        ["ProductEntitlement"] = NodeKinds.ProductEntitlement,
        ["TaskConfiguration"] = NodeKinds.TaskConfiguration,
        ["AuditEntry"] = NodeKinds.AuditEntry,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string[]> AggregateTypesByKind = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [NodeKinds.Tenant] = ["Tenant"],
        [NodeKinds.Organization] = ["Organization"],
        [NodeKinds.Person] = ["Person"],
        [NodeKinds.Site] = ["Site"],
        [NodeKinds.Study] = ["Study"],
        [NodeKinds.StudySite] = ["StudySite"],
        [NodeKinds.Participant] = ["Participant"],
        [NodeKinds.Enrollment] = ["Enrollment"],
        [NodeKinds.ProtocolVersion] = ["ProtocolVersion"],
        [NodeKinds.ProtocolElement] = ["ProtocolElement"],
        [NodeKinds.Observation] = ["Observation"],
        [NodeKinds.Assertion] = ["Assertion"],
        [NodeKinds.CollectionInstance] = ["CollectionInstance", "execution.CollectionInstance", "execution.EcrfFormInstance"],
        [NodeKinds.Attestation] = ["Attestation"],
        [NodeKinds.Artifact] = ["Artifact"],
        [NodeKinds.ArtifactVersion] = ["ArtifactVersion"],
        [NodeKinds.Role] = ["Role", "RoleDefinition"],
        [NodeKinds.Grant] = ["Grant"],
        [NodeKinds.Qualification] = ["Qualification"],
        [NodeKinds.QualificationRequirement] = ["QualificationRequirement"],
        [NodeKinds.Effectivity] = ["Effectivity"],
        [NodeKinds.TenantMembership] = ["TenantMembership"],
        [NodeKinds.CodeSystem] = ["CodeSystem"],
        [NodeKinds.CodeSystemVersion] = ["CodeSystemVersion"],
        [NodeKinds.Concept] = ["Concept"],
        [NodeKinds.ConceptMapping] = ["ConceptMapping"],
        [NodeKinds.StandardVersion] = ["StandardVersion"],
        [NodeKinds.LibraryItem] = ["LibraryItem"],
        [NodeKinds.StudyStandardsProfile] = ["StudyStandardsProfile"],
        [NodeKinds.WorkQueueDefinition] = ["WorkQueueDefinition"],
        [NodeKinds.ValidationRun] = ["ValidationRun"],
        [NodeKinds.ValidationFinding] = ["ValidationFinding"],
        [NodeKinds.Proposal] = ["Proposal", "intelligence.Proposal"],
        [NodeKinds.FormVersionSnapshot] = ["FormVersionSnapshot", "execution.FormVersionSnapshot"],
        [NodeKinds.FormQuery] = ["FormQuery", "execution.FormQuery"],
        [NodeKinds.Subscription] = ["Subscription"],
        [NodeKinds.ProductEntitlement] = ["ProductEntitlement"],
        [NodeKinds.TaskConfiguration] = ["TaskConfiguration"],
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> HumanLabelsByKind = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [NodeKinds.Tenant] = "tenant",
        [NodeKinds.Organization] = "organization",
        [NodeKinds.Person] = "person",
        [NodeKinds.Site] = "site",
        [NodeKinds.Study] = "study",
        [NodeKinds.StudySite] = "study site",
        [NodeKinds.Participant] = "participant",
        [NodeKinds.Enrollment] = "enrollment",
        [NodeKinds.ProtocolVersion] = "protocol version",
        [NodeKinds.ProtocolElement] = "protocol element",
        [NodeKinds.Observation] = "observation",
        [NodeKinds.Assertion] = "assertion",
        [NodeKinds.CollectionInstance] = "form",
        [NodeKinds.Attestation] = "attestation",
        [NodeKinds.Artifact] = "document",
        [NodeKinds.ArtifactVersion] = "document version",
        [NodeKinds.Role] = "role",
        [NodeKinds.Grant] = "grant",
        [NodeKinds.Qualification] = "qualification",
        [NodeKinds.QualificationRequirement] = "qualification requirement",
        [NodeKinds.Effectivity] = "effectivity",
        [NodeKinds.TenantMembership] = "tenant membership",
        [NodeKinds.CodeSystem] = "code system",
        [NodeKinds.CodeSystemVersion] = "code system version",
        [NodeKinds.Concept] = "concept",
        [NodeKinds.ConceptMapping] = "concept mapping",
        [NodeKinds.StandardVersion] = "standard version",
        [NodeKinds.LibraryItem] = "library item",
        [NodeKinds.StudyStandardsProfile] = "study standards",
        [NodeKinds.WorkQueueDefinition] = "work queue",
        [NodeKinds.ValidationRun] = "validation run",
        [NodeKinds.ValidationFinding] = "validation finding",
        [NodeKinds.Proposal] = "proposal",
        [NodeKinds.FormVersionSnapshot] = "form version",
        [NodeKinds.FormQuery] = "query",
        [NodeKinds.Subscription] = "subscription",
        [NodeKinds.ProductEntitlement] = "product entitlement",
        [NodeKinds.TaskConfiguration] = "task configuration",
        [NodeKinds.AuditEntry] = "audit event",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string[] AggregateTypesForKind(string kind) =>
        AggregateTypesByKind.TryGetValue(kind, out var types) ? types : [];

    public static string? TryMapAggregateTypeToKind(string? aggregateType)
    {
        if (string.IsNullOrWhiteSpace(aggregateType))
        {
            return null;
        }

        var trimmed = aggregateType.Trim();
        if (NodeKinds.IsRegistered(trimmed))
        {
            return trimmed;
        }

        var dotIndex = trimmed.LastIndexOf('.');
        var shortName = dotIndex >= 0 && dotIndex < trimmed.Length - 1 ? trimmed[(dotIndex + 1)..] : trimmed;
        return KindByAggregateName.GetValueOrDefault(shortName);
    }

    public static bool TryResolveTarget(string? aggregateType, string? aggregateId, out NodeRef target)
    {
        if (TryMapAggregateTypeToKind(aggregateType) is { } kind
            && Guid.TryParse(aggregateId, out var guid)
            && guid != Guid.Empty)
        {
            target = new NodeRef(kind, guid);
            return true;
        }

        target = null!;
        return false;
    }

    public static string HumanizeKind(string? kind, string? fallbackAggregateType = null)
    {
        if (kind is not null && HumanLabelsByKind.TryGetValue(kind, out var label))
        {
            return label;
        }

        if (TryMapAggregateTypeToKind(fallbackAggregateType) is { } mapped && HumanLabelsByKind.TryGetValue(mapped, out var mappedLabel))
        {
            return mappedLabel;
        }

        return string.IsNullOrWhiteSpace(fallbackAggregateType) ? "record" : fallbackAggregateType;
    }
}
