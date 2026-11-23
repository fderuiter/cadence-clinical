import { encodeChangeReason } from "../changeReason";
import { unwrapResponse } from "../client";
import {
  kernelClient,
  windowQuery,
  type KernelLineageWindow,
  type KernelSchemas,
} from "./shared";

export type KernelProtocolVersion =
  KernelSchemas["Kernel_KernelProtocolVersionResponse"];
export type KernelScheduleOfActivities =
  KernelSchemas["Kernel_KernelScheduleOfActivitiesResponse"];
export type KernelSoaEpoch = KernelSchemas["Kernel_KernelSoaEpochResponse"];
export type KernelSoaColumn = KernelSchemas["Kernel_KernelSoaColumnResponse"];
export type KernelSoaRow = KernelSchemas["Kernel_KernelSoaRowResponse"];
export type KernelSoaFootnote =
  KernelSchemas["Kernel_KernelSoaFootnoteResponse"];
export type KernelScreenEnrollmentResult =
  KernelSchemas["Kernel_KernelEnrollmentStateResponse"];
export type KernelAmendmentImpact =
  KernelSchemas["Kernel_KernelAmendmentImpactResponse"];
export type KernelSimulationResult = KernelSchemas["Kernel_SimulationResult"];
export type KernelMatrixGapFinding = KernelSchemas["Kernel_MatrixGapFinding"];
export type KernelStudySummary =
  KernelSchemas["Kernel_KernelStudySummaryResponse"];
export type KernelOrganizationSummary =
  KernelSchemas["Kernel_KernelOrganizationSummaryResponse"];
export type KernelSiteSummary =
  KernelSchemas["Kernel_KernelSiteSummaryResponse"];
export type KernelStudySiteSummary =
  KernelSchemas["Kernel_KernelStudySiteSummaryResponse"];
export type KernelProtocolTemplate =
  KernelSchemas["Kernel_KernelProtocolTemplateResponse"];
export type KernelProtocolDraftSummary =
  KernelSchemas["Kernel_KernelProtocolDraftSummaryResponse"];
export type KernelProtocolFormSummary =
  KernelSchemas["Kernel_KernelProtocolFormSummaryResponse"];
export type KernelProtocolFormItem =
  KernelSchemas["Kernel_KernelProtocolFormItemResponse"];
export type KernelProtocolImportNote =
  KernelSchemas["Kernel_KernelProtocolImportNoteResponse"];
export type KernelImportedRulePackSummary =
  KernelSchemas["Kernel_KernelImportedRulePackSummaryResponse"];
export type KernelProtocolAmendment =
  KernelSchemas["Kernel_KernelProtocolAmendmentResponse"];

export interface KernelCreateStudyRequest {
  code: string;
  title: string;
  phase: string;
  sponsorRef?: string | null;
  sponsorCode?: string | null;
  sponsorName?: string | null;
  linkSandboxSite?: boolean;
  initialSiteRef?: string | null;
  initialSiteCode?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelCreateOrganizationInput {
  name: string;
  code?: string | null;
  kind?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelCreateSiteInput {
  siteCode: string;
  name: string;
  country: string;
  institutionRef?: string | null;
  institutionCode?: string | null;
  institutionName?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelLinkSiteToStudyInput {
  studyRef: string;
  siteRef?: string | null;
  siteCode?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelProtocolImportRequest {
  studyRef: string;
  file?: File | null;
  content?: string | null;
  fileName?: string | null;
  format?: string | null;
  templateIds?: string[];
  replaceForms?: boolean;
  amendmentClassification?: string | null;
  transitionPolicy?: string | null;
  reason: string;
  idempotencyKey: string;
}

export const kernelStudiesApi = {
  /** Visible studies in the caller's tenant, ordered by study code (GET /api/v1/kernel/studies). */
  listStudies(): Promise<KernelStudySummary[]> {
    return unwrapResponse(kernelClient.GET("/api/v1/kernel/studies"));
  },

  /** Registered organizations in the caller's tenant, optionally filtered by kind (GET /api/v1/kernel/organizations). */
  listOrganizations(kind?: string): Promise<KernelOrganizationSummary[]> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/organizations", {
        params: { query: { kind: kind || undefined } },
      })
    );
  },

  /** Registers an organization such as a SiteInstitution with a signed reason and idempotency key (POST /api/v1/kernel/organizations). */
  createOrganization({
    name,
    code,
    kind = "SiteInstitution",
    reason,
    idempotencyKey,
  }: KernelCreateOrganizationInput): Promise<KernelOrganizationSummary> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/organizations", {
        body: {
          name,
          code: code ?? null,
          kind: kind ?? "SiteInstitution",
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },

  /** Registered investigational sites in the caller's tenant, ordered by site code (GET /api/v1/kernel/sites). */
  listSites(): Promise<KernelSiteSummary[]> {
    return unwrapResponse(kernelClient.GET("/api/v1/kernel/sites"));
  },

  /** Registers an investigational site with a 2-letter ISO country code, signed reason, and idempotency key (POST /api/v1/kernel/sites). */
  createSite({
    siteCode,
    name,
    country,
    institutionRef,
    institutionCode,
    institutionName,
    reason,
    idempotencyKey,
  }: KernelCreateSiteInput): Promise<KernelSiteSummary> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/sites", {
        body: {
          site_code: siteCode,
          name,
          country,
          institution_ref: institutionRef ?? null,
          institution_code: institutionCode ?? null,
          institution_name: institutionName ?? null,
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },

