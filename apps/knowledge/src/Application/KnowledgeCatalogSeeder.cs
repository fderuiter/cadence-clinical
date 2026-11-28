using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Audit;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Apps.Knowledge.Application;

/// <summary>
/// Ambient actor override used strictly when bootstrapping controlled baseline SOPs so the shared
/// <c>GxPAuditInterceptor</c> attributes system-seeded records to <c>svc:knowledge-sop-catalog</c> rather than
/// an interactive reader (including read-only auditors).
/// </summary>
public static class KnowledgeAuditActorScope
{
    public static string? CurrentUserId => AmbientAuditActorScope.Current?.Actor;

    public static IDisposable Begin(
        string actorUserId,
        string changeReason = "Initial controlled SOP baseline publication") =>
        AmbientAuditActorScope.BeginScope(actorUserId, changeReason);
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

    private static readonly ConditionalWeakTable<DbConnection, ConcurrentDictionary<string, byte>> SeededTenantsByConnection = new();

    private static readonly ConcurrentDictionary<string, byte> SeededTenantsByConnectionKey = new(StringComparer.Ordinal);

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

    private const int ExpectedSeededSopCount = 33;

    public static async Task EnsureSeededAsync(KnowledgeDbContext db, IClock clock, CancellationToken ct = default)
    {
        var ext = db.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        Guid? resolvedTenantId = ext?.TenantAccessor is { } accessor
            ? await accessor.GetCurrentTenantIdAsync(ct).ConfigureAwait(false)
            : null;
        string tenantSuffix = FormatSeedTenantSuffix(resolvedTenantId);
        if (IsTenantSeeded(db, tenantSuffix))
        {
            return;
        }

        string latestMappingSentinelId = $"m-stc-q-observation{tenantSuffix}";

        // Fast path: once complete HTML snapshots, Canvas section SOPs, and zoom mappings exist for this tenant, skip locking and writes.
        if (await HasCompleteCatalogBaselineAsync(db, latestMappingSentinelId, ct).ConfigureAwait(false))
        {
            MarkTenantSeeded(db, tenantSuffix);
            return;
        }

        await SeedLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsTenantSeeded(db, tenantSuffix)
                || await HasCompleteCatalogBaselineAsync(db, latestMappingSentinelId, ct).ConfigureAwait(false))
            {
                MarkTenantSeeded(db, tenantSuffix);
                return;
            }

            IDisposable? boundSeedTenantScope = resolvedTenantId is { } activeTenantId
                && activeTenantId != Guid.Empty
                && ext?.TenantAccessor is ITenantScope tenantScope
                    ? tenantScope.Bind(activeTenantId)
                    : null;
            using var _boundScope = boundSeedTenantScope;
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
                .Select(v => new { v.Id, v.ArticleId, v.VersionIndex, v.BodyMarkdown, v.BodyHtml })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var existingVersionsById = existingVersions
                .ToDictionary(v => v.Id, StringComparer.Ordinal);

