import { unwrapResponse } from "../client";
import { kernelClient, unversioned } from "./shared";
import type { KernelWorkQueuesPage, KernelWorkQueuesQuery } from "./workQueues";

export function executeListWorkQueues(
  query: KernelWorkQueuesQuery = {}
): Promise<KernelWorkQueuesPage> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/work-queues", {
      params: {
        query: {
          within: query.within ? unversioned(query.within) : undefined,
          code: query.codes && query.codes.length > 0 ? query.codes : undefined,
          limit: query.limit,
        },
      },
    })
  );
}
