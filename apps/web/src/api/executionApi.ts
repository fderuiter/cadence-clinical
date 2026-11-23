import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/execution";

export type {
  CgtApheresisCreateRequest,
  CgtChainOfCustodyTrace,
  CgtMilestoneCreateRequest,
  ComplianceStatus,
  IpKitBatchReconciliationRequest,
  IpKitItemReconcileRequest,
  IpKitReconciliationCertificate,
  IpKitScanValidateRequest,
  IpKitScanValidationResult,
  IpKitSeedItem,
  OperationEnvelope,
  ProtocolVersionPayload,
  RandomizationResponse,
  RandomizationScheme,
  RandomizationSchemeDraftRequest,
  RandomizeSubjectRequest,
  SeedInventoryRequest,
  SiteIpKitInventoryItem,
  SubjectConsentRequest,
  SubjectConsentResponse,
} from "./execution/rtsm";

/**
 * Typed client for the .NET Execution service, generated from docs/openapi/execution_openapi.json.
 *
 * Every Execution call in the web app goes through this client (docs/web/API_MIGRATION.md). eCRF forms are not here:
 * they are kernel form instances served by the kernel host (ecrfFormsApi.ts, ADR-2622), and so are their queries
 * (ecrfQueriesApi.ts, #5461). Route-only RTSM, IP kit accountability, CGT, consent, and site-activation endpoints
 * live in `./execution/rtsm.ts` and load on demand so cold-boot shell chunks stay lean.
 */
type Schemas = components["schemas"];

export type SubjectSummary = Schemas["Execution_SubjectSummaryResponse"];
export type SubjectList = Schemas["Execution_SubjectListResponse"];
export type Subject = Schemas["Execution_SubjectResponse"];
export type SubjectDetail = Schemas["Execution_SubjectDetailResponse"];
export type SubjectCounts = Schemas["Execution_SubjectCountsResponse"];

export interface AddSubject {
  studyId: string;
  subjectCode: string;
  siteId?: string;
  reason: string;
}

export interface ListSubjectsQuery {
  studyId: string;
  siteId?: string;
  status?: string;
  q?: string;
  limit?: number;
  offset?: number;
}

export interface GetSubjectCountsQuery {
  studyId: string;
  siteId?: string;
}

const client = createOpenApiClient<paths>();

type RtsmApi = typeof import("./execution/rtsm").executionRtsmApi;

let rtsmPromise: Promise<RtsmApi> | undefined;

const loadRtsm = () =>
  (rtsmPromise ??= import("./execution/rtsm").then(
    (module) => module.executionRtsmApi
  ));

const RTSM_METHODS = [
  "addSubject",
  "changeSubjectState",
  "assignSubjectSite",
  "getSiteComplianceStatus",
  "activateSite",
  "reconcileIpKitBatch",
  "getSiteIpKitInventory",
  "seedSiteIpKitInventory",
  "getIpKitCertificate",
  "validateIpKitScan",
  "listRandomizationSchemes",
  "draftRandomizationScheme",
  "approveRandomizationScheme",
  "recordSubjectConsent",
  "randomizeSubject",
  "getChainOfCustody",
  "recordCgtApheresis",
  "recordCgtMilestone",
  "listCgtCertificates",
  "listCgtReconciliations",
] as const satisfies ReadonlyArray<keyof RtsmApi>;

type CompleteRtsmApi =
  Exclude<keyof RtsmApi, (typeof RTSM_METHODS)[number]> extends never
    ? RtsmApi
    : never;

const rtsmDelegates = Object.fromEntries(
  RTSM_METHODS.map((name) => [
    name,
    (...args: unknown[]) =>
      loadRtsm().then((api) =>
        (api[name] as (...params: unknown[]) => unknown)(...args)
      ),
  ])
) as CompleteRtsmApi;

export const executionApi = {
  /** One page of a study's subjects in enrollment order (GET /api/v1/execution/subjects). */
  listSubjects(query: ListSubjectsQuery): Promise<SubjectList> {
    return unwrapResponse(
      client.GET("/api/v1/execution/subjects", {
        params: {
          query: {
            study_id: query.studyId,
            site_id: query.siteId,
            status: query.status,
            q: query.q,
            limit: query.limit,
            offset: query.offset,
          },
        },
      })
    );
  },

  /** Counts a study's subjects grouped by lifecycle status (GET /api/v1/execution/subjects/counts). */
  getSubjectCounts(query: GetSubjectCountsQuery): Promise<SubjectCounts> {
    return unwrapResponse(
      client.GET("/api/v1/execution/subjects/counts", {
        params: {
          query: {
            study_id: query.studyId,
            site_id: query.siteId,
          },
        },
      })
    );
  },

  /** Full detail for a participant by surrogate UUID or subject code (GET /api/v1/execution/subjects/{subjectId}). */
  getSubjectDetail(subjectId: string): Promise<SubjectDetail> {
    return unwrapResponse(
      client.GET("/api/v1/execution/subjects/{subjectId}", {
        params: {
          path: { subjectId },
        },
      })
    );
  },

  ...rtsmDelegates,
};
