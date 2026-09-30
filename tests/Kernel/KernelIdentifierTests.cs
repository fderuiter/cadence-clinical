using System;
using System.ComponentModel;
using System.Text.Json;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Serialization;
using Cadence.Kernel.Trust;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

public class KernelIdentifierTests
{
    /// <summary>
    /// Validates that strongly-typed UUIDv7 entity identifiers generate valid Version 7 UUIDs with time ordering.
    /// </summary>
    [Fact(DisplayName = "Identifiers_MustGenerate_ValidUuidV7")]
    [Trait("Category", "GxP_OQ")]
    public void IdentifiersMustGenerateValidUuidV7()
    {
        // Arrange & Act
        var baseTime = FakeClock.DefaultStart;
        var tenantId1 = TenantId.FromGuid(Guid.CreateVersion7(baseTime));
        var tenantId2 = TenantId.FromGuid(Guid.CreateVersion7(baseTime.AddSeconds(1)));
        var generatedId = TenantId.New();
        var studyId = StudyId.New();
        var siteId = SiteId.New();
        var participantId = ParticipantId.New();
        var obsId = ObservationId.New();

        // Assert
        tenantId1.Value.Version.Should().Be(7, "TenantId must be a UUIDv7");
        tenantId2.Value.Version.Should().Be(7, "TenantId must be a UUIDv7");
        generatedId.Value.Version.Should().Be(7, "TenantId must be a UUIDv7");
        studyId.Value.Version.Should().Be(7, "StudyId must be a UUIDv7");
        siteId.Value.Version.Should().Be(7, "SiteId must be a UUIDv7");
        participantId.Value.Version.Should().Be(7, "ParticipantId must be a UUIDv7");
        obsId.Value.Version.Should().Be(7, "ObservationId must be a UUIDv7");

        tenantId1.Should().NotBe(tenantId2);
        tenantId1.CompareTo(tenantId2).Should().BeLessThan(0);
        (tenantId1 < tenantId2).Should().BeTrue("TenantId must support < operator based on UUIDv7 timestamp");
        (tenantId1 <= tenantId2).Should().BeTrue("TenantId must support <= operator");
        (tenantId2 > tenantId1).Should().BeTrue("TenantId must support > operator");
        (tenantId2 >= tenantId1).Should().BeTrue("TenantId must support >= operator");
        (tenantId1 >= tenantId2).Should().BeFalse();
        (tenantId1 > tenantId2).Should().BeFalse();

        // Test timestamp-anchored factory method
        var timestampId = ParticipantId.New(baseTime);
        timestampId.Value.Version.Should().Be(7);
        generatedId.IsEmpty.Should().BeFalse();
    }

    /// <summary>
    /// Validates that empty and default states of entity identifier structs behave consistently.
    /// </summary>
    [Fact(DisplayName = "Identifiers_EmptyAndDefaultStates_AreConsistent")]
    [Trait("Category", "GxP_OQ")]
    public void IdentifiersEmptyAndDefaultStatesAreConsistent()
    {
        // Arrange
        var emptyTenant = TenantId.Empty;
        var defaultTenant = default(TenantId);

        // Assert
        emptyTenant.IsEmpty.Should().BeTrue();
        defaultTenant.IsEmpty.Should().BeTrue();
        emptyTenant.Should().Be(defaultTenant);
        emptyTenant.Value.Should().Be(Guid.Empty);
    }

    /// <summary>
    /// Validates string and span parsing for strongly-typed entity identifiers.
    /// </summary>
    [Fact(DisplayName = "Identifiers_ParseAndTryParse_RoundtripCorrectly")]
    [Trait("Category", "GxP_OQ")]
    public void IdentifiersParseAndTryParseRoundtripCorrectly()
    {
        // Arrange
        var original = StudyId.New();
        string stringVal = original.ToString();

        // Act
        var parsedFromString = StudyId.Parse(stringVal);
        var parsedFromSpan = StudyId.Parse(stringVal.AsSpan());
        bool tryParseSuccess = StudyId.TryParse(stringVal, null, out var tryParsed);
        bool tryParseFail = StudyId.TryParse("invalid-guid", null, out var failedId);

        // Act - 2-parameter convenient overloads (standard .NET pattern)
        bool tryParse2Param = StudyId.TryParse(stringVal, out var tryParsed2);
        bool tryParseSpan2Param = StudyId.TryParse(stringVal.AsSpan(), out var tryParsedSpan2);

        // Assert
        parsedFromString.Should().Be(original);
        parsedFromSpan.Should().Be(original);
        tryParseSuccess.Should().BeTrue();
        tryParsed.Should().Be(original);
        tryParseFail.Should().BeFalse();
        failedId.Should().Be(StudyId.Empty);

        tryParse2Param.Should().BeTrue();
        tryParsed2.Should().Be(original);
        tryParseSpan2Param.Should().BeTrue();
        tryParsedSpan2.Should().Be(original);
    }

