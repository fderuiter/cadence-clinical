using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.Deid;

public class DeidDetector
{
    private static readonly Regex EmailRegex = new(@"(?<!\w)[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex PhoneFaxRegex = new(@"(?<!\w)(?:\+(?:[-.\s]*1)?[-.\s]*\(?\d{3}\)?|\(?\d{3}\)?)[-.\s]*\d{3}[-.\s]*\d{4}\b|(?<!\w)(?:\(?\d{3}\)?)[-.\s]*\d{4}\b|\b\d{3}[-.\s]*\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex SsnRegex = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"\b(?:https?://|www\.)[a-zA-Z0-9-._~:/?#\[\]@!$&'()*+,;=]+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IpRegex = new(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b", RegexOptions.Compiled);
    private static readonly Regex AgeRegex = new(@"\b(?:age[sd]?|age of)\s*[:\- ]?\s*\d{1,3}\b|\b\d{1,3}\s*(?:years?\s*(?:of\s*age)?\s*old|-years?-old|yo|-yo)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            foreach (Match m in SsnRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.SsnNationalId, m.Index, m.Index + m.Length, m.Value));
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
            foreach (Match m in IpRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.IpMacAddresses, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.Age))
        {
            foreach (Match m in AgeRegex.Matches(text))
            {
                candidates.Add(new DetectionResult(DetectorCategory.Age, m.Index, m.Index + m.Length, m.Value));
            }
        }

        if (activeCategories.Contains(DetectorCategory.Custom) && customTerms != null)
        {
            foreach (var term in customTerms.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                var regex = new Regex(Regex.Escape(term), RegexOptions.IgnoreCase);
                foreach (Match m in regex.Matches(text))
                {
                    candidates.Add(new DetectionResult(DetectorCategory.Custom, m.Index, m.Index + m.Length, m.Value));
                }
            }
        }

        return ResolveOverlaps(candidates);
    }

    public static List<DetectionResult> ResolveOverlaps(List<DetectionResult> results)
    {
        var sorted = results.OrderBy(x => x.Start)
                            .ThenByDescending(x => x.End)
                            .ThenBy(x => x.Category)
                            .ThenByDescending(x => x.Value.Length)
                            .ToList();

        var resolved = new List<DetectionResult>();
        foreach (var res in sorted)
        {
            var overlaps = false;
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
        var clean = ResolveOverlaps(results);
        var sb = new System.Text.StringBuilder(text);

        foreach (var res in clean.OrderByDescending(x => x.Start))
        {
            var replacement = placeholderFunc != null ? placeholderFunc(res) : $"[{res.Category.ToUpperInvariant()}]";
            sb.Remove(res.Start, res.End - res.Start);
            sb.Insert(res.Start, replacement);
        }

        return sb.ToString();
    }
}
