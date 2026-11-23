using System;
using System.Collections.Generic;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Time;

namespace Cadence.Modules.Execution.Domain;

/// <summary>
/// One de-identified aging query line item in a daily study/site lead digest (<c>PRD-QRY-002</c>).
/// Contains strictly trial operational codes and timestamps — never participant PII or <c>ParticipantId</c>.
/// </summary>
/// <param name="QueryId">The escalated query identifier.</param>
/// <param name="FormId">The form instance identifier.</param>
/// <param name="ItemId">The CollectionSpec item identifier.</param>
/// <param name="SiteCode">The trial site code.</param>
/// <param name="SubjectCode">The pseudonymous trial subject code.</param>
/// <param name="OwnerRole">Current owner role (<c>site</c>, <c>monitor</c>, <c>data_manager</c>).</param>
/// <param name="Priority">Current priority (<c>high</c>).</param>
/// <param name="OpenedAt">When the query was first opened.</param>
/// <param name="DueAt">When the query's 14-day SLA expired.</param>
/// <param name="AgeDays">Calendar days elapsed in the current open window.</param>
public sealed record FormQueryAgingDigestEntry(
    Guid QueryId,
    Guid FormId,
    string ItemId,
    string SiteCode,
    string SubjectCode,
    string OwnerRole,
    string Priority,
    DateTimeOffset OpenedAt,
    DateTimeOffset DueAt,
    int AgeDays);

/// <summary>
/// Insert-only daily query aging digest emitted for study leads (<c>study_lead</c>) and site leads (<c>site_investigator</c>)
/// when queries remain open past 14 calendar days (<c>PRD-QRY-002</c>).
/// </summary>
public sealed class FormQueryAgingDigest : AggregateRoot<Guid>
{
    private readonly List<FormQueryAgingDigestEntry> _entries = [];

    private FormQueryAgingDigest()
    {
    }

    private FormQueryAgingDigest(
        Guid id,
        TenantId tenantId,
        StudyId studyId,
        StudySiteId? studySiteId,
        string scopeKey,
        string recipientRole,
        DateOnly digestDate,
        DateTimeOffset generatedAt,
        string studyCode,
        string? siteCode,
        IReadOnlyList<FormQueryAgingDigestEntry> entries)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
        StudySiteId = studySiteId;
        ScopeKey = scopeKey;
        RecipientRole = recipientRole;
        DigestDate = digestDate;
        GeneratedAt = KernelTimestamps.ToStoredPrecision(generatedAt);
        StudyCode = studyCode;
        SiteCode = siteCode;
        EscalatedCount = entries.Count;
        _entries.AddRange(entries);
    }

    /// <summary>The tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The study whose aging queries are summarized.</summary>
    public StudyId StudyId { get; private set; }

    /// <summary>Optional study site when scoped to a site lead; null for study-wide lead digests.</summary>
    public StudySiteId? StudySiteId { get; private set; }

    /// <summary>Deterministic scope key (<c>study</c> or <c>site:&lt;studySiteId&gt;</c>).</summary>
    public string ScopeKey { get; private set; } = string.Empty;

    /// <summary>Target lead role (<c>study_lead</c> or <c>site_investigator</c>).</summary>
    public string RecipientRole { get; private set; } = string.Empty;

    /// <summary>UTC calendar date of the digest on <c>IClock</c>.</summary>
    public DateOnly DigestDate { get; private set; }

    /// <summary>Recorded timestamp on <c>IClock</c> when the digest was emitted.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Human-facing study code.</summary>
    public string StudyCode { get; private set; } = string.Empty;

    /// <summary>Human-facing site code for site-scoped digests; null for study-wide digests.</summary>
    public string? SiteCode { get; private set; }

    /// <summary>Number of overdue open queries included in the digest.</summary>
    public int EscalatedCount { get; private set; }

    /// <summary>De-identified overdue query entries.</summary>
    public IReadOnlyList<FormQueryAgingDigestEntry> Entries => _entries;

    /// <summary>Creates a new daily aging digest record.</summary>
    public static FormQueryAgingDigest Create(
        Guid id,
        TenantId tenantId,
        StudyId studyId,
        StudySiteId? studySiteId,
        string recipientRole,
        DateOnly digestDate,
        DateTimeOffset generatedAt,
        string studyCode,
        string? siteCode,
        IReadOnlyList<FormQueryAgingDigestEntry> entries)
    {
        if (id == Guid.Empty || tenantId.IsEmpty || studyId.IsEmpty || string.IsNullOrWhiteSpace(recipientRole))
        {
            throw new ArgumentException("A query aging digest requires a non-empty id, tenant, study, and recipient role.");
        }

        ArgumentNullException.ThrowIfNull(entries);
        var scopeKey = studySiteId is { } site ? $"site:{site.Value:D}" : "study";
        return new FormQueryAgingDigest(
            id,
            tenantId,
            studyId,
            studySiteId,
            scopeKey,
            recipientRole.Trim(),
            digestDate,
            generatedAt,
            string.IsNullOrWhiteSpace(studyCode) ? studyId.ToString() : studyCode.Trim(),
            string.IsNullOrWhiteSpace(siteCode) ? null : siteCode.Trim(),
            entries);
    }
}