    /// <summary>
    /// Validates that TypeConverter enables bidirectional conversion for Minimal API routing and parameter binding.
    /// </summary>
    [Fact(DisplayName = "Identifiers_TypeConverter_ConvertsCorrectly")]
    [Trait("Category", "GxP_OQ")]
    public void IdentifiersTypeConverterConvertsCorrectly()
    {
        // Arrange
        var original = SiteId.New();
        var converter = TypeDescriptor.GetConverter(typeof(SiteId));

        // Act
        converter.CanConvertFrom(typeof(string)).Should().BeTrue();
        converter.CanConvertTo(typeof(string)).Should().BeTrue();

        var convertedFromStr = (SiteId)converter.ConvertFromString(original.ToString())!;
        var convertedToStr = (string)converter.ConvertTo(original, typeof(string))!;
        var convertedToGuid = (Guid)converter.ConvertTo(original, typeof(Guid))!;

        // Assert
        convertedFromStr.Should().Be(original);
        convertedToStr.Should().Be(original.ToString());
        convertedToGuid.Should().Be(original.Value);
    }

    /// <summary>
    /// Validates JSON serialization and deserialization across standard and source-generated serializer contexts.
    /// </summary>
    [Fact(DisplayName = "Identifiers_JsonSerialization_Roundtrips")]
    [Trait("Category", "GxP_OQ")]
    public void IdentifiersJsonSerializationRoundtrips()
    {
        // Arrange
        var participantId = ParticipantId.New();

        // Act - Standard System.Text.Json
        string json = JsonSerializer.Serialize(participantId);
        var deserialized = JsonSerializer.Deserialize<ParticipantId>(json);

        // Act - Source generated context
        string aotJson = JsonSerializer.Serialize(participantId, KernelJsonSerializerContext.Default.ParticipantId);
        var aotDeserialized = JsonSerializer.Deserialize(aotJson, KernelJsonSerializerContext.Default.ParticipantId);

        // Act - Nullable and collection source generated context
        StudyId? nullableStudy = StudyId.New();
        string nullableJson = JsonSerializer.Serialize(nullableStudy, KernelJsonSerializerContext.Default.NullableStudyId);
        var deserializedNullable = JsonSerializer.Deserialize(nullableJson, KernelJsonSerializerContext.Default.NullableStudyId);

        StudyId? nullStudy = null;
        string nullJson = JsonSerializer.Serialize(nullStudy, KernelJsonSerializerContext.Default.NullableStudyId);
        var deserializedNull = JsonSerializer.Deserialize(nullJson, KernelJsonSerializerContext.Default.NullableStudyId);

        var idList = new System.Collections.Generic.List<StudyId> { StudyId.New(), StudyId.New() };
        string listJson = JsonSerializer.Serialize(idList, KernelJsonSerializerContext.Default.ListStudyId);
        var deserializedList = JsonSerializer.Deserialize(listJson, KernelJsonSerializerContext.Default.ListStudyId);

        // Assert
        json.Should().Be($"\"{participantId.Value}\"");
        deserialized.Should().Be(participantId);
        aotJson.Should().Be($"\"{participantId.Value}\"");
        aotDeserialized.Should().Be(participantId);

        deserializedNullable.Should().Be(nullableStudy);
        deserializedNull.Should().BeNull();
        deserializedList.Should().BeEquivalentTo(idList);
    }