            var existingVersionIds = existingVersionsById.Keys
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
                        existingVersionIds.Add(seed.Version.Id);
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
                else
                {
                    bool missingSeedVersion = !existingVersionIds.Contains(seed.Version.Id);
                    bool currentPublishedIncomplete = existingArticle.CurrentPublishedVersionId is null
                        || !existingVersionsById.TryGetValue(existingArticle.CurrentPublishedVersionId, out var currentPublished)
                        || !SopHtmlCompiler.HasCompleteHeadingIdsAndSteps(currentPublished.BodyMarkdown, currentPublished.BodyHtml);

                    if (missingSeedVersion || currentPublishedIncomplete)
                    {
                        int nextVersionIndex = Math.Max(
                            existingArticle.VersionIndex,
                            maxVersionIndexByArticleId.GetValueOrDefault(existingArticle.Id, 0)) + 1;

                        string upgradedVersionId = missingSeedVersion
                            ? seed.Version.Id
                            : BuildNextSeedVersionId(seed.Version.Id, existingVersionIds, tenantSuffix, nextVersionIndex);
                        existingVersionIds.Add(upgradedVersionId);

                        var upgradedVersion = new KnowledgeArticleVersion
                        {
                            Id = upgradedVersionId,
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
                }

                foreach (var mapping in seed.Mappings.Where(m => !existingMappingIds.Contains(m.Id)))
                {
                    db.ContextualHelpMappings.Add(mapping);
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            MarkTenantSeeded(db, tenantSuffix);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static bool IsTenantSeeded(KnowledgeDbContext db, string tenantSuffix)
    {
        var (connectionInstance, connectionKey) = ResolveConnectionMemoKey(db, tenantSuffix);
        if (connectionInstance is not null)
        {
            return SeededTenantsByConnection.TryGetValue(connectionInstance, out var tenants)
                   && tenants.ContainsKey(tenantSuffix);
        }

        return connectionKey is not null && SeededTenantsByConnectionKey.ContainsKey(connectionKey);
    }

    private static void MarkTenantSeeded(KnowledgeDbContext db, string tenantSuffix)
    {
        var (connectionInstance, connectionKey) = ResolveConnectionMemoKey(db, tenantSuffix);
        if (connectionInstance is not null)
        {
            var tenants = SeededTenantsByConnection.GetOrCreateValue(connectionInstance);
            tenants.TryAdd(tenantSuffix, 0);
            return;
        }

        if (connectionKey is not null)
        {
            SeededTenantsByConnectionKey.TryAdd(connectionKey, 0);
        }
    }

    private static (DbConnection? ConnectionInstance, string? ConnectionKey) ResolveConnectionMemoKey(
        KnowledgeDbContext db,
        string tenantSuffix)
    {
        var relationalExt = db.GetService<IDbContextOptions>()?
            .Extensions
            .OfType<RelationalOptionsExtension>()
            .FirstOrDefault();

        if (relationalExt?.Connection is not null)
        {
            return (relationalExt.Connection, null);
        }

        string? configuredConnectionString = relationalExt?.ConnectionString;
        if (string.IsNullOrWhiteSpace(configuredConnectionString) && db.Database.IsRelational())
        {
            configuredConnectionString = db.Database.GetConnectionString();
        }

        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            bool isPrivateInMemory =
                (configuredConnectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
                 || configuredConnectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
                && !configuredConnectionString.Contains("Cache=Shared", StringComparison.OrdinalIgnoreCase);

            if (isPrivateInMemory)
            {
                return (db.Database.GetDbConnection(), null);
            }

            string providerName = relationalExt?.GetType().FullName ?? "relational";
            return (null, $"{providerName}|{configuredConnectionString}|{tenantSuffix}");
        }

        if (db.Database.IsRelational())
        {
            return (db.Database.GetDbConnection(), null);
        }

        return (null, null);
    }

    private static string BuildNextSeedVersionId(
        string currentSeedVersionId,
        IReadOnlySet<string> existingVersionIds,
        string tenantSuffix,
        int startIndex)
    {
        string baseId = tenantSuffix.Length > 0 && currentSeedVersionId.EndsWith(tenantSuffix, StringComparison.Ordinal)
            ? currentSeedVersionId[..^tenantSuffix.Length]
            : currentSeedVersionId;

        int vPos = baseId.LastIndexOf("-v", StringComparison.Ordinal);
        string stem = vPos > 0 ? baseId[..vPos] : baseId;
        int idx = Math.Max(3, startIndex);
        while (true)
        {
            string candidateBase = $"{stem}-v{idx}";
            if (candidateBase.Length > 27)
            {
                candidateBase = $"{stem[..Math.Max(1, 27 - ($"-v{idx}").Length)]}-v{idx}";
            }

            string candidate = $"{candidateBase}{tenantSuffix}";
            if (!existingVersionIds.Contains(candidate))
            {
                return candidate;
            }

            idx++;
        }
    }

    private static async Task<bool> HasCompleteCatalogBaselineAsync(
        KnowledgeDbContext db,
        string latestMappingSentinelId,
        CancellationToken ct)
    {
        if (!await db.ContextualHelpMappings
                .AsNoTracking()
                .AnyAsync(m => m.Id == latestMappingSentinelId, ct)
                .ConfigureAwait(false))
        {
            return false;
        }

        var seededStates = await (
            from a in db.KnowledgeArticles.AsNoTracking()
            where a.CreatedBy == CatalogServiceAccount && !a.IsDeleted
            join v in db.KnowledgeArticleVersions.AsNoTracking()
                on a.CurrentPublishedVersionId equals v.Id into vj
            from v in vj.DefaultIfEmpty()
            select new
            {
                IsComplete = v != null
                    && v.BodyHtml != null
                    && v.BodyHtml.Contains("id=\"")
                    && (v.BodyMarkdown == null || !v.BodyMarkdown.Contains("1. ") || v.BodyHtml.Contains("<ol>"))
                    && !v.BodyHtml.Contains("<h2>")
                    && !v.BodyHtml.Contains("<h3>")
                    && !v.BodyHtml.Contains("<h4>"),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return seededStates.Count >= ExpectedSeededSopCount && seededStates.All(s => s.IsComplete);
    }

    private static string FormatSeedTenantSuffix(Guid? tenantId)
    {
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
                versionId: "ver-studies-port-101-v2",
                slug: "sop-std-101-study-portfolio-governance",
                title: "SOP-STD-101: Study Portfolio Governance & Workspace Selection",
                categoryId: "cat-study-authoring",
                versionLabel: "2.1",
                versionIndex: 2,
                tags: ["studies", "portfolio", "governance", "usdm"],
                primaryAnchor: "#study-portfolio-governance",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Operational Steps"] = "portfolio-operational-steps",
                },
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

            // 2. /studies/:studyCode (Parameterized Study Clinical Canvas & participant zoom) — 3 procedural steps
            CreateSop(
                articleId: "art-study-canvas-sop",
                versionId: "ver-study-canvas-102-v2",
                slug: "sop-std-102-active-study-clinical-canvas",
                title: "SOP-STD-102: Active Study Clinical Canvas & Visit Matrix Operations",
                categoryId: "cat-clinical-ops",
                versionLabel: "2.3",
                versionIndex: 2,
                tags: ["studies", "clinical-canvas", "enrollment", "visits", "queries"],
                primaryAnchor: "#active-study-clinical-canvas",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Key Controls"] = "study-canvas-key-controls",
                    ["Investigator Oversight & Sign-Off"] = "investigator-oversight-and-signoff",
                },
                markdown: """
                    ## Purpose & Scope
                    Defines standard operating procedures for executing study-specific enrollment, visit schedule navigation, and form query resolution inside `/studies/:studyCode`.

                    ## Key Controls
                    1. Use pseudonymous subject codes (for example `SUBJ-1001`) exclusively; never enter direct participant PII.
                    2. Supply a clear 21 CFR Part 11 reason for change whenever recording or amending clinical observations.
                    3. Review open form queries and SDV indicators prior to visit sign-off.

                    ### Participant Timeline & Visit Record
                    Inspect longitudinal visit completion, scheduled windows, and open form queries within the individual participant record view.

                    ### Study Governance & Locks
                    Sponsor administrators oversee study-level access boundaries, freeze milestones, and lock authorization.

                    ### Visit Schedule & Form Definitions
                    Sponsor designers maintain USDM visit schedules, collection specifications, and controlled terminology bindings.

                    ### Subject Enrollment & Visit Capture
                    Site coordinators verify eligibility criteria and enter contemporaneous visit assessments under pseudonymous subject IDs.

                    ### Investigator Oversight & Sign-Off
                    Site investigators review clinical significance assessments, protocol deviations, and 21 CFR Part 11 visit attestations.

                    ### Study SDV & Query Oversight
                    Monitors track source data verification progress and issue targeted discrepancy queries across assigned sites.

                    ### Study Query Triage & Cleanliness
                    Data managers reconcile aged queries, coding flags, and cross-form consistency checks prior to milestone locks.

                    ### Study Safety Signal Review
                    Medical monitors and safety officers evaluate serious adverse event trends and laboratory alert thresholds.

                    ### Study Audit Trail Inspection
                    Auditors perform read-only verification of study lineage, electronic signatures, and contemporaneous change reasons.
                    """,
                createdAt: baseTime.AddMinutes(-50),
                mappings: BuildParameterizedStudyMappings(
                    articleId: "art-study-canvas-sop",
                    createdAt: baseTime.AddMinutes(-50))),

            // 3. /ecrf (Subject Enrollment & eCRF Data Capture + ?form=* zoom) — 3 procedural steps
            CreateSop(
                articleId: "art-ecrf-sop",
                versionId: "ver-ecrf-201-v2",
                slug: "sop-ecrf-201-subject-enrollment",
                title: "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture",
                categoryId: "cat-clinical-ops",
                versionLabel: "2.2",
                versionIndex: 2,
                tags: ["ecrf", "enrollment", "gxp-edc", "site-sop"],
                primaryAnchor: "#subject-enrollment-and-visit-entry",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Mandatory GxP Controls"] = "ecrf-mandatory-gxp-controls",
                },
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

                    ### EDC Governance & Locks
                    Sponsor administrators govern eCRF environment locks, freeze rules, and role-level data entry permissions.

                    ### CRF Layout Validation
                    Study designers verify form item bindings, CDASH/SDTM annotations, and edit-check firing conditions prior to live capture.

                    ### eCRF SDV & Query Verification
                    Clinical Research Associates verify eCRF entries against clinic source records and close answered discrepancy queries.

                    ### eCRF Audit Trail Review
                    Auditors inspect item-level revision histories, actor attributions, and SHA-256 Merkle chain integrity.
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
                primaryAnchor: "#site-monitoring-and-sdv",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Operational Checklist"] = "ctms-operational-checklist",
                    ["Site Visit Readiness & Delegation"] = "site-visit-readiness",
                },
                markdown: """
                    ## Site Monitoring & SDV Procedures
                    Governs Clinical Trial Management System (CTMS) monitoring visit scheduling, follow-up letters, CRA workload balancing, and site milestone tracking across `/ctms`.

                    ## Operational Checklist
                    1. Reconcile site enrollment metrics and pending monitoring action items prior to on-site or remote visits.
                    2. Document monitoring findings with ICH E6(R3) severity classification and link follow-up CAPA or support tickets.

                    ### CTMS Portfolio Oversight
                    Sponsor operations track site milestone attainment, monitoring cadence compliance, and regional enrollment velocity.

                    ### Monitoring Plan Alignment
                    Study designers align risk-based monitoring (RBQM) critical data indicators with Schedule of Activities assessments.

                    ### Site Visit Readiness & Delegation
                    Site coordinators maintain current delegation logs, source chart availability, and action-item remediation prior to CRA visits.

                    ### Monitoring Query Reconciliation
                    Data managers reconcile unresolved monitoring findings with open eCRF discrepancy queries before interim locks.

                    ### Monitoring Visit Report Inspection
                    Auditors verify signed monitoring visit reports, follow-up communications, and CAPA closure evidence.
                    """,
                createdAt: baseTime.AddMinutes(-30),
                mappings: BuildRouteMappings(
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
                    createdAt: baseTime.AddMinutes(-30))),

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
                primaryAnchor: "#site-greenlight-activation-matrix",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Greenlight Verification"] = "ssu-greenlight-verification",
                    ["Site Initiation Visit & Greenlight"] = "site-initiation-visit-greenlight",
                },
                markdown: """
                    ## Site Startup & Greenlight Activation Gate
                    Establishes mandatory regulatory, ethics (IRB/IEC), contract, essential document, and training prerequisites required before certifying site activation on `/ssu`.

                    ## Greenlight Verification
                    1. Confirm Essential Document List (EDL) artifacts and Delegation of Authority sign-offs are complete.
                    2. Verify country regulatory approval and IP pharmacy readiness before issuing greenlight certification.

                    ### Global Country Activation Gates
                    Sponsor operations verify competent authority approvals, clinical trial agreements, and site roster readiness across participating countries.

                    ### Protocol Country-Pack Alignment
                    Study designers align country-specific ICF templates and local assessment rules with the master USDM protocol.

                    ### Site Essential Documents Checklist
                    Site coordinators upload current IRB/IEC approvals, investigator CVs, medical licenses, and laboratory certifications.

                    ### Site Initiation Visit & Greenlight
                    Monitors confirm Site Initiation Visit (SIV) completion and facility readiness prior to greenlight release.

                    ### EDC Environment Release Gate
                    Data managers confirm production eCRF edit checks and randomization tables are validated before unlocking site enrollment.

                    ### Greenlight Certification Audit
                    Auditors inspect timestamped greenlight tokens and prerequisite document completeness prior to first-patient-in.
                    """,
                createdAt: baseTime.AddMinutes(-20),
                mappings: BuildRouteMappings(
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
                    createdAt: baseTime.AddMinutes(-20))),

