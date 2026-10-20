using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Database;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Safety.Application;

/// <summary>
/// Compiles, updates and approves DSUR periodic safety reports. Every operation acts as the verified caller the endpoint
/// passes in; there is no default actor, and the caller is the audit actor of the entry each operation writes through
/// <see cref="AuditActions"/> (#5462).
/// </summary>
public class DsurCompilerEngine
{
    /// <summary>The audit chain the report's entries join (the record's CLR type name).</summary>
    public const string AuditEntityName = nameof(PeriodicSafetyReportRecord);

    /// <summary>The audit action of a compilation.</summary>
    public const string CompiledAuditAction = "DSUR_COMPILED";

    /// <summary>The audit action of a draft update.</summary>
    public const string UpdatedAuditAction = "DSUR_UPDATED";

    /// <summary>The audit action of an approval, which signs and locks the report.</summary>
    public const string ApprovedAuditAction = "DSUR_APPROVED";

    private const string DefaultSigningReason = "Approval of ICH E2F DSUR Periodic Safety Report";

    private readonly SafetyDbContext _dbContext;
    private readonly IClock _clock;

    public DsurCompilerEngine(SafetyDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<DsurCompileResult> CompileDsurAsync(
        DsurCompileRequest request,
        string callerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(callerUserId);
        if (string.IsNullOrWhiteSpace(request.CompoundName))
        {
            throw new ArgumentException("CompoundName is required for DSUR compilation.", nameof(request));
        }

        async Task<DsurCompileResult> CompileCoreAsync()
        {
            var exposureMetrics = CalculatePatientYearsExposure(request);
            var (lineListings, summaryTabulations) = ProcessMedDraCrossTabulations(request);

            var fatalCasesCount = lineListings.Count(l => l.IsFatal);
            var susarsCount = lineListings.Count(l => l.IsSerious && string.Equals(l.Expectedness, "UNEXPECTED", StringComparison.OrdinalIgnoreCase) && string.Equals(l.Causality, "CAUSAL", StringComparison.OrdinalIgnoreCase));
            var aesiCount = lineListings.Count(l => l.IsAesi);

            var reportId = Guid.NewGuid().ToString();

            var payload = new DsurReportPayload(
                ReportId: reportId,
                ReportType: "DSUR",
                CompoundName: request.CompoundName,
                StudyId: request.StudyId ?? string.Empty,
                PeriodStart: request.PeriodStart,
                PeriodEnd: request.PeriodEnd,
                ExposureMetrics: exposureMetrics,
                TotalSubjectExposure: exposureMetrics.TotalSubjectsEnrolled,
                TotalSaes: lineListings.Count,
                TotalFatalCases: fatalCasesCount,
                TotalSusars: susarsCount,
                TotalAesi: aesiCount,
                LineListings: lineListings,
                SummaryTabulations: summaryTabulations,
                RsiComparisonSummary: BuildRsiComparisonSummary(summaryTabulations),
                SignalSummary: []
            );

            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

            var record = new PeriodicSafetyReportRecord
            {
                Id = reportId,
                ReportType = "DSUR",
                CompoundName = request.CompoundName,
                StudyId = request.StudyId ?? string.Empty,
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                Status = "DRAFT",
                TotalSubjectExposure = (int)Math.Round(exposureMetrics.TotalPatientYearsExposure),
                TotalSaes = lineListings.Count,
                ReportPayloadJson = payloadJson,
                CreatedAt = _clock.UtcNow,
                CreatedBy = callerUserId,
                ReasonForChange = string.IsNullOrWhiteSpace(request.ReasonForChange) ? "DSUR Automated Aggregation and Compilation" : request.ReasonForChange,
                VersionIndex = 1
            };

            _dbContext.PeriodicSafetyReports.Add(record);
            AuditActions.Record(_dbContext, new AuditAction(
                AuditEntityName, record.Id, CompiledAuditAction, callerUserId, record.ReasonForChange));
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DsurCompileResult(record, payload);
        }

        // One read-committed unit of work inside the execution strategy (#5383); the in-memory provider runs it directly.
        return await _dbContext.ExecuteInTransactionAsync(_ => CompileCoreAsync(), IsolationLevel.ReadCommitted, cancellationToken);
    }

    /// <summary>
    /// Signs and locks a DRAFT report as <paramref name="callerUserId"/>, the verified caller who presented a step-up
    /// signature token. The signer is always the caller; the report's compiler cannot approve it, a LOCKED report
    /// cannot be signed again, and a reason for change is required.
    /// </summary>
    public async Task<DsurApprovalResult> ApproveDsurAsync(
        string reportId,
        DsurApproveRequest request,
        string callerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(callerUserId);

        if (string.IsNullOrWhiteSpace(request.ReasonForChange))
        {
            return new DsurApprovalResult(DsurOperationStatus.Invalid, "A reason for change is required to approve and lock a DSUR.");
        }

        var record = await _dbContext.PeriodicSafetyReports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (record == null)
        {
            return new DsurApprovalResult(DsurOperationStatus.NotFound, "DSUR periodic report record not found.");
        }

        if (string.Equals(record.Status, "LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            return new DsurApprovalResult(DsurOperationStatus.Conflict, "Cannot approve or re-sign a periodic safety report record that is already LOCKED.");
        }

        // Compiling and approving share the medical monitor role, so two people must do them (separation of duties).
        if (string.Equals(record.CreatedBy, callerUserId, StringComparison.Ordinal))
        {
            return new DsurApprovalResult(DsurOperationStatus.Forbidden, "The user who compiled this DSUR cannot also approve it.");
        }

        string signerId = callerUserId;
        string signingReason = string.IsNullOrWhiteSpace(request.SigningReason) ? DefaultSigningReason : request.SigningReason;
        string reasonForChange = request.ReasonForChange;
        var signedAt = _clock.UtcNow;
        var timestamp = signedAt.ToString("yyyy-MM-dd HH:mm:ss UTC", System.Globalization.CultureInfo.InvariantCulture);

        var markdownBody = new StringBuilder()
            .AppendLine("# Development Safety Update Report (DSUR) - Regulatory Approval")
            .AppendLine()
            .AppendLine($" - **Report Identifier:** {record.Id}")
            .AppendLine($" - **Compound Name:** {record.CompoundName}")
            .AppendLine($" - **Study Identifier:** {record.StudyId}")
            .AppendLine($" - **Reporting Period:** {record.PeriodStart:yyyy-MM-dd} to {record.PeriodEnd:yyyy-MM-dd}")
            .AppendLine($" - **Total Patient-Years Exposure:** {record.TotalSubjectExposure}")
            .AppendLine($" - **Total SAE Count:** {record.TotalSaes}")
            .AppendLine($" - **Regulatory Status:** APPROVED & LOCKED")
            .AppendLine()
            .AppendLine("This document is frozen and cryptographically signed in compliance with 21 CFR Part 11.")
            .ToString();

        var signedManifest = GxPSigner.SignGxpMarkdown(
            content: markdownBody,
            signerId: signerId,
            signingReason: signingReason,
            timestamp: timestamp,
            clock: _clock);

        // Verify that ESignatureVerifier validates the signature
        var verifier = new ESignatureVerifier();
        var verificationResult = verifier.VerifySignature(Encoding.UTF8.GetBytes(signedManifest));

        if (!verificationResult.IsValid)
        {
            return new DsurApprovalResult(
                DsurOperationStatus.Error,
                $"Part 11 Cryptographic Signature Verification Failed: {verificationResult.FailureReason}");
        }

        record.EsignatureManifestId = signedManifest;
        record.Status = "LOCKED";
        record.ApprovedByUserId = signerId;
        record.ApprovedAt = signedAt;
        record.ReasonForChange = reasonForChange;
        record.VersionIndex += 1;

        AuditActions.Record(_dbContext, new AuditAction(
            AuditEntityName,
            record.Id,
            ApprovedAuditAction,
            signerId,
            reasonForChange,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["signer_id"] = signerId,
                ["signing_reason"] = signingReason,
                ["signed_at"] = signedAt,
                ["version_index"] = record.VersionIndex,
            })));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DsurApprovalResult(DsurOperationStatus.Success, "DSUR approved and locked.", record, verificationResult);
    }