    /// <summary>
    /// Validates that all canonical Kernel domain entities implement IGxPAuditable with 21 CFR Part 11 audit fields.
    /// </summary>
    [Fact(DisplayName = "KernelDomain_Entities_Implement_IGxPAuditable")]
    [Trait("Category", "GxP_OQ")]
    public void KernelDomainEntitiesImplementIGxPAuditable()
    {
        // Arrange
        var tenantId = TenantId.New();
        var orgId = OrganizationId.New();
        var personId = PersonId.New();
        var studyId = StudyId.New();
        var siteId = SiteId.New();
        var studySiteId = StudySiteId.New();
        var participantId = ParticipantId.New();
        var obsId = ObservationId.New();
        var attestationId = AttestationId.New();

        var now = FakeClock.DefaultStart;
        var period = TemporalPeriod.CreateCurrent(now, now);

        // Act
        var tenant = new Tenant(tenantId, "TEN-01", "Acme Health", "usr_admin", "Tenant provisioned");
        var org = new Organization(orgId, tenantId, "ORG-01", "Acme Pharma", OrganizationKind.Sponsor, "usr_admin", "Sponsor configured");
        var person = new Person(personId, "pi@site.org", "Jane", "Doe", "usr_admin", "Staff registered");
        var study = new Study(studyId, tenantId, orgId, "PROTOCOL-101", "Phase 3 Oncology", "Phase 3", "usr_admin", "Study authored");
        var site = new Site(siteId, tenantId, orgId, "SITE-101", "Memorial Hospital", "US", "usr_admin", "Site qualified");
        var studySite = new StudySite(studySiteId, tenantId, studyId, siteId, "Selected", "usr_admin", "Site selected");
        var participant = new Participant(participantId, tenantId, studyId, studySiteId, "SUBJ-001", "Screened", "usr_cra", "Subject screened");
        var observation = new Observation(obsId, tenantId, studyId, participantId, "VITAL_SIGNS", "120/80", period, "usr_crc", "Blood pressure entered");
        var target = new AttestationTarget(AttestationTarget.ProtocolVersionKind, Guid.CreateVersion7(), ContentHash.Compute("{}"));
        var attestation = Attestation.Sign(
            attestationId, tenantId, Actor.Person("usr_pi"), AttestationMeaning.Approved, target, now, "pwd+otp", now, "Signature completed").Value!;

        // Assert
        tenant.Should().BeAssignableTo<IGxPAuditable>();
        org.Should().BeAssignableTo<IGxPAuditable>();
        person.Should().BeAssignableTo<IGxPAuditable>();
        study.Should().BeAssignableTo<IGxPAuditable>();
        site.Should().BeAssignableTo<IGxPAuditable>();
        studySite.Should().BeAssignableTo<IGxPAuditable>();
        participant.Should().BeAssignableTo<IGxPAuditable>();
        observation.Should().BeAssignableTo<IGxPAuditable>();
        attestation.Should().BeAssignableTo<IGxPAuditable>();

        participant.VersionIndex.Should().Be(1);
        participant.Enroll("usr_pi", "Subject passed inclusion criteria");
        participant.VersionIndex.Should().Be(2);
        participant.Status.Should().Be("Enrolled");
    }

    /// <summary>
    /// Validates bitemporal dual-clock point-in-time reconstruction for clinical observations under ADR-2246.
    /// </summary>
    [Fact(DisplayName = "Bitemporal_TemporalPeriod_DualClockReconstruction")]
    [Trait("Category", "GxP_OQ")]
    public void BitemporalTemporalPeriodDualClockReconstruction()
    {
        // Arrange
        var t0 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 1, 1, 13, 0, 0, TimeSpan.Zero);

        var period = TemporalPeriod.CreateCurrent(t0, t1);

        // Act & Assert
        // Before valid from -> not effective
        period.IsEffectiveAsOf(t0.AddHours(-1), t1).Should().BeFalse();

        // Valid clinical time, but before recorded -> not known yet
        period.IsEffectiveAsOf(t0, t1.AddHours(-1)).Should().BeFalse();

        // At t1, known and valid
        period.IsEffectiveAsOf(t0, t1).Should().BeTrue();

        // Supersede observation at t2
        var superseded = period.Supersede(t2);

        // Before supersede time t2, record was known
        superseded.IsEffectiveAsOf(t0, t1.AddMinutes(30)).Should().BeTrue();

        // As of t3 (post-correction), old record is superseded
        superseded.IsEffectiveAsOf(t0, t3).Should().BeFalse();
    }
}