            // 6. /mdr (Study Authoring & MDR + ?proposal=* zoom)
            CreateSop(
                articleId: "art-mdr-sop",
                versionId: "ver-mdr-104-v2",
                slug: "sop-mdr-104-soa-matrix-authoring",
                title: "SOP-MDR-104: Schedule of Activities Matrix & Protocol Authoring",
                categoryId: "cat-study-authoring",
                versionLabel: "3.1",
                versionIndex: 3,
                tags: ["mdr", "usdm", "soa-matrix", "study-design"],
                primaryAnchor: "#schedule-of-activities-matrix-authoring",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Authoring Steps"] = "mdr-authoring-steps",
                },
                markdown: """
                    ## Protocol Metadata Management & SoA Design
                    Standardizes CDISC USDM Schedule of Activities authoring and controlled terminology binding.

                    ## Authoring Steps
                    1. Bind every clinical activity and eCRF item to an approved CDISC Biomedical Concept and NCI Codelist version.
                    2. Document protocol amendment rationale and assess downstream participant visit impact before publishing a new version.

                    ### Amendment Proposal Review
                    Review structural Schedule of Activities diffs, version lineage, and governance sign-off prerequisites before approving protocol proposals.
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
                primaryAnchor: "#gxp-audit-trail-inspection",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Inspection Steps"] = "audit-inspection-steps",
                },
                markdown: """
                    ## Audit Trail Inspection Guidelines
                    Defines SHA-256 Merkle audit chain verification and regulatory inspection procedures.