    public async Task<DsurUpdateResult> UpdateDsurAsync(
        string reportId,
        DsurUpdateRequest request,
        string callerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(callerUserId);

        var record = await _dbContext.PeriodicSafetyReports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (record == null)
        {
            return new DsurUpdateResult(DsurOperationStatus.NotFound, "DSUR periodic report record not found.");
        }

        // Immutability Check: Locked DSUR records must remain immutable
        if (string.Equals(record.Status, "LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            return new DsurUpdateResult(DsurOperationStatus.Conflict, "Cannot edit or update a LOCKED periodic safety report record.");
        }

        // 21 CFR 11.10(e): a change to a regulated record states why it was made; no default reason is filled in.
        if (string.IsNullOrWhiteSpace(request.ReasonForChange))
        {
            return new DsurUpdateResult(DsurOperationStatus.Invalid, "A reason for change is required to update a DSUR.");
        }

        if (!string.IsNullOrWhiteSpace(request.CompoundName))
        {
            record.CompoundName = request.CompoundName;
        }

        if (request.StudyId != null)
        {
            record.StudyId = request.StudyId;
        }

        if (request.PeriodStart.HasValue)
        {
            record.PeriodStart = request.PeriodStart.Value;
        }

        if (request.PeriodEnd.HasValue)
        {
            record.PeriodEnd = request.PeriodEnd.Value;
        }

        record.ReasonForChange = request.ReasonForChange;
        record.VersionIndex += 1;

        AuditActions.Record(_dbContext, new AuditAction(
            AuditEntityName, record.Id, UpdatedAuditAction, callerUserId, record.ReasonForChange));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DsurUpdateResult(DsurOperationStatus.Success, "DSUR updated.", record);
    }

    public static SubjectExposureMetrics CalculatePatientYearsExposure(DsurCompileRequest request)
    {
        var enrollments = request.SubjectEnrollments ?? [];
        if (enrollments.Count == 0)
        {
            return new SubjectExposureMetrics(0, 0.0, 0.0, 0.0, []);
        }

        var details = new List<SubjectExposureDetail>();
        double totalPatientYears = 0.0;
        double investigationalPatientYears = 0.0;
        double placeboPatientYears = 0.0;

        foreach (var sub in enrollments)
        {
            var start = sub.EnrollmentDate > request.PeriodStart ? sub.EnrollmentDate : request.PeriodStart;
            var effectiveEnd = sub.DiscontinuationDate.HasValue && sub.DiscontinuationDate.Value < request.PeriodEnd
                ? sub.DiscontinuationDate.Value
                : request.PeriodEnd;

            double exposureDays = 0.0;
            if (effectiveEnd > start)
            {
                exposureDays = (effectiveEnd - start).TotalDays;
            }

            double patientYears = Math.Round(exposureDays / 365.25, 4);

            totalPatientYears += patientYears;

            var arm = sub.TreatmentArm ?? "Investigational Drug";
            if (arm.Contains("Placebo", StringComparison.OrdinalIgnoreCase))
            {
                placeboPatientYears += patientYears;
            }
            else
            {
                investigationalPatientYears += patientYears;
            }

            details.Add(new SubjectExposureDetail(
                SubjectId: sub.SubjectId,
                StudyId: sub.StudyId,
                TreatmentArm: arm,
                StartDate: start,
                EndDate: effectiveEnd,
                ExposureDays: Math.Round(exposureDays, 2),
                PatientYears: patientYears
            ));
        }

        return new SubjectExposureMetrics(
            TotalSubjectsEnrolled: enrollments.Count,
            TotalPatientYearsExposure: Math.Round(totalPatientYears, 4),
            InvestigationalArmPatientYears: Math.Round(investigationalPatientYears, 4),
            PlaceboArmPatientYears: Math.Round(placeboPatientYears, 4),
            ExposureDetails: details
        );
    }

    private static (List<DsurLineListingItem> LineListings, List<DsurSummaryTabulationRow> SummaryTabulations)
        ProcessMedDraCrossTabulations(DsurCompileRequest request)
    {
        var cases = request.SafetyCases ?? [];

        // De-identify patient demographics in safety cases
        var lineListings = cases.Select(c => new DsurLineListingItem(
            CaseId: c.CaseId,
            SubjectId: MaskSubjectId(c.SubjectId),
            StudyId: c.StudyId,
            TreatmentArm: string.IsNullOrWhiteSpace(c.TreatmentArm) ? "Investigational Drug" : c.TreatmentArm,
            DosageCohort: null,
            Age: null,
            Sex: null,
            AeTerm: c.AeTerm,
            MeddraPt: c.MeddraPt,
            MeddraSoc: c.MeddraSoc,
            OnsetDate: c.OnsetDate,
            ResolutionDate: c.ResolutionDate,
            IsSerious: c.IsSerious,
            IsFatal: c.IsFatal,
            IsAesi: c.IsAesi,
            AesiCategory: c.AesiCategory,
            Causality: string.IsNullOrWhiteSpace(c.Causality) ? "CAUSAL" : c.Causality,
            Expectedness: string.IsNullOrWhiteSpace(c.Expectedness) ? "UNEXPECTED" : c.Expectedness,
            ActionTaken: string.IsNullOrWhiteSpace(c.ActionTaken) ? "DRUG_WITHDRAWN" : c.ActionTaken
        )).ToList();

        // Cross-tabulation by MedDRA SOC and PT against Reference Safety Information (RSI)
        var tabulations = lineListings
            .GroupBy(l => (l.MeddraSoc, l.MeddraPt))
            .Select(g =>
            {
                var investigationalCount = g.Count(x => !x.TreatmentArm.Contains("Placebo", StringComparison.OrdinalIgnoreCase) && !x.TreatmentArm.Contains("Comparator", StringComparison.OrdinalIgnoreCase));
                var placeboCount = g.Count(x => x.TreatmentArm.Contains("Placebo", StringComparison.OrdinalIgnoreCase));
                var comparatorCount = g.Count(x => x.TreatmentArm.Contains("Comparator", StringComparison.OrdinalIgnoreCase));
                var totalCount = g.Count();

                var isFatal = g.Any(x => x.IsFatal);
                var isAesi = g.Any(x => x.IsAesi);
                var aesiCategory = g.Select(x => x.AesiCategory).FirstOrDefault(cat => !string.IsNullOrEmpty(cat));

                // Reference Safety Information expectedness status
                var rsiStatus = g.All(x => string.Equals(x.Expectedness, "EXPECTED", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Expectedness, "LISTED", StringComparison.OrdinalIgnoreCase))
                    ? "EXPECTED"
                    : "UNEXPECTED";

                return new DsurSummaryTabulationRow(
                    MeddraSoc: g.Key.MeddraSoc,
                    MeddraPt: g.Key.MeddraPt,
                    IsAesi: isAesi,
                    AesiCategory: aesiCategory,
                    IsFatal: isFatal,
                    InvestigationalDrugCount: investigationalCount,
                    PlaceboCount: placeboCount,
                    ComparatorCount: comparatorCount,
                    TotalCount: totalCount,
                    RsiStatus: rsiStatus
                );
            })
            .OrderBy(t => t.MeddraSoc)
            .ThenBy(t => t.MeddraPt)
            .ToList();

        return (lineListings, tabulations);
    }

    private static List<IReadOnlyDictionary<string, object>> BuildRsiComparisonSummary(List<DsurSummaryTabulationRow> tabulations)
    {
        return tabulations.Select(t => (IReadOnlyDictionary<string, object>)new Dictionary<string, object>
        {
            ["meddra_soc"] = t.MeddraSoc,
            ["meddra_pt"] = t.MeddraPt,
            ["rsi_status"] = t.RsiStatus,
            ["total_occurrences"] = t.TotalCount,
            ["investigational_drug_count"] = t.InvestigationalDrugCount
        }).ToList();
    }

    private static string MaskSubjectId(string rawSubjectId)
    {
        if (string.IsNullOrWhiteSpace(rawSubjectId))
        {
            return "SUBJ-DEID-ANON";
        }

        if (rawSubjectId.StartsWith("SUBJ-", StringComparison.OrdinalIgnoreCase) ||
            rawSubjectId.StartsWith("PT-", StringComparison.OrdinalIgnoreCase))
        {
            return rawSubjectId;
        }

        return $"SUBJ-{rawSubjectId.GetHashCode():X8}";
    }
}

public enum DsurOperationStatus
{
    Success,
    NotFound,
    Conflict,
    Error,
    Invalid,
    Forbidden
}

public record DsurCompileResult(PeriodicSafetyReportRecord Record, DsurReportPayload Payload);

public record DsurApprovalResult(
    DsurOperationStatus Status,
    string Message,
    PeriodicSafetyReportRecord? Record = null,
    VerificationResult? VerificationResult = null
);

public record DsurUpdateResult(
    DsurOperationStatus Status,
    string Message,
    PeriodicSafetyReportRecord? Record = null
);
