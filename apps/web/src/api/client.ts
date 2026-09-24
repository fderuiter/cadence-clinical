import createClient from "openapi-fetch";
import type { paths } from "./types";
import { getBaseUrl } from "./apiClient";

/**
 * Creates an openapi-fetch client instance bound to specified or default base URL.
 *
 * @param baseUrl - The base API URL.
 * @returns Strongly-typed openapi-fetch client instance.
 */
export function createOpenApiClient<T extends {} = paths>(baseUrl: string = getBaseUrl()) {
  return createClient<T>({ baseUrl });
}

/**
 * Default strongly-typed openapi-fetch client instance for web API requests.
 */
export const openApiClient = createClient<paths>({
  baseUrl: getBaseUrl(),
});

export default openApiClient;
