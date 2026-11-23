using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Apps.Knowledge.Application;

/// <summary>
/// Ambient actor override used strictly when bootstrapping controlled baseline SOPs so the shared
/// <c>GxPAuditInterceptor</c> attributes system-seeded records to <c>system_sop_catalog</c> rather than
/// an interactive reader (including read-only auditors).
/// </summary>
public static class KnowledgeAuditActorScope
{
    private static readonly AsyncLocal<string?> AmbientUserId = new();

    public static string? CurrentUserId => AmbientUserId.Value;

    public static IDisposable Begin(string actorUserId)
    {
        string? previous = AmbientUserId.Value;
        AmbientUserId.Value = actorUserId;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(string? previous) : IDisposable
    {
        public void Dispose() => AmbientUserId.Value = previous;
    }
}

/// <summary>
/// Idempotently seeds controlled baseline SOP categories, published articles, locked snapshot versions,
/// and contextual help route mappings (including query-aware Clinical Canvas section and zoom mappings)
/// for <c>/studies</c> (<c>/studies/:studyCode</c>), <c>/ecrf</c>, <c>/ctms</c>, <c>/ssu</c>, <c>/mdr</c>,
/// <c>/audit</c>, and workspace hubs across all clinical staff personas (PRD-KNB-001, PRD-KNB-002).
/// @req:PRD-KNB-001
/// @req:PRD-KNB-002
/// </summary>
public static class KnowledgeCatalogSeeder
{
    private const string CatalogServiceAccount = "svc:knowledge-sop-catalog";
    private const string SeedReason = "Initial controlled SOP baseline publication";
    private const string UpgradeSeedReason = "Synchronized controlled SOP HTML procedures and section anchor IDs";

    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private static readonly string AllStaffVisibilityJson = JsonSerializer.Serialize(new[]
    {
        "super_admin",
        "sponsor_designer",
        "site_crc",
        "site_investigator",
        "cra_monitor",
        "data_manager",
        "safety_officer",
        "auditor",
    });

    private static readonly string[] AllStaffPersonas =
    [
        "super_admin",
        "sponsor_designer",
        "site_crc",
        "site_investigator",
        "cra_monitor",
        "data_manager",
        "safety_officer",
        "auditor",
    ];

    private static readonly Guid DefaultTestTenantGuid =
        DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-test");

    public static async Task EnsureSeededAsync(KnowledgeDbContext db, IClock clock, CancellationToken ct = default)
    {
        string tenantSuffix = await ResolveSeedTenantSuffixAsync(db, ct).ConfigureAwait(false);
        string latestVersionSentinelId = $"ver-knb-502-v2{tenantSuffix}";
        string latestMappingSentinelId = $"m-stc-q-observation{tenantSuffix}";

        // Fast path: once our complete HTML snapshot and query-aware Canvas mappings exist for this tenant, skip locking and writes.
        if (await HasCompleteCatalogBaselineAsync(db, latestVersionSentinelId, latestMappingSentinelId, ct).ConfigureAwait(false))
        {
            return;
        }

        await SeedLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await HasCompleteCatalogBaselineAsync(db, latestVersionSentinelId, latestMappingSentinelId, ct).ConfigureAwait(false))
            {
                return;
            }

            using var _ = KnowledgeAuditActorScope.Begin(CatalogServiceAccount);
            DateTimeOffset seededAt = clock.UtcNow;

            var categories = BuildCategories(seededAt, tenantSuffix);
            var existingCategoriesById = await db.KnowledgeCategories
                .ToDictionaryAsync(c => c.Id, StringComparer.Ordinal, ct)
                .ConfigureAwait(false);

            foreach (var category in categories)
            {
                if (!existingCategoriesById.TryGetValue(category.Id, out var existingCategory))
                {
                    db.KnowledgeCategories.Add(category);
                }
                else if (!string.Equals(existingCategory.PersonaVisibility, AllStaffVisibilityJson, StringComparison.Ordinal))
                {
                    existingCategory.PersonaVisibility = AllStaffVisibilityJson;
                }
            }

            var seeds = BuildSopSeeds(seededAt, tenantSuffix);
            var existingArticlesById = await db.KnowledgeArticles
                .ToDictionaryAsync(a => a.Id, StringComparer.Ordinal, ct)
                .ConfigureAwait(false);

            // Read existing version metadata with AsNoTracking so locked 21 CFR Part 11 snapshots are never mutated.
            var existingVersions = await db.KnowledgeArticleVersions
                .AsNoTracking()
                .Select(v => new { v.Id, v.ArticleId, v.VersionIndex })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var existingVersionIds = existingVersions
                .Select(v => v.Id)
                .ToHashSet(StringComparer.Ordinal);

            var maxVersionIndexByArticleId = existingVersions
                .GroupBy(v => v.ArticleId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Max(v => v.VersionIndex), StringComparer.Ordinal);

            var existingMappingIds = await db.ContextualHelpMappings
                .AsNoTracking()
                .Select(m => m.Id)
                .ToHashSetAsync(StringComparer.Ordinal, ct)
                .ConfigureAwait(false);

