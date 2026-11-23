import { SEMANTIC_ACTIONS } from "ui";
import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/imaging";
import { soaClient } from "./soaClient";

type ImagingSchemas = components["schemas"];

export type ChairAdjudicateImagingRequest =
  ImagingSchemas["Imaging_ChairAdjudicateImagingRequest"];
export type AdjudicationSessionResponse =
  ImagingSchemas["Imaging_AdjudicationSessionResponse"];

export function imagingChairAdjudicateAction(sessionId: string): string {
  return `/api/v1/imaging/adjudications/${encodeURIComponent(sessionId.trim())}/chair-adjudicate`;
}

const client = createOpenApiClient<paths>();

export const imagingApi = {
  /**
   * Executes committee chair tie-breaker adjudication with a verified single-use 21 CFR Part 11 `X-Sig-Token`.
   */
  chairAdjudicateSession(
    sessionId: string,
    payload: ChairAdjudicateImagingRequest,
    sigToken: string,
    changeReason?: string
  ): Promise<AdjudicationSessionResponse> {
    if (!sigToken || !sigToken.trim()) {
      throw new Error(
        "Step-up X-Sig-Token is required for imaging chair adjudication."
      );
    }
    const reason =
      changeReason?.trim() ||
      payload.adjudicationRationale?.trim() ||
      payload.signatureMeaning?.trim() ||
      "Imaging committee chair adjudication";
    return unwrapResponse(
      client.POST(
        "/api/v1/imaging/adjudications/{sessionId}/chair-adjudicate",
        {
          params: { path: { sessionId: sessionId.trim() } },
          body: payload,
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "X-Sig-Token": sigToken.trim(),
          },
        }
      )
    );
  },

  /**
   * Performs step-up credential verification and submits imaging chair adjudication.
   */
  async verifyAndChairAdjudicateSession({
    sessionId,
    username,
    password,
    totp = null,
    payload,
    changeReason,
  }: {
    sessionId: string;
    username: string;
    password: string;
    totp?: string | null;
    payload: ChairAdjudicateImagingRequest;
    changeReason?: string;
  }): Promise<AdjudicationSessionResponse> {
    const verification = await soaClient.verifySignature({
      username,
      password,
      totp,
      action: imagingChairAdjudicateAction(sessionId),
      semanticAction: SEMANTIC_ACTIONS.IMAGING_CHAIR_ADJUDICATE,
    });
    return this.chairAdjudicateSession(
      sessionId,
      payload,
      verification.sig_token,
      changeReason
    );
  },
};
