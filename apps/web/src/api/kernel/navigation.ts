import { unwrapResponse } from "../client";
import {
  kernelClient,
  lazyKernelAction,
  type KernelLineageWindow,
  type KernelSchemas,
} from "./shared";

const loadRouteTransport = () => import("./navigation.transport");
export type KernelNode = KernelSchemas["Kernel_KernelNodeResponse"];
export type KernelBatchNodes = KernelSchemas["Kernel_KernelBatchNodesResponse"];
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
  includeNodes?: boolean;
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

  /** Summaries of up to 100 objects at their current or pinned versions (GET /api/v1/kernel/nodes). */
  getNodes(nodeRefs: string[]): Promise<KernelBatchNodes> {
    if (nodeRefs.length === 0) {
      return Promise.resolve({ nodes: {} });
    }
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/nodes", {
        params: { query: { ref: nodeRefs } },
      })
    );
  },

  /** Zoom path from the tenant down to the object; presentation only, it grants nothing (spec §9.3). */
  getZoomPath: lazyKernelAction(loadRouteTransport, "executeGetZoomPath"),

  /** Zoom children of a scope node, with their own child counts. */
  getChildren: lazyKernelAction(loadRouteTransport, "executeGetChildren"),

  /** Kernel commands offered on the object, each with the reader's dry-run decision and its explanation (ADR-2483). */
  getCommands: lazyKernelAction(loadRouteTransport, "executeGetCommands"),

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
  trace: lazyKernelAction(loadRouteTransport, "executeTrace"),

  /** Impact: what depends on the object, grouped by kind; masked objects are counted like any other (Canvas D2). */
  dependents: lazyKernelAction(loadRouteTransport, "executeDependents"),

  /**
   * What happened in a scope, newest first (ADR-2485). A scope the reader may not see is refused; an entry about an
   * object the reader may not see keeps its place with its kind and `mask`, and no `subject`.
   */
  activity: lazyKernelAction(loadRouteTransport, "executeActivity"),
};
