import { isDemoBuild } from "../utils/demoMode.js";
import { createOpenApiClient, openApiClient, unwrapResponse } from "./client";
import type { paths as KnowledgePaths } from "./generated/knowledge";

const knowledgeClient = createOpenApiClient<KnowledgePaths>();

/**
 * Bundled offline help categories. Served only by a demo build when the API is unreachable (#4523).
 */
export const DEFAULT_CATEGORIES = [
  {
    id: "cat-clinical-ops",
    name: "Clinical Operations",
    slug: "clinical-operations",
    description: "eCRF data capture, subject enrollment, and visit procedures.",
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

/**
 * Bundled offline help SOPs. Served only by a demo build when the API is unreachable (#4523).
 */
export const DEFAULT_CONTEXTUAL_SOPS: Record<
  string,
  Record<string, unknown>
> = {
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
      body_html: `<h2 id="clinical-study-canvas-operations">Clinical Study Canvas Operations</h2><p id="active-study-clinical-canvas">Governs study-level subject enrollment, Schedule of Activities verification, and site activation monitoring.</p><h2 id="study-canvas-key-controls">Key Controls</h2><ol><li>Use pseudonymous subject codes (for example <code>SUBJ-1001</code>) exclusively; never enter direct participant PII.</li><li>Supply a clear 21 CFR Part 11 reason for change whenever recording or amending clinical observations.</li><li>Review open form queries and SDV indicators prior to visit sign-off.</li></ol><h3 id="protocol-versions-and-soa-design">Protocol Versions &amp; Schedule Design</h3><p>Verify active protocol version lineage and USDM Schedule of Activities bindings.</p><h3 id="participant-enrollment-and-visit-matrix">Participant Enrollment &amp; Visit Matrix</h3><p>Confirm site greenlight and consent verification before registering participants.</p><h3 id="participant-timeline-and-visit-record">Participant Timeline &amp; Visit Record</h3><p>Inspect longitudinal visit completion and open form queries within the participant record.</p><h3 id="database-freeze-and-lock-controls">Database Freeze &amp; Lock Controls</h3><p>Confirm zero unresolved critical queries and PI sign-off prior to database lock.</p><h3 id="regulatory-submission-and-exports">Regulatory Submission &amp; Data Exports</h3><p>Validate CDISC SDTM/ADaM dataset lineage and SHA-256 export manifests.</p><h3 id="investigational-product-and-rtsm-supply">Investigational Product &amp; RTSM Supply</h3><p>Reconcile site depot inventory, temperature excursion holds, and randomization kit accountability.</p><h3 id="protocol-validation-and-release-checks">Protocol Validation &amp; Release Checks</h3><p>Resolve structural edit-check diagnostics and USDM schema validation findings before promoting protocol versions.</p>`,
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
      title: "SOP-MDR-104: Schedule of Activities Matrix & Protocol Authoring",
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
      title: "SOP-CTMS-302: Site Monitoring Visits & Source Data Verification",
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
      id: "ver-aud-401",
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
  "/observations": {
    matched_mapping: {
      id: "map-obs-01",
      route_pattern: "/observations/:observationRef",
      persona: "data_manager",
      article_id: "art-observations-sop",
      section_anchor: "#clinical-observation-provenance-review",
      priority: 30,
      is_active: true,
    },
    primary_article: {
      id: "art-observations-sop",
      title:
        "SOP-OBS-210: Clinical Observation Inspection, Query History & Provenance Review",
      slug: "sop-obs-210-clinical-observation-inspection",
      status: "PUBLISHED",
      version_index: 1,
      version_label: "1.1",
      tags: ["observations", "provenance", "sdv", "queries", "safety"],
      category_id: "cat-data-management",
      category_name: "Data Management & Coding",
      author_user_id: "lead_data_manager",
      last_edited_by: "lead_data_manager",
      approved_by: "qa_director",
      created_at: "2026-02-15T09:00:00Z",
      updated_at: "2026-06-15T11:15:00Z",
      body_markdown: `## Clinical Observation Provenance & Discrepancy Review\n1. Inspect source provenance, prior amendments, and open queries before updating or verifying an observation.\n2. Escalate clinically significant safety abnormalities or MedDRA coding questions with pseudonymous subject codes only.`,
      body_html: `<h2 id="clinical-observation-provenance-review">Clinical Observation Provenance &amp; Discrepancy Review</h2><p>Standardizes item-level observation inspection, audit provenance verification, medical safety review, and query resolution.</p><ol><li>Inspect source provenance, prior amendments, and open queries before updating or verifying an observation.</li><li>Escalate clinically significant safety abnormalities or MedDRA coding questions with pseudonymous subject codes only.</li></ol>`,
    },
    primary_version: {
      id: "ver-obs-210",
      version_index: 1,
      version_label: "1.1",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "lead_data_manager",
      reason_for_change: "Published observation provenance SOP",
      created_at: "2026-06-15T11:15:00Z",
    },
    section_anchor: "#clinical-observation-provenance-review",
    related_articles: [],
  },
  "/etmf": {
    matched_mapping: {
      id: "map-etmf-01",
      route_pattern: "/etmf/*",
      persona: "cra_monitor",
      article_id: "art-etmf-sop",
      section_anchor: "#etmf-artifact-filing-and-qc",
      priority: 25,
      is_active: true,
    },
    primary_article: {
      id: "art-etmf-sop",
      title:
        "SOP-TMF-301: Electronic Trial Master File (eTMF) Artifact Filing & QC",
      slug: "sop-tmf-301-etmf-artifact-filing",
      status: "PUBLISHED",
      version_index: 1,
      version_label: "1.4",
      tags: ["etmf", "dia-reference-model", "essential-documents", "qc"],
      category_id: "cat-regulatory-compliance",
      category_name: "Regulatory & Quality Compliance",
      author_user_id: "qa_director",
      last_edited_by: "qa_director",
      approved_by: "qa_director",
      created_at: "2026-02-18T09:00:00Z",
      updated_at: "2026-06-18T11:15:00Z",
      body_markdown: `## eTMF Contemporaneous Filing & Inspection Readiness\n1. File essential regulatory and ethics artifacts under the matching zone, section, and study/site scope.\n2. Verify document legibility, metadata completeness, and immutable hash integrity during QC review.`,
      body_html: `<h2 id="etmf-artifact-filing-and-qc">eTMF Contemporaneous Filing &amp; Inspection Readiness</h2><ol><li>File essential regulatory and ethics artifacts under the matching zone, section, and study/site scope.</li><li>Verify document legibility, metadata completeness, and immutable hash integrity during QC review.</li></ol>`,
    },
    primary_version: {
      id: "ver-tmf-301",
      version_index: 1,
      version_label: "1.4",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "qa_director",
      reason_for_change: "Published eTMF QC controls",
      created_at: "2026-06-18T11:15:00Z",
    },
    section_anchor: "#etmf-artifact-filing-and-qc",
    related_articles: [],
  },
  "/training": {
    matched_mapping: {
      id: "map-training-01",
      route_pattern: "/training/*",
      persona: "site_crc",
      article_id: "art-training-sop",
      section_anchor: "#gxp-training-and-delegation",
      priority: 25,
      is_active: true,
    },
    primary_article: {
      id: "art-training-sop",
      title:
        "SOP-TRN-202: GxP Training Curriculum Completion & Delegation Sign-Off",
      slug: "sop-trn-202-gxp-training-attestation",
      status: "PUBLISHED",
      version_index: 1,
      version_label: "1.3",
      tags: ["training", "delegation", "ich-e6", "qualification"],
      category_id: "cat-regulatory-compliance",
      category_name: "Regulatory & Quality Compliance",
      author_user_id: "qa_director",
      last_edited_by: "qa_director",
      approved_by: "qa_director",
      created_at: "2026-02-19T09:00:00Z",
      updated_at: "2026-06-19T11:15:00Z",
      body_markdown: `## GxP Role Qualification & Training Attestation\n1. Complete assigned protocol and GxP SOP modules prior to performing delegated clinical duties.\n2. Maintain current training records across protocol amendments to preserve active write access.`,
      body_html: `<h2 id="gxp-training-and-delegation">GxP Role Qualification &amp; Training Attestation</h2><ol><li>Complete assigned protocol and GxP SOP modules prior to performing delegated clinical duties.</li><li>Maintain current training records across protocol amendments to preserve active write access.</li></ol>`,
    },
    primary_version: {
      id: "ver-trn-202",
      version_index: 1,
      version_label: "1.3",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "qa_director",
      reason_for_change: "Published GxP training controls",
      created_at: "2026-06-19T11:15:00Z",
    },
    section_anchor: "#gxp-training-and-delegation",
    related_articles: [],
  },
  "/overview": {
    matched_mapping: {
      id: "map-overview-01",
      route_pattern: "/overview",
      persona: "super_admin",
      article_id: "art-overview-sop",
      section_anchor: "#clinical-command-center-triage",
      priority: 25,
      is_active: true,
    },
    primary_article: {
      id: "art-overview-sop",
      title:
        "SOP-OPS-100: Clinical Operations Command Center & Action Queue Triage",
      slug: "sop-ops-100-clinical-command-center",
      status: "PUBLISHED",
      version_index: 1,
      version_label: "1.1",
      tags: ["overview", "command-center", "triage", "portfolio"],
      category_id: "cat-clinical-ops",
      category_name: "Clinical Operations",
      author_user_id: "lead_clinical_ops",
      last_edited_by: "lead_clinical_ops",
      approved_by: "qa_director",
      created_at: "2026-02-20T09:00:00Z",
      updated_at: "2026-06-20T11:15:00Z",
      body_markdown: `## Purpose & Scope\n1. Review high-priority safety, SDV, and query alerts at workspace login.\n2. Navigate directly to study, site, or support queues requiring immediate GxP follow-up.`,
      body_html: `<h2 id="clinical-command-center-triage">Purpose &amp; Scope</h2><ol><li>Review high-priority safety, SDV, and query alerts at workspace login.</li><li>Navigate directly to study, site, or support queues requiring immediate GxP follow-up.</li></ol>`,
    },
    primary_version: {
      id: "ver-ops-100",
      version_index: 1,
      version_label: "1.1",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "lead_clinical_ops",
      reason_for_change: "Published command center SOP",
      created_at: "2026-06-20T11:15:00Z",
    },
    section_anchor: "#clinical-command-center-triage",
    related_articles: [],
  },
  "/tickets": {
    matched_mapping: {
      id: "map-tickets-01",
      route_pattern: "/tickets",
      persona: "super_admin",
      article_id: "art-tickets-sop",
      section_anchor: "#gxp-support-ticket-lifecycle",
      priority: 25,
      is_active: true,
    },
    primary_article: {
      id: "art-tickets-sop",
      title:
        "SOP-TCK-501: GxP Support Ticket Triage, Root-Cause CAPA & Part 11 Closure",
      slug: "sop-tck-501-gxp-support-ticket-lifecycle",
      status: "PUBLISHED",
      version_index: 1,
      version_label: "1.1",
      tags: ["tickets", "support", "capa", "sla", "part11"],
      category_id: "cat-regulatory-compliance",
      category_name: "Regulatory & Quality Compliance",
      author_user_id: "qa_director",
      last_edited_by: "qa_director",
      approved_by: "qa_director",
      created_at: "2026-02-21T09:00:00Z",
      updated_at: "2026-06-21T11:15:00Z",
      body_markdown: `## GxP Support Ticket Triage & Closure Governance\n1. Never enter direct participant PII in ticket descriptions, comments, or root-cause summaries; use pseudonymous subject codes.\n2. Record structured resolution codes and root-cause summaries prior to resolving tickets and applying Part 11 closure signatures.`,
      body_html: `<h2 id="gxp-support-ticket-lifecycle">GxP Support Ticket Triage &amp; Closure Governance</h2><ol><li>Never enter direct participant PII in ticket descriptions, comments, or root-cause summaries; use pseudonymous subject codes.</li><li>Record structured resolution codes and root-cause summaries prior to resolving tickets and applying Part 11 closure signatures.</li></ol>`,
    },
    primary_version: {
      id: "ver-tck-501",
      version_index: 1,
      version_label: "1.1",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "qa_director",
      reason_for_change: "Published support ticket SOP",
      created_at: "2026-06-21T11:15:00Z",
    },
    section_anchor: "#gxp-support-ticket-lifecycle",
    related_articles: [],
  },
  "/knowledge": {
    matched_mapping: {
      id: "map-knowledge-01",
      route_pattern: "/knowledge",
      persona: "super_admin",
      article_id: "art-knowledge-sop",
      section_anchor: "#controlled-sop-repository-governance",
      priority: 25,
      is_active: true,
    },
    primary_article: {
      id: "art-knowledge-sop",
      title:
        "SOP-KNB-502: Controlled SOP Knowledge Hub & Regulatory Version Governance",
      slug: "sop-knb-502-controlled-knowledge-hub",
      status: "PUBLISHED",
      version_index: 1,
      version_label: "1.1",
      tags: ["knowledge", "sop", "versioning", "part11"],
      category_id: "cat-regulatory-compliance",
      category_name: "Regulatory & Quality Compliance",
      author_user_id: "qa_director",
      last_edited_by: "qa_director",
      approved_by: "qa_director",
      created_at: "2026-02-22T09:00:00Z",
      updated_at: "2026-06-22T11:15:00Z",
      body_markdown: `## Controlled SOP Repository Usage\n1. Verify that referenced SOP guidance carries PUBLISHED status and a locked version snapshot before executing regulated procedures.\n2. Inspect the 21 CFR Part 11 audit trail tab to verify cryptographic SHA-256 block hashes during quality or regulatory reviews.`,
      body_html: `<h2 id="controlled-sop-repository-governance">Controlled SOP Repository Usage</h2><ol><li>Verify that referenced SOP guidance carries <code>PUBLISHED</code> status and a locked version snapshot before executing regulated procedures.</li><li>Inspect the 21 CFR Part 11 audit trail tab to verify cryptographic SHA-256 block hashes during quality or regulatory reviews.</li></ol>`,
    },
    primary_version: {
      id: "ver-knb-502",
      version_index: 1,
      version_label: "1.1",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "qa_director",
      reason_for_change: "Published knowledge hub SOP",
      created_at: "2026-06-22T11:15:00Z",
    },
    section_anchor: "#controlled-sop-repository-governance",
    related_articles: [],
  },
};

export const DEFAULT_MOCK_ARTICLES = [
  DEFAULT_CONTEXTUAL_SOPS["/ecrf"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/mdr"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/studies"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/studies/:studyCode"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/ctms"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/ssu"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/audit"].primary_article,
];

const BUNDLED_ARTICLES = DEFAULT_MOCK_ARTICLES as Array<
  Record<string, unknown>
>;

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value));
}

