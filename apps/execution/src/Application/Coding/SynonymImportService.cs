using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Coding;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Application.Coding;

/// <summary>
/// Service implementation for bulk synonym dictionary file validation, central rule persistence,
/// and GxP audit lineage.
/// @req:PRD-SYS-001
/// </summary>
public sealed class SynonymImportService(
    ExecutionDbContext dbContext,
    IClock clock) : ISynonymImportService
{
    private static readonly Regex MedDraCodeRegex = new(@"^\d{8}$", RegexOptions.Compiled);
    private static readonly Regex WhoDrugCodeRegex = new(@"^[A-Za-z0-9]{8,11}$", RegexOptions.Compiled);

    public async Task<PreflightValidationReportResponse> ValidatePreflightAsync(
        byte[] fileBytes,
        string fileName,
        string scope,
        string? studyId,
        CancellationToken cancellationToken = default)
    {
        string normalizedScope = NormalizeScope(scope);
        List<SynonymRowDto> rows = SynonymFileParser.Parse(fileBytes, fileName);

        // Fetch existing active rules for duplicate checking
        var existingActiveRules = await GetActiveRuleKeysAsync(normalizedScope, studyId, cancellationToken);

        var invalidRows = new List<InvalidRowDetailResponse>();
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int validCount = 0;
        int duplicateCount = 0;

        foreach (var row in rows)
        {
            var errors = new List<string>();

            // 1. Verbatim string check
            if (string.IsNullOrWhiteSpace(row.VerbatimText))
            {
                errors.Add("Missing required verbatim text.");
            }

            // 2. Dictionary type check
            string dictType = (row.DictionaryType ?? string.Empty).Trim().ToUpperInvariant();
            if (dictType != "MEDDRA" && dictType != "WHODRUG")
            {
                errors.Add($"Invalid dictionary type '{row.DictionaryType}'. Expected 'MEDDRA' or 'WHODRUG'.");
            }

            // 3. Dictionary version check
            if (string.IsNullOrWhiteSpace(row.DictionaryVersion))
            {
                errors.Add("Missing required dictionary version.");
            }

            // 4. Target code format check
            string code = (row.TargetCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                errors.Add("Missing required target code.");
            }
            else if (dictType == "MEDDRA" && !MedDraCodeRegex.IsMatch(code))
            {
                errors.Add($"Invalid MedDRA target code format '{code}'. Must be an 8-digit numeric code.");
            }
            else if (dictType == "WHODRUG" && !WhoDrugCodeRegex.IsMatch(code))
            {
                errors.Add($"Invalid WHODrug target code format '{code}'. Must be an 8 to 11 digit code.");
            }

            // 5. Target term check
            if (string.IsNullOrWhiteSpace(row.TargetTerm))
            {
                errors.Add("Missing required target term.");
            }

            // 6. Duplicate check within file and against active database store
            string ruleKey = $"{row.VerbatimText?.Trim().ToUpperInvariant()}|{dictType}";
            bool isDuplicateInFile = !seenInFile.Add(ruleKey);
            bool isDuplicateInDb = existingActiveRules.Contains(ruleKey);

            if (isDuplicateInFile || isDuplicateInDb)
            {
                duplicateCount++;
                if (isDuplicateInFile)
                {
                    errors.Add($"Duplicate verbatim mapping for '{row.VerbatimText}' within uploaded file.");
                }
                if (isDuplicateInDb)
                {
                    errors.Add($"Duplicate verbatim mapping for '{row.VerbatimText}' already exists in active synonym store.");
                }
            }

            if (errors.Count > 0)
            {
                invalidRows.Add(new InvalidRowDetailResponse(
                    RowNumber: row.RowNumber,
                    VerbatimText: row.VerbatimText ?? string.Empty,
                    DictionaryType: dictType,
                    TargetCode: code,
                    ErrorReason: string.Join(" ", errors)));
            }
            else
            {
                validCount++;
            }
        }

        int totalRows = rows.Count;
        int invalidCount = invalidRows.Count;
        bool canImport = validCount > 0;

        return new PreflightValidationReportResponse(
            TotalRows: totalRows,
            ValidCount: validCount,
            DuplicateCount: duplicateCount,
            InvalidCount: invalidCount,
            InvalidRows: invalidRows,
            CanImport: canImport);
    }

    public async Task<BulkImportExecutionResponse> ImportSynonymRulesAsync(
        byte[] fileBytes,
        string fileName,
        string scope,
        string? studyId,
        string changeReason,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(changeReason))
        {
            throw new ArgumentException("A valid reason for change is required for GxP audit compliance.", nameof(changeReason));
        }

        string normalizedScope = NormalizeScope(scope);
        string fileHash = ComputeSha256Hex(fileBytes);
        DateTime now = clock.UtcNow.UtcDateTime;

        List<SynonymRowDto> rows = SynonymFileParser.Parse(fileBytes, fileName);
        var existingActiveRules = await GetActiveRuleKeysAsync(normalizedScope, studyId, cancellationToken);

        var validRulesToInsert = new List<SynonymRule>();
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int duplicateCount = 0;
        int invalidCount = 0;

        Guid importJobId = Guid.NewGuid();

        foreach (var row in rows)
        {
            string dictType = (row.DictionaryType ?? string.Empty).Trim().ToUpperInvariant();
            string code = (row.TargetCode ?? string.Empty).Trim();

            bool isValidVerbatim = !string.IsNullOrWhiteSpace(row.VerbatimText);
            bool isValidDict = dictType == "MEDDRA" || dictType == "WHODRUG";
            bool isValidVersion = !string.IsNullOrWhiteSpace(row.DictionaryVersion);
            bool isValidTerm = !string.IsNullOrWhiteSpace(row.TargetTerm);
            bool isValidCode = !string.IsNullOrWhiteSpace(code) &&
                ((dictType == "MEDDRA" && MedDraCodeRegex.IsMatch(code)) ||
                 (dictType == "WHODRUG" && WhoDrugCodeRegex.IsMatch(code)));

            string ruleKey = $"{row.VerbatimText?.Trim().ToUpperInvariant()}|{dictType}";
            bool isDupFile = !seenInFile.Add(ruleKey);
            bool isDupDb = existingActiveRules.Contains(ruleKey);

            if (isDupFile || isDupDb)
            {
                duplicateCount++;
            }

            if (!isValidVerbatim || !isValidDict || !isValidVersion || !isValidTerm || !isValidCode || isDupFile || isDupDb)
            {
                invalidCount++;
                continue;
            }

            var entity = new SynonymRule(
                id: Guid.NewGuid(),
                scope: normalizedScope,
                studyId: normalizedScope == "STUDY" ? studyId : null,
                verbatimText: row.VerbatimText ?? string.Empty,
                dictionaryType: dictType,
                dictionaryVersion: row.DictionaryVersion ?? string.Empty,
                targetCode: code,
                targetTerm: row.TargetTerm ?? string.Empty,
                targetLevel: row.TargetLevel,
                importJobId: importJobId,
                createdBy: createdBy,
                reasonForChange: changeReason,
                createdAt: now);

            validRulesToInsert.Add(entity);
        }

        var importJob = new BulkSynonymImportJob(
            id: importJobId,
            scope: normalizedScope,
            studyId: normalizedScope == "STUDY" ? studyId : null,
            fileName: fileName,
            fileHash: fileHash,
            totalRows: rows.Count,
            validRows: validRulesToInsert.Count,
            duplicateRows: duplicateCount,
            invalidRows: invalidCount,
            status: "COMPLETED",
            createdBy: createdBy,
            reasonForChange: changeReason,
            createdAt: now);

        dbContext.BulkSynonymImportJobs.Add(importJob);

        if (validRulesToInsert.Count > 0)
        {
            dbContext.SynonymRules.AddRange(validRulesToInsert);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var jobResponse = MapJobResponse(importJob);
        var ruleResponses = validRulesToInsert.Select(MapRuleResponse).ToList();

        return new BulkImportExecutionResponse(jobResponse, ruleResponses);
    }

    public async Task<IReadOnlyList<SynonymRuleResponse>> GetActiveSynonymRulesAsync(
        string? studyId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.SynonymRules
            .AsNoTracking()
            .Where(x => x.Status == "ACTIVE");

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(x => x.Scope == "ENTERPRISE" || (x.Scope == "STUDY" && x.StudyId == studyId));
        }

        var rules = await query.ToListAsync(cancellationToken);
        return rules.Select(MapRuleResponse).ToList();
    }

    public async Task<IReadOnlyList<BulkSynonymImportJobResponse>> GetImportJobsAsync(
        string? studyId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.BulkSynonymImportJobs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(x => x.Scope == "ENTERPRISE" || (x.Scope == "STUDY" && x.StudyId == studyId));
        }

        var jobs = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return jobs.Select(MapJobResponse).ToList();
    }

    public async Task<bool> DeactivateRuleAsync(
        Guid id,
        string changeReason,
        CancellationToken cancellationToken = default)
    {
        var rule = await dbContext.SynonymRules.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (rule == null)
        {
            return false;
        }

        rule.Deactivate(changeReason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SynonymRuleResponse?> GetRuleByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var rule = await dbContext.SynonymRules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return rule is null ? null : MapRuleResponse(rule);
    }

    private async Task<HashSet<string>> GetActiveRuleKeysAsync(
        string scope,
        string? studyId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SynonymRules
            .AsNoTracking()
            .Where(x => x.Status == "ACTIVE");

        if (scope == "STUDY" && !string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(x => x.Scope == "ENTERPRISE" || (x.Scope == "STUDY" && x.StudyId == studyId));
        }

        var list = await query
            .Select(x => new { x.VerbatimText, x.DictionaryType })
            .ToListAsync(cancellationToken);

        return new HashSet<string>(
            list.Select(x => $"{x.VerbatimText.Trim().ToUpperInvariant()}|{x.DictionaryType.Trim().ToUpperInvariant()}"),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeScope(string scope)
    {
        string s = (scope ?? "STUDY").Trim().ToUpperInvariant();
        return s == "ENTERPRISE" ? "ENTERPRISE" : "STUDY";
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static SynonymRuleResponse MapRuleResponse(SynonymRule r) => new(
        r.Id,
        r.Scope,
        r.StudyId,
        r.VerbatimText,
        r.DictionaryType,
        r.DictionaryVersion,
        r.TargetCode,
        r.TargetTerm,
        r.TargetLevel,
        r.Status,
        r.ImportJobId,
        r.CreatedBy,
        r.CreatedAt,
        r.ReasonForChange);

    private static BulkSynonymImportJobResponse MapJobResponse(BulkSynonymImportJob j) => new(
        j.Id,
        j.Scope,
        j.StudyId,
        j.FileName,
        j.FileHash,
        j.TotalRows,
        j.ValidRows,
        j.DuplicateRows,
        j.InvalidRows,
        j.Status,
        j.CreatedBy,
        j.CreatedAt,
        j.ReasonForChange);
}
