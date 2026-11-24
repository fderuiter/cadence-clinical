using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Domain.Ledger;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Database;
using Cadence.Kernel;
using Cadence.Kernel.Time;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Payments.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="ClinicalVisitCompleted"/> events.
/// Automates investigator payment milestone calculations via the transactional outbox and <see cref="PayableLedger"/>.
/// @req:PRD-SYS-001
/// </summary>
public sealed class ClinicalVisitCompletedConsumer(
    PaymentsDbContext dbContext,
    IClock clock,
    ILogger<ClinicalVisitCompletedConsumer>? logger = null) : IConsumer<ClinicalVisitCompleted>
{
    private const string SystemUserId = "svc:payments-consumer";

    public async Task Consume(ConsumeContext<ClinicalVisitCompleted> context)
    {
        using var _ = StandaloneAuditActorScope.Begin(SystemUserId);
        var msg = context.Message;

        if (string.IsNullOrWhiteSpace(msg.EventId) || string.IsNullOrWhiteSpace(msg.Study) || string.IsNullOrWhiteSpace(msg.Site))
        {
            logger?.LogWarning("Received malformed ClinicalVisitCompleted event with missing EventId, Study, or Site.");
            return;
        }

        string triggerEventId = msg.EventId;
        DateTimeOffset now = clock.UtcNow;

        // Requirement 5 & Criterion 2: Verify event idempotency by checking TriggerEventId before processing
        bool alreadyProcessed = await dbContext.Payables
            .AnyAsync(p => p.TriggerEventId == triggerEventId, context.CancellationToken);

        if (alreadyProcessed)
        {
            logger?.LogInformation("Clinical visit event with TriggerEventId {TriggerEventId} has already been processed. Skipping duplicate.", triggerEventId);
            return;
        }

        // Requirement 2: Load the active BudgetContract for the study and site
        var contract = await dbContext.BudgetContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StudyId == msg.Study && c.SiteId == msg.Site && c.IsActive, context.CancellationToken);

        if (contract is null)
        {
            logger?.LogWarning("No active budget contract found for study {Study} and site {Site}.", msg.Study, msg.Site);

            var contractNotFoundAudit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "BudgetContract",
                EntityId = $"{msg.Study}:{msg.Site}",
                Action = "CONTRACT_NOT_FOUND",
                UserId = string.IsNullOrWhiteSpace(msg.Signer) ? SystemUserId : msg.Signer,
                UserRoles = "system",
                Details = JsonSerializer.Serialize(new
                {
                    trigger_event_id = triggerEventId,
                    study_id = msg.Study,
                    site_id = msg.Site,
                    visit_code = msg.VisitCode,
                    reason = $"No active budget contract found for study '{msg.Study}' and site '{msg.Site}'."
                }),
                CreatedAt = now,
                CreatedBy = SystemUserId,
                ReasonForChange = "Automated visit fee processing failed: contract not found",
                VersionIndex = 1
            };
            dbContext.PaymentAuditLogs.Add(contractNotFoundAudit);
            await dbContext.SaveChangesAsync(context.CancellationToken);
            return;
        }

        // Requirement 2: Load matching FeeSchedule and SplitPayeeConfig rows
        var feeSchedules = await dbContext.FeeSchedules
            .AsNoTracking()
            .Where(f => f.ContractId == contract.Id && f.IsActive)
            .ToListAsync(context.CancellationToken);

        var splitConfigs = await dbContext.SplitPayeeConfigs
            .AsNoTracking()
            .Where(s => s.ContractId == contract.Id && s.IsActive)
            .ToListAsync(context.CancellationToken);

        // Map to domain GrantContract for PayableLedger
        var studyId = StudyId.TryParse(contract.StudyId, out var parsedStudy)
            ? parsedStudy
            : new StudyId(new Guid(MD5.HashData(Encoding.UTF8.GetBytes(contract.StudyId))));

        var siteId = SiteId.TryParse(contract.SiteId, out var parsedSite)
            ? parsedSite
            : new SiteId(new Guid(MD5.HashData(Encoding.UTF8.GetBytes(contract.SiteId))));

        decimal overheadRate = contract.OverheadPercentage > 1m
            ? contract.OverheadPercentage / 100m
            : contract.OverheadPercentage;

        decimal withholdingRate = contract.WithholdingPercentage > 1m
            ? contract.WithholdingPercentage / 100m
            : contract.WithholdingPercentage;

        var payeeSplits = splitConfigs.Select((s, index) =>
        {
            decimal pct = s.AllocationPercentage > 1m ? s.AllocationPercentage / 100m : s.AllocationPercentage;
            return new PayeeSplit(s.PayeeId, s.PayeeName, pct, IsPrimary: index == 0, s.BankAccountReference);
        }).ToList();

        var feeMatrixItems = feeSchedules.Select(f =>
            new FeeMatrixItem(
                ItemCode: string.IsNullOrWhiteSpace(f.ProcedureCode) ? f.VisitCode : f.ProcedureCode,
                Description: string.IsNullOrWhiteSpace(f.ProcedureName) ? f.VisitCode : f.ProcedureName,
                TriggerType: PaymentTriggerType.VisitCompleted,
                BaseAmount: f.BaseAmount,
                EventIdentifier: f.VisitCode,
                OverheadApplicable: f.OverheadPercentage > 0,
                WithholdingApplicable: f.WithholdingPercentage > 0,
                HoldbackApplicable: true
            )).ToList();

        var grantContract = new GrantContract
        {
            ContractId = contract.Id,
            StudyId = studyId,
            SiteId = siteId,
            Currency = string.IsNullOrWhiteSpace(contract.Currency) ? "USD" : contract.Currency,
            EffectiveDate = contract.CreatedAt,
            DefaultOverheadRate = overheadRate,
            DefaultWithholdingRate = withholdingRate,
            HoldbackRule = new HoldbackReserveRule(0.10m),
            PayeeSplits = payeeSplits,
            FeeMatrix = feeMatrixItems,
            IsActive = contract.IsActive
        };

        // Requirement 3: Map event data to a PaymentEvent and invoke PayableLedger.ProcessEvent
        ParticipantId? participantId = null;
        if (!string.IsNullOrWhiteSpace(msg.SubjectId))
        {
            participantId = ParticipantId.TryParse(msg.SubjectId, out var pId)
                ? pId
                : new ParticipantId(new Guid(MD5.HashData(Encoding.UTF8.GetBytes(msg.SubjectId))));
        }

        var paymentEvent = new PaymentEvent
        {
            EventId = triggerEventId,
            TriggerType = PaymentTriggerType.VisitCompleted,
            OccurredAt = msg.CompletedAt != default ? msg.CompletedAt : now,
            ParticipantId = participantId,
            VisitId = msg.VisitCode,
            Identifier = msg.VisitCode
        };

        var ledger = new PayableLedger(grantContract, clock);
        var lineItems = ledger.ProcessEvent(paymentEvent);

        // Criterion 3: Unmatched visit codes log an explanatory audit record and raise no unhandled exceptions
        if (lineItems.Count == 0)
        {
            logger?.LogInformation("Visit code '{VisitCode}' did not match any active fee schedule row for contract '{ContractId}'.", msg.VisitCode, contract.Id);

            var unmatchedAudit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "FeeSchedule",
                EntityId = msg.VisitCode,
                Action = "UNMATCHED_VISIT_CODE",
                UserId = string.IsNullOrWhiteSpace(msg.Signer) ? SystemUserId : msg.Signer,
                UserRoles = "system",
                Details = JsonSerializer.Serialize(new
                {
                    trigger_event_id = triggerEventId,
                    study_id = msg.Study,
                    site_id = msg.Site,
                    visit_code = msg.VisitCode,
                    contract_id = contract.Id,
                    message = $"No active fee schedule matching visit code '{msg.VisitCode}' found for contract '{contract.Id}'."
                }),
                CreatedAt = now,
                CreatedBy = SystemUserId,
                ReasonForChange = $"Unmatched visit code '{msg.VisitCode}' during automated payment milestone processing",
                VersionIndex = 1
            };
            dbContext.PaymentAuditLogs.Add(unmatchedAudit);
            await dbContext.SaveChangesAsync(context.CancellationToken);
            return;
        }

        // Requirement 4 & Criterion 1: Persist generated PayableLineItem instances as Payable, PayableAllocation, and HoldbackReserve
        var groupedByItem = lineItems.GroupBy(l => l.ItemCode);
        var createdPayables = new List<Payable>();

        foreach (var itemGroup in groupedByItem)
        {
            var first = itemGroup.First();
            decimal totalBase = itemGroup.Sum(l => l.Breakdown.BaseAmount);
            decimal totalOverhead = itemGroup.Sum(l => l.Breakdown.OverheadAmount);
            decimal totalGross = itemGroup.Sum(l => l.Breakdown.GrossAmount);
            decimal totalWithholding = itemGroup.Sum(l => l.Breakdown.WithholdingAmount);
            decimal totalHoldback = itemGroup.Sum(l => l.Breakdown.HoldbackAmount);
            decimal totalNet = itemGroup.Sum(l => l.Breakdown.NetPayable);

            var matchingFee = feeSchedules.FirstOrDefault(f => f.VisitCode == msg.VisitCode || f.ProcedureCode == first.ItemCode);

            var payable = new Payable
            {
                Id = Guid.NewGuid().ToString(),
                StudyId = msg.Study,
                SiteId = msg.Site,
                ContractId = contract.Id,
                SubjectId = msg.SubjectId ?? string.Empty,
                VisitCode = msg.VisitCode,
                ProcedureCode = first.ItemCode,
                ProcedureName = first.ItemDescription,
                TriggerType = "VISIT_COMPLETED",
                TriggerEventId = triggerEventId,
                TriggerSource = "CTMS_SERVICE",
                BaseAmount = totalBase,
                OverheadAmount = totalOverhead,
                PatientStipendAmount = matchingFee?.PatientStipend ?? 0m,
                TravelReimbursementAmount = matchingFee?.TravelReimbursement ?? 0m,
                GrossAmount = totalGross,
                WithholdingRate = contract.WithholdingPercentage,
                WithholdingAmount = totalWithholding,
                NetAmount = totalNet,
                Currency = contract.Currency,
                Status = "TRIGGERED",
                CreatedAt = now,
                CreatedBy = SystemUserId,
                UpdatedAt = now,
                ReasonForChange = $"Automated visit fee payable generated for {msg.VisitCode}",
                VersionIndex = 1
            };

            dbContext.Payables.Add(payable);
            createdPayables.Add(payable);

            foreach (var line in itemGroup)
            {
                var allocation = new PayableAllocation
                {
                    Id = Guid.NewGuid().ToString(),
                    PayableId = payable.Id,
                    PayeeId = line.PayeeId,
                    PayeeName = line.PayeeName,
                    PayeeRole = splitConfigs.FirstOrDefault(s => s.PayeeId == line.PayeeId)?.PayeeRole ?? "PAYEE",
                    AllocationPercentage = line.SplitPercentage > 1m ? line.SplitPercentage : line.SplitPercentage * 100m,
                    GrossShare = line.Breakdown.GrossAmount,
                    WithholdingShare = line.Breakdown.WithholdingAmount,
                    NetShare = line.Breakdown.NetPayable,
                    Currency = line.Currency,
                    Status = "ALLOCATED",
                    CreatedAt = now,
                    CreatedBy = SystemUserId,
                    UpdatedAt = now,
                    ReasonForChange = "Automated payee allocation generated from visit event",
                    VersionIndex = 1
                };
                dbContext.PayableAllocations.Add(allocation);
            }

            if (totalHoldback > 0)
            {
                var holdback = new HoldbackReserve
                {
                    Id = Guid.NewGuid().ToString(),
                    StudyId = msg.Study,
                    SiteId = msg.Site,
                    ContractId = contract.Id,
                    PayableId = payable.Id,
                    HoldbackAmount = totalHoldback,
                    Currency = contract.Currency,
                    Status = "HELD",
                    CreatedAt = now,
                    CreatedBy = SystemUserId,
                    UpdatedAt = now,
                    ReasonForChange = $"Automated holdback retention escrow for visit payable {payable.Id}",
                    VersionIndex = 1
                };
                dbContext.HoldbackReserves.Add(holdback);
            }
        }

        // Requirement 6: Log an immutable entry in PaymentAuditLogs for each processed milestone
        var auditEntry = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "Payable",
            EntityId = string.Join(",", createdPayables.Select(p => p.Id)),
            Action = "MILESTONE_PAYABLE_GENERATED",
            UserId = string.IsNullOrWhiteSpace(msg.Signer) ? SystemUserId : msg.Signer,
            UserRoles = "system",
            Details = JsonSerializer.Serialize(new
            {
                trigger_event_id = triggerEventId,
                study_id = msg.Study,
                site_id = msg.Site,
                visit_code = msg.VisitCode,
                contract_id = contract.Id,
                payables_count = createdPayables.Count,
                total_net_amount = createdPayables.Sum(p => p.NetAmount)
            }),
            CreatedAt = now,
            CreatedBy = SystemUserId,
            ReasonForChange = $"Automated investigator payment milestone generated for visit {msg.VisitCode}",
            VersionIndex = 1
        };
        dbContext.PaymentAuditLogs.Add(auditEntry);

        await dbContext.SaveChangesAsync(context.CancellationToken);

        logger?.LogInformation("Successfully generated {Count} payables for visit milestone {VisitCode} (TriggerEventId: {TriggerEventId}).",
            createdPayables.Count, msg.VisitCode, triggerEventId);
    }
}