                    ## Inspection Steps
                    1. Filter immutable audit records by study, actor, entity, and UTC date window while preserving read-only inspection posture.
                    2. Verify predecessor and block SHA-256 hashes prior to exporting signed regulatory inspection bundles.
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
                primaryAnchor: "#clinical-command-center-triage",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Operational Steps"] = "overview-operational-steps",
                },
                markdown: """
                    ## Purpose & Scope
                    Governs daily review of clinical portfolio KPIs, pending operational tasks, safety escalations, and query backlogs across `/overview`.

                    ## Operational Steps
                    1. Review high-priority safety, SDV, and query alerts at workspace login.
                    2. Navigate directly to study, site, or support queues requiring immediate GxP follow-up.

                    ### Pharmacovigilance Signal Overview
                    Safety officers prioritize expedited serious adverse event alerts and medical review escalations upon sign-in.

                    ### Investigator Oversight Summary
                    Principal Investigators review pending visit signatures, safety notifications, and site delegation alerts.
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
                primaryAnchor: "#study-execution-plan-milestones",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Key Controls"] = "plans-key-controls",
                },
                markdown: """
                    ## Study Execution Plan Governance
                    Defines milestone tracking, enrollment forecasting, and cross-functional study execution plans on `/plans`.

                    ## Key Controls
                    1. Reconcile country and site activation dates with approved protocol amendments.
                    2. Escalate milestone slippage with documented root-cause context.
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
                primaryAnchor: "#clinical-trial-onboarding",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Mandatory Checks"] = "onboarding-mandatory-checks",
                },
                markdown: """
                    ## Clinical Trial Onboarding Procedure
                    Establishes controlled onboarding steps for new study protocols, organization tenancy bindings, and initial site rosters on `/onboarding/trial`.

                    ## Mandatory Checks
                    1. Validate sponsor protocol identifier, therapeutic area, and regulatory phase before workspace creation.
                    2. Verify initial study team RBAC assignments and training curriculum bindings prior to activation.
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
                primaryAnchor: "#etmf-artifact-filing-and-qc",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Operational Controls"] = "etmf-operational-controls",
                },
                markdown: """
                    ## eTMF Contemporaneous Filing & Inspection Readiness
                    Governs DIA Reference Model classification, artifact completeness review, and regulatory inspection readiness across `/etmf`.

                    ## Operational Controls
                    1. File essential regulatory and ethics artifacts under the matching zone, section, and study/site scope.
                    2. Verify document legibility, metadata completeness, and immutable hash integrity during QC review.

                    ### eTMF Inspection Readiness Verification
                    Auditors inspect DIA Reference Model completeness, certified copy attestations, and immutable filing timestamps.

                    ### Investigator Site File Attestation
                    Principal Investigators verify site-level regulatory binder synchronization and essential document currency.
                    """,
                createdAt: baseTime.AddMinutes(-6),
                mappings: BuildRouteMappings(
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
                    createdAt: baseTime.AddMinutes(-6))),

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
                primaryAnchor: "#gxp-training-and-delegation",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Qualification Rules"] = "training-qualification-rules",
                    ["PI Delegation Log Sign-Off"] = "pi-delegation-log-signoff",
                },
                markdown: """
                    ## GxP Role Qualification & Training Attestation
                    Defines mandatory SOP read-and-understand training completion and Principal Investigator Delegation of Authority prerequisites on `/training`.

                    ## Qualification Rules
                    1. Complete assigned protocol and GxP SOP modules prior to performing delegated clinical duties.
                    2. Maintain current training records across protocol amendments to preserve active write access.

                    ### PI Delegation Log Sign-Off
                    Principal Investigators verify completed staff training attestations before countersigning Delegation of Authority entries.

                    ### Site Staff Training Attestation
                    Site coordinators complete read-and-understand attestations for protocol amendments prior to executing clinical visits.
                    """,
                createdAt: baseTime.AddMinutes(-5),
                mappings: BuildRouteMappings(
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
                    createdAt: baseTime.AddMinutes(-5))),

            // 13. /observations/:observationRef (Clinical Observation Provenance & Query Review + ?observation=* zoom)
            CreateSop(
                articleId: "art-observations-sop",
                versionId: "ver-obs-210-v2",
                slug: "sop-obs-210-clinical-observation-inspection",
                title: "SOP-OBS-210: Clinical Observation Inspection, Query History & Provenance Review",
                categoryId: "cat-data-management",
                versionLabel: "1.1",
                versionIndex: 1,
                tags: ["observations", "provenance", "sdv", "queries", "safety"],
                primaryAnchor: "#clinical-observation-provenance-review",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Key Controls"] = "observation-key-controls",
                    ["Investigator Clinical Significance Sign-Off"] = "investigator-clinical-significance-signoff",
                },
                markdown: """
                    ## Clinical Observation Provenance & Discrepancy Review
                    Standardizes item-level observation inspection, audit provenance verification, medical safety review, and query resolution on `/observations/:observationRef`.

                    ## Key Controls
                    1. Inspect source provenance, prior amendments, and open queries before updating or verifying an observation.
                    2. Escalate clinically significant safety abnormalities or MedDRA coding questions with pseudonymous subject codes only.

                    ### Safety Observation Medical Review
                    Safety officers evaluate abnormal laboratory values and adverse event observations for expedited reporting criteria.

                    ### Investigator Clinical Significance Sign-Off
                    Investigators record clinical significance determinations and contemporaneous medical rationale on flagged observations.

                    ### Source Observation Amendment
                    Site coordinators submit source-verified corrections with mandatory 21 CFR Part 11 reasons for change.

                    ### Observation SDV Verification
                    Monitors compare recorded kernel observations against clinic source charts and record SDV status.

                    ### Observation Discrepancy Query Resolution
                    Data managers review edit-check lineage and verify query responses before closing item-level discrepancies.

                    ### Observation Provenance Audit Trail
                    Auditors inspect the append-only revision chain, actor identity, and UTC timestamps for every observation mutation.
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
                primaryAnchor: "#gxp-support-ticket-lifecycle",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Mandatory Rules"] = "tickets-mandatory-rules",
                },
                markdown: """
                    ## GxP Support Ticket Triage & Closure Governance
                    Defines severity classification, SLA pause/resume tracking, participant de-identification rules, root-cause documentation, and 21 CFR Part 11 electronic signatures on `/tickets`.

                    ## Mandatory Rules
                    1. Never enter direct participant PII in ticket descriptions, comments, or root-cause summaries; use pseudonymous subject codes (for example `SUBJ-1001`).
                    2. Record structured resolution codes and root-cause summaries prior to resolving tickets and applying Part 11 closure signatures.
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
                primaryAnchor: "#controlled-sop-repository-governance",
                headingAnchors: new Dictionary<string, string>
                {
                    ["Operational Steps"] = "knowledge-operational-steps",
                },
                markdown: """
                    ## Controlled SOP Repository Usage
                    Governs role-scoped SOP lookup, locked snapshot inspection, and 21 CFR Part 11 Merkle audit verification on `/knowledge`.

                    ## Operational Steps
                    1. Verify that referenced SOP guidance carries `PUBLISHED` status and a locked version snapshot before executing regulated procedures.
                    2. Inspect the 21 CFR Part 11 audit trail tab to verify cryptographic SHA-256 block hashes during quality or regulatory reviews.
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

            // 16. Clinical Canvas: Design -> Protocol versions & Schedule of activities (?stage=design&section=protocol-versions and ?stage=design&section=schedule-of-activities)
            CreateSop(
                articleId: "art-can-prot-sop",
                versionId: "ver-can-110-v2",
                slug: "sop-can-110-canvas-protocol-versions",
                title: "SOP-CAN-110: Clinical Canvas Protocol Versions, USDM Import & Amendment Impact",
                categoryId: "cat-study-authoring",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "protocol-versions", "schedule-of-activities", "usdm", "design", "amendments"],
                primaryAnchor: "#canvas-protocol-versions",
                markdown: """
                    ## Protocol Versions & USDM Draft Governance
                    Governs protocol version inspection, USDM JSON draft import, Schedule of Activities review, and amendment impact evaluation in Clinical Canvas (`stage=design&section=protocol-versions` and `stage=design&section=schedule-of-activities`).

                    ### Key Controls
                    1. Validate USDM schedule structure, visit windows, and form bindings before publishing a new protocol version.
                    2. Record a 21 CFR Part 11 justification whenever importing or superseding protocol versions.

                    ### Schedule of Activities Matrix & Visit Windows
                    Inspect epochs, visit windows, collection rows, CDISC concepts, and protocol footnotes across approved and draft protocol versions before locking or amending the schedule.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-55),
                mappings:
                [
                    .. BuildCanvasSectionMappings(
                        code: "pv",
                        stage: "design",
                        section: "protocol-versions",
                        includeStageFallback: true,
                        articleId: "art-can-prot-sop",
                        defaultAnchor: "#canvas-protocol-versions",
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-55)),
                    .. BuildCanvasSectionMappings(
                        code: "soa",
                        stage: "design",
                        section: "schedule-of-activities",
                        includeStageFallback: false,
                        articleId: "art-can-prot-sop",
                        defaultAnchor: "#canvas-schedule-of-activities",
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-55)),
                ],
                headingAnchors: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Schedule of Activities Matrix & Visit Windows"] = "canvas-schedule-of-activities",
                }),

            // 17. Clinical Canvas: Start & Conduct -> Sites (?stage=start&section=sites and ?stage=conduct&section=sites)
            CreateSop(
                articleId: "art-can-sites-sop",
                versionId: "ver-can-111-v2",
                slug: "sop-can-111-canvas-study-sites",
                title: "SOP-CAN-111: Clinical Canvas Study Site Roster & Investigator Assignment",
                categoryId: "cat-site-startup",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "sites", "investigator", "site-roster", "start", "conduct"],
                primaryAnchor: "#canvas-study-sites",
                markdown: """
                    ## Study Site Roster & Investigator Binding
                    Standardizes clinical site linking, Principal Investigator assignment, and country-level site scope verification in Clinical Canvas (`stage=start&section=sites` and `stage=conduct&section=sites`).

                    ### Operational Steps
                    1. Confirm organization and facility identifiers prior to linking a clinical site to the study.
                    2. Verify site code uniqueness and RBAC scope visibility for assigned site coordinators and monitors.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-52),
                mappings:
                [
                    .. BuildCanvasSectionMappings(
                        code: "st",
                        stage: "start",
                        section: "sites",
                        includeStageFallback: true,
                        articleId: "art-can-sites-sop",
                        defaultAnchor: "#canvas-study-sites",
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-52)),
                    .. BuildCanvasFullStageSectionMappings(
                        code: "sc",
                        stage: "conduct",
                        section: "sites",
                        articleId: "art-can-sites-sop",
                        defaultAnchor: "#canvas-study-sites",
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-52)),
                ]),

            // 18. Clinical Canvas: Start -> Site activation (?stage=start&section=site-activation)
            CreateSop(
                articleId: "art-can-actv-sop",
                versionId: "ver-can-112-v2",
                slug: "sop-can-112-canvas-site-activation",
                title: "SOP-CAN-112: Clinical Canvas Site Activation Lifecycle & Country Regulatory Gates",
                categoryId: "cat-site-startup",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "site-activation", "irb-iec", "regulatory", "start"],
                primaryAnchor: "#canvas-site-activation",
                markdown: """
                    ## Site Activation Lifecycle Controls
                    Defines site status progression from feasibility and IRB/IEC submission through activation readiness in Clinical Canvas (`stage=start&section=site-activation`).

                    ### Mandatory Checks
                    1. Confirm ethics approval and executed clinical trial agreement dates before advancing site activation state.
                    2. Document regulatory submission blockers via support escalation with study and site scope attached.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-49),
                mappings: BuildCanvasSectionMappings(
                    code: "sa",
                    stage: "start",
                    section: "site-activation",
                    includeStageFallback: true,
                    articleId: "art-can-actv-sop",
                    defaultAnchor: "#canvas-site-activation",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-49))),

            // 19. Clinical Canvas: Start -> Startup readiness (?stage=start&section=startup-greenlight)
            CreateSop(
                articleId: "art-can-grnl-sop",
                versionId: "ver-can-113-v2",
                slug: "sop-can-113-canvas-startup-greenlight",
                title: "SOP-CAN-113: Clinical Canvas Startup Readiness Matrix & Greenlight Certification",
                categoryId: "cat-site-startup",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "startup-greenlight", "readiness", "ssu", "start"],
                primaryAnchor: "#canvas-startup-greenlight",
                markdown: """
                    ## Startup Readiness & Enrollment Greenlight Gate
                    Governs pre-enrollment checklist verification across essential documents, training completion, delegation sign-off, and IP pharmacy release (`stage=start&section=startup-greenlight`).

                    ### Greenlight Rules
                    1. Verify 100% completion of mandatory startup prerequisites prior to issuing site enrollment greenlight.
                    2. Retain immutable certification timestamps and approver attribution in the study audit trail.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-46),
                mappings: BuildCanvasSectionMappings(
                    code: "sg",
                    stage: "start",
                    section: "startup-greenlight",
                    includeStageFallback: false,
                    articleId: "art-can-grnl-sop",
                    defaultAnchor: "#canvas-startup-greenlight",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-46))),

            // 20. Clinical Canvas: Start -> Documents (?stage=start&section=documents)
            CreateSop(
                articleId: "art-can-docs-sop",
                versionId: "ver-can-114-v2",
                slug: "sop-can-114-canvas-essential-documents",
                title: "SOP-CAN-114: Clinical Canvas Essential Documents & Study eTMF Binder QC",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "documents", "etmf", "dia-reference-model", "start"],
                primaryAnchor: "#canvas-essential-documents",
                markdown: """
                    ## Study Essential Document Filing & QC
                    Standardizes DIA TMF Reference Model filing, metadata verification, and quality review within Clinical Canvas (`stage=start&section=documents`).

                    ### Operational Controls
                    1. Classify uploaded essential documents under the correct study and site scope without participant PII.
                    2. Complete QC verification prior to relying on regulatory artifacts for site greenlight.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-43),
                mappings: BuildCanvasSectionMappings(
                    code: "dc",
                    stage: "start",
                    section: "documents",
                    includeStageFallback: false,
                    articleId: "art-can-docs-sop",
                    defaultAnchor: "#canvas-essential-documents",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-43))),

            // 21. Clinical Canvas: Start -> Training (?stage=start&section=training)
            CreateSop(
                articleId: "art-can-trng-sop",
                versionId: "ver-can-115-v2",
                slug: "sop-can-115-canvas-study-training",
                title: "SOP-CAN-115: Clinical Canvas Study Training Curricula & Qualification Gating",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "training", "qualification", "ich-e6", "start"],
                primaryAnchor: "#canvas-study-training",
                markdown: """
                    ## Study Protocol Training & Role Qualification
                    Defines protocol-specific training assignment, read-and-understand attestation, and write-access qualification checks (`stage=start&section=training`).

                    ### Qualification Rules
                    1. Ensure site personnel complete required protocol version curricula prior to delegation sign-off.
                    2. Re-verify training currency following major protocol amendments.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-40),
                mappings: BuildCanvasSectionMappings(
                    code: "tr",
                    stage: "start",
                    section: "training",
                    includeStageFallback: false,
                    articleId: "art-can-trng-sop",
                    defaultAnchor: "#canvas-study-training",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-40))),

            // 22. Clinical Canvas: Start -> Site milestones (?stage=start&section=site-milestones)
            CreateSop(
                articleId: "art-can-mile-sop",
                versionId: "ver-can-116-v2",
                slug: "sop-can-116-canvas-site-milestones",
                title: "SOP-CAN-116: Clinical Canvas Site Activation Milestones & Target Variance Review",
                categoryId: "cat-site-monitoring",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "site-milestones", "ctms", "timelines", "start"],
                primaryAnchor: "#canvas-site-milestones",
                markdown: """
                    ## Site Milestone Tracking & Baseline Variance Governance
                    Governs planned, baseline, and actual startup milestone tracking across study sites (`stage=start&section=site-milestones`).

                    ### Operational Steps
                    1. Record contemporaneous actual completion dates for IRB submission, contract execution, and SIV completion.
                    2. Escalate critical-path startup delays with root-cause commentary.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-37),
                mappings: BuildCanvasSectionMappings(
                    code: "ml",
                    stage: "start",
                    section: "site-milestones",
                    includeStageFallback: false,
                    articleId: "art-can-mile-sop",
                    defaultAnchor: "#canvas-site-milestones",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-37))),

            // 23. Clinical Canvas: Start -> Delegation log (?stage=start&section=delegation)
            CreateSop(
                articleId: "art-can-delg-sop",
                versionId: "ver-can-117-v2",
                slug: "sop-can-117-canvas-delegation-log",
                title: "SOP-CAN-117: Clinical Canvas Delegation of Authority Log & PI Electronic Sign-Off",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "delegation", "doa-log", "part11", "start"],
                primaryAnchor: "#canvas-delegation-log",
                markdown: """
                    ## Principal Investigator Delegation of Authority (DoA) Governance
                    Establishes electronic DoA duty assignment, effective date tracking, and Principal Investigator 21 CFR Part 11 countersignature rules (`stage=start&section=delegation`).

                    ### Mandatory Controls
                    1. Verify active GxP training completion before delegating clinical assessment or eCRF entry responsibilities.
                    2. Capture step-up electronic signatures when authorizing or ending delegated site duties.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-34),
                mappings: BuildCanvasSectionMappings(
                    code: "dl",
                    stage: "start",
                    section: "delegation",
                    includeStageFallback: false,
                    articleId: "art-can-delg-sop",
                    defaultAnchor: "#canvas-delegation-log",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-34))),

            // 24. Clinical Canvas: Conduct -> Participants (?stage=conduct&section=participants)
            CreateSop(
                articleId: "art-can-part-sop",
                versionId: "ver-can-118-v2",
                slug: "sop-can-118-canvas-participants-execution",
                title: "SOP-CAN-118: Clinical Canvas Participant Enrollment, Visit Forms & Source Verification",
                categoryId: "cat-clinical-ops",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "participants", "enrollment", "ecrf", "conduct"],
                primaryAnchor: "#canvas-participants-execution",
                markdown: """
                    ## Participant Lifecycle, Visit Matrix & eCRF Execution
                    Governs participant screening, enrollment, visit collection instances, source data verification, and PI form signatures (`stage=conduct&section=participants`).

                    ### Mandatory GxP Controls
                    1. Reference participants strictly by pseudonymous subject code (for example `SUBJ-1001`); never record direct PII.
                    2. Provide an auditable 21 CFR Part 11 reason for change on every observation amendment or form correction.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-31),
                mappings: BuildCanvasSectionMappings(
                    code: "pt",
                    stage: "conduct",
                    section: "participants",
                    includeStageFallback: true,
                    articleId: "art-can-part-sop",
                    defaultAnchor: "#canvas-participants-execution",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-31))),

            // 25. Clinical Canvas: Conduct -> Monitoring visits (?stage=conduct&section=monitoring-visits)
            CreateSop(
                articleId: "art-can-monv-sop",
                versionId: "ver-can-119-v2",
                slug: "sop-can-119-canvas-monitoring-visits",
                title: "SOP-CAN-119: Clinical Canvas Site Monitoring Visits, Findings & Follow-Up Letters",
                categoryId: "cat-site-monitoring",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "monitoring-visits", "mvr", "sdv", "conduct"],
                primaryAnchor: "#canvas-monitoring-visits",
                markdown: """
                    ## Site Monitoring Visit Execution & Finding Resolution
                    Standardizes SIV, IMV, and COV visit execution, finding severity grading, and compiled follow-up letter sign-off (`stage=conduct&section=monitoring-visits`).

                    ### Operational Checklist
                    1. Reconcile SDV coverage and open discrepancy queries before finalizing monitoring visit reports.
                    2. Escalate major or critical monitoring findings to the quality and site operations queue with site scope preserved.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-28),
                mappings: BuildCanvasSectionMappings(
                    code: "mv",
                    stage: "conduct",
                    section: "monitoring-visits",
                    includeStageFallback: false,
                    articleId: "art-can-monv-sop",
                    defaultAnchor: "#canvas-monitoring-visits",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-28))),

            // 26. Clinical Canvas: Conduct -> Investigational supply (?stage=conduct&section=supply)
            CreateSop(
                articleId: "art-can-supp-sop",
                versionId: "ver-can-120-v2",
                slug: "sop-can-120-canvas-investigational-supply",
                title: "SOP-CAN-120: Clinical Canvas Investigational Supply, Kit Dispensing & Excursion Control",
                categoryId: "cat-clinical-ops",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "supply", "rtsm", "ip-accountability", "conduct"],
                primaryAnchor: "#canvas-investigational-supply",
                markdown: """
                    ## Investigational Product Inventory & Temperature Excursion Controls
                    Governs depot-to-site shipments, kit quarantine on temperature excursion, subject dispensing, and IP accountability (`stage=conduct&section=supply`).

                    ### Mandatory Controls
                    1. Quarantine affected kit lots immediately upon cold-chain excursion detection and escalate under `SUPPLY_EXCURSION`.
                    2. Maintain blinded kit reconciliation records across dispensing and return workflows.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-25),
                mappings: BuildCanvasSectionMappings(
                    code: "sp",
                    stage: "conduct",
                    section: "supply",
                    includeStageFallback: false,
                    articleId: "art-can-supp-sop",
                    defaultAnchor: "#canvas-investigational-supply",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-25))),

            // 27. Clinical Canvas: Data -> Queries (?stage=data&section=queries)
            CreateSop(
                articleId: "art-can-qrys-sop",
                versionId: "ver-can-121-v2",
                slug: "sop-can-121-canvas-discrepancy-queries",
                title: "SOP-CAN-121: Clinical Canvas Discrepancy Query Triage, Site Response & Closure",
                categoryId: "cat-data-management",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "queries", "discrepancies", "data-cleaning", "data"],
                primaryAnchor: "#canvas-discrepancy-queries",
                markdown: """
                    ## Study Discrepancy Query Triage & Resolution
                    Defines manual and rule-generated query review, site coordinator response, and data manager closure verification (`stage=data&section=queries`).

                    ### Operational Steps
                    1. Inspect linked observation provenance before answering or closing open discrepancy queries.
                    2. Never close discrepancy queries without verifying either an observation correction or a documented clinical explanation.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-22),
                mappings: BuildCanvasSectionMappings(
                    code: "qr",
                    stage: "data",
                    section: "queries",
                    includeStageFallback: true,
                    articleId: "art-can-qrys-sop",
                    defaultAnchor: "#canvas-discrepancy-queries",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-22))),

            // 28. Clinical Canvas: Data -> Locks (?stage=data&section=locks & /data-lock)
            CreateSop(
                articleId: "art-can-lock-sop",
                versionId: "ver-can-122-v2",
                slug: "sop-can-122-canvas-scoped-data-locks",
                title: "SOP-CAN-122: Clinical Canvas Scoped Data Locks, Prerequisite Audits & Part 11 Attestation",
                categoryId: "cat-data-management",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "locks", "data-lock", "part11", "data"],
                primaryAnchor: "#canvas-scoped-data-locks",
                markdown: """
                    ## Scoped Data Lock & Unlock Governance
                    Governs observation, form, participant, site, and study lock readiness checks, Part 11 step-up electronic signatures, and controlled unlock procedures (`stage=data&section=locks` and `/data-lock`).

                    ### Mandatory Lock Gates
                    1. Resolve all open discrepancy queries, pending SDV items, and required PI signatures before applying a hard lock.
                    2. Record an explicit regulatory justification and step-up signature for every lock or controlled unlock action.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-19),
                mappings:
                [
                    .. BuildCanvasSectionMappings(
                        code: "lk",
                        stage: "data",
                        section: "locks",
                        includeStageFallback: false,
                        articleId: "art-can-lock-sop",
                        defaultAnchor: "#canvas-scoped-data-locks",
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-19)),
                    .. BuildRouteMappings(
                        prefix: "m-dlk",
                        exactRoute: "/data-lock",
                        wildcardRoute: "/data-lock/*",
                        articleId: "art-can-lock-sop",
                        defaultAnchor: "#canvas-scoped-data-locks",
                        personaAnchors: new Dictionary<string, string>(),
                        priority: 30,
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-19)),
                ]),

            // 29. Clinical Canvas: Data -> Validation (?stage=data&section=validation)
            CreateSop(
                articleId: "art-can-vald-sop",
                versionId: "ver-can-123-v2",
                slug: "sop-can-123-canvas-rule-validation",
                title: "SOP-CAN-123: Clinical Canvas Edit-Check Rule Packs, Dry-Run Preview & Validation Findings",
                categoryId: "cat-data-management",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "validation", "edit-checks", "rules", "data"],
                primaryAnchor: "#canvas-rule-validation",
                markdown: """
                    ## Edit-Check Rule Pack Authoring & Validation Findings
                    Standardizes deterministic edit-check specification, dry-run impact preview, rule pack activation, and cross-visit validation triage (`stage=data&section=validation`).

                    ### Key Controls
                    1. Execute dry-run previews against study observations to verify false-positive rates before activating new rule packs.
                    2. Review validation findings alongside query ownership to coordinate timely site data cleaning.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-16),
                mappings: BuildCanvasSectionMappings(
                    code: "vl",
                    stage: "data",
                    section: "validation",
                    includeStageFallback: false,
                    articleId: "art-can-vald-sop",
                    defaultAnchor: "#canvas-rule-validation",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-16))),

            // 30. Clinical Canvas: Analyze stage (?stage=analyze)
            CreateSop(
                articleId: "art-can-anlz-sop",
                versionId: "ver-can-125-v2",
                slug: "sop-can-125-canvas-analysis-readiness",
                title: "SOP-CAN-125: Clinical Canvas Endpoint Analysis Readiness & Blinded Review",
                categoryId: "cat-data-management",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "analyze", "blinded-review", "biostatistics"],
                primaryAnchor: "#canvas-analysis-readiness",
                markdown: """
                    ## Blinded Data Review & Endpoint Analysis Readiness
                    Defines pre-lock blinded data review, population evaluability checks, and statistical readiness governance (`stage=analyze`).

                    ### Operational Controls
                    1. Confirm database lock prerequisites and protocol deviation classifications prior to unblinded analysis cutoffs.
                    2. Preserve treatment-assignment blinding across operational support escalations.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-13),
                mappings: BuildCanvasStageOnlyMappings(
                    code: "an",
                    stage: "analyze",
                    articleId: "art-can-anlz-sop",
                    defaultAnchor: "#canvas-analysis-readiness",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-13))),

            // 31. Clinical Canvas: Submit -> Exports (?stage=submit&section=exports & /exports)
            CreateSop(
                articleId: "art-can-expt-sop",
                versionId: "ver-can-124-v2",
                slug: "sop-can-124-canvas-regulated-exports",
                title: "SOP-CAN-124: Clinical Canvas Regulated Dataset & Audit-Trail Export Generation",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["clinical-canvas", "exports", "submission", "cdisc", "submit"],
                primaryAnchor: "#canvas-regulated-exports",
                markdown: """
                    ## Regulated Clinical Dataset & Audit-Trail Exports
                    Governs authorized clinical observation exports, 21 CFR Part 11 audit-trail archival packages, and submission readiness checks (`stage=submit&section=exports` and `/exports`).

                    ### Mandatory Controls
                    1. Verify study/site authorization and lock state prior to generating regulatory submission or inspection export bundles.
                    2. Confirm SHA-256 manifest integrity and pseudonymous subject de-identification on exported deliverables.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-10),
                mappings:
                [
                    .. BuildCanvasSectionMappings(
                        code: "ex",
                        stage: "submit",
                        section: "exports",
                        includeStageFallback: true,
                        articleId: "art-can-expt-sop",
                        defaultAnchor: "#canvas-regulated-exports",
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-10)),
                    .. BuildRouteMappings(
                        prefix: "m-exp",
                        exactRoute: "/exports",
                        wildcardRoute: "/exports/*",
                        articleId: "art-can-expt-sop",
                        defaultAnchor: "#canvas-regulated-exports",
                        personaAnchors: new Dictionary<string, string>(),
                        priority: 30,
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-10)),
                ]),

            // 32. /forbidden (Role-Based Access Refusal & Least-Privilege Provisioning)
            CreateSop(
                articleId: "art-forbidden-sop",
                versionId: "ver-rbac-403-v2",
                slug: "sop-rbac-403-rbac-access-refusal-governance",
                title: "SOP-RBAC-403: Role-Based Access Refusal, Least-Privilege Scope & Provisioning Escalation",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["forbidden", "rbac", "access-control", "least-privilege", "provisioning"],
                primaryAnchor: "#rbac-access-refusal-and-escalation",
                markdown: """
                    ## Role-Based Access Control (RBAC) Refusal & Scope Escalation
                    Explains ICH E6(R3) least-privilege role enforcement, study/site assignment boundaries, and controlled access request procedures when landing on `/forbidden`.

                    ### Operational Steps
                    1. Confirm your active workspace persona and assigned study/site scope before retrying restricted clinical routes.
                    2. Escalate legitimate role or site provisioning requests under `ACCESS_CONTROL` for Sponsor Admin review and training verification.
                    """,
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-5),
                mappings: BuildRouteMappings(
                    prefix: "m-frb",
                    exactRoute: "/forbidden",
                    wildcardRoute: "/forbidden/*",
                    articleId: "art-forbidden-sop",
                    defaultAnchor: "#rbac-access-refusal-and-escalation",
                    personaAnchors: new Dictionary<string, string>(),
                    priority: 25,
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-5))),

            // 33. /unavailable (Pre-Release Module Availability & Validated Fallback Workflows)
            CreateSop(
                articleId: "art-unavailable-sop",
                versionId: "ver-sys-404-v2",
                slug: "sop-sys-404-module-availability-fallback",
                title: "SOP-SYS-404: Pre-Release Workspace Module Availability & Validated Fallback Workflows",
                categoryId: "cat-clinical-ops",
                versionLabel: "1.1",
                versionIndex: 2,
                tags: ["unavailable", "roadmap", "validated-workflows", "clinical-canvas"],
                primaryAnchor: "#module-availability-and-roadmap",
                markdown: """
                    ## Workspace Module Availability & Validated Clinical Canvas Fallbacks
                    Guides operators encountering pre-release module screens on `/unavailable` to the validated Clinical Kernel workflows active in Clinical Canvas.

                    ### Operational Steps
                    1. Use Clinical Canvas (`/studies/:studyCode`), Data Collection (`/ecrf`), Site Readiness (`/ctms`, `/ssu`), eTMF (`/etmf`), Training (`/training`), Support (`/tickets`), and Knowledge (`/knowledge`) for live regulated operations.
                    2. Log an operational support ticket under `SYSTEM_SUPPORT` if an expected entitlement or module workflow is unavailable.
                    """,
                createdAt: baseTime.AddMinutes(-1),
                mappings: BuildRouteMappings(
                    prefix: "m-unv",
                    exactRoute: "/unavailable",
                    wildcardRoute: "/unavailable/*",
                    articleId: "art-unavailable-sop",
                    defaultAnchor: "#module-availability-and-roadmap",
                    personaAnchors: new Dictionary<string, string>(),
                    priority: 25,
                    createdAt: baseTime.AddMinutes(-1))),
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
        string primaryAnchor,
        string markdown,
        DateTimeOffset createdAt,
        IReadOnlyList<ContextualHelpMapping> mappings,
        IReadOnlyDictionary<string, string>? headingAnchors = null)
    {
        string compiledHtml = SopHtmlCompiler.Compile(markdown, primaryAnchor, headingAnchors);
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
            BodyHtml = compiledHtml,
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

    private static string ShortPersonaKey(string persona) =>
        persona switch
        {
            "super_admin" => "sa",
            "sponsor_designer" => "sd",
            "site_crc" => "sc",
            "site_investigator" => "si",
            "cra_monitor" => "cm",
            "data_manager" => "dm",
            "safety_officer" => "so",
            "auditor" => "au",
            _ => persona[..Math.Min(4, persona.Length)],
        };

    private static List<ContextualHelpMapping> BuildCanvasSectionMappings(
        string code,
        string stage,
        string section,
        bool includeStageFallback,
        string articleId,
        string defaultAnchor,
        DateTimeOffset createdAt)
    {
        string paramFull = $"/studies/:studyCode?stage={stage}&section={section}";
        string paramSec = $"/studies/:studyCode?section={section}";
        string rootFull = $"/studies?stage={stage}&section={section}";
        string rootSec = $"/studies?section={section}";

        var list = new List<ContextualHelpMapping>
        {
            NewMapping($"mc-{code}-pf-any", paramFull, null, articleId, defaultAnchor, 40, createdAt),
            NewMapping($"mc-{code}-ps-any", paramSec, null, articleId, defaultAnchor, 38, createdAt),
            NewMapping($"mc-{code}-rf-any", rootFull, null, articleId, defaultAnchor, 40, createdAt),
            NewMapping($"mc-{code}-rs-any", rootSec, null, articleId, defaultAnchor, 38, createdAt),
        };

        if (includeStageFallback)
        {
            list.Add(NewMapping($"mc-{code}-pg-any", $"/studies/:studyCode?stage={stage}", null, articleId, defaultAnchor, 36, createdAt));
            list.Add(NewMapping($"mc-{code}-rg-any", $"/studies?stage={stage}", null, articleId, defaultAnchor, 36, createdAt));
        }

        foreach (string persona in AllStaffPersonas)
        {
            string pk = ShortPersonaKey(persona);
            list.Add(NewMapping($"mc-{code}-pf-{pk}", paramFull, persona, articleId, defaultAnchor, 45, createdAt));
            list.Add(NewMapping($"mc-{code}-ps-{pk}", paramSec, persona, articleId, defaultAnchor, 43, createdAt));
            list.Add(NewMapping($"mc-{code}-rf-{pk}", rootFull, persona, articleId, defaultAnchor, 45, createdAt));
            list.Add(NewMapping($"mc-{code}-rs-{pk}", rootSec, persona, articleId, defaultAnchor, 43, createdAt));

            if (includeStageFallback)
            {
                list.Add(NewMapping($"mc-{code}-pg-{pk}", $"/studies/:studyCode?stage={stage}", persona, articleId, defaultAnchor, 41, createdAt));
                list.Add(NewMapping($"mc-{code}-rg-{pk}", $"/studies?stage={stage}", persona, articleId, defaultAnchor, 41, createdAt));
            }
        }

        return list;
    }

    private static List<ContextualHelpMapping> BuildCanvasFullStageSectionMappings(
        string code,
        string stage,
        string section,
        string articleId,
        string defaultAnchor,
        DateTimeOffset createdAt)
    {
        string paramFull = $"/studies/:studyCode?stage={stage}&section={section}";
        string rootFull = $"/studies?stage={stage}&section={section}";

        var list = new List<ContextualHelpMapping>
        {
            NewMapping($"mc-{code}-pf-any", paramFull, null, articleId, defaultAnchor, 40, createdAt),
            NewMapping($"mc-{code}-rf-any", rootFull, null, articleId, defaultAnchor, 40, createdAt),
        };

        foreach (string persona in AllStaffPersonas)
        {
            string pk = ShortPersonaKey(persona);
            list.Add(NewMapping($"mc-{code}-pf-{pk}", paramFull, persona, articleId, defaultAnchor, 45, createdAt));
            list.Add(NewMapping($"mc-{code}-rf-{pk}", rootFull, persona, articleId, defaultAnchor, 45, createdAt));
        }

        return list;
    }

    private static List<ContextualHelpMapping> BuildCanvasStageOnlyMappings(
        string code,
        string stage,
        string articleId,
        string defaultAnchor,
        DateTimeOffset createdAt)
    {
        string paramStage = $"/studies/:studyCode?stage={stage}";
        string rootStage = $"/studies?stage={stage}";

        var list = new List<ContextualHelpMapping>
        {
            NewMapping($"mc-{code}-pg-any", paramStage, null, articleId, defaultAnchor, 40, createdAt),
            NewMapping($"mc-{code}-rg-any", rootStage, null, articleId, defaultAnchor, 40, createdAt),
        };

        foreach (string persona in AllStaffPersonas)
        {
            string pk = ShortPersonaKey(persona);
            list.Add(NewMapping($"mc-{code}-pg-{pk}", paramStage, persona, articleId, defaultAnchor, 45, createdAt));
            list.Add(NewMapping($"mc-{code}-rg-{pk}", rootStage, persona, articleId, defaultAnchor, 45, createdAt));
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
