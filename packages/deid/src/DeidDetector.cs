using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.Deid;

public class DeidDetector
{
    private static readonly Regex EmailRegex = new(@"(?<!\w)[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b", RegexOptions.Compiled);

    private static readonly Regex PhoneFaxRegex = new(
        @"(?<!\w)(?:\+(?:[-.\s]*1)?[-.\s]*\(?\d{3}\)?|\(?\d{3}\)?)[-.\s]*\d{3}[-.\s]*\d{4}\b|(?<!\w)(?:\(?\d{3}\)?)[-.\s]*\d{4}\b|(?:\+\d{1,3}[-.\s]*)?\(?\d{2,4}\)?[-.\s]*\d{3,4}[-.\s]*\d{4}\b|\b\d{3}[-.\s]*\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex SsnNationalIdRegex = new(
        @"\b\d{3}-\d{2}-\d{4}\b|\b[A-CEGHJ-PR-TW-Z][A-CEGHJ-NPR-TW-Z]\s*\d{2}\s*\d{2}\s*\d{2}\s*[A-D]\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex[] DatePatterns =
    [
        new(@"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}\b", RegexOptions.Compiled),
        new(@"\b\d{1,2}[-/]\d{1,2}[-/]\d{2,4}\b", RegexOptions.Compiled),
        new(@"\b\d{1,2}[-\s](?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-zA-Z]*[-\s]\d{2,4}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-zA-Z]*\s+\d{1,2}(?:st|nd|rd|th)?[\s,]+\d{2,4}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    ];

    private static readonly Regex ZipGeographicRegex = new(
        @"\b\d{5}(?:-\d{4})?\b|\b[A-Za-z]\d[A-Za-z][ -]?\d[A-Za-z]\d\b|\b[A-Za-z]{1,2}\d[A-Za-z\d]?\s*\d[A-Za-z]{2}\b",
        RegexOptions.Compiled);

    private static readonly Regex UrlRegex = new(
        @"\b(?:https?://|www\.)[a-zA-Z0-9-._~:/?#\[\]@!$&'()*+,;=]+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IpMacRegex = new(
        @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b|\b(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}\b|\b(?:[0-9a-fA-F]{1,4}:)+(?::[0-9a-fA-F]{1,4})+\b|\b(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}\b|\b::(?:[0-9a-fA-F]{1,4}:){0,6}[0-9a-fA-F]{1,4}\b|\b(?:[0-9a-fA-F]{1,4}:){1,7}:\b",
        RegexOptions.Compiled);

    private static readonly Regex MedicalRecordAccountRegex = new(
        @"\b(?:MRN|EHR|ACC|NHS)[-:#\s]*[a-zA-Z0-9-]*\d[a-zA-Z0-9-]*\b|\b\d{3}[-\s]?\d{3}[-\s]?\d{4}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AgeRegex = new(
        @"\b(?:age[sd]?|age of)\s*[:\- ]?\s*\d{1,3}\b|\b\d{1,3}\s*(?:years?\s*(?:of\s*age)?\s*old|-years?-old|yo|-yo)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HealthPlanBeneficiaryRegex = new(
        @"\b(?:HICN|MBI|Medicare|Beneficiary|Health Plan|Policy(?: Number)?|Plan ID)[:#\s]*(?=[A-Za-z0-9-]*\d)[A-Za-z0-9-]{5,15}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CertificateLicenseRegex = new(
        @"\b(?:License|Certificate|Cert|Lic|Medical License|State License|Driver License)[:#\s]*(?=[A-Za-z0-9-]*\d)[A-Za-z0-9-]{5,15}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VehicleIdentifiersRegex = new(
        @"\b(?:VIN|Vehicle ID|License Plate|Plate Number|Engine Number)[:#\s]*(?=[A-Za-z0-9-]*\d)[A-Za-z0-9-]{5,17}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DeviceSerialRegex = new(
        @"\b(?:Device ID|Serial Number|Serial|Device Serial|Model Number|UDI)[:#\s]*(?=[A-Za-z0-9-]*\d)[A-Za-z0-9-]{5,20}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TenDigitRegex = new(@"^\d{3}[-.\s]?\d{3}[-.\s]?\d{4}$", RegexOptions.Compiled);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods should be static", Justification = "Instance method API consistency across de-identification detectors")]
    public List<DetectionResult> Detect(string text, ComplianceProfile profile = ComplianceProfile.HIPAA, List<string>? customTerms = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var activeCategories = ProfileCategories.GetCategoriesForProfile(profile);
        var candidates = new List<DetectionResult>();

        if (activeCategories.Contains(DetectorCategory.Email))
        {
            foreach (Match m in EmailRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.Email, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.TelephoneFax))
        {
            foreach (Match m in PhoneFaxRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.TelephoneFax, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.SsnNationalId))
        {
            foreach (Match m in SsnNationalIdRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.SsnNationalId, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.Dates))
        {
            foreach (var pattern in DatePatterns)
            {
                foreach (Match m in pattern.Matches(text))
                {
                    candidates.Add(new DetectionResult(DetectorCategory.Dates, m.Index, m.Index + m.Length, m.Value));
                }
            }
        }

        if (activeCategories.Contains(DetectorCategory.ZipGeographic))
        {
            foreach (Match m in ZipGeographicRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.ZipGeographic, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.Urls))
        {
            foreach (Match m in UrlRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.Urls, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.IpMacAddresses))
        {
            foreach (Match m in IpMacRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.IpMacAddresses, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.MedicalRecordAccount))
        {
            foreach (Match m in MedicalRecordAccountRegex.Matches(text))
            {
                string val = m.Value;
                if (TenDigitRegex.IsMatch(val))
                {
                    int startCheck = Math.Max(0, m.Index - 25);
                    string preceding = text.Substring(startCheck, m.Index - startCheck).ToLowerInvariant();
                    if (!preceding.Contains("nhs") && !preceding.Contains("acc") && !preceding.Contains("record") &&
                        !preceding.Contains("mrn") && !preceding.Contains("ehr"))
                    {
                        continue;
                    }
                }
                candidates.Add(new DetectionResult(DetectorCategory.MedicalRecordAccount, m.Index, m.Index + m.Length, val));
            }
        }

        if (activeCategories.Contains(DetectorCategory.Age))
        {
            foreach (Match m in AgeRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.Age, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.HealthPlanBeneficiary))
        {
            foreach (Match m in HealthPlanBeneficiaryRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.HealthPlanBeneficiary, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.CertificateLicense))
        {
            foreach (Match m in CertificateLicenseRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.CertificateLicense, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.VehicleIdentifiers))
        {
            foreach (Match m in VehicleIdentifiersRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.VehicleIdentifiers, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.DeviceSerial))
        {
            foreach (Match m in DeviceSerialRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.DeviceSerial, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.Custom) && customTerms != null)
        {
            var validTerms = customTerms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (validTerms.Count > 0)
            {
                validTerms.Sort((a, b) => b.Length.CompareTo(a.Length));
                var patterns = new List<string>();
                foreach (var term in validTerms)
                {
                    string escaped = Regex.Escape(term);
                    if (term.Length <= 3 && Regex.IsMatch(term, @"^\w+$"))
                    {
                        patterns.Add($@"\b{escaped}\b");
                    }
                    else
                    {
                        patterns.Add(escaped);
                    }
                }

                var customRegex = new Regex(string.Join("|", patterns), RegexOptions.IgnoreCase);
                foreach (Match m in customRegex.Matches(text))
                {
                    candidates.Add(new DetectionResult(DetectorCategory.Custom, m.Index, m.Index + m.Length, m.Value));
                }
            }
        }

        var clinicalCategories = new HashSet<string>
        {
            DetectorCategory.MedicalRecordAccount,
            DetectorCategory.SsnNationalId,
            DetectorCategory.HealthPlanBeneficiary,
            DetectorCategory.CertificateLicense,
            DetectorCategory.VehicleIdentifiers,
            DetectorCategory.DeviceSerial
        };

        foreach (var cand in candidates.Where(c => clinicalCategories.Contains(c.Category)))
        {
            int newEnd = cand.End;
            while (newEnd < text.Length && (text[newEnd] == '-' || text[newEnd] == '/' || text[newEnd] == '\\'))
            {
                newEnd++;
            }
            if (newEnd > cand.End)
            {
                cand.End = newEnd;
                cand.Value = text.Substring(cand.Start, cand.End - cand.Start);
            }
        }

        return ResolveOverlaps(candidates, text);
    }

    public static List<DetectionResult> ResolveOverlaps(List<DetectionResult> results, string text = "")
    {
        var sorted = results.OrderBy(x => x.Start)
                            .ThenByDescending(x => x.End)
                            .ThenBy(x => x.Category, StringComparer.Ordinal)
                            .ThenByDescending(x => x.Value.Length)
                            .ToList();

        var resolved = new List<DetectionResult>();
        foreach (var res in sorted)
        {
            bool overlaps = false;
            foreach (var accepted in resolved)
            {
                if (Math.Max(res.Start, accepted.Start) < Math.Min(res.End, accepted.End))
                {
                    overlaps = true;
                    break;
                }
            }
            if (!overlaps)
            {
                resolved.Add(res);
            }
        }

        return resolved.OrderBy(x => x.Start).ToList();
    }

    public static string RedactText(string text, List<DetectionResult> results, Func<DetectionResult, string>? placeholderFunc = null)
    {
        var clean = ResolveOverlaps(results, text);
        var sb = new StringBuilder(text);

        foreach (var res in clean.OrderByDescending(x => x.Start))
        {
            string replacement = placeholderFunc != null ? placeholderFunc(res) : $"[{res.Category.ToUpperInvariant()}]";
            sb.Remove(res.Start, res.End - res.Start);
            sb.Insert(res.Start, replacement);
        }

        return sb.ToString();
    }
}