  /** Sites linked to a study in the caller's tenant, ordered by site code (GET /api/v1/kernel/studies/{studyRef}/sites). */
  listStudySites(studyRef: string): Promise<KernelStudySiteSummary[]> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/studies/{studyRef}/sites", {
        params: { path: { studyRef } },
      })
    );
  },

  /** Links a registered investigational site to a study with a signed reason and idempotency key (POST /api/v1/kernel/studies/{studyRef}/sites). */
  linkSiteToStudy({
    studyRef,
    siteRef,
    siteCode,
    reason,
    idempotencyKey,
  }: KernelLinkSiteToStudyInput): Promise<KernelStudySiteSummary> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/studies/{studyRef}/sites", {
        params: { path: { studyRef } },
        body: {
          site_ref: siteRef ?? null,
          site_code: siteCode ?? null,
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },

  /** Registers a new study with a signed reason for change and idempotency key (POST /api/v1/kernel/studies). */
  createStudy({
    code,
    title,
    phase,
    sponsorRef,
    sponsorCode,
    sponsorName,
    linkSandboxSite,
    initialSiteRef,
    initialSiteCode,
    reason,
    idempotencyKey,
  }: KernelCreateStudyRequest): Promise<KernelStudySummary> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/studies", {
        body: {
          study_code: code,
          title,
          phase,
          sponsor_ref: sponsorRef ?? null,
          sponsor_code: sponsorCode ?? null,
          sponsor_name: sponsorName ?? null,
          link_sandbox_site: Boolean(linkSandboxSite),
          initial_site_ref: initialSiteRef ?? null,
          initial_site_code: initialSiteCode ?? null,
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },

  /** Bundled CDISC Library eCRF templates available to import into a protocol draft (GET /api/v1/kernel/protocol-templates). */
  listProtocolTemplates(): Promise<KernelProtocolTemplate[]> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/protocol-templates")
    );
  },

  /** Viewable forms, ordered items, prompts, mandatory flags, and import notes for a protocol version (GET /api/v1/kernel/protocol-versions/{versionRef}/design-summary). */
  getProtocolDesignSummary(
    versionRef: string
  ): Promise<KernelProtocolDraftSummary> {
    return unwrapResponse(
      kernelClient.GET(
        "/api/v1/kernel/protocol-versions/{versionRef}/design-summary",
        {
          params: { path: { versionRef } },
        }
      )
    );
  },

  /**
   * Synchronously imports an ODM v1.3.2 XML file, USDM v4 JSON file, or bundled CDISC Library eCRF templates
   * to create or revise a study's protocol draft (POST /api/v1/kernel/studies/{studyRef}/protocol-imports).
   */
  async importProtocolDraft(
    request: KernelProtocolImportRequest
  ): Promise<KernelProtocolDraftSummary> {
    const { executeProtocolDraftImport } = await import("./protocolImport");
    return executeProtocolDraftImport(request);
  },

  /** A study's protocol versions, newest first, without content. */
  listProtocolVersions(studyRef: string): Promise<KernelProtocolVersion[]> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/studies/{studyRef}/protocol-versions", {
        params: { path: { studyRef } },
      })
    );
  },

  /** One protocol version, without content. */
  getProtocolVersion(versionRef: string): Promise<KernelProtocolVersion> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/protocol-versions/{versionRef}", {
        params: { path: { versionRef } },
      })
    );
  },

  /** A protocol version's Schedule of Activities: planned activities as columns, collection specifications as rows (ADR-2475). */
  getScheduleOfActivities(
    versionRef: string
  ): Promise<KernelScheduleOfActivities> {
    return unwrapResponse(
      kernelClient.GET(
        "/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities",
        {
          params: { path: { versionRef } },
        }
      )
    );
  },

  /** What depends on the version a protocol amendment changes (K-PROT-2). A first version amends nothing and is refused. */
  amendmentImpact(
    versionRef: string,
    window: Omit<KernelLineageWindow, "edgeKinds"> = {}
  ): Promise<KernelAmendmentImpact> {
    const { depth, valid_at, known_at } = windowQuery(window);
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/protocol-versions/{versionRef}/impact", {
        params: { path: { versionRef }, query: { depth, valid_at, known_at } },
      })
    );
  },

  /** Runs evaluate-only in-memory simulation over candidate protocol version. */
  simulate(request: {
    baseVersionRef?: string;
    storedVersionRef?: string;
    candidateContent?: string;
    validAt?: string;
    knownAt?: string;
  }): Promise<KernelSimulationResult> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/simulation/simulate", {
        body: {
          base_version_ref: request.baseVersionRef ?? null,
          stored_version_ref: request.storedVersionRef ?? null,
          candidate_content: request.candidateContent ?? null,
          valid_at: request.validAt ?? null,
          known_at: request.knownAt ?? null,
        },
      })
    );
  },

  /** Performs real-time schedule matrix gap check over a protocol design document. */
  checkMatrixGaps(protocolContent: string): Promise<KernelMatrixGapFinding[]> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/protocols/matrix-gap-check", {
        body: { protocol_content: protocolContent },
      })
    );
  },
};
