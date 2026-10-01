using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Domain.Ledger;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Application.Services;

/// <summary>
/// Parsed CSV row DTO with line-item calculations and validation state.
/// </summary>
public sealed record FeeScheduleImportRow(
    int RowNumber,
    string StudyId,
    string SiteId,
    string VisitCode,
    string ProcedureCode,
    string ProcedureName,
    decimal BaseAmount,
    decimal OverheadPercentage,
    decimal WithholdingPercentage,
    decimal PatientStipend,
    decimal TravelReimbursement,
    bool RequiresSdv,
    decimal OverheadAmount,
    decimal GrossAmount,
    decimal WithholdingAmount,
    decimal NetPayable,
    IReadOnlyList<string> ValidationErrors)
{
    public bool IsValid => ValidationErrors.Count == 0;
}

/// <summary>
/// Input DTO for committing validated fee schedule rows.
/// </summary>
public sealed record FeeScheduleImportRowInput(
    string StudyId,
    string SiteId,
    string VisitCode,
    string ProcedureCode,
    string ProcedureName,
    decimal BaseAmount,
    decimal OverheadPercentage,
    decimal WithholdingPercentage,
    decimal PatientStipend,
    decimal TravelReimbursement,
    bool RequiresSdv);

/// <summary>
/// Preview result for bulk fee schedule CSV upload validation.
/// </summary>
public sealed record BulkFeeScheduleValidationResult(
    int TotalRows,
    int ValidRowsCount,
    int InvalidRowsCount,
    bool IsValid,
    decimal TotalEstimatedBudget,
    IReadOnlyList<FeeScheduleImportRow> Rows,
    IReadOnlyList<string> GlobalErrors);

/// <summary>
/// Request payload to commit validated fee schedule import rows.
/// </summary>
public sealed record BulkFeeScheduleCommitRequest(
    IReadOnlyList<FeeScheduleImportRowInput> Rows,
    string? CreatedBy);

/// <summary>
/// Result of bulk fee schedule batch transaction commit.
/// </summary>
public sealed record BulkFeeScheduleCommitResult(
    bool Success,
    int ImportedContractsCount,
    int ImportedFeeSchedulesCount,
    decimal TotalCommittedBudget,
    string Message,
    IReadOnlyList<string> ContractIds);

/// <summary>
/// Contract interface for bulk CSV site fee schedule importer service.
/// </summary>
public interface IBulkFeeScheduleImporterService
{
    /// <summary>
    /// Parses and validates site fee schedule CSV string payload.
    /// </summary>
    BulkFeeScheduleValidationResult ValidateCsv(string csvContent);

    /// <summary>
    /// Parses and validates site fee schedule CSV stream payload.
    /// </summary>
    BulkFeeScheduleValidationResult ValidateCsvStream(Stream stream);

    /// <summary>
    /// Commits validated fee schedule rows in a single batch transaction.
    /// </summary>
    Task<BulkFeeScheduleCommitResult> CommitBulkFeeScheduleAsync(
        IReadOnlyList<FeeScheduleImportRowInput> rows,
        string createdBy,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a standardized CSV site fee schedule template.
    /// </summary>
    string GenerateCsvTemplate(string? studyId = null, string? siteId = null);
}

/// <summary>
/// Application service to parse, validate, preview and commit site fee schedule CSV uploads in bulk.
/// </summary>
public sealed class BulkFeeScheduleImporterService : IBulkFeeScheduleImporterService
{
    private readonly PaymentsDbContext _dbContext;
    private readonly IClock _clock;

    public BulkFeeScheduleImporterService(PaymentsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public BulkFeeScheduleValidationResult ValidateCsv(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return new BulkFeeScheduleValidationResult(
                TotalRows: 0,
                ValidRowsCount: 0,
                InvalidRowsCount: 0,
                IsValid: false,
                TotalEstimatedBudget: 0m,
                Rows: [],
                GlobalErrors: ["CSV payload is empty."]);
        }

        using var reader = new StringReader(csvContent);
        return ParseAndValidate(reader);
    }

