import { findSection, findStage } from "../canvas/lifecycle";
import { listDemoCatalogModules } from "../router/availability.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { createOpenApiClient, openApiClient, unwrapResponse } from "./client";
import type { paths as KnowledgePaths } from "./generated/knowledge";

const knowledgeClient = createOpenApiClient<KnowledgePaths>();

const DOCS_PORTAL_MARKDOWN_FOOTER =
  "\n\n## Documentation Portal\n- [Open Cadence Clinical Documentation Portal](/cadence-clinical/)";
const DOCS_PORTAL_HTML_FOOTER =
  '<h3>Documentation Portal</h3><p><a href="/cadence-clinical/">Open Cadence Clinical Documentation Portal</a></p>';

function isNonDemoProductionBuild(): boolean {
  return import.meta.env.MODE === "production" && !isDemoBuild();
}

/**
 * Bundled offline help categories. Served only by a demo build when the API is unreachable (#4523).
 */
export const DEFAULT_CATEGORIES = isNonDemoProductionBuild()
  ? []
  : [
      {
        id: "cat-clinical-ops",
        name: "Clinical Operations",
        slug: "clinical-operations",
        description:
          "eCRF data capture, subject enrollment, and visit procedures.",
        persona_visibility: [
          "site_crc",
          "site_investigator",
          "safety_officer",
          "cra_monitor",
          "data_manager",
          "super_admin",
        ],
        parent_id: null,
        is_deleted: false,
        created_at: "2026-01-15T08:00:00Z",
        created_by: "svc:knowledge-sop-catalog",
      },
      {
        id: "cat-study-authoring",
        name: "Study Authoring & MDR",
        slug: "study-authoring-mdr",
        description:
          "CDISC USDM protocol authoring, SoA matrix design, and digitizer workflows.",
        persona_visibility: ["sponsor_designer", "data_manager", "super_admin"],
        parent_id: null,
        is_deleted: false,
        created_at: "2026-01-15T08:00:00Z",
        created_by: "svc:knowledge-sop-catalog",
      },
      {
        id: "cat-site-startup",
        name: "Site Startup & Activation",
        slug: "site-startup-activation",
        description:
          "IRB/IEC submissions, regulatory greenlight checklists, delegation logs, and site initiation readiness.",
        persona_visibility: [
          "cra_monitor",
          "site_crc",
          "site_investigator",
          "sponsor_designer",
          "super_admin",
        ],
        parent_id: null,
        is_deleted: false,
        created_at: "2026-01-15T08:00:00Z",
        created_by: "svc:knowledge-sop-catalog",
      },
      {
        id: "cat-site-monitoring",
        name: "Site Monitoring & CTMS",
        slug: "site-monitoring-ctms",
        description:
          "CRA monitoring visits, source data verification, and trip reports.",
        persona_visibility: [
          "cra_monitor",
          "site_crc",
          "site_investigator",
          "super_admin",
        ],
        parent_id: null,
        is_deleted: false,
        created_at: "2026-01-15T08:00:00Z",
        created_by: "svc:knowledge-sop-catalog",
      },
      {
        id: "cat-regulatory-compliance",
        name: "Regulatory & Quality Compliance",
        slug: "regulatory-compliance",
        description:
          "21 CFR Part 11 compliance, audit trail inspection, and digital signatures.",
        persona_visibility: [
          "auditor",
          "super_admin",
          "sponsor_designer",
          "data_manager",
          "safety_officer",
          "site_investigator",
        ],
        parent_id: null,
        is_deleted: false,
        created_at: "2026-01-15T08:00:00Z",
        created_by: "svc:knowledge-sop-catalog",
      },
      {
        id: "cat-data-management",
        name: "Data Management & Coding",
        slug: "data-management-coding",
        description:
          "MedDRA / WHODrug coding, query management, and data lock workflows.",
        persona_visibility: [
          "data_manager",
          "safety_officer",
          "sponsor_designer",
          "super_admin",
        ],
        parent_id: null,
        is_deleted: false,
        created_at: "2026-01-15T08:00:00Z",
        created_by: "svc:knowledge-sop-catalog",
      },
    ];

