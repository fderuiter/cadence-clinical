using System;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// EF Core ValueComparer providing change-tracking and snapshot comparisons for strongly-typed UUIDv7 identifiers.
/// @req:PRD-SYS-053
/// </summary>
public class EntityIdValueComparer<TId>() : ValueComparer<TId>(
    (l, r) => l.Equals(r),
    v => v.GetHashCode(),
    v => v
) where TId : struct, IEntityId<TId>;

public class TenantIdValueConverter() : ValueConverter<TenantId, Guid>(
    id => id.Value,
    guid => new TenantId(guid)
);

public class OrganizationIdValueConverter() : ValueConverter<OrganizationId, Guid>(
    id => id.Value,
    guid => new OrganizationId(guid)
);

public class PersonIdValueConverter() : ValueConverter<PersonId, Guid>(
    id => id.Value,
    guid => new PersonId(guid)
);

public class TenantMembershipIdValueConverter() : ValueConverter<TenantMembershipId, Guid>(
    id => id.Value,
    guid => new TenantMembershipId(guid)
);

public class StudyIdValueConverter() : ValueConverter<StudyId, Guid>(
    id => id.Value,
    guid => new StudyId(guid)
);

public class SiteIdValueConverter() : ValueConverter<SiteId, Guid>(
    id => id.Value,
    guid => new SiteId(guid)
);

public class StudySiteIdValueConverter() : ValueConverter<StudySiteId, Guid>(
    id => id.Value,
    guid => new StudySiteId(guid)
);

public class ParticipantIdValueConverter() : ValueConverter<ParticipantId, Guid>(
    id => id.Value,
    guid => new ParticipantId(guid)
);

public class EnrollmentIdValueConverter() : ValueConverter<EnrollmentId, Guid>(
    id => id.Value,
    guid => new EnrollmentId(guid)
);

public class ArtifactIdValueConverter() : ValueConverter<ArtifactId, Guid>(
    id => id.Value,
    guid => new ArtifactId(guid)
);

public class ArtifactVersionIdValueConverter() : ValueConverter<ArtifactVersionId, Guid>(
    id => id.Value,
    guid => new ArtifactVersionId(guid)
);

public class ObservationIdValueConverter() : ValueConverter<ObservationId, Guid>(
    id => id.Value,
    guid => new ObservationId(guid)
);

public class AttestationIdValueConverter() : ValueConverter<AttestationId, Guid>(
    id => id.Value,
    guid => new AttestationId(guid)
);

public class ProtocolVersionIdValueConverter() : ValueConverter<ProtocolVersionId, Guid>(
    id => id.Value,
    guid => new ProtocolVersionId(guid)
);


public class CodeSystemIdValueConverter() : ValueConverter<CodeSystemId, Guid>(
    id => id.Value,
    guid => new CodeSystemId(guid)
);

public class CodeSystemVersionIdValueConverter() : ValueConverter<CodeSystemVersionId, Guid>(
    id => id.Value,
    guid => new CodeSystemVersionId(guid)
);

public class ConceptIdValueConverter() : ValueConverter<ConceptId, Guid>(
    id => id.Value,
    guid => new ConceptId(guid)
);

public class ConceptMappingIdValueConverter() : ValueConverter<ConceptMappingId, Guid>(
    id => id.Value,
    guid => new ConceptMappingId(guid)
);

public class RoleIdValueConverter() : ValueConverter<RoleId, Guid>(
    id => id.Value,
    guid => new RoleId(guid)
);

public class GrantIdValueConverter() : ValueConverter<GrantId, Guid>(
    id => id.Value,
    guid => new GrantId(guid)
);

public class QualificationIdValueConverter() : ValueConverter<QualificationId, Guid>(
    id => id.Value,
    guid => new QualificationId(guid)
);

public class QualificationRequirementIdValueConverter() : ValueConverter<QualificationRequirementId, Guid>(
    id => id.Value,
    guid => new QualificationRequirementId(guid)
);

public class WorkQueueDefinitionIdValueConverter() : ValueConverter<WorkQueueDefinitionId, Guid>(
    id => id.Value,
    guid => new WorkQueueDefinitionId(guid)
);

public class EffectivityIdValueConverter() : ValueConverter<EffectivityId, Guid>(
    id => id.Value,
    guid => new EffectivityId(guid)
);
