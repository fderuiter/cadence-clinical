import { HttpResponse } from "msw";

export const DEFAULT_STUDY_REF = "study:00000000-0000-0000-0000-000000000001"; // deid-ignore

export function decodeReasonHeader(request) {
  const raw =
    request.headers.get("X-Change-Reason") ||
    request.headers.get("x-change-reason");
  if (!raw) return "Recorded in demo session";
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

export function readRequiredReasonHeader(request) {
  const raw =
    request.headers.get("X-Change-Reason") ||
    request.headers.get("x-change-reason") ||
    "";
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

export function readIdempotencyKey(request) {
  return (
    request.headers.get("Idempotency-Key") ||
    request.headers.get("idempotency-key") ||
    ""
  );
}

export function requireKernelMutationHeaders(request) {
  const idempotencyKey = readIdempotencyKey(request);
  if (!idempotencyKey.trim()) {
    return HttpResponse.json(
      {
        type: "https://cadence.clinical/errors/K.Idempotency.KeyRequired",
        title: "Missing Idempotency-Key",
        status: 400,
        code: "K.Idempotency.KeyRequired",
      },
      { status: 400 }
    );
  }
  const reason = readRequiredReasonHeader(request);
  if (!reason.trim()) {
    return HttpResponse.json(
      {
        type: "https://cadence.clinical/errors/K.ReasonForChange.Required",
        title: "Missing X-Change-Reason",
        status: 400,
        code: "K.ReasonForChange.Required",
      },
      { status: 400 }
    );
  }
  return null;
}

export async function readJsonBody(request) {
  try {
    return await request.json();
  } catch {
    return {};
  }
}
