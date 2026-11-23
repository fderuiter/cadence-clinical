import { encodeChangeReason } from "../changeReason";
import { unwrapResponse } from "../client";
import { kernelClient, unversioned, type KernelSchemas } from "./shared";

export type KernelWorkQueue = KernelSchemas["Kernel_KernelWorkQueueResponse"];
export type KernelWorkQueuesPage =
  KernelSchemas["Kernel_KernelWorkQueuesResponse"];
export type KernelWorkItem = KernelSchemas["Kernel_KernelWorkItemResponse"];
export type KernelWorkItemTarget =
  KernelSchemas["Kernel_KernelWorkItemTargetResponse"];
export type KernelWorkQueueDefinition =
  KernelSchemas["Kernel_KernelWorkQueueDefinitionResponse"];

export interface KernelWorkQueuesQuery {
  within?: string;
  codes?: string[];
  limit?: number;
}

export interface KernelDefineWorkQueueInput {
  code: string;
  title: string;
  capability: string;
  verbClass: "Read" | "Propose" | "Execute" | "Approve" | "Administer";
  source: string;
  workQueueDefinitionId?: string | null;
  reason: string;
  idempotencyKey: string;
}

export const kernelWorkQueuesApi = {
  /** Reads the caller's capability-keyed work queues (`GET /api/v1/kernel/work-queues`). */
  listWorkQueues(
    query: KernelWorkQueuesQuery = {}
  ): Promise<KernelWorkQueuesPage> {
    return unwrapResponse(
      kernelClient.GET("/api/v1/kernel/work-queues", {
        params: {
          query: {
            within: query.within ? unversioned(query.within) : undefined,
            code:
              query.codes && query.codes.length > 0 ? query.codes : undefined,
            limit: query.limit,
          },
        },
      })
    );
  },

  /** Defines the next version of a capability-keyed work queue (`POST /api/v1/kernel/work-queues`). */
  defineWorkQueue({
    code,
    title,
    capability,
    verbClass,
    source,
    workQueueDefinitionId,
    reason,
    idempotencyKey,
  }: KernelDefineWorkQueueInput): Promise<KernelWorkQueueDefinition> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/work-queues", {
        body: {
          code,
          title,
          capability,
          verb_class: verbClass,
          source,
          ...(workQueueDefinitionId
            ? { work_queue_definition_id: workQueueDefinitionId }
            : {}),
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },
};
