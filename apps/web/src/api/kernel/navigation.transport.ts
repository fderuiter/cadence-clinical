import { unwrapResponse } from "../client";
import type {
  KernelActivityPage,
  KernelActivityQuery,
  KernelChild,
  KernelCommand,
  KernelDependents,
  KernelLineage,
  KernelTraceQuery,
  KernelZoomStep,
} from "./navigation";
import { kernelClient, windowQuery, type KernelLineageWindow } from "./shared";

export function executeGetZoomPath(nodeRef: string): Promise<KernelZoomStep[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/path", {
      params: { path: { nodeRef } },
    })
  );
}

export function executeGetChildren(nodeRef: string): Promise<KernelChild[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/children", {
      params: { path: { nodeRef } },
    })
  );
}

export function executeGetCommands(nodeRef: string): Promise<KernelCommand[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/commands", {
      params: { path: { nodeRef } },
    })
  );
}

export function executeTrace(
  nodeRef: string,
  query: KernelTraceQuery = {}
): Promise<KernelLineage> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/lineage", {
      params: {
        path: { nodeRef },
        query: { direction: query.direction, ...windowQuery(query) },
      },
    })
  );
}

export function executeDependents(
  nodeRef: string,
  window: KernelLineageWindow = {}
): Promise<KernelDependents> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/dependents", {
      params: { path: { nodeRef }, query: windowQuery(window) },
    })
  );
}

export function executeActivity({
  scope,
  families,
  actor,
  from,
  before,
  limit,
  includeNodes,
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
          include_nodes: includeNodes ? true : undefined,
        },
      },
    })
  );
}
