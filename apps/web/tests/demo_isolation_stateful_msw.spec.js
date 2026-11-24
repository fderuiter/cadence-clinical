import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { setupServer } from "msw/node";
import { createPinia, setActivePinia } from "pinia";
import { getApiBaseUrl } from "../src/api/authorization.js";
import { createOpenApiClient, getBaseUrl } from "../src/api/client";
import ProposalReview from "../src/components/ai/ProposalReview.vue";
import {
  ensureServiceWorkerController,
  normalizeBasePath,
  resolveWebServiceWorkerOptions,
  startMswWorker,
} from "../src/mocks/browser.js";
import {
  WEB_DEMO_SLICE_NAMES,
  createWebDemoStore,
} from "../src/mocks/demoStore.js";
import { buildWebGatewayHandlers } from "../src/mocks/handlers.js";
import {
  AVAILABLE_SCREENS,
  DEMO_AVAILABLE_SCREENS,
  isScreenAvailable,
} from "../src/router/availability.js";
import { createAppHistory, router } from "../src/router";
import { useAuthStore } from "../src/stores/auth.js";

const DEMO_HEADERS = {
  Authorization: "Bearer demo-token-001",
  "Content-Type": "application/json",
};

describe("Isolated static demo builds & gateway boundary isolation (web)", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  describe("Base-aware MSW worker registration & startup", () => {
    it("resolves mockServiceWorker.js relative to BASE_URL with matching scope", () => {
      expect(normalizeBasePath("/cadence-clinical/")).toBe(
        "/cadence-clinical/"
      );
      expect(normalizeBasePath("cadence-clinical")).toBe("/cadence-clinical/");
      expect(normalizeBasePath("")).toBe("/");

      expect(resolveWebServiceWorkerOptions("/cadence-clinical/")).toEqual({
        url: "/cadence-clinical/mockServiceWorker.js",
        options: { scope: "/cadence-clinical/" },
      });
      expect(resolveWebServiceWorkerOptions("/")).toEqual({
        url: "/mockServiceWorker.js",
        options: { scope: "/" },
      });
      expect(resolveWebServiceWorkerOptions("/cadence-clinical/demo/")).toEqual(
        {
          url: "/cadence-clinical/demo/mockServiceWorker.js",
          options: { scope: "/cadence-clinical/demo/" },
        }
      );
    });

    it("uses hash history in demo builds for subpath deep-link safety and web history in standard builds", () => {
      const demoHistory = createAppHistory(
        { MODE: "demo" },
        "/cadence-clinical/demo/"
      );
      expect(demoHistory.base).toContain("#");
      expect(demoHistory.base).toContain("/cadence-clinical/demo/");

      const prodHistory = createAppHistory(
        { MODE: "production", VITE_DEMO_MODE: "false" },
        "/cadence-clinical/"
      );
      expect(prodHistory.base).not.toContain("#");
    });

    it("awaits worker.start with error unhandled-frame mode in demo builds", async () => {
      let markStarted;
      const startBarrier = new Promise((resolve) => {
        markStarted = resolve;
      });
      const fakeWorker = {
        start: vi.fn(() => startBarrier),
      };

      let settled = false;
      const startup = startMswWorker({
        worker: fakeWorker,
        baseUrl: "/cadence-clinical/",
        env: { MODE: "demo" },
        quiet: true,
      }).then((res) => {
        settled = true;
        return res;
      });

      expect(settled).toBe(false);
      markStarted({ scope: "/cadence-clinical/" });
      const registration = await startup;

      expect(settled).toBe(true);
      expect(registration).toEqual({ scope: "/cadence-clinical/" });
      expect(fakeWorker.start).toHaveBeenCalledWith({
        onUnhandledFrame: "error",
        quiet: true,
        serviceWorker: {
          url: "/cadence-clinical/mockServiceWorker.js",
          options: { scope: "/cadence-clinical/" },
        },
      });
    });

    it("waits for controllerchange when navigator.serviceWorker.ready resolves before controller is set", async () => {
      const originalDescriptor = Object.getOwnPropertyDescriptor(
        globalThis.navigator,
        "serviceWorker"
      );
      const listeners = new Set();
      const fakeServiceWorker = {
        controller: null,
        ready: Promise.resolve({ active: {} }),
        addEventListener: vi.fn((event, cb) => {
          if (event === "controllerchange") {
            listeners.add(cb);
          }
        }),
        removeEventListener: vi.fn((event, cb) => {
          if (event === "controllerchange") {
            listeners.delete(cb);
          }
        }),
      };

      Object.defineProperty(globalThis.navigator, "serviceWorker", {
        configurable: true,
        value: fakeServiceWorker,
      });

      try {
        let settled = false;
        const waitPromise = ensureServiceWorkerController(1000).then(() => {
          settled = true;
        });

        await Promise.resolve();
        await Promise.resolve();
        expect(settled).toBe(false);

        fakeServiceWorker.controller = { scriptURL: "/mockServiceWorker.js" };
        for (const cb of [...listeners]) {
          cb();
        }

        await waitPromise;
        expect(settled).toBe(true);
      } finally {
        if (originalDescriptor) {
          Object.defineProperty(
            globalThis.navigator,
            "serviceWorker",
            originalDescriptor
          );
        } else {
          delete globalThis.navigator.serviceWorker;
        }
      }
    });
  });

  describe("Same-origin API base URL isolation in demo mode", () => {
    it("targets window.location.origin in demo builds and localhost:8000 in standard builds when unconfigured", () => {
      vi.stubEnv("VITE_API_BASE_URL", "");
      vi.stubEnv("VITE_DEMO_MODE", "false");
      expect(getApiBaseUrl()).toBe("http://localhost:8000");
      expect(getBaseUrl()).toBe("http://localhost:8000");

      vi.stubEnv("VITE_DEMO_MODE", "true");
      expect(getApiBaseUrl()).toBe(window.location.origin);
      expect(getBaseUrl()).toBe(window.location.origin);
      expect(getApiBaseUrl()).not.toContain("localhost:8000");
    });
  });

  describe("Fail-closed demo gateway boundary & slice composition", () => {
    let store;
    let server;
    const origin = window.location.origin;

    beforeEach(() => {
      store = createWebDemoStore();
      server = setupServer(
        ...buildWebGatewayHandlers({ forceDemo: true, store })
      );
      server.listen({ onUnhandledRequest: "error" });
    });

    afterEach(() => {
      server.close();
    });

    it("fails closed on unmocked API routes (501) and off-origin requests (503)", async () => {
      const unmockedRes = await fetch(
        `${origin}/api/v1/unimplemented/demo-check`,
        {
          headers: DEMO_HEADERS,
        }
      );
      expect(unmockedRes.status).toBe(501);
      const unmockedBody = await unmockedRes.json();
      expect(unmockedBody.error).toBe("DEMO_UNMOCKED_ENDPOINT");

      const offOriginApi = await fetch("http://localhost:8080/realms/cadence");
      expect(offOriginApi.status).toBe(503);
      const offOriginBody = await offOriginApi.json();
      expect(offOriginBody.error).toBe("DEMO_OFF_ORIGIN_BLOCKED");
    });

    it("composes all 9 clinical domain slices with shared demo-fixtures routes and isolated instance state", async () => {
      expect(WEB_DEMO_SLICE_NAMES).toEqual([
        "kernel",
        "protocol-soa",
        "execution-ecrf",
        "ctms-ssu",
        "etmf-documents",
        "payments-wallet",
        "knowledge-tickets",
        "safety-labs-dct",
        "training-locks",
      ]);
      expect(Object.keys(store.slices)).toEqual(WEB_DEMO_SLICE_NAMES);

      const healthRes = await fetch(`${origin}/api/v1/health`, {
        headers: DEMO_HEADERS,
      });
      expect(healthRes.status).toBe(200);
      expect(await healthRes.json()).toEqual({
        status: "healthy",
        mode: "demo",
      });

      const sigRes = await fetch(
        `${origin}/api/v1/auth/signature-verification`,
        {
          method: "POST",
          headers: DEMO_HEADERS,
          body: JSON.stringify({
            action: "/api/v1/kernel/library-items/approve",
            semantic_action: "collection.sign",
          }),
        }
      );
      expect(sigRes.status).toBe(200);
      const sigBody = await sigRes.json();
      expect(sigBody.sig_token).toMatch(/^demo_sig_tok_/);
      expect(sigBody.semantic_action).toBe("collection.sign");

      const isolatedPeer = createWebDemoStore();
      store.upgradeSubscriptionTier("Select");
      expect(store.getEntitlements().tier).toBe("Select");
      expect(isolatedPeer.getEntitlements().tier).toBe("Complete");
    });
  });

  describe("Demo route availability & router guard behavior", () => {
    it("unlocks DEMO_AVAILABLE_SCREENS only in demo builds while preserving AVAILABLE_SCREENS in standard builds", () => {
      expect(AVAILABLE_SCREENS.has("mdr")).toBe(false);
      expect(DEMO_AVAILABLE_SCREENS.has("mdr")).toBe(true);

      expect(isScreenAvailable("mdr", { MODE: "production" })).toBe(false);
      expect(isScreenAvailable("rules", { MODE: "test" })).toBe(false);
      expect(isScreenAvailable("mdr", { MODE: "demo" })).toBe(true);
      expect(isScreenAvailable("rules", { VITE_DEMO_MODE: "true" })).toBe(true);
      expect(isScreenAvailable("nonexistent-route", { MODE: "demo" })).toBe(
        false
      );
    });

    it("navigates to /mdr in demo mode without redirecting to /unavailable", async () => {
      setActivePinia(createPinia());
      const authStore = useAuthStore();

      vi.stubEnv("VITE_DEMO_MODE", "true");
      authStore.startDemoSession();

      await router.push("/mdr");
      await router.isReady();

      expect(router.currentRoute.value.name).toBe("mdr");
    });

    it("rewrites default createOpenApiClient requests when VITE_API_BASE_URL updates after client construction", async () => {
      vi.stubEnv("VITE_API_BASE_URL", "");
      const client = createOpenApiClient();

      const seenRequests = [];
      vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
        const req = input instanceof Request ? input : new Request(input);
        seenRequests.push({
          url: req.url,
          method: req.method,
          body:
            req.method === "GET" || req.method === "HEAD"
              ? null
              : await req.text(),
        });
        return new Response(JSON.stringify({ ok: true }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      });

      vi.stubEnv("VITE_API_BASE_URL", "http://127.0.0.1:45678");
      await client.POST("/api/v1/kernel/observations/correct", {
        body: { value: "125" },
      });

      expect(seenRequests).toHaveLength(1);
      expect(seenRequests[0].url).toBe(
        "http://127.0.0.1:45678/api/v1/kernel/observations/correct"
      );
      expect(seenRequests[0].method).toBe("POST");
      expect(JSON.parse(seenRequests[0].body)).toEqual({ value: "125" });
    });
  });

  describe("Governed AI proposal review & deterministic rejection (ADR-2635)", () => {
    let store;
    let server;

    beforeEach(() => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      store = createWebDemoStore();
      server = setupServer(
        ...buildWebGatewayHandlers({ forceDemo: true, store })
      );
      server.listen({ onUnhandledRequest: "error" });
    });

    afterEach(() => {
      server.close();
    });

    it("seeds Open AutomationAgent proposal on CADENCE-101, resolves plan/proposal nodes, and records deterministic Rejected decision in ProposalReview.vue", async () => {
      const pinia = createPinia();
      setActivePinia(pinia);
      const authStore = useAuthStore();
      authStore.startDemoSession();
      authStore.switchPersona("sponsor_designer");

      const origin = window.location.origin;
      const openListRes = await fetch(
        `${origin}/api/v1/kernel/studies/CADENCE-101/proposals?status=Open&limit=5`,
        { headers: DEMO_HEADERS }
      );
      expect(openListRes.status).toBe(200);
      const openProposals = await openListRes.json();
      expect(openProposals).toHaveLength(1);
      const seeded = openProposals[0];
      expect(seeded.status).toBe("Open");
      expect(seeded.proposed_by_kind).toBe("AutomationAgent");
      expect(seeded.model_id).toBe("cadence-protocol-guardian-1.2");

      const planNodeRes = await fetch(
        `${origin}/api/v1/kernel/nodes/${encodeURIComponent(seeded.plan.ref)}`,
        { headers: DEMO_HEADERS }
      );
      expect(planNodeRes.status).toBe(200);
      const planNode = await planNodeRes.json();
      expect(planNode.kind).toBe("plan");
      expect(planNode.title).toContain("Protocol Amendment 2");

      const propNodeRes = await fetch(
        `${origin}/api/v1/kernel/nodes/${encodeURIComponent(seeded.ref)}`,
        { headers: DEMO_HEADERS }
      );
      expect(propNodeRes.status).toBe(200);
      const propNode = await propNodeRes.json();
      expect(propNode.kind).toBe("proposal");
      expect(propNode.status).toBe("Open");

      await router.push("/studies/CADENCE-101");
      await router.isReady();

      const wrapper = mount(ProposalReview, {
        props: {
          studyCode: "CADENCE-101",
          proposalRef: seeded.ref,
          reviewer: "qa_director",
        },
        global: { plugins: [pinia, router] },
      });
      await vi.waitFor(() => {
        expect(wrapper.find("#proposal-review-ai").exists()).toBe(true);
      });

      expect(wrapper.find("#proposal-review-state").text()).toBe("Open");
      expect(wrapper.find("#proposal-approve-blockers").text()).toContain(
        "source"
      );

      // Open both sources (plan + protocol_version)
      await wrapper.find("#proposal-source-open-0").trigger("click");
      await flushPromises();
      await wrapper.find("#proposal-source-open-1").trigger("click");
      await vi.waitFor(() => {
        expect(wrapper.find("#proposal-review-open-rule").text()).toContain(
          "2 of 2 opened"
        );
      });

      // Reject proposal with reason
      await wrapper
        .find("#proposal-reject-reason")
        .setValue(
          "Week 4 PK trough window cannot widen beyond +/-2 days per clinical pharmacology plan."
        );
      await wrapper.find("#proposal-reject-form").trigger("submit");
      await vi.waitFor(() => {
        expect(wrapper.find("#proposal-review-state").text()).toBe("Rejected");
      });

      expect(wrapper.find("#proposal-review-decision-text").text()).toContain(
        "Rejected by"
      );
      expect(wrapper.find("#proposal-review-decision-text").text()).toContain(
        "Week 4 PK trough window"
      );

      wrapper.unmount();
    });
  });
});