    public BulkFeeScheduleValidationResult ValidateCsvStream(Stream stream)
    {
        if (stream == null || stream.Length == 0)
        {
            return new BulkFeeScheduleValidationResult(
                TotalRows: 0,
                ValidRowsCount: 0,
                InvalidRowsCount: 0,
                IsValid: false,
                TotalEstimatedBudget: 0m,
                Rows: [],
                GlobalErrors: ["CSV stream is empty."]);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return ParseAndValidate(reader);
    }

    public async Task<BulkFeeScheduleCommitResult> CommitBulkFeeScheduleAsync(
        IReadOnlyList<FeeScheduleImportRowInput> rows,
        string createdBy,
        CancellationToken ct = default)
    {
        if (rows == null || rows.Count == 0)
        {
            return new BulkFeeScheduleCommitResult(
                Success: false,
                ImportedContractsCount: 0,
                ImportedFeeSchedulesCount: 0,
                TotalCommittedBudget: 0m,
                Message: "No rows provided for import.",
                ContractIds: []);
        }

        string userActor = string.IsNullOrWhiteSpace(createdBy) ? "system_importer" : createdBy;
        DateTimeOffset now = _clock.UtcNow;

        using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            var createdContractIds = new List<string>();
            int totalFeeSchedulesCreated = 0;
            decimal totalBudgetSum = 0m;

            // Group rows by StudyId and SiteId
            var groupedBySite = rows.GroupBy(r => (
                StudyId: string.IsNullOrWhiteSpace(r.StudyId) ? "UNKNOWN_STUDY" : r.StudyId.Trim(),
                SiteId: string.IsNullOrWhiteSpace(r.SiteId) ? "UNKNOWN_SITE" : r.SiteId.Trim()));

            foreach (var group in groupedBySite)
            {
                string studyId = group.Key.StudyId;
                string siteId = group.Key.SiteId;

                // Check for existing active BudgetContract
                var existingContract = await _dbContext.BudgetContracts
                    .FirstOrDefaultAsync(b => b.StudyId == studyId && b.SiteId == siteId && b.IsActive, ct);

                BudgetContract contract;
                if (existingContract != null)
                {
                    contract = existingContract;
                }
                else
                {
                    contract = new BudgetContract
                    {
                        Id = Guid.NewGuid().ToString(),
                        StudyId = studyId,
                        SiteId = siteId,
                        ContractName = $"Grant Contract {siteId} - {studyId}",
                        TierLevel = "TIER_1_ACADEMIC",
                        Currency = "USD",
                        TotalBudget = 0m,
                        OverheadPercentage = 25.0m,
                        WithholdingPercentage = 10.0m,
                        EffectiveDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Status = "ACTIVE",
                        IsActive = true,
                        CreatedAt = now,
                        CreatedBy = userActor,
                        UpdatedAt = now,
                        ReasonForChange = "Bulk CSV Fee Schedule Ingestion",
                        VersionIndex = 1
                    };
                    _dbContext.BudgetContracts.Add(contract);
                }

                if (!createdContractIds.Contains(contract.Id))
                {
                    createdContractIds.Add(contract.Id);
                }

                decimal siteGroupBudget = 0m;

                foreach (var item in group)
                {
                    decimal baseAmount = ContractCalculations.QuantizeCurrency(item.BaseAmount);
                    decimal overheadRate = item.OverheadPercentage;
                    decimal overheadAmount = ContractCalculations.QuantizeCurrency(baseAmount * overheadRate / 100m);
                    decimal grossAmount = baseAmount + overheadAmount;

                    siteGroupBudget += grossAmount;

                    var feeSchedule = new FeeSchedule
                    {
                        Id = Guid.NewGuid().ToString(),
                        ContractId = contract.Id,
                        VisitCode = item.VisitCode.Trim(),
                        ProcedureCode = item.ProcedureCode.Trim(),
                        ProcedureName = item.ProcedureName.Trim(),
                        BaseAmount = baseAmount,
                        OverheadPercentage = overheadRate,
                        WithholdingPercentage = item.WithholdingPercentage,
                        PatientStipend = ContractCalculations.QuantizeCurrency(item.PatientStipend),
                        TravelReimbursement = ContractCalculations.QuantizeCurrency(item.TravelReimbursement),
                        RequiresSdv = item.RequiresSdv,
                        IsActive = true,
                        CreatedAt = now,
                        CreatedBy = userActor,
                        UpdatedAt = now,
                        ReasonForChange = "Bulk CSV Fee Schedule Import",
                        VersionIndex = 1
                    };

                    _dbContext.FeeSchedules.Add(feeSchedule);
                    totalFeeSchedulesCreated++;
                }

                contract.TotalBudget = ContractCalculations.QuantizeCurrency(contract.TotalBudget + siteGroupBudget);
                contract.UpdatedAt = now;
                contract.ReasonForChange = "Updated budget via bulk fee schedule import";

                totalBudgetSum += siteGroupBudget;

                // GxP Audit Log entry per contract
                var auditLog = new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "FeeSchedule",
                    EntityId = contract.Id,
                    Action = "BULK_IMPORT_FEE_SCHEDULE",
                    Details = $"Bulk imported {group.Count()} fee schedule procedures for study '{studyId}', site '{siteId}'. Total gross amount: ${siteGroupBudget:F2}",
                    UserId = userActor,
                    UserRoles = "sponsor_finance_manager",
                    CreatedAt = now,
                    CreatedBy = userActor,
                    ReasonForChange = "Bulk CSV Fee Schedule Ingestion",
                    VersionIndex = 1
                };
                _dbContext.PaymentAuditLogs.Add(auditLog);
            }

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new BulkFeeScheduleCommitResult(
                Success: true,
                ImportedContractsCount: createdContractIds.Count,
                ImportedFeeSchedulesCount: totalFeeSchedulesCreated,
                TotalCommittedBudget: totalBudgetSum,
                Message: $"Successfully imported {totalFeeSchedulesCreated} fee schedule items across {createdContractIds.Count} site contracts in a single batch transaction.",
                ContractIds: createdContractIds);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            return new BulkFeeScheduleCommitResult(
                Success: false,
                ImportedContractsCount: 0,
                ImportedFeeSchedulesCount: 0,
                TotalCommittedBudget: 0m,
                Message: $"Failed to commit bulk fee schedule import: {ex.Message}",
                ContractIds: []);
        }
    }

