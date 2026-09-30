using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Standards;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// EF Core configuration extensions registering strongly-typed UUIDv7 converters across clinical kernel entity models.
/// @req:PRD-SYS-053
/// </summary>
public static class KernelModelConfigurationBuilderExtensions
{
    public static ModelConfigurationBuilder ConfigureKernelEntityIds(this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<TenantId>().HaveConversion<TenantIdValueConverter, EntityIdValueComparer<TenantId>>();
        configurationBuilder.Properties<OrganizationId>().HaveConversion<OrganizationIdValueConverter, EntityIdValueComparer<OrganizationId>>();
        configurationBuilder.Properties<PersonId>().HaveConversion<PersonIdValueConverter, EntityIdValueComparer<PersonId>>();
        configurationBuilder.Properties<TenantMembershipId>().HaveConversion<TenantMembershipIdValueConverter, EntityIdValueComparer<TenantMembershipId>>();
        configurationBuilder.Properties<StudyId>().HaveConversion<StudyIdValueConverter, EntityIdValueComparer<StudyId>>();
        configurationBuilder.Properties<SiteId>().HaveConversion<SiteIdValueConverter, EntityIdValueComparer<SiteId>>();
        configurationBuilder.Properties<StudySiteId>().HaveConversion<StudySiteIdValueConverter, EntityIdValueComparer<StudySiteId>>();
        configurationBuilder.Properties<ParticipantId>().HaveConversion<ParticipantIdValueConverter, EntityIdValueComparer<ParticipantId>>();
        configurationBuilder.Properties<ObservationId>().HaveConversion<ObservationIdValueConverter, EntityIdValueComparer<ObservationId>>();
        configurationBuilder.Properties<AttestationId>().HaveConversion<AttestationIdValueConverter, EntityIdValueComparer<AttestationId>>();
        configurationBuilder.Properties<ProtocolVersionId>().HaveConversion<ProtocolVersionIdValueConverter, EntityIdValueComparer<ProtocolVersionId>>();
        configurationBuilder.Properties<CodeSystemId>().HaveConversion<CodeSystemIdValueConverter, EntityIdValueComparer<CodeSystemId>>();
        configurationBuilder.Properties<CodeSystemVersionId>().HaveConversion<CodeSystemVersionIdValueConverter, EntityIdValueComparer<CodeSystemVersionId>>();
        configurationBuilder.Properties<ConceptId>().HaveConversion<ConceptIdValueConverter, EntityIdValueComparer<ConceptId>>();
        configurationBuilder.Properties<ConceptMappingId>().HaveConversion<ConceptMappingIdValueConverter, EntityIdValueComparer<ConceptMappingId>>();
        configurationBuilder.Properties<StandardVersionId>().HaveConversion<StandardVersionIdValueConverter, EntityIdValueComparer<StandardVersionId>>();
        configurationBuilder.Properties<LibraryItemId>().HaveConversion<LibraryItemIdValueConverter, EntityIdValueComparer<LibraryItemId>>();
        configurationBuilder.Properties<StudyStandardsProfileId>().HaveConversion<StudyStandardsProfileIdValueConverter, EntityIdValueComparer<StudyStandardsProfileId>>();

        return configurationBuilder;
    }
}
