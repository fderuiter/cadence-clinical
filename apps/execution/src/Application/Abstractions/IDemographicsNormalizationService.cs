namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Rule 7 demographics derivation/normalization seam (`01_spec.md` §3, Rule 7): gender/sex mapping,
/// the fixed `/365.25`-divisor age approximation, and the read-only `GetSafeDemographics`
/// fallback contract. This is the only interface in the slice whose implementations must
/// intentionally never throw — every method returns a safe default on failure, matching the
/// legacy's explicit "preserve this silent-fallback contract" decision. It exists to support
/// generic reference-range/eligibility matching, never the demographics write path.
/// @req:PRD-SUB-001
/// </summary>
public interface IDemographicsNormalizationService
{
    /// <summary>
    /// Normalizes a raw gender/sex string into a CDISC SEX code ("M"/"F"/"U"). When
    /// <paramref name="preserveCustom"/> is <see langword="true"/>, an unmapped, non-empty input is
    /// returned trimmed and upper-cased instead of collapsing to "U". Never throws.
    /// </summary>
    string NormalizeGender(string? gender, bool preserveCustom = false);

    /// <summary>
    /// Computes the decimal age between <paramref name="birthDate"/> and
    /// <paramref name="observationDate"/> as <c>(observationDate.DayNumber - birthDate.DayNumber) /
    /// 365.25</c> — a fixed average-year-length approximation, not calendar-accurate year math.
    /// Returns <see langword="null"/> when either date is absent. Never throws.
    /// </summary>
    double? CalculateAge(DateOnly? birthDate, DateOnly? observationDate);

    /// <summary>
    /// Safely extracts the normalized gender and current age from <paramref name="subject"/> — a
    /// raw ciphertext <see cref="string"/>, an <see cref="IReadOnlyDictionary{TKey,TValue}"/>
    /// carrying an <c>"encrypted_demographics"</c> key, or any object exposing an
    /// <c>EncryptedDemographics</c> property. Always returns at least <c>{"gender": "U", "age":
    /// null}</c>, silently swallowing decrypt failures and any other exception. Never throws.
    /// </summary>
    SafeDemographics GetSafeDemographics(object? subject, bool preserveCustom = false);
}

/// <summary>
/// The safe, PII-free demographic profile returned by
/// <see cref="IDemographicsNormalizationService.GetSafeDemographics"/>.
/// </summary>
public sealed record SafeDemographics(string Gender, double? Age);
