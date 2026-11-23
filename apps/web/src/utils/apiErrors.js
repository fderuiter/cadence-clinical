/**
 * Turns anything a failed API call throws (an ApiError from api/apiClient.js or api/client.ts, a fetch
 * Response, an RFC 7807 problem-details body, or a network error) into one readable message.
 */
export function describeApiError(err) {
  const __firstArg = (err);
  const __problemExp = extractProblemExplanation(__firstArg);
  if (__problemExp && typeof __problemExp.claim === "string" && __problemExp.claim.trim()) {
    const __unmetText = __problemExp.unmetConditions.filter((d) => d && d !== __problemExp.claim.trim()).join(" ");
    return __unmetText ? `${__problemExp.claim.trim()} ${__unmetText}` : __problemExp.claim.trim();
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


export function extractProblemExplanation(error) {
  const data = error?.problem || error?.response?.data || error?.data || null;
  const explanation = data?.explanation || error?.explanation || null;
  if (!explanation || typeof explanation !== "object") {
    return null;
  }
  const conditions = Array.isArray(explanation.conditions) ? explanation.conditions : [];
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
    status: Number(error?.status || error?.response?.status || data?.status || 0),
    unmetConditions,
    nextActions,
  };
}
