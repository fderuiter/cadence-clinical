import type { KernelSchemas } from "./shared";

const loadRouteTransport = () => import("./workQueues.transport");

export type KernelWorkQueue = KernelSchemas["Kernel_KernelWorkQueueResponse"];
export type KernelWorkQueuesPage =
  KernelSchemas["Kernel_KernelWorkQueuesResponse"];
export type KernelWorkItem = KernelSchemas["Kernel_KernelWorkItemResponse"];
export type KernelWorkItemTarget =
  KernelSchemas["Kernel_KernelWorkItemTargetResponse"];

export interface KernelWorkQueuesQuery {
  within?: string;
  codes?: string[];
  limit?: number;
}

export const kernelWorkQueuesApi = {
  /** Reads the caller's capability-keyed work queues (`GET /api/v1/kernel/work-queues`). */
  async listWorkQueues(
    query: KernelWorkQueuesQuery = {}
  ): Promise<KernelWorkQueuesPage> {
    const { executeListWorkQueues } = await loadRouteTransport();
    return executeListWorkQueues(query);
  },
};
