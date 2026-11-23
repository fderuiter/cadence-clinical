import "fake-indexeddb/auto";
import { describe, expect, it } from "vitest";
import { setupServer } from "msw/node";
import { PORTAL_DEMO_SLICE_NAMES } from "../src/mocks/demoHandlers.js";
import { createPortalGatewayInstance } from "../src/mocks/handlers.js";

const PORTAL_HEADERS = {
  Authorization: "Bearer demo-portal-token",
  "Content-Type": "application/json",
};

describe("Subject Portal Instance-Scoped MSW Slices", () => {
  it("isolates interop-epro, econsent, and wallet-payments state across independent portal gateway instances", async () => {
    const gatewayA = createPortalGatewayInstance({ forceDemo: true });
    const gatewayB = createPortalGatewayInstance({ forceDemo: true });

    expect(PORTAL_DEMO_SLICE_NAMES).toEqual([
      "interop-epro",
      "econsent",
      "wallet-payments",
    ]);
    expect(Object.keys(gatewayA.store.slices)).toEqual(PORTAL_DEMO_SLICE_NAMES);
    expect(gatewayA.store).not.toBe(gatewayB.store);

    const origin = window.location.origin;
    const server = setupServer(...gatewayA.handlers);
    server.listen({ onUnhandledRequest: "error" });

    try {
      const healthRes = await fetch(`${origin}/api/v1/health`, {
        headers: PORTAL_HEADERS,
      });
      expect(healthRes.status).toBe(200);
      expect(await healthRes.json()).toEqual({
        status: "healthy",
        portal: "demo",
      });

      const expenseRes = await fetch(
        `${origin}/api/v1/payments/participant/me/expenses`,
        {
          method: "POST",
          headers: PORTAL_HEADERS,
          body: JSON.stringify({
            subjectId: "SUBJ-101-001",
            category: "PARKING",
            grossAmount: 24,
            merchantName: "Hospital Garage",
            ocrConfidence: 0.96,
          }),
        }
      );
      expect(expenseRes.status).toBe(200);
      expect((await expenseRes.json()).status).toBe("APPROVED");

      const submitRes = await fetch(`${origin}/api/v1/interop/epro/submit`, {
        method: "POST",
        headers: {
          ...PORTAL_HEADERS,
          "X-Change-Reason": "Daily symptom check",
        },
        body: JSON.stringify({
          assignment_id: "assign_01",
          subject_id: "SUBJ-101-001",
          responses: { pain_scale: 1 },
        }),
      });
      expect(submitRes.status).toBe(200);

      const reconsentRes = await fetch(
        `${origin}/api/v1/execution/amendments/reconsent`,
        {
          method: "POST",
          headers: PORTAL_HEADERS,
          body: JSON.stringify({
            study_id: "CADENCE-101",
            subject_id: "SUBJ-101-001",
            target_version: "2.0",
          }),
        }
      );
      expect(reconsentRes.status).toBe(200);
      expect((await reconsentRes.json()).status).toBe("RECORDED");

      const completeRes = await fetch(
        `${origin}/api/v1/econsent/reconsent/complete/req_amendment_v2`,
        {
          method: "POST",
          headers: {
            ...PORTAL_HEADERS,
            "X-Change-Reason": "Participant acknowledged v2.0",
          },
          body: JSON.stringify({ subject_pseudonym: "SUBJ-101-001" }),
        }
      );
      expect(completeRes.status).toBe(200);
      expect(gatewayA.store.state.pendingReconsents).toEqual([]);
      expect(gatewayA.store.state.walletLedger.size).toBe(1);

      // Instance B retains pristine state with zero module-scoped mutable globals
      expect(gatewayB.store.state.pendingReconsents.length).toBe(1);
      expect(gatewayB.store.state.walletLedger.size).toBe(0);
      expect(
        gatewayB.store.state.assignments.find((a) => a.id === "assign_01")
          ?.status
      ).not.toBe("COMPLETED");

      // Resetting Instance A restores all slices
      gatewayA.reset();
      expect(gatewayA.store.state.pendingReconsents.length).toBe(1);
      expect(gatewayA.store.state.walletLedger.size).toBe(0);
    } finally {
      server.close();
    }
  });
});
