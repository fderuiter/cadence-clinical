using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Kernel.Serialization;

/// <summary>
/// Source-generated JsonSerializerContext for Native AOT and high-performance serialization of Kernel identifier types.
/// @req:PRD-SYS-053
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(TenantId))]
[JsonSerializable(typeof(TenantId?))]
[JsonSerializable(typeof(List<TenantId>))]
[JsonSerializable(typeof(TenantId[]))]
[JsonSerializable(typeof(OrganizationId))]
[JsonSerializable(typeof(OrganizationId?))]
[JsonSerializable(typeof(List<OrganizationId>))]
[JsonSerializable(typeof(OrganizationId[]))]
[JsonSerializable(typeof(PersonId))]
[JsonSerializable(typeof(PersonId?))]
[JsonSerializable(typeof(List<PersonId>))]
[JsonSerializable(typeof(PersonId[]))]
[JsonSerializable(typeof(StudyId))]
[JsonSerializable(typeof(StudyId?))]
[JsonSerializable(typeof(List<StudyId>))]
[JsonSerializable(typeof(StudyId[]))]
[JsonSerializable(typeof(SiteId))]
[JsonSerializable(typeof(SiteId?))]
[JsonSerializable(typeof(List<SiteId>))]
[JsonSerializable(typeof(SiteId[]))]
[JsonSerializable(typeof(StudySiteId))]
[JsonSerializable(typeof(StudySiteId?))]
[JsonSerializable(typeof(List<StudySiteId>))]
[JsonSerializable(typeof(StudySiteId[]))]
[JsonSerializable(typeof(ParticipantId))]
[JsonSerializable(typeof(ParticipantId?))]
[JsonSerializable(typeof(List<ParticipantId>))]
[JsonSerializable(typeof(ParticipantId[]))]
[JsonSerializable(typeof(ObservationId))]
[JsonSerializable(typeof(ObservationId?))]
[JsonSerializable(typeof(List<ObservationId>))]
[JsonSerializable(typeof(ObservationId[]))]
[JsonSerializable(typeof(AttestationId))]
[JsonSerializable(typeof(AttestationId?))]
[JsonSerializable(typeof(List<AttestationId>))]
[JsonSerializable(typeof(AttestationId[]))]
public partial class KernelJsonSerializerContext : JsonSerializerContext
{
}
