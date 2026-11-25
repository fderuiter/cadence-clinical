import {
  requestStepUpSignature as requestSharedStepUpSignature,
  resolveDefaultSemanticAction,
  type StepUpSignatureRequestInput,
  type StepUpVerificationPayload,
} from "ui";
import { unwrapResponse } from "./client";
import { openApiClient } from "./legacyClient";

export { resolveDefaultSemanticAction };
export type { StepUpSignatureRequestInput, StepUpVerificationPayload };

export interface StepUpSignatureResponse {
  sig_token: string;
  expires_in?: number;
  [key: string]: unknown;
}

export interface WebStepUpSignatureOptions {
  requireSigToken?: boolean;
  missingTokenMessage?: string;
  requestOptions?: Record<string, unknown>;
}

/**
 * Dispatches a 21 CFR Part 11 step-up verification request (`POST /api/v1/auth/signature-verification`)
 * via the typed gateway HTTP client without pulling domain-specific API clients into caller chunks.
 */
export function requestStepUpSignature<TResponse = StepUpSignatureResponse>(
  input: StepUpSignatureRequestInput,
  options: WebStepUpSignatureOptions = {}
): Promise<TResponse> {
  const { requestOptions = {}, requireSigToken, missingTokenMessage } = options;
  return requestSharedStepUpSignature<TResponse>(input, {
    requireSigToken,
    missingTokenMessage,
    post: (path, body) =>
      unwrapResponse<TResponse>(
        (openApiClient as any).POST(path, {
          body,
          ...requestOptions,
        })
      ),
  });
}
