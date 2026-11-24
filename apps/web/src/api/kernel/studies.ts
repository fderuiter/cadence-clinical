import { unwrapResponse } from "../client";
import {
  kernelClient,
  type KernelLineageWindow,
  type KernelSchemas,
} from "./shared";

const loadAuthoring = () => import("./protocolImport");

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
export type KernelSimulationResult =
  KernelSchemas["Kernel_KernelSimulationResponse"];
export type KernelMatrixGapFinding =
  KernelSchemas["Kernel_KernelMatrixGapFindingResponse"];
export type KernelScheduleChange =
  KernelSchemas["Kernel_KernelScheduleChangeResponse"];
export type KernelPlannedVisit =
  KernelSchemas["Kernel_KernelPlannedVisitResponse"];
export type KernelMaskedPart = KernelSchemas["Kernel_KernelMaskedPartResponse"];
export type KernelSoaAmendmentDiff =
  KernelSchemas["Kernel_KernelSoaAmendmentDiffResponse"];
export type KernelSoaDirective =
  KernelSchemas["Kernel_KernelSoaDirectiveResponse"];
export type KernelSoaVisit = KernelSchemas["Kernel_KernelSoaVisitResponse"];
export type KernelStudySummary =
  KernelSchemas["Kernel_KernelStudySummaryResponse"];
export type KernelOrganizationSummary =
  KernelSchemas["Kernel_KernelOrganizationSummaryResponse"];
export type KernelSiteSummary =
  KernelSchemas["Kernel_KernelSiteSummaryResponse"];
export type KernelStudySiteSummary =
  KernelSchemas["Kernel_KernelStudySiteSummaryResponse"] & {
    mask?: string | null;
  };
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
  siteCode?: string;
  code?: string;
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
  institutionRef?: string | null;
  institutionCode?: string | null;
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
  async listOrganizations(kind?: string): Promise<KernelOrganizationSummary[]> {
    const { executeListOrganizations } = await loadAuthoring();
    return executeListOrganizations(kind);
  },

  /** Registers an organization such as a SiteInstitution with a signed reason and idempotency key (POST /api/v1/kernel/organizations). */
  async createOrganization(
    input: KernelCreateOrganizationInput
  ): Promise<KernelOrganizationSummary> {
    const { executeCreateOrganization } = await loadAuthoring();
    return executeCreateOrganization(input);
  },

  /** Registered investigational sites in the caller's tenant, ordered by site code (GET /api/v1/kernel/sites). */
  async listSites(): Promise<KernelSiteSummary[]> {
    const { executeListSites } = await loadAuthoring();
    return executeListSites();
  },

  /** Registers an investigational site with a 2-letter ISO country code, signed reason, and idempotency key (POST /api/v1/kernel/sites). */
  async createSite(input: KernelCreateSiteInput): Promise<KernelSiteSummary> {
    const { executeCreateSite } = await loadAuthoring();
    return executeCreateSite(input);
  },

  /** Sites linked to a study in the caller's tenant, ordered by site code (GET /api/v1/kernel/studies/{studyRef}/sites). */
  async listStudySites(studyRef: string): Promise<KernelStudySiteSummary[]> {
    const { executeListStudySites } = await loadAuthoring();
    return executeListStudySites(studyRef);
  },

  /** Links a registered investigational site to a study with a signed reason and idempotency key (POST /api/v1/kernel/studies/{studyRef}/sites). */
  async linkSiteToStudy(
    input: KernelLinkSiteToStudyInput
  ): Promise<KernelStudySiteSummary> {
    const { executeLinkSiteToStudy } = await loadAuthoring();
    return executeLinkSiteToStudy(input);
  },

  /** Registers a new study with a signed reason for change and idempotency key (POST /api/v1/kernel/studies). */
  async createStudy(
    input: KernelCreateStudyRequest
  ): Promise<KernelStudySummary> {
    const { executeCreateStudy } = await loadAuthoring();
    return executeCreateStudy(input);
  },

  /** Bundled CDISC Library eCRF templates available to import into a protocol draft (GET /api/v1/kernel/protocol-templates). */
  async listProtocolTemplates(): Promise<KernelProtocolTemplate[]> {
    const { executeListProtocolTemplates } = await loadAuthoring();
    return executeListProtocolTemplates();
  },

  /** Viewable forms, ordered items, prompts, mandatory flags, and import notes for a protocol version (GET /api/v1/kernel/protocol-versions/{versionRef}/design-summary). */
  async getProtocolDesignSummary(
    versionRef: string
  ): Promise<KernelProtocolDraftSummary> {
    const { executeGetProtocolDesignSummary } = await loadAuthoring();
    return executeGetProtocolDesignSummary(versionRef);
  },

  /**
   * Synchronously imports an ODM v1.3.2 XML file, USDM v4 JSON file, or bundled CDISC Library eCRF templates
   * to create or revise a study's protocol draft (POST /api/v1/kernel/studies/{studyRef}/protocol-imports).
   */
  async importProtocolDraft(
    request: KernelProtocolImportRequest
  ): Promise<KernelProtocolDraftSummary> {
    const { executeProtocolDraftImport } = await loadAuthoring();
    return executeProtocolDraftImport(request);
  },

  /** A study's protocol versions, newest first, without content. */
  async listProtocolVersions(
    studyRef: string
  ): Promise<KernelProtocolVersion[]> {
    const { executeListProtocolVersions } = await loadAuthoring();
    return executeListProtocolVersions(studyRef);
  },

  /** One protocol version, without content. */
  async getProtocolVersion(versionRef: string): Promise<KernelProtocolVersion> {
    const { executeGetProtocolVersion } = await loadAuthoring();
    return executeGetProtocolVersion(versionRef);
  },

  /** A protocol version's Schedule of Activities: planned activities as columns, collection specifications as rows (ADR-2475). */
  async getScheduleOfActivities(
    versionRef: string
  ): Promise<KernelScheduleOfActivities> {
    const { executeGetScheduleOfActivities } = await loadAuthoring();
    return executeGetScheduleOfActivities(versionRef);
  },

  /** What depends on the version a protocol amendment changes (K-PROT-2). A first version amends nothing and is refused. */
  async amendmentImpact(
    versionRef: string,
    window: Omit<KernelLineageWindow, "edgeKinds"> = {}
  ): Promise<KernelAmendmentImpact> {
    const { executeAmendmentImpact } = await loadAuthoring();
    return executeAmendmentImpact(versionRef, window);
  },

  /** Runs evaluate-only in-memory simulation over candidate protocol version, mapping specification, or rule pack. */
  async simulate(request: {
    baseVersionRef?: string;
    storedVersionRef?: string;
    candidateContent?: string;
    validAt?: string;
    knownAt?: string;
    kind?: "ProtocolVersion" | "MappingSpecification" | "RulePack";
    studyRef?: string;
    libraryItemRef?: string;
    baseLibraryItemRef?: string;
    standardVersionRef?: string;
  }): Promise<KernelSimulationResult> {
    const { executeSimulate } = await loadAuthoring();
    return executeSimulate(request);
  },

  /** Performs real-time schedule matrix gap check over a protocol design document. */
  async checkMatrixGaps(
    protocolContent: string
  ): Promise<KernelMatrixGapFinding[]> {
    const { executeCheckMatrixGaps } = await loadAuthoring();
    return executeCheckMatrixGaps(protocolContent);
  },
};
