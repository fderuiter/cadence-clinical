using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.References;

/// <summary>
/// The registered kinds of kernel object a <see cref="NodeRef"/> can name (ADR-2481, spec §3.4). A kind is the
/// lower snake_case name of the object, bound to exactly one strongly-typed identifier. Adding a kernel aggregate
/// registers its kind here; the architecture tests fail for an identifier type without one.
/// </summary>
public static class NodeKinds
{
    public const string Tenant = "tenant";
    public const string Organization = "organization";
    public const string Person = "person";
    public const string Site = "site";
    public const string Study = "study";
    public const string StudySite = "study_site";
    public const string Participant = "participant";
    public const string Enrollment = "enrollment";
    public const string ProtocolVersion = "protocol_version";
    public const string Observation = "observation";
    public const string Assertion = "assertion";
    public const string CollectionInstance = "collection_instance";
    public const string Attestation = "attestation";
    public const string Artifact = "artifact";
    public const string ArtifactVersion = "artifact_version";
    public const string Role = "role";
    public const string Grant = "grant";
    public const string Qualification = "qualification";
    public const string QualificationRequirement = "qualification_requirement";
    public const string Effectivity = "effectivity";
    public const string Applicability = "applicability";
    public const string ConsentStatus = "consent_status";
    public const string TenantMembership = "tenant_membership";
    public const string CodeSystem = "code_system";
    public const string CodeSystemVersion = "code_system_version";
    public const string Concept = "concept";
    public const string ConceptMapping = "concept_mapping";
    public const string StandardVersion = "standard_version";
    public const string LibraryItem = "library_item";
    public const string StudyStandardsProfile = "study_standards_profile";
    public const string WorkQueueDefinition = "work_queue_definition";
    public const string ValidationRun = "validation_run";
    public const string ValidationFinding = "validation_finding";
    public const string ProtocolElement = "protocol_element";
    public const string Proposal = "proposal";
    public const string FormVersionSnapshot = "form_version_snapshot";
    public const string FormQuery = "form_query";
    public const string Subscription = "subscription";
    public const string ProductEntitlement = "product_entitlement";
    public const string TaskConfiguration = "task_configuration";

    private static readonly FrozenDictionary<Type, string> ByIdType = new Dictionary<Type, string>
    {
        [typeof(TenantId)] = Tenant,
        [typeof(OrganizationId)] = Organization,
        [typeof(PersonId)] = Person,
        [typeof(SiteId)] = Site,
        [typeof(StudyId)] = Study,
        [typeof(StudySiteId)] = StudySite,
        [typeof(ParticipantId)] = Participant,
        [typeof(EnrollmentId)] = Enrollment,
        [typeof(ProtocolVersionId)] = ProtocolVersion,
        [typeof(ObservationId)] = Observation,
        [typeof(AssertionId)] = Assertion,
        [typeof(CollectionInstanceId)] = CollectionInstance,
        [typeof(AttestationId)] = Attestation,
        [typeof(ArtifactId)] = Artifact,
        [typeof(ArtifactVersionId)] = ArtifactVersion,
        [typeof(RoleId)] = Role,
        [typeof(GrantId)] = Grant,
        [typeof(QualificationId)] = Qualification,
        [typeof(QualificationRequirementId)] = QualificationRequirement,
        [typeof(EffectivityId)] = Effectivity,
        [typeof(ApplicabilityId)] = Applicability,
        [typeof(ConsentStatusId)] = ConsentStatus,
        [typeof(TenantMembershipId)] = TenantMembership,
        [typeof(CodeSystemId)] = CodeSystem,
        [typeof(CodeSystemVersionId)] = CodeSystemVersion,
        [typeof(ConceptId)] = Concept,
        [typeof(ConceptMappingId)] = ConceptMapping,
        [typeof(StandardVersionId)] = StandardVersion,
        [typeof(LibraryItemId)] = LibraryItem,
        [typeof(StudyStandardsProfileId)] = StudyStandardsProfile,
        [typeof(WorkQueueDefinitionId)] = WorkQueueDefinition,
        [typeof(ValidationRunId)] = ValidationRun,
        [typeof(ValidationFindingId)] = ValidationFinding,
        [typeof(ProtocolElementId)] = ProtocolElement,
        [typeof(ProposalId)] = Proposal,
        [typeof(FormVersionSnapshotId)] = FormVersionSnapshot,
        [typeof(FormQueryId)] = FormQuery,
        [typeof(SubscriptionId)] = Subscription,
        [typeof(ProductEntitlementId)] = ProductEntitlement,
        [typeof(TaskConfigurationId)] = TaskConfiguration,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, Type> ByKind =
        ByIdType.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    private static readonly FrozenSet<string> Kinds = ByKind.Keys.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Every registered kind.</summary>
    public static IReadOnlyCollection<string> All => ByKind.Keys;

    /// <summary>Every identifier type that has a kind.</summary>
    public static IReadOnlyCollection<Type> IdentifierTypes => ByIdType.Keys;

    /// <summary>True when <paramref name="kind"/> is a registered kind.</summary>
    public static bool IsRegistered(string? kind) => kind is not null && ByKind.ContainsKey(kind);

    /// <summary>The kind for identifier type <typeparamref name="TId"/>.</summary>
    public static string Of<TId>()
        where TId : struct, IEntityId<TId> => Of(typeof(TId));

    /// <summary>The kind for an identifier type; throws when the type has no registered kind.</summary>
    public static string Of(Type identifierType)
    {
        ArgumentNullException.ThrowIfNull(identifierType);
        return ByIdType.TryGetValue(identifierType, out var kind)
            ? kind
            : throw new ArgumentException($"{identifierType.Name} has no registered node kind (ADR-2481).", nameof(identifierType));
    }

    /// <summary>The identifier type bound to <paramref name="kind"/>, or null when the kind is not registered.</summary>
    public static Type? IdentifierTypeOf(string kind) => ByKind.GetValueOrDefault(kind);

    /// <summary>
    /// True when <paramref name="kind"/> is well formed: lower snake_case, starting with a letter, at most 48
    /// characters. Registration is checked separately.
    /// </summary>
    public static bool IsWellFormed(ReadOnlySpan<char> kind)
    {
        if (kind.IsEmpty || kind.Length > 48 || !char.IsAsciiLetterLower(kind[0]) || kind[^1] == '_')
        {
            return false;
        }

        for (var i = 1; i < kind.Length; i++)
        {
            var c = kind[i];
            if (c == '_' ? kind[i - 1] == '_' : !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The registered kind string equal to <paramref name="kind"/>, without allocating, or null.</summary>
    internal static string? Registered(ReadOnlySpan<char> kind) =>
        Kinds.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(kind, out var registered) ? registered : null;
}
