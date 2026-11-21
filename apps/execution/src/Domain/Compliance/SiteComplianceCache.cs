using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Compliance;

/// <summary>
/// The eTMF-completeness cache aggregate keyed by <c>(study, site, milestone)</c> — Rule 2's compliance-gate
/// data source. Carries its own plain-integer version counter, deliberately <b>not</b> an EF concurrency
/// token (see MIG-SUBJ-012): <see cref="ApplyWebhookUpdate"/> is a plain upsert-increment counter per
/// <c>01_spec.md</c> §1.1 ("if a row exists, its <c>version</c> is incremented by 1 ... not a concurrency
/// token check"), never a compare-and-swap.
/// Legacy Python source being replaced: <c>apps/execution/database/models/compliance.py</c> (<c>SiteComplianceCache</c>).
/// @req:PRD-SUB-001
/// </summary>
public sealed class SiteComplianceCache : AggregateRoot<Guid>
{
    public StudyCode StudyCode { get; private set; } = default!;
    public SiteCode SiteCode { get; private set; }
    public string Milestone { get; private set; } = default!;
    public bool IsComplete { get; private set; }
    public bool IsActivated { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public string? ActivatedBy { get; private set; }

    /// <summary>
    /// Exposed as a genuine read-only view — backed by a private mutable list that is never handed out
    /// directly, so casting this back to <see cref="List{T}"/> at a call site and mutating the result
    /// cannot reach the aggregate's internal state.
    /// </summary>
    public IReadOnlyList<string> MissingDocuments => _missingDocuments.AsReadOnly();

    private readonly List<string> _missingDocuments = new();

    // legacy's own optimistic counter — see §4.2, distinct from EF's rowversion.
    public int Version { get; private set; } = 1;

    private SiteComplianceCache() { }

    public static SiteComplianceCache Create(StudyCode studyCode, SiteCode siteCode, string milestone,
        bool isComplete, IEnumerable<string> missingDocuments)
    {
        var cache = new SiteComplianceCache
        {
            Id = Guid.NewGuid(),
            StudyCode = studyCode,
            SiteCode = siteCode,
            Milestone = milestone,
            IsComplete = isComplete,
        };
        cache._missingDocuments.AddRange(missingDocuments);
        return cache;
    }

    /// <summary>
    /// Upsert semantics from <c>01_spec.md</c> §1.1: always overwrites <see cref="IsComplete"/> and fully
    /// replaces the <see cref="MissingDocuments"/> contents (clear + re-add, never merge/append), then
    /// increments <see cref="Version"/> by exactly 1 on every call — no compare-and-swap / optimistic-conflict
    /// rejection is performed here.
    /// </summary>
    public void ApplyWebhookUpdate(bool isComplete, IEnumerable<string> missingDocuments)
    {
        IsComplete = isComplete;
        _missingDocuments.Clear();
        _missingDocuments.AddRange(missingDocuments);
        Version++;
    }

    /// <summary>
    /// Marks this compliance gate as activated without incrementing <see cref="Version"/> or mutating
    /// <see cref="IsComplete"/>. Idempotent: returns <see langword="false"/> without overwriting the original
    /// timestamp or actor when already activated.
    /// </summary>
    public bool MarkActivated(DateTimeOffset activatedAt, string activatedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activatedBy);
        if (IsActivated)
        {
            return false;
        }

        IsActivated = true;
        ActivatedAt = activatedAt;
        ActivatedBy = activatedBy.Trim();
        return true;
    }
}
