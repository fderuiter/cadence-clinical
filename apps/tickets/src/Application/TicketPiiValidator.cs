using System;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Deid;

namespace Cadence.Apps.Tickets.Application;

/// <summary>
/// Rejects direct participant PII in support ticket fields while permitting pseudonymous clinical subject codes
/// (for example <c>SUBJ-1001</c> or <c>SCR-0042</c>).
/// @req:PRD-TCK-001
/// </summary>
public static partial class TicketPiiValidator
{
    private static readonly DeidDetector Detector = new();

    // Pseudonymous subject codes require an alphabetic prefix + separator + alphanumeric identifier (no whitespace or @).
    [GeneratedRegex(@"^[A-Za-z]{2,12}[-_][A-Za-z0-9][A-Za-z0-9_-]{0,24}$")]
    private static partial Regex PseudonymousSubjectCodeRegex();

    [GeneratedRegex(@"\b(?:DOB|Date\s+of\s+Birth|Birth\s*Date|Born(?:\s+on)?)\s*[:=#-]?\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitBirthDateRegex();

    [GeneratedRegex(@"\b(?:Patient|Participant|Subject)\s+(?:Full\s+)?Name\s*[:=#-]\s*[A-Za-z]{2,}|\b(?:Mr|Mrs|Ms|Miss)\.?\s+[A-Z][a-z]+\s+[A-Z][a-z]+")]
    private static partial Regex ExplicitParticipantNameRegex();

    [GeneratedRegex(@"\d")]
    private static partial Regex DigitRegex();

    public static string? ValidateNoDirectParticipantPii(
        string? title,
        string? description,
        string? subjectCode,
        string? entityType,
        string? entityId,
        string? reasonForChange)
    {
        if (!string.IsNullOrWhiteSpace(subjectCode))
        {
            string trimmedSubject = subjectCode.Trim();
            if (!PseudonymousSubjectCodeRegex().IsMatch(trimmedSubject))
            {
                return "Subject identifier must be a pseudonymous clinical code (for example 'SUBJ-1001'); direct participant names or raw identifiers are not permitted.";
            }
        }

        if (string.Equals(entityType?.Trim(), "SUBJECT", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(entityId)
            && !PseudonymousSubjectCodeRegex().IsMatch(entityId.Trim()))
        {
            return "Linked SUBJECT entity ID must be a pseudonymous clinical code (for example 'SUBJ-1001'); direct participant identifiers are not permitted.";
        }

        string? titleViolation = ScanFreeText("title", title);
        if (titleViolation is not null)
        {
            return titleViolation;
        }

        string? descriptionViolation = ScanFreeText("description", description);
        if (descriptionViolation is not null)
        {
            return descriptionViolation;
        }

        string? reasonViolation = ScanFreeText("reason_for_change", reasonForChange);
        if (reasonViolation is not null)
        {
            return reasonViolation;
        }

        return null;
    }

    public static string? ValidateCommentNoDirectParticipantPii(
        string? body,
        string? reasonForChange)
    {
        string? bodyViolation = ScanFreeText("body", body);
        if (bodyViolation is not null)
        {
            return bodyViolation;
        }

        return ScanFreeText("reason_for_change", reasonForChange);
    }

    public static string? ValidateTransitionNoDirectParticipantPii(
        string? rootCauseSummary,
        string? resolutionCode,
        string? reasonForChange)
    {
        string? rootCauseViolation = ScanFreeText("root_cause_summary", rootCauseSummary);
        if (rootCauseViolation is not null)
        {
            return rootCauseViolation;
        }

        string? resolutionViolation = ScanFreeText("resolution_code", resolutionCode);
        if (resolutionViolation is not null)
        {
            return resolutionViolation;
        }

        return ScanFreeText("reason_for_change", reasonForChange);
    }

    public static string? ValidateSignatureNoDirectParticipantPii(
        string? meaning,
        string? reasonForChange)
    {
        string? meaningViolation = ScanFreeText("signature_meaning", meaning);
        if (meaningViolation is not null)
        {
            return meaningViolation;
        }

        return ScanFreeText("reason_for_change", reasonForChange);
    }

    private static string? ScanFreeText(string fieldName, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (ExplicitBirthDateRegex().IsMatch(text))
        {
            return $"Direct participant PII (date of birth) detected in {fieldName}. Reference participants by pseudonymous subject code only.";
        }

        if (ExplicitParticipantNameRegex().IsMatch(text))
        {
            return $"Direct participant PII (participant name) detected in {fieldName}. Reference participants by pseudonymous subject code only.";
        }

        var findings = Detector.Detect(text, ComplianceProfile.HIPAA);
        foreach (var finding in findings)
        {
            switch (finding.Category)
            {
                case DetectorCategory.Email:
                    return $"Direct participant PII (email address) detected in {fieldName}. Remove personal contact details and use pseudonymous subject codes only.";

                case DetectorCategory.SsnNationalId:
                    return $"Direct participant PII (government or national identifier) detected in {fieldName}. Remove direct identifiers and use pseudonymous subject codes only.";

                case DetectorCategory.MedicalRecordAccount:
                    return $"Direct participant PII (medical record or account number) detected in {fieldName}. Use pseudonymous subject codes only.";

                case DetectorCategory.HealthPlanBeneficiary:
                case DetectorCategory.CertificateLicense:
                    return $"Direct participant PII ({finding.Category}) detected in {fieldName}. Use pseudonymous subject codes only.";

                case DetectorCategory.TelephoneFax:
                    if (IsRealPhoneNumberMatch(text, finding))
                    {
                        return $"Direct participant PII (telephone or fax number) detected in {fieldName}. Remove personal contact details and use pseudonymous subject codes only.";
                    }

                    break;
            }
        }

        return null;
    }

    private static bool IsRealPhoneNumberMatch(string fullText, DetectionResult finding)
    {
        // Ignore hyphenated study/protocol/ticket codes where an alphabetic code prefix immediately precedes '-'.
        if (finding.Start > 1 && fullText[finding.Start - 1] == '-' && char.IsLetterOrDigit(fullText[finding.Start - 2]))
        {
            return false;
        }

        int digitCount = DigitRegex().Count(finding.Value);
        if (digitCount >= 10)
        {
            return true;
        }

        // For 7-digit local matches, require an explicit phone/fax/tel context cue so protocol numbers do not false-positive.
        int lookbackStart = Math.Max(0, finding.Start - 24);
        string contextPrefix = fullText.Substring(lookbackStart, finding.Start - lookbackStart);
        return contextPrefix.Contains("phone", StringComparison.OrdinalIgnoreCase)
               || contextPrefix.Contains("tel", StringComparison.OrdinalIgnoreCase)
               || contextPrefix.Contains("fax", StringComparison.OrdinalIgnoreCase)
               || contextPrefix.Contains("mobile", StringComparison.OrdinalIgnoreCase)
               || contextPrefix.Contains("call", StringComparison.OrdinalIgnoreCase);
    }
}
