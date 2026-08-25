import { apiClient } from "./apiClient.js";

/**
 * Default fallback categories for offline sandbox demo mode.
 */
export const DEFAULT_CATEGORIES = [
  {
    id: "cat-clinical-ops",
    name: "Clinical Operations",
    slug: "clinical-operations",
    description: "eCRF data capture, subject enrollment, and visit procedures.",
    persona_visibility: [
      "site_crc",
      "cra_monitor",
      "data_manager",
      "super_admin",
    ],
    parent_id: null,
    is_deleted: false,
    created_at: "2026-01-15T08:00:00Z",
    created_by: "system_admin",
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
    created_by: "system_admin",
  },
  {
    id: "cat-site-monitoring",
    name: "Site Monitoring & CTMS",
    slug: "site-monitoring-ctms",
    description:
      "CRA monitoring visits, source data verification, and trip reports.",
    persona_visibility: ["cra_monitor", "site_crc", "super_admin"],
    parent_id: null,
    is_deleted: false,
    created_at: "2026-01-15T08:00:00Z",
    created_by: "system_admin",
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
    ],
    parent_id: null,
    is_deleted: false,
    created_at: "2026-01-15T08:00:00Z",
    created_by: "system_admin",
  },
  {
    id: "cat-data-management",
    name: "Data Management & Coding",
    slug: "data-management-coding",
    description:
      "MedDRA / WHODrug coding, query management, and data lock workflows.",
    persona_visibility: ["data_manager", "sponsor_designer", "super_admin"],
    parent_id: null,
    is_deleted: false,
    created_at: "2026-01-15T08:00:00Z",
    created_by: "system_admin",
  },
];

/**
 * Default fallback clinical SOPs for offline sandbox demo mode.
 */