            foreach (var seed in seeds)
            {
                if (!existingArticlesById.TryGetValue(seed.Article.Id, out var existingArticle))
                {
                    db.KnowledgeArticles.Add(seed.Article);
                    if (!existingVersionIds.Contains(seed.Version.Id))
                    {
                        db.KnowledgeArticleVersions.Add(seed.Version);
                    }

                    AuditActions.Record(db, new AuditAction(
                        EntityName: nameof(KnowledgeArticle),
                        EntityId: seed.Article.Id,
                        Action: "SOP_PUBLISHED",
                        UserId: CatalogServiceAccount,
                        ReasonForChange: SeedReason,
                        ChangesJson: JsonSerializer.Serialize(new
                        {
                            slug = seed.Article.Slug,
                            version_label = seed.Article.VersionLabel,
                            version_id = seed.Version.Id,
                            status = seed.Article.Status,
                        })));
                }
                else if (!existingVersionIds.Contains(seed.Version.Id))
                {
                    int nextVersionIndex = Math.Max(
                        existingArticle.VersionIndex,
                        maxVersionIndexByArticleId.GetValueOrDefault(existingArticle.Id, 0)) + 1;

                    var upgradedVersion = new KnowledgeArticleVersion
                    {
                        Id = seed.Version.Id,
                        ArticleId = existingArticle.Id,
                        VersionIndex = nextVersionIndex,
                        VersionLabel = seed.Article.VersionLabel,
                        BodyMarkdown = seed.Version.BodyMarkdown,
                        BodyHtml = seed.Version.BodyHtml,
                        StatusAtSnapshot = "PUBLISHED",
                        IsLocked = true,
                        CreatedAt = seededAt,
                        CreatedBy = CatalogServiceAccount,
                        ReasonForChange = UpgradeSeedReason,
                    };

                    db.KnowledgeArticleVersions.Add(upgradedVersion);

                    string? previousVersionId = existingArticle.CurrentPublishedVersionId;
                    string? previousVersionLabel = existingArticle.VersionLabel;

                    existingArticle.CurrentPublishedVersionId = upgradedVersion.Id;
                    existingArticle.VersionLabel = upgradedVersion.VersionLabel;
                    existingArticle.VersionIndex = nextVersionIndex;
                    existingArticle.LastEditedBy = CatalogServiceAccount;
                    existingArticle.ReasonForChange = UpgradeSeedReason;

                    AuditActions.Record(db, new AuditAction(
                        EntityName: nameof(KnowledgeArticle),
                        EntityId: existingArticle.Id,
                        Action: "SOP_PUBLISHED",
                        UserId: CatalogServiceAccount,
                        ReasonForChange: UpgradeSeedReason,
                        ChangesJson: JsonSerializer.Serialize(new
                        {
                            slug = existingArticle.Slug,
                            previous_version_id = previousVersionId,
                            previous_version_label = previousVersionLabel,
                            version_id = upgradedVersion.Id,
                            version_label = upgradedVersion.VersionLabel,
                            status = existingArticle.Status,
                        })));
                }

                foreach (var mapping in seed.Mappings.Where(m => !existingMappingIds.Contains(m.Id)))
                {
                    db.ContextualHelpMappings.Add(mapping);
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static async Task<bool> HasCompleteCatalogBaselineAsync(
        KnowledgeDbContext db,
        string latestVersionSentinelId,
        string latestMappingSentinelId,
        CancellationToken ct)
    {
        bool hasLatestVersion = await db.KnowledgeArticleVersions
            .AsNoTracking()
            .AnyAsync(v => v.Id == latestVersionSentinelId, ct)
            .ConfigureAwait(false);
        if (!hasLatestVersion)
        {
            return false;
        }

        return await db.ContextualHelpMappings
            .AsNoTracking()
            .AnyAsync(m => m.Id == latestMappingSentinelId, ct)
            .ConfigureAwait(false);
    }

    private static async Task<string> ResolveSeedTenantSuffixAsync(KnowledgeDbContext db, CancellationToken ct)
    {
        var ext = db.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        Guid? tenantId = ext?.TenantAccessor is not null
            ? await ext.TenantAccessor.GetCurrentTenantIdAsync(ct).ConfigureAwait(false)
            : null;
        if (tenantId is null || tenantId.Value == Guid.Empty || tenantId.Value == DefaultTestTenantGuid)
        {
            return string.Empty;
        }

        return $"-{tenantId.Value.ToString("N")[..8]}";
    }

    private sealed record SopSeed(
        KnowledgeArticle Article,
        KnowledgeArticleVersion Version,
        IReadOnlyList<ContextualHelpMapping> Mappings);

    private static List<KnowledgeCategory> BuildCategories(DateTimeOffset createdAt, string tenantSuffix) =>
        BuildBaseCategories(createdAt)
            .Select(c =>
            {
                c.Id = $"{c.Id}{tenantSuffix}";
                return c;
            })
            .ToList();

    private static List<KnowledgeCategory> BuildBaseCategories(DateTimeOffset createdAt) =>
    [
        NewCategory(
            "cat-clinical-ops",
            "Clinical Operations",
            "clinical-operations",
            "eCRF data capture, subject enrollment, and clinical visit execution procedures.",
            createdAt),
        NewCategory(
            "cat-study-authoring",
            "Study Authoring & MDR",
            "study-authoring-mdr",
            "CDISC USDM protocol authoring, study portfolio governance, and Schedule of Activities design.",
            createdAt),
        NewCategory(
            "cat-site-monitoring",
            "Site Monitoring & CTMS",
            "site-monitoring-ctms",
            "CRA monitoring visits, source data verification, and site operational oversight.",
            createdAt),
        NewCategory(
            "cat-site-startup",
            "Site Startup & Activation",
            "site-startup-activation",
            "Country regulatory submissions, IRB/IEC approvals, and site greenlight activation gates.",
            createdAt),
        NewCategory(
            "cat-regulatory-compliance",
            "Regulatory & Quality Compliance",
            "regulatory-compliance",
            "21 CFR Part 11 compliance, audit trail inspection, and controlled quality workflows.",
            createdAt),
        NewCategory(
            "cat-data-management",
            "Data Management & Coding",
            "data-management-coding",
            "MedDRA and WHODrug medical coding, discrepancy queries, and database lock governance.",
            createdAt),
    ];

    private static KnowledgeCategory NewCategory(string id, string name, string slug, string description, DateTimeOffset createdAt) =>
        new()
        {
            Id = id,
            Name = name,
            Slug = slug,
            Description = description,
            PersonaVisibility = AllStaffVisibilityJson,
            ParentId = null,
            IsDeleted = false,
            CreatedAt = createdAt,
            CreatedBy = CatalogServiceAccount,
            ReasonForChange = SeedReason,
            VersionIndex = 1,
        };

    private static List<SopSeed> BuildSopSeeds(DateTimeOffset baseTime, string tenantSuffix) =>
        BuildBaseSopSeeds(baseTime)
            .Select(seed => ApplyTenantSuffix(seed, tenantSuffix))
            .ToList();

    private static SopSeed ApplyTenantSuffix(SopSeed seed, string tenantSuffix)
    {
        if (tenantSuffix.Length == 0)
        {
            return seed;
        }

        seed.Article.Id = $"{seed.Article.Id}{tenantSuffix}";
        seed.Article.CategoryId = $"{seed.Article.CategoryId}{tenantSuffix}";
        seed.Article.CurrentPublishedVersionId = $"{seed.Article.CurrentPublishedVersionId}{tenantSuffix}";

        seed.Version.Id = $"{seed.Version.Id}{tenantSuffix}";
        seed.Version.ArticleId = seed.Article.Id;

        foreach (var mapping in seed.Mappings)
        {
            mapping.Id = $"{mapping.Id}{tenantSuffix}";
            mapping.ArticleId = seed.Article.Id;
        }

        return seed;
    }

    private static List<SopSeed> BuildBaseSopSeeds(DateTimeOffset baseTime)
    {
        var list = new List<SopSeed>
        {
            // 1. /studies (Portfolio directory) — 3 procedural steps
            CreateSop(
                articleId: "art-studies-portfolio-sop",
                versionId: "ver-studies-portfolio-101-v2",
                slug: "sop-std-101-study-portfolio-governance",
                title: "SOP-STD-101: Study Portfolio Governance & Workspace Selection",
                categoryId: "cat-study-authoring",
                versionLabel: "2.1",
                versionIndex: 2,
                tags: ["studies", "portfolio", "governance", "usdm"],
                markdown: """
                    ## Purpose & Scope
                    Governs study selection, protocol status verification, and role-scoped workspace entry across `/studies`.

                    ## Operational Steps
                    1. Verify protocol code and lifecycle status (`ACTIVE`, `IN_DESIGN`, or `LOCKED`) prior to opening clinical data workspaces.
                    2. Confirm assigned study and site scope before initiating enrollment, monitoring, or data management actions.
                    3. Escalate missing study assignments to Platform Access Governance via contextual support ticketing.

                    ### Enterprise Portfolio Oversight
                    Sponsor administrators verify tenant-wide study lifecycle readiness, environment locks, and portfolio governance controls.

                    ### Protocol Version Selection
                    Study designers confirm active USDM protocol versions before entering Schedule of Activities or amendment workspaces.

                    ### Assigned Site Study Selection
                    Site coordinators select authorized protocols and verify site activation status before enrolling or updating participants.

                    ### Study Monitoring Entry
                    Clinical Research Associates verify site monitoring assignments, SDV backlogs, and pending visit action items.

                    ### Study Data Review Entry
                    Data managers review open discrepancy query counts and database lock readiness across active protocols.

                    ### Inspection Scope Selection
                    Auditors confirm read-only inspection scope across published study artifacts and immutable audit trails.
                    """,
                html: """
                    <h2 id="study-portfolio-governance">Purpose &amp; Scope</h2>
                    <p>Governs study selection, protocol status verification, and role-scoped workspace entry across <code>/studies</code>.</p>
                    <h2 id="portfolio-operational-steps">Operational Steps</h2>
                    <ol>
                      <li>Verify protocol code and lifecycle status (<code>ACTIVE</code>, <code>IN_DESIGN</code>, or <code>LOCKED</code>) prior to opening clinical data workspaces.</li>
                      <li>Confirm assigned study and site scope before initiating enrollment, monitoring, or data management actions.</li>
                      <li>Escalate missing study assignments to Platform Access Governance via contextual support ticketing.</li>
                    </ol>
                    <h3 id="enterprise-portfolio-oversight">Enterprise Portfolio Oversight</h3>
                    <p>Sponsor administrators verify tenant-wide study lifecycle readiness, environment locks, and portfolio governance controls.</p>
                    <h3 id="protocol-version-selection">Protocol Version Selection</h3>
                    <p>Study designers confirm active USDM protocol versions before entering Schedule of Activities or amendment workspaces.</p>
                    <h3 id="assigned-site-study-selection">Assigned Site Study Selection</h3>
                    <p>Site coordinators select authorized protocols and verify site activation status before enrolling or updating participants.</p>
                    <h3 id="study-monitoring-entry">Study Monitoring Entry</h3>
                    <p>Clinical Research Associates verify site monitoring assignments, SDV backlogs, and pending visit action items.</p>
                    <h3 id="study-data-review-entry">Study Data Review Entry</h3>
                    <p>Data managers review open discrepancy query counts and database lock readiness across active protocols.</p>
                    <h3 id="inspection-scope-selection">Inspection Scope Selection</h3>
                    <p>Auditors confirm read-only inspection scope across published study artifacts and immutable audit trails.</p>
                    """,
                createdAt: baseTime.AddMinutes(-60),
                mappings: BuildRouteMappings(
                    prefix: "m-std",
                    exactRoute: "/studies",
                    wildcardRoute: "/studies/*",
                    articleId: "art-studies-portfolio-sop",
                    defaultAnchor: "#study-portfolio-governance",
                    personaAnchors: new Dictionary<string, string>
                    {
                        ["super_admin"] = "#enterprise-portfolio-oversight",
                        ["sponsor_designer"] = "#protocol-version-selection",
                        ["site_crc"] = "#assigned-site-study-selection",
                        ["cra_monitor"] = "#study-monitoring-entry",
                        ["data_manager"] = "#study-data-review-entry",
                        ["auditor"] = "#inspection-scope-selection",
                    },
                    priority: 20,
                    createdAt: baseTime.AddMinutes(-60))),

            // 2. /studies/:studyCode (Parameterized Study Clinical Canvas) — 3 procedural steps
            CreateSop(
                articleId: "art-study-canvas-sop",
                versionId: "ver-study-canvas-102-v2",
                slug: "sop-std-102-active-study-clinical-canvas",
                title: "SOP-STD-102: Active Study Clinical Canvas & Visit Matrix Operations",
                categoryId: "cat-clinical-ops",
                versionLabel: "2.3",
                versionIndex: 2,
                tags: ["studies", "clinical-canvas", "enrollment", "visits", "queries"],
                markdown: """
                    ## Purpose & Scope
                    Defines standard operating procedures for executing study-specific enrollment, visit schedule navigation, and form query resolution inside `/studies/:studyCode`.

                    ## Key Controls
                    1. Use pseudonymous subject codes (for example `SUBJ-1001`) exclusively; never enter direct participant PII.
                    2. Supply a clear 21 CFR Part 11 reason for change whenever recording or amending clinical observations.
                    3. Review open form queries and SDV indicators prior to visit sign-off.

                    ### Protocol Versions & Schedule Design
                    Verify active protocol version lineage, epoch windows, and CDISC USDM Schedule of Activities bindings before releasing design updates.

                    ### Participant Enrollment & Visit Matrix
                    Confirm site greenlight status and informed consent verification prior to registering participants or launching scheduled visit forms.

                    ### Participant Timeline & Visit Record
                    Inspect longitudinal visit completion, scheduled windows, and open form queries within the individual participant record view.

                    ### Database Freeze & Lock Controls
                    Confirm zero unresolved critical queries, complete medical coding, and Principal Investigator eCRF sign-off before executing soft freeze or hard database lock.

                    ### Regulatory Submission & Data Exports
                    Validate CDISC SDTM/ADaM dataset lineage, de-identification checks, and SHA-256 export manifests prior to regulatory package generation.

                    ### Investigational Product & RTSM Supply
                    Reconcile site depot inventory, temperature excursion holds, and randomization kit accountability across clinical supply workflows.

                    ### Protocol Validation & Release Checks
                    Resolve structural edit-check diagnostics and USDM schema validation findings before promoting protocol versions.

                    ### Role-Specific Study Canvas Responsibilities
                    Sponsor administrators oversee lock gates; designers maintain visit schedules; site CRCs and investigators capture and sign visit data; CRAs verify source records; data managers triage cleanliness; safety officers review clinical signals; and auditors inspect read-only provenance.
                    """,
                html: """
                    <h2 id="active-study-clinical-canvas">Purpose &amp; Scope</h2>
                    <p>Defines standard operating procedures for executing study-specific enrollment, visit schedule navigation, and form query resolution inside <code>/studies/:studyCode</code>.</p>
                    <h2 id="study-canvas-key-controls">Key Controls</h2>
                    <ol>
                      <li>Use pseudonymous subject codes (for example <code>SUBJ-1001</code>) exclusively; never enter direct participant PII.</li>
                      <li>Supply a clear 21 CFR Part 11 reason for change whenever recording or amending clinical observations.</li>
                      <li>Review open form queries and SDV indicators prior to visit sign-off.</li>
                    </ol>
                    <h3 id="protocol-versions-and-soa-design">Protocol Versions &amp; Schedule Design</h3>
                    <p>Verify active protocol version lineage, epoch windows, and CDISC USDM Schedule of Activities bindings before releasing design updates.</p>
                    <h3 id="participant-enrollment-and-visit-matrix">Participant Enrollment &amp; Visit Matrix</h3>
                    <p>Confirm site greenlight status and informed consent verification prior to registering participants or launching scheduled visit forms.</p>
                    <h3 id="participant-timeline-and-visit-record">Participant Timeline &amp; Visit Record</h3>
                    <p>Inspect longitudinal visit completion, scheduled windows, and open form queries within the individual participant record view.</p>
                    <h3 id="database-freeze-and-lock-controls">Database Freeze &amp; Lock Controls</h3>
                    <p>Confirm zero unresolved critical queries, complete medical coding, and Principal Investigator eCRF sign-off before executing soft freeze or hard database lock.</p>
                    <h3 id="regulatory-submission-and-exports">Regulatory Submission &amp; Data Exports</h3>
                    <p>Validate CDISC SDTM/ADaM dataset lineage, de-identification checks, and SHA-256 export manifests prior to regulatory package generation.</p>
                    <h3 id="investigational-product-and-rtsm-supply">Investigational Product &amp; RTSM Supply</h3>
                    <p>Reconcile site depot inventory, temperature excursion holds, and randomization kit accountability across clinical supply workflows.</p>
                    <h3 id="protocol-validation-and-release-checks">Protocol Validation &amp; Release Checks</h3>
                    <p>Resolve structural edit-check diagnostics and USDM schema validation findings before promoting protocol versions.</p>
                    <h3 id="study-governance-and-locks">Study Governance &amp; Locks</h3>
                    <p>Sponsor administrators oversee study-level access boundaries, freeze milestones, and lock authorization.</p>
                    <h3 id="visit-schedule-and-form-definitions">Visit Schedule &amp; Form Definitions</h3>
                    <p>Sponsor designers maintain USDM visit schedules, collection specifications, and controlled terminology bindings.</p>
                    <h3 id="subject-enrollment-and-visit-capture">Subject Enrollment &amp; Visit Capture</h3>
                    <p>Site coordinators verify eligibility criteria and enter contemporaneous visit assessments under pseudonymous subject IDs.</p>
                    <h3 id="investigator-oversight-and-signoff">Investigator Oversight &amp; Sign-Off</h3>
                    <p>Site investigators review clinical significance assessments, protocol deviations, and 21 CFR Part 11 visit attestations.</p>
                    <h3 id="study-sdv-and-query-oversight">Study SDV &amp; Query Oversight</h3>
                    <p>Monitors track source data verification progress and issue targeted discrepancy queries across assigned sites.</p>
                    <h3 id="study-query-triage-and-cleanliness">Study Query Triage &amp; Cleanliness</h3>
                    <p>Data managers reconcile aged queries, coding flags, and cross-form consistency checks prior to milestone locks.</p>
                    <h3 id="study-safety-signal-review">Study Safety Signal Review</h3>
                    <p>Medical monitors and safety officers evaluate serious adverse event trends and laboratory alert thresholds.</p>
                    <h3 id="study-audit-trail-inspection">Study Audit Trail Inspection</h3>
                    <p>Auditors perform read-only verification of study lineage, electronic signatures, and contemporaneous change reasons.</p>
                    """,
                createdAt: baseTime.AddMinutes(-50),
                mappings: BuildParameterizedStudyMappings(
                    articleId: "art-study-canvas-sop",
                    createdAt: baseTime.AddMinutes(-50))),

            // 3. /ecrf (Subject Enrollment & eCRF Data Capture) — 3 procedural steps
            CreateSop(
                articleId: "art-ecrf-sop",
                versionId: "ver-ecrf-201-v2",
                slug: "sop-ecrf-201-subject-enrollment",
                title: "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture",
                categoryId: "cat-clinical-ops",
                versionLabel: "2.2",
                versionIndex: 2,
                tags: ["ecrf", "enrollment", "gxp-edc", "site-sop"],
                markdown: """
                    ## Purpose & Scope
                    This Standard Operating Procedure (SOP) governs all clinical trial personnel executing electronic Case Report Form (eCRF) data capture, subject registration, source verification, and discrepancy query management within the Cadence Clinical Research Platform.

                    ## Mandatory GxP Controls
                    1. Confirm informed consent verification prior to recording baseline assessments.
                    2. Record contemporaneous source-verified measurements and document mandatory change justifications for every amendment.
                    3. Escalate protocol deviations or locked-form blockers via Contextual Help using pseudonymous subject identifiers only.

                    ### eCRF Form Capture & Change Reason
                    Verify unit conversions, controlled terminology selections, and non-PII 21 CFR Part 11 change reasons when saving form instances.

                    ### Discrepancy Management & Coding
                    Triage open validation queries, document source-backed responses, and reconcile MedDRA/WHODrug coding terms before form lock.

                    ### EDC Governance, SDV & Audit Review
                    Maintain role-segregated eCRF layout validation, CRA source data verification, and immutable Part 11 audit inspection.
                    """,
                html: """
                    <h2 id="subject-enrollment-and-visit-entry">Purpose &amp; Scope</h2>
                    <p>This Standard Operating Procedure (SOP) governs all clinical trial personnel executing electronic Case Report Form (eCRF) data capture, subject registration, source verification, and discrepancy query management within the Cadence Clinical Research Platform.</p>
                    <h2 id="ecrf-mandatory-gxp-controls">Mandatory GxP Controls</h2>
                    <ol>
                      <li>Confirm informed consent verification prior to recording baseline assessments.</li>
                      <li>Record contemporaneous source-verified measurements and document mandatory change justifications for every amendment.</li>
                      <li>Escalate protocol deviations or locked-form blockers via Contextual Help using pseudonymous subject identifiers only.</li>
                    </ol>
                    <h3 id="ecrf-form-capture-and-change-reason">eCRF Form Capture &amp; Change Reason</h3>
                    <p>Verify unit conversions, controlled terminology selections, and non-PII 21 CFR Part 11 change reasons when saving form instances.</p>
                    <h3 id="discrepancy-management-and-coding">Discrepancy Management &amp; Coding</h3>
                    <p>Triage open validation queries, document source-backed responses, and reconcile MedDRA/WHODrug coding terms before form lock.</p>
                    <h3 id="edc-governance-and-locks">EDC Governance &amp; Locks</h3>
                    <p>Sponsor administrators govern eCRF environment locks, freeze rules, and role-level data entry permissions.</p>
                    <h3 id="crf-layout-validation">CRF Layout Validation</h3>
                    <p>Study designers verify form item bindings, CDASH/SDTM annotations, and edit-check firing conditions prior to live capture.</p>
                    <h3 id="ecrf-sdv-and-query-verification">eCRF SDV &amp; Query Verification</h3>
                    <p>Clinical Research Associates verify eCRF entries against clinic source records and close answered discrepancy queries.</p>
                    <h3 id="ecrf-audit-trail-review">eCRF Audit Trail Review</h3>
                    <p>Auditors inspect item-level revision histories, actor attributions, and SHA-256 Merkle chain integrity.</p>
                    """,
                createdAt: baseTime.AddMinutes(-40),
                mappings:
                [
                    .. BuildRouteMappings(
                        prefix: "m-crf",
                        exactRoute: "/ecrf",
                        wildcardRoute: "/ecrf/*",
                        articleId: "art-ecrf-sop",
                        defaultAnchor: "#subject-enrollment-and-visit-entry",
                        personaAnchors: new Dictionary<string, string>
                        {
                            ["super_admin"] = "#edc-governance-and-locks",
                            ["sponsor_designer"] = "#crf-layout-validation",
                            ["site_crc"] = "#subject-enrollment-and-visit-entry",
                            ["cra_monitor"] = "#ecrf-sdv-and-query-verification",
                            ["data_manager"] = "#discrepancy-management-and-coding",
                            ["auditor"] = "#ecrf-audit-trail-review",
                        },
                        priority: 25,
                        createdAt: baseTime.AddMinutes(-40)),
                    NewMapping(
                        "m-stc-q-queries",
                        "/studies/:studyCode?section=queries",
                        null,
                        "art-ecrf-sop",
                        "#discrepancy-management-and-coding",
                        50,
                        createdAt: baseTime.AddMinutes(-40)),
                    NewMapping(
                        "m-stc-q-form",
                        "/studies/:studyCode?form=*",
                        null,
                        "art-ecrf-sop",
                        "#ecrf-form-capture-and-change-reason",
                        65,
                        createdAt: baseTime.AddMinutes(-40)),
                ]),

            // 4. /ctms (Site Monitoring Visits & CTMS) — 2 procedural steps
            CreateSop(
                articleId: "art-ctms-sop",
                versionId: "ver-ctms-302-v2",
                slug: "sop-ctms-302-site-monitoring",
                title: "SOP-CTMS-302: Site Monitoring Visits & Source Data Verification",
                categoryId: "cat-site-monitoring",
                versionLabel: "1.5",
                versionIndex: 1,
                tags: ["ctms", "monitoring", "sdv", "cra", "trip-reports"],
                markdown: """
                    ## Site Monitoring & SDV Procedures
                    Governs Clinical Trial Management System (CTMS) monitoring visit scheduling, follow-up letters, CRA workload balancing, and site milestone tracking across `/ctms`.

                    ## Operational Checklist
                    1. Reconcile site enrollment metrics and pending monitoring action items prior to on-site or remote visits.
                    2. Document monitoring findings with ICH E6(R3) severity classification and link follow-up CAPA or support tickets.

                    ### Role-Scoped Monitoring & Milestone Controls
                    Align monitoring plans, site visit readiness, delegation rosters, query reconciliation, and signed trip report inspection across clinical operations roles.
                    """,
                html: """
                    <h2 id="site-monitoring-and-sdv">Site Monitoring &amp; SDV Procedures</h2>
                    <p>Governs Clinical Trial Management System (CTMS) monitoring visit scheduling, follow-up letters, CRA workload balancing, and site milestone tracking across <code>/ctms</code>.</p>
                    <h2 id="ctms-operational-checklist">Operational Checklist</h2>
                    <ol>
                      <li>Reconcile site enrollment metrics and pending monitoring action items prior to on-site or remote visits.</li>
                      <li>Document monitoring findings with ICH E6(R3) severity classification and link follow-up CAPA or support tickets.</li>
                    </ol>
                    <h3 id="ctms-portfolio-oversight">CTMS Portfolio Oversight</h3>
                    <p>Sponsor operations track site milestone attainment, monitoring cadence compliance, and regional enrollment velocity.</p>
                    <h3 id="monitoring-plan-alignment">Monitoring Plan Alignment</h3>
                    <p>Study designers align risk-based monitoring (RBQM) critical data indicators with Schedule of Activities assessments.</p>
                    <h3 id="site-visit-readiness">Site Visit Readiness &amp; Delegation</h3>
                    <p>Site coordinators maintain current delegation logs, source chart availability, and action-item remediation prior to CRA visits.</p>
                    <h3 id="monitoring-query-reconciliation">Monitoring Query Reconciliation</h3>
                    <p>Data managers reconcile unresolved monitoring findings with open eCRF discrepancy queries before interim locks.</p>
                    <h3 id="monitoring-visit-report-inspection">Monitoring Visit Report Inspection</h3>
                    <p>Auditors verify signed monitoring visit reports, follow-up communications, and CAPA closure evidence.</p>
                    """,
                createdAt: baseTime.AddMinutes(-30),
                mappings:
                [
                    .. BuildRouteMappings(
                        prefix: "m-ctm",
                        exactRoute: "/ctms",
                        wildcardRoute: "/ctms/*",
                        articleId: "art-ctms-sop",
                        defaultAnchor: "#site-monitoring-and-sdv",
                        personaAnchors: new Dictionary<string, string>
                        {
                            ["super_admin"] = "#ctms-portfolio-oversight",
                            ["sponsor_designer"] = "#monitoring-plan-alignment",
                            ["site_crc"] = "#site-visit-readiness",
                            ["cra_monitor"] = "#site-monitoring-and-sdv",
                            ["data_manager"] = "#monitoring-query-reconciliation",
                            ["auditor"] = "#monitoring-visit-report-inspection",
                        },
                        priority: 25,
                        createdAt: baseTime.AddMinutes(-30)),
                    NewMapping(
                        "m-stc-q-site-milestones",
                        "/studies/:studyCode?section=site-milestones",
                        null,
                        "art-ctms-sop",
                        "#ctms-portfolio-oversight",
                        50,
                        createdAt: baseTime.AddMinutes(-30)),
                    NewMapping(
                        "m-stc-q-delegation",
                        "/studies/:studyCode?section=delegation",
                        null,
                        "art-ctms-sop",
                        "#site-visit-readiness",
                        50,
                        createdAt: baseTime.AddMinutes(-30)),
                    NewMapping(
                        "m-stc-q-monitoring-visits",
                        "/studies/:studyCode?section=monitoring-visits",
                        null,
                        "art-ctms-sop",
                        "#site-monitoring-and-sdv",
                        50,
                        createdAt: baseTime.AddMinutes(-30)),
                ]),

            // 5. /ssu (Site Startup & Greenlight Activation) — 2 procedural steps
            CreateSop(
                articleId: "art-ssu-sop",
                versionId: "ver-ssu-205-v2",
                slug: "sop-ssu-205-site-startup-greenlight",
                title: "SOP-SSU-205: Site Startup, Regulatory Greenlight & Country Activation",
                categoryId: "cat-site-startup",
                versionLabel: "1.3",
                versionIndex: 1,
                tags: ["ssu", "site-startup", "greenlight", "regulatory", "activation"],
                markdown: """
                    ## Site Startup & Greenlight Activation Gate
                    Establishes mandatory regulatory, ethics (IRB/IEC), contract, essential document, and training prerequisites required before certifying site activation on `/ssu`.

                    ## Greenlight Verification
                    1. Confirm Essential Document List (EDL) artifacts and Delegation of Authority sign-offs are complete.
                    2. Verify country regulatory approval and IP pharmacy readiness before issuing greenlight certification.

                    ### Country & Site Activation Controls
                    Enforce country-pack regulatory alignment, site initiation visit completion, EDC release gating, and immutable greenlight certification records.
                    """,
                html: """
                    <h2 id="site-greenlight-activation-matrix">Site Startup &amp; Greenlight Activation Gate</h2>
                    <p>Establishes mandatory regulatory, ethics (IRB/IEC), contract, essential document, and training prerequisites required before certifying site activation on <code>/ssu</code>.</p>
                    <h2 id="ssu-greenlight-verification">Greenlight Verification</h2>
                    <ol>
                      <li>Confirm Essential Document List (EDL) artifacts and Delegation of Authority sign-offs are complete.</li>
                      <li>Verify country regulatory approval and IP pharmacy readiness before issuing greenlight certification.</li>
                    </ol>
                    <h3 id="global-country-activation-gates">Global Country Activation Gates</h3>
                    <p>Sponsor operations verify competent authority approvals, clinical trial agreements, and site roster readiness across participating countries.</p>
                    <h3 id="protocol-country-pack-alignment">Protocol Country-Pack Alignment</h3>
                    <p>Study designers align country-specific ICF templates and local assessment rules with the master USDM protocol.</p>
                    <h3 id="site-essential-documents-checklist">Site Essential Documents Checklist</h3>
                    <p>Site coordinators upload current IRB/IEC approvals, investigator CVs, medical licenses, and laboratory certifications.</p>
                    <h3 id="site-initiation-visit-greenlight">Site Initiation Visit &amp; Greenlight</h3>
                    <p>Monitors confirm Site Initiation Visit (SIV) completion and facility readiness prior to greenlight release.</p>
                    <h3 id="edc-environment-release-gate">EDC Environment Release Gate</h3>
                    <p>Data managers confirm production eCRF edit checks and randomization tables are validated before unlocking site enrollment.</p>
                    <h3 id="greenlight-certification-audit">Greenlight Certification Audit</h3>
                    <p>Auditors inspect timestamped greenlight tokens and prerequisite document completeness prior to first-patient-in.</p>
                    """,
                createdAt: baseTime.AddMinutes(-20),
                mappings:
                [
                    .. BuildRouteMappings(
                        prefix: "m-ssu",
                        exactRoute: "/ssu",
                        wildcardRoute: "/ssu/*",
                        articleId: "art-ssu-sop",
                        defaultAnchor: "#site-greenlight-activation-matrix",
                        personaAnchors: new Dictionary<string, string>
                        {
                            ["super_admin"] = "#global-country-activation-gates",
                            ["sponsor_designer"] = "#protocol-country-pack-alignment",
                            ["site_crc"] = "#site-essential-documents-checklist",
                            ["cra_monitor"] = "#site-initiation-visit-greenlight",
                            ["data_manager"] = "#edc-environment-release-gate",
                            ["auditor"] = "#greenlight-certification-audit",
                        },
                        priority: 25,
                        createdAt: baseTime.AddMinutes(-20)),
                    NewMapping(
                        "m-stc-q-site-activation",
                        "/studies/:studyCode?section=site-activation",
                        null,
                        "art-ssu-sop",
                        "#site-greenlight-activation-matrix",
                        50,
                        createdAt: baseTime.AddMinutes(-20)),
                    NewMapping(
                        "m-stc-q-sites",
                        "/studies/:studyCode?section=sites",
                        null,
                        "art-ssu-sop",
                        "#global-country-activation-gates",
                        50,
                        createdAt: baseTime.AddMinutes(-20)),
                    NewMapping(
                        "m-stc-q-startup-greenlight",
                        "/studies/:studyCode?section=startup-greenlight",
                        null,
                        "art-ssu-sop",
                        "#site-initiation-visit-greenlight",
                        50,
                        createdAt: baseTime.AddMinutes(-20)),
                ]),

            // 6. /mdr (Study Authoring & MDR)
            CreateSop(
                articleId: "art-mdr-sop",
                versionId: "ver-mdr-104-v2",
                slug: "sop-mdr-104-soa-matrix-authoring",
                title: "SOP-MDR-104: Schedule of Activities Matrix & Protocol Authoring",
                categoryId: "cat-study-authoring",
                versionLabel: "3.1",
                versionIndex: 3,
                tags: ["mdr", "usdm", "soa-matrix", "study-design"],
                markdown: """
                    ## Protocol Metadata Management & SoA Design
                    Standardizes CDISC USDM Schedule of Activities authoring and controlled terminology binding.

                    ## Authoring Steps
                    1. Bind every clinical activity and eCRF item to an approved CDISC Biomedical Concept and NCI Codelist version.
                    2. Document protocol amendment rationale and assess downstream participant visit impact before publishing a new version.
                    """,
                html: """
                    <h2 id="schedule-of-activities-matrix-authoring">Protocol Metadata Management &amp; SoA Design</h2>
                    <p>Standardizes CDISC USDM Schedule of Activities authoring and controlled terminology binding.</p>
                    <h2 id="mdr-authoring-steps">Authoring Steps</h2>
                    <ol>
                      <li>Bind every clinical activity and eCRF item to an approved CDISC Biomedical Concept and NCI Codelist version.</li>
                      <li>Document protocol amendment rationale and assess downstream participant visit impact before publishing a new version.</li>
                    </ol>
                    <h3 id="amendment-proposal-review">Amendment Proposal Review</h3>
                    <p>Review structural Schedule of Activities diffs, version lineage, and governance sign-off prerequisites before approving protocol proposals.</p>
                    """,
                createdAt: baseTime.AddMinutes(-15),
                mappings:
                [
                    .. BuildRouteMappings(
                        prefix: "m-mdr",
                        exactRoute: "/mdr",
                        wildcardRoute: "/mdr/*",
                        articleId: "art-mdr-sop",
                        defaultAnchor: "#schedule-of-activities-matrix-authoring",
                        personaAnchors: new Dictionary<string, string>(),
                        priority: 20,
                        createdAt: baseTime.AddMinutes(-15)),
                    NewMapping(
                        "m-stc-q-proposal",
                        "/studies/:studyCode?proposal=*",
                        null,
                        "art-mdr-sop",
                        "#amendment-proposal-review",
                        72,
                        createdAt: baseTime.AddMinutes(-15)),
                ]),

            // 7. /audit (21 CFR Part 11 Audit Trail Inspection)
            CreateSop(
                articleId: "art-audit-sop",
                versionId: "ver-aud-401-v2",
                slug: "sop-aud-401-audit-trail-inspection",
                title: "SOP-AUD-401: 21 CFR Part 11 Audit Trail & Qualification Inspection",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.1",
                versionIndex: 1,
                tags: ["audit", "part11", "gxp", "compliance"],
                markdown: """
                    ## Audit Trail Inspection Guidelines
                    Defines SHA-256 Merkle audit chain verification and regulatory inspection procedures.

                    ## Inspection Steps
                    1. Filter immutable audit records by study, actor, entity, and UTC date window while preserving read-only inspection posture.
                    2. Verify predecessor and block SHA-256 hashes prior to exporting signed regulatory inspection bundles.
                    """,
                html: """
                    <h2 id="gxp-audit-trail-inspection">Audit Trail Inspection Guidelines</h2>
                    <p>Defines SHA-256 Merkle audit chain verification and regulatory inspection procedures.</p>
                    <h2 id="audit-inspection-steps">Inspection Steps</h2>
                    <ol>
                      <li>Filter immutable audit records by study, actor, entity, and UTC date window while preserving read-only inspection posture.</li>
                      <li>Verify predecessor and block SHA-256 hashes prior to exporting signed regulatory inspection bundles.</li>
                    </ol>
                    """,
                createdAt: baseTime.AddMinutes(-10),
                mappings: BuildRouteMappings(
                    prefix: "m-aud",
                    exactRoute: "/audit",
                    wildcardRoute: "/audit/*",
                    articleId: "art-audit-sop",
                    defaultAnchor: "#gxp-audit-trail-inspection",
                    personaAnchors: new Dictionary<string, string>(),
                    priority: 20,
                    createdAt: baseTime.AddMinutes(-10))),

            // 8. /overview (Clinical Operations Command Center)
            CreateSop(
                articleId: "art-overview-sop",
                versionId: "ver-ops-100-v2",
                slug: "sop-ops-100-clinical-command-center",
                title: "SOP-OPS-100: Clinical Operations Command Center & Action Queue Triage",
                categoryId: "cat-clinical-ops",
                versionLabel: "1.1",
                versionIndex: 1,
                tags: ["overview", "command-center", "triage", "portfolio"],
                markdown: """
                    ## Purpose & Scope
                    Governs daily review of clinical portfolio KPIs, pending operational tasks, safety escalations, and query backlogs across `/overview`.

                    ## Operational Steps
                    1. Review high-priority safety, SDV, and query alerts at workspace login.
                    2. Navigate directly to study, site, or support queues requiring immediate GxP follow-up.
                    """,
                html: """
                    <h2 id="clinical-command-center-triage">Purpose &amp; Scope</h2>
                    <p>Governs daily review of clinical portfolio KPIs, pending operational tasks, safety escalations, and query backlogs across <code>/overview</code>.</p>
                    <h2 id="overview-operational-steps">Operational Steps</h2>
                    <ol>
                      <li>Review high-priority safety, SDV, and query alerts at workspace login.</li>
                      <li>Navigate directly to study, site, or support queues requiring immediate GxP follow-up.</li>
                    </ol>
                    <h3 id="pharmacovigilance-signal-overview">Pharmacovigilance Signal Overview</h3>
                    <p>Safety officers prioritize expedited serious adverse event alerts and medical review escalations upon sign-in.</p>
                    <h3 id="investigator-oversight-summary">Investigator Oversight Summary</h3>
                    <p>Principal Investigators review pending visit signatures, safety notifications, and site delegation alerts.</p>
                    """,
                createdAt: baseTime.AddMinutes(-9),
                mappings: BuildRouteMappings(
                    prefix: "m-ovw",
                    exactRoute: "/overview",
                    wildcardRoute: "/overview/*",
                    articleId: "art-overview-sop",
                    defaultAnchor: "#clinical-command-center-triage",
                    personaAnchors: new Dictionary<string, string>
                    {
                        ["safety_officer"] = "#pharmacovigilance-signal-overview",
                        ["site_investigator"] = "#investigator-oversight-summary",
                    },
                    priority: 25,
                    createdAt: baseTime.AddMinutes(-9))),

            // 9. /plans (Study Execution Plans & Operational Milestones)
            CreateSop(
                articleId: "art-plans-sop",
                versionId: "ver-pln-105-v2",
                slug: "sop-pln-105-study-execution-plans",
                title: "SOP-PLN-105: Study Execution Plans, Milestones & Operational Readiness",
                categoryId: "cat-site-monitoring",
                versionLabel: "1.1",
                versionIndex: 1,
                tags: ["plans", "milestones", "ctms", "readiness"],
                markdown: """
                    ## Study Execution Plan Governance
                    Defines milestone tracking, enrollment forecasting, and cross-functional study execution plans on `/plans`.

                    ## Key Controls
                    1. Reconcile country and site activation dates with approved protocol amendments.
                    2. Escalate milestone slippage with documented root-cause context.
                    """,
                html: """
                    <h2 id="study-execution-plan-milestones">Study Execution Plan Governance</h2>
                    <p>Defines milestone tracking, enrollment forecasting, and cross-functional study execution plans on <code>/plans</code>.</p>
                    <h2 id="plans-key-controls">Key Controls</h2>
                    <ol>
                      <li>Reconcile country and site activation dates with approved protocol amendments.</li>
                      <li>Escalate milestone slippage with documented root-cause context.</li>
                    </ol>
                    """,
                createdAt: baseTime.AddMinutes(-8),
                mappings: BuildRouteMappings(
                    prefix: "m-pln",
                    exactRoute: "/plans",
                    wildcardRoute: "/plans/*",
                    articleId: "art-plans-sop",
                    defaultAnchor: "#study-execution-plan-milestones",
                    personaAnchors: new Dictionary<string, string>(),
                    priority: 25,
                    createdAt: baseTime.AddMinutes(-8))),

            // 10. /onboarding/trial (Clinical Trial Onboarding & Protocol Intake)
            CreateSop(
                articleId: "art-onboarding-trial-sop",
                versionId: "ver-onb-110-v2",
                slug: "sop-onb-110-clinical-trial-onboarding",
                title: "SOP-ONB-110: Clinical Trial Onboarding, Protocol Intake & Site Provisioning",
                categoryId: "cat-site-startup",
                versionLabel: "1.2",
                versionIndex: 1,
                tags: ["onboarding", "trial-setup", "provisioning", "protocol"],
                markdown: """
                    ## Clinical Trial Onboarding Procedure
                    Establishes controlled onboarding steps for new study protocols, organization tenancy bindings, and initial site rosters on `/onboarding/trial`.

                    ## Mandatory Checks
                    1. Validate sponsor protocol identifier, therapeutic area, and regulatory phase before workspace creation.
                    2. Verify initial study team RBAC assignments and training curriculum bindings prior to activation.
                    """,
                html: """
                    <h2 id="clinical-trial-onboarding">Clinical Trial Onboarding Procedure</h2>
                    <p>Establishes controlled onboarding steps for new study protocols, organization tenancy bindings, and initial site rosters on <code>/onboarding/trial</code>.</p>
                    <h2 id="onboarding-mandatory-checks">Mandatory Checks</h2>
                    <ol>
                      <li>Validate sponsor protocol identifier, therapeutic area, and regulatory phase before workspace creation.</li>
                      <li>Verify initial study team RBAC assignments and training curriculum bindings prior to activation.</li>
                    </ol>
                    """,
                createdAt: baseTime.AddMinutes(-7),
                mappings: BuildOnboardingMappings(
                    articleId: "art-onboarding-trial-sop",
                    createdAt: baseTime.AddMinutes(-7))),

            // 11. /etmf (Electronic Trial Master File Artifact Filing & QC)
            CreateSop(
                articleId: "art-etmf-sop",
                versionId: "ver-tmf-301-v2",
                slug: "sop-tmf-301-etmf-artifact-filing",
                title: "SOP-TMF-301: Electronic Trial Master File (eTMF) Artifact Filing & QC",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.4",
                versionIndex: 1,
                tags: ["etmf", "dia-reference-model", "essential-documents", "qc"],
                markdown: """
                    ## eTMF Contemporaneous Filing & Inspection Readiness
                    Governs DIA Reference Model classification, artifact completeness review, and regulatory inspection readiness across `/etmf`.

                    ## Operational Controls
                    1. File essential regulatory and ethics artifacts under the matching zone, section, and study/site scope.
                    2. Verify document legibility, metadata completeness, and immutable hash integrity during QC review.
                    """,
                html: """
                    <h2 id="etmf-artifact-filing-and-qc">eTMF Contemporaneous Filing &amp; Inspection Readiness</h2>
                    <p>Governs DIA Reference Model classification, artifact completeness review, and regulatory inspection readiness across <code>/etmf</code>.</p>
                    <h2 id="etmf-operational-controls">Operational Controls</h2>
                    <ol>
                      <li>File essential regulatory and ethics artifacts under the matching zone, section, and study/site scope.</li>
                      <li>Verify document legibility, metadata completeness, and immutable hash integrity during QC review.</li>
                    </ol>
                    <h3 id="etmf-inspection-readiness-verification">eTMF Inspection Readiness Verification</h3>
                    <p>Auditors inspect DIA Reference Model completeness, certified copy attestations, and immutable filing timestamps.</p>
                    <h3 id="investigator-site-file-attestation">Investigator Site File Attestation</h3>
                    <p>Principal Investigators verify site-level regulatory binder synchronization and essential document currency.</p>
                    """,
                createdAt: baseTime.AddMinutes(-6),
                mappings:
                [
                    .. BuildRouteMappings(
                        prefix: "m-tmf",
                        exactRoute: "/etmf",
                        wildcardRoute: "/etmf/*",
                        articleId: "art-etmf-sop",
                        defaultAnchor: "#etmf-artifact-filing-and-qc",
                        personaAnchors: new Dictionary<string, string>
                        {
                            ["auditor"] = "#etmf-inspection-readiness-verification",
                            ["site_investigator"] = "#investigator-site-file-attestation",
                        },
                        priority: 25,
                        createdAt: baseTime.AddMinutes(-6)),
                    NewMapping(
                        "m-stc-q-documents",
                        "/studies/:studyCode?section=documents",
                        null,
                        "art-etmf-sop",
                        "#etmf-artifact-filing-and-qc",
                        50,
                        createdAt: baseTime.AddMinutes(-6)),
                ]),

            // 12. /training (GxP Training Curriculum & Delegation Readiness)
            CreateSop(
                articleId: "art-training-sop",
                versionId: "ver-trn-202-v2",
                slug: "sop-trn-202-gxp-training-attestation",
                title: "SOP-TRN-202: GxP Training Curriculum Completion & Delegation Sign-Off",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.3",
                versionIndex: 1,
                tags: ["training", "delegation", "ich-e6", "qualification"],
                markdown: """
                    ## GxP Role Qualification & Training Attestation
                    Defines mandatory SOP read-and-understand training completion and Principal Investigator Delegation of Authority prerequisites on `/training`.

                    ## Qualification Rules
                    1. Complete assigned protocol and GxP SOP modules prior to performing delegated clinical duties.
                    2. Maintain current training records across protocol amendments to preserve active write access.
                    """,
                html: """
                    <h2 id="gxp-training-and-delegation">GxP Role Qualification &amp; Training Attestation</h2>
                    <p>Defines mandatory SOP read-and-understand training completion and Principal Investigator Delegation of Authority prerequisites on <code>/training</code>.</p>
                    <h2 id="training-qualification-rules">Qualification Rules</h2>
                    <ol>
                      <li>Complete assigned protocol and GxP SOP modules prior to performing delegated clinical duties.</li>
                      <li>Maintain current training records across protocol amendments to preserve active write access.</li>
                    </ol>
                    <h3 id="pi-delegation-log-signoff">PI Delegation Log Sign-Off</h3>
                    <p>Principal Investigators verify completed staff training attestations before countersigning Delegation of Authority entries.</p>
                    <h3 id="site-staff-training-attestation">Site Staff Training Attestation</h3>
                    <p>Site coordinators complete read-and-understand attestations for protocol amendments prior to executing clinical visits.</p>
                    """,
                createdAt: baseTime.AddMinutes(-5),
                mappings:
                [
                    .. BuildRouteMappings(
                        prefix: "m-trn",
                        exactRoute: "/training",
                        wildcardRoute: "/training/*",
                        articleId: "art-training-sop",
                        defaultAnchor: "#gxp-training-and-delegation",
                        personaAnchors: new Dictionary<string, string>
                        {
                            ["site_investigator"] = "#pi-delegation-log-signoff",
                            ["site_crc"] = "#site-staff-training-attestation",
                        },
                        priority: 25,
                        createdAt: baseTime.AddMinutes(-5)),
                    NewMapping(
                        "m-stc-q-training",
                        "/studies/:studyCode?section=training",
                        null,
                        "art-training-sop",
                        "#gxp-training-and-delegation",
                        50,
                        createdAt: baseTime.AddMinutes(-5)),
                ]),

            // 13. /observations/:observationRef (Clinical Observation Provenance & Query Review)
            CreateSop(
                articleId: "art-observations-sop",
                versionId: "ver-obs-210-v2",
                slug: "sop-obs-210-clinical-observation-inspection",
                title: "SOP-OBS-210: Clinical Observation Inspection, Query History & Provenance Review",
                categoryId: "cat-data-management",
                versionLabel: "1.1",
                versionIndex: 1,
                tags: ["observations", "provenance", "sdv", "queries", "safety"],
                markdown: """
                    ## Clinical Observation Provenance & Discrepancy Review
                    Standardizes item-level observation inspection, audit provenance verification, medical safety review, and query resolution on `/observations/:observationRef`.

                    ## Key Controls
                    1. Inspect source provenance, prior amendments, and open queries before updating or verifying an observation.
                    2. Escalate clinically significant safety abnormalities or MedDRA coding questions with pseudonymous subject codes only.
                    """,
                html: """
                    <h2 id="clinical-observation-provenance-review">Clinical Observation Provenance &amp; Discrepancy Review</h2>
                    <p>Standardizes item-level observation inspection, audit provenance verification, medical safety review, and query resolution on <code>/observations/:observationRef</code>.</p>
                    <h2 id="observation-key-controls">Key Controls</h2>
                    <ol>
                      <li>Inspect source provenance, prior amendments, and open queries before updating or verifying an observation.</li>
                      <li>Escalate clinically significant safety abnormalities or MedDRA coding questions with pseudonymous subject codes only.</li>
                    </ol>
                    <h3 id="safety-observation-medical-review">Safety Observation Medical Review</h3>
                    <p>Safety officers evaluate abnormal laboratory values and adverse event observations for expedited reporting criteria.</p>
                    <h3 id="investigator-clinical-significance-signoff">Investigator Clinical Significance Sign-Off</h3>
                    <p>Investigators record clinical significance determinations and contemporaneous medical rationale on flagged observations.</p>
                    <h3 id="source-observation-amendment">Source Observation Amendment</h3>
                    <p>Site coordinators submit source-verified corrections with mandatory 21 CFR Part 11 reasons for change.</p>
                    <h3 id="observation-sdv-verification">Observation SDV Verification</h3>
                    <p>Monitors compare recorded kernel observations against clinic source charts and record SDV status.</p>
                    <h3 id="observation-discrepancy-query-resolution">Observation Discrepancy Query Resolution</h3>
                    <p>Data managers review edit-check lineage and verify query responses before closing item-level discrepancies.</p>
                    <h3 id="observation-provenance-audit-trail">Observation Provenance Audit Trail</h3>
                    <p>Auditors inspect the append-only revision chain, actor identity, and UTC timestamps for every observation mutation.</p>
                    """,
                createdAt: baseTime.AddMinutes(-4),
                mappings:
                [
                    .. BuildParameterizedObservationMappings(
                        articleId: "art-observations-sop",
                        createdAt: baseTime.AddMinutes(-4)),
                    NewMapping(
                        "m-stc-q-observation",
                        "/studies/:studyCode?observation=*",
                        null,
                        "art-observations-sop",
                        "#clinical-observation-provenance-review",
                        70,
                        createdAt: baseTime.AddMinutes(-4)),
                ]),

            // 14. /tickets (GxP Support Ticket Triage & Part 11 Closure)
            CreateSop(
                articleId: "art-tickets-sop",
                versionId: "ver-tck-501-v2",
                slug: "sop-tck-501-gxp-support-ticket-lifecycle",
                title: "SOP-TCK-501: GxP Support Ticket Triage, Root-Cause CAPA & Part 11 Closure",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.1",
                versionIndex: 1,
                tags: ["tickets", "support", "capa", "sla", "part11"],
                markdown: """
                    ## GxP Support Ticket Triage & Closure Governance
                    Defines severity classification, SLA pause/resume tracking, participant de-identification rules, root-cause documentation, and 21 CFR Part 11 electronic signatures on `/tickets`.

                    ## Mandatory Rules
                    1. Never enter direct participant PII in ticket descriptions, comments, or root-cause summaries; use pseudonymous subject codes (for example `SUBJ-1001`).
                    2. Record structured resolution codes and root-cause summaries prior to resolving tickets and applying Part 11 closure signatures.
                    """,
                html: """
                    <h2 id="gxp-support-ticket-lifecycle">GxP Support Ticket Triage &amp; Closure Governance</h2>
                    <p>Defines severity classification, SLA pause/resume tracking, participant de-identification rules, root-cause documentation, and 21 CFR Part 11 electronic signatures on <code>/tickets</code>.</p>
                    <h2 id="tickets-mandatory-rules">Mandatory Rules</h2>
                    <ol>
                      <li>Never enter direct participant PII in ticket descriptions, comments, or root-cause summaries; use pseudonymous subject codes (for example <code>SUBJ-1001</code>).</li>
                      <li>Record structured resolution codes and root-cause summaries prior to resolving tickets and applying Part 11 closure signatures.</li>
                    </ol>
                    """,
                createdAt: baseTime.AddMinutes(-3),
                mappings: BuildRouteMappings(
                    prefix: "m-tck",
                    exactRoute: "/tickets",
                    wildcardRoute: "/tickets/*",
                    articleId: "art-tickets-sop",
                    defaultAnchor: "#gxp-support-ticket-lifecycle",
                    personaAnchors: new Dictionary<string, string>(),
                    priority: 25,
                    createdAt: baseTime.AddMinutes(-3))),

            // 15. /knowledge (Controlled SOP Knowledge Hub Governance)
            CreateSop(
                articleId: "art-knowledge-sop",
                versionId: "ver-knb-502-v2",
                slug: "sop-knb-502-controlled-knowledge-hub",
                title: "SOP-KNB-502: Controlled SOP Knowledge Hub & Regulatory Version Governance",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.1",
                versionIndex: 1,
                tags: ["knowledge", "sop", "versioning", "part11"],
                markdown: """
                    ## Controlled SOP Repository Usage
                    Governs role-scoped SOP lookup, locked snapshot inspection, and 21 CFR Part 11 Merkle audit verification on `/knowledge`.

                    ## Operational Steps
                    1. Verify that referenced SOP guidance carries `PUBLISHED` status and a locked version snapshot before executing regulated procedures.
                    2. Inspect the 21 CFR Part 11 audit trail tab to verify cryptographic SHA-256 block hashes during quality or regulatory reviews.
                    """,
                html: """
                    <h2 id="controlled-sop-repository-governance">Controlled SOP Repository Usage</h2>
                    <p>Governs role-scoped SOP lookup, locked snapshot inspection, and 21 CFR Part 11 Merkle audit verification on <code>/knowledge</code>.</p>
                    <h2 id="knowledge-operational-steps">Operational Steps</h2>
                    <ol>
                      <li>Verify that referenced SOP guidance carries <code>PUBLISHED</code> status and a locked version snapshot before executing regulated procedures.</li>
                      <li>Inspect the 21 CFR Part 11 audit trail tab to verify cryptographic SHA-256 block hashes during quality or regulatory reviews.</li>
                    </ol>
                    """,
                createdAt: baseTime.AddMinutes(-2),
                mappings: BuildRouteMappings(
                    prefix: "m-knb",
                    exactRoute: "/knowledge",
                    wildcardRoute: "/knowledge/*",
                    articleId: "art-knowledge-sop",
                    defaultAnchor: "#controlled-sop-repository-governance",
                    personaAnchors: new Dictionary<string, string>(),
                    priority: 25,
                    createdAt: baseTime.AddMinutes(-2))),
        };

        return list;
    }

    private static SopSeed CreateSop(
        string articleId,
        string versionId,
        string slug,
        string title,
        string categoryId,
        string versionLabel,
        int versionIndex,
        string[] tags,
        string markdown,
        string html,
        DateTimeOffset createdAt,
        IReadOnlyList<ContextualHelpMapping> mappings)
    {
        var article = new KnowledgeArticle
        {
            Id = articleId,
            Slug = slug,
            Title = title,
            CategoryId = categoryId,
            Status = "PUBLISHED",
            VersionLabel = versionLabel,
            CurrentPublishedVersionId = versionId,
            Tags = JsonSerializer.Serialize(tags),
            AuthorUserId = "lead_clinical_ops",
            LastEditedBy = "lead_clinical_ops",
            ApprovedBy = "qa_director",
            IsDeleted = false,
            CreatedAt = createdAt,
            CreatedBy = CatalogServiceAccount,
            ReasonForChange = SeedReason,
            VersionIndex = versionIndex,
        };

        var version = new KnowledgeArticleVersion
        {
            Id = versionId,
            ArticleId = articleId,
            VersionIndex = versionIndex,
            VersionLabel = versionLabel,
            BodyMarkdown = markdown,
            BodyHtml = html,
            StatusAtSnapshot = "PUBLISHED",
            IsLocked = true,
            CreatedAt = createdAt,
            CreatedBy = CatalogServiceAccount,
            ReasonForChange = SeedReason,
        };

        return new SopSeed(article, version, mappings);
    }

    private static List<ContextualHelpMapping> BuildRouteMappings(
        string prefix,
        string exactRoute,
        string wildcardRoute,
        string articleId,
        string defaultAnchor,
        IReadOnlyDictionary<string, string> personaAnchors,
        int priority,
        DateTimeOffset createdAt)
    {
        var list = new List<ContextualHelpMapping>
        {
            NewMapping($"{prefix}-ex-any", exactRoute, null, articleId, defaultAnchor, priority, createdAt),
            NewMapping($"{prefix}-wc-any", wildcardRoute, null, articleId, defaultAnchor, priority - 5, createdAt),
        };

        foreach (string persona in AllStaffPersonas)
        {
            string anchor = personaAnchors.TryGetValue(persona, out string? custom) ? custom : defaultAnchor;
            list.Add(NewMapping($"{prefix}-ex-{persona}", exactRoute, persona, articleId, anchor, priority + 5, createdAt));
            list.Add(NewMapping($"{prefix}-wc-{persona}", wildcardRoute, persona, articleId, anchor, priority, createdAt));
        }

        return list;
    }

    private static List<ContextualHelpMapping> BuildParameterizedStudyMappings(string articleId, DateTimeOffset createdAt)
    {
        var personaAnchors = new Dictionary<string, string>
        {
            ["super_admin"] = "#study-governance-and-locks",
            ["sponsor_designer"] = "#visit-schedule-and-form-definitions",
            ["site_crc"] = "#subject-enrollment-and-visit-capture",
            ["site_investigator"] = "#investigator-oversight-and-signoff",
            ["cra_monitor"] = "#study-sdv-and-query-oversight",
            ["data_manager"] = "#study-query-triage-and-cleanliness",
            ["safety_officer"] = "#study-safety-signal-review",
            ["auditor"] = "#study-audit-trail-inspection",
        };

        var list = new List<ContextualHelpMapping>
        {
            NewMapping("m-stp-any", "/studies/:studyCode", null, articleId, "#active-study-clinical-canvas", 30, createdAt),
            NewMapping("m-stps-any", "/studies/:studyCode/*", null, articleId, "#active-study-clinical-canvas", 28, createdAt),
            NewMapping("m-stc-q-protocol-versions", "/studies/:studyCode?section=protocol-versions", null, articleId, "#protocol-versions-and-soa-design", 50, createdAt),
            NewMapping("m-stc-q-participants", "/studies/:studyCode?section=participants", null, articleId, "#participant-enrollment-and-visit-matrix", 50, createdAt),
            NewMapping("m-stc-q-locks", "/studies/:studyCode?section=locks", null, articleId, "#database-freeze-and-lock-controls", 50, createdAt),
            NewMapping("m-stc-q-exports", "/studies/:studyCode?section=exports", null, articleId, "#regulatory-submission-and-exports", 50, createdAt),
            NewMapping("m-stc-q-supply", "/studies/:studyCode?section=supply", null, articleId, "#investigational-product-and-rtsm-supply", 50, createdAt),
            NewMapping("m-stc-q-validation", "/studies/:studyCode?section=validation", null, articleId, "#protocol-validation-and-release-checks", 50, createdAt),
            NewMapping("m-stc-q-participant", "/studies/:studyCode?participant=*", null, articleId, "#participant-timeline-and-visit-record", 60, createdAt),
        };

        foreach (string persona in AllStaffPersonas)
        {
            string anchor = personaAnchors.TryGetValue(persona, out string? custom)
                ? custom
                : "#active-study-clinical-canvas";
            list.Add(NewMapping($"m-stp-{persona}", "/studies/:studyCode", persona, articleId, anchor, 35, createdAt));
            list.Add(NewMapping($"m-stps-{persona}", "/studies/:studyCode/*", persona, articleId, anchor, 32, createdAt));
        }

        return list;
    }

    private static List<ContextualHelpMapping> BuildOnboardingMappings(string articleId, DateTimeOffset createdAt)
    {
        const string defaultAnchor = "#clinical-trial-onboarding";
        var list = new List<ContextualHelpMapping>
        {
            NewMapping("m-onb-ex-any", "/onboarding/trial", null, articleId, defaultAnchor, 25, createdAt),
            NewMapping("m-onb-wc-any", "/onboarding/trial/*", null, articleId, defaultAnchor, 22, createdAt),
            NewMapping("m-onb-rt-any", "/onboarding/*", null, articleId, defaultAnchor, 18, createdAt),
        };

        foreach (string persona in AllStaffPersonas)
        {
            list.Add(NewMapping($"m-onb-ex-{persona}", "/onboarding/trial", persona, articleId, defaultAnchor, 30, createdAt));
            list.Add(NewMapping($"m-onb-wc-{persona}", "/onboarding/trial/*", persona, articleId, defaultAnchor, 26, createdAt));
            list.Add(NewMapping($"m-onb-rt-{persona}", "/onboarding/*", persona, articleId, defaultAnchor, 20, createdAt));
        }

        return list;
    }

    private static List<ContextualHelpMapping> BuildParameterizedObservationMappings(string articleId, DateTimeOffset createdAt)
    {
        var personaAnchors = new Dictionary<string, string>
        {
            ["safety_officer"] = "#safety-observation-medical-review",
            ["site_investigator"] = "#investigator-clinical-significance-signoff",
            ["site_crc"] = "#source-observation-amendment",
            ["cra_monitor"] = "#observation-sdv-verification",
            ["data_manager"] = "#observation-discrepancy-query-resolution",
            ["auditor"] = "#observation-provenance-audit-trail",
        };
        const string defaultAnchor = "#clinical-observation-provenance-review";

        var list = new List<ContextualHelpMapping>
        {
            NewMapping("m-obs-ex-any", "/observations", null, articleId, defaultAnchor, 25, createdAt),
            NewMapping("m-obs-p-any", "/observations/:observationRef", null, articleId, defaultAnchor, 30, createdAt),
            NewMapping("m-obs-ps-any", "/observations/:observationRef/*", null, articleId, defaultAnchor, 28, createdAt),
            NewMapping("m-obs-wc-any", "/observations/*", null, articleId, defaultAnchor, 20, createdAt),
        };

        foreach (string persona in AllStaffPersonas)
        {
            string anchor = personaAnchors.TryGetValue(persona, out string? custom) ? custom : defaultAnchor;
            list.Add(NewMapping($"m-obs-ex-{persona}", "/observations", persona, articleId, anchor, 30, createdAt));
            list.Add(NewMapping($"m-obs-p-{persona}", "/observations/:observationRef", persona, articleId, anchor, 35, createdAt));
            list.Add(NewMapping($"m-obs-ps-{persona}", "/observations/:observationRef/*", persona, articleId, anchor, 32, createdAt));
            list.Add(NewMapping($"m-obs-wc-{persona}", "/observations/*", persona, articleId, anchor, 24, createdAt));
        }

        return list;
    }

    private static ContextualHelpMapping NewMapping(
        string id,
        string routePattern,
        string? persona,
        string articleId,
        string sectionAnchor,
        int priority,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = id,
            RoutePattern = routePattern,
            Persona = persona,
            ArticleId = articleId,
            SectionAnchor = sectionAnchor,
            Priority = priority,
            IsActive = true,
            CreatedAt = createdAt,
            CreatedBy = CatalogServiceAccount,
            ReasonForChange = SeedReason,
            VersionIndex = 1,
        };
}
