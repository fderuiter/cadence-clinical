using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

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
        "cra_monitor",
        "data_manager",
        "auditor",
    });

    private static readonly string[] AllStaffPersonas =
    [
        "super_admin",
        "sponsor_designer",
        "site_crc",
        "cra_monitor",
        "data_manager",
        "auditor",
    ];

    public static async Task EnsureSeededAsync(KnowledgeDbContext db, IClock clock, CancellationToken ct = default)
    {
        // Fast path: once our canonical SSU + Studies + eCRF + CTMS SOPs exist, skip locking and writes entirely.
        bool alreadySeeded = await db.KnowledgeArticles
            .AsNoTracking()
            .AnyAsync(a => a.Id == "art-ssu-sop", ct)
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
                .AnyAsync(a => a.Id == "art-ssu-sop", ct)
                .ConfigureAwait(false);
            if (recheck)
            {
                return;
            }

            using var _ = KnowledgeAuditActorScope.Begin(CatalogServiceAccount);
            DateTimeOffset seededAt = clock.UtcNow;

            var categories = BuildCategories(seededAt);
            var existingCategoryIds = await db.KnowledgeCategories
                .Select(c => c.Id)
                .ToHashSetAsync(StringComparer.Ordinal, ct)
                .ConfigureAwait(false);

            foreach (var category in categories.Where(c => !existingCategoryIds.Contains(c.Id)))
            {
                db.KnowledgeCategories.Add(category);
            }

            var seeds = BuildSopSeeds(seededAt);
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

    private sealed record SopSeed(
        KnowledgeArticle Article,
        KnowledgeArticleVersion Version,
        IReadOnlyList<ContextualHelpMapping> Mappings);

    private static List<KnowledgeCategory> BuildCategories(DateTimeOffset createdAt) =>
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

    private static List<SopSeed> BuildSopSeeds(DateTimeOffset baseTime)
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
            ["cra_monitor"] = "#study-sdv-and-query-oversight",
            ["data_manager"] = "#study-query-triage-and-cleanliness",
            ["auditor"] = "#study-audit-trail-inspection",
        };

        var list = new List<ContextualHelpMapping>
        {
            NewMapping("m-stp-any", "/studies/:studyCode", null, articleId, "#active-study-clinical-canvas", 30, createdAt),
            NewMapping("m-stps-any", "/studies/:studyCode/*", null, articleId, "#active-study-clinical-canvas", 28, createdAt),
        };

        foreach (string persona in AllStaffPersonas)
        {
            string anchor = personaAnchors[persona];
            list.Add(NewMapping($"m-stp-{persona}", "/studies/:studyCode", persona, articleId, anchor, 35, createdAt));
            list.Add(NewMapping($"m-stps-{persona}", "/studies/:studyCode/*", persona, articleId, anchor, 32, createdAt));
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