export const DEFAULT_CONTEXTUAL_SOPS = {
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

### Regulatory Governance
* **21 CFR Part 11**: Electronic Records and Electronic Signatures.
* **ICH GCP E6(R3) Section 4.9**: Data Collection and Maintenance.
* **FDA Guidance for Industry**: Electronic Source Data in Clinical Investigations.

### Subject Enrollment and Visit Entry
1. **Informed Consent Verification**: Prior to performing any protocol-specific procedure, ensure the subject has signed the active IRB/IEC-approved eConsent version.
2. **Subject ID Assignment**: Navigate to \`/ecrf\` and click **Enroll Subject**. The system automatically generates a cryptographically bound subject identifier (\`SUBJ-XXXX\`).
3. **Screening & Baseline Visit Data**: Complete all mandated Schedule of Activities assessments within 24 hours of clinical evaluation.
4. **Discrepancy Queries**: Respond to automated or manual data queries with clear clinical explanations and GxP change justifications.`,
      body_html: `<h2>Purpose & Scope</h2>
<p>This Standard Operating Procedure (SOP) governs all investigator site personnel executing electronic Case Report Form (eCRF) data capture, subject registration, and visit assessment recording within the Cadence Clinical Research Platform.</p>
<h3>Regulatory Governance</h3>
<ul>
<li><strong>21 CFR Part 11</strong>: Electronic Records and Electronic Signatures.</li>
<li><strong>ICH GCP E6(R3) Section 4.9</strong>: Data Collection and Maintenance.</li>
<li><strong>FDA Guidance for Industry</strong>: Electronic Source Data in Clinical Investigations.</li>
</ul>
<h3 id="subject-enrollment-and-visit-entry">Subject Enrollment and Visit Entry</h3>
<ol>
<li><strong>Informed Consent Verification</strong>: Prior to performing any protocol-specific procedure, ensure the subject has signed the active IRB/IEC-approved eConsent version.</li>
<li><strong>Subject ID Assignment</strong>: Navigate to <code>/ecrf</code> and click <strong>Enroll Subject</strong>. The system automatically generates a cryptographically bound subject identifier (<code>SUBJ-XXXX</code>).</li>
<li><strong>Screening & Baseline Visit Data</strong>: Complete all mandated Schedule of Activities assessments within 24 hours of clinical evaluation.</li>
<li><strong>Discrepancy Queries</strong>: Respond to automated or manual data queries with clear clinical explanations and GxP change justifications.</li>
</ol>`,
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
    related_articles: [
      {
        id: "art-ecrf-queries",
        title: "SOP-ECRF-205: Data Query Resolution & Re-Query Workflow",
        slug: "sop-ecrf-205-query-resolution",
        status: "PUBLISHED",
        version_index: 1,
        version_label: "1.2",
        category_id: "cat-clinical-ops",
        category_name: "Clinical Operations",
        author_user_id: "data_manager",
        tags: ["queries", "discrepancies", "site_crc"],
        body_markdown:
          "## Query Resolution\n\nGuidelines for resolving automated and manual discrepancies within 5 business days with GxP change justifications.",
        body_html:
          "<h2>Query Resolution</h2><p>Guidelines for resolving automated and manual discrepancies within 5 business days with GxP change justifications.</p>",
      },
      {
        id: "art-ecrf-adverse-events",
        title: "SOP-SAF-301: Serious Adverse Event (SAE) Expedited Reporting",
        slug: "sop-saf-301-sae-reporting",
        status: "PUBLISHED",
        version_index: 3,
        version_label: "3.0",
        category_id: "cat-clinical-ops",
        category_name: "Clinical Operations",
        author_user_id: "safety_physician",
        tags: ["safety", "sae", "expedited-reporting"],
        body_markdown:
          "## SAE Reporting\n\nReport all Serious Adverse Events (SAEs) to Sponsor Safety within 24 hours of awareness.",
        body_html:
          "<h2>SAE Reporting</h2><p>Report all Serious Adverse Events (SAEs) to Sponsor Safety within 24 hours of awareness.</p>",
      },
    ],
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
      body_markdown: `## Protocol Metadata Management & SoA Design
This guidance specifies CDISC USDM v3.0 standard authoring for study encounters, planned activities, and biomedical concepts.

### Schedule of Activities Matrix Authoring
1. **Arms and Epochs**: Define treatment epochs (Screening, Treatment, Follow-up) and randomized arms.
2. **Encounters & Timing**: Specify visit windows and target timing relative to Cycle 1 Day 1.
3. **Activity Linking**: Link biomedical concepts and CDISC C-Code terminology to each activity cell.
4. **Draft Upversioning**: Any alteration to an approved protocol generates a major amendment draft with automatic diff calculation.`,
      body_html: `<h2>Protocol Metadata Management & SoA Design</h2>
<p>This guidance specifies CDISC USDM v3.0 standard authoring for study encounters, planned activities, and biomedical concepts.</p>
<h3 id="schedule-of-activities-matrix-authoring">Schedule of Activities Matrix Authoring</h3>
<ol>
<li><strong>Arms and Epochs</strong>: Define treatment epochs (Screening, Treatment, Follow-up) and randomized arms.</li>
<li><strong>Encounters & Timing</strong>: Specify visit windows and target timing relative to Cycle 1 Day 1.</li>
<li><strong>Activity Linking</strong>: Link biomedical concepts and CDISC C-Code terminology to each activity cell.</li>
<li><strong>Draft Upversioning</strong>: Any alteration to an approved protocol generates a major amendment draft with automatic diff calculation.</li>
</ol>`,
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
    related_articles: [
      {
        id: "art-mdr-digitization",
        title: "SOP-MDR-108: AI-Assisted Protocol Digitization & Extraction",
        slug: "sop-mdr-108-ai-digitization",
        status: "PUBLISHED",
        version_index: 1,
        version_label: "1.0",
        category_id: "cat-study-authoring",
        category_name: "Study Authoring & MDR",
        author_user_id: "sponsor_study_lead",
        tags: ["digitizer", "ai", "protocol-pdf"],
        body_markdown:
          "## Protocol Digitization\n\nAI ingestion and extraction of protocol PDFs into structured CDISC USDM data objects.",
        body_html:
          "<h2>Protocol Digitization</h2><p>AI ingestion and extraction of protocol PDFs into structured CDISC USDM data objects.</p>",
      },
    ],
  },
  "/ctms": {
    matched_mapping: {
      id: "map-ctms-01",
      route_pattern: "/ctms/*",
      persona: "cra_monitor",
      article_id: "art-ctms-sop",
      section_anchor: "#monitoring-visit-and-issue-management",
      priority: 10,
      is_active: true,
    },
    primary_article: {
      id: "art-ctms-sop",
      title: "SOP-CTMS-302: Clinical Monitoring Visit & Query Management",
      slug: "sop-ctms-302-monitoring-operations",
      status: "PUBLISHED",
      version_index: 1,
      version_label: "1.4",
      tags: ["ctms", "monitoring", "cra_monitor", "site-visits"],
      category_id: "cat-site-monitoring",
      category_name: "Site Monitoring & Operations",
      author_user_id: "cra_manager",
      last_edited_by: "cra_manager",
      approved_by: "qa_director",
      created_at: "2026-03-05T08:30:00Z",
      updated_at: "2026-04-12T16:00:00Z",
      body_markdown: `## Clinical Monitoring Operations
Guidelines for Clinical Research Associates (CRAs) conducting site initiation, routine interim monitoring, and close-out visits.

### Monitoring Visit and Issue Management
1. **Source Data Verification (SDV)**: Verify 100% of primary endpoint and safety critical data points against raw medical records.
2. **Protocol Deviation Logging**: Any observed deviation must be logged in the Clinical Issue Hub within 48 hours.
3. **Monitoring Visit Report (MVR)**: Finalize MVR and issue action items within 10 business days.`,
      body_html: `<h2>Clinical Monitoring Operations</h2>
<p>Guidelines for Clinical Research Associates (CRAs) conducting site initiation, routine interim monitoring, and close-out visits.</p>
<h3 id="monitoring-visit-and-issue-management">Monitoring Visit and Issue Management</h3>
<ol>
<li><strong>Source Data Verification (SDV)</strong>: Verify 100% of primary endpoint and safety critical data points against raw medical records.</li>
<li><strong>Protocol Deviation Logging</strong>: Any observed deviation must be logged in the Clinical Issue Hub within 48 hours.</li>
<li><strong>Monitoring Visit Report (MVR)</strong>: Finalize MVR and issue action items within 10 business days.</li>
</ol>`,
    },
    primary_version: {
      id: "ver-ctms-302",
      version_index: 1,
      version_label: "1.4",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "cra_manager",
      reason_for_change: "Risk-based monitoring (RBM) methodology update",
      created_at: "2026-04-12T16:00:00Z",
    },
    section_anchor: "#monitoring-visit-and-issue-management",
    related_articles: [
      {
        id: "art-ctms-trips",
        title: "SOP-CTMS-305: Trip Report Review & Sponsor Sign-Off",
        slug: "sop-ctms-305-trip-reports",
        status: "PUBLISHED",
        version_index: 2,
        version_label: "2.0",
        category_id: "cat-site-monitoring",
        category_name: "Site Monitoring & Operations",
        author_user_id: "cra_manager",
        tags: ["mvr", "trip-reports", "sponsor-signoff"],
        body_markdown:
          "## Trip Reports\n\nProcedures for sponsor QA review and dual-electronic sign-off of monitoring visit reports.",
        body_html:
          "<h2>Trip Reports</h2><p>Procedures for sponsor QA review and dual-electronic sign-off of monitoring visit reports.</p>",
      },
    ],
  },
  "/audit": {
    matched_mapping: {
      id: "map-audit-01",
      route_pattern: "/audit/*",
      persona: "auditor",
      article_id: "art-audit-sop",
      section_anchor: "#regulatory-inspection-and-ledger-verification",
      priority: 10,
      is_active: true,
    },
    primary_article: {
      id: "art-audit-sop",
      title:
        "SOP-AUD-401: 21 CFR Part 11 Audit Trail Verification & Inspection",
      slug: "sop-aud-401-audit-trail-inspection",
      status: "PUBLISHED",
      version_index: 2,
      version_label: "2.0",
      tags: ["audit", "part11", "inspection-readiness", "cryptographic-ledger"],
      category_id: "cat-regulatory-compliance",
      category_name: "Regulatory & Quality Compliance",
      author_user_id: "qa_director",
      last_edited_by: "qa_director",
      approved_by: "super_admin",
      created_at: "2026-01-10T12:00:00Z",
      updated_at: "2026-05-02T10:00:00Z",
      body_markdown: `## Regulatory Audit Trail Inspection
Instructions for GxP auditors, QA leads, and regulatory inspectors reviewing computer system audit logs.

### Regulatory Inspection and Ledger Verification
1. **Append-Only Tamper Evident Log**: Ensure all ledger transactions contain timestamp, actor, tenant, SHA-256 block hash, and reason for change.
2. **Four-Eyes Verification**: Confirm that all controlled document approvals and protocol amendments satisfy independent secondary review.
3. **Data Export Controls**: Inspect regulatory JSON/CSV export packages for full cryptographic hash verification.`,
      body_html: `<h2>Regulatory Audit Trail Inspection</h2>
<p>Instructions for GxP auditors, QA leads, and regulatory inspectors reviewing computer system audit logs.</p>
<h3 id="regulatory-inspection-and-ledger-verification">Regulatory Inspection and Ledger Verification</h3>
<ol>
<li><strong>Append-Only Tamper Evident Log</strong>: Ensure all ledger transactions contain timestamp, actor, tenant, SHA-256 block hash, and reason for change.</li>
<li><strong>Four-Eyes Verification</strong>: Confirm that all controlled document approvals and protocol amendments satisfy independent secondary review.</li>
<li><strong>Data Export Controls</strong>: Inspect regulatory JSON/CSV export packages for full cryptographic hash verification.</li>
</ol>`,
    },
    primary_version: {
      id: "ver-aud-401",
      version_index: 2,
      version_label: "2.0",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "qa_director",
      reason_for_change: "Inspection readiness checklist update",
      created_at: "2026-05-02T10:00:00Z",
    },
    section_anchor: "#regulatory-inspection-and-ledger-verification",
    related_articles: [
      {
        id: "art-audit-esign",
        title:
          "SOP-AUD-405: Electronic Signature Verification & PKCS#7 Handshake",
        slug: "sop-aud-405-esign-verification",
        status: "PUBLISHED",
        version_index: 1,
        version_label: "1.1",
        category_id: "cat-regulatory-compliance",
        category_name: "Regulatory & Quality Compliance",
        author_user_id: "qa_director",
        tags: ["esign", "pkcs7", "cryptography"],
        body_markdown:
          "## Electronic Signatures\n\nVerifying cryptographic digital signatures and PKCS#7 certificates.",
        body_html:
          "<h2>Electronic Signatures</h2><p>Verifying cryptographic digital signatures and PKCS#7 certificates.</p>",
      },
    ],
  },
};

