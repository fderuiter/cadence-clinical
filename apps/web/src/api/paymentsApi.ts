import { SEMANTIC_ACTIONS } from "ui";
import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/payments";
import { soaClient } from "./soaClient";

type PaymentsSchemas = components["schemas"];

export type SignRemittanceRequest =
  PaymentsSchemas["Payments_SignRemittanceRequest"];

export function remittanceSignAction(invoiceId: string): string {
  return `/api/v1/payments/invoices/${encodeURIComponent(invoiceId.trim())}/remittance-advice/sign`;
}

const client = createOpenApiClient<paths>();

export const paymentsApi = {
  /**
   * Signs an invoice remittance advice statement with a verified single-use 21 CFR Part 11 `X-Sig-Token`.
   */
  signRemittanceAdvice(
    invoiceId: string,
    payload: SignRemittanceRequest,
    sigToken: string,
    changeReason?: string
  ): Promise<unknown> {
    if (!sigToken || !sigToken.trim()) {
      throw new Error(
        "Step-up X-Sig-Token is required to sign remittance advice."
      );
    }
    const reason =
      changeReason?.trim() ||
      (payload as Record<string, unknown>)?.signature_reason?.toString() ||
      "Sign remittance advice";
    return unwrapResponse(
      client.POST("/api/v1/payments/invoices/{id}/remittance-advice/sign", {
        params: { path: { id: invoiceId.trim() } },
        body: payload,
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "X-Sig-Token": sigToken.trim(),
        },
      })
    );
  },

  /**
   * Performs step-up credential verification and signs an invoice remittance advice statement.
   */
  async verifyAndSignRemittanceAdvice({
    invoiceId,
    username,
    password,
    totp = null,
    payload,
    changeReason,
  }: {
    invoiceId: string;
    username: string;
    password: string;
    totp?: string | null;
    payload: SignRemittanceRequest;
    changeReason?: string;
  }): Promise<unknown> {
    const verification = await soaClient.verifySignature({
      username,
      password,
      totp,
      action: remittanceSignAction(invoiceId),
      semanticAction: SEMANTIC_ACTIONS.PAYMENTS_REMITTANCE_SIGN,
    });
    return this.signRemittanceAdvice(
      invoiceId,
      payload,
      verification.sig_token,
      changeReason
    );
  },
};
