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
  stepsHeading?: string;
  steps?: string[];
  extraSections?: Array<{ id?: string; heading: string; body: string }>;
  extraHtml?: string;
  reasonForChange?: string;
  updatedAt?: string;
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

export function slugifySopHeading(heading: string): string {
  const cleaned = String(heading || "")
    .trim()
    .replace(/^#+/, "")
    .replace(/&/g, " and ")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return cleaned || "section";
}

function formatInlineSopMarkdown(rawLine: string): string {
  if (!rawLine) return "";
  const parts = rawLine.split(/(`[^`]+`)/g);
  return parts
    .map((part) => {
      if (part.length >= 2 && part.startsWith("`") && part.endsWith("`")) {
        return `<code>${escapeHtml(part.slice(1, -1))}</code>`;
      }
      const escaped = escapeHtml(part);
      const withStrong = escaped.replace(
        /\*\*([^*]+)\*\*/g,
        "<strong>$1</strong>"
      );
      return withStrong.replace(/(^|[^*])\*([^*]+)\*/g, "$1<em>$2</em>");
    })
    .join("");
}

export function compileSopMarkdownToHtml(
  markdown: string,
  primaryAnchor?: string | null,
  headingAnchors?: Record<string, string>
): string {
  if (!markdown || !markdown.trim()) return "";
  const lines = markdown
    .replace(/\r\n/g, "\n")
    .replace(/\r/g, "\n")
    .split("\n");
  const usedIds = new Set<string>();
  const normalizedPrimary = (primaryAnchor || "").trim().replace(/^#+/, "");
  let primaryApplied = false;

  const allocateId = (desired: string): string => {
    const baseId = (desired || "").trim().replace(/^#+/, "") || "section";
    if (!usedIds.has(baseId)) {
      usedIds.add(baseId);
      return baseId;
    }
    let suffix = 2;
    while (usedIds.has(`${baseId}-${suffix}`)) {
      suffix++;
    }
    const unique = `${baseId}-${suffix}`;
    usedIds.add(unique);
    return unique;
  };

  let html = "";
  const paragraphLines: string[] = [];
  let listTag: "ol" | "ul" | null = null;
  const listItems: string[] = [];

  const flushParagraph = () => {
    if (paragraphLines.length === 0) return;
    html += `<p>${formatInlineSopMarkdown(paragraphLines.join(" "))}</p>`;
    paragraphLines.length = 0;
  };

  const flushList = () => {
    if (!listTag || listItems.length === 0) {
      listTag = null;
      listItems.length = 0;
      return;
    }
    html += `<${listTag}>${listItems
      .map((item) => `<li>${formatInlineSopMarkdown(item)}</li>`)
      .join("")}</${listTag}>`;
    listTag = null;
    listItems.length = 0;
  };

  for (const rawLine of lines) {
    const trimmed = rawLine.trim();
    if (!trimmed) {
      flushParagraph();
      flushList();
      continue;
    }

    const headingMatch = trimmed.match(/^(#{2,4})\s+(.+?)$/);
    if (headingMatch) {
      flushParagraph();
      flushList();
      const level = headingMatch[1].length;
      const headingText = headingMatch[2].trim();
      const mapped = headingAnchors?.[headingText];
      let desiredId: string;
      if (mapped) {
        desiredId = mapped;
        if (
          normalizedPrimary &&
          mapped.replace(/^#+/, "") === normalizedPrimary
        ) {
          primaryApplied = true;
        }
      } else if (level === 2 && !primaryApplied && normalizedPrimary) {
        desiredId = normalizedPrimary;
        primaryApplied = true;
      } else {
        desiredId = slugifySopHeading(headingText);
        if (normalizedPrimary && desiredId === normalizedPrimary) {
          primaryApplied = true;
        }
      }
      const uniqueId = allocateId(desiredId);
      html += `<h${level} id="${escapeHtml(uniqueId)}">${formatInlineSopMarkdown(headingText)}</h${level}>`;
      continue;
    }

    const olMatch = trimmed.match(/^\d+\.\s+(.+?)$/);
    if (olMatch) {
      flushParagraph();
      if (listTag !== "ol") {
        flushList();
        listTag = "ol";
      }
      listItems.push(olMatch[1].trim());
      continue;
    }

    const ulMatch = trimmed.match(/^[-*]\s+(.+?)$/);
    if (ulMatch) {
      flushParagraph();
      if (listTag !== "ul") {
        flushList();
        listTag = "ul";
      }
      listItems.push(ulMatch[1].trim());
      continue;
    }

    flushList();
    paragraphLines.push(trimmed);
  }

  flushParagraph();
  flushList();
  return html;
}

function buildBundledSop(spec: BundledSopSpec): Record<string, unknown> {
  const versionIndex = spec.versionIndex ?? 1;
  const versionLabel = spec.versionLabel ?? "1.0";
  const author = spec.authorUserId ?? "qa_director";
  const updatedAt = spec.updatedAt ?? "2026-06-15T10:00:00Z";
  const steps =
    spec.steps && spec.steps.length > 0
      ? spec.steps
      : [
          `Verify assigned study, site, and role permissions prior to executing ${spec.heading.toLowerCase()}.`,
          "Record contemporaneous 21 CFR Part 11 change reasons for regulated updates and escalate blockers via Contextual Help using pseudonymous subject codes only.",
        ];
  const stepsHeading = spec.stepsHeading ?? "Operational Steps";
  const stepsMarkdown = steps
    .map((step, idx) => `${idx + 1}. ${step}`)
    .join("\n");
  const extraMarkdown = (spec.extraSections || [])
    .map((sec) => `\n\n### ${sec.heading}\n${sec.body}`)
    .join("");
  const bodyMarkdown = `## ${spec.heading}\n${spec.summary}\n\n### ${stepsHeading}\n${stepsMarkdown}${extraMarkdown}`;
  const headingAnchors: Record<string, string> = {};
  for (const sec of spec.extraSections || []) {
    if (sec.id) {
      headingAnchors[sec.heading] = sec.id.replace(/^#+/, "");
    }
  }
  const compiledHtml = compileSopMarkdownToHtml(
    bodyMarkdown,
    spec.sectionAnchor,
    headingAnchors
  );
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
      body_markdown: bodyMarkdown,
      body_html: `${compiledHtml}${spec.extraHtml || ""}`,
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
  stepsHeading: "Mandatory Lock Gates",
  steps: [
    "Resolve all open discrepancy queries, pending SDV items, and required PI signatures before applying a hard lock.",
    "Record an explicit regulatory justification and step-up signature for every lock or controlled unlock action.",
  ],
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
  stepsHeading: "Mandatory Controls",
  steps: [
    "Verify study/site authorization and lock state prior to generating regulatory submission or inspection export bundles.",
    "Confirm SHA-256 manifest integrity and pseudonymous subject de-identification on exported deliverables.",
  ],
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
      sectionAnchor: "#clinical-command-center-triage",
      heading: "Workspace Overview & Role Orientation",
      summary:
        "Guides clinical operators through role-scoped study readiness indicators, pending GxP tasks, and compliant navigation.",
      steps: [
        "Review high-priority safety, SDV, and query alerts at workspace login.",
        "Navigate directly to study, site, or support queues requiring immediate GxP follow-up.",
      ],
      extraSections: [
        {
          id: "workspace-overview-orientation",
          heading: "Role-Scoped Orientation",
          body: "Operators verify assigned study/site readiness indicators and active work queues upon sign-in.",
        },
        {
          id: "pharmacovigilance-signal-overview",
          heading: "Pharmacovigilance Signal Overview",
          body: "Safety officers prioritize expedited serious adverse event alerts and medical review escalations upon sign-in.",
        },
        {
          id: "investigator-oversight-summary",
          heading: "Investigator Oversight Summary",
          body: "Principal Investigators review pending visit signatures, safety notifications, and site delegation alerts.",
        },
      ],
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
      stepsHeading: "Key Controls",
      steps: [
        "Reconcile country and site activation dates with approved protocol amendments.",
        "Escalate milestone slippage with documented root-cause context.",
      ],
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
      stepsHeading: "Mandatory Checks",
      steps: [
        "Validate sponsor protocol identifier, therapeutic area, and regulatory phase before workspace creation.",
        "Verify initial study team RBAC assignments and training curriculum bindings prior to activation.",
      ],
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
      steps: [
        "Confirm your active workspace persona and assigned study/site scope before retrying restricted clinical routes.",
        "Escalate legitimate role or site provisioning requests under `ACCESS_CONTROL` for Sponsor Admin review and training verification.",
      ],
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
      steps: [
        "Use Clinical Canvas (`/studies/:studyCode`), Data Collection (`/ecrf`), Site Readiness (`/ctms`, `/ssu`), eTMF (`/etmf`), Training (`/training`), Support (`/tickets`), and Knowledge (`/knowledge`) for live regulated operations.",
        "Log an operational support ticket under `SYSTEM_SUPPORT` if an expected entitlement or module workflow is unavailable.",
      ],
    }),
    "/studies": buildBundledSop({
      mappingId: "map-studies-01",
      routePattern: "/studies",
      persona: "sponsor_designer",
      priority: 20,
      articleId: "art-studies-sop",
      versionId: "ver-std-101",
      title: "SOP-STD-101: Study Portfolio Governance & Workspace Navigation",
      slug: "sop-std-101-study-portfolio-governance",
      versionIndex: 1,
      versionLabel: "1.0",
      tags: ["studies", "portfolio", "governance", "usdm"],
      categoryId: "cat-study-authoring",
      categoryName: "Study Authoring & MDR",
      authorUserId: "sponsor_study_lead",
      sectionAnchor: "#study-portfolio-governance",
      heading: "Study Portfolio Governance",
      summary:
        "Standardizes study workspace selection, role-scoped study visibility, and protocol readiness checks.",
      steps: [
        "Verify protocol code and lifecycle status (`ACTIVE`, `IN_DESIGN`, or `LOCKED`) prior to opening clinical data workspaces.",
        "Confirm assigned study and site scope before initiating enrollment, monitoring, or data management actions.",
        "Escalate missing study assignments to Platform Access Governance via contextual support ticketing.",
      ],
      extraSections: [
        {
          id: "enterprise-portfolio-oversight",
          heading: "Enterprise Portfolio Oversight",
          body: "Sponsor administrators verify tenant-wide study lifecycle readiness, environment locks, and portfolio governance controls.",
        },
        {
          id: "protocol-version-selection",
          heading: "Protocol Version Selection",
          body: "Study designers confirm active USDM protocol versions before entering Schedule of Activities or amendment workspaces.",
        },
        {
          id: "assigned-site-study-selection",
          heading: "Assigned Site Study Selection",
          body: "Site coordinators select authorized protocols and verify site activation status before enrolling or updating participants.",
        },
        {
          id: "study-monitoring-entry",
          heading: "Study Monitoring Entry",
          body: "Clinical Research Associates verify site monitoring assignments, SDV backlogs, and pending visit action items.",
        },
        {
          id: "study-data-review-entry",
          heading: "Study Data Review Entry",
          body: "Data managers review open discrepancy query counts and database lock readiness across active protocols.",
        },
        {
          id: "inspection-scope-selection",
          heading: "Inspection Scope Selection",
          body: "Auditors confirm read-only inspection scope across published study artifacts and immutable audit trails.",
        },
      ],
      updatedAt: "2026-06-05T10:00:00Z",
    }),
    "/studies/:studyCode": buildBundledSop({
      mappingId: "map-study-canvas-01",
      routePattern: "/studies/:studyCode",
      persona: "site_crc",
      priority: 30,
      articleId: "art-study-canvas-sop",
      versionId: "ver-std-102",
      title:
        "SOP-STD-102: Clinical Study Canvas, Enrollment & Protocol Execution",
      slug: "sop-std-102-clinical-study-canvas",
      versionIndex: 2,
      versionLabel: "2.0",
      tags: ["studies", "canvas", "enrollment", "protocol-execution"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#clinical-study-canvas-operations",
      heading: "Clinical Study Canvas Operations",
      summary:
        "Governs study-level subject enrollment, Schedule of Activities verification, and site activation monitoring.",
      stepsHeading: "Key Controls",
      steps: [
        "Use pseudonymous subject codes (for example `SUBJ-1001`) exclusively; never enter direct participant PII.",
        "Supply a clear 21 CFR Part 11 reason for change whenever recording or amending clinical observations.",
        "Review open form queries and SDV indicators prior to visit sign-off.",
      ],
      extraSections: [
        {
          id: "active-study-clinical-canvas",
          heading: "Active Study Clinical Canvas",
          body: "Defines standard operating procedures for executing study-specific enrollment, visit schedule navigation, and form query resolution.",
        },
        {
          id: "participant-timeline-and-visit-record",
          heading: "Participant Timeline & Visit Record",
          body: "Inspect longitudinal visit completion, scheduled windows, and open form queries within the participant record.",
        },
        {
          id: "study-governance-and-locks",
          heading: "Study Governance & Locks",
          body: "Sponsor administrators oversee study-level access boundaries, freeze milestones, and lock authorization.",
        },
        {
          id: "visit-schedule-and-form-definitions",
          heading: "Visit Schedule & Form Definitions",
          body: "Sponsor designers maintain USDM visit schedules, collection specifications, and controlled terminology bindings.",
        },
        {
          id: "subject-enrollment-and-visit-capture",
          heading: "Subject Enrollment & Visit Capture",
          body: "Site coordinators verify eligibility criteria and enter contemporaneous visit assessments under pseudonymous subject IDs.",
        },
        {
          id: "investigator-oversight-and-signoff",
          heading: "Investigator Oversight & Sign-Off",
          body: "Site investigators review clinical significance assessments, protocol deviations, and 21 CFR Part 11 visit attestations.",
        },
        {
          id: "study-sdv-and-query-oversight",
          heading: "Study SDV & Query Oversight",
          body: "Monitors track source data verification progress and issue targeted discrepancy queries across assigned sites.",
        },
        {
          id: "study-query-triage-and-cleanliness",
          heading: "Study Query Triage & Cleanliness",
          body: "Data managers reconcile aged queries, coding flags, and cross-form consistency checks prior to milestone locks.",
        },
        {
          id: "study-safety-signal-review",
          heading: "Study Safety Signal Review",
          body: "Medical monitors and safety officers evaluate serious adverse event trends and laboratory alert thresholds.",
        },
        {
          id: "study-audit-trail-inspection",
          heading: "Study Audit Trail Inspection",
          body: "Auditors perform read-only verification of study lineage, electronic signatures, and contemporaneous change reasons.",
        },
      ],
      reasonForChange: "Updated for Clinical Canvas v2",
      updatedAt: "2026-06-10T10:00:00Z",
    }),
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
      stepsHeading: "Key Controls",
      steps: [
        "Validate USDM schedule structure, visit windows, and form bindings before publishing a new protocol version.",
        "Record a 21 CFR Part 11 justification whenever importing or superseding protocol versions.",
      ],
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
        steps: [
          "Confirm organization and facility identifiers prior to linking a clinical site to the study.",
          "Verify site code uniqueness and RBAC scope visibility for assigned site coordinators and monitors.",
        ],
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
      stageFallback: "start",
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
      stepsHeading: "Mandatory Checks",
      steps: [
        "Confirm ethics approval and executed clinical trial agreement dates before advancing site activation state.",
        "Document regulatory submission blockers via support escalation with study and site scope attached.",
      ],
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
      stepsHeading: "Greenlight Rules",
      steps: [
        "Verify 100% completion of mandatory startup prerequisites prior to issuing site enrollment greenlight.",
        "Retain immutable certification timestamps and approver attribution in the study audit trail.",
      ],
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
      stepsHeading: "Operational Controls",
      steps: [
        "Classify uploaded essential documents under the correct study and site scope without participant PII.",
        "Complete QC verification prior to relying on regulatory artifacts for site greenlight.",
      ],
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
      stepsHeading: "Qualification Rules",
      steps: [
        "Ensure site personnel complete required protocol version curricula prior to delegation sign-off.",
        "Re-verify training currency following major protocol amendments.",
      ],
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
      steps: [
        "Record contemporaneous actual completion dates for IRB submission, contract execution, and SIV completion.",
        "Escalate critical-path startup delays with root-cause commentary.",
      ],
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
      stepsHeading: "Mandatory Controls",
      steps: [
        "Verify active GxP training completion before delegating clinical assessment or eCRF entry responsibilities.",
        "Capture step-up electronic signatures when authorizing or ending delegated site duties.",
      ],
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
      stepsHeading: "Mandatory GxP Controls",
      steps: [
        "Reference participants strictly by pseudonymous subject code (for example `SUBJ-1001`); never record direct PII.",
        "Provide an auditable 21 CFR Part 11 reason for change on every observation amendment or form correction.",
      ],
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
      stepsHeading: "Operational Checklist",
      steps: [
        "Reconcile SDV coverage and open discrepancy queries before finalizing monitoring visit reports.",
        "Escalate major or critical monitoring findings to the quality and site operations queue with site scope preserved.",
      ],
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
      stepsHeading: "Mandatory Controls",
      steps: [
        "Quarantine affected kit lots immediately upon cold-chain excursion detection and escalate under `SUPPLY_EXCURSION`.",
        "Maintain blinded kit reconciliation records across dispensing and return workflows.",
      ],
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
      steps: [
        "Inspect linked observation provenance before answering or closing open discrepancy queries.",
        "Never close discrepancy queries without verifying either an observation correction or a documented clinical explanation.",
      ],
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
      stepsHeading: "Key Controls",
      steps: [
        "Execute dry-run previews against study observations to verify false-positive rates before activating new rule packs.",
        "Review validation findings alongside query ownership to coordinate timely site data cleaning.",
      ],
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
      stepsHeading: "Operational Controls",
      steps: [
        "Confirm database lock prerequisites and protocol deviation classifications prior to unblinded analysis cutoffs.",
        "Preserve treatment-assignment blinding across operational support escalations.",
      ],
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
    "/ecrf": buildBundledSop({
      mappingId: "map-ecrf-01",
      routePattern: "/ecrf/*",
      persona: "site_crc",
      priority: 10,
      articleId: "art-ecrf-sop",
      versionId: "ver-ecrf-201",
      title: "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture",
      slug: "sop-ecrf-201-subject-enrollment",
      versionIndex: 2,
      versionLabel: "2.1",
      tags: ["ecrf", "enrollment", "gxp-edc", "site-sop"],
      categoryId: "cat-clinical-ops",
      categoryName: "Clinical Operations",
      authorUserId: "lead_data_manager",
      sectionAnchor: "#subject-enrollment-and-visit-entry",
      heading: "Purpose & Scope",
      summary:
        "This Standard Operating Procedure (SOP) governs all investigator site personnel executing electronic Case Report Form (eCRF) data capture, subject registration, and visit assessment recording within the Cadence Clinical Research Platform.",
      stepsHeading: "Mandatory GxP Controls",
      steps: [
        "Confirm informed consent verification prior to recording baseline assessments.",
        "Record contemporaneous source-verified measurements and document mandatory change justifications for every amendment.",
        "Escalate protocol deviations or locked-form blockers via Contextual Help using pseudonymous subject identifiers only.",
      ],
      extraSections: [
        {
          id: "ecrf-form-capture-and-change-reason",
          heading: "eCRF Form Capture & Change Reason",
          body: "Verify unit conversions, controlled terminology selections, and non-PII 21 CFR Part 11 change reasons when saving form instances.",
        },
        {
          id: "discrepancy-management-and-coding",
          heading: "Discrepancy Management & Coding",
          body: "Triage open validation queries, document source-backed responses, and reconcile coding terms prior to form lock.",
        },
        {
          id: "edc-governance-and-locks",
          heading: "EDC Governance & Locks",
          body: "Sponsor administrators govern eCRF environment locks, freeze rules, and role-level data entry permissions.",
        },
        {
          id: "crf-layout-validation",
          heading: "CRF Layout Validation",
          body: "Study designers verify form item bindings, CDASH/SDTM annotations, and edit-check firing conditions prior to live capture.",
        },
        {
          id: "ecrf-sdv-and-query-verification",
          heading: "eCRF SDV & Query Verification",
          body: "Clinical Research Associates verify eCRF entries against clinic source records and close answered discrepancy queries.",
        },
        {
          id: "ecrf-audit-trail-review",
          heading: "eCRF Audit Trail Review",
          body: "Auditors inspect item-level revision histories, actor attributions, and SHA-256 Merkle chain integrity.",
        },
      ],
      reasonForChange: "Annual review & protocol amendment 2 alignment",
      updatedAt: "2026-05-18T14:30:00Z",
    }),
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
      stepsHeading: "Key Controls",
      steps: [
        "Inspect source provenance, prior amendments, and open queries before updating or verifying an observation.",
        "Escalate clinically significant safety abnormalities or medical coding questions with pseudonymous subject codes only.",
      ],
      extraSections: [
        {
          id: "safety-observation-medical-review",
          heading: "Safety Observation Medical Review",
          body: "Safety officers evaluate abnormal laboratory values and adverse event observations for expedited reporting criteria.",
        },
        {
          id: "investigator-clinical-significance-signoff",
          heading: "Investigator Clinical Significance Sign-Off",
          body: "Investigators record clinical significance determinations and contemporaneous medical rationale on flagged observations.",
        },
        {
          id: "source-observation-amendment",
          heading: "Source Observation Amendment",
          body: "Site coordinators submit source-verified corrections with mandatory 21 CFR Part 11 reasons for change.",
        },
        {
          id: "observation-sdv-verification",
          heading: "Observation SDV Verification",
          body: "Monitors compare recorded kernel observations against clinic source charts and record SDV status.",
        },
        {
          id: "observation-discrepancy-query-resolution",
          heading: "Observation Discrepancy Query Resolution",
          body: "Data managers review edit-check lineage and verify query responses before closing item-level discrepancies.",
        },
        {
          id: "observation-provenance-audit-trail",
          heading: "Observation Provenance Audit Trail",
          body: "Auditors inspect the append-only revision chain, actor identity, and UTC timestamps for every observation mutation.",
        },
      ],
    }),
    "/mdr": buildBundledSop({
      mappingId: "map-mdr-01",
      routePattern: "/mdr/*",
      persona: "sponsor_designer",
      priority: 10,
      articleId: "art-mdr-sop",
      versionId: "ver-mdr-104",
      title: "SOP-MDR-104: Schedule of Activities Matrix & Protocol Authoring",
      slug: "sop-mdr-104-soa-matrix-authoring",
      versionIndex: 3,
      versionLabel: "3.0",
      tags: ["mdr", "usdm", "soa-matrix", "study-design"],
      categoryId: "cat-study-authoring",
      categoryName: "Study Authoring & MDR",
      authorUserId: "sponsor_study_lead",
      sectionAnchor: "#schedule-of-activities-matrix-authoring",
      heading: "Protocol Metadata Management & SoA Design",
      summary:
        "Standardizes CDISC USDM Schedule of Activities authoring and controlled terminology binding.",
      stepsHeading: "Authoring Steps",
      steps: [
        "Bind every clinical activity and eCRF item to an approved CDISC Biomedical Concept and NCI Codelist version.",
        "Document protocol amendment rationale and assess downstream participant visit impact before publishing a new version.",
      ],
      extraSections: [
        {
          id: "amendment-proposal-review",
          heading: "Amendment Proposal Review",
          body: "Review structural Schedule of Activities diffs, version lineage, and governance sign-off prerequisites before approving protocol proposals.",
        },
      ],
      reasonForChange: "CDISC USDM v3.0 standard alignment",
      updatedAt: "2026-06-01T11:15:00Z",
    }),
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
      steps: [
        "Inspect extracted Schedule of Activities visit windows and Biomedical Concept mappings against the signed PDF protocol.",
        "Approve human-verified extraction blocks with a 21 CFR Part 11 justification prior to USDM draft import.",
      ],
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
      steps: [
        "Verify all mandatory ICH E6(R3) essential informed consent disclosures and comprehension checkpoints before freezing a master template.",
        "Bind IRB/IEC approval metadata and effective dates prior to releasing consent versions to investigator sites.",
      ],
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
      steps: [
        "Attach certified forward/back translation certificates before activating localized eConsent templates for a country or site.",
        "Configure participant re-consent flags whenever safety or Schedule of Activities amendments affect enrolled subjects.",
      ],
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
      steps: [
        "Evaluate structural visit, form, and edit-check diffs across baseline and target USDM protocol versions.",
        "Verify site IRB approval and staff training prerequisites before migrating enrolled participant casebooks.",
      ],
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
      steps: [
        "Confirm automated DICOM header scrubbing and burnt-in pixel PHI checks pass under pseudonymous subject codes only.",
        "Reconcile target and non-target lesion measurements across baseline and follow-up RECIST 1.1 timepoints.",
      ],
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
      steps: [
        "Record unique device identifier (`UDI`), serial number, and active firmware version prior to participant assignment.",
        "Document device return, cleaning, or quarantine disposition in the ISO 14155 accountability log.",
      ],
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
      steps: [
        "Quarantine suspect investigational device units immediately and link the deficiency record to pseudonymous subject observations.",
        "Assess unanticipated serious adverse device effect (`USADE`) causality within statutory vigilance reporting timelines.",
      ],
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
      steps: [
        "Verify source dossier redaction removes treatment-arm indicators and direct participant identifiers before CEC release.",
        "Complete independent reviewer assessments and record Part 11 signed consensus adjudication outcomes.",
      ],
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
      steps: [
        "Verify source dossier redaction removes treatment-arm indicators and direct participant identifiers before CEC release.",
        "Complete independent reviewer assessments and record Part 11 signed consensus adjudication outcomes.",
      ],
    }),
    "/ctms": buildBundledSop({
      mappingId: "map-ctms-01",
      routePattern: "/ctms/*",
      persona: "cra_monitor",
      priority: 10,
      articleId: "art-ctms-sop",
      versionId: "ver-ctms-302",
      title: "SOP-CTMS-302: Site Monitoring Visits & Source Data Verification",
      slug: "sop-ctms-302-site-monitoring",
      versionIndex: 1,
      versionLabel: "1.0",
      tags: ["ctms", "monitoring", "sdv", "cra"],
      categoryId: "cat-site-monitoring",
      categoryName: "Site Monitoring & CTMS",
      authorUserId: "cra_monitor",
      sectionAnchor: "#site-monitoring-and-sdv",
      heading: "Site Monitoring & SDV Procedures",
      summary:
        "Governs Clinical Trial Management System (CTMS) monitoring visit scheduling, follow-up letters, CRA workload balancing, and site milestone tracking.",
      stepsHeading: "Operational Checklist",
      steps: [
        "Reconcile site enrollment metrics and pending monitoring action items prior to on-site or remote visits.",
        "Document monitoring findings with ICH E6(R3) severity classification and link follow-up CAPA or support tickets.",
      ],
      extraSections: [
        {
          id: "ctms-portfolio-oversight",
          heading: "CTMS Portfolio Oversight",
          body: "Sponsor operations track site milestone attainment, monitoring cadence compliance, and regional enrollment velocity.",
        },
        {
          id: "monitoring-plan-alignment",
          heading: "Monitoring Plan Alignment",
          body: "Study designers align risk-based monitoring critical data indicators with Schedule of Activities assessments.",
        },
        {
          id: "site-visit-readiness",
          heading: "Site Visit Readiness & Delegation",
          body: "Site coordinators maintain current delegation logs, source chart availability, and action-item remediation prior to CRA visits.",
        },
        {
          id: "monitoring-query-reconciliation",
          heading: "Monitoring Query Reconciliation",
          body: "Data managers reconcile unresolved monitoring findings with open eCRF discrepancy queries before interim locks.",
        },
        {
          id: "monitoring-visit-report-inspection",
          heading: "Monitoring Visit Report Inspection",
          body: "Auditors verify signed monitoring visit reports, follow-up communications, and CAPA closure evidence.",
        },
      ],
      reasonForChange: "Initial release",
      updatedAt: "2026-06-01T11:15:00Z",
    }),
    "/ssu": buildBundledSop({
      mappingId: "map-ssu-01",
      routePattern: "/ssu/*",
      persona: "cra_monitor",
      priority: 15,
      articleId: "art-ssu-sop",
      versionId: "ver-ssu-205",
      title: "SOP-SSU-205: Site Startup Readiness & Regulatory Greenlight",
      slug: "sop-ssu-205-site-startup-greenlight",
      versionIndex: 1,
      versionLabel: "1.1",
      tags: ["ssu", "site-startup", "greenlight", "activation"],
      categoryId: "cat-site-monitoring",
      categoryName: "Site Monitoring & CTMS",
      authorUserId: "cra_monitor",
      sectionAnchor: "#site-greenlight-activation-matrix",
      heading: "Site Startup & Greenlight Certification",
      summary:
        "Defines IRB/IEC approval verification, DoA log completion, and investigational product release prerequisites.",
      stepsHeading: "Greenlight Verification",
      steps: [
        "Confirm Essential Document List (EDL) artifacts and Delegation of Authority sign-offs are complete.",
        "Verify country regulatory approval and IP pharmacy readiness before issuing greenlight certification.",
      ],
      extraSections: [
        {
          id: "site-startup-and-greenlight",
          heading: "Site Startup Prerequisites",
          body: "Establishes mandatory regulatory, ethics, contract, essential document, and training prerequisites required before certifying site activation.",
        },
        {
          id: "global-country-activation-gates",
          heading: "Global Country Activation Gates",
          body: "Sponsor operations verify competent authority approvals, clinical trial agreements, and site roster readiness across participating countries.",
        },
        {
          id: "protocol-country-pack-alignment",
          heading: "Protocol Country-Pack Alignment",
          body: "Study designers align country-specific ICF templates and local assessment rules with the master USDM protocol.",
        },
        {
          id: "site-essential-documents-checklist",
          heading: "Site Essential Documents Checklist",
          body: "Site coordinators upload current IRB/IEC approvals, investigator CVs, medical licenses, and laboratory certifications.",
        },
        {
          id: "site-initiation-visit-greenlight",
          heading: "Site Initiation Visit & Greenlight",
          body: "Monitors confirm Site Initiation Visit completion and facility readiness prior to greenlight release.",
        },
        {
          id: "edc-environment-release-gate",
          heading: "EDC Environment Release Gate",
          body: "Data managers confirm production eCRF edit checks and randomization tables are validated before unlocking site enrollment.",
        },
        {
          id: "greenlight-certification-audit",
          heading: "Greenlight Certification Audit",
          body: "Auditors inspect timestamped greenlight tokens and prerequisite document completeness prior to first-patient-in.",
        },
      ],
      reasonForChange: "Added country greenlight matrix checks",
      updatedAt: "2026-06-12T11:15:00Z",
    }),
    "/audit": buildBundledSop({
      mappingId: "map-audit-01",
      routePattern: "/audit/*",
      persona: "auditor",
      priority: 10,
      articleId: "art-audit-sop",
      versionId: "ver-audit-401",
      title:
        "SOP-AUD-401: 21 CFR Part 11 Audit Trail & Qualification Inspection",
      slug: "sop-aud-401-audit-trail-inspection",
      versionIndex: 1,
      versionLabel: "1.0",
      tags: ["audit", "part11", "gxp", "compliance"],
      categoryId: "cat-regulatory-compliance",
      categoryName: "Regulatory & Quality Compliance",
      authorUserId: "qa_director",
      sectionAnchor: "#gxp-audit-trail-inspection",
      heading: "Audit Trail Inspection Guidelines",
      summary:
        "Defines SHA-256 Merkle audit chain verification and regulatory inspection procedures.",
      stepsHeading: "Inspection Steps",
      steps: [
        "Filter immutable audit records by study, actor, entity, and UTC date window while preserving read-only inspection posture.",
        "Verify predecessor and block SHA-256 hashes prior to exporting signed regulatory inspection bundles.",
      ],
      reasonForChange: "Initial release",
      updatedAt: "2026-06-01T11:15:00Z",
    }),
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
      steps: [
        "Inspect electronic signature manifestations, signer meanings, and linked audit-trail records in read-only mode.",
        "Execute cryptographic SHA-256 hash-chain verification across study records without mutating operational state.",
      ],
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
      steps: [
        "Validate cross-form range and consistency expressions against dry-run test datasets prior to rule pack activation.",
        "Assign automated discrepancy queries to site or data management queues with clear remediation prompts.",
      ],
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
      steps: [
        "Verify study-pinned clinical dictionary versions before approving automated or manual verbatim term mappings.",
        "Issue targeted site clarification queries whenever verbatim terms are ambiguous or contain multiple conditions.",
      ],
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
      steps: [
        "Confirm projection refresh timestamps and upstream observation commit lineage before reviewing analytical tables.",
        "Enforce pseudonymous subject keys and blinded treatment masks across all lakehouse views.",
      ],
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
      steps: [
        "Pair wearable devices strictly against pseudonymous subject identifiers and verify UTC epoch synchronization.",
        "Investigate telemetry dropout alerts or out-of-range physiological streams with documented site follow-up.",
      ],
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
      steps: [
        "Verify active laboratory certification dates and age/sex-stratified reference ranges before reconciling local lab results.",
        "Route out-of-range analytes to the Principal Investigator for contemporaneous `CS` or `NCS` clinical significance sign-off.",
      ],
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
      steps: [
        "Confirm delegated telehealth jurisdiction and participant identity verification without recording video PII.",
        "Verify direct-to-participant cold-chain courier temperature logs before recording remote IP administration.",
      ],
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
      steps: [
        "Triage incoming serious adverse event reports within 24 hours and reconcile onset, severity, and causality fields against eCRF records.",
        "Evaluate Investigator's Brochure expectedness for `SUSAR` determination and track expedited regulatory submission clocks.",
      ],
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
      stepsHeading: "Operational Controls",
      steps: [
        "File essential regulatory and ethics artifacts under the matching zone, section, and study/site scope.",
        "Verify document legibility, metadata completeness, and immutable hash integrity during QC review.",
      ],
      extraSections: [
        {
          id: "etmf-inspection-readiness-verification",
          heading: "eTMF Inspection Readiness Verification",
          body: "Auditors inspect DIA Reference Model completeness, certified copy attestations, and immutable filing timestamps.",
        },
        {
          id: "investigator-site-file-attestation",
          heading: "Investigator Site File Attestation",
          body: "Principal Investigators verify site-level regulatory binder synchronization and essential document currency.",
        },
      ],
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
      steps: [
        "Review Key Risk Indicator (`KRI`) trends and Quality Tolerance Limit breaches across active studies and sites.",
        "Link confirmed systemic deviations to root-cause investigations and verify CAPA effectiveness checks prior to closure.",
      ],
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
      steps: [
        "Prioritize critical safety, temperature excursion, and amber SLA notifications at the start of each operational shift.",
        "Navigate directly from alert deep links to the scoped study, site, or ticket record to complete remediation.",
      ],
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
      stepsHeading: "Mandatory Rules",
      steps: [
        "Never enter direct participant PII in ticket descriptions, comments, or root-cause summaries; use pseudonymous subject codes (for example `SUBJ-1001`).",
        "Record structured resolution codes and root-cause summaries prior to resolving tickets and applying Part 11 closure signatures.",
      ],
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
      steps: [
        "Verify effective version numbers on Investigator's Brochures, lab manuals, and pharmacy manuals prior to site use.",
        "Record electronic site receipt acknowledgements and synchronize controlled documents with the Investigator Site File.",
      ],
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
      steps: [
        "Verify that referenced SOP guidance carries `PUBLISHED` status and a locked version snapshot before executing regulated procedures.",
        "Inspect the 21 CFR Part 11 audit trail tab to verify cryptographic SHA-256 block hashes during quality or regulatory reviews.",
      ],
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
      stepsHeading: "Qualification Rules",
      steps: [
        "Complete assigned protocol and GxP SOP modules prior to performing delegated clinical duties.",
        "Maintain current training records across protocol amendments to preserve active write access.",
      ],
      extraSections: [
        {
          id: "pi-delegation-log-signoff",
          heading: "PI Delegation Log Sign-Off",
          body: "Principal Investigators verify completed staff training attestations before countersigning Delegation of Authority entries.",
        },
        {
          id: "site-staff-training-attestation",
          heading: "Site Staff Training Attestation",
          body: "Site coordinators complete read-and-understand attestations for protocol amendments prior to executing clinical visits.",
        },
      ],
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
      steps: [
        "Confirm completed visit milestones and IRB-approved stipend schedules under pseudonymous subject codes only.",
        "Redact direct participant banking or address details from sponsor-visible reimbursement audit logs.",
      ],
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
      steps: [
        "Verify health-literacy readability metrics and non-promotional medical accuracy against locked CSR results.",
        "Complete ethics and translation approvals prior to publishing multi-language plain-language trial summaries.",
      ],
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