    public string GenerateCsvTemplate(string? studyId = null, string? siteId = null)
    {
        string study = string.IsNullOrWhiteSpace(studyId) ? "STUDY-DCT-001" : studyId.Trim();
        string site = string.IsNullOrWhiteSpace(siteId) ? "SITE-101" : siteId.Trim();

        var sb = new StringBuilder();
        sb.AppendLine("StudyId,SiteId,VisitCode,ProcedureCode,ProcedureName,BaseAmount,OverheadPercentage,WithholdingPercentage,PatientStipend,TravelReimbursement,RequiresSdv");
        sb.AppendLine($"{study},{site},V1_SCREENING,PROC-101,Blood Draw & Lab Panel,150.00,25.00,10.00,50.00,25.00,true");
        sb.AppendLine($"{study},{site},V1_SCREENING,PROC-102,12-Lead ECG,120.00,25.00,10.00,25.00,0.00,false");
        sb.AppendLine($"{study},{site},V2_BASELINE,PROC-201,Vital Signs & Physical Exam,200.00,25.00,10.00,75.00,30.00,true");
        sb.AppendLine($"{study},{site},V3_CYCLE_1,PROC-301,Investigational Drug Infusion,350.00,25.00,10.00,100.00,50.00,true");
        return sb.ToString();
    }

