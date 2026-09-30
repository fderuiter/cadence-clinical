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
};
