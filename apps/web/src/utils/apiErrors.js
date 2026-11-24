/**
 * Canonical error class representing gateway HTTP, status, or business failures across web clients.
 */
export class ApiError extends Error {
  /**
   * @param {string} message
   * @param {number | null} [status=null]
   * @param {string | null} [statusText=null]
   * @param {unknown} [data=null]
   */
  constructor(message, status = null, statusText = null, data = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.statusText = statusText;
    this.data = data;
  }
}

/**
 * Turns anything a failed API call throws (an ApiError, a fetch Response,
 * an RFC 7807 problem-details body, or a network error) into one readable message.
 */
export function describeApiError(err) {
  const exp = extractProblemExplanation(err);
  if (exp && typeof exp.claim === "string" && exp.claim.trim()) {
    const claim = exp.claim.trim();
    const unmet = exp.unmetConditions.filter((d) => d && d !== claim).join(" ");
    return unmet ? `${claim} ${unmet}` : claim;
  }
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

export function extractProblemExplanation(error) {
  const data = error?.problem || error?.response?.data || error?.data || null;
  const explanation = data?.explanation || error?.explanation || null;
  if (!explanation || typeof explanation !== "object") {
    return null;
  }
  const conditions = Array.isArray(explanation.conditions)
    ? explanation.conditions
    : [];
  const unmetConditions = conditions
    .filter((c) => c && c.met === false && typeof c.description === "string")
    .map((c) => c.description.trim());
  const rawActions = Array.isArray(explanation.next_actions)
    ? explanation.next_actions
    : Array.isArray(explanation.nextActions)
      ? explanation.nextActions
      : [];
  const nextActions = rawActions.map((a) => ({
    description: a?.description || "",
    capability: a?.capability || null,
    target: a?.target || null,
  }));
  return {
    ...explanation,
    code: data?.code || error?.code || "",
    status: Number(
      error?.status || error?.response?.status || data?.status || 0
    ),
    unmetConditions,
    nextActions,
  };
}
