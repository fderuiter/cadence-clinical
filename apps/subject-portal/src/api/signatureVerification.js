import {
  requestStepUpSignature as requestSharedStepUpSignature,
  resolveDefaultSemanticAction,
} from "ui";
import { dispatchApi } from "./client.js";

export { resolveDefaultSemanticAction };

export const PORTAL_MISSING_SIG_TOKEN_MESSAGE =
  "Signature verification service is unavailable or did not issue a signature token. Nothing was saved.";

/**
 * Dispatches a 21 CFR Part 11 step-up verification request (`POST /api/v1/auth/signature-verification`)
 * for the subject portal and validates that a non-empty `sig_token` was issued.
 */
export function requestStepUpSignature(input, options = {}) {
  return requestSharedStepUpSignature(input, {
    missingTokenMessage: PORTAL_MISSING_SIG_TOKEN_MESSAGE,
    ...options,
    post: (path, body) =>
      dispatchApi(path.replace(/^\/+/, ""), {
        method: "POST",
        body: JSON.stringify(body),
      }),
  });
}
