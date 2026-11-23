import {
  STEP_UP_VERIFICATION_ENDPOINT,
  verifyStepUpSignature,
  type StepUpVerificationResponse,
} from "ui";
import openApiClient, { unwrapResponse } from "./client";

export interface SignatureVerificationRequest {
  username?: string | null;
  password?: string;
  totp?: string | null;
  action: string;
  semanticAction?: string;
  semantic_action?: string;
  batchId?: string | null;
  batch_id?: string | null;
}

export interface SignatureVerificationOptions extends Record<string, unknown> {
  missingTokenMessage?: string;
}

/**
 * Verifies 21 CFR Part 11 step-up re-authentication credentials against
 * `POST /api/v1/auth/signature-verification` and ensures a non-empty `sig_token` is issued.
 */
export function verifySignature(
  payload: SignatureVerificationRequest,
  options: SignatureVerificationOptions = {}
): Promise<StepUpVerificationResponse> {
  const { missingTokenMessage, ...requestOptions } = options;
  return verifyStepUpSignature(
    {
      ...payload,
      password: payload?.password || "",
    },
    (body) =>
      unwrapResponse<Record<string, unknown>>(
        (openApiClient as any).POST(STEP_UP_VERIFICATION_ENDPOINT, {
          body,
          ...requestOptions,
        })
      ),
    { missingTokenMessage }
  );
}

export const signatureApi = {
  verifySignature,
};

export default signatureApi;
