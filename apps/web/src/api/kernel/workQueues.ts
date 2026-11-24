import type { KernelSchemas } from "./shared";

const loadRouteTransport = () => import("./protocolImport");

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
  async listWorkQueues(
    query: KernelWorkQueuesQuery = {}
  ): Promise<KernelWorkQueuesPage> {
    const { executeListWorkQueues } = await loadRouteTransport();
    return executeListWorkQueues(query);
  },

  /** Defines the next version of a capability-keyed work queue (`POST /api/v1/kernel/work-queues`). */
  async defineWorkQueue(
    input: KernelDefineWorkQueueInput
  ): Promise<KernelWorkQueueDefinition> {
    const { executeDefineWorkQueue } = await loadRouteTransport();
    return executeDefineWorkQueue(input);
  },
};
