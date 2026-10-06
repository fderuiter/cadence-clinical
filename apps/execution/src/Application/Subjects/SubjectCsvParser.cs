using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Subjects;

public sealed record ParsedCsvRow(
    int RowNumber,
    string SubjectId,
    StudyCode? StudyCode,
    SiteCode? SiteCode,
    string? ArmId,
    string? Name,
    string? Birthdate,
    string? Gender,
    string? Race);

public static class SubjectCsvParser
{
    public const int MaxSupportedRows = 1000;

    public static async Task<IReadOnlyList<ParsedCsvRow>> ParseAsync(Stream csvStream, CancellationToken ct)
    {
        using var reader = new StreamReader(csvStream, Encoding.UTF8, leaveOpen: true);
        var lines = new List<List<string>>();

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ParseCsvLine(line);
            if (fields.Count > 0 && fields.Any(f => !string.IsNullOrWhiteSpace(f)))
            {
                lines.Add(fields);
            }
        }

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("CSV roster file is empty.");
        }

        var headers = lines[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int subjectIdIndex = FindHeaderIndex(headers, "subject_id", "subjectid", "subject_identifier", "subject");
        int studyIdIndex = FindHeaderIndex(headers, "study_id", "studyid", "study");
        int siteIdIndex = FindHeaderIndex(headers, "site_id", "siteid", "site_code", "site");
        int armIdIndex = FindHeaderIndex(headers, "arm_id", "armid", "arm");
        int nameIndex = FindHeaderIndex(headers, "name", "full_name");
        int birthdateIndex = FindHeaderIndex(headers, "birthdate", "date_of_birth", "dob");
        int genderIndex = FindHeaderIndex(headers, "gender", "sex");
        int raceIndex = FindHeaderIndex(headers, "race", "ethnicity");

        if (subjectIdIndex < 0)
        {
            throw new InvalidOperationException("CSV roster file must contain a 'subject_id' header column.");
        }

        var rows = new List<ParsedCsvRow>();
        for (int i = 1; i < lines.Count; i++)
        {
            if (rows.Count >= MaxSupportedRows)
            {
                throw new InvalidOperationException($"CSV roster file exceeds maximum row limit of {MaxSupportedRows} subjects.");
            }

            var lineFields = lines[i];
            string subjectId = GetValue(lineFields, subjectIdIndex) ?? string.Empty;
            string? rawStudyId = GetValue(lineFields, studyIdIndex);
            string? rawSiteId = GetValue(lineFields, siteIdIndex);
            string? armId = GetValue(lineFields, armIdIndex);
            string? name = GetValue(lineFields, nameIndex);
            string? birthdate = GetValue(lineFields, birthdateIndex);
            string? gender = GetValue(lineFields, genderIndex);
            string? race = GetValue(lineFields, raceIndex);

            StudyCode? studyCode = !string.IsNullOrWhiteSpace(rawStudyId) ? new StudyCode(rawStudyId) : null;
            SiteCode? siteCode = !string.IsNullOrWhiteSpace(rawSiteId) ? new SiteCode(rawSiteId) : null;

            rows.Add(new ParsedCsvRow(
                RowNumber: i,
                SubjectId: subjectId,
                StudyCode: studyCode,
                SiteCode: siteCode,
                ArmId: armId,
                Name: name,
                Birthdate: birthdate,
                Gender: gender,
                Race: race));
        }

        return rows;
    }

    private static int FindHeaderIndex(List<string> headers, params string[] candidates)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            string clean = headers[i].Replace(" ", "_");
            if (candidates.Contains(clean)) return i;
        }
        return -1;
    }

    private static string? GetValue(List<string> fields, int index)
    {
        if (index < 0 || index >= fields.Count) return null;
        string val = fields[index].Trim();
        return string.IsNullOrEmpty(val) ? null : val;
    }

    public static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        var current = new StringBuilder();
        bool inQuotes = false;
        int i = 0;

        while (i < line.Length)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i += 2;
                    continue;
                }

                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }

            i++;
        }
        result.Add(current.ToString());

        return result;
    }
}
