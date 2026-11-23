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
/// and contextual help route mappings for <c>/studies</c> (including <c>/studies/:studyCode</c>), <c>/ecrf</c>,
/// <c>/ctms</c>, <c>/ssu</c>, <c>/mdr</c>, and <c>/audit</c> across all clinical staff personas (PRD-KNB-001, PRD-KNB-002).
/// @req:PRD-KNB-001
/// @req:PRD-KNB-002
/// </summary>
public static class KnowledgeCatalogSeeder
{
    private const string CatalogServiceAccount = "svc:knowledge-sop-catalog";
    private const string SeedReason = "Initial controlled SOP baseline publication";

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
        string latestSentinelId = $"art-unavailable-sop{tenantSuffix}";

        // Fast path: once our complete live-workspace SOP baseline exists for this tenant, skip locking and writes entirely.
        bool alreadySeeded = await db.KnowledgeArticles
            .AsNoTracking()
            .AnyAsync(a => a.Id == latestSentinelId, ct)
            .ConfigureAwait(false);
        if (alreadySeeded)
        {
            return;
        }

        await SeedLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool recheck = await db.KnowledgeArticles
                .AsNoTracking()
                .AnyAsync(a => a.Id == latestSentinelId, ct)
                .ConfigureAwait(false);
            if (recheck)
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
            var existingArticleIds = await db.KnowledgeArticles
                .Select(a => a.Id)
                .ToHashSetAsync(StringComparer.Ordinal, ct)
                .ConfigureAwait(false);
            var existingVersionIds = await db.KnowledgeArticleVersions
                .Select(v => v.Id)
                .ToHashSetAsync(StringComparer.Ordinal, ct)
                .ConfigureAwait(false);
            var existingMappingIds = await db.ContextualHelpMappings
                .Select(m => m.Id)
                .ToHashSetAsync(StringComparer.Ordinal, ct)
                .ConfigureAwait(false);

