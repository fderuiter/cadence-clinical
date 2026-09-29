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

/** Throws an Error carrying the status and body when a fetch Response is not 2xx. */
export async function ensureOk(response) {
  if (response.ok) return response;
  let body;
  try {
    body = await response.clone().json();
  } catch {
    body = undefined;
  }
  const error = new Error(
    body?.detail ||
      body?.title ||
      `Request failed with status ${response.status}`
  );
  error.status = response.status;
  error.data = body;
  throw error;
}