    private static BulkFeeScheduleValidationResult ParseAndValidate(TextReader reader)
    {
        var rawRows = SimpleCsvReader.Read(reader).ToList();
        if (rawRows.Count == 0)
        {
            return new BulkFeeScheduleValidationResult(
                TotalRows: 0,
                ValidRowsCount: 0,
                InvalidRowsCount: 0,
                IsValid: false,
                TotalEstimatedBudget: 0m,
                Rows: [],
                GlobalErrors: ["CSV file contains no data."]);
        }

        // Parse header row
        var headerMap = ParseHeader(rawRows[0]);
        if (!headerMap.ContainsKey("studyid") || !headerMap.ContainsKey("siteid") ||
            !headerMap.ContainsKey("visitcode") || !headerMap.ContainsKey("procedurecode"))
        {
            return new BulkFeeScheduleValidationResult(
                TotalRows: 0,
                ValidRowsCount: 0,
                InvalidRowsCount: 0,
                IsValid: false,
                TotalEstimatedBudget: 0m,
                Rows: [],
                GlobalErrors: ["CSV missing required header columns: 'StudyId', 'SiteId', 'VisitCode', 'ProcedureCode'."]);
        }

        var parsedRows = new List<FeeScheduleImportRow>();
        decimal totalBudget = 0m;
        int validCount = 0;
        int invalidCount = 0;

        for (int i = 1; i < rawRows.Count; i++)
        {
            var rowValues = rawRows[i];
            if (rowValues.Count == 0 || (rowValues.Count == 1 && string.IsNullOrWhiteSpace(rowValues[0])))
            {
                continue; // Skip blank lines
            }

            var errors = new List<string>();
            int rowNum = i;

            string studyId = GetValue(rowValues, headerMap, "studyid");
            string siteId = GetValue(rowValues, headerMap, "siteid");
            string visitCode = GetValue(rowValues, headerMap, "visitcode");
            string procedureCode = GetValue(rowValues, headerMap, "procedurecode");
            string procedureName = GetValue(rowValues, headerMap, "procedurename");
            if (string.IsNullOrWhiteSpace(procedureName))
            {
                procedureName = procedureCode;
            }

            if (string.IsNullOrWhiteSpace(studyId))
            {
                errors.Add($"Row {rowNum}: 'StudyId' is required.");
            }
            if (string.IsNullOrWhiteSpace(siteId))
            {
                errors.Add($"Row {rowNum}: 'SiteId' is required.");
            }
            if (string.IsNullOrWhiteSpace(visitCode))
            {
                errors.Add($"Row {rowNum}: 'VisitCode' is required.");
            }
            if (string.IsNullOrWhiteSpace(procedureCode))
            {
                errors.Add($"Row {rowNum}: 'ProcedureCode' is required.");
            }

            decimal baseAmount = ParseDecimal(GetValue(rowValues, headerMap, "baseamount"), 0m, "BaseAmount", rowNum, errors);
            decimal overheadPct = ParseDecimal(GetValue(rowValues, headerMap, "overheadpercentage"), 0m, "OverheadPercentage", rowNum, errors);
            decimal withholdingPct = ParseDecimal(GetValue(rowValues, headerMap, "withholdingpercentage"), 10.0m, "WithholdingPercentage", rowNum, errors);
            decimal patientStipend = ParseDecimal(GetValue(rowValues, headerMap, "patientstipend"), 0m, "PatientStipend", rowNum, errors);
            decimal travelReimb = ParseDecimal(GetValue(rowValues, headerMap, "travelreimbursement"), 0m, "TravelReimbursement", rowNum, errors);
            bool requiresSdv = ParseBool(GetValue(rowValues, headerMap, "requiressdv"), false);

            if (baseAmount < 0)
            {
                errors.Add($"Row {rowNum}: 'BaseAmount' must be non-negative (received {baseAmount}).");
            }
            if (overheadPct < 0 || overheadPct > 100)
            {
                errors.Add($"Row {rowNum}: 'OverheadPercentage' must be between 0% and 100% (received {overheadPct}%).");
            }
            if (withholdingPct < 0 || withholdingPct > 100)
            {
                errors.Add($"Row {rowNum}: 'WithholdingPercentage' must be between 0% and 100% (received {withholdingPct}%).");
            }
            if (patientStipend < 0)
            {
                errors.Add($"Row {rowNum}: 'PatientStipend' must be non-negative.");
            }
            if (travelReimb < 0)
            {
                errors.Add($"Row {rowNum}: 'TravelReimbursement' must be non-negative.");
            }

            // High precision calculations following banker's rounding (ToEven)
            decimal baseQty = ContractCalculations.QuantizeCurrency(baseAmount);
            decimal overheadAmount = ContractCalculations.QuantizeCurrency(baseQty * overheadPct / 100m);
            decimal grossAmount = baseQty + overheadAmount;
            decimal withholdingAmount = ContractCalculations.QuantizeCurrency(grossAmount * withholdingPct / 100m);
            decimal netPayable = grossAmount - withholdingAmount;

            if (errors.Count == 0)
            {
                validCount++;
                totalBudget += grossAmount;
            }
            else
            {
                invalidCount++;
            }

            parsedRows.Add(new FeeScheduleImportRow(
                RowNumber: rowNum,
                StudyId: studyId,
                SiteId: siteId,
                VisitCode: visitCode,
                ProcedureCode: procedureCode,
                ProcedureName: procedureName,
                BaseAmount: baseQty,
                OverheadPercentage: overheadPct,
                WithholdingPercentage: withholdingPct,
                PatientStipend: ContractCalculations.QuantizeCurrency(patientStipend),
                TravelReimbursement: ContractCalculations.QuantizeCurrency(travelReimb),
                RequiresSdv: requiresSdv,
                OverheadAmount: overheadAmount,
                GrossAmount: grossAmount,
                WithholdingAmount: withholdingAmount,
                NetPayable: netPayable,
                ValidationErrors: errors));
        }

        return new BulkFeeScheduleValidationResult(
            TotalRows: parsedRows.Count,
            ValidRowsCount: validCount,
            InvalidRowsCount: invalidCount,
            IsValid: invalidCount == 0 && parsedRows.Count > 0,
            TotalEstimatedBudget: ContractCalculations.QuantizeCurrency(totalBudget),
            Rows: parsedRows,
            GlobalErrors: []);
    }

