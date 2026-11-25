import { createOpenApiClient } from "./client";
import type { paths } from "./types";

/**
 * Dedicated OpenAPI client for remaining unmigrated Python-era endpoints typed against
 * the pruned `src/api/types.ts` contract (ADR-2678). `.NET`-backed clients instantiate
 * per-service clients via `createOpenApiClient<ServicePaths>()` from `./client`.
 */
export const openApiClient = createOpenApiClient<paths>();

export default openApiClient;
