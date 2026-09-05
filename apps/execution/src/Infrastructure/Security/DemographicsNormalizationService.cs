using System.Globalization;

using Cadence.Apps.Execution.Application.Abstractions;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// Rule 7 demographics normalization (`01_spec.md` §3): gender/sex mapping, the fixed
/// `/365.25`-divisor age approximation, and the never-throws `GetSafeDemographics` fallback
/// contract that replaces the legacy `demographics.py`'s bare `except Exception` swallow
/// (raise sites #19/#22) with a single, documented, intentional non-throwing seam.
/// @req:PRD-SUB-001
/// </summary>
public sealed class DemographicsNormalizationService(
    IDemographicsEncryptionService encryptionService,
    IClock clock) : IDemographicsNormalizationService
{
    public string NormalizeGender(string? gender, bool preserveCustom = false)
    {
        if (string.IsNullOrWhiteSpace(gender))
        {
            return "U";
        }

        string normalized = gender.Trim().ToUpperInvariant();

        return normalized switch
        {
            "M" or "MALE" or "BOY" or "MAN" => "M",
            "F" or "FEMALE" or "GIRL" or "WOMAN" => "F",
            "U" or "UNKNOWN" or "UNK" => "U",
            _ => preserveCustom ? normalized : "U",
        };
    }

    public double? CalculateAge(DateOnly? birthDate, DateOnly? observationDate)
    {
        if (birthDate is null || observationDate is null)
        {
            return null;
        }

        return (observationDate.Value.DayNumber - birthDate.Value.DayNumber) / 365.25;
    }

    public SafeDemographics GetSafeDemographics(object? subject, bool preserveCustom = false)
    {
        string? ciphertext = ExtractCiphertext(subject);
        if (string.IsNullOrEmpty(ciphertext))
        {
            return new SafeDemographics("U", null);
        }

        try
        {
            IReadOnlyDictionary<string, object?> decrypted = encryptionService.Decrypt(ciphertext);

            string? rawGender = ExtractString(decrypted, "gender") ?? ExtractString(decrypted, "sex");
            string gender = NormalizeGender(rawGender, preserveCustom);

            string? rawBirthDate = ExtractString(decrypted, "birthdate")
                ?? ExtractString(decrypted, "date_of_birth")
                ?? ExtractString(decrypted, "dob");
            DateOnly? birthDate = ParseDateString(rawBirthDate);
            DateOnly observationDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

            return new SafeDemographics(gender, CalculateAge(birthDate, observationDate));
        }
        catch (DemographicsDecryptionFailedException)
        {
            return new SafeDemographics("U", null);
        }
    }

    private static string? ExtractCiphertext(object? subject) => subject switch
    {
        null => null,
        string ciphertext => ciphertext,
        IReadOnlyDictionary<string, object?> dict => ExtractString(dict, "encrypted_demographics"),
        _ => subject.GetType().GetProperty("EncryptedDemographics")?.GetValue(subject) as string,
    };

    private static string? ExtractString(IReadOnlyDictionary<string, object?> dict, string key) =>
        dict.TryGetValue(key, out object? value) && !string.IsNullOrEmpty(value as string) ? (string?)value : null;

    private static DateOnly? ParseDateString(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
        {
            return null;
        }

        string trimmed = dateStr.Trim();

        if (trimmed.Contains('T'))
        {
            string normalized = trimmed.Replace("Z", "+00:00");
            if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed))
            {
                return DateOnly.FromDateTime(parsed.DateTime);
            }
        }
        else if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsedDate))
        {
            return parsedDate;
        }

        string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0
            && DateOnly.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly fallback)
            ? fallback
            : null;
    }
}
