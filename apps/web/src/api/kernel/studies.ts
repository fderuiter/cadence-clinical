import { unwrapResponse } from "../client";
import { kernelClient, lazyKernelAction, type KernelSchemas } from "./shared";

const loadRouteTransport = () => import("./studies.transport");

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
export type KernelEffectiveVersion =
  KernelSchemas["Kernel_KernelEffectiveVersionResponse"];
export type KernelExpectedVisit =
  KernelSchemas["Kernel_KernelExpectedVisitResponse"];
export type KernelEnrollmentSchedule =
  KernelSchemas["Kernel_KernelEnrollmentScheduleResponse"];
export type KernelTemplateActivityAssignment =
  KernelSchemas["Kernel_KernelTemplateActivityAssignmentRequest"];
export type KernelProtocolAmendment =
  KernelSchemas["Kernel_KernelProtocolAmendmentResponse"];

export interface KernelTemplateAssignmentInput {
  templateId: string;
  activityIds: string[];
}

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
  templateAssignments?: KernelTemplateAssignmentInput[];
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
  listOrganizations: lazyKernelAction(
    loadRouteTransport,
    "executeListOrganizations"
  ),

  /** Registers an organization such as a SiteInstitution with a signed reason and idempotency key (POST /api/v1/kernel/organizations). */
  createOrganization: lazyKernelAction(
    loadRouteTransport,
    "executeCreateOrganization"
  ),

  /** Registered investigational sites in the caller's tenant, ordered by site code (GET /api/v1/kernel/sites). */
  listSites: lazyKernelAction(loadRouteTransport, "executeListSites"),

  /** Registers an investigational site with a 2-letter ISO country code, signed reason, and idempotency key (POST /api/v1/kernel/sites). */
  createSite: lazyKernelAction(loadRouteTransport, "executeCreateSite"),

  /** Sites linked to a study in the caller's tenant, ordered by site code (GET /api/v1/kernel/studies/{studyRef}/sites). */
  listStudySites: lazyKernelAction(loadRouteTransport, "executeListStudySites"),

  /** Links a registered investigational site to a study with a signed reason and idempotency key (POST /api/v1/kernel/studies/{studyRef}/sites). */
  linkSiteToStudy: lazyKernelAction(
    loadRouteTransport,
    "executeLinkSiteToStudy"
  ),

  /** Registers a new study with a signed reason for change and idempotency key (POST /api/v1/kernel/studies). */
  createStudy: lazyKernelAction(loadRouteTransport, "executeCreateStudy"),

  /** Bundled CDISC Library eCRF templates available to import into a protocol draft (GET /api/v1/kernel/protocol-templates). */
  listProtocolTemplates: lazyKernelAction(
    loadRouteTransport,
    "executeListProtocolTemplates"
  ),

  /** Viewable forms, ordered items, prompts, mandatory flags, and import notes for a protocol version (GET /api/v1/kernel/protocol-versions/{versionRef}/design-summary). */
  getProtocolDesignSummary: lazyKernelAction(
    loadRouteTransport,
    "executeGetProtocolDesignSummary"
  ),

  /**
   * Synchronously imports an ODM v1.3.2 XML file, USDM v4 JSON file, or bundled CDISC Library eCRF templates
   * to create or revise a study's protocol draft (POST /api/v1/kernel/studies/{studyRef}/protocol-imports).
   */
  importProtocolDraft: lazyKernelAction(
    loadRouteTransport,
    "executeProtocolDraftImport"
  ),

  /**
   * Resolves the governing protocol version at the enrollment's site and computes expected visit target dates and
   * window statuses in site calendar time (GET /api/v1/kernel/enrollments/{enrollmentRef}/schedule).
   */
  getEnrollmentSchedule(
    enrollmentRef: string,
    options: { asOf?: string | null; timeZone?: string | null } = {}
  ): Promise<KernelEnrollmentSchedule> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/enrollments/{enrollmentRef}/schedule", {
        params: {
          path: { enrollmentRef },
          query: {
            as_of: options.asOf || undefined,
            time_zone: options.timeZone || undefined,
          },
        },
      })
    );
  },

  /** A study's protocol versions, newest first, without content. */
  listProtocolVersions: lazyKernelAction(
    loadRouteTransport,
    "executeListProtocolVersions"
  ),

  /** One protocol version, without content. */
  getProtocolVersion: lazyKernelAction(
    loadRouteTransport,
    "executeGetProtocolVersion"
  ),

  /** A protocol version's Schedule of Activities: planned activities as columns, collection specifications as rows (ADR-2475). */
  getScheduleOfActivities: lazyKernelAction(
    loadRouteTransport,
    "executeGetScheduleOfActivities"
  ),

  /** What depends on the version a protocol amendment changes (K-PROT-2). A first version amends nothing and is refused. */
  amendmentImpact: lazyKernelAction(
    loadRouteTransport,
    "executeAmendmentImpact"
  ),

  /** Runs evaluate-only in-memory simulation over candidate protocol version, mapping specification, or rule pack. */
  simulate: lazyKernelAction(loadRouteTransport, "executeSimulate"),
};
