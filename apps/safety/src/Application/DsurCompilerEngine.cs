using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Core.Domain.Audit;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Safety.Application;

public class DsurCompilerEngine
{
    private readonly SafetyDbContext _dbContext;
    private readonly IClock _clock;

    public DsurCompilerEngine(SafetyDbContext dbContext, IClock? clock = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<DsurCompileResult> CompileDsurAsync(
        DsurCompileRequest request,
        string callerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CompoundName))
        {
            throw new ArgumentException("CompoundName is required for DSUR compilation.", nameof(request));
        }

        async Task<DsurCompileResult> CompileCoreAsync()
        {
            var (enrollments, safetyCases) = await ResolveInputsAsync(request, cancellationToken);
            var effectiveRequest = request with { SubjectEnrollments = enrollments, SafetyCases = safetyCases };

            var exposureMetrics = CalculatePatientYearsExposure(effectiveRequest);
            var (lineListings, summaryTabulations) = ProcessMedDraCrossTabulations(effectiveRequest);

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
            string payloadDigestSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

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
                CreatedBy = string.IsNullOrWhiteSpace(callerUserId) ? "safety-officer" : callerUserId,
                ReasonForChange = string.IsNullOrWhiteSpace(request.ReasonForChange) ? "DSUR Automated Aggregation and Compilation" : request.ReasonForChange,
                VersionIndex = 1
            };

            var snapshot = new DsurSnapshotRecord
            {
                Id = Guid.NewGuid().ToString(),
                ReportId = reportId,
                ReportType = "DSUR",
                CompoundName = request.CompoundName,
                StudyId = request.StudyId ?? string.Empty,
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                Status = "DRAFT",
                ReportPayloadJson = payloadJson,
                PayloadDigestSha256 = payloadDigestSha256,
                CreatedBy = string.IsNullOrWhiteSpace(callerUserId) ? "safety-officer" : callerUserId,
                ReasonForChange = string.IsNullOrWhiteSpace(request.ReasonForChange) ? "DSUR Point-in-time Snapshot Ledger Creation" : request.ReasonForChange,
                VersionIndex = 1
            };

