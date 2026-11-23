import "fake-indexeddb/auto";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { setupServer } from "msw/node";
import { getApiBaseUrl } from "../src/api/client.js";
import { login, logout } from "../src/features/auth.js";
import {
  normalizePortalBasePath,
  resolvePortalServiceWorkerOptions,
  startMswWorker,
} from "../src/mocks/browser.js";
import { createPortalDemoStore } from "../src/mocks/demoHandlers.js";
import { buildPortalGatewayHandlers } from "../src/mocks/handlers.js";
import { createPortalHistory } from "../src/router/index.js";
import { state } from "../src/stores/state.js";

const PORTAL_HEADERS = {
  Authorization: "Bearer demo-portal-token",
  "Content-Type": "application/json",
};

describe("Subject Portal Demo Isolation & Stateful MSW Handlers", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it("activates local demo participant in login() without contacting Keycloak in demo builds", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const fetchSpy = vi.spyOn(globalThis, "fetch");

    logout();
    expect(state.session.userId).toBeNull();

    await login();

    expect(state.session.userId).toBe("SUBJ-101-001");
    expect(state.session.studyId).toBe("CADENCE-101");
    expect(state.session.siteId).toBe("SITE-101");
    expect(state.session.isOfflineMode).toBe(true);
    expect(state.session.isDemoMode).toBe(true);
    expect(state.session.token).toBeNull();

    const keycloakCalls = fetchSpy.mock.calls.filter(([url]) =>
      String(url).includes("8080")
    );
    expect(keycloakCalls).toEqual([]);
  });

  it("resolves same-origin API base URL in demo builds and localhost:8000 in standard builds", () => {
    vi.stubEnv("VITE_API_BASE_URL", "");
    vi.stubEnv("VITE_DEMO_MODE", "false");
    expect(getApiBaseUrl()).toBe("http://localhost:8000");

    vi.stubEnv("VITE_DEMO_MODE", "true");
    expect(getApiBaseUrl()).toBe(window.location.origin);
    expect(getApiBaseUrl()).not.toContain("localhost:8000");
  });

  it("resolves single-scope unified service worker options (/subject-portal/sw.js?msw=1)", async () => {
    expect(normalizePortalBasePath("/subject-portal/")).toBe(
      "/subject-portal/"
    );
    expect(normalizePortalBasePath("subject-portal")).toBe("/subject-portal/");
    expect(resolvePortalServiceWorkerOptions("/subject-portal/")).toEqual({
      url: "/subject-portal/sw.js?msw=1",
      options: { scope: "/subject-portal/" },
    });

    const fakeWorker = {
      start: vi.fn().mockResolvedValue({ scope: "/subject-portal/" }),
    };
    const registration = await startMswWorker({
      worker: fakeWorker,
      baseUrl: "/subject-portal/",
      env: { MODE: "demo" },
      quiet: true,
    });

    expect(registration).toEqual({ scope: "/subject-portal/" });
    expect(fakeWorker.start).toHaveBeenCalledWith({
      onUnhandledFrame: "error",
      quiet: true,
      serviceWorker: {
        url: "/subject-portal/sw.js?msw=1",
        options: { scope: "/subject-portal/" },
      },
    });

    expect(
      resolvePortalServiceWorkerOptions("/cadence-clinical/subject-portal/")
    ).toEqual({
      url: "/cadence-clinical/subject-portal/sw.js?msw=1",
      options: { scope: "/cadence-clinical/subject-portal/" },
    });

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
      let startupSettled = false;
      const pendingStartup = startMswWorker({
        worker: fakeWorker,
        baseUrl: "/subject-portal/",
        env: { MODE: "demo" },
        quiet: true,
      }).then((res) => {
        startupSettled = true;
        return res;
      });

      await Promise.resolve();
      await Promise.resolve();
      expect(startupSettled).toBe(false);

      fakeServiceWorker.controller = {
        scriptURL: "/subject-portal/sw.js?msw=1",
      };
      for (const cb of [...listeners]) {
        cb();
      }

      const settledReg = await pendingStartup;
      expect(startupSettled).toBe(true);
      expect(settledReg).toEqual({ scope: "/subject-portal/" });
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

  it("uses hash history in demo builds for subpath deep-link safety and web history in standard builds", () => {
    const demoHistory = createPortalHistory(
      { MODE: "demo" },
      { memory: false, base: "/cadence-clinical/subject-portal/" }
    );
    expect(demoHistory.base).toContain("#");
    expect(demoHistory.base).toContain("/cadence-clinical/subject-portal/");

    const prodHistory = createPortalHistory(
      { MODE: "production", VITE_DEMO_MODE: "false" },
      { memory: false, base: "/subject-portal/" }
    );
    expect(prodHistory.base).not.toContain("#");
  });

  describe("Fail-closed portal boundary & stateful session persistence", () => {
    let store;
    let server;
    const origin = window.location.origin;

    beforeEach(() => {
      store = createPortalDemoStore();
      server = setupServer(
        ...buildPortalGatewayHandlers({ forceDemo: true, store })
      );
      server.listen({ onUnhandledRequest: "error" });
    });

    afterEach(() => {
      server.close();
    });

    it("fails closed on unmocked portal API routes (501) and off-origin requests (503)", async () => {
      const unmockedRes = await fetch(
        `${origin}/api/v1/interop/unknown-demo-route`,
        { headers: PORTAL_HEADERS }
      );
      expect(unmockedRes.status).toBe(501);
      const unmockedBody = await unmockedRes.json();
      expect(unmockedBody.error).toBe("DEMO_UNMOCKED_ENDPOINT");

      const offOriginRes = await fetch("http://localhost:8080/realms/cadence");
      expect(offOriginRes.status).toBe(503);
      const offOriginBody = await offOriginRes.json();
      expect(offOriginBody.error).toBe("DEMO_OFF_ORIGIN_BLOCKED");
    });

    it("persists notification acknowledgements, ePRO submissions, and re-consent completion", async () => {
      const ackRes = await fetch(
        `${origin}/api/v1/interop/notifications/notif_01/acknowledge`,
        {
          method: "POST",
          headers: {
            ...PORTAL_HEADERS,
            "X-Change-Reason": "Participant read reminder",
          },
          body: JSON.stringify({}),
        }
      );
      expect(ackRes.status).toBe(200);

      const notifsRes = await fetch(
        `${origin}/api/v1/interop/subjects/SUBJ-101-001/notifications`,
        { headers: PORTAL_HEADERS }
      );
      expect(notifsRes.status).toBe(200);
      const notifs = await notifsRes.json();
      expect(notifs.find((n) => n.id === "notif_01")?.is_read).toBe(true);

      const submitRes = await fetch(`${origin}/api/v1/interop/epro/submit`, {
        method: "POST",
        headers: {
          ...PORTAL_HEADERS,
          "X-Change-Reason": "Daily diary submission",
        },
        body: JSON.stringify({
          assignment_id: "assign_01",
          subject_id: "SUBJ-101-001",
          responses: { vssbp: 120, vsdpb: 78, vshr: 68, pain_scale: 2 },
        }),
      });
      expect(submitRes.status).toBe(200);

      const assignRes = await fetch(
        `${origin}/api/v1/interop/assignments/subject/SUBJ-101-001`,
        { headers: PORTAL_HEADERS }
      );
      expect(assignRes.status).toBe(200);
      const assignments = await assignRes.json();
      expect(assignments.find((a) => a.id === "assign_01")?.status).toBe(
        "COMPLETED"
      );

      const pendingBefore = await (
        await fetch(
          `${origin}/api/v1/econsent/reconsent/pending/CADENCE-101?subject_pseudonym=SUBJ-101-001`,
          { headers: PORTAL_HEADERS }
        )
      ).json();
      expect(pendingBefore.length).toBe(1);
      expect(pendingBefore[0].study_id).toBe("CADENCE-101");

      const completeRes = await fetch(
        `${origin}/api/v1/econsent/reconsent/complete/${pendingBefore[0].id}`,
        {
          method: "POST",
          headers: {
            ...PORTAL_HEADERS,
            "X-Change-Reason": "Protocol Amendment Re-Consent Acknowledgment",
          },
          body: JSON.stringify({
            subject_pseudonym: "SUBJ-101-001",
          }),
        }
      );
      expect(completeRes.status).toBe(200);

      const pendingAfter = await (
        await fetch(
          `${origin}/api/v1/econsent/reconsent/pending/CADENCE-101?subject_pseudonym=SUBJ-101-001`,
          { headers: PORTAL_HEADERS }
        )
      ).json();
      expect(pendingAfter).toEqual([]);
    });
  });
});
