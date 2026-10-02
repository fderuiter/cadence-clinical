import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/kernel";

/**
 * Typed client for the kernel API host, generated from docs/openapi/kernel_openapi.json (ADR-2540).
 *
 * Every call reads: none sends a kernel command. The host acts in the kernel tenant and as the person named by the
 * gateway-signed identity, so nothing here names a tenant or an actor. NodeRefs travel as their canonical strings
 * (`kind:id@version`). An object the reader may not see comes back masked (ADR-2471): only its `kind` and `mask`,
 * everything else null; search leaves it out. Clients render the kernel's explanations and never compose one
 * (K-EXP-1).
 */
type Schemas = components["schemas"];

export type KernelNode = Schemas["Kernel_KernelNodeResponse"];
export type KernelZoomStep = Schemas["Kernel_KernelZoomStepResponse"];
export type KernelChild = Schemas["Kernel_KernelChildResponse"];
export type KernelDecision = Schemas["Kernel_KernelDecisionResponse"];
export type KernelExplanation = Schemas["Kernel_KernelExplanationResponse"];
export type KernelCommand = Schemas["Kernel_KernelCommandResponse"];
export type KernelSearchHit = Schemas["Kernel_KernelSearchHitResponse"];
export type KernelProtocolVersion =
  Schemas["Kernel_KernelProtocolVersionResponse"];
export type KernelLineageNode = Schemas["Kernel_KernelLineageNodeResponse"];
export type KernelLineageEdge = Schemas["Kernel_KernelLineageEdgeResponse"];
export type KernelLineage = Schemas["Kernel_KernelLineageResponse"];
export type KernelDependentGroup =
  Schemas["Kernel_KernelDependentGroupResponse"];
export type KernelDependents = Schemas["Kernel_KernelDependentsResponse"];
export type KernelAmendmentImpact =
  Schemas["Kernel_KernelAmendmentImpactResponse"];
export type KernelActivityEntry = Schemas["Kernel_KernelActivityEntryResponse"];
export type KernelActivityPage = Schemas["Kernel_KernelActivityPageResponse"];

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

/** How far and along which edges a lineage read goes, and as of when (ADR-2471). The host's defaults apply to anything left out. */
export interface KernelLineageWindow {
  depth?: number;
  edgeKinds?: string[];
  validAt?: string;
  knownAt?: string;
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

/** True when the reader may not see the object: show its place and `mask`, never guess at the rest. */
export function isMasked(object: { mask: string | null }): boolean {
  return object.mask !== null;
}

const client = createOpenApiClient<paths>();

function windowQuery({
  depth,
  edgeKinds,
  validAt,
  knownAt,
}: KernelLineageWindow) {
  return {
    depth,
    edge_kind: edgeKinds && edgeKinds.length > 0 ? edgeKinds : undefined,
    valid_at: validAt,
    known_at: knownAt,
  };
}

export const kernelApi = {
  /** Summary of one object at its current version (GET /api/v1/kernel/nodes/{nodeRef}). */
  getNode(nodeRef: string): Promise<KernelNode> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Zoom path from the tenant down to the object; presentation only, it grants nothing (spec §9.3). */
  getZoomPath(nodeRef: string): Promise<KernelZoomStep[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}/path", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Zoom children of a scope node, with their own child counts. */
  getChildren(nodeRef: string): Promise<KernelChild[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}/children", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Kernel commands offered on the object, each with the reader's dry-run decision and its explanation (ADR-2483). */
  getCommands(nodeRef: string): Promise<KernelCommand[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}/commands", {
        params: { path: { nodeRef } },
      })
    );
  },

  /** Whether the reader may exercise a capability on an object now, and why (ADR-2482). Executes nothing. */
  canI(capability: string, resource: string): Promise<KernelDecision> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/authorization/can-i", {
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
      client.GET("/api/v1/kernel/search", {
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

  /** A study's protocol versions, newest first, without content. */
  listProtocolVersions(studyRef: string): Promise<KernelProtocolVersion[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/studies/{studyRef}/protocol-versions", {
        params: { path: { studyRef } },
      })
    );
  },

  /** One protocol version, without content. */
  getProtocolVersion(versionRef: string): Promise<KernelProtocolVersion> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/protocol-versions/{versionRef}", {
        params: { path: { versionRef } },
      })
    );
  },

  /** Trace: the object's lineage walk, backward to its inputs by default. Masked objects keep their place (ADR-2471). */
  trace(nodeRef: string, query: KernelTraceQuery = {}): Promise<KernelLineage> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/nodes/{nodeRef}/lineage", {
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
      client.GET("/api/v1/kernel/nodes/{nodeRef}/dependents", {
        params: { path: { nodeRef }, query: windowQuery(window) },
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
      client.GET("/api/v1/kernel/activity", {
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

  /** What depends on the version a protocol amendment changes (K-PROT-2). A first version amends nothing and is refused. */
  amendmentImpact(
    versionRef: string,
    window: Omit<KernelLineageWindow, "edgeKinds"> = {}
  ): Promise<KernelAmendmentImpact> {
    const { depth, valid_at, known_at } = windowQuery(window);
    return unwrapResponse(
      client.GET("/api/v1/kernel/protocol-versions/{versionRef}/impact", {
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
  }): Promise<any> {
    return fetch("/api/v1/kernel/simulation/simulate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        base_version_ref: request.baseVersionRef,
        stored_version_ref: request.storedVersionRef,
        candidate_content: request.candidateContent,
        valid_at: request.validAt,
        known_at: request.knownAt,
      }),
    }).then((res) => {
      if (!res.ok) throw new Error(`Simulation failed: ${res.statusText}`);
      return res.json();
    });
  },

  /** Generates a candidate USDM protocol proposal from a trial brief prompt. */
  generateProposal(trialBrief: string): Promise<any> {
    return fetch("/api/v1/kernel/protocols/generate-proposal", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ trial_brief: trialBrief }),
    }).then((res) => {
      if (!res.ok)
        throw new Error(`Proposal generation failed: ${res.statusText}`);
      return res.json();
    });
  },

  /** Performs real-time schedule matrix gap check over a protocol design document. */
  checkMatrixGaps(protocolContent: string): Promise<any[]> {
    return fetch("/api/v1/kernel/protocols/matrix-gap-check", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ protocol_content: protocolContent }),
    }).then((res) => {
      if (!res.ok)
        throw new Error(`Matrix gap check failed: ${res.statusText}`);
      return res.json();
    });
  },
};
