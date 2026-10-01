using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Queries;

/// <summary>
/// Application service implementing automated query dispatch for rule check violations
/// and auto-resolution/closure of open queries upon site data correction.
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// @req:SRS-EDC-004
/// </summary>
public sealed class AutoQueryDispatchService(
    IClinicalQueryRepository queryRepository,
    IClock clock) : IAutoQueryDispatchService
{
    public async Task<IReadOnlyList<ClinicalQuery>> ProcessViolationsAndAutoResolutionAsync(
        StudyCode studyId,
        SubjectCode subjectId,
        SiteCode? siteId,
        string? visitId,
        string? formId,
        IReadOnlyList<ClinicalObservation> observations,
        IReadOnlyList<EditCheckViolation> violations,
        CancellationToken cancellationToken)
    {
        var processedQueries = new List<ClinicalQuery>();

        // 1. Fetch existing queries for this subject
        var existingQueries = await queryRepository.ListBySubjectAsync(studyId, subjectId, cancellationToken).ConfigureAwait(false);

        var activeQueries = existingQueries
            .Where(q => q.Status is QueryStatus.Open or QueryStatus.Candidate or QueryStatus.Reopened)
            .ToList();

        // Map observations by test code for quick lookup
        var obsMap = observations
            .ToLookup(o => o.TestCode.Value, StringComparer.OrdinalIgnoreCase);

        // Track rules/fields that were evaluated during this run
        var evaluatedRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evaluatedFields = new HashSet<string>(observations.Select(o => o.TestCode.Value), StringComparer.OrdinalIgnoreCase);

        // 2. Process violations: Dispatch auto-queries or create CDM candidate queries
        foreach (var violation in violations)
        {
            evaluatedRules.Add(violation.RuleId);
            string targetField = violation.TargetField ?? string.Empty;
            if (!string.IsNullOrEmpty(targetField))
            {
                evaluatedFields.Add(targetField);
            }

            TestCode testCode;
            try
            {
                testCode = new TestCode(!string.IsNullOrWhiteSpace(targetField) ? targetField : "UNKNOWN");
            }
            catch
            {
                testCode = new TestCode("UNKNOWN");
            }

            DomainCode? domainCode = null;
            ObservationId? obsId = null;
            var matchingObs = obsMap[testCode.Value].FirstOrDefault();
            if (matchingObs != null)
            {
                domainCode = matchingObs.Domain;
                obsId = matchingObs.Id;
            }

            // Check if active query already exists on coordinates
            var activeQuery = activeQueries.FirstOrDefault(q =>
                q.RuleId == violation.RuleId ||
                (q.TestCode.Value.Equals(testCode.Value, StringComparison.OrdinalIgnoreCase) &&
                 (visitId == null || q.VisitId == visitId)));

            if (activeQuery is null)
            {
                string messageTemplate = !string.IsNullOrWhiteSpace(violation.StandardTemplate)
                    ? violation.StandardTemplate
                    : violation.Message;

                QueryStatus initialStatus = violation.IsAutoDispatchEnabled
                    ? QueryStatus.Open
                    : QueryStatus.Candidate;

                string origin = violation.IsAutoDispatchEnabled ? "AUTO_DISPATCH" : "RULE_ENGINE";
                string queryType = violation.IsAutoDispatchEnabled ? "AUTO_DISPATCH" : "CLINICAL_DISCREPANCY";

                var raiseResult = ClinicalQuery.Raise(
                    studyId: studyId,
                    subjectId: subjectId,
                    testCode: testCode,
                    explanation: messageTemplate,
                    initialStatus: initialStatus,
                    siteId: siteId,
                    visitId: violation.VisitId ?? visitId,
                    domain: domainCode,
                    observationId: obsId,
                    fieldLink: targetField,
                    message: messageTemplate,
                    origin: origin,
                    priority: "MEDIUM",
                    ruleId: violation.RuleId,
                    formId: violation.FormId ?? formId,
                    fieldId: targetField,
                    queryType: queryType,
                    actionRequired: "CORRECT_DATA");

                if (raiseResult.IsSuccess && raiseResult.Value is not null)
                {
                    var query = raiseResult.Value;
                    query.CreatedBy = ServiceAccounts.AutoQueryEngine.UserId;
                    query.ReasonForChange = ServiceAccounts.AutoQueryEngine.ChangeReason;

                    await queryRepository.AddAsync(query, cancellationToken).ConfigureAwait(false);
                    processedQueries.Add(query);
                    activeQueries.Add(query);
                }
            }
        }

        // 3. Auto-Resolution: Close active queries whose target fields/rules were re-evaluated and NO LONGER have violations
        var activeViolatedRules = new HashSet<string>(violations.Select(v => v.RuleId), StringComparer.OrdinalIgnoreCase);
        var activeViolatedFields = new HashSet<string>(
            violations.Where(v => !string.IsNullOrEmpty(v.TargetField)).Select(v => v.TargetField!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var query in activeQueries)
        {
            if (query.Status is not (QueryStatus.Open or QueryStatus.Candidate or QueryStatus.Reopened))
            {
                continue;
            }

            bool isRuleMatch = !string.IsNullOrEmpty(query.RuleId) && evaluatedRules.Contains(query.RuleId);
            bool isFieldMatch = evaluatedFields.Contains(query.TestCode.Value) ||
                (!string.IsNullOrEmpty(query.FieldId) && evaluatedFields.Contains(query.FieldId));

            if (isRuleMatch || isFieldMatch)
            {
                bool stillViolated = (!string.IsNullOrEmpty(query.RuleId) && activeViolatedRules.Contains(query.RuleId)) ||
                    activeViolatedFields.Contains(query.TestCode.Value) ||
                    (!string.IsNullOrEmpty(query.FieldId) && activeViolatedFields.Contains(query.FieldId));

                if (!stillViolated)
                {
                    // Discrepancy has been corrected by site submission!
                    var closeResult = query.Close(ServiceAccounts.AutoQueryEngine.UserId, clock.UtcNow);
                    if (closeResult.IsSuccess)
                    {
                        query.ReasonForChange = "Automated query auto-resolution upon site data correction";
                        processedQueries.Add(query);
                    }
                }
            }
        }

        await queryRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return processedQueries;
    }
}