    private static Dictionary<string, int> ParseHeader(IReadOnlyList<string> headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerRow.Count; i++)
        {
            string col = headerRow[i].Trim().ToLowerInvariant().Replace("_", string.Empty).Replace(" ", string.Empty);
            if (!map.ContainsKey(col))
            {
                map[col] = i;
            }
        }
        return map;
    }

    private static string GetValue(IReadOnlyList<string> row, Dictionary<string, int> map, string key)
    {
        if (map.TryGetValue(key, out int index) && index < row.Count)
        {
            return row[index].Trim();
        }
        return string.Empty;
    }

    private static decimal ParseDecimal(string val, decimal defaultVal, string fieldName, int rowNum, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(val)) return defaultVal;
        if (decimal.TryParse(val, NumberStyles.Number | NumberStyles.Currency, CultureInfo.InvariantCulture, out decimal result))
        {
            return result;
        }
        errors.Add($"Row {rowNum}: Invalid decimal value '{val}' for field '{fieldName}'.");
        return defaultVal;
    }

    private static bool ParseBool(string val, bool defaultVal)
    {
        if (string.IsNullOrWhiteSpace(val)) return defaultVal;
        string clean = val.Trim().ToLowerInvariant();
        return clean is "true" or "1" or "yes" or "y";
    }
}

/// <summary>
/// RFC 4180 compliant CSV parser.
/// </summary>
internal static class SimpleCsvReader
{
    public static IEnumerable<IReadOnlyList<string>> Read(TextReader reader)
    {
        string? line;
        var rowBuffer = new List<string>();
        var currentField = new StringBuilder();
        bool inQuotes = false;

        while ((line = reader.ReadLine()) != null)
        {
            int index = 0;
            while (index < line.Length)
            {
                char ch = line[index];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (index + 1 < line.Length && line[index + 1] == '"')
                        {
                            currentField.Append('"');
                            index += 2;
                            continue;
                        }

                        inQuotes = false;
                    }
                    else
                    {
                        currentField.Append(ch);
                    }
                }
                else if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == ',')
                {
                    rowBuffer.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(ch);
                }

                index++;
            }

            if (!inQuotes)
            {
                rowBuffer.Add(currentField.ToString());
                currentField.Clear();
                yield return rowBuffer;
                rowBuffer = new List<string>();
            }
            else
            {
                currentField.AppendLine();
            }
        }

        if (rowBuffer.Count > 0 || currentField.Length > 0)
        {
            rowBuffer.Add(currentField.ToString());
            yield return rowBuffer;
        }
    }
}
