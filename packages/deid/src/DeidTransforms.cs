using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.Deid;

public static class DeidTransforms
{
    public const int DefaultDateShiftDays = 365;

    /// <summary>
    /// The shortest HMAC key pseudonymization accepts, in bytes. The key is a secret from configuration; there is no
    /// default, because anyone who knows the key can recompute pseudonyms from candidate identifiers (#5016).
    /// </summary>
    public const int MinimumPseudonymizationKeyLength = 32;

    /// <summary>
    /// Replaces <paramref name="value"/> with its HMAC-SHA256 under <paramref name="salt"/>, the secret pseudonymization
    /// key (at least <see cref="MinimumPseudonymizationKeyLength"/> bytes).
    /// </summary>
    public static string PseudonymizeValue(string value, byte[] salt, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsurePseudonymizationKey(salt);

        using var hmac = new HMACSHA256(salt);
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        string hex = Convert.ToHexStringLower(hash);
        return string.IsNullOrEmpty(prefix) ? hex : $"{prefix}{hex}";
    }

    public static string PseudonymizeValue(string value, string salt, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(salt);
        return PseudonymizeValue(value, Encoding.UTF8.GetBytes(salt), prefix);
    }

    private static void EnsurePseudonymizationKey([NotNull] byte[]? salt)
    {
        if (salt is null || salt.Length < MinimumPseudonymizationKeyLength)
        {
            throw new ArgumentException(
                $"Pseudonymization needs a secret key of at least {MinimumPseudonymizationKeyLength} bytes; there is no default key.",
                nameof(salt));
        }
    }

    public static string ShiftDateString(string dateStr, int shiftDays = DefaultDateShiftDays)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
        {
            return "[DATE_INVALID]";
        }

