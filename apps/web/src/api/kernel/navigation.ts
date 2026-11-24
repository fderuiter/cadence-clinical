import { unwrapResponse } from "../client";
import {
  kernelClient,
  windowQuery,
  type KernelLineageWindow,
  type KernelSchemas,
} from "./shared";

export type KernelNode = KernelSchemas["Kernel_KernelNodeResponse"];
export type KernelZoomStep = KernelSchemas["Kernel_KernelZoomStepResponse"];
export type KernelChild = KernelSchemas["Kernel_KernelChildResponse"];
export type KernelDecision = KernelSchemas["Kernel_KernelDecisionResponse"];
export type KernelExplanation =
  KernelSchemas["Kernel_KernelExplanationResponse"];
export type KernelCommand = KernelSchemas["Kernel_KernelCommandResponse"];
export type KernelSearchHit = KernelSchemas["Kernel_KernelSearchHitResponse"];
export type KernelEnrollmentLookup =
  KernelSchemas["Kernel_KernelEnrollmentLookupResponse"];
export type KernelLineageNode =
  KernelSchemas["Kernel_KernelLineageNodeResponse"];
export type KernelLineageEdge =
  KernelSchemas["Kernel_KernelLineageEdgeResponse"];
export type KernelLineage = KernelSchemas["Kernel_KernelLineageResponse"];
export type KernelDependentGroup =
  KernelSchemas["Kernel_KernelDependentGroupResponse"];
export type KernelDependents = KernelSchemas["Kernel_KernelDependentsResponse"];
export type KernelActivityEntry =
  KernelSchemas["Kernel_KernelActivityEntryResponse"];
export type KernelActivityPage =
  KernelSchemas["Kernel_KernelActivityPageResponse"];
export type KernelHistoryFieldChange =
  KernelSchemas["Kernel_KernelHistoryFieldChangeResponse"];
export type KernelHistoryAttestation =
  KernelSchemas["Kernel_KernelHistoryAttestationResponse"];
export type KernelHistoryEntry =
  KernelSchemas["Kernel_KernelHistoryEntryResponse"];
export type KernelNodeHistory =
  KernelSchemas["Kernel_KernelNodeHistoryResponse"];
export type KernelAuditEntryDetail =
  KernelSchemas["Kernel_KernelAuditEntryDetailResponse"];

/** The event families of the kernel's activity stream (spec §5.2). */
export type KernelEventFamily =
  "Knowledge" | "Execution" | "Evidence" | "Trust" | "Quality" | "Intelligence";

/** Which activity to read (ADR-2485). The whole tenant when `scope` is left out; `before` is an earlier page's `next`. */
export interface KernelActivityQuery {
  scope?: string;
  families?: KernelEventFamily[];
  actor?: string;
  from?: string;
  before?: string;
  limit?: number;
}

export interface KernelTraceQuery extends KernelLineageWindow {
  /** `Backward` toward inputs (the default) or `Forward` toward dependents. */
  direction?: "Backward" | "Forward";
}

export interface KernelSearchQuery {
  text: string;
  kinds?: string[];
  limit?: number;
}

export interface KernelEnrollmentLookupQuery {
  studyCode: string;
  subjectCode: string;
  siteCode?: string | null;
}

export const kernelNavigationApi = {
  /** Summary of one object at its current version (GET /api/v1/kernel/nodes/{nodeRef}). */
  getNode(nodeRef: string): Promise<KernelNode> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Zoom path from the tenant down to the object; presentation only, it grants nothing (spec §9.3). */
  getZoomPath(nodeRef: string): Promise<KernelZoomStep[]> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/path", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Zoom children of a scope node, with their own child counts. */
  getChildren(nodeRef: string): Promise<KernelChild[]> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/children", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Kernel commands offered on the object, each with the reader's dry-run decision and its explanation (ADR-2483). */
  getCommands(nodeRef: string): Promise<KernelCommand[]> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/commands", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Whether the reader may exercise a capability on an object now, and why (ADR-2482). Executes nothing. */
  canI(capability: string, resource: string): Promise<KernelDecision> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/authorization/can-i", {
        params: { query: { capability, resource } },
      })
    );
  },

  /** Kernel objects by code or title (ADR-2484); hidden objects are left out. */
  search({
    text,
    kinds,
    limit,
  }: KernelSearchQuery): Promise<KernelSearchHit[]> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/search", {
        params: {
          query: {
            q: text,
            kind: kinds && kinds.length > 0 ? kinds : undefined,
            limit,
          },
        },
      })
    );
  },

  /**
   * Resolves a study code, subject code, and optional site code to pinned study, study site, and enrollment NodeRefs
   * (GET /api/v1/kernel/enrollments/lookup). Rejects with status 404 when absent, site-mismatched, or masked (ADR-2471).
   */
  lookupEnrollment({
    studyCode,
    subjectCode,
    siteCode,
  }: KernelEnrollmentLookupQuery): Promise<KernelEnrollmentLookup> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/enrollments/lookup", {
        params: {
          query: {
            study_code: studyCode,
            subject_code: subjectCode,
            site_code: siteCode ? siteCode : undefined,
          },
        },
      })
    );
  },

  /** Trace: the object's lineage walk, backward to its inputs by default. Masked objects keep their place (ADR-2471). */
  trace(nodeRef: string, query: KernelTraceQuery = {}): Promise<KernelLineage> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/lineage", {
        params: {
          path: { nodeRef },
          query: { direction: query.direction, ...windowQuery(query) },
        },
      })
    );
  },

  /** Impact: what depends on the object, grouped by kind; masked objects are counted like any other (Canvas D2). */
  dependents(
    nodeRef: string,
    window: KernelLineageWindow = {}
  ): Promise<KernelDependents> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/dependents", {
        params: { path: { nodeRef }, query: windowQuery(window) },
      })
    );
  },

  /**
   * Bitemporal audit, correction, supersession, and attestation history for a single object
   * (GET /api/v1/kernel/nodes/{nodeRef}/history, PX-12). Masked entries preserve timeline position.
   */
  getNodeHistory(
    nodeRef: string,
    window: KernelLineageWindow = {}
  ): Promise<KernelNodeHistory> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/history", {
        params: { path: { nodeRef }, query: windowQuery(window) },
      })
    );
  },

  /**
   * Tamper-evident audit record detail for the Audit Event peek drawer
   * (GET /api/v1/kernel/audit-entries/{auditRef}, K-AUTH-4).
   */
  getAuditEntry(auditRef: string): Promise<KernelAuditEntryDetail> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/audit-entries/{auditRef}", {
        params: { path: { auditRef } },
      })
    );
  },

  /**
   * What happened in a scope, newest first (ADR-2485). A scope the reader may not see is refused; an entry about an
   * object the reader may not see keeps its place with its kind and `mask`, and no `subject`.
   */
  activity({
    scope,
    families,
    actor,
    from,
    before,
    limit,
  }: KernelActivityQuery = {}): Promise<KernelActivityPage> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/activity", {
        params: {
          query: {
            scope,
            family: families && families.length > 0 ? families : undefined,
            actor,
            from,
            before,
            limit,
          },
        },
      })
    );
  },
};
