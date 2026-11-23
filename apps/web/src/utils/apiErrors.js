/**
 * Canonical error class representing API network, HTTP status, or RFC 9457 problem failures
 * across both `api/client.ts` (openapi-fetch) and `api/apiClient.js`.
 */
export class ApiError extends Error {
  /**
   * @param {string} message
   * @param {number|null} [status=null]
   * @param {string|null} [statusText=null]
   * @param {unknown} [data=null]
   */
  constructor(message, status = null, statusText = null, data = null) {
    super(message);
    this.name = "ApiError";
    /** @type {number|null} */
    this.status = status;
    /** @type {string|null} */
    this.statusText = statusText;
    /** @type {unknown} */
    this.data = data;
  }
}

/**
 * Turns anything a failed API call throws (an ApiError from api/apiClient.js or api/client.ts, a fetch
 * Response, an RFC 7807 problem-details body, or a network error) into one readable message.
 */
export function describeApiError(err) {
  const status = err?.status ?? err?.response?.status;
  const problem = err?.data ?? err?.body ?? err?.problem;
  const title = problem?.title;
  const detail =
    typeof problem?.detail === "string" ? problem.detail : undefined;
  const message =
    detail || title || err?.message || String(err ?? "Unknown error");
  return status ? `${message} (HTTP ${status})` : message;
}

/** Throws an ApiError carrying the status and body when a fetch Response is not 2xx. */
export async function ensureOk(response) {
  if (response.ok) return response;
  let body;
  try {
    body = await response.clone().json();
  } catch {
    body = undefined;
  }
  throw new ApiError(
    body?.detail ||
      body?.title ||
      `Request failed with status ${response.status}`,
    response.status,
    response.statusText ?? null,
    body
  );
}
