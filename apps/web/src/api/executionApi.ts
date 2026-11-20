import { getBearerToken } from "./authorization.js";
import { encodeChangeReason } from "./changeReason";
import {
  ApiError,
  createOpenApiClient,
  getBaseUrl,
  unwrapResponse,
} from "./client";
import type { components, paths } from "./generated/execution";

/**
 * Typed client for the .NET Execution service, generated from docs/openapi/execution_openapi.json.
 *
 * Every Execution call in the web app goes through this client (docs/web/API_MIGRATION.md). eCRF forms are not here:
 * they are kernel form instances served by the kernel host (ecrfFormsApi.ts, ADR-2622), and so are their queries
 * (ecrfQueriesApi.ts, #5461).
 */
type Schemas = components["schemas"];

export type SubjectSummary = Schemas["Execution_SubjectSummaryResponse"];
export type SubjectList = Schemas["Execution_SubjectListResponse"];
export type Subject = Schemas["Execution_SubjectResponse"];
export type SubjectDetail = Schemas["Execution_SubjectDetailResponse"];
export type SubjectConsentRequest = Schemas["Execution_SubjectConsentRequest"];
export type SubjectConsent = Schemas["Execution_SubjectConsentResponse"];
export type ProtocolVersionPayload =
  Schemas["Execution_ProtocolVersionPayload"];
export type ComplianceStatus = Schemas["Execution_ComplianceStatusResponse"];
export type OperationEnvelope = Schemas["Execution_OperationEnvelopeResponse"];
export type RandomizationScheme =
  Schemas["Execution_RandomizationSchemeResponse"];
export type RandomizationSchemeDraftRequest =
  Schemas["Execution_RandomizationSchemeDraftRequest"];
export type RandomizeSubjectRequest =
  Schemas["Execution_RandomizeSubjectRequest"];
export type RandomizationResult = Schemas["Execution_RandomizationResponse"];
export type IpKitSeedItem = Schemas["Execution_IpKitSeedItem"];
export type SeedInventoryRequest = Schemas["Execution_SeedInventoryRequest"];

export interface IpKitInventoryItem {
  id?: string;
  study_id: string;
  site_id: string;
  kit_number: string;
  lot_number: string;
  expiration_date: string;
  status: string;
  residual_volume_ml?: number | null;
  residual_volume_percentage?: number | null;
  reconciliation_count?: number;
}

export {
  APPROVE_SCHEME_SEMANTIC_ACTION,
  RANDOMIZE_SUBJECT_SEMANTIC_ACTION,
  approveSchemeSignatureAction,
  randomizeSubjectSignatureAction,
} from "../canvas/rtsm";

export interface SignatureVerificationInput {
  password: string;
  action: string;
  semanticAction: string;
  totp?: string | null;
  username?: string | null;
  batchId?: string | null;
}

export interface SignatureTokenResponse {
  sig_token: string;
  expires_in: number;
}

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
  limit?: number;
  offset?: number;
}

const client = createOpenApiClient<paths>();

