import { apiClient } from "./apiClient.js";

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
    },
    section_anchor: "#subject-enrollment-and-visit-entry",
    related_articles: [
      {
        id: "art-ecrf-queries",
        title: "SOP-ECRF-205: Data Query Resolution & Re-Query Workflow",
        slug: "sop-ecrf-205-query-resolution",
        status: "PUBLISHED",
        version_label: "1.2",
        tags: ["queries", "discrepancies", "site_crc"],
        body_markdown: "# Query Resolution\n\nGuidelines for resolving queries within 5 business days.",
        body_html: "<h1>Query Resolution</h1><p>Guidelines for resolving queries within 5 business days.</p>",
      },
      {
        id: "art-ecrf-adverse-events",
        title: "SOP-SAF-301: Serious Adverse Event (SAE) Expedited Reporting",
        slug: "sop-saf-301-sae-reporting",
        status: "PUBLISHED",
        version_label: "3.0",
        tags: ["safety", "sae", "expedited-reporting"],
        body_markdown: "# SAE Reporting\n\nReport all SAEs to Sponsor Safety within 24 hours of awareness.",
        body_html: "<h1>SAE Reporting</h1><p>Report all SAEs to Sponsor Safety within 24 hours of awareness.</p>",
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
    },
    section_anchor: "#schedule-of-activities-matrix-authoring",
    related_articles: [
      {
        id: "art-mdr-digitization",
        title: "SOP-MDR-108: AI-Assisted Protocol Digitization & Extraction",
        slug: "sop-mdr-108-ai-digitization",
        status: "PUBLISHED",
        version_label: "1.0",
        tags: ["digitizer", "ai", "protocol-pdf"],
        body_markdown: "# Protocol Digitization\n\nAI ingestion and extraction of protocol PDFs.",
        body_html: "<h1>Protocol Digitization</h1><p>AI ingestion and extraction of protocol PDFs.</p>",
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
    },
    section_anchor: "#monitoring-visit-and-issue-management",
    related_articles: [
      {
        id: "art-ctms-trips",
        title: "SOP-CTMS-305: Trip Report Review & Sponsor Sign-Off",
        slug: "sop-ctms-305-trip-reports",
        status: "PUBLISHED",
        version_label: "2.0",
        tags: ["mvr", "trip-reports", "sponsor-signoff"],
        body_markdown: "# Trip Reports\n\nProcedures for sponsor QA review of trip reports.",
        body_html: "<h1>Trip Reports</h1><p>Procedures for sponsor QA review of trip reports.</p>",
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
      title: "SOP-AUD-401: 21 CFR Part 11 Audit Trail Verification & Inspection",
      slug: "sop-aud-401-audit-trail-inspection",
      status: "PUBLISHED",
      version_index: 2,
      version_label: "2.0",
      tags: ["audit", "part11", "inspection-readiness", "cryptographic-ledger"],
      category_id: "cat-regulatory-compliance",
      category_name: "Regulatory & Quality Compliance",
      author_user_id: "qa_director",
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
    },
    section_anchor: "#regulatory-inspection-and-ledger-verification",
    related_articles: [
      {
        id: "art-audit-esign",
        title: "SOP-AUD-405: Electronic Signature Verification & PKCS#7 Handshake",
        slug: "sop-aud-405-esign-verification",
        status: "PUBLISHED",
        version_label: "1.1",
        tags: ["esign", "pkcs7", "cryptography"],
        body_markdown: "# Electronic Signatures\n\nVerifying cryptographic digital signatures.",
        body_html: "<h1>Electronic Signatures</h1><p>Verifying cryptographic digital signatures.</p>",
      },
    ],
  },
};

/**
 * Resolves fallback mock contextual help when backend is unavailable.
 */
export function getFallbackContextualHelp(route, persona) {
  const cleanRoute = (route || "/").split("?")[0].replace(/\/+$/, "") || "/";

  // Check direct route prefix match
  for (const [key, fallback] of Object.entries(DEFAULT_CONTEXTUAL_SOPS)) {
    if (cleanRoute === key || cleanRoute.startsWith(`${key}/`)) {
      return JSON.parse(JSON.stringify(fallback));
    }
  }

  // Fallback to default eCRF SOP if on clinical screen
  if (cleanRoute.includes("subject") || cleanRoute.includes("form") || cleanRoute.includes("visit")) {
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
    const personaQuery = persona ? `&persona=${encodeURIComponent(persona)}` : "";
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
    return await apiClient.get(`/api/v1/knowledge/articles/${articleId}`);
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
    return await apiClient.get(`/api/v1/knowledge/articles${qs}`);
  },

  /**
   * Lists active categories.
   *
   * @returns {Promise<Array>} List of CategoryResponse
   */
  async listCategories() {
    return await apiClient.get("/api/v1/knowledge/categories");
  },
};