/**
 * All default mock articles across categories for offline demo mode.
 */
export const DEFAULT_MOCK_ARTICLES = [
  DEFAULT_CONTEXTUAL_SOPS["/ecrf"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/ecrf"].related_articles[0],
  DEFAULT_CONTEXTUAL_SOPS["/ecrf"].related_articles[1],
  DEFAULT_CONTEXTUAL_SOPS["/mdr"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/mdr"].related_articles[0],
  DEFAULT_CONTEXTUAL_SOPS["/ctms"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/ctms"].related_articles[0],
  DEFAULT_CONTEXTUAL_SOPS["/audit"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/audit"].related_articles[0],
  {
    id: "art-dm-coding",
    title: "SOP-DM-501: MedDRA & WHODrug Medical Coding Workflow",
    slug: "sop-dm-501-medical-coding",
    status: "IN_REVIEW",
    version_index: 1,
    version_label: "1.0",
    tags: ["coding", "meddra", "whodrug", "data_manager"],
    category_id: "cat-data-management",
    category_name: "Data Management & Coding",
    author_user_id: "lead_data_manager",
    last_edited_by: "lead_data_manager",
    approved_by: null,
    created_at: "2026-07-01T09:00:00Z",
    updated_at: "2026-07-01T09:00:00Z",
    body_markdown: `## MedDRA and WHODrug Coding Operations
Standard process for automated dictionary search and medical coder manual review of adverse event terms and concomitant medications.

### Coder Responsibilities
1. **Auto-Match Review**: Verify high-confidence auto-encoded terms.
2. **Ambiguous Term Queries**: Issue clarification queries to investigator site within 48 hours for ambiguous verbatims.`,
    body_html: `<h2>MedDRA and WHODrug Coding Operations</h2>
<p>Standard process for automated dictionary search and medical coder manual review of adverse event terms and concomitant medications.</p>
<h3>Coder Responsibilities</h3>
<ol>
<li><strong>Auto-Match Review</strong>: Verify high-confidence auto-encoded terms.</li>
<li><strong>Ambiguous Term Queries</strong>: Issue clarification queries to investigator site within 48 hours for ambiguous verbatims.</li>
</ol>`,
  },
  {
    id: "art-draft-amendment",
    title:
      "SOP-MDR-112: Protocol Amendment Impact Assessment & Version Upgrading",
    slug: "sop-mdr-112-amendment-assessment",
    status: "DRAFT",
    version_index: 1,
    version_label: "0.1",
    tags: ["amendment", "versioning", "diff", "usdm"],
    category_id: "cat-study-authoring",
    category_name: "Study Authoring & MDR",
    author_user_id: "sponsor_study_lead",
    last_edited_by: "sponsor_study_lead",
    approved_by: null,
    created_at: "2026-08-01T14:00:00Z",
    updated_at: "2026-08-01T14:00:00Z",
    body_markdown: `## Protocol Amendment Impact Assessment
Draft guidelines for evaluating down-stream eCRF schema impact when study designs undergo minor or major amendments.`,
    body_html: `<h2>Protocol Amendment Impact Assessment</h2>
<p>Draft guidelines for evaluating down-stream eCRF schema impact when study designs undergo minor or major amendments.</p>`,
  },
];

// In-memory state storage for offline mutations
let memoryArticles = JSON.parse(JSON.stringify(DEFAULT_MOCK_ARTICLES));
let memoryCategories = JSON.parse(JSON.stringify(DEFAULT_CATEGORIES));
const memoryVersions = new Map();
const memoryAuditLogs = new Map();

/**
 * Resolves fallback mock contextual help when backend is unavailable.
 */
export function getFallbackContextualHelp(route, _persona) {
  const cleanRoute = (route || "/").split("?")[0].replace(/\/+$/, "") || "/";

  // Check direct route prefix match
  for (const [key, fallback] of Object.entries(DEFAULT_CONTEXTUAL_SOPS)) {
    if (cleanRoute === key || cleanRoute.startsWith(`${key}/`)) {
      return JSON.parse(JSON.stringify(fallback));
    }
  }

  // Fallback to default eCRF SOP if on clinical screen
  if (
    cleanRoute.includes("subject") ||
    cleanRoute.includes("form") ||
    cleanRoute.includes("visit")
  ) {
    return JSON.parse(JSON.stringify(DEFAULT_CONTEXTUAL_SOPS["/ecrf"]));
  }

  // Generic empty response
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

export const knowledgeService = {
  /**
   * Resolves in-page contextual SOP guidance for a given route and persona.
   *
   * @param {Object} params
   * @param {string} params.route - In-app route path (e.g. '/ecrf', '/mdr')
   * @param {string} [params.persona] - Active user persona (e.g. 'site_crc')
   * @returns {Promise<Object>} ContextualHelpResolutionResponse
   */
  async resolveContextualHelp({ route, persona } = {}) {
    const encodedRoute = encodeURIComponent(route || "/");
    const personaQuery = persona
      ? `&persona=${encodeURIComponent(persona)}`
      : "";
    const path = `/api/v1/knowledge/contextual-help?route=${encodedRoute}${personaQuery}`;

    try {
      const response = await apiClient.get(path);
      if (response && (response.primary_article || response.article)) {
        return response;
      }
      return getFallbackContextualHelp(route, persona);
    } catch {
      return getFallbackContextualHelp(route, persona);
    }
  },

  /**
   * Retrieves a single KnowledgeArticle by ID along with active body content.
   *
   * @param {string} articleId
   * @returns {Promise<Object>} ArticleResponse
   */
  async getArticle(articleId) {
    try {
      return await apiClient.get(`/api/v1/knowledge/articles/${articleId}`);
    } catch {
      const found = memoryArticles.find(
        (a) => a.id === articleId || a.slug === articleId
      );
      if (found) {
        return JSON.parse(JSON.stringify(found));
      }
      throw new Error(`Article with id ${articleId} not found`);
    }
  },

  /**
   * Lists published articles with optional filters.
   *
   * @param {Object} [params]
   * @param {string} [params.status]
   * @param {string} [params.categoryId]
   * @returns {Promise<Array>} List of ArticleResponse
   */
  async listArticles(params = {}) {
    const query = new URLSearchParams();
    if (params.status) query.append("status_filter", params.status);
    if (params.categoryId) query.append("category_id", params.categoryId);
    const qs = query.toString() ? `?${query.toString()}` : "";

    try {
      const resp = await apiClient.get(`/api/v1/knowledge/articles${qs}`);
      if (Array.isArray(resp)) return resp;
      return memoryArticles;
    } catch {
      let filtered = [...memoryArticles];
      if (params.status && params.status !== "ALL") {
        filtered = filtered.filter((a) => a.status === params.status);
      }
      if (params.categoryId) {
        filtered = filtered.filter((a) => a.category_id === params.categoryId);
      }
      return JSON.parse(JSON.stringify(filtered));
    }
  },

  /**
   * Lists active categories.
   *
   * @returns {Promise<Array>} List of CategoryResponse
   */
  async listCategories() {
    try {
      const resp = await apiClient.get("/api/v1/knowledge/categories");
      if (Array.isArray(resp)) return resp;
      return memoryCategories;
    } catch {
      return JSON.parse(
        JSON.stringify(memoryCategories.filter((c) => !c.is_deleted))
      );
    }
  },

  /**
   * Creates a new KnowledgeCategory.
   *
   * @param {Object} payload
   * @returns {Promise<Object>} CategoryResponse
   */
  async createCategory(payload) {
    try {
      return await apiClient.post("/api/v1/knowledge/categories", payload);
    } catch {
      const newCat = {
        id: `cat-${Date.now()}`,
        name: payload.name,
        slug:
          payload.slug ||
          payload.name.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
        description: payload.description || "",
        persona_visibility: payload.persona_visibility || [],
        parent_id: payload.parent_id || null,
        is_deleted: false,
        created_at: new Date().toISOString(),
        created_by: "current_user",
      };
      memoryCategories.push(newCat);
      return newCat;
    }
  },

  /**
   * Deletes a KnowledgeCategory.
   *
   * @param {string} categoryId
   * @param {string} [reason]
   * @returns {Promise<Object>}
   */
  async deleteCategory(categoryId, reason = "Soft deleted") {
    try {
      return await apiClient.delete(
        `/api/v1/knowledge/categories/${categoryId}`,
        {
          changeReason: reason,
        }
      );
    } catch {
      const idx = memoryCategories.findIndex((c) => c.id === categoryId);
      if (idx !== -1) {
        memoryCategories[idx].is_deleted = true;
      }
      return { success: true, id: categoryId };
    }
  },

  /**
   * Creates a new KnowledgeArticle in DRAFT status.
   *
   * @param {Object} payload
   * @returns {Promise<Object>} ArticleResponse
   */
  async createArticle(payload) {
    try {
      return await apiClient.post("/api/v1/knowledge/articles", payload);
    } catch {
      const category = memoryCategories.find(
        (c) => c.id === payload.category_id
      );
      const newArt = {
        id: `art-${Date.now()}`,
        title: payload.title,
        slug:
          payload.slug ||
          payload.title.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
        status: "DRAFT",
        version_index: 1,
        version_label: payload.version_label || "0.1",
        category_id: payload.category_id,
        category_name: category ? category.name : "General Guidance",
        tags: Array.isArray(payload.tags)
          ? payload.tags
          : payload.tags
            ? payload.tags.split(",").map((t) => t.trim())
            : [],
        author_user_id: "current_user",
        last_edited_by: "current_user",
        approved_by: null,
        created_at: new Date().toISOString(),
        updated_at: new Date().toISOString(),
        body_markdown: payload.body_markdown || "",
        body_html: `<p>${payload.body_markdown || ""}</p>`,
      };
      memoryArticles.unshift(newArt);
      return newArt;
    }
  },

  /**
   * Updates an existing article working draft.
   *
   * @param {string} articleId
   * @param {Object} payload
   * @returns {Promise<Object>} ArticleResponse
   */
  async updateArticleDraft(articleId, payload) {
    try {
      return await apiClient.put(
        `/api/v1/knowledge/articles/${articleId}`,
        payload
      );
    } catch {
      const art = memoryArticles.find((a) => a.id === articleId);
      if (!art) throw new Error("Article not found");
      if (payload.title) art.title = payload.title;
      if (payload.slug) art.slug = payload.slug;
      if (payload.category_id) {
        art.category_id = payload.category_id;
        const cat = memoryCategories.find((c) => c.id === payload.category_id);
        if (cat) art.category_name = cat.name;
      }
      if (payload.tags) art.tags = payload.tags;
      if (payload.body_markdown !== undefined) {
        art.body_markdown = payload.body_markdown;
        art.body_html = `<p>${payload.body_markdown}</p>`;
      }
      art.last_edited_by = "current_user";
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  /**
   * Submits a DRAFT article for peer review (DRAFT -> IN_REVIEW).
   *
   * @param {string} articleId
   * @param {Object} [payload]
   * @returns {Promise<Object>} ArticleResponse
   */
  async submitArticleReview(articleId, payload = {}) {
    try {
      return await apiClient.post(
        `/api/v1/knowledge/articles/${articleId}/submit-review`,
        payload
      );
    } catch {
      const art = memoryArticles.find((a) => a.id === articleId);
      if (!art) throw new Error("Article not found");
      art.status = "IN_REVIEW";
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  /**
   * Approves an article in IN_REVIEW status (IN_REVIEW -> APPROVED).
   *
   * @param {string} articleId
   * @param {Object} payload
   * @returns {Promise<Object>} ArticleResponse
   */
  async approveArticle(articleId, payload = {}) {
    try {
      return await apiClient.post(
        `/api/v1/knowledge/articles/${articleId}/approve`,
        payload
      );
    } catch {
      const art = memoryArticles.find((a) => a.id === articleId);
      if (!art) throw new Error("Article not found");
      art.status = "APPROVED";
      art.approved_by = "current_reviewer";
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  /**
   * Rejects an article in IN_REVIEW status (IN_REVIEW -> REJECTED).
   *
   * @param {string} articleId
   * @param {Object} [payload]
   * @returns {Promise<Object>} ArticleResponse
   */
  async rejectArticle(articleId, payload = {}) {
    try {
      return await apiClient.post(
        `/api/v1/knowledge/articles/${articleId}/reject`,
        payload
      );
    } catch {
      const art = memoryArticles.find((a) => a.id === articleId);
      if (!art) throw new Error("Article not found");
      art.status = "REJECTED";
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  /**
   * Publishes an APPROVED article (APPROVED -> PUBLISHED).
   *
   * @param {string} articleId
   * @param {Object} payload
   * @returns {Promise<Object>} ArticleResponse
   */
  async publishArticle(articleId, payload = {}) {
    try {
      return await apiClient.post(
        `/api/v1/knowledge/articles/${articleId}/publish`,
        payload
      );
    } catch {
      const art = memoryArticles.find((a) => a.id === articleId);
      if (!art) throw new Error("Article not found");
      art.status = "PUBLISHED";
      if (payload.version_label) art.version_label = payload.version_label;
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  /**
   * Transitions an article through any state machine event.
   *
   * @param {string} articleId
   * @param {Object} payload
   * @returns {Promise<Object>}
   */
  async transitionArticle(articleId, payload) {
    try {
      return await apiClient.post(
        `/api/v1/knowledge/articles/${articleId}/transition`,
        payload
      );
    } catch {
      const art = memoryArticles.find((a) => a.id === articleId);
      if (!art) throw new Error("Article not found");
      art.status = payload.target_status;
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  /**
   * Lists immutable version snapshots for an article.
   *
   * @param {string} articleId
   * @returns {Promise<Array>} List of ArticleVersionResponse
   */
  async listArticleVersions(articleId) {
    try {
      return await apiClient.get(
        `/api/v1/knowledge/articles/${articleId}/versions`
      );
    } catch {
      const art = memoryArticles.find((a) => a.id === articleId);
      if (!art) return [];
      const cached = memoryVersions.get(articleId);
      if (cached) return cached;
      return [
        {
          id: `ver-${art.id}-1`,
          article_id: art.id,
          version_index: 1,
          version_label: "1.0",
          status_at_snapshot: "PUBLISHED",
          body_markdown: art.body_markdown,
          body_html: art.body_html,
          is_locked: true,
          created_by: art.author_user_id,
          created_at: art.created_at,
          reason_for_change: "Initial baseline SOP creation",
        },
        ...(art.version_index > 1
          ? [
              {
                id: `ver-${art.id}-${art.version_index}`,
                article_id: art.id,
                version_index: art.version_index,
                version_label: art.version_label || `${art.version_index}.0`,
                status_at_snapshot: art.status,
                body_markdown: art.body_markdown,
                body_html: art.body_html,
                is_locked:
                  art.status === "PUBLISHED" || art.status === "APPROVED",
                created_by: art.last_edited_by || art.author_user_id,
                created_at: art.updated_at,
                reason_for_change:
                  "Annual periodic review & protocol amendment alignment",
              },
            ]
          : []),
      ];
    }
  },

  /**
   * Returns immutable audit trail logs for an article.
   *
   * @param {string} articleId
   * @returns {Promise<Array>} List of AuditLogResponse
   */
  async getArticleAuditLog(articleId) {
    try {
      return await apiClient.get(
        `/api/v1/knowledge/articles/${articleId}/audit-log`
      );
    } catch {
      const art = memoryArticles.find((a) => a.id === articleId);
      if (!art) return [];
      const cached = memoryAuditLogs.get(articleId);
      if (cached) return cached;
      return [
        {
          id: `log-${art.id}-1`,
          article_id: art.id,
          event_type: "ARTICLE_CREATED",
          actor_user_id: art.author_user_id,
          target_status: "DRAFT",
          version_index: 1,
          version_label: "0.1",
          reason_for_change: "Initial GxP document authoring initiated",
          sha256_hash:
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
          created_at: art.created_at,
        },
        {
          id: `log-${art.id}-2`,
          article_id: art.id,
          event_type: "ARTICLE_PUBLISHED",
          actor_user_id: art.approved_by || "qa_director",
          target_status: "PUBLISHED",
          version_index: art.version_index,
          version_label: art.version_label,
          reason_for_change: "Formal QA and regulatory approval sign-off",
          sha256_hash:
            "8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa4",
          created_at: art.updated_at,
        },
      ];
    }
  },

  /**
   * Searches knowledge articles by keyword or tag.
   *
   * @param {string} query
   * @returns {Promise<Array>}
   */
  async searchArticles(query) {
    const q = (query || "").trim().toLowerCase();
    if (!q) return this.listArticles();
    const all = await this.listArticles();
    return all.filter((art) => {
      const matchTitle = art.title?.toLowerCase().includes(q);
      const matchBody = (art.body_markdown || art.body_html || "")
        .toLowerCase()
        .includes(q);
      const matchTags = Array.isArray(art.tags)
        ? art.tags.some((t) => t.toLowerCase().includes(q))
        : typeof art.tags === "string"
          ? art.tags.toLowerCase().includes(q)
          : false;
      const matchCategory = art.category_name?.toLowerCase().includes(q);
      return matchTitle || matchBody || matchTags || matchCategory;
    });
  },
};
