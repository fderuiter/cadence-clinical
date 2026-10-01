import { http, HttpResponse, passthrough } from "msw";
import { validateGatewayRequest } from "ui";

export const gatewayHandlers = [
  http.post("*/api/v1/payments/disburse", async ({ request }) => {
    const body = await request.json().catch(() => ({}));
    const subjectId = body.subject_id || "subject_001";
    const amount = typeof body.amount === "number" ? body.amount : 25.0;
    const seq = body.sequence_number || 1;
    return HttpResponse.json({
      success: true,
      id: "disb_" + Date.now(),
      wallet_id: "wallet_" + subjectId,
      subject_id: subjectId,
      amount: amount,
      new_balance: amount * seq,
      currency: "USD",
      status: "COMPLETED",
      reference_number: "DISB-MOCK-" + seq,
      idempotency_key: body.idempotency_key || `seq_${seq}_${subjectId}`,
      dispatched_at: new Date().toISOString(),
    });
  }),

  http.post(
    "*/api/v1/payments/participant/wallets/disburse",
    async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const subjectId = body.subject_id || "subject_001";
      const amount = typeof body.amount === "number" ? body.amount : 25.0;
      return HttpResponse.json({
        success: true,
        id: "disb_" + Date.now(),
        wallet_id: "wallet_" + subjectId,
        subject_id: subjectId,
        amount: amount,
        new_balance: amount,
        currency: "USD",
        status: "COMPLETED",
        reference_number: "DISB-MOCK",
        idempotency_key: body.idempotency_key || `disburse_${subjectId}`,
        dispatched_at: new Date().toISOString(),
      });
    }
  ),

  // Intercept all HTTP methods matching API gateway host or API paths
  http.all("*", async ({ request }) => {
    const url = new URL(request.url);

    // Only intercept API / service calls targeting gateway or non-asset paths
    const isApiCall =
      url.port === "8000" ||
      url.host.includes("localhost:8000") ||
      url.pathname.startsWith("/api/") ||
      url.pathname.startsWith("/designer/") ||
      url.pathname.startsWith("/execution/") ||
      url.pathname.startsWith("/etmf/") ||
      url.pathname.startsWith("/interop/") ||
      url.pathname.startsWith("/ctms/") ||
      url.pathname.startsWith("/notifications/") ||
      url.pathname.startsWith("/quality/") ||
      url.pathname.startsWith("/safety/") ||
      url.pathname.startsWith("/tickets/") ||
      url.pathname.startsWith("/eisf/") ||
      url.pathname.startsWith("/org/") ||
      url.pathname.startsWith("/econsent/") ||
      url.pathname.startsWith("/terminology/") ||
      url.pathname.startsWith("/invalid-prefix/");

    if (!isApiCall) {
      return passthrough();
    }

    const validation = await validateGatewayRequest(request);

    if (!validation.valid) {
      return HttpResponse.json(validation.body, {
        status: validation.status,
        headers: { "Content-Type": "application/json" },
      });
    }

    // Default passthrough for valid gateway requests in live dev/mock mode
    return passthrough();
  }),
];