            foreach (var seed in seeds)
            {
                if (!existingArticleIds.Contains(seed.Article.Id))
                {
                    db.KnowledgeArticles.Add(seed.Article);
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
                            status = seed.Article.Status,
                        })));
                }

                if (!existingVersionIds.Contains(seed.Version.Id))
                {
                    db.KnowledgeArticleVersions.Add(seed.Version);
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
            // 1. /studies (Portfolio directory)
            CreateSop(
                articleId: "art-studies-portfolio-sop",
                versionId: "ver-studies-portfolio-101",
                slug: "sop-std-101-study-portfolio-governance",
                title: "SOP-STD-101: Study Portfolio Governance & Workspace Selection",
                categoryId: "cat-study-authoring",
                versionLabel: "2.0",
                versionIndex: 2,
                tags: ["studies", "portfolio", "governance", "usdm"],
                markdown: """
                    ## Purpose & Scope
                    Governs study selection, protocol status verification, and role-scoped workspace entry across `/studies`.

                    ## Operational Steps
                    1. Verify protocol code and lifecycle status (`ACTIVE`, `IN_DESIGN`, or `LOCKED`) prior to opening clinical data workspaces.
                    2. Confirm assigned study and site scope before initiating enrollment, monitoring, or data management actions.
                    3. Escalate missing study assignments to Platform Access Governance via contextual support ticketing.
                    """,
                html: "<h2>Purpose &amp; Scope</h2><p>Governs study selection, protocol status verification, and role-scoped workspace entry across <code>/studies</code>.</p>",
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

            // 2. /studies/:studyCode (Parameterized Study Clinical Canvas)
            CreateSop(
                articleId: "art-study-canvas-sop",
                versionId: "ver-study-canvas-102",
                slug: "sop-std-102-active-study-clinical-canvas",
                title: "SOP-STD-102: Active Study Clinical Canvas & Visit Matrix Operations",
                categoryId: "cat-clinical-ops",
                versionLabel: "2.2",
                versionIndex: 2,
                tags: ["studies", "clinical-canvas", "enrollment", "visits", "queries"],
                markdown: """
                    ## Purpose & Scope
                    Defines standard operating procedures for executing study-specific enrollment, visit schedule navigation, and form query resolution inside `/studies/:studyCode`.

                    ## Key Controls
                    1. Use pseudonymous subject codes (for example `SUBJ-1001`) exclusively; never enter direct participant PII.
                    2. Supply a clear 21 CFR Part 11 reason for change whenever recording or amending clinical observations.
                    3. Review open form queries and SDV indicators prior to visit sign-off.
                    """,
                html: "<h2>Purpose &amp; Scope</h2><p>Defines standard operating procedures for executing study-specific enrollment, visit schedule navigation, and form query resolution inside <code>/studies/:studyCode</code>.</p>",
                createdAt: baseTime.AddMinutes(-50),
                mappings: BuildParameterizedStudyMappings(
                    articleId: "art-study-canvas-sop",
                    createdAt: baseTime.AddMinutes(-50))),

            // 3. /ecrf (Subject Enrollment & eCRF Data Capture)
            CreateSop(
                articleId: "art-ecrf-sop",
                versionId: "ver-ecrf-201",
                slug: "sop-ecrf-201-subject-enrollment",
                title: "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture",
                categoryId: "cat-clinical-ops",
                versionLabel: "2.1",
                versionIndex: 2,
                tags: ["ecrf", "enrollment", "gxp-edc", "site-sop"],
                markdown: """
                    ## Purpose & Scope
                    This Standard Operating Procedure (SOP) governs all clinical trial personnel executing electronic Case Report Form (eCRF) data capture, subject registration, source verification, and discrepancy query management within the Cadence Clinical Research Platform.

                    ## Mandatory GxP Controls
                    1. Confirm informed consent verification prior to recording baseline assessments.
                    2. Record contemporaneous source-verified measurements and document mandatory change justifications for every amendment.
                    3. Escalate protocol deviations or locked-form blockers via Contextual Help using pseudonymous subject identifiers only.
                    """,
                html: "<h2>Purpose &amp; Scope</h2><p>This Standard Operating Procedure (SOP) governs all clinical trial personnel executing electronic Case Report Form (eCRF) data capture, subject registration, source verification, and discrepancy query management.</p>",
                createdAt: baseTime.AddMinutes(-40),
                mappings: BuildRouteMappings(
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
                    createdAt: baseTime.AddMinutes(-40))),

            // 4. /ctms (Site Monitoring Visits & CTMS)
            CreateSop(
                articleId: "art-ctms-sop",
                versionId: "ver-ctms-302",
                slug: "sop-ctms-302-site-monitoring",
                title: "SOP-CTMS-302: Site Monitoring Visits & Source Data Verification",
                categoryId: "cat-site-monitoring",
                versionLabel: "1.4",
                versionIndex: 1,
                tags: ["ctms", "monitoring", "sdv", "cra", "trip-reports"],
                markdown: """
                    ## Site Monitoring & SDV Procedures
                    Governs Clinical Trial Management System (CTMS) monitoring visit scheduling, follow-up letters, CRA workload balancing, and site milestone tracking across `/ctms`.

                    ## Operational Checklist
                    1. Reconcile site enrollment metrics and pending monitoring action items prior to on-site or remote visits.
                    2. Document monitoring findings with ICH E6(R3) severity classification and link follow-up CAPA or support tickets.
                    """,
                html: "<h2>Site Monitoring &amp; SDV Procedures</h2><p>Governs Clinical Trial Management System (CTMS) monitoring visit scheduling, follow-up letters, CRA workload balancing, and site milestone tracking across <code>/ctms</code>.</p>",
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

            // 5. /ssu (Site Startup & Greenlight Activation)
            CreateSop(
                articleId: "art-ssu-sop",
                versionId: "ver-ssu-205",
                slug: "sop-ssu-205-site-startup-greenlight",
                title: "SOP-SSU-205: Site Startup, Regulatory Greenlight & Country Activation",
                categoryId: "cat-site-startup",
                versionLabel: "1.2",
                versionIndex: 1,
                tags: ["ssu", "site-startup", "greenlight", "regulatory", "activation"],
                markdown: """
                    ## Site Startup & Greenlight Activation Gate
                    Establishes mandatory regulatory, ethics (IRB/IEC), contract, essential document, and training prerequisites required before certifying site activation on `/ssu`.

                    ## Greenlight Verification
                    1. Confirm Essential Document List (EDL) artifacts and Delegation of Authority sign-offs are complete.
                    2. Verify country regulatory approval and IP pharmacy readiness before issuing greenlight certification.
                    """,
                html: "<h2>Site Startup &amp; Greenlight Activation Gate</h2><p>Establishes mandatory regulatory, ethics (IRB/IEC), contract, essential document, and training prerequisites required before certifying site activation on <code>/ssu</code>.</p>",
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

            // 6. /mdr (Study Authoring & MDR)
            CreateSop(
                articleId: "art-mdr-sop",
                versionId: "ver-mdr-104",
                slug: "sop-mdr-104-soa-matrix-authoring",
                title: "SOP-MDR-104: Schedule of Activities Matrix & Protocol Authoring",
                categoryId: "cat-study-authoring",
                versionLabel: "3.0",
                versionIndex: 3,
                tags: ["mdr", "usdm", "soa-matrix", "study-design"],
                markdown: "## Protocol Metadata Management & SoA Design\nStandardizes CDISC USDM Schedule of Activities authoring and controlled terminology binding.",
                html: "<h2>Protocol Metadata Management &amp; SoA Design</h2><p>Standardizes CDISC USDM Schedule of Activities authoring and controlled terminology binding.</p>",
                createdAt: baseTime.AddMinutes(-15),
                mappings: BuildRouteMappings(
                    prefix: "m-mdr",
                    exactRoute: "/mdr",
                    wildcardRoute: "/mdr/*",
                    articleId: "art-mdr-sop",
                    defaultAnchor: "#schedule-of-activities-matrix-authoring",
                    personaAnchors: new Dictionary<string, string>(),
                    priority: 20,
                    createdAt: baseTime.AddMinutes(-15))),

            // 7. /audit (21 CFR Part 11 Audit Trail Inspection)
            CreateSop(
                articleId: "art-audit-sop",
                versionId: "ver-aud-401",
                slug: "sop-aud-401-audit-trail-inspection",
                title: "SOP-AUD-401: 21 CFR Part 11 Audit Trail & Qualification Inspection",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["audit", "part11", "gxp", "compliance"],
                markdown: "## Audit Trail Inspection Guidelines\nDefines SHA-256 Merkle audit chain verification and regulatory inspection procedures.",
                html: "<h2>Audit Trail Inspection Guidelines</h2><p>Defines SHA-256 Merkle audit chain verification and regulatory inspection procedures.</p>",
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
                versionId: "ver-ops-100",
                slug: "sop-ops-100-clinical-command-center",
                title: "SOP-OPS-100: Clinical Operations Command Center & Action Queue Triage",
                categoryId: "cat-clinical-ops",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["overview", "command-center", "triage", "portfolio"],
                markdown: """
                    ## Purpose & Scope
                    Governs daily review of clinical portfolio KPIs, pending operational tasks, safety escalations, and query backlogs across `/overview`.

                    ## Operational Steps
                    1. Review high-priority safety, SDV, and query alerts at workspace login.
                    2. Navigate directly to study, site, or support queues requiring immediate GxP follow-up.
                    """,
                html: "<h2>Purpose &amp; Scope</h2><p>Governs daily review of clinical portfolio KPIs, pending operational tasks, safety escalations, and query backlogs across <code>/overview</code>.</p>",
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
                versionId: "ver-pln-105",
                slug: "sop-pln-105-study-execution-plans",
                title: "SOP-PLN-105: Study Execution Plans, Milestones & Operational Readiness",
                categoryId: "cat-site-monitoring",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["plans", "milestones", "ctms", "readiness"],
                markdown: """
                    ## Study Execution Plan Governance
                    Defines milestone tracking, enrollment forecasting, and cross-functional study execution plans on `/plans`.

                    ## Key Controls
                    1. Reconcile country and site activation dates with approved protocol amendments.
                    2. Escalate milestone slippage with documented root-cause context.
                    """,
                html: "<h2>Study Execution Plan Governance</h2><p>Defines milestone tracking, enrollment forecasting, and cross-functional study execution plans on <code>/plans</code>.</p>",
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
                versionId: "ver-onb-110",
                slug: "sop-onb-110-clinical-trial-onboarding",
                title: "SOP-ONB-110: Clinical Trial Onboarding, Protocol Intake & Site Provisioning",
                categoryId: "cat-site-startup",
                versionLabel: "1.1",
                versionIndex: 1,
                tags: ["onboarding", "trial-setup", "provisioning", "protocol"],
                markdown: """
                    ## Clinical Trial Onboarding Procedure
                    Establishes controlled onboarding steps for new study protocols, organization tenancy bindings, and initial site rosters on `/onboarding/trial`.

                    ## Mandatory Checks
                    1. Validate sponsor protocol identifier, therapeutic area, and regulatory phase before workspace creation.
                    2. Verify initial study team RBAC assignments and training curriculum bindings prior to activation.
                    """,
                html: "<h2>Clinical Trial Onboarding Procedure</h2><p>Establishes controlled onboarding steps for new study protocols, organization tenancy bindings, and initial site rosters on <code>/onboarding/trial</code>.</p>",
                createdAt: baseTime.AddMinutes(-7),
                mappings: BuildOnboardingMappings(
                    articleId: "art-onboarding-trial-sop",
                    createdAt: baseTime.AddMinutes(-7))),

            // 11. /etmf (Electronic Trial Master File Artifact Filing & QC)
            CreateSop(
                articleId: "art-etmf-sop",
                versionId: "ver-tmf-301",
                slug: "sop-tmf-301-etmf-artifact-filing",
                title: "SOP-TMF-301: Electronic Trial Master File (eTMF) Artifact Filing & QC",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.3",
                versionIndex: 1,
                tags: ["etmf", "dia-reference-model", "essential-documents", "qc"],
                markdown: """
                    ## eTMF Contemporaneous Filing & Inspection Readiness
                    Governs DIA Reference Model classification, artifact completeness review, and regulatory inspection readiness across `/etmf`.

                    ## Operational Controls
                    1. File essential regulatory and ethics artifacts under the matching zone, section, and study/site scope.
                    2. Verify document legibility, metadata completeness, and immutable hash integrity during QC review.
                    """,
                html: "<h2>eTMF Contemporaneous Filing &amp; Inspection Readiness</h2><p>Governs DIA Reference Model classification, artifact completeness review, and regulatory inspection readiness across <code>/etmf</code>.</p>",
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
                versionId: "ver-trn-202",
                slug: "sop-trn-202-gxp-training-attestation",
                title: "SOP-TRN-202: GxP Training Curriculum Completion & Delegation Sign-Off",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.2",
                versionIndex: 1,
                tags: ["training", "delegation", "ich-e6", "qualification"],
                markdown: """
                    ## GxP Role Qualification & Training Attestation
                    Defines mandatory SOP read-and-understand training completion and Principal Investigator Delegation of Authority prerequisites on `/training`.

                    ## Qualification Rules
                    1. Complete assigned protocol and GxP SOP modules prior to performing delegated clinical duties.
                    2. Maintain current training records across protocol amendments to preserve active write access.
                    """,
                html: "<h2>GxP Role Qualification &amp; Training Attestation</h2><p>Defines mandatory SOP read-and-understand training completion and Principal Investigator Delegation of Authority prerequisites on <code>/training</code>.</p>",
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

            // 13. /observations/:observationRef (Clinical Observation Provenance & Query Review)
            CreateSop(
                articleId: "art-observations-sop",
                versionId: "ver-obs-210",
                slug: "sop-obs-210-clinical-observation-inspection",
                title: "SOP-OBS-210: Clinical Observation Inspection, Query History & Provenance Review",
                categoryId: "cat-data-management",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["observations", "provenance", "sdv", "queries", "safety"],
                markdown: """
                    ## Clinical Observation Provenance & Discrepancy Review
                    Standardizes item-level observation inspection, audit provenance verification, medical safety review, and query resolution on `/observations/:observationRef`.

                    ## Key Controls
                    1. Inspect source provenance, prior amendments, and open queries before updating or verifying an observation.
                    2. Escalate clinically significant safety abnormalities or MedDRA coding questions with pseudonymous subject codes only.
                    """,
                html: "<h2>Clinical Observation Provenance &amp; Discrepancy Review</h2><p>Standardizes item-level observation inspection, audit provenance verification, medical safety review, and query resolution on <code>/observations/:observationRef</code>.</p>",
                createdAt: baseTime.AddMinutes(-4),
                mappings: BuildParameterizedObservationMappings(
                    articleId: "art-observations-sop",
                    createdAt: baseTime.AddMinutes(-4))),

            // 14. /tickets (GxP Support Ticket Triage & Part 11 Closure)
            CreateSop(
                articleId: "art-tickets-sop",
                versionId: "ver-tck-501",
                slug: "sop-tck-501-gxp-support-ticket-lifecycle",
                title: "SOP-TCK-501: GxP Support Ticket Triage, Root-Cause CAPA & Part 11 Closure",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["tickets", "support", "capa", "sla", "part11"],
                markdown: """
                    ## GxP Support Ticket Triage & Closure Governance
                    Defines severity classification, SLA pause/resume tracking, participant de-identification rules, root-cause documentation, and 21 CFR Part 11 electronic signatures on `/tickets`.

                    ## Mandatory Rules
                    1. Never enter direct participant PII in ticket descriptions, comments, or root-cause summaries; use pseudonymous subject codes (for example `SUBJ-1001`).
                    2. Record structured resolution codes and root-cause summaries prior to resolving tickets and applying Part 11 closure signatures.
                    """,
                html: "<h2>GxP Support Ticket Triage &amp; Closure Governance</h2><p>Defines severity classification, SLA pause/resume tracking, participant de-identification rules, root-cause documentation, and 21 CFR Part 11 electronic signatures on <code>/tickets</code>.</p>",
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
                versionId: "ver-knb-502",
                slug: "sop-knb-502-controlled-knowledge-hub",
                title: "SOP-KNB-502: Controlled SOP Knowledge Hub & Regulatory Version Governance",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["knowledge", "sop", "versioning", "part11"],
                markdown: """
                    ## Controlled SOP Repository Usage
                    Governs role-scoped SOP lookup, locked snapshot inspection, and 21 CFR Part 11 Merkle audit verification on `/knowledge`.

                    ## Operational Steps
                    1. Verify that referenced SOP guidance carries `PUBLISHED` status and a locked version snapshot before executing regulated procedures.
                    2. Inspect the 21 CFR Part 11 audit trail tab to verify cryptographic SHA-256 block hashes during quality or regulatory reviews.
                    """,
                html: "<h2>Controlled SOP Repository Usage</h2><p>Governs role-scoped SOP lookup, locked snapshot inspection, and 21 CFR Part 11 Merkle audit verification on <code>/knowledge</code>.</p>",
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

            // 16. Clinical Canvas: Design -> Protocol versions (?stage=design&section=protocol-versions)
            CreateSop(
                articleId: "art-can-prot-sop",
                versionId: "ver-can-110",
                slug: "sop-can-110-canvas-protocol-versions",
                title: "SOP-CAN-110: Clinical Canvas Protocol Versions, USDM Import & Amendment Impact",
                categoryId: "cat-study-authoring",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "protocol-versions", "usdm", "design", "amendments"],
                markdown: """
                    ## Protocol Versions & USDM Draft Governance
                    Governs protocol version inspection, USDM JSON draft import, Schedule of Activities review, and amendment impact evaluation in Clinical Canvas (`stage=design&section=protocol-versions`).

                    ## Key Controls
                    1. Validate USDM schedule structure, visit windows, and form bindings before publishing a new protocol version.
                    2. Record a 21 CFR Part 11 justification whenever importing or superseding protocol versions.
                    """,
                html: "<h2>Protocol Versions &amp; USDM Draft Governance</h2><p>Governs protocol version inspection, USDM JSON draft import, Schedule of Activities review, and amendment impact evaluation in Clinical Canvas.</p>",
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-55),
                mappings: BuildCanvasSectionMappings(
                    code: "pv",
                    stage: "design",
                    section: "protocol-versions",
                    includeStageFallback: true,
                    articleId: "art-can-prot-sop",
                    defaultAnchor: "#canvas-protocol-versions",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-55))),

            // 17. Clinical Canvas: Start -> Sites (?stage=start&section=sites)
            CreateSop(
                articleId: "art-can-sites-sop",
                versionId: "ver-can-111",
                slug: "sop-can-111-canvas-study-sites",
                title: "SOP-CAN-111: Clinical Canvas Study Site Roster & Investigator Assignment",
                categoryId: "cat-site-startup",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "sites", "investigator", "site-roster", "start"],
                markdown: """
                    ## Study Site Roster & Investigator Binding
                    Standardizes clinical site linking, Principal Investigator assignment, and country-level site scope verification in Clinical Canvas (`stage=start&section=sites`).

                    ## Operational Steps
                    1. Confirm organization and facility identifiers prior to linking a clinical site to the study.
                    2. Verify site code uniqueness and RBAC scope visibility for assigned site coordinators and monitors.
                    """,
                html: "<h2>Study Site Roster &amp; Investigator Binding</h2><p>Standardizes clinical site linking, Principal Investigator assignment, and country-level site scope verification in Clinical Canvas.</p>",
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-52),
                mappings: BuildCanvasSectionMappings(
                    code: "st",
                    stage: "start",
                    section: "sites",
                    includeStageFallback: true,
                    articleId: "art-can-sites-sop",
                    defaultAnchor: "#canvas-study-sites",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-52))),

            // 18. Clinical Canvas: Start -> Site activation (?stage=start&section=site-activation)
            CreateSop(
                articleId: "art-can-actv-sop",
                versionId: "ver-can-112",
                slug: "sop-can-112-canvas-site-activation",
                title: "SOP-CAN-112: Clinical Canvas Site Activation Lifecycle & Country Regulatory Gates",
                categoryId: "cat-site-startup",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "site-activation", "irb-iec", "regulatory", "start"],
                markdown: """
                    ## Site Activation Lifecycle Controls
                    Defines site status progression from feasibility and IRB/IEC submission through activation readiness in Clinical Canvas (`stage=start&section=site-activation`).

                    ## Mandatory Checks
                    1. Confirm ethics approval and executed clinical trial agreement dates before advancing site activation state.
                    2. Document regulatory submission blockers via support escalation with study and site scope attached.
                    """,
                html: "<h2>Site Activation Lifecycle Controls</h2><p>Defines site status progression from feasibility and IRB/IEC submission through activation readiness in Clinical Canvas.</p>",
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-49),
                mappings: BuildCanvasSectionMappings(
                    code: "sa",
                    stage: "start",
                    section: "site-activation",
                    includeStageFallback: false,
                    articleId: "art-can-actv-sop",
                    defaultAnchor: "#canvas-site-activation",
                    createdAt: baseTime.AddMinutes(-1).AddSeconds(-49))),

            // 19. Clinical Canvas: Start -> Startup readiness (?stage=start&section=startup-greenlight)
            CreateSop(
                articleId: "art-can-grnl-sop",
                versionId: "ver-can-113",
                slug: "sop-can-113-canvas-startup-greenlight",
                title: "SOP-CAN-113: Clinical Canvas Startup Readiness Matrix & Greenlight Certification",
                categoryId: "cat-site-startup",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "startup-greenlight", "readiness", "ssu", "start"],
                markdown: """
                    ## Startup Readiness & Enrollment Greenlight Gate
                    Governs pre-enrollment checklist verification across essential documents, training completion, delegation sign-off, and IP pharmacy release (`stage=start&section=startup-greenlight`).

                    ## Greenlight Rules
                    1. Verify 100% completion of mandatory startup prerequisites prior to issuing site enrollment greenlight.
                    2. Retain immutable certification timestamps and approver attribution in the study audit trail.
                    """,
                html: "<h2>Startup Readiness &amp; Enrollment Greenlight Gate</h2><p>Governs pre-enrollment checklist verification across essential documents, training completion, delegation sign-off, and IP pharmacy release.</p>",
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
                versionId: "ver-can-114",
                slug: "sop-can-114-canvas-essential-documents",
                title: "SOP-CAN-114: Clinical Canvas Essential Documents & Study eTMF Binder QC",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "documents", "etmf", "dia-reference-model", "start"],
                markdown: """
                    ## Study Essential Document Filing & QC
                    Standardizes DIA TMF Reference Model filing, metadata verification, and quality review within Clinical Canvas (`stage=start&section=documents`).

                    ## Operational Controls
                    1. Classify uploaded essential documents under the correct study and site scope without participant PII.
                    2. Complete QC verification prior to relying on regulatory artifacts for site greenlight.
                    """,
                html: "<h2>Study Essential Document Filing &amp; QC</h2><p>Standardizes DIA TMF Reference Model filing, metadata verification, and quality review within Clinical Canvas.</p>",
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
                versionId: "ver-can-115",
                slug: "sop-can-115-canvas-study-training",
                title: "SOP-CAN-115: Clinical Canvas Study Training Curricula & Qualification Gating",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "training", "qualification", "ich-e6", "start"],
                markdown: """
                    ## Study Protocol Training & Role Qualification
                    Defines protocol-specific training assignment, read-and-understand attestation, and write-access qualification checks (`stage=start&section=training`).

                    ## Qualification Rules
                    1. Ensure site personnel complete required protocol version curricula prior to delegation sign-off.
                    2. Re-verify training currency following major protocol amendments.
                    """,
                html: "<h2>Study Protocol Training &amp; Role Qualification</h2><p>Defines protocol-specific training assignment, read-and-understand attestation, and write-access qualification checks.</p>",
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
                versionId: "ver-can-116",
                slug: "sop-can-116-canvas-site-milestones",
                title: "SOP-CAN-116: Clinical Canvas Site Activation Milestones & Target Variance Review",
                categoryId: "cat-site-monitoring",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "site-milestones", "ctms", "timelines", "start"],
                markdown: """
                    ## Site Milestone Tracking & Baseline Variance Governance
                    Governs planned, baseline, and actual startup milestone tracking across study sites (`stage=start&section=site-milestones`).

                    ## Operational Steps
                    1. Record contemporaneous actual completion dates for IRB submission, contract execution, and SIV completion.
                    2. Escalate critical-path startup delays with root-cause commentary.
                    """,
                html: "<h2>Site Milestone Tracking &amp; Baseline Variance Governance</h2><p>Governs planned, baseline, and actual startup milestone tracking across study sites.</p>",
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
                versionId: "ver-can-117",
                slug: "sop-can-117-canvas-delegation-log",
                title: "SOP-CAN-117: Clinical Canvas Delegation of Authority Log & PI Electronic Sign-Off",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "delegation", "doa-log", "part11", "start"],
                markdown: """
                    ## Principal Investigator Delegation of Authority (DoA) Governance
                    Establishes electronic DoA duty assignment, effective date tracking, and Principal Investigator 21 CFR Part 11 countersignature rules (`stage=start&section=delegation`).

                    ## Mandatory Controls
                    1. Verify active GxP training completion before delegating clinical assessment or eCRF entry responsibilities.
                    2. Capture step-up electronic signatures when authorizing or ending delegated site duties.
                    """,
                html: "<h2>Principal Investigator Delegation of Authority (DoA) Governance</h2><p>Establishes electronic DoA duty assignment, effective date tracking, and Principal Investigator 21 CFR Part 11 countersignature rules.</p>",
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
                versionId: "ver-can-118",
                slug: "sop-can-118-canvas-participants-execution",
                title: "SOP-CAN-118: Clinical Canvas Participant Enrollment, Visit Forms & Source Verification",
                categoryId: "cat-clinical-ops",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "participants", "enrollment", "ecrf", "conduct"],
                markdown: """
                    ## Participant Lifecycle, Visit Matrix & eCRF Execution
                    Governs participant screening, enrollment, visit collection instances, source data verification, and PI form signatures (`stage=conduct&section=participants`).

                    ## Mandatory GxP Controls
                    1. Reference participants strictly by pseudonymous subject code (for example `SUBJ-1001`); never record direct PII.
                    2. Provide an auditable 21 CFR Part 11 reason for change on every observation amendment or form correction.
                    """,
                html: "<h2>Participant Lifecycle, Visit Matrix &amp; eCRF Execution</h2><p>Governs participant screening, enrollment, visit collection instances, source data verification, and PI form signatures.</p>",
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
                versionId: "ver-can-119",
                slug: "sop-can-119-canvas-monitoring-visits",
                title: "SOP-CAN-119: Clinical Canvas Site Monitoring Visits, Findings & Follow-Up Letters",
                categoryId: "cat-site-monitoring",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "monitoring-visits", "mvr", "sdv", "conduct"],
                markdown: """
                    ## Site Monitoring Visit Execution & Finding Resolution
                    Standardizes SIV, IMV, and COV visit execution, finding severity grading, and compiled follow-up letter sign-off (`stage=conduct&section=monitoring-visits`).

                    ## Operational Checklist
                    1. Reconcile SDV coverage and open discrepancy queries before finalizing monitoring visit reports.
                    2. Escalate major or critical monitoring findings to the quality and site operations queue with site scope preserved.
                    """,
                html: "<h2>Site Monitoring Visit Execution &amp; Finding Resolution</h2><p>Standardizes SIV, IMV, and COV visit execution, finding severity grading, and compiled follow-up letter sign-off.</p>",
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
                versionId: "ver-can-120",
                slug: "sop-can-120-canvas-investigational-supply",
                title: "SOP-CAN-120: Clinical Canvas Investigational Supply, Kit Dispensing & Excursion Control",
                categoryId: "cat-clinical-ops",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "supply", "rtsm", "ip-accountability", "conduct"],
                markdown: """
                    ## Investigational Product Inventory & Temperature Excursion Controls
                    Governs depot-to-site shipments, kit quarantine on temperature excursion, subject dispensing, and IP accountability (`stage=conduct&section=supply`).

                    ## Mandatory Controls
                    1. Quarantine affected kit lots immediately upon cold-chain excursion detection and escalate under `SUPPLY_EXCURSION`.
                    2. Maintain blinded kit reconciliation records across dispensing and return workflows.
                    """,
                html: "<h2>Investigational Product Inventory &amp; Temperature Excursion Controls</h2><p>Governs depot-to-site shipments, kit quarantine on temperature excursion, subject dispensing, and IP accountability.</p>",
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
                versionId: "ver-can-121",
                slug: "sop-can-121-canvas-discrepancy-queries",
                title: "SOP-CAN-121: Clinical Canvas Discrepancy Query Triage, Site Response & Closure",
                categoryId: "cat-data-management",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "queries", "discrepancies", "data-cleaning", "data"],
                markdown: """
                    ## Study Discrepancy Query Triage & Resolution
                    Defines manual and rule-generated query review, site coordinator response, and data manager closure verification (`stage=data&section=queries`).

                    ## Operational Steps
                    1. Inspect linked observation provenance before answering or closing open discrepancy queries.
                    2. Never close discrepancy queries without verifying either an observation correction or a documented clinical explanation.
                    """,
                html: "<h2>Study Discrepancy Query Triage &amp; Resolution</h2><p>Defines manual and rule-generated query review, site coordinator response, and data manager closure verification.</p>",
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
                versionId: "ver-can-122",
                slug: "sop-can-122-canvas-scoped-data-locks",
                title: "SOP-CAN-122: Clinical Canvas Scoped Data Locks, Prerequisite Audits & Part 11 Attestation",
                categoryId: "cat-data-management",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "locks", "data-lock", "part11", "data"],
                markdown: """
                    ## Scoped Data Lock & Unlock Governance
                    Governs observation, form, participant, site, and study lock readiness checks, Part 11 step-up electronic signatures, and controlled unlock procedures (`stage=data&section=locks` and `/data-lock`).

                    ## Mandatory Lock Gates
                    1. Resolve all open discrepancy queries, pending SDV items, and required PI signatures before applying a hard lock.
                    2. Record an explicit regulatory justification and step-up signature for every lock or controlled unlock action.
                    """,
                html: "<h2>Scoped Data Lock &amp; Unlock Governance</h2><p>Governs observation, form, participant, site, and study lock readiness checks, Part 11 step-up electronic signatures, and controlled unlock procedures.</p>",
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-19),
                mappings:
                [
                    ..BuildCanvasSectionMappings(
                        code: "lk",
                        stage: "data",
                        section: "locks",
                        includeStageFallback: false,
                        articleId: "art-can-lock-sop",
                        defaultAnchor: "#canvas-scoped-data-locks",
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-19)),
                    ..BuildRouteMappings(
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
                versionId: "ver-can-123",
                slug: "sop-can-123-canvas-rule-validation",
                title: "SOP-CAN-123: Clinical Canvas Edit-Check Rule Packs, Dry-Run Preview & Validation Findings",
                categoryId: "cat-data-management",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "validation", "edit-checks", "rules", "data"],
                markdown: """
                    ## Edit-Check Rule Pack Authoring & Validation Findings
                    Standardizes deterministic edit-check specification, dry-run impact preview, rule pack activation, and cross-visit validation triage (`stage=data&section=validation`).

                    ## Key Controls
                    1. Execute dry-run previews against study observations to verify false-positive rates before activating new rule packs.
                    2. Review validation findings alongside query ownership to coordinate timely site data cleaning.
                    """,
                html: "<h2>Edit-Check Rule Pack Authoring &amp; Validation Findings</h2><p>Standardizes deterministic edit-check specification, dry-run impact preview, rule pack activation, and cross-visit validation triage.</p>",
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
                versionId: "ver-can-125",
                slug: "sop-can-125-canvas-analysis-readiness",
                title: "SOP-CAN-125: Clinical Canvas Endpoint Analysis Readiness & Blinded Review",
                categoryId: "cat-data-management",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "analyze", "blinded-review", "biostatistics"],
                markdown: """
                    ## Blinded Data Review & Endpoint Analysis Readiness
                    Defines pre-lock blinded data review, population evaluability checks, and statistical readiness governance (`stage=analyze`).

                    ## Operational Controls
                    1. Confirm database lock prerequisites and protocol deviation classifications prior to unblinded analysis cutoffs.
                    2. Preserve treatment-assignment blinding across operational support escalations.
                    """,
                html: "<h2>Blinded Data Review &amp; Endpoint Analysis Readiness</h2><p>Defines pre-lock blinded data review, population evaluability checks, and statistical readiness governance.</p>",
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
                versionId: "ver-can-124",
                slug: "sop-can-124-canvas-regulated-exports",
                title: "SOP-CAN-124: Clinical Canvas Regulated Dataset & Audit-Trail Export Generation",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["clinical-canvas", "exports", "submission", "cdisc", "submit"],
                markdown: """
                    ## Regulated Clinical Dataset & Audit-Trail Exports
                    Governs authorized clinical observation exports, 21 CFR Part 11 audit-trail archival packages, and submission readiness checks (`stage=submit&section=exports` and `/exports`).

                    ## Mandatory Controls
                    1. Verify study/site authorization and lock state prior to generating regulatory submission or inspection export bundles.
                    2. Confirm SHA-256 manifest integrity and pseudonymous subject de-identification on exported deliverables.
                    """,
                html: "<h2>Regulated Clinical Dataset &amp; Audit-Trail Exports</h2><p>Governs authorized clinical observation exports, 21 CFR Part 11 audit-trail archival packages, and submission readiness checks.</p>",
                createdAt: baseTime.AddMinutes(-1).AddSeconds(-10),
                mappings:
                [
                    ..BuildCanvasSectionMappings(
                        code: "ex",
                        stage: "submit",
                        section: "exports",
                        includeStageFallback: true,
                        articleId: "art-can-expt-sop",
                        defaultAnchor: "#canvas-regulated-exports",
                        createdAt: baseTime.AddMinutes(-1).AddSeconds(-10)),
                    ..BuildRouteMappings(
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
                versionId: "ver-rbac-403",
                slug: "sop-rbac-403-rbac-access-refusal-governance",
                title: "SOP-RBAC-403: Role-Based Access Refusal, Least-Privilege Scope & Provisioning Escalation",
                categoryId: "cat-regulatory-compliance",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["forbidden", "rbac", "access-control", "least-privilege", "provisioning"],
                markdown: """
                    ## Role-Based Access Control (RBAC) Refusal & Scope Escalation
                    Explains ICH E6(R3) least-privilege role enforcement, study/site assignment boundaries, and controlled access request procedures when landing on `/forbidden`.

                    ## Operational Steps
                    1. Confirm your active workspace persona and assigned study/site scope before retrying restricted clinical routes.
                    2. Escalate legitimate role or site provisioning requests under `ACCESS_CONTROL` for Sponsor Admin review and training verification.
                    """,
                html: "<h2>Role-Based Access Control (RBAC) Refusal &amp; Scope Escalation</h2><p>Explains ICH E6(R3) least-privilege role enforcement, study/site assignment boundaries, and controlled access request procedures on <code>/forbidden</code>.</p>",
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
                versionId: "ver-sys-404",
                slug: "sop-sys-404-module-availability-fallback",
                title: "SOP-SYS-404: Pre-Release Workspace Module Availability & Validated Fallback Workflows",
                categoryId: "cat-clinical-ops",
                versionLabel: "1.0",
                versionIndex: 1,
                tags: ["unavailable", "roadmap", "validated-workflows", "clinical-canvas"],
                markdown: """
                    ## Workspace Module Availability & Validated Clinical Canvas Fallbacks
                    Guides operators encountering pre-release module screens on `/unavailable` to the validated Clinical Kernel workflows active in Clinical Canvas.

                    ## Operational Steps
                    1. Use Clinical Canvas (`/studies/:studyCode`), Data Collection (`/ecrf`), Site Readiness (`/ctms`, `/ssu`), eTMF (`/etmf`), Training (`/training`), Support (`/tickets`), and Knowledge (`/knowledge`) for live regulated operations.
                    2. Log an operational support ticket under `SYSTEM_SUPPORT` if an expected entitlement or module workflow is unavailable.
                    """,
                html: "<h2>Workspace Module Availability &amp; Validated Clinical Canvas Fallbacks</h2><p>Guides operators encountering pre-release module screens on <code>/unavailable</code> to validated Clinical Kernel workflows active in Clinical Canvas.</p>",
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