        if (!DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) &&
            !DateTime.TryParse(dateStr, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
        {
            return "[DATE_INVALID]";
        }

        var shifted = dt.AddDays(shiftDays);

        if (Regex.IsMatch(dateStr, @"^\d{4}-\d{2}-\d{2}$"))
        {
            return shifted.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (Regex.IsMatch(dateStr, @"^\d{4}/\d{2}/\d{2}$"))
        {
            return shifted.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        }
        if (Regex.IsMatch(dateStr, @"^\d{2}/\d{2}/\d{4}$"))
        {
            return shifted.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        }
        if (Regex.IsMatch(dateStr, @"^\d{1,2}-[a-zA-Z]{3}-\d{4}$", RegexOptions.IgnoreCase))
        {
            return shifted.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        }
        if (Regex.IsMatch(dateStr, @"^[a-zA-Z]{3}\s+\d{1,2},?\s+\d{4}$", RegexOptions.IgnoreCase))
        {
            bool hasComma = dateStr.Contains(',');
            string fmt = hasComma ? "MMM dd, yyyy" : "MMM dd yyyy";
            return shifted.ToString(fmt, CultureInfo.InvariantCulture);
        }

        return shifted.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string CapAgeString(string ageStr, int cap = 89)
    {
        if (string.IsNullOrWhiteSpace(ageStr))
        {
            return ageStr;
        }

        var pattern = new Regex(
            @"\b(?:age[sd]?|age of)\s*[:\- ]?\s*(\d{1,3})\b|\b(\d{1,3})\s*(?:years?\s*(?:of\s*age)?\s*old|-years?-old|yo|-yo)\b",
            RegexOptions.IgnoreCase);

        var matches = pattern.Matches(ageStr).Cast<Match>().ToList();
        if (matches.Count == 0)
        {
            return ageStr;
        }

        var sb = new StringBuilder(ageStr);
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            int startIdx = -1;
            int length = -1;
            string ageValStr = string.Empty;

            if (match.Groups[1].Success)
            {
                startIdx = match.Groups[1].Index;
                length = match.Groups[1].Length;
                ageValStr = match.Groups[1].Value;
            }
            else if (match.Groups[2].Success)
            {
                startIdx = match.Groups[2].Index;
                length = match.Groups[2].Length;
                ageValStr = match.Groups[2].Value;
            }

            if (startIdx != -1 && int.TryParse(ageValStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ageVal) && ageVal > cap)
            {
                sb.Remove(startIdx, length);
                sb.Insert(startIdx, $"{cap}+");
            }
        }

        return sb.ToString();
    }

    public static (string TransformedText, List<RedactionRecordItem> RedactionRecord) ApplyDeidTransforms(
        string text,
        List<DetectionResult> results,
        Dictionary<string, string>? strategies = null,
        string defaultStrategy = "mask",
        byte[]? salt = null,
        int shiftDays = DefaultDateShiftDays,
        int ageCap = 89)
    {
        // No default key (#5016): the pseudonymize strategy fails below unless the caller passes one.
        var cleanResults = DeidDetector.ResolveOverlaps(results, text);

        var sb = new StringBuilder(text);
        var redactionRecord = new List<RedactionRecordItem>();

        for (int i = cleanResults.Count - 1; i >= 0; i--)
        {
            var res = cleanResults[i];
            string strategy = "mask";

            if (strategies != null && strategies.TryGetValue(res.Category, out var customStrat))
            {
                strategy = customStrat;
            }
            else if (!string.IsNullOrEmpty(defaultStrategy))
            {
                strategy = defaultStrategy;
            }

            if (strategy != "mask" && strategy != "pseudonymize" && strategy != "date_shift" && strategy != "age_cap")
            {
                strategy = "mask";
            }

            string replacement = $"[{res.Category.ToUpperInvariant()}]";
            if (strategy == "mask")
            {
                replacement = $"[{res.Category.ToUpperInvariant()}]";
            }
            else if (strategy == "pseudonymize")
            {
                EnsurePseudonymizationKey(salt);
                replacement = PseudonymizeValue(res.Value, salt);
            }
            else if (strategy == "date_shift")
            {
                replacement = res.Category == DetectorCategory.Dates ? ShiftDateString(res.Value, shiftDays) : $"[{res.Category.ToUpperInvariant()}]";
            }
            else if (strategy == "age_cap")
            {
                replacement = res.Category == DetectorCategory.Age ? CapAgeString(res.Value, ageCap) : $"[{res.Category.ToUpperInvariant()}]";
            }

            sb.Remove(res.Start, res.End - res.Start);
            sb.Insert(res.Start, replacement);

            redactionRecord.Add(new RedactionRecordItem(res.Category, strategy, res.Start, res.End, replacement));
        }

        redactionRecord.Reverse();
        return (sb.ToString(), redactionRecord);
    }

    public static string PseudonymizeSubjectId(string subjectId, byte[] salt, string prefix = "")
    {
        return PseudonymizeValue(subjectId, salt, prefix);
    }

    public static string PseudonymizeSubjectId(string subjectId, string salt, string prefix = "")
    {
        return PseudonymizeValue(subjectId, Encoding.UTF8.GetBytes(salt ?? string.Empty), prefix);
    }

    public static int GetSubjectDateShift(string subjectId, byte[] salt)
    {
        if (string.IsNullOrEmpty(subjectId))
        {
            return 0;
        }

        string hex = PseudonymizeValue(subjectId, salt);
        byte[] bytes = Convert.FromHexString(hex);
        var bigInt = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        BigInteger remainder = bigInt % 731;
        return (int)remainder - 365;
    }

    public static int GetSubjectDateShift(string subjectId, string salt)
    {
        return GetSubjectDateShift(subjectId, Encoding.UTF8.GetBytes(salt ?? string.Empty));
    }

    public static object? ShiftDateBySubject(object? dateVal, string subjectId, byte[] salt)
    {
        if (dateVal == null)
        {
            return null;
        }

        int offset = GetSubjectDateShift(subjectId, salt);

        if (dateVal is bool)
        {
            return dateVal;
        }

        if (dateVal is int iVal)
        {
            return iVal + offset;
        }
        if (dateVal is long lVal)
        {
            return lVal + offset;
        }
        if (dateVal is double dVal)
        {
            return dVal + offset;
        }
        if (dateVal is float fVal)
        {
            return fVal + offset;
        }

        if (dateVal is string strVal)
        {
            string trimmed = strVal.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return dateVal;
            }

            if (Regex.IsMatch(trimmed, @"^-?\d+$") && long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
            {
                return parsedLong + offset;
            }
            if (Regex.IsMatch(trimmed, @"^-?\d+\.\d+$") && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                return parsedDouble + offset;
            }

            return ShiftDateString(trimmed, offset);
        }

        return dateVal;
    }

    public static object? ShiftDateBySubject(object? dateVal, string subjectId, string salt)
    {
        return ShiftDateBySubject(dateVal, subjectId, Encoding.UTF8.GetBytes(salt ?? string.Empty));
    }

    public static double CapAgeNumeric(double age, int cap = 89)
    {
        return age > cap ? cap : age;
    }

    public static object? NormalizeAndCapAge(object? ageVal, int cap = 89)
    {
        if (ageVal == null || ageVal is bool)
        {
            return ageVal;
        }

        if (ageVal is int iVal)
        {
            return iVal > cap ? cap : iVal;
        }
        if (ageVal is long lVal)
        {
            return lVal > cap ? (long)cap : lVal;
        }
        if (ageVal is double dVal)
        {
            return dVal > cap ? (double)cap : dVal;
        }
        if (ageVal is float fVal)
        {
            return fVal > cap ? (float)cap : fVal;
        }

        if (ageVal is string strVal)
        {
            string trimmed = strVal.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return ageVal;
            }

            var match = Regex.Match(trimmed, @"(\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return ageVal;
            }

            string numStr = match.Groups[1].Value;
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                if (val > cap)
                {
                    return cap.ToString(CultureInfo.InvariantCulture);
                }
                return ageVal;
            }
        }

        return ageVal;
    }

    public static string ScrubErrorMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg))
        {
            return msg;
        }

        string result = msg;
        result = Regex.Replace(result, @"\b\d{3}-\d{2}-\d{4}\b", "[REDACTED_SSN]");
        result = Regex.Replace(result, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", "[REDACTED_EMAIL]");
        result = Regex.Replace(result, @"\b(?:\+?1[-. ]?)?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}\b", "[REDACTED_PHONE]");

        result = Regex.Replace(result, @"\b(?:USUBJID|SUBJID|SUBJECT|PATIENT)[_:-]+\s*[\w-]+\b", "[REDACTED_SUBJECT]", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\bSUBJ-[\w-]+\b", "[REDACTED_SUBJECT]", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, @"\b(?:SITEID|SITE)[_:-]+\s*[\w-]+\b", "[REDACTED_SITE]", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\b(?:STUDYID|STUDY)[_:-]+\s*[\w-]+\b", "[REDACTED_STUDY]", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, @"'(.*?)'", "'[REDACTED_VALUE]'");
        result = Regex.Replace(result, @"""(.*?)""", "\"[REDACTED_VALUE]\"");

        return result;
    }
}
