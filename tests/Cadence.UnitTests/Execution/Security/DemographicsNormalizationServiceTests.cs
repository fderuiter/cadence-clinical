using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Security;

/// <summary>
/// Coverage for `MIG-SUBJ-016`'s <see cref="DemographicsNormalizationService"/>: the Rule 7
/// gender-mapping table, the fixed `/365.25`-divisor age approximation (including both
/// date-string-parsing fallback paths), and the `GetSafeDemographics` never-throws fallback
/// contract across all three legacy input shapes.
/// </summary>
public class DemographicsNormalizationServiceTests
{
    private sealed class SubjectWithEncryptedDemographics(string? encryptedDemographics)
    {
        public string? EncryptedDemographics { get; } = encryptedDemographics;
    }

    private static DemographicsNormalizationService CreateService(
        IDemographicsEncryptionService? encryptionService = null, DateTimeOffset? utcNow = null) =>
        new(
            encryptionService ?? FakeDemographicsEncryptionService.ThrowingOnDecrypt(),
            new FakeClock(utcNow ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)));

    public static TheoryData<string, string> GenderMappingTable => new()
    {
        { "M", "M" }, { "MALE", "M" }, { "BOY", "M" }, { "MAN", "M" },
        { "m", "M" }, { "  male  ", "M" },
        { "F", "F" }, { "FEMALE", "F" }, { "GIRL", "F" }, { "WOMAN", "F" },
        { "f", "F" }, { " Woman ", "F" },
        { "U", "U" }, { "UNKNOWN", "U" }, { "UNK", "U" },
        { "", "U" }, { "   ", "U" },
    };

    [Theory]
    [MemberData(nameof(GenderMappingTable))]
    public void NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(string input, string expected)
    {
        var service = CreateService();

        service.NormalizeGender(input, preserveCustom: false).Should().Be(expected);
        service.NormalizeGender(input, preserveCustom: true).Should().Be(expected);
    }

    [Fact]
    public void NormalizeGender_GivenNullInput_ReturnsU()
    {
        var service = CreateService();

        service.NormalizeGender(null).Should().Be("U");
    }

    [Fact]
    public void NormalizeGender_GivenUnmappedInputWithPreserveCustomFalse_ReturnsU()
    {
        var service = CreateService();

        service.NormalizeGender("NB", preserveCustom: false).Should().Be("U");
    }

    [Fact]
    public void NormalizeGender_GivenUnmappedInputWithPreserveCustomTrue_ReturnsTrimmedUppercasedVerbatim()
    {
        var service = CreateService();

        service.NormalizeGender("NB", preserveCustom: true).Should().Be("NB");
        service.NormalizeGender("  nb  ", preserveCustom: true).Should().Be("NB");
    }

    [Fact]
    public void CalculateAge_GivenTwentyYearSpan_ReturnsExactComputedValueNotApproximation()
    {
        var service = CreateService();
        var birthDate = new DateOnly(2000, 1, 1);
        var observationDate = new DateOnly(2020, 1, 1);

        double? age = service.CalculateAge(birthDate, observationDate);

        age.Should().Be(20.0);
    }

    [Fact]
    public void CalculateAge_GivenOneDaySpan_ReturnsExactFixedDivisorValue()
    {
        var service = CreateService();
        var birthDate = new DateOnly(2020, 1, 1);
        var observationDate = new DateOnly(2020, 1, 2);

        double? age = service.CalculateAge(birthDate, observationDate);

        age.Should().Be(1 / 365.25);
    }

    [Fact]
    public void CalculateAge_GivenNullBirthDate_ReturnsNull()
    {
        var service = CreateService();

        service.CalculateAge(null, new DateOnly(2020, 1, 1)).Should().BeNull();
    }

    [Fact]
    public void CalculateAge_GivenNullObservationDate_ReturnsNull()
    {
        var service = CreateService();

        service.CalculateAge(new DateOnly(2000, 1, 1), null).Should().BeNull();
    }

    [Fact]
    public void GetSafeDemographics_GivenNullSubject_ReturnsDefaultShape()
    {
        var service = CreateService();

        SafeDemographics result = service.GetSafeDemographics(null);

        result.Should().Be(new SafeDemographics("U", null));
    }

    [Fact]
    public void GetSafeDemographics_GivenEmptyCiphertextString_ReturnsDefaultShape()
    {
        var service = CreateService();

        service.GetSafeDemographics(string.Empty).Should().Be(new SafeDemographics("U", null));
    }

    [Fact]
    public void GetSafeDemographics_GivenDecryptFailure_ReturnsDefaultShapeAndDoesNotThrow()
    {
        var service = CreateService(FakeDemographicsEncryptionService.ThrowingOnDecrypt());

        var act = () => service.GetSafeDemographics("some-ciphertext");

        act.Should().NotThrow();
        service.GetSafeDemographics("some-ciphertext").Should().Be(new SafeDemographics("U", null));
    }

    [Fact]
    public void GetSafeDemographics_GivenRawCiphertextStringShape_DecryptsAndDerivesGenderAndAge()
    {
        var payload = new Dictionary<string, object?> { ["gender"] = "FEMALE", ["birthdate"] = "2000-01-01" };
        var service = CreateService(
            FakeDemographicsEncryptionService.ReturningPayload(payload),
            utcNow: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        SafeDemographics result = service.GetSafeDemographics("ciphertext");

        result.Gender.Should().Be("F");
        result.Age.Should().Be(20.0);
    }

    [Fact]
    public void GetSafeDemographics_GivenDictionaryShapeWithEncryptedDemographicsKey_DecryptsAndDerivesResult()
    {
        var payload = new Dictionary<string, object?> { ["gender"] = "M", ["birthdate"] = "1990-05-15" };
        var service = CreateService(FakeDemographicsEncryptionService.ReturningPayload(payload));
        var subject = new Dictionary<string, object?> { ["encrypted_demographics"] = "ciphertext" };

        SafeDemographics result = service.GetSafeDemographics(subject);

        result.Gender.Should().Be("M");
        result.Age.Should().NotBeNull();
    }

    [Fact]
    public void GetSafeDemographics_GivenObjectShapeWithEncryptedDemographicsProperty_DecryptsAndDerivesResult()
    {
        var payload = new Dictionary<string, object?> { ["sex"] = "F" };
        var service = CreateService(FakeDemographicsEncryptionService.ReturningPayload(payload));
        var subject = new SubjectWithEncryptedDemographics("ciphertext");

        SafeDemographics result = service.GetSafeDemographics(subject);

        result.Gender.Should().Be("F");
        result.Age.Should().BeNull();
    }

    [Fact]
    public void GetSafeDemographics_GivenObjectShapeWithNullEncryptedDemographicsProperty_ReturnsDefaultShape()
    {
        var service = CreateService();
        var subject = new SubjectWithEncryptedDemographics(null);

        service.GetSafeDemographics(subject).Should().Be(new SafeDemographics("U", null));
    }

    [Fact]
    public void GetSafeDemographics_GivenGenderKeyAbsent_FallsBackToSexKey()
    {
        var payload = new Dictionary<string, object?> { ["sex"] = "MALE" };
        var service = CreateService(FakeDemographicsEncryptionService.ReturningPayload(payload));

        service.GetSafeDemographics("ciphertext").Gender.Should().Be("M");
    }

    [Fact]
    public void GetSafeDemographics_GivenPreserveCustomTrue_ForwardsFlagToNormalizeGender()
    {
        var payload = new Dictionary<string, object?> { ["gender"] = "NB" };
        var service = CreateService(FakeDemographicsEncryptionService.ReturningPayload(payload));

        service.GetSafeDemographics("ciphertext", preserveCustom: true).Gender.Should().Be("NB");
        service.GetSafeDemographics("ciphertext", preserveCustom: false).Gender.Should().Be("U");
    }

    [Fact]
    public void GetSafeDemographics_GivenNoBirthDateKeyPresent_ReturnsNullAge()
    {
        var payload = new Dictionary<string, object?> { ["gender"] = "M" };
        var service = CreateService(FakeDemographicsEncryptionService.ReturningPayload(payload));

        service.GetSafeDemographics("ciphertext").Age.Should().BeNull();
    }

    [Fact]
    public void GetSafeDemographics_GivenIso8601BirthDateWithTAndZOffset_ParsesDateComponentCorrectly()
    {
        var payload = new Dictionary<string, object?> { ["birthdate"] = "2000-06-15T08:30:00Z" };
        var service = CreateService(
            FakeDemographicsEncryptionService.ReturningPayload(payload),
            utcNow: new DateTimeOffset(2000, 6, 16, 0, 0, 0, TimeSpan.Zero));

        SafeDemographics result = service.GetSafeDemographics("ciphertext");

        result.Age.Should().Be(1 / 365.25);
    }

    [Fact]
    public void GetSafeDemographics_GivenDateWithTrailingGarbageAfterWhitespace_FallsBackToLeadingDateToken()
    {
        var payload = new Dictionary<string, object?> { ["date_of_birth"] = "1990-05-15 not-a-real-time" };
        var service = CreateService(
            FakeDemographicsEncryptionService.ReturningPayload(payload),
            utcNow: new DateTimeOffset(1991, 5, 15, 0, 0, 0, TimeSpan.Zero));

        SafeDemographics result = service.GetSafeDemographics("ciphertext");

        result.Age.Should().Be(365 / 365.25);
    }

    [Fact]
    public void GetSafeDemographics_GivenBirthDateFailingBothParseAttempts_ReturnsNullAgeWithoutThrowing()
    {
        var payload = new Dictionary<string, object?> { ["dob"] = "not-a-date-at-all" };
        var service = CreateService(FakeDemographicsEncryptionService.ReturningPayload(payload));

        var act = () => service.GetSafeDemographics("ciphertext");

        act.Should().NotThrow();
        service.GetSafeDemographics("ciphertext").Age.Should().BeNull();
    }
}