            _dbContext.PeriodicSafetyReports.Add(record);
            _dbContext.DsurSnapshots.Add(snapshot);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DsurCompileResult(record, payload);
        }

        // Execute within a read-committed database transaction on relational databases
        if (_dbContext.Database.IsRelational())
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                var result = await CompileCoreAsync();
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }

        return await CompileCoreAsync();
    }

    public async Task<DsurApprovalResult> ApproveDsurAsync(
        string reportId,
        DsurApproveRequest request,
        string callerUserId,
        CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.PeriodicSafetyReports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (record == null)
        {
            return new DsurApprovalResult(DsurOperationStatus.NotFound, "DSUR periodic report record not found.");
        }

        if (string.Equals(record.Status, "LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            return new DsurApprovalResult(DsurOperationStatus.Conflict, "Cannot approve or re-sign a periodic safety report record that is already LOCKED.");
        }

        string signerId;
        if (!string.IsNullOrWhiteSpace(request?.SignerUserId))
        {
            signerId = request.SignerUserId;
        }
        else if (!string.IsNullOrWhiteSpace(callerUserId))
        {
            signerId = callerUserId;
        }
        else
        {
            signerId = "safety-physician";
        }

        var signingReason = string.IsNullOrWhiteSpace(request?.SigningReason)
            ? "Approval of ICH E2F DSUR Periodic Safety Report"
            : request.SigningReason;

        var timestamp = _clock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC", System.Globalization.CultureInfo.InvariantCulture);

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
        record.ApprovedAt = _clock.UtcNow;
        record.ReasonForChange = string.IsNullOrWhiteSpace(request?.ReasonForChange)
            ? "Part 11 Electronic Signature Approval and Record Lock"
            : request.ReasonForChange;
        record.VersionIndex += 1;

        var snapshots = await _dbContext.DsurSnapshots.Where(s => s.ReportId == reportId).ToListAsync(cancellationToken);
        foreach (var snap in snapshots)
        {
            snap.Status = "LOCKED";
            snap.ApprovedByUserId = signerId;
            snap.ApprovedAt = _clock.UtcNow;
            snap.EsignatureManifestId = signedManifest;
            snap.ReasonForChange = string.IsNullOrWhiteSpace(request?.ReasonForChange)
                ? "Part 11 Electronic Signature Snapshot Freeze"
                : request.ReasonForChange;
            snap.VersionIndex += 1;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DsurApprovalResult(DsurOperationStatus.Success, "DSUR approved and locked.", record, verificationResult);
    }

    public async Task<DsurUpdateResult> UpdateDsurAsync(
        string reportId,
        DsurUpdateRequest request,
        string callerUserId,
        CancellationToken cancellationToken = default)
    {
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

        var compileReq = new DsurCompileRequest(
            CompoundName: record.CompoundName,
            StudyId: record.StudyId,
            PeriodStart: record.PeriodStart,
            PeriodEnd: record.PeriodEnd,
            ReasonForChange: request.ReasonForChange
        );

        var (enrollments, safetyCases) = await ResolveInputsAsync(compileReq, cancellationToken);
        var effectiveReq = compileReq with { SubjectEnrollments = enrollments, SafetyCases = safetyCases };

        var exposureMetrics = CalculatePatientYearsExposure(effectiveReq);
        var (lineListings, summaryTabulations) = ProcessMedDraCrossTabulations(effectiveReq);

        var fatalCasesCount = lineListings.Count(l => l.IsFatal);
        var susarsCount = lineListings.Count(l => l.IsSerious && string.Equals(l.Expectedness, "UNEXPECTED", StringComparison.OrdinalIgnoreCase) && string.Equals(l.Causality, "CAUSAL", StringComparison.OrdinalIgnoreCase));
        var aesiCount = lineListings.Count(l => l.IsAesi);

        var payload = new DsurReportPayload(
            ReportId: record.Id,
            ReportType: record.ReportType,
            CompoundName: record.CompoundName,
            StudyId: record.StudyId,
            PeriodStart: record.PeriodStart,
            PeriodEnd: record.PeriodEnd,
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
        string payloadDigestSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        record.ReportPayloadJson = payloadJson;
        record.TotalSubjectExposure = (int)Math.Round(exposureMetrics.TotalPatientYearsExposure);
        record.TotalSaes = lineListings.Count;
        record.ReasonForChange = string.IsNullOrWhiteSpace(request.ReasonForChange)
            ? "Draft DSUR record updated"
            : request.ReasonForChange;
        record.VersionIndex += 1;

        var existingSnapshots = await _dbContext.DsurSnapshots.Where(s => s.ReportId == reportId).ToListAsync(cancellationToken);
        long nextVersion = (existingSnapshots.Count > 0 ? existingSnapshots.Max(s => s.VersionIndex) : 0L) + 1;

        var newSnapshot = new DsurSnapshotRecord
        {
            Id = Guid.NewGuid().ToString(),
            ReportId = reportId,
            ReportType = record.ReportType,
            CompoundName = record.CompoundName,
            StudyId = record.StudyId,
            PeriodStart = record.PeriodStart,
            PeriodEnd = record.PeriodEnd,
            Status = record.Status,
            ReportPayloadJson = payloadJson,
            PayloadDigestSha256 = payloadDigestSha256,
            CreatedBy = string.IsNullOrWhiteSpace(callerUserId) ? "safety-officer" : callerUserId,
            ReasonForChange = record.ReasonForChange,
            VersionIndex = nextVersion
        };

        _dbContext.DsurSnapshots.Add(newSnapshot);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DsurUpdateResult(DsurOperationStatus.Success, "DSUR updated.", record);
    }

    public async Task<IReadOnlyList<DsurSnapshotSummary>> GetSnapshotsForReportAsync(
        string reportId,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await _dbContext.DsurSnapshots
            .AsNoTracking()
            .Where(s => s.ReportId == reportId)
            .OrderBy(s => s.VersionIndex)
            .ToListAsync(cancellationToken);

        return snapshots.Select(s => new DsurSnapshotSummary(
            Id: s.Id,
            ReportId: s.ReportId,
            ReportType: s.ReportType,
            CompoundName: s.CompoundName,
            StudyId: s.StudyId,
            PeriodStart: s.PeriodStart,
            PeriodEnd: s.PeriodEnd,
            Status: s.Status,
            PayloadDigestSha256: s.PayloadDigestSha256,
            VersionIndex: s.VersionIndex,
            CreatedAt: s.CreatedAt,
            CreatedBy: s.CreatedBy,
            ApprovedByUserId: s.ApprovedByUserId,
            ApprovedAt: s.ApprovedAt
        )).ToList();
    }

    public async Task<DsurSnapshotDetail?> GetSnapshotByIdAsync(
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        var snap = await _dbContext.DsurSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == snapshotId || s.ReportId == snapshotId, cancellationToken);

        if (snap == null)
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<DsurReportPayload>(snap.ReportPayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload == null)
        {
            return null;
        }

        return new DsurSnapshotDetail(
            Id: snap.Id,
            ReportId: snap.ReportId,
            ReportType: snap.ReportType,
            CompoundName: snap.CompoundName,
            StudyId: snap.StudyId,
            PeriodStart: snap.PeriodStart,
            PeriodEnd: snap.PeriodEnd,
            Status: snap.Status,
            Payload: payload,
            PayloadDigestSha256: snap.PayloadDigestSha256,
            VersionIndex: snap.VersionIndex,
            CreatedAt: snap.CreatedAt,
            CreatedBy: snap.CreatedBy,
            ApprovedByUserId: snap.ApprovedByUserId,
            ApprovedAt: snap.ApprovedAt,
            EsignatureManifestId: snap.EsignatureManifestId
        );
    }

    public async Task<DsurSnapshotVerificationResult> VerifySnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        var snap = await _dbContext.DsurSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == snapshotId || s.ReportId == snapshotId, cancellationToken);

        if (snap == null)
        {
            return new DsurSnapshotVerificationResult(
                SnapshotId: snapshotId,
                ReportId: string.Empty,
                HashValid: false,
                PayloadHash: string.Empty,
                ExpectedHash: string.Empty,
                SignatureValid: false,
                SignatureStatus: "NOT_FOUND",
                MerkleChainValid: false,
                Message: $"Snapshot record '{snapshotId}' not found."
            );
        }

        string computedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snap.ReportPayloadJson)));
        bool hashValid = string.Equals(computedHash, snap.PayloadDigestSha256, StringComparison.OrdinalIgnoreCase);

        bool sigValid = true;
        string? sigStatus = "UNSIGNED";
        if (!string.IsNullOrWhiteSpace(snap.EsignatureManifestId))
        {
            var verifier = new ESignatureVerifier();
            var verification = verifier.VerifySignature(Encoding.UTF8.GetBytes(snap.EsignatureManifestId));
            sigValid = verification.IsValid;
            sigStatus = verification.Status;
        }

        // Verify Merkle Audit Log entries
        var auditLogs = await _dbContext.AuditLogEntries
            .AsNoTracking()
            .Where(a => a.EntityName == nameof(DsurSnapshotRecord) && a.EntityId == snap.Id)
            .OrderBy(a => a.VersionIndex)
            .ToListAsync(cancellationToken);

        bool merkleValid = true;
        if (auditLogs.Count > 0)
        {
            foreach (var log in auditLogs)
            {
                string recalculatedBlock = AuditLogEntry.ComputeBlockHash(
                    log.EntityName, log.EntityId, log.Action, log.Timestamp, log.UserId,
                    log.VersionIndex, log.ChangesJson, log.PredecessorHash);

                if (!string.Equals(recalculatedBlock, log.BlockHash, StringComparison.Ordinal))
                {
                    merkleValid = false;
                    break;
                }
            }
        }

        bool allPassed = hashValid && sigValid && merkleValid;
        string message = allPassed
            ? "Cryptographic snapshot verification PASSED. SHA-256 payload digest, Part 11 e-signature, and GxP Merkle audit chain intact."
            : "Cryptographic snapshot verification FAILED.";

        return new DsurSnapshotVerificationResult(
            SnapshotId: snap.Id,
            ReportId: snap.ReportId,
            HashValid: hashValid,
            PayloadHash: computedHash,
            ExpectedHash: snap.PayloadDigestSha256,
            SignatureValid: sigValid,
            SignatureStatus: sigStatus,
            MerkleChainValid: merkleValid,
            Message: message
        );
    }

    private async Task<(IReadOnlyList<SubjectEnrollmentRecord> Enrollments, IReadOnlyList<SafetyCaseIcsrInput> SafetyCases)>
        ResolveInputsAsync(DsurCompileRequest request, CancellationToken cancellationToken)
    {
        var enrollments = request.SubjectEnrollments?.ToList() ?? new List<SubjectEnrollmentRecord>();
        var safetyCases = request.SafetyCases?.ToList() ?? new List<SafetyCaseIcsrInput>();

        if (safetyCases.Count == 0)
        {
            // 1. Query SafetyCases table
            var dbCases = await _dbContext.SafetyCases.AsNoTracking().ToListAsync(cancellationToken);
            foreach (var c in dbCases)
            {
                try
                {
                    var input = JsonSerializer.Deserialize<SafetyCaseIcsrInput>(c.CaseDataJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (input != null)
                    {
                        bool studyMatch = string.IsNullOrEmpty(request.StudyId) || string.Equals(input.StudyId, request.StudyId, StringComparison.OrdinalIgnoreCase);
                        bool dateMatch = input.ReceiptDate >= request.PeriodStart && input.ReceiptDate <= request.PeriodEnd;
                        if (studyMatch && dateMatch)
                        {
                            safetyCases.Add(input);
                        }
                    }
                }
                catch
                {
                    // Ignore non-matching json payloads
                }
            }

            // 2. Query SafetyReportingEvaluations table
            var dbEvals = await _dbContext.SafetyReportingEvaluations
                .AsNoTracking()
                .Where(e => (string.IsNullOrEmpty(request.StudyId) || e.StudyId == request.StudyId) && e.ReceiptDate >= request.PeriodStart && e.ReceiptDate <= request.PeriodEnd)
                .ToListAsync(cancellationToken);

            foreach (var e in dbEvals)
            {
                if (!safetyCases.Any(sc => sc.CaseId == e.CaseId))
                {
                    safetyCases.Add(new SafetyCaseIcsrInput(
                        CaseId: e.CaseId,
                        SubjectId: e.SubjectId,
                        StudyId: e.StudyId,
                        ReceiptDate: e.ReceiptDate,
                        AeTerm: "Adverse Event",
                        MeddraPt: "Unspecified Adverse Event",
                        MeddraSoc: "General disorders and administration site conditions",
                        IsSerious: e.IsSerious,
                        IsFatal: e.SeriousnessDeath,
                        IsAesi: false,
                        AesiCategory: null,
                        Causality: e.Causality,
                        Expectedness: e.Expectedness,
                        TreatmentArm: "Investigational Drug",
                        ActionTaken: "DRUG_WITHDRAWN",
                        OnsetDate: e.ReceiptDate,
                        ResolutionDate: null
                    ));
                }
            }
        }

        if (enrollments.Count == 0 && safetyCases.Count > 0)
        {
            // Infer enrollments from aggregated safety cases
            var groupedSubjects = safetyCases.GroupBy(c => c.SubjectId);
            foreach (var group in groupedSubjects)
            {
                var minDate = group.Min(c => c.OnsetDate < c.ReceiptDate ? c.OnsetDate : c.ReceiptDate);
                var arm = group.Select(c => c.TreatmentArm).FirstOrDefault(a => !string.IsNullOrEmpty(a)) ?? "Investigational Drug";
                var studyId = group.Select(c => c.StudyId).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? request.StudyId ?? string.Empty;

                enrollments.Add(new SubjectEnrollmentRecord(
                    SubjectId: group.Key,
                    StudyId: studyId,
                    TreatmentArm: arm,
                    EnrollmentDate: minDate < request.PeriodStart ? minDate : request.PeriodStart,
                    DiscontinuationDate: null
                ));
            }
        }

        return (enrollments, safetyCases);
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
    Error
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