export const executionApi = {
  /**
   * Re-authenticates the signed-in user at the gateway (POST /api/v1/auth/signature-verification)
   * and returns a single-use, 60-second X-Sig-Token bound to one action (21 CFR Part 11).
   */
  async requestSignatureToken(
    input: SignatureVerificationInput
  ): Promise<SignatureTokenResponse> {
    const baseUrl = getBaseUrl().replace(/\/+$/, "");
    const headers: Record<string, string> = {
      "Content-Type": "application/json",
    };
    const bearer = await getBearerToken();
    if (bearer) {
      headers.Authorization = `Bearer ${bearer}`;
    }
    const response = await globalThis.fetch(
      `${baseUrl}/api/v1/auth/signature-verification`,
      {
        method: "POST",
        headers,
        body: JSON.stringify({
          username: input.username || undefined,
          password: input.password,
          totp: input.totp || undefined,
          action: input.action,
          semantic_action: input.semanticAction,
          batch_id: input.batchId || undefined,
        }),
      }
    );
    const data = await response.json().catch(() => null);
    if (!response.ok) {
      const detail =
        data?.detail ||
        data?.title ||
        data?.message ||
        `Signature verification failed with status ${response.status}`;
      throw new ApiError(
        typeof detail === "string" ? detail : JSON.stringify(detail),
        response.status,
        response.statusText,
        data
      );
    }
    return data as SignatureTokenResponse;
  },

  /** One page of a study's subjects in enrollment order (GET /api/v1/execution/subjects). */
  listSubjects(query: ListSubjectsQuery): Promise<SubjectList> {
    return unwrapResponse(
      client.GET("/api/v1/execution/subjects", {
        params: {
          query: {
            study_id: query.studyId,
            site_id: query.siteId,
            status: query.status,
            limit: query.limit,
            offset: query.offset,
          },
        },
      })
    );
  },

  /** Full detail for a participant by subject code (GET /api/v1/execution/subjects/{subjectId}). */
  getSubjectDetail(subjectId: string): Promise<SubjectDetail> {
    return unwrapResponse(
      client.GET("/api/v1/execution/subjects/{subjectId}", {
        params: {
          path: { subjectId },
        },
      })
    );
  },

  /**
   * Adds a participant to a study in screening (POST /api/v1/execution/subjects). The canvas sends no
   * demographics: identity details are not captured here. A participant is never given an arm at creation;
   * randomisation has its own route (ADR-2640). The reason reaches
   * the audit entry as X-Change-Reason.
   */
  addSubject({
    studyId,
    subjectCode,
    siteId,
    reason,
  }: AddSubject): Promise<Subject> {
    return unwrapResponse(
      client.POST("/api/v1/execution/subjects", {
        body: {
          subject_id: subjectCode,
          study_id: studyId,
          site_id: siteId || null,
          demographics: null,
        },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /**
   * Moves a participant to another state (PATCH /api/v1/execution/subjects/{subjectId}/state), by id or
   * subject code. Execution checks the transition and, for enrolment, that the site is activated; the reason
   * is sent as X-Change-Reason.
   */
  changeSubjectState(
    subjectId: string,
    state: string,
    reason: string
  ): Promise<Subject> {
    return unwrapResponse(
      client.PATCH("/api/v1/execution/subjects/{subjectId}/state", {
        params: { path: { subjectId } },
        body: { status: state, state: null },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /**
   * Whether eTMF has reported the site's documents complete for a milestone (default SITE_ACTIVATION).
   * The service answers even when eTMF has reported nothing: `is_complete` is false and no documents are listed.
   */
  getSiteComplianceStatus(
    siteId: string,
    studyId: string,
    milestone?: string
  ): Promise<ComplianceStatus> {
    return unwrapResponse(
      client.GET("/api/v1/execution/sites/{siteId}/compliance-status", {
        params: {
          path: { siteId },
          query: { study_id: studyId, milestone },
        },
      })
    );
  },

  /**
   * Activates a site for the study. Execution refuses (400) unless eTMF reports the site's activation
   * documents complete, and audits the attempt either way with the reason sent as X-Change-Reason
   * (encoded per ADR-2495, so any script reaches the audit trail as typed).
   */
  activateSite(
    siteId: string,
    studyId: string,
    changeReason: string
  ): Promise<OperationEnvelope> {
    return unwrapResponse(
      client.POST("/api/v1/execution/sites/{siteId}/activate", {
        params: { path: { siteId } },
        body: { study_id: studyId },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Lists a study's randomization scheme versions, newest first (GET /api/v1/execution/randomization/schemes). */
  listRandomizationSchemes(studyId: string): Promise<RandomizationScheme[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/randomization/schemes", {
        params: { query: { study_id: studyId } },
      })
    );
  },

  /** Drafts the study's next randomization scheme version (POST /api/v1/execution/randomization/schemes). */
  draftRandomizationScheme(
    payload: RandomizationSchemeDraftRequest,
    reason: string
  ): Promise<RandomizationScheme> {
    return unwrapResponse(
      client.POST("/api/v1/execution/randomization/schemes", {
        body: payload,
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /**
   * Approves a draft randomization scheme version for go-live using a single-use X-Sig-Token
   * bound to `execution.form.approve` (POST /api/v1/execution/randomization/schemes/{schemeId}/approve).
   */
  approveRandomizationScheme(
    schemeId: string,
    options: { reason: string; sigToken: string }
  ): Promise<RandomizationScheme> {
    return unwrapResponse(
      client.POST(
        "/api/v1/execution/randomization/schemes/{schemeId}/approve",
        {
          params: { path: { schemeId } },
          headers: {
            "X-Change-Reason": encodeChangeReason(options.reason),
            "X-Sig-Token": options.sigToken,
          },
        }
      )
    );
  },

  /**
   * Randomizes an enrolled participant under the live scheme with a single-use X-Sig-Token
   * bound to `execution.subject.randomize` (POST /api/v1/execution/subjects/{subjectId}/randomize).
   * Never returns the treatment arm.
   */
  randomizeSubject(
    subjectId: string,
    options: {
      stratificationFactors?: Record<string, string> | null;
      reason: string;
      sigToken: string;
    }
  ): Promise<RandomizationResult> {
    return unwrapResponse(
      client.POST("/api/v1/execution/subjects/{subjectId}/randomize", {
        params: { path: { subjectId } },
        body: {
          stratification_factors: options.stratificationFactors ?? null,
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(options.reason),
          "X-Sig-Token": options.sigToken,
        },
      })
    );
  },

  /**
   * Records or merges a participant's informed consent with an explicit signed date
   * (POST /api/v1/execution/subjects/{subjectId}/consent).
   */
  recordSubjectConsent(
    subjectId: string,
    payload: SubjectConsentRequest,
    reason?: string
  ): Promise<SubjectConsent> {
    return unwrapResponse(
      client.POST("/api/v1/execution/subjects/{subjectId}/consent", {
        params: { path: { subjectId } },
        body: payload,
        headers: reason
          ? { "X-Change-Reason": encodeChangeReason(reason) }
          : undefined,
      })
    );
  },

  /** Seeds encrypted IP kit inventory for a site (POST /api/v1/execution/rtsm/kits/seed-inventory). */
  seedIpKitInventory(
    payload: SeedInventoryRequest,
    reason: string
  ): Promise<unknown> {
    return unwrapResponse(
      client.POST("/api/v1/execution/rtsm/kits/seed-inventory", {
        body: payload,
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /** Reconciles a batch of IP kit returns atomically in RTSM (POST /api/v1/execution/rtsm/kits/reconcile-batch). */
  async reconcileIpKitBatch(payload: {
    study_id: string;
    site_id: string;
    processed_by: string;
    notes?: string;
    reason_for_change: string;
    items: Array<{
      kit_number: string;
      lot_number: string;
      expected_lot_number?: string;
      residual_volume_ml?: number;
      residual_volume_percentage?: number;
      disposition: string;
      disposition_reason: string;
    }>;
  }): Promise<any> {
    const res = await fetch("/api/v1/execution/rtsm/kits/reconcile-batch", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Change-Reason": encodeChangeReason(payload.reason_for_change),
      },
      body: JSON.stringify(payload),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ detail: "Request failed" }));
      throw new Error(
        err.detail || "Server failed to process batch reconciliation."
      );
    }
    return res.json();
  },

  /** Retrieves blind-safe site IP kit inventory catalog (GET /api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}). */
  getSiteIpKitInventory(
    studyId: string,
    siteId: string
  ): Promise<IpKitInventoryItem[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}", {
        params: { path: { studyId, siteId } },
      }) as unknown as Promise<{
        data?: IpKitInventoryItem[];
        error?: unknown;
        response: Response;
      }>
    );
  },

  /** Validates scanned barcode against site inventory (POST /api/v1/execution/rtsm/kits/validate-scan). */
  async validateIpKitScan(
    studyId: string,
    siteId: string,
    scannedBarcode: string
  ): Promise<any> {
    const res = await fetch("/api/v1/execution/rtsm/kits/validate-scan", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        study_id: studyId,
        site_id: siteId,
        scanned_barcode: scannedBarcode,
      }),
    });
    if (!res.ok) {
      throw new Error("Failed to validate barcode scan");
    }
    return res.json();
  },
};