interface BundledSopSpec {
  mappingId: string;
  routePattern: string;
  persona: string;
  priority: number;
  articleId: string;
  versionId: string;
  title: string;
  slug: string;
  versionIndex?: number;
  versionLabel?: string;
  tags: string[];
  categoryId: string;
  categoryName: string;
  authorUserId?: string;
  sectionAnchor: string;
  heading: string;
  summary: string;
  steps?: string[];
  extraHtml?: string;
  reasonForChange?: string;
  updatedAt?: string;
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function buildBundledSop(spec: BundledSopSpec): Record<string, unknown> {
  const versionIndex = spec.versionIndex ?? 1;
  const versionLabel = spec.versionLabel ?? "1.0";
  const author = spec.authorUserId ?? "qa_director";
  const updatedAt = spec.updatedAt ?? "2026-06-15T10:00:00Z";
  const anchorId = spec.sectionAnchor.replace(/^#+/, "");
  const steps =
    spec.steps && spec.steps.length > 0
      ? spec.steps
      : [
          `Verify assigned study, site, and role permissions prior to executing ${spec.heading.toLowerCase()}.`,
          "Record contemporaneous 21 CFR Part 11 change reasons for regulated updates and escalate blockers via Contextual Help using pseudonymous subject codes only.",
        ];
  const stepsMarkdown = steps
    .map((step, idx) => `${idx + 1}. ${step}`)
    .join("\n");
  const stepsHtml = steps
    .map((step) => `<li>${escapeHtml(step)}</li>`)
    .join("");
  return {
    matched_mapping: {
      id: spec.mappingId,
      route_pattern: spec.routePattern,
      persona: spec.persona,
      article_id: spec.articleId,
      section_anchor: spec.sectionAnchor,
      priority: spec.priority,
      is_active: true,
    },
    primary_article: {
      id: spec.articleId,
      title: spec.title,
      slug: spec.slug,
      status: "PUBLISHED",
      version_index: versionIndex,
      version_label: versionLabel,
      tags: spec.tags,
      category_id: spec.categoryId,
      category_name: spec.categoryName,
      author_user_id: author,
      last_edited_by: author,
      approved_by: "qa_director",
      created_at: "2026-02-01T09:00:00Z",
      updated_at: updatedAt,
      body_markdown: `## ${spec.heading}\n${spec.summary}\n\n${stepsMarkdown}`,
      body_html: `<h2 id="${escapeHtml(anchorId)}">${escapeHtml(spec.heading)}</h2><p>${escapeHtml(spec.summary)}</p><ol>${stepsHtml}</ol>${spec.extraHtml || ""}`,
    },
    primary_version: {
      id: spec.versionId,
      version_index: versionIndex,
      version_label: versionLabel,
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: author,
      reason_for_change: spec.reasonForChange ?? "Initial controlled release",
      created_at: updatedAt,
    },
    section_anchor: spec.sectionAnchor,
    related_articles: [],
  };
}

function buildCanvasSectionSopEntries(
  querySuffix: string,
  spec: Omit<BundledSopSpec, "routePattern" | "mappingId"> & {
    mappingPrefix: string;
    stageFallback?: string;
  }
): Record<string, Record<string, unknown>> {
  const studyPattern = `/studies/:studyCode?${querySuffix}`;
  const directoryPattern = `/studies?${querySuffix}`;
  const entries: Record<string, Record<string, unknown>> = {
    [studyPattern]: buildBundledSop({
      ...spec,
      mappingId: `${spec.mappingPrefix}-study`,
      routePattern: studyPattern,
    }),
    [directoryPattern]: buildBundledSop({
      ...spec,
      mappingId: `${spec.mappingPrefix}-dir`,
      routePattern: directoryPattern,
    }),
  };
  if (spec.stageFallback) {
    const studyStagePattern = `/studies/:studyCode?stage=${spec.stageFallback}`;
    const dirStagePattern = `/studies?stage=${spec.stageFallback}`;
    entries[studyStagePattern] = buildBundledSop({
      ...spec,
      priority: Math.max(1, spec.priority - 4),
      mappingId: `${spec.mappingPrefix}-study-stage`,
      routePattern: studyStagePattern,
    });
    entries[dirStagePattern] = buildBundledSop({
      ...spec,
      priority: Math.max(1, spec.priority - 4),
      mappingId: `${spec.mappingPrefix}-dir-stage`,
      routePattern: dirStagePattern,
    });
  }
  return entries;
}

const DATA_LOCK_SPEC: Omit<BundledSopSpec, "routePattern" | "mappingId"> = {
  persona: "data_manager",
  priority: 45,
  articleId: "art-can-locks",
  versionId: "ver-can-122",
  title:
    "SOP-CAN-122: Scoped Data Locks, Freeze Verification & Database Unlock",
  slug: "sop-can-122-scoped-data-locks",
  tags: ["canvas", "data-lock", "freeze", "attestation", "part11"],
  categoryId: "cat-data-management",
  categoryName: "Data Management & Coding",
  authorUserId: "lead_data_manager",
  sectionAnchor: "#canvas-scoped-data-locks",
  heading: "Scoped Data Lock & Unlock Governance",
  summary:
    "Controls observation, form, participant, site, and study lock prerequisites, query closure checks, and 21 CFR Part 11 signed unlock justifications.",
};

const REGULATED_EXPORTS_SPEC: Omit<
  BundledSopSpec,
  "routePattern" | "mappingId"
> = {
  persona: "auditor",
  priority: 45,
  articleId: "art-can-exports",
  versionId: "ver-can-124",
  title:
    "SOP-CAN-124: Regulated Submission Exports, CDISC Archives & Checksums",
  slug: "sop-can-124-regulated-exports",
  tags: ["canvas", "exports", "cdisc", "odm", "submission"],
  categoryId: "cat-regulatory-compliance",
  categoryName: "Regulatory & Quality Compliance",
  authorUserId: "qa_director",
  sectionAnchor: "#canvas-regulated-exports",
  heading: "Regulated Submission Export Verification",
  summary:
    "Governs CDISC ODM/SDTM archive generation, cryptographic SHA-256 manifest verification, and read-only regulatory inspection packages.",
};

/**
 * Bundled offline help SOPs. Served only by a demo build when the API is unreachable (#4523).
 */
function createDefaultContextualSops(): Record<
  string,
  Record<string, unknown>
> {
  if (isNonDemoProductionBuild()) {
    return {};
  }
  return {
    "/overview": buildBundledSop({
      mappingId: "map-overview-01",
      routePattern: "/overview",
      persona: "super_admin",
      priority: 25,
      articleId: "art-overview-sop",
      versionId: "ver-ops-100",
      title: "SOP-OPS-100: Workspace Overview & Role-Scoped Orientation",
      slug: "sop-ops-100-workspace-overview",
      tags: ["overview", "orientation", "rbac", "workspace"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      authorUserId: "sponsor_study_lead",
      sectionAnchor: "#workspace-overview-orientation",
      heading: "Workspace Overview & Role Orientation",
      summary:
        "Guides clinical operators through role-scoped study readiness indicators, pending GxP tasks, and compliant navigation.",
    }),
    "/plans": buildBundledSop({
      mappingId: "map-plans-01",
      routePattern: "/plans",
      persona: "super_admin",
      priority: 25,
      articleId: "art-plans-sop",
      versionId: "ver-pln-105",
      title:
        "SOP-PLN-105: Study Execution Plans, Milestones & Operational Readiness",
      slug: "sop-pln-105-study-execution-plans",
      tags: ["plans", "milestones", "ctms", "readiness"],
      categoryId: "cat-site-monitoring",
      categoryName: "Site Monitoring & CTMS",
      sectionAnchor: "#study-execution-plan-milestones",
      heading: "Study Execution Plan Governance",
      summary:
        "Defines milestone tracking, enrollment forecasting, and cross-functional study execution plans on /plans.",
    }),
    "/onboarding/trial": buildBundledSop({
      mappingId: "map-trial-onboarding-01",
      routePattern: "/onboarding/trial",
      persona: "super_admin",
      priority: 25,
      articleId: "art-onboarding-trial-sop",
      versionId: "ver-onb-110",
      title:
        "SOP-ONB-110: Clinical Trial Onboarding, Protocol Intake & Site Provisioning",
      slug: "sop-onb-110-clinical-trial-onboarding",
      tags: ["onboarding", "trial-setup", "provisioning", "protocol"],
      categoryId: "cat-site-startup",
      categoryName: "Site Startup & Activation",
      authorUserId: "sponsor_study_lead",
      sectionAnchor: "#clinical-trial-onboarding",
      heading: "Clinical Trial Onboarding Procedure",
      summary:
        "Establishes controlled onboarding steps for new study protocols, organization tenancy bindings, and initial site rosters.",
    }),
    "/forbidden": buildBundledSop({
      mappingId: "map-forbidden-01",
      routePattern: "/forbidden",
      persona: "site_crc",
      priority: 30,
      articleId: "art-forbidden-sop",
      versionId: "ver-rbac-403",
      title:
        "SOP-RBAC-403: Role-Based Access Refusal & Least-Privilege Escalation",
      slug: "sop-rbac-403-rbac-access-refusal",
      tags: ["rbac", "access-control", "forbidden", "least-privilege"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#rbac-access-refusal-and-escalation",
      heading: "RBAC Refusal Verification & Access Provisioning",
      summary:
        "Explains ICH E6(R3) least-privilege role enforcement, training/delegation prerequisites, and audited access-control ticket escalation.",
    }),
    "/unavailable": buildBundledSop({
      mappingId: "map-unavailable-01",
      routePattern: "/unavailable",
      persona: "sponsor_designer",
      priority: 30,
      articleId: "art-unavailable-sop",
      versionId: "ver-sys-404",
      title:
        "SOP-SYS-404: Module Availability & Pre-Release Capability Governance",
      slug: "sop-sys-404-module-availability",
      tags: ["availability", "roadmap", "validation", "gamp5"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#module-availability-and-roadmap",
      heading: "Validated Module Availability & Clinical Canvas Equivalents",
      summary:
        "Details GAMP 5 release gating for pre-release screens and directs operators to validated Clinical Canvas workflows.",
    }),
    "/studies": {
      matched_mapping: {
        id: "map-studies-01",
        route_pattern: "/studies",
        persona: "sponsor_designer",
        article_id: "art-studies-sop",
        section_anchor: "#study-portfolio-governance",
        priority: 20,
        is_active: true,
      },
      primary_article: {
        id: "art-studies-sop",
        title: "SOP-STD-101: Study Portfolio Governance & Workspace Navigation",
        slug: "sop-std-101-study-portfolio-governance",
        status: "PUBLISHED",
        version_index: 1,
        version_label: "1.0",
        tags: ["studies", "portfolio", "governance", "usdm"],
        category_id: "cat-study-authoring",
        category_name: "Study Authoring & MDR",
        author_user_id: "sponsor_study_lead",
        last_edited_by: "sponsor_study_lead",
        approved_by: "qa_director",
        created_at: "2026-02-01T09:00:00Z",
        updated_at: "2026-06-05T10:00:00Z",
        body_markdown: `## Study Portfolio Governance
Standardizes study workspace selection, role-scoped study visibility, and protocol readiness checks.

## Operational Steps
1. Verify protocol code and lifecycle status (\`ACTIVE\`, \`IN_DESIGN\`, or \`LOCKED\`) prior to opening clinical data workspaces.
2. Confirm assigned study and site scope before initiating enrollment, monitoring, or data management actions.
3. Escalate missing study assignments to Platform Access Governance via contextual support ticketing.`,
        body_html: `<h2 id="study-portfolio-governance">Study Portfolio Governance</h2><p>Standardizes study workspace selection, role-scoped study visibility, and protocol readiness checks.</p><h2 id="portfolio-operational-steps">Operational Steps</h2><ol><li>Verify protocol code and lifecycle status (<code>ACTIVE</code>, <code>IN_DESIGN</code>, or <code>LOCKED</code>) prior to opening clinical data workspaces.</li><li>Confirm assigned study and site scope before initiating enrollment, monitoring, or data management actions.</li><li>Escalate missing study assignments to Platform Access Governance via contextual support ticketing.</li></ol>`,
      },
      primary_version: {
        id: "ver-std-101",
        version_index: 1,
        version_label: "1.0",
        status_at_snapshot: "PUBLISHED",
        is_locked: true,
        created_by: "sponsor_study_lead",
        reason_for_change: "Initial controlled release",
        created_at: "2026-06-05T10:00:00Z",
      },
      section_anchor: "#study-portfolio-governance",
      related_articles: [],
    },
    "/studies/:studyCode": {
      matched_mapping: {
        id: "map-study-canvas-01",
        route_pattern: "/studies/:studyCode",
        persona: "site_crc",
        article_id: "art-study-canvas-sop",
        section_anchor: "#clinical-study-canvas-operations",
        priority: 30,
        is_active: true,
      },
      primary_article: {
        id: "art-study-canvas-sop",
        title:
          "SOP-STD-102: Clinical Study Canvas, Enrollment & Protocol Execution",
        slug: "sop-std-102-clinical-study-canvas",
        status: "PUBLISHED",
        version_index: 2,
        version_label: "2.0",
        tags: ["studies", "canvas", "enrollment", "protocol-execution"],
        category_id: "cat-clinical-ops",
        category_name: "Clinical Operations",
        author_user_id: "lead_data_manager",
        last_edited_by: "lead_data_manager",
        approved_by: "qa_director",
        created_at: "2026-02-05T09:00:00Z",
        updated_at: "2026-06-10T10:00:00Z",
        body_markdown: `## Clinical Study Canvas Operations
Governs study-level subject enrollment, Schedule of Activities verification, and site activation monitoring.

## Key Controls
1. Use pseudonymous subject codes (for example \`SUBJ-1001\`) exclusively; never enter direct participant PII.
2. Supply a clear 21 CFR Part 11 reason for change whenever recording or amending clinical observations.
3. Review open form queries and SDV indicators prior to visit sign-off.`,
        body_html: `<h2 id="clinical-study-canvas-operations">Clinical Study Canvas Operations</h2><p id="active-study-clinical-canvas">Governs study-level subject enrollment, Schedule of Activities verification, and site activation monitoring.</p><h2 id="study-canvas-key-controls">Key Controls</h2><ol><li>Use pseudonymous subject codes (for example <code>SUBJ-1001</code>) exclusively; never enter direct participant PII.</li><li>Supply a clear 21 CFR Part 11 reason for change whenever recording or amending clinical observations.</li><li>Review open form queries and SDV indicators prior to visit sign-off.</li></ol><h3 id="participant-timeline-and-visit-record">Participant Timeline &amp; Visit Record</h3><p>Inspect longitudinal visit completion and open form queries within the participant record.</p>`,
      },
      primary_version: {
        id: "ver-std-102",
        version_index: 2,
        version_label: "2.0",
        status_at_snapshot: "PUBLISHED",
        is_locked: true,
        created_by: "lead_data_manager",
        reason_for_change: "Updated for Clinical Canvas v2",
        created_at: "2026-06-10T10:00:00Z",
      },
      section_anchor: "#clinical-study-canvas-operations",
      related_articles: [],
    },
    ...buildCanvasSectionSopEntries("stage=design&section=protocol-versions", {
      mappingPrefix: "map-can-proto",
      stageFallback: "design",
      persona: "sponsor_designer",
      priority: 45,
      articleId: "art-can-proto",
      versionId: "ver-can-110",
      title:
        "SOP-CAN-110: Protocol Versions, USDM Draft Import & Amendment Lineage",
      slug: "sop-can-110-protocol-versions",
      tags: ["canvas", "protocol-versions", "usdm", "odm", "design"],
      categoryId: "cat-study-authoring",
      categoryName: "Study Authoring & MDR",
      authorUserId: "sponsor_study_lead",
      sectionAnchor: "#canvas-protocol-versions",
      heading: "Protocol Version Authoring & Publication",
      summary:
        "Governs ODM XML and USDM JSON protocol draft imports, Schedule of Activities inspection, and immutable version promotion.",
    }),
    ...(() => {
      const sitesSpec = {
        persona: "super_admin",
        priority: 45,
        articleId: "art-can-sites",
        versionId: "ver-can-111",
        title:
          "SOP-CAN-111: Study Site Directory, Country Linking & Assignment",
        slug: "sop-can-111-study-sites",
        categoryId: "cat-site-startup",
        categoryName: "Site Startup & Activation",
        authorUserId: "cra_monitor",
        sectionAnchor: "#canvas-study-sites",
        heading: "Study Site Linking & Principal Investigator Assignment",
        summary:
          "Standardizes linking clinical investigator sites to a study workspace, country feasibility verification, and site roster maintenance.",
      };
      return {
        ...buildCanvasSectionSopEntries("stage=conduct&section=sites", {
          ...sitesSpec,
          mappingPrefix: "map-can-sites-conduct",
          tags: ["canvas", "sites", "site-directory", "conduct"],
        }),
        ...buildCanvasSectionSopEntries("stage=start&section=sites", {
          ...sitesSpec,
          mappingPrefix: "map-can-sites",
          stageFallback: "start",
          tags: ["canvas", "sites", "site-directory", "start"],
        }),
      };
    })(),
    ...buildCanvasSectionSopEntries("stage=start&section=site-activation", {
      mappingPrefix: "map-can-act",
      persona: "cra_monitor",
      priority: 45,
      articleId: "art-can-act",
      versionId: "ver-can-112",
      title:
        "SOP-CAN-112: Site Activation Workflow, IRB/IEC Approval & Enrollment Gate",
      slug: "sop-can-112-site-activation",
      tags: ["canvas", "site-activation", "irb", "iec", "start"],
      categoryId: "cat-site-startup",
      categoryName: "Site Startup & Activation",
      authorUserId: "cra_monitor",
      sectionAnchor: "#canvas-site-activation",
      heading: "Site Activation State Transitions",
      summary:
        "Defines regulatory approval verification, contract execution checks, and signed site activation state transitions prior to enrollment.",
    }),
    ...buildCanvasSectionSopEntries("stage=start&section=startup-greenlight", {
      mappingPrefix: "map-can-grn",
      persona: "cra_monitor",
      priority: 45,
      articleId: "art-can-grn",
      versionId: "ver-can-113",
      title:
        "SOP-CAN-113: Regulatory Greenlight Checklist & IP Release Readiness",
      slug: "sop-can-113-startup-greenlight",
      tags: ["canvas", "startup-greenlight", "greenlight", "ssu"],
      categoryId: "cat-site-startup",
      categoryName: "Site Startup & Activation",
      authorUserId: "cra_monitor",
      sectionAnchor: "#canvas-startup-greenlight",
      heading: "Startup Greenlight Certification",
      summary:
        "Verifies country and site greenlight document hashes, essential regulatory packets, and investigational product release readiness.",
    }),
    ...buildCanvasSectionSopEntries("stage=start&section=documents", {
      mappingPrefix: "map-can-docs",
      persona: "auditor",
      priority: 45,
      articleId: "art-can-docs",
      versionId: "ver-can-114",
      title:
        "SOP-CAN-114: Study Essential Documents, DIA TMF Filing & QC Review",
      slug: "sop-can-114-essential-documents",
      tags: ["canvas", "documents", "etmf", "essential-documents"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      authorUserId: "qa_director",
      sectionAnchor: "#canvas-essential-documents",
      heading: "Essential Document Filing & Quality Control",
      summary:
        "Governs study-scoped DIA TMF Reference Model classification, metadata completeness verification, and CRA quality control sign-off.",
    }),
    ...buildCanvasSectionSopEntries("stage=start&section=training", {
      mappingPrefix: "map-can-trn",
      persona: "site_crc",
      priority: 45,
      articleId: "art-can-trn",
      versionId: "ver-can-115",
      title:
        "SOP-CAN-115: Protocol Training Matrix & Part 11 Completion Sign-Off",
      slug: "sop-can-115-study-training",
      tags: ["canvas", "training", "curriculum", "attestation"],
      categoryId: "cat-site-startup",
      categoryName: "Site Startup & Activation",
      authorUserId: "qa_director",
      sectionAnchor: "#canvas-study-training",
      heading: "Protocol Training Assignments & Attestation",
      summary:
        "Ensures site and sponsor personnel complete version-specific protocol curricula with 21 CFR Part 11 attestation before delegation.",
    }),
    ...buildCanvasSectionSopEntries("stage=start&section=site-milestones", {
      mappingPrefix: "map-can-ms",
      persona: "cra_monitor",
      priority: 45,
      articleId: "art-can-ms",
      versionId: "ver-can-116",
      title:
        "SOP-CAN-116: Site Startup Milestones, SIV Scheduling & FPFV Tracking",
      slug: "sop-can-116-site-milestones",
      tags: ["canvas", "site-milestones", "siv", "fpfv", "ctms"],
      categoryId: "cat-site-monitoring",
      categoryName: "Site Monitoring & CTMS",
      authorUserId: "cra_monitor",
      sectionAnchor: "#canvas-site-milestones",
      heading: "Site Startup & Initiation Milestone Tracking",
      summary:
        "Tracks baseline, planned, and actual dates for ethics submission, Site Initiation Visit completion, and First Participant First Visit.",
    }),
    ...buildCanvasSectionSopEntries("stage=start&section=delegation", {
      mappingPrefix: "map-can-doa",
      persona: "site_investigator",
      priority: 45,
      articleId: "art-can-doa",
      versionId: "ver-can-117",
      title:
        "SOP-CAN-117: Electronic Delegation of Authority (eDoA) Log & PI Sign-Off",
      slug: "sop-can-117-delegation-log",
      tags: ["canvas", "delegation", "doa", "investigator", "part11"],
      categoryId: "cat-site-startup",
      categoryName: "Site Startup & Activation",
      authorUserId: "cra_monitor",
      sectionAnchor: "#canvas-delegation-log",
      heading: "Delegation of Authority Governance",
      summary:
        "Controls site staff duty assignment, training prerequisite verification, and Principal Investigator electronic countersignatures.",
    }),
    ...buildCanvasSectionSopEntries("stage=conduct&section=participants", {
      mappingPrefix: "map-can-part",
      stageFallback: "conduct",
      persona: "site_crc",
      priority: 45,
      articleId: "art-can-part",
      versionId: "ver-can-118",
      title:
        "SOP-CAN-118: Participant Screening, Casebook Execution & Visit Capture",
      slug: "sop-can-118-participants-execution",
      tags: ["canvas", "participants", "enrollment", "casebook", "conduct"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#canvas-participants-execution",
      heading: "Participant Enrollment & Casebook Data Entry",
      summary:
        "Standardizes pseudonymous participant registration, lifecycle state transitions, eCRF visit form entry, and source data verification.",
    }),
    ...buildCanvasSectionSopEntries("stage=conduct&section=monitoring-visits", {
      mappingPrefix: "map-can-mvr",
      persona: "cra_monitor",
      priority: 45,
      articleId: "art-can-mvr",
      versionId: "ver-can-119",
      title:
        "SOP-CAN-119: Clinical Monitoring Visits, Findings & Follow-Up Letters",
      slug: "sop-can-119-monitoring-visits",
      tags: ["canvas", "monitoring-visits", "mvr", "sdv", "cra"],
      categoryId: "cat-site-monitoring",
      categoryName: "Site Monitoring & CTMS",
      authorUserId: "cra_monitor",
      sectionAnchor: "#canvas-monitoring-visits",
      heading: "Monitoring Visit Execution & Trip Reports",
      summary:
        "Governs on-site and remote monitoring visit checklists, finding classification, CAPA linkage, and compiled follow-up letters.",
    }),
    ...buildCanvasSectionSopEntries("stage=conduct&section=supply", {
      mappingPrefix: "map-can-sup",
      persona: "site_crc",
      priority: 45,
      articleId: "art-can-sup",
      versionId: "ver-can-120",
      title:
        "SOP-CAN-120: Investigational Product Supply, Kit Accountability & Excursions",
      slug: "sop-can-120-investigational-supply",
      tags: ["canvas", "supply", "rtsm", "ip-kits", "excursion"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      authorUserId: "cra_monitor",
      sectionAnchor: "#canvas-investigational-supply",
      heading: "Investigational Product Inventory & Dispensing",
      summary:
        "Defines randomization kit receipt, temperature excursion quarantine, blinded dispensing verification, and IP reconciliation.",
    }),
    ...buildCanvasSectionSopEntries("stage=data&section=queries", {
      mappingPrefix: "map-can-qry",
      stageFallback: "data",
      persona: "data_manager",
      priority: 45,
      articleId: "art-can-qry",
      versionId: "ver-can-121",
      title:
        "SOP-CAN-121: Clinical Discrepancy Queries, Site Responses & Closure",
      slug: "sop-can-121-discrepancy-queries",
      tags: ["canvas", "queries", "discrepancies", "data-cleaning"],
      categoryId: "cat-data-management",
      categoryName: "Data Management & Coding",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#canvas-discrepancy-queries",
      heading: "Discrepancy Query Lifecycle Management",
      summary:
        "Standardizes manual and rule-generated query triage, site coordinator clarification with change reasons, and data manager closure.",
    }),
    ...buildCanvasSectionSopEntries("stage=data&section=locks", {
      ...DATA_LOCK_SPEC,
      mappingPrefix: "map-can-locks",
    }),
    ...buildCanvasSectionSopEntries("stage=data&section=validation", {
      mappingPrefix: "map-can-val",
      persona: "data_manager",
      priority: 45,
      articleId: "art-can-val",
      versionId: "ver-can-123",
      title:
        "SOP-CAN-123: Edit-Check Rule Packs, Dry-Run Preview & Validation Findings",
      slug: "sop-can-123-rule-validation",
      tags: ["canvas", "validation", "edit-checks", "rules"],
      categoryId: "cat-data-management",
      categoryName: "Data Management & Coding",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#canvas-rule-validation",
      heading: "Programmatic Edit Checks & Validation Findings",
      summary:
        "Covers deterministic edit-check rule pack activation, historical dry-run impact previews, and cross-form validation triage.",
    }),
    ...buildCanvasSectionSopEntries("stage=analyze", {
      mappingPrefix: "map-can-anl",
      persona: "safety_officer",
      priority: 40,
      articleId: "art-can-anl",
      versionId: "ver-can-125",
      title:
        "SOP-CAN-125: Clinical Analysis Readiness, Blinded Review & Lock Cutoffs",
      slug: "sop-can-125-analysis-readiness",
      tags: ["canvas", "analyze", "biostatistics", "blinded-review"],
      categoryId: "cat-data-management",
      categoryName: "Data Management & Coding",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#canvas-analysis-readiness",
      heading: "Analysis Stage Readiness & Blinded Data Review",
      summary:
        "Defines pre-analysis data review, protocol deviation classification, and interim cutoff readiness checks.",
    }),
    ...buildCanvasSectionSopEntries("stage=submit&section=exports", {
      ...REGULATED_EXPORTS_SPEC,
      mappingPrefix: "map-can-exports",
      stageFallback: "submit",
    }),
    "/data-lock": buildBundledSop({
      ...DATA_LOCK_SPEC,
      mappingId: "map-data-lock-01",
      routePattern: "/data-lock/*",
    }),
    "/exports": buildBundledSop({
      ...REGULATED_EXPORTS_SPEC,
      mappingId: "map-exports-01",
      routePattern: "/exports/*",
    }),
    "/ecrf": {
      matched_mapping: {
        id: "map-ecrf-01",
        route_pattern: "/ecrf/*",
        persona: "site_crc",
        article_id: "art-ecrf-sop",
        section_anchor: "#subject-enrollment-and-visit-entry",
        priority: 10,
        is_active: true,
      },
      primary_article: {
        id: "art-ecrf-sop",
        title: "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture",
        slug: "sop-ecrf-201-subject-enrollment",
        status: "PUBLISHED",
        version_index: 2,
        version_label: "2.1",
        tags: ["ecrf", "enrollment", "gxp-edc", "site-sop"],
        category_id: "cat-clinical-ops",
        category_name: "Clinical Operations",
        author_user_id: "lead_data_manager",
        last_edited_by: "lead_data_manager",
        approved_by: "qa_director",
        created_at: "2026-02-10T10:00:00Z",
        updated_at: "2026-05-18T14:30:00Z",
        body_markdown: `## Purpose & Scope
This Standard Operating Procedure (SOP) governs all investigator site personnel executing electronic Case Report Form (eCRF) data capture, subject registration, and visit assessment recording within the Cadence Clinical Research Platform.

## Mandatory GxP Controls
1. Confirm informed consent verification prior to recording baseline assessments.
2. Record contemporaneous source-verified measurements and document mandatory change justifications for every amendment.
3. Escalate protocol deviations or locked-form blockers via Contextual Help using pseudonymous subject identifiers only.`,
        body_html: `<h2 id="subject-enrollment-and-visit-entry">Purpose &amp; Scope</h2><p>This Standard Operating Procedure (SOP) governs investigator site eCRF data capture, subject registration, and visit assessment recording.</p><h2 id="ecrf-mandatory-gxp-controls">Mandatory GxP Controls</h2><ol><li>Confirm informed consent verification prior to recording baseline assessments.</li><li>Record contemporaneous source-verified measurements and document mandatory change justifications for every amendment.</li><li>Escalate protocol deviations or locked-form blockers via Contextual Help using pseudonymous subject identifiers only.</li></ol><h3 id="discrepancy-management-and-coding">Discrepancy Management &amp; Coding</h3><p>Triage open validation queries and reconcile coding terms prior to form lock.</p><h3 id="ecrf-form-capture-and-change-reason">eCRF Form Capture &amp; Change Reason</h3><p>Verify controlled terminology selections and non-PII change reasons when saving forms.</p>`,
      },
      primary_version: {
        id: "ver-ecrf-201",
        version_index: 2,
        version_label: "2.1",
        status_at_snapshot: "PUBLISHED",
        is_locked: true,
        created_by: "lead_data_manager",
        reason_for_change: "Annual review & protocol amendment 2 alignment",
        created_at: "2026-05-18T14:30:00Z",
      },
      section_anchor: "#subject-enrollment-and-visit-entry",
      related_articles: [],
    },
    "/observations/:observationRef": buildBundledSop({
      mappingId: "map-observation-01",
      routePattern: "/observations/:observationRef",
      persona: "site_investigator",
      priority: 30,
      articleId: "art-observations-sop",
      versionId: "ver-obs-210",
      title:
        "SOP-OBS-210: Clinical Observation Inspection, Query History & Provenance Review",
      slug: "sop-obs-210-clinical-observation-inspection",
      tags: ["observations", "provenance", "sdv", "queries", "safety"],
      categoryId: "cat-data-management",
      categoryName: "Data Management & Coding",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#clinical-observation-provenance-review",
      heading: "Clinical Observation Provenance & Discrepancy Review",
      summary:
        "Standardizes item-level observation inspection, audit provenance verification, medical safety review, and query resolution.",
      steps: [
        "Inspect source provenance, prior amendments, and open queries before updating or verifying an observation.",
        "Escalate clinically significant safety abnormalities or medical coding questions with pseudonymous subject codes only.",
      ],
    }),
    "/mdr": {
      matched_mapping: {
        id: "map-mdr-01",
        route_pattern: "/mdr/*",
        persona: "sponsor_designer",
        article_id: "art-mdr-sop",
        section_anchor: "#schedule-of-activities-matrix-authoring",
        priority: 10,
        is_active: true,
      },
      primary_article: {
        id: "art-mdr-sop",
        title:
          "SOP-MDR-104: Schedule of Activities Matrix & Protocol Authoring",
        slug: "sop-mdr-104-soa-matrix-authoring",
        status: "PUBLISHED",
        version_index: 3,
        version_label: "3.0",
        tags: ["mdr", "usdm", "soa-matrix", "study-design"],
        category_id: "cat-study-authoring",
        category_name: "Study Authoring & MDR",
        author_user_id: "sponsor_study_lead",
        last_edited_by: "sponsor_study_lead",
        approved_by: "qa_director",
        created_at: "2026-01-20T09:00:00Z",
        updated_at: "2026-06-01T11:15:00Z",
        body_markdown: `## Protocol Metadata Management & SoA Design\n1. Bind clinical activities to CDISC Biomedical Concepts.\n2. Document protocol amendment rationale prior to publication.`,
        body_html: `<h2 id="schedule-of-activities-matrix-authoring">Protocol Metadata Management &amp; SoA Design</h2><ol><li>Bind clinical activities to CDISC Biomedical Concepts.</li><li>Document protocol amendment rationale prior to publication.</li></ol><h3 id="amendment-proposal-review">Amendment Proposal Review</h3><p>Review structural Schedule of Activities diffs, version lineage, and governance sign-off prerequisites before approving protocol proposals.</p>`,
      },
      primary_version: {
        id: "ver-mdr-104",
        version_index: 3,
        version_label: "3.0",
        status_at_snapshot: "PUBLISHED",
        is_locked: true,
        created_by: "sponsor_study_lead",
        reason_for_change: "CDISC USDM v3.0 standard alignment",
        created_at: "2026-06-01T11:15:00Z",
      },
      section_anchor: "#schedule-of-activities-matrix-authoring",
      related_articles: [],
    },
    "/digitization": buildBundledSop({
      mappingId: "map-digitization-01",
      routePattern: "/digitization/*",
      persona: "sponsor_designer",
      priority: 15,
      articleId: "art-digitization-sop",
      versionId: "ver-mdr-106",
      title:
        "SOP-MDR-106: AI-Assisted Protocol Digitization & USDM Human Review",
      slug: "sop-mdr-106-protocol-digitization",
      tags: ["digitization", "usdm", "ai-review", "protocol"],
      categoryId: "cat-study-authoring",
      categoryName: "Study Authoring & MDR",
      authorUserId: "sponsor_study_lead",
      sectionAnchor: "#protocol-digitization-review",
      heading: "Protocol Digitization & Human-in-the-Loop Verification",
      summary:
        "Requires human verification of extracted endpoints, eligibility criteria, and Schedule of Activities cells prior to USDM draft promotion.",
    }),
    "/icf-builder": buildBundledSop({
      mappingId: "map-icf-builder-01",
      routePattern: "/icf-builder/*",
      persona: "sponsor_designer",
      priority: 15,
      articleId: "art-icf-builder-sop",
      versionId: "ver-con-210",
      title:
        "SOP-CON-210: Informed Consent Form Authoring & IRB Version Control",
      slug: "sop-con-210-icf-builder",
      tags: ["econsent", "icf", "irb", "ethics"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#icf-builder-governance",
      heading: "Informed Consent Authoring & Ethics Versioning",
      summary:
        "Standardizes master ICF authoring, mandatory ICH E6(R3) consent elements, and comprehension check validation.",
    }),
    "/econsent-authoring": buildBundledSop({
      mappingId: "map-econsent-01",
      routePattern: "/econsent-authoring/*",
      persona: "sponsor_designer",
      priority: 15,
      articleId: "art-econsent-sop",
      versionId: "ver-con-211",
      title:
        "SOP-CON-211: Multi-Language eConsent Templates & Site Localization",
      slug: "sop-con-211-econsent-localization",
      tags: ["econsent", "localization", "translation", "irb"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#econsent-localization",
      heading: "eConsent Template Localization & Site IRB Release",
      summary:
        "Controls certified translation verification, country/site consent localization, and re-consent triggers.",
    }),
    "/amendment-diff": buildBundledSop({
      mappingId: "map-amendment-diff-01",
      routePattern: "/amendment-diff/*",
      persona: "sponsor_designer",
      priority: 15,
      articleId: "art-amendment-sop",
      versionId: "ver-mdr-108",
      title:
        "SOP-MDR-108: Protocol Amendment Impact Assessment & Casebook Migration",
      slug: "sop-mdr-108-amendment-diff",
      tags: ["amendment", "migration", "usdm", "casebook"],
      categoryId: "cat-study-authoring",
      categoryName: "Study Authoring & MDR",
      authorUserId: "sponsor_study_lead",
      sectionAnchor: "#amendment-impact-assessment",
      heading: "Protocol Amendment Comparison & Migration",
      summary:
        "Governs structural diffs across protocol versions, active subject impact analysis, and site amendment cutover.",
    }),
    "/imaging": buildBundledSop({
      mappingId: "map-imaging-01",
      routePattern: "/imaging/*",
      persona: "site_investigator",
      priority: 15,
      articleId: "art-imaging-sop",
      versionId: "ver-img-310",
      title: "SOP-IMG-310: De-Identified DICOM Review & RECIST 1.1 Assessment",
      slug: "sop-img-310-imaging-recist",
      tags: ["imaging", "dicom", "recist", "deidentification"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      sectionAnchor: "#imaging-recist-review",
      heading: "Medical Imaging De-Identification & Lesion Tracking",
      summary:
        "Enforces DICOM header pixel/tag de-identification, target lesion measurement consistency, and blinded central read workflows.",
    }),
    "/devices": buildBundledSop({
      mappingId: "map-devices-01",
      routePattern: "/devices/*",
      persona: "site_crc",
      priority: 15,
      articleId: "art-devices-sop",
      versionId: "ver-dev-312",
      title:
        "SOP-DEV-312: Investigational Medical Device Exposure & Accountability",
      slug: "sop-dev-312-device-accountability",
      tags: ["devices", "iso14155", "udi", "firmware"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      sectionAnchor: "#device-exposure-accountability",
      heading: "ISO 14155 Device Exposure & UDI Accountability",
      summary:
        "Tracks investigational device serial/UDI assignment, firmware lineage, calibration status, and return accountability.",
    }),
    "/device-investigation": buildBundledSop({
      mappingId: "map-device-inv-01",
      routePattern: "/device-investigation/*",
      persona: "safety_officer",
      priority: 15,
      articleId: "art-device-inv-sop",
      versionId: "ver-dev-313",
      title:
        "SOP-DEV-313: Medical Device Deficiency & Vigilance Incident Traceability",
      slug: "sop-dev-313-device-investigation",
      tags: ["devices", "vigilance", "usade", "incident"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#device-incident-investigation",
      heading: "Device Deficiency & Adverse Device Effect Investigation",
      summary:
        "Links device hardware/firmware revisions to clinical observations and governs USADE regulatory reporting.",
    }),
    "/adjudication": buildBundledSop({
      mappingId: "map-adjudication-01",
      routePattern: "/adjudication/*",
      persona: "site_investigator",
      priority: 15,
      articleId: "art-cec-sop",
      versionId: "ver-cec-315",
      title:
        "SOP-CEC-315: Blinded Clinical Events Committee Dossier & Adjudication",
      slug: "sop-cec-315-endpoint-adjudication",
      tags: ["cec", "adjudication", "endpoints", "blinding"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      sectionAnchor: "#cec-endpoint-adjudication",
      heading: "Clinical Events Committee Dossier & Consensus Review",
      summary:
        "Standardizes treatment-blinded clinical dossier redaction, independent reviewer voting, and consensus adjudication sign-off.",
    }),
    "/cec": buildBundledSop({
      mappingId: "map-cec-01",
      routePattern: "/cec/*",
      persona: "site_investigator",
      priority: 15,
      articleId: "art-cec-sop",
      versionId: "ver-cec-315",
      title:
        "SOP-CEC-315: Blinded Clinical Events Committee Dossier & Adjudication",
      slug: "sop-cec-315-endpoint-adjudication",
      tags: ["cec", "adjudication", "endpoints", "blinding"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      sectionAnchor: "#cec-endpoint-adjudication",
      heading: "Clinical Events Committee Dossier & Consensus Review",
      summary:
        "Standardizes treatment-blinded clinical dossier redaction, independent reviewer voting, and consensus adjudication sign-off.",
    }),
    "/ctms": {
      matched_mapping: {
        id: "map-ctms-01",
        route_pattern: "/ctms/*",
        persona: "cra_monitor",
        article_id: "art-ctms-sop",
        section_anchor: "#site-monitoring-and-sdv",
        priority: 10,
        is_active: true,
      },
      primary_article: {
        id: "art-ctms-sop",
        title:
          "SOP-CTMS-302: Site Monitoring Visits & Source Data Verification",
        slug: "sop-ctms-302-site-monitoring",
        status: "PUBLISHED",
        version_index: 1,
        version_label: "1.0",
        tags: ["ctms", "monitoring", "sdv", "cra"],
        category_id: "cat-site-monitoring",
        category_name: "Site Monitoring & CTMS",
        author_user_id: "cra_monitor",
        last_edited_by: "cra_monitor",
        approved_by: "qa_director",
        created_at: "2026-01-20T09:00:00Z",
        updated_at: "2026-06-01T11:15:00Z",
        body_markdown: `## Site Monitoring & SDV Procedures\n1. Reconcile site enrollment metrics and pending monitoring action items prior to on-site or remote visits.\n2. Document monitoring findings with ICH E6(R3) severity classification and link follow-up CAPA or support tickets.`,
        body_html: `<h2 id="site-monitoring-and-sdv">Site Monitoring &amp; SDV Procedures</h2><ol><li>Reconcile site enrollment metrics and pending monitoring action items prior to on-site or remote visits.</li><li>Document monitoring findings with ICH E6(R3) severity classification and link follow-up CAPA or support tickets.</li></ol><h3 id="ctms-portfolio-oversight">CTMS Portfolio Oversight</h3><p>Sponsor operations track site milestone attainment and monitoring cadence compliance.</p><h3 id="site-visit-readiness">Site Visit Readiness &amp; Delegation</h3><p>Site coordinators maintain current delegation logs and source chart availability prior to CRA visits.</p>`,
      },
      primary_version: {
        id: "ver-ctms-302",
        version_index: 1,
        version_label: "1.0",
        status_at_snapshot: "PUBLISHED",
        is_locked: true,
        created_by: "cra_monitor",
        reason_for_change: "Initial release",
        created_at: "2026-06-01T11:15:00Z",
      },
      section_anchor: "#site-monitoring-and-sdv",
      related_articles: [],
    },
    "/ssu": {
      matched_mapping: {
        id: "map-ssu-01",
        route_pattern: "/ssu/*",
        persona: "cra_monitor",
        article_id: "art-ssu-sop",
        section_anchor: "#site-greenlight-activation-matrix",
        priority: 15,
        is_active: true,
      },
      primary_article: {
        id: "art-ssu-sop",
        title: "SOP-SSU-205: Site Startup Readiness & Regulatory Greenlight",
        slug: "sop-ssu-205-site-startup-greenlight",
        status: "PUBLISHED",
        version_index: 1,
        version_label: "1.1",
        tags: ["ssu", "site-startup", "greenlight", "activation"],
        category_id: "cat-site-monitoring",
        category_name: "Site Monitoring & CTMS",
        author_user_id: "cra_monitor",
        last_edited_by: "cra_monitor",
        approved_by: "qa_director",
        created_at: "2026-02-12T09:00:00Z",
        updated_at: "2026-06-12T11:15:00Z",
        body_markdown: `## Site Startup & Greenlight Certification\n1. Confirm Essential Document List (EDL) artifacts and Delegation of Authority sign-offs are complete.\n2. Verify country regulatory approval and IP pharmacy readiness before issuing greenlight certification.`,
        body_html: `<h2 id="site-greenlight-activation-matrix">Site Startup &amp; Greenlight Certification</h2><p id="site-startup-and-greenlight">Defines IRB/IEC approval verification, DoA log completion, and investigational product release prerequisites.</p><ol><li>Confirm Essential Document List (EDL) artifacts and Delegation of Authority sign-offs are complete.</li><li>Verify country regulatory approval and IP pharmacy readiness before issuing greenlight certification.</li></ol><h3 id="global-country-activation-gates">Global Country Activation Gates</h3><p>Sponsor operations verify competent authority approvals and site roster readiness across participating countries.</p><h3 id="site-initiation-visit-greenlight">Site Initiation Visit &amp; Greenlight</h3><p>Monitors confirm Site Initiation Visit completion and facility readiness prior to greenlight release.</p>`,
      },
      primary_version: {
        id: "ver-ssu-205",
        version_index: 1,
        version_label: "1.1",
        status_at_snapshot: "PUBLISHED",
        is_locked: true,
        created_by: "cra_monitor",
        reason_for_change: "Added country greenlight matrix checks",
        created_at: "2026-06-12T11:15:00Z",
      },
      section_anchor: "#site-greenlight-activation-matrix",
      related_articles: [],
    },
    "/audit": {
      matched_mapping: {
        id: "map-audit-01",
        route_pattern: "/audit/*",
        persona: "auditor",
        article_id: "art-audit-sop",
        section_anchor: "#gxp-audit-trail-inspection",
        priority: 10,
        is_active: true,
      },
      primary_article: {
        id: "art-audit-sop",
        title:
          "SOP-AUD-401: 21 CFR Part 11 Audit Trail & Qualification Inspection",
        slug: "sop-aud-401-audit-trail-inspection",
        status: "PUBLISHED",
        version_index: 1,
        version_label: "1.0",
        tags: ["audit", "part11", "gxp", "compliance"],
        category_id: "cat-regulatory-compliance",
        category_name: "Regulatory & Quality Compliance",
        author_user_id: "qa_director",
        last_edited_by: "qa_director",
        approved_by: "qa_director",
        created_at: "2026-01-20T09:00:00Z",
        updated_at: "2026-06-01T11:15:00Z",
        body_markdown: `## Audit Trail Inspection Guidelines\n1. Filter immutable audit records by study, actor, entity, and UTC date window.\n2. Verify predecessor and block SHA-256 hashes prior to exporting signed regulatory inspection bundles.`,
        body_html: `<h2 id="gxp-audit-trail-inspection">Audit Trail Inspection Guidelines</h2><ol><li>Filter immutable audit records by study, actor, entity, and UTC date window.</li><li>Verify predecessor and block SHA-256 hashes prior to exporting signed regulatory inspection bundles.</li></ol>`,
      },
      primary_version: {
        id: "ver-audit-401",
        version_index: 1,
        version_label: "1.0",
        status_at_snapshot: "PUBLISHED",
        is_locked: true,
        created_by: "qa_director",
        reason_for_change: "Initial release",
        created_at: "2026-06-01T11:15:00Z",
      },
      section_anchor: "#gxp-audit-trail-inspection",
      related_articles: [],
    },
    "/auditor": buildBundledSop({
      mappingId: "map-auditor-01",
      routePattern: "/auditor/*",
      persona: "auditor",
      priority: 15,
      articleId: "art-auditor-sop",
      versionId: "ver-aud-402",
      title:
        "SOP-AUD-402: Regulatory Inspector Read-Only Portal & Chain Verification",
      slug: "sop-aud-402-auditor-portal",
      tags: ["auditor", "inspection", "merkle-chain", "part11"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#auditor-inspection-portal",
      heading: "Regulatory Inspection Portal Procedures",
      summary:
        "Guides independent inspectors through read-only SHA-256 Merkle chain verification, electronic signature inspection, and certified exports.",
    }),
    "/rules": buildBundledSop({
      mappingId: "map-rules-01",
      routePattern: "/rules/*",
      persona: "data_manager",
      priority: 15,
      articleId: "art-rules-sop",
      versionId: "ver-dm-220",
      title:
        "SOP-DM-220: Edit Check Authoring, Rule Packs & Discrepancy Triage",
      slug: "sop-dm-220-edit-checks-and-queries",
      tags: ["rules", "edit-checks", "queries", "data-management"],
      categoryId: "cat-data-management",
      categoryName: "Data Management & Coding",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#edit-check-and-query-triage",
      heading: "Edit Check Specification & Query Triage",
      summary:
        "Defines programmatic validation rule authoring, UAT qualification runs, and discrepancy query resolution SLAs.",
    }),
    "/coding": buildBundledSop({
      mappingId: "map-coding-01",
      routePattern: "/coding/*",
      persona: "data_manager",
      priority: 15,
      articleId: "art-medical-coding-sop",
      versionId: "ver-dm-222",
      title:
        "SOP-DM-222: Clinical Dictionary & Adverse Event Coding Governance",
      slug: "sop-dm-222-medical-coding",
      tags: ["coding", "dictionary", "terminology", "auto-coding"],
      categoryId: "cat-data-management",
      categoryName: "Data Management & Coding",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#medical-dictionary-coding",
      heading: "Medical Dictionary Coding & Version Upversioning",
      summary:
        "Governs verbatim adverse event and concomitant medication coding, dictionary version pinning, and medical coder second-review.",
    }),
    "/lakehouse": buildBundledSop({
      mappingId: "map-lakehouse-01",
      routePattern: "/lakehouse/*",
      persona: "data_manager",
      priority: 15,
      articleId: "art-lakehouse-sop",
      versionId: "ver-anl-330",
      title:
        "SOP-ANL-330: Clinical Lakehouse Projections & Lineage Verification",
      slug: "sop-anl-330-clinical-lakehouse",
      tags: ["lakehouse", "analytics", "lineage", "cdisc"],
      categoryId: "cat-data-management",
      categoryName: "Data Management & Coding",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#clinical-lakehouse-lineage",
      heading: "Clinical Lakehouse Dataset Lineage",
      summary:
        "Ensures end-to-end provenance from raw eCRF observations to analysis-ready dataset projections without treatment unblinding.",
    }),
    "/sensors": buildBundledSop({
      mappingId: "map-sensors-01",
      routePattern: "/sensors/*",
      persona: "site_crc",
      priority: 15,
      articleId: "art-sensors-sop",
      versionId: "ver-iot-332",
      title:
        "SOP-IOT-332: Connected Sensor Telemetry & Wearable Data Integrity",
      slug: "sop-iot-332-sensor-telemetry",
      tags: ["sensors", "wearables", "dht", "telemetry"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      sectionAnchor: "#sensor-telemetry-governance",
      heading: "Digital Health Technology & Sensor Ingestion",
      summary:
        "Controls wearable device pairing, epoch clock synchronization, missing-stream alerts, and physiological threshold review.",
    }),
    "/labs": buildBundledSop({
      mappingId: "map-labs-01",
      routePattern: "/labs/*",
      persona: "data_manager",
      priority: 15,
      articleId: "art-labs-sop",
      versionId: "ver-lab-334",
      title:
        "SOP-LAB-334: Central & Local Laboratory Reference Ranges & Reconciliation",
      slug: "sop-lab-334-laboratory-governance",
      tags: ["labs", "loinc", "reference-ranges", "ctcae"],
      categoryId: "cat-data-management",
      categoryName: "Data Management & Coding",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#laboratory-reconciliation",
      heading: "Laboratory Normal Ranges & Sample Reconciliation",
      summary:
        "Standardizes age/sex-stratified normal range maintenance, LOINC unit normalization, and Investigator CS/NCS grading.",
    }),
    "/dct": buildBundledSop({
      mappingId: "map-dct-01",
      routePattern: "/dct/*",
      persona: "site_crc",
      priority: 15,
      articleId: "art-dct-sop",
      versionId: "ver-dct-336",
      title:
        "SOP-DCT-336: Decentralized Telehealth Visits & Direct-to-Participant IP",
      slug: "sop-dct-336-decentralized-trials",
      tags: ["dct", "telehealth", "dtp", "home-health"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      sectionAnchor: "#decentralized-trial-execution",
      heading: "Telehealth Visit Verification & Direct-to-Participant Chain",
      summary:
        "Governs participant identity verification during remote visits, home-health nursing source capture, and DTP cold-chain custody.",
    }),
    "/safety": buildBundledSop({
      mappingId: "map-safety-01",
      routePattern: "/safety/*",
      persona: "safety_officer",
      priority: 15,
      articleId: "art-safety-sop",
      versionId: "ver-saf-340",
      title:
        "SOP-SAF-340: Aggregate Pharmacovigilance, SAE Reconciliation & QPPV Review",
      slug: "sop-saf-340-aggregate-safety",
      tags: ["safety", "pharmacovigilance", "sae", "susar", "e2b"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#aggregate-safety-oversight",
      heading: "Pharmacovigilance Signal Detection & Expedited Reporting",
      summary:
        "Defines 24-hour SAE triage, clinical/safety database reconciliation, SUSAR expectedness evaluation, and ICH E2B(R3) reporting.",
    }),
    "/etmf": buildBundledSop({
      mappingId: "map-etmf-01",
      routePattern: "/etmf/*",
      persona: "auditor",
      priority: 25,
      articleId: "art-etmf-sop",
      versionId: "ver-tmf-301",
      title:
        "SOP-TMF-301: Electronic Trial Master File (eTMF) Artifact Filing & QC",
      slug: "sop-tmf-301-etmf-artifact-filing",
      tags: ["etmf", "dia-reference-model", "essential-documents", "qc"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#etmf-artifact-filing-and-qc",
      heading: "eTMF Contemporaneous Filing & Inspection Readiness",
      summary:
        "Governs DIA Reference Model classification, artifact completeness review, and regulatory inspection readiness across /etmf.",
    }),
    "/quality": buildBundledSop({
      mappingId: "map-quality-01",
      routePattern: "/quality/*",
      persona: "auditor",
      priority: 15,
      articleId: "art-quality-sop",
      versionId: "ver-qms-410",
      title:
        "SOP-QMS-410: Quality Management Cockpit, KRI Thresholds & CAPA Oversight",
      slug: "sop-qms-410-quality-cockpit",
      tags: ["quality", "qms", "rbqm", "capa", "kri"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#qms-risk-and-capa",
      heading: "ICH E6(R3) Quality Tolerance Limits & CAPA",
      summary:
        "Monitors Critical-to-Quality Key Risk Indicators, Quality Tolerance Limit breaches, root-cause investigations, and CAPA effectiveness.",
    }),
    "/notifications": buildBundledSop({
      mappingId: "map-notifications-01",
      routePattern: "/notifications/*",
      persona: "site_crc",
      priority: 15,
      articleId: "art-notifications-sop",
      versionId: "ver-ops-412",
      title: "SOP-OPS-412: Operational Alert Triage & SLA Escalation Routing",
      slug: "sop-ops-412-notifications-triage",
      tags: ["notifications", "alerts", "sla", "triage"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      sectionAnchor: "#clinical-notification-triage",
      heading: "Operational & Safety Alert Acknowledgement",
      summary:
        "Standardizes timely review of safety, query, lock, and SLA warning notifications across clinical study teams.",
    }),
    "/tickets": buildBundledSop({
      mappingId: "map-tickets-01",
      routePattern: "/tickets/*",
      persona: "site_crc",
      priority: 25,
      articleId: "art-tickets-sop",
      versionId: "ver-tck-501",
      title:
        "SOP-TCK-501: GxP Support Ticket Triage, Root-Cause CAPA & Part 11 Closure",
      slug: "sop-tck-501-gxp-support-ticket-lifecycle",
      tags: ["tickets", "support", "capa", "sla", "part11"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#gxp-support-ticket-lifecycle",
      heading: "GxP Support Ticket Triage & Closure Governance",
      summary:
        "Defines severity classification, SLA pause/resume tracking, participant de-identification rules, root-cause documentation, and 21 CFR Part 11 electronic signatures.",
    }),
    "/documents": buildBundledSop({
      mappingId: "map-documents-01",
      routePattern: "/documents/*",
      persona: "site_crc",
      priority: 15,
      articleId: "art-documents-sop",
      versionId: "ver-tmf-305",
      title:
        "SOP-TMF-305: Controlled Study Document Hub & Essential Record Sync",
      slug: "sop-tmf-305-documents-hub",
      tags: ["documents", "eisf", "controlled-docs", "versioning"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#study-documents-hub",
      heading: "Controlled Study Document Distribution & Receipt",
      summary:
        "Controls investigator brochure, lab manual, and pharmacy manual distribution with site acknowledgement tracking.",
    }),
    "/knowledge": buildBundledSop({
      mappingId: "map-knowledge-01",
      routePattern: "/knowledge/*",
      persona: "auditor",
      priority: 25,
      articleId: "art-knowledge-sop",
      versionId: "ver-knb-502",
      title:
        "SOP-KNB-502: Controlled SOP Knowledge Hub & Regulatory Version Governance",
      slug: "sop-knb-502-controlled-knowledge-hub",
      tags: ["knowledge", "sop", "versioning", "part11"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#controlled-sop-repository-governance",
      heading: "Controlled SOP Repository Usage",
      summary:
        "Governs role-scoped SOP lookup, locked snapshot inspection, and 21 CFR Part 11 Merkle audit verification on /knowledge.",
    }),
    "/training": buildBundledSop({
      mappingId: "map-training-01",
      routePattern: "/training/*",
      persona: "site_crc",
      priority: 25,
      articleId: "art-training-sop",
      versionId: "ver-trn-202",
      title:
        "SOP-TRN-202: GxP Training Curriculum Completion & Delegation Sign-Off",
      slug: "sop-trn-202-gxp-training-attestation",
      tags: ["training", "delegation", "ich-e6", "qualification"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#gxp-training-and-delegation",
      heading: "GxP Role Qualification & Training Attestation",
      summary:
        "Defines mandatory SOP read-and-understand training completion and Principal Investigator Delegation of Authority prerequisites on /training.",
    }),
    "/participant-wallet": buildBundledSop({
      mappingId: "map-wallet-01",
      routePattern: "/participant-wallet/*",
      persona: "site_crc",
      priority: 15,
      articleId: "art-wallet-sop",
      versionId: "ver-eng-350",
      title:
        "SOP-ENG-350: Participant Stipend & Visit Reimbursement Governance",
      slug: "sop-eng-350-participant-wallet",
      tags: ["participant-wallet", "stipends", "reimbursement", "irb"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      sectionAnchor: "#participant-reimbursement-governance",
      heading: "IRB-Approved Stipend & Expense Disbursement",
      summary:
        "Ensures pseudonymous visit stipend eligibility, receipt verification without exposing banking PII to sponsors, and IRB cap compliance.",
    }),
    "/lay-summaries": buildBundledSop({
      mappingId: "map-lay-summaries-01",
      routePattern: "/lay-summaries/*",
      persona: "sponsor_designer",
      priority: 15,
      articleId: "art-lay-summaries-sop",
      versionId: "ver-eng-352",
      title:
        "SOP-ENG-352: Plain-Language Trial Results & Lay Summary Publication",
      slug: "sop-eng-352-lay-summaries",
      tags: ["lay-summaries", "eu-ctr", "plain-language", "transparency"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      sectionAnchor: "#lay-summary-publication",
      heading: "EU CTR Article 37 Plain-Language Summary Governance",
      summary:
        "Controls health-literacy readability checks, non-promotional medical review, and multi-language lay summary release.",
    }),
  };
}

function attachCanvasZoomSops(
  sops: Record<string, Record<string, unknown>>
): Record<string, Record<string, unknown>> {
  if (Object.keys(sops).length === 0) {
    return sops;
  }
  const buildZoomEntry = (
    baseKey: string,
    mappingId: string,
    routePattern: string,
    persona: string,
    sectionAnchor: string,
    priority: number
  ): Record<string, unknown> => {
    const base = sops[baseKey];
    const primaryArticle = base.primary_article as Record<string, unknown>;
    return {
      matched_mapping: {
        id: mappingId,
        route_pattern: routePattern,
        persona,
        article_id: primaryArticle.id,
        section_anchor: sectionAnchor,
        priority,
        is_active: true,
      },
      primary_article: primaryArticle,
      primary_version: base.primary_version,
      section_anchor: sectionAnchor,
      related_articles: [],
    };
  };

  sops["/studies/:studyCode?participant=*"] = buildZoomEntry(
    "/studies/:studyCode",
    "m-stc-q-participant",
    "/studies/:studyCode?participant=*",
    "site_crc",
    "#participant-timeline-and-visit-record",
    48
  );
  sops["/studies/:studyCode?form=*"] = buildZoomEntry(
    "/ecrf",
    "m-stc-q-form",
    "/studies/:studyCode?form=*",
    "site_crc",
    "#ecrf-form-capture-and-change-reason",
    49
  );
  sops["/studies/:studyCode?observation=*"] = buildZoomEntry(
    "/observations/:observationRef",
    "m-stc-q-observation",
    "/studies/:studyCode?observation=*",
    "site_investigator",
    "#clinical-observation-provenance-review",
    50
  );
  sops["/studies/:studyCode?proposal=*"] = buildZoomEntry(
    "/mdr",
    "m-stc-q-proposal",
    "/studies/:studyCode?proposal=*",
    "sponsor_designer",
    "#amendment-proposal-review",
    50
  );

  return sops;
}

function withPortalReferenceLinks(
  sops: Record<string, Record<string, unknown>>
): Record<string, Record<string, unknown>> {
  const patchedArticles = new WeakSet<object>();
  for (const entry of Object.values(sops)) {
    const article = entry.primary_article as
      Record<string, unknown> | undefined;
    if (!article || patchedArticles.has(article)) continue;
    patchedArticles.add(article);
    const markdown =
      typeof article.body_markdown === "string" ? article.body_markdown : "";
    if (!markdown.includes("/cadence-clinical/")) {
      article.body_markdown = `${markdown}${DOCS_PORTAL_MARKDOWN_FOOTER}`;
    }
    const html = typeof article.body_html === "string" ? article.body_html : "";
    if (!html.includes("/cadence-clinical/")) {
      article.body_html = `${html}${DOCS_PORTAL_HTML_FOOTER}`;
    }
  }
  return sops;
}

/**
 * Bundled offline help SOPs. Served only by a demo build when the API is unreachable (#4523).
 */
export const DEFAULT_CONTEXTUAL_SOPS: Record<
  string,
  Record<string, unknown>
> = /*#__PURE__*/ withPortalReferenceLinks(
  attachCanvasZoomSops(createDefaultContextualSops())
);

function collectUniqueBundledArticles(
  sops: Record<string, Record<string, unknown>>
): Array<Record<string, unknown>> {
  const seen = new Set<string>();
  const articles: Array<Record<string, unknown>> = [];
  for (const entry of Object.values(sops)) {
    const article = entry.primary_article as
      Record<string, unknown> | undefined;
    const id = typeof article?.id === "string" ? article.id : "";
    if (article && id && !seen.has(id)) {
      seen.add(id);
      articles.push(article);
    }
  }
  return articles;
}

export const DEFAULT_MOCK_ARTICLES = /*#__PURE__*/ collectUniqueBundledArticles(
  DEFAULT_CONTEXTUAL_SOPS
);

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value));
}

interface ParsedCanvasRoute {
  path: string;
  stage: string | null;
  section: string | null;
  participant: string | null;
  form: string | null;
  observation: string | null;
  proposal: string | null;
}

function parseRouteAndCanvasQuery(rawRoute?: string): ParsedCanvasRoute {
  const withoutHash = (rawRoute || "/").trim().split("#")[0] || "/";
  const qIndex = withoutHash.indexOf("?");
  const rawPath = qIndex >= 0 ? withoutHash.slice(0, qIndex) : withoutHash;
  const rawQuery = qIndex >= 0 ? withoutHash.slice(qIndex + 1) : "";

  const prefixed = rawPath.startsWith("/") ? rawPath : `/${rawPath}`;
  const path = prefixed.replace(/\/+$/, "") || "/";

  let stage: string | null = null;
  let section: string | null = null;
  let participant: string | null = null;
  let form: string | null = null;
  let observation: string | null = null;
  let proposal: string | null = null;
  if (rawQuery) {
    const params = new URLSearchParams(rawQuery);
    for (const [key, val] of params.entries()) {
      const lowerKey = key.trim().toLowerCase();
      const lowerVal = val.trim().toLowerCase();
      if (!lowerVal) continue;
      if (lowerKey === "stage" && !stage) stage = lowerVal;
      else if (lowerKey === "section" && !section) section = lowerVal;
      else if (lowerKey === "participant" && !participant) {
        participant = lowerVal;
      } else if (lowerKey === "form" && !form) form = lowerVal;
      else if (lowerKey === "observation" && !observation) {
        observation = lowerVal;
      } else if (lowerKey === "proposal" && !proposal) proposal = lowerVal;
    }
  }

  if (path === "/studies" || path.startsWith("/studies/")) {
    const knownSection = section ? findSection(section) : undefined;
    if (knownSection) {
      section = knownSection.section.id;
      stage = knownSection.stage.id;
    } else if (stage) {
      stage = findStage(stage)?.id ?? stage;
    }
  }

  return { path, stage, section, participant, form, observation, proposal };
}

function matchesBundledQueryParam(
  routeValue: string | null,
  patternValue: string
): boolean {
  if (!routeValue) return false;
  if (
    patternValue === "*" ||
    patternValue.startsWith(":") ||
    (patternValue.startsWith("{") && patternValue.endsWith("}"))
  ) {
    return true;
  }
  return routeValue === patternValue;
}

function scoreBundledPattern(
  parsedRoute: ParsedCanvasRoute,
  rawPattern: string
): number {
  const {
    path: normalizedPath,
    stage: routeStage,
    section: routeSection,
  } = parsedRoute;
  const trimmed = (rawPattern || "").trim().split("#")[0] || "";
  if (!trimmed) return 0;
  if (trimmed === "*" || trimmed === "/*") return 100;

  const qIdx = trimmed.indexOf("?");
  const rawPatternPath = qIdx >= 0 ? trimmed.slice(0, qIdx) : trimmed;
  const rawPatternQuery = qIdx >= 0 ? trimmed.slice(qIdx + 1) : "";

  let patternStage: string | null = null;
  let patternSection: string | null = null;
  let patternParticipant: string | null = null;
  let patternForm: string | null = null;
  let patternObservation: string | null = null;
  let patternProposal: string | null = null;
  if (rawPatternQuery) {
    const params = new URLSearchParams(rawPatternQuery);
    for (const [k, v] of params.entries()) {
      const lk = k.trim().toLowerCase();
      const lv = v.trim().toLowerCase();
      if (!lv) return 0;
      if (lk === "stage") patternStage = lv;
      else if (lk === "section") patternSection = lv;
      else if (lk === "participant") patternParticipant = lv;
      else if (lk === "form") patternForm = lv;
      else if (lk === "observation") patternObservation = lv;
      else if (lk === "proposal") patternProposal = lv;
      else return 0;
    }
  }

  let queryBonus = 0;
  if (patternStage && patternSection) {
    if (
      !matchesBundledQueryParam(routeStage, patternStage) ||
      !matchesBundledQueryParam(routeSection, patternSection)
    ) {
      return 0;
    }
    queryBonus += 600;
  } else if (patternSection) {
    if (!matchesBundledQueryParam(routeSection, patternSection)) return 0;
    queryBonus += 400;
  } else if (patternStage) {
    if (!matchesBundledQueryParam(routeStage, patternStage)) return 0;
    queryBonus += 200;
  }

  if (patternProposal) {
    if (!matchesBundledQueryParam(parsedRoute.proposal, patternProposal)) {
      return 0;
    }
    queryBonus += 850;
  }
  if (patternObservation) {
    if (
      !matchesBundledQueryParam(parsedRoute.observation, patternObservation)
    ) {
      return 0;
    }
    queryBonus += 800;
  }
  if (patternForm) {
    if (!matchesBundledQueryParam(parsedRoute.form, patternForm)) return 0;
    queryBonus += 750;
  }
  if (patternParticipant) {
    if (
      !matchesBundledQueryParam(parsedRoute.participant, patternParticipant)
    ) {
      return 0;
    }
    queryBonus += 700;
  }

  const normPattern =
    (rawPatternPath.startsWith("/")
      ? rawPatternPath
      : `/${rawPatternPath}`
    ).replace(/\/+$/, "") || "/";

  if (normPattern.endsWith("/*")) {
    const prefix = normPattern.slice(0, -2) || "/";
    if (
      prefix === "/" ||
      normalizedPath.toLowerCase() === prefix.toLowerCase() ||
      normalizedPath.toLowerCase().startsWith(`${prefix.toLowerCase()}/`)
    ) {
      const depth = prefix.split("/").filter(Boolean).length;
      return 2000 + depth * 20 + queryBonus;
    }
    return 0;
  }

  if (
    !normPattern.includes(":") &&
    !normPattern.includes("{") &&
    normalizedPath.toLowerCase() === normPattern.toLowerCase()
  ) {
    const depth = normPattern.split("/").filter(Boolean).length;
    return 4000 + depth * 10 + queryBonus;
  }

  const routeSegs = normalizedPath.split("/").filter(Boolean);
  const patternSegs = normPattern.split("/").filter(Boolean);
  if (routeSegs.length !== patternSegs.length) return 0;

  let literalCount = 0;
  let paramCount = 0;
  for (let i = 0; i < patternSegs.length; i++) {
    const pSeg = patternSegs[i];
    const rSeg = routeSegs[i];
    const isParam =
      pSeg.startsWith(":") || (pSeg.startsWith("{") && pSeg.endsWith("}"));
    if (isParam) {
      if (!rSeg) return 0;
      paramCount++;
    } else if (pSeg.toLowerCase() === rSeg.toLowerCase()) {
      literalCount++;
    } else {
      return 0;
    }
  }

  return 3000 + literalCount * 20 + paramCount * 5 + queryBonus;
}

/** Bundled, read-only offline help articles filtered like the list endpoint (demo builds only). */
function getFallbackArticles(
  params: { status?: string; categoryId?: string; q?: string } = {}
) {
  let filtered = [...DEFAULT_MOCK_ARTICLES];
  if (params.status && params.status !== "ALL") {
    filtered = filtered.filter((a) => a.status === params.status);
  }
  if (params.categoryId) {
    filtered = filtered.filter((a) => a.category_id === params.categoryId);
  }
  const q = (params.q || "").trim().toLowerCase();
  if (q) {
    filtered = filtered.filter((art) => {
      const matchTitle = String(art.title || "")
        .toLowerCase()
        .includes(q);
      const matchBody = String(art.body_markdown || art.body_html || "")
        .toLowerCase()
        .includes(q);
      const matchTags = Array.isArray(art.tags)
        ? art.tags.some((t) => String(t).toLowerCase().includes(q))
        : false;
      return matchTitle || matchBody || matchTags;
    });
  }
  return clone(filtered);
}

const FAMILY_CATEGORY_META: Record<
  string,
  { categoryId: string; categoryName: string }
> = {
  Blueprint: {
    categoryId: "cat-study-authoring",
    categoryName: "Study Authoring & MDR",
  },
  Capture: {
    categoryId: "cat-data-management",
    categoryName: "Data Management & Coding",
  },
  Orbit: {
    categoryId: "cat-site-monitoring",
    categoryName: "Site Monitoring & CTMS",
  },
  Engage: {
    categoryId: "cat-clinical-ops",
    categoryName: "Clinical Operations",
  },
  Allocate: {
    categoryId: "cat-clinical-ops",
    categoryName: "Clinical Operations",
  },
  Lens: {
    categoryId: "cat-data-management",
    categoryName: "Data Management & Coding",
  },
  Vigil: {
    categoryId: "cat-regulatory-compliance",
    categoryName: "Regulatory & Quality Compliance",
  },
  Assure: {
    categoryId: "cat-regulatory-compliance",
    categoryName: "Regulatory & Quality Compliance",
  },
  "Cadence Core": {
    categoryId: "cat-clinical-ops",
    categoryName: "Clinical Operations",
  },
};

function buildSynthesizedDemoFamilySop(
  parsedRoute: ParsedCanvasRoute,
  persona?: string
): Record<string, unknown> | null {
  const catalog = listDemoCatalogModules();
  const matchedModule =
    catalog.find(
      (mod) =>
        scoreBundledPattern(parsedRoute, mod.defaultPath) > 0 ||
        scoreBundledPattern(parsedRoute, `${mod.defaultPath}/*`) > 0
    ) || null;
  if (!matchedModule) {
    return null;
  }

  const cleanSlug =
    (matchedModule.routeName || parsedRoute.path)
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "") || "workspace";
  const familyLabel = matchedModule.family || "Cadence Core";
  const titleLabel = matchedModule.title || "Clinical Workspace Guidance";
  const summaryText =
    matchedModule.summary ||
    "Provides family-scoped operational guidance and regulatory controls for this demonstration screen.";
  const categoryMeta = FAMILY_CATEGORY_META[familyLabel] || {
    categoryId: "cat-clinical-ops",
    categoryName: "Clinical Operations",
  };

  const entry = buildBundledSop({
    mappingId: `map-demo-synth-${cleanSlug}`,
    routePattern: matchedModule.defaultPath || parsedRoute.path,
    persona: persona || "super_admin",
    priority: 10,
    articleId: `art-demo-synth-${cleanSlug}`,
    versionId: `ver-demo-synth-${cleanSlug}`,
    title: `SOP-DEMO: ${familyLabel} — ${titleLabel}`,
    slug: `sop-demo-${cleanSlug}`,
    tags: ["demo", cleanSlug, familyLabel.toLowerCase().replace(/\s+/g, "-")],
    categoryId: categoryMeta.categoryId,
    categoryName: categoryMeta.categoryName,
    sectionAnchor: `#${cleanSlug}-guidance`,
    heading: `${titleLabel} (${familyLabel})`,
    summary: summaryText,
  });
  return withPortalReferenceLinks({ [parsedRoute.path]: entry })[
    parsedRoute.path
  ];
}

export function getFallbackContextualHelp(route?: string, persona?: string) {
  if (isNonDemoProductionBuild()) {
    return {
      matched_mapping: null,
      primary_article: null,
      primary_version: null,
      section_anchor: null,
      related_articles: [],
      article: null,
      version: null,
    };
  }
  const parsedRoute = parseRouteAndCanvasQuery(route);
  const cleanRoute = parsedRoute.path;

  const candidates: Array<{
    score: number;
    priority: number;
    entry: Record<string, unknown>;
  }> = [];

  for (const [key, fallback] of Object.entries(DEFAULT_CONTEXTUAL_SOPS)) {
    const mapping = fallback.matched_mapping as
      Record<string, unknown> | undefined;
    const pattern = String(mapping?.route_pattern || key);
    const score = scoreBundledPattern(parsedRoute, pattern);
    if (score > 0) {
      candidates.push({
        score,
        priority: Number(mapping?.priority || 0),
        entry: fallback,
      });
    }
  }

  candidates.sort((a, b) => b.score - a.score || b.priority - a.priority);

  let selected: Record<string, unknown> | null =
    candidates.length > 0 ? clone(candidates[0].entry) : null;

  if (
    !selected &&
    DEFAULT_CONTEXTUAL_SOPS["/ecrf"] &&
    (cleanRoute.includes("subject") ||
      cleanRoute.includes("form") ||
      cleanRoute.includes("visit"))
  ) {
    selected = clone(DEFAULT_CONTEXTUAL_SOPS["/ecrf"]);
  }

  if (!selected && isDemoBuild()) {
    selected = buildSynthesizedDemoFamilySop(parsedRoute, persona);
  }

  if (selected) {
    if (persona && selected.matched_mapping) {
      (selected.matched_mapping as Record<string, unknown>).persona = persona;
    }
    const primaryArticle = selected.primary_article as
      Record<string, unknown> | undefined;
    const primaryId = String(primaryArticle?.id || "");
    const seenIds = new Set<string>(primaryId ? [primaryId] : []);
    const related: Array<Record<string, unknown>> = [];

    for (const candidate of candidates.slice(1)) {
      const art = candidate.entry.primary_article as
        Record<string, unknown> | undefined;
      const artId = String(art?.id || "");
      if (art && artId && !seenIds.has(artId)) {
        seenIds.add(artId);
        related.push(clone(art));
        if (related.length === 3) break;
      }
    }

    if (related.length < 3 && primaryArticle?.category_id) {
      for (const sibling of DEFAULT_MOCK_ARTICLES) {
        const sibId = String(sibling.id || "");
        if (
          sibId &&
          !seenIds.has(sibId) &&
          sibling.category_id === primaryArticle.category_id
        ) {
          seenIds.add(sibId);
          related.push(clone(sibling));
          if (related.length === 3) break;
        }
      }
    }

    selected.related_articles = related;
    return selected;
  }

  return {
    matched_mapping: null,
    primary_article: null,
    primary_version: null,
    section_anchor: null,
    related_articles: [],
    article: null,
    version: null,
  };
}

/**
 * Knowledge base API. Writes, approvals, version history and audit logs always come from the server and throw on
 * failure: an approval, publish or audit entry is never synthesised locally (#4523). Only read-only help content
 * (contextual help, article list/detail, categories) may fall back to the bundled offline set, and only in a demo
 * build.
 */
export const knowledgeService = {
  async resolveContextualHelp({
    route,
    persona,
  }: { route?: string; persona?: string } = {}) {
    let response: Record<string, unknown>;
    try {
      response = (await unwrapResponse(
        knowledgeClient.GET("/api/v1/knowledge/contextual-help", {
          params: {
            query: {
              route: route || "/",
              persona,
            },
          },
        })
      )) as unknown as Record<string, unknown>;
    } catch (err) {
      if (isDemoBuild()) return getFallbackContextualHelp(route, persona);
      throw err;
    }
    if (response && (response.primary_article || response.article)) {
      return response;
    }
    // No mapped SOP: a demo build shows bundled help, any other build shows the server's (empty) answer.
    return isDemoBuild() ? getFallbackContextualHelp(route, persona) : response;
  },

  async listArticles(
    params: {
      status?: string;
      categoryId?: string;
      q?: string;
      persona?: string;
    } = {}
  ) {
    try {
      const resp = await unwrapResponse(
        knowledgeClient.GET("/api/v1/knowledge/articles", {
          params: {
            query: {
              status_filter: params.status ?? undefined,
              category_id: params.categoryId,
              q: params.q,
              persona: params.persona,
            },
          },
        })
      );
      return Array.isArray(resp) ? resp : [];
    } catch (err) {
      if (isDemoBuild()) return getFallbackArticles(params);
      throw err;
    }
  },

  async listCategories(params: { persona?: string } = {}) {
    try {
      const resp = await unwrapResponse(
        knowledgeClient.GET("/api/v1/knowledge/categories", {
          params: {
            query: {
              persona: params.persona,
            },
          },
        })
      );
      return Array.isArray(resp) ? resp : [];
    } catch (err) {
      if (isDemoBuild()) return clone(DEFAULT_CATEGORIES);
      throw err;
    }
  },

  async createCategory(payload: Record<string, unknown>) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/categories", {
        body: payload as never,
      })
    );
  },

  async createArticle(payload: Record<string, unknown>) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles", {
        body: payload as never,
      })
    );
  },

  async updateArticleDraft(
    articleId: string,
    payload: Record<string, unknown>
  ) {
    return await unwrapResponse(
      openApiClient.PUT("/api/v1/knowledge/articles/{article_id}", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async submitArticleReview(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    return await unwrapResponse(
      openApiClient.POST(
        "/api/v1/knowledge/articles/{article_id}/submit-review",
        {
          params: { path: { article_id: articleId } },
          body: payload as never,
        }
      )
    );
  },

  async approveArticle(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles/{article_id}/approve", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async rejectArticle(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles/{article_id}/reject", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async publishArticle(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles/{article_id}/publish", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async transitionArticle(
    articleId: string,
    payload: { target_status: string }
  ) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles/{article_id}/transition", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async listArticleVersions(articleId: string) {
    const resp = await unwrapResponse(
      knowledgeClient.GET("/api/v1/knowledge/articles/{article_id}/versions", {
        params: { path: { article_id: articleId } },
      })
    );
    return Array.isArray(resp) ? resp : [];
  },

  async getArticleAuditLog(articleId: string) {
    const resp = await unwrapResponse(
      knowledgeClient.GET("/api/v1/knowledge/articles/{article_id}/audit-log", {
        params: { path: { article_id: articleId } },
      })
    );
    return Array.isArray(resp) ? resp : [];
  },
};