/** Bundled, read-only offline help articles filtered like the list endpoint (demo builds only). */
function getFallbackArticles(
  params: { status?: string; categoryId?: string; q?: string } = {}
) {
  let filtered = [...BUNDLED_ARTICLES];
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

export function getFallbackContextualHelp(route?: string, persona?: string) {
  const raw = (route || "/").trim();
  const withoutHash = raw.split("#")[0] || "/";
  const queryIdx = withoutHash.indexOf("?");
  const cleanRoute =
    (queryIdx >= 0 ? withoutHash.slice(0, queryIdx) : withoutHash).replace(
      /\/+$/,
      ""
    ) || "/";
  const searchParams = new URLSearchParams(
    queryIdx >= 0 ? withoutHash.slice(queryIdx + 1) : ""
  );
  const segments = cleanRoute.split("/").filter(Boolean);

  let selected: Record<string, unknown> | null = null;
  if (cleanRoute === "/studies") {
    selected = clone(DEFAULT_CONTEXTUAL_SOPS["/studies"]);
  } else if (segments.length === 2 && segments[0] === "studies") {
    const section = (searchParams.get("section") || "").trim().toLowerCase();
    let matchedPattern = "/studies/:studyCode";
    if (searchParams.get("proposal")) {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/mdr"]);
      selected.section_anchor = "#amendment-proposal-review";
      matchedPattern = "/studies/:studyCode?proposal=*";
    } else if (searchParams.get("observation")) {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/observations"]);
      selected.section_anchor = "#clinical-observation-provenance-review";
      matchedPattern = "/studies/:studyCode?observation=*";
    } else if (searchParams.get("form")) {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/ecrf"]);
      selected.section_anchor = "#ecrf-form-capture-and-change-reason";
      matchedPattern = "/studies/:studyCode?form=*";
    } else if (searchParams.get("participant")) {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/studies/:studyCode"]);
      selected.section_anchor = "#participant-timeline-and-visit-record";
      matchedPattern = "/studies/:studyCode?participant=*";
    } else if (
      section === "site-activation" ||
      section === "sites" ||
      section === "startup-greenlight"
    ) {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/ssu"]);
      selected.section_anchor =
        section === "sites"
          ? "#global-country-activation-gates"
          : section === "startup-greenlight"
            ? "#site-initiation-visit-greenlight"
            : "#site-greenlight-activation-matrix";
      matchedPattern = `/studies/:studyCode?section=${section}`;
    } else if (section === "documents") {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/etmf"]);
      selected.section_anchor = "#etmf-artifact-filing-and-qc";
      matchedPattern = "/studies/:studyCode?section=documents";
    } else if (section === "training") {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/training"]);
      selected.section_anchor = "#gxp-training-and-delegation";
      matchedPattern = "/studies/:studyCode?section=training";
    } else if (
      section === "site-milestones" ||
      section === "delegation" ||
      section === "monitoring-visits"
    ) {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/ctms"]);
      selected.section_anchor =
        section === "site-milestones"
          ? "#ctms-portfolio-oversight"
          : section === "delegation"
            ? "#site-visit-readiness"
            : "#site-monitoring-and-sdv";
      matchedPattern = `/studies/:studyCode?section=${section}`;
    } else if (section === "queries") {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/ecrf"]);
      selected.section_anchor = "#discrepancy-management-and-coding";
      matchedPattern = "/studies/:studyCode?section=queries";
    } else {
      selected = clone(DEFAULT_CONTEXTUAL_SOPS["/studies/:studyCode"]);
      const canvasSectionAnchors: Record<string, string> = {
        "protocol-versions": "#protocol-versions-and-soa-design",
        participants: "#participant-enrollment-and-visit-matrix",
        locks: "#database-freeze-and-lock-controls",
        exports: "#regulatory-submission-and-exports",
        supply: "#investigational-product-and-rtsm-supply",
        validation: "#protocol-validation-and-release-checks",
      };
      if (canvasSectionAnchors[section]) {
        selected.section_anchor = canvasSectionAnchors[section];
        matchedPattern = `/studies/:studyCode?section=${section}`;
      }
    }
    if (selected?.matched_mapping) {
      const mapping = selected.matched_mapping as Record<string, unknown>;
      mapping.route_pattern = matchedPattern;
      if (selected.section_anchor) {
        mapping.section_anchor = selected.section_anchor;
      }
    }
  } else if (segments.length === 2 && segments[0] === "observations") {
    selected = clone(DEFAULT_CONTEXTUAL_SOPS["/observations"]);
  } else {
    for (const [key, fallback] of Object.entries(DEFAULT_CONTEXTUAL_SOPS)) {
      if (key.includes(":")) continue;
      if (cleanRoute === key || cleanRoute.startsWith(`${key}/`)) {
        selected = clone(fallback);
        break;
      }
    }
  }

  if (
    !selected &&
    (cleanRoute.includes("subject") ||
      cleanRoute.includes("form") ||
      cleanRoute.includes("visit"))
  ) {
    selected = clone(DEFAULT_CONTEXTUAL_SOPS["/ecrf"]);
  }

  if (selected) {
    if (persona && selected.matched_mapping) {
      (selected.matched_mapping as Record<string, unknown>).persona = persona;
    }
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
