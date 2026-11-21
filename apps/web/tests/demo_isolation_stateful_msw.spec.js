import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { setupServer } from "msw/node";
import { createPinia, setActivePinia } from "pinia";
import { getApiBaseUrl } from "../src/api/authorization.js";
import { getBaseUrl } from "../src/api/client";
import {
  ensureServiceWorkerController,
  normalizeBasePath,
  resolveWebServiceWorkerOptions,
  startMswWorker,
} from "../src/mocks/browser.js";
import { createWebDemoStore } from "../src/mocks/demoStore.js";
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

describe("Isolated static demo builds & stateful MSW handlers (web)", () => {
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

  describe("Fail-closed demo boundary & stateful OpenAPI handlers", () => {
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

    it("persists subject enrollment and state transitions across subsequent reads", async () => {
      const initialRes = await fetch(
        `${origin}/api/v1/execution/subjects?study_id=DEMO-01`,
        { headers: DEMO_HEADERS }
      );
      expect(initialRes.status).toBe(200);
      const initialPage = await initialRes.json();
      expect(initialPage.items.length).toBe(2);

      const createRes = await fetch(`${origin}/api/v1/execution/subjects`, {
        method: "POST",
        headers: {
          ...DEMO_HEADERS,
          "X-Change-Reason": "Enroll synthetic participant",
        },
        body: JSON.stringify({
          study_id: "DEMO-01",
          site_id: "SITE-101",
          subject_id: "SUBJ-1004",
          status: "SCREENED",
        }),
      });
      expect(createRes.status).toBe(201);
      const created = await createRes.json();
      expect(created.subject_id).toBe("SUBJ-1004");

      const patchRes = await fetch(
        `${origin}/api/v1/execution/subjects/${created.id}/state`,
        {
          method: "PATCH",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "Confirm eligibility",
          },
          body: JSON.stringify({ status: "ENROLLED" }),
        }
      );
      expect(patchRes.status).toBe(200);

      const updatedRes = await fetch(
        `${origin}/api/v1/execution/subjects?study_id=DEMO-01`,
        { headers: DEMO_HEADERS }
      );
      const updatedPage = await updatedRes.json();
      expect(updatedPage.items.length).toBe(3);
      const persisted = updatedPage.items.find(
        (subj) => subj.id === created.id
      );
      expect(persisted?.status).toBe("ENROLLED");
    });

    it("persists eCRF form value saves, signatures, and query workflow transitions", async () => {
      const openRes = await fetch(`${origin}/api/v1/execution/forms`, {
        method: "POST",
        headers: {
          ...DEMO_HEADERS,
          "X-Change-Reason": "Open baseline vitals form",
        },
        body: JSON.stringify({
          enrollment_id: "00000000-0000-0000-0000-000000001001",
          visit_code: "VISIT-BASELINE",
          form_code: "FORM-VITALS",
        }),
      });
      expect(openRes.status).toBe(201);
      const opened = await openRes.json();

      const stateBeforeRes = await fetch(
        `${origin}/api/v1/execution/forms/${opened.form_id}`,
        { headers: DEMO_HEADERS }
      );
      const stateBefore = await stateBeforeRes.json();
      const sbpBefore = stateBefore.items.find(
        (it) => it.item_id === "item-sysbp"
      );
      expect(sbpBefore.value.value).toBe("128");

      const saveRes = await fetch(
        `${origin}/api/v1/execution/forms/${opened.form_id}/values`,
        {
          method: "PUT",
          headers: {
            ...DEMO_HEADERS,
            "Idempotency-Key": "idem-save-1",
            "X-Change-Reason": "Source worksheet update",
          },
          body: JSON.stringify({
            values: [
              {
                item_id: "item-sysbp",
                expected_revision: sbpBefore.revision,
                value: {
                  type: "quantity",
                  value: "122",
                  unit: "mm[Hg]",
                  comparator: null,
                },
              },
            ],
          }),
        }
      );
      expect(saveRes.status).toBe(200);

      const raiseQueryRes = await fetch(
        `${origin}/api/v1/execution/forms/${opened.form_id}/queries`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "Verify cuff position",
          },
          body: JSON.stringify({
            observation_id: sbpBefore.observation_id,
            revision: 2,
            text: "Please confirm seated 5-minute rest before reading.",
          }),
        }
      );
      expect(raiseQueryRes.status).toBe(201);
      const raised = await raiseQueryRes.json();
      expect(raised.status).toBe("open");

      const answerRes = await fetch(
        `${origin}/api/v1/execution/forms/${opened.form_id}/queries/${raised.query_id}/answer`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "Site coordinator response",
          },
          body: JSON.stringify({
            text: "Confirmed seated rest per protocol section 6.2.",
          }),
        }
      );
      expect(answerRes.status).toBe(200);
      const answered = await answerRes.json();
      expect(answered.status).toBe("answered");

      const signRes = await fetch(
        `${origin}/api/v1/execution/forms/${opened.form_id}/signatures`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "Investigator review complete",
          },
          body: JSON.stringify({ meaning: "approved" }),
        }
      );
      expect(signRes.status).toBe(201);

      const rereadRes = await fetch(
        `${origin}/api/v1/execution/forms/${opened.form_id}`,
        { headers: DEMO_HEADERS }
      );
      const reread = await rereadRes.json();
      const sbpAfter = reread.items.find((it) => it.item_id === "item-sysbp");
      expect(sbpAfter.value.value).toBe("122");
      expect(sbpAfter.revision).toBe(2);
      expect(reread.signatures.length).toBe(1);

      const listRes = await fetch(
        `${origin}/api/v1/execution/forms?enrollment_id=00000000-0000-0000-0000-000000001001`, // deid-ignore
        { headers: DEMO_HEADERS }
      );
      expect(listRes.status).toBe(200);
      const summaries = await listRes.json();
      const baselineSummary = summaries.find(
        (s) => s.form_id === opened.form_id
      );
      expect(baselineSummary).toMatchObject({
        complete: true,
        missing_required_count: 0,
        open_query_count: 1,
        answered_query_count: 1,
        signed: true,
        signature_superseded: false,
        signature_count: 1,
      });

      const unscheduledRes = await fetch(`${origin}/api/v1/execution/forms`, {
        method: "POST",
        headers: {
          ...DEMO_HEADERS,
          "X-Change-Reason": "Open unscheduled adverse event form",
        },
        body: JSON.stringify({
          enrollment_id: "00000000-0000-0000-0000-000000001001", // deid-ignore
          collection_spec_id: "form-adverse-event",
          planned_activity_id: null,
          collected_at: "2026-08-05T14:00:00Z", // deid-ignore
        }),
      });
      expect(unscheduledRes.status).toBe(201);
      const unscheduled = await unscheduledRes.json();
      expect(unscheduled.planned_activity_id).toBeNull();
      expect(unscheduled.collection_title).toBe("Adverse Event (CDASH AE)");
    });

    it("persists kernel observation corrections and enforces expected_version conflict checks", async () => {
      const obsRef = "observation:00000000-0000-0000-0000-000000000001";
      const getRes = await fetch(
        `${origin}/api/v1/kernel/observations/${encodeURIComponent(obsRef)}`,
        { headers: DEMO_HEADERS }
      );
      expect(getRes.status).toBe(200);
      const initialObs = await getRes.json();
      expect(initialObs.version).toBe(1);

      const correctRes = await fetch(
        `${origin}/api/v1/kernel/observations/${encodeURIComponent(obsRef)}/corrections`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "Idempotency-Key": "idem-obs-correct-1",
            "X-Change-Reason": "Transcription correction against source",
          },
          body: JSON.stringify({
            expected_version: 1,
            value: {
              type: "quantity",
              value: "124",
              unit: "mm[Hg]",
              comparator: null,
            },
          }),
        }
      );
      expect(correctRes.status).toBe(200);

      const staleRes = await fetch(
        `${origin}/api/v1/kernel/observations/${encodeURIComponent(obsRef)}/corrections`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "Idempotency-Key": "idem-obs-stale",
            "X-Change-Reason": "Stale version check",
          },
          body: JSON.stringify({
            expected_version: 1,
            value: {
              type: "quantity",
              value: "120",
              unit: "mm[Hg]",
              comparator: null,
            },
          }),
        }
      );
      expect(staleRes.status).toBe(409);

      const rereadRes = await fetch(
        `${origin}/api/v1/kernel/observations/${encodeURIComponent(obsRef)}`,
        { headers: DEMO_HEADERS }
      );
      const updatedObs = await rereadRes.json();
      expect(updatedObs.version).toBe(2);
      expect(updatedObs.value.value).toBe("124");
      expect(updatedObs.revisions.length).toBe(2);
      expect(updatedObs.revisions[1].number).toBe(2);
    });

    it("persists site activation, SSU certification, and contextual help resolution", async () => {
      const certifyRes = await fetch(
        `${origin}/api/v1/ctms/startup/sites/SITE-102/greenlight/certify?study_id=DEMO-01&country_code=US`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "SSU readiness sign-off",
          },
        }
      );
      expect(certifyRes.status).toBe(200);

      const matrixRes = await fetch(
        `${origin}/api/v1/ctms/startup/matrix?country_code=US`,
        { headers: DEMO_HEADERS }
      );
      expect(matrixRes.status).toBe(200);
      const matrix = await matrixRes.json();
      const site102 = matrix.find((s) => s.site_id === "SITE-102");
      expect(site102?.overall_status).toBe("APPROVED");
      expect(site102?.greenlight_token).toContain("SITE-102");

      const activateRes = await fetch(
        `${origin}/api/v1/execution/sites/SITE-101/activate`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "Greenlight package verified",
          },
          body: JSON.stringify({ study_id: "DEMO-01" }),
        }
      );
      expect(activateRes.status).toBe(200);

      const helpRes = await fetch(
        `${origin}/api/v1/knowledge/contextual-help?route=/ecrf&persona=super_admin`,
        { headers: DEMO_HEADERS }
      );
      expect(helpRes.status).toBe(200);
      const help = await helpRes.json();
      expect(help.primary_article).toBeTruthy();
    });

    it("supports kernel SDV verifications, retractions, and study-wide query worklist filters", async () => {
      const obsRef = "observation:00000000-0000-0000-0000-000000000001";
      const enrollmentRef = "enrollment:00000000-0000-0000-0000-000000001001";

      const verifyRes = await fetch(
        `${origin}/api/v1/kernel/observations/${encodeURIComponent(obsRef)}/verifications`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "Idempotency-Key": "idem-sdv-1",
            "X-Change-Reason": "Verified against EHR source",
          },
          body: JSON.stringify({
            enrollment_ref: enrollmentRef,
            revision: 1,
          }),
        }
      );
      expect(verifyRes.status).toBe(201);
      const verified = await verifyRes.json();
      expect(verified.kind).toBe("Verified");
      expect(verified.ref).toMatch(/^assertion:/);

      const retractRes = await fetch(
        `${origin}/api/v1/kernel/assertions/${encodeURIComponent(verified.ref)}/retraction`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "Idempotency-Key": "idem-retract-1",
            "X-Change-Reason": "Withdrawn after chart update",
          },
          body: JSON.stringify({
            enrollment_ref: enrollmentRef,
          }),
        }
      );
      expect(retractRes.status).toBe(200);
      const retracted = await retractRes.json();
      expect(retracted.retracts).toBe(verified.ref);

      const studyQueriesRes = await fetch(
        `${origin}/api/v1/execution/forms/queries?study_id=00000000-0000-0000-0000-000000000001&status=open&site=SITE-101`,
        { headers: DEMO_HEADERS }
      );
      expect(studyQueriesRes.status).toBe(200);
      const studyQueries = await studyQueriesRes.json();
      expect(studyQueries.length).toBeGreaterThanOrEqual(1);
      expect(studyQueries[0].site_code).toBe("SITE-101");
      expect(studyQueries[0].subject_code).toBe("SUBJ-1001");
    });

    it("lists participant forms for Clinical Canvas and resolves study_site enrollment children and zoom lineage", async () => {
      const studyRef = "study:00000000-0000-0000-0000-000000000001";
      const studyChildrenRes = await fetch(
        `${origin}/api/v1/kernel/nodes/${encodeURIComponent(studyRef)}/children`,
        { headers: DEMO_HEADERS }
      );
      expect(studyChildrenRes.status).toBe(200);
      const studyChildren = await studyChildrenRes.json();
      const site101 = studyChildren.find(
        (c) => c.node.kind === "study_site" && c.node.code === "SITE-101"
      );
      expect(site101).toBeTruthy();

      const siteChildrenRes = await fetch(
        `${origin}/api/v1/kernel/nodes/${encodeURIComponent(site101.node.ref)}/children`,
        { headers: DEMO_HEADERS }
      );
      expect(siteChildrenRes.status).toBe(200);
      const siteChildren = await siteChildrenRes.json();
      const subj1001 = siteChildren.find(
        (c) => c.node.kind === "enrollment" && c.node.code === "SUBJ-1001"
      );
      expect(subj1001?.node.ref).toBe(
        "enrollment:00000000-0000-0000-0000-000000001001"
      );

      const zoomPathRes = await fetch(
        `${origin}/api/v1/kernel/nodes/${encodeURIComponent(subj1001.node.ref)}/path`,
        { headers: DEMO_HEADERS }
      );
      expect(zoomPathRes.status).toBe(200);
      const zoomSteps = await zoomPathRes.json();
      expect(zoomSteps.find((s) => s.level === "study_site")?.label).toBe(
        "SITE-101"
      );
      expect(zoomSteps.find((s) => s.level === "enrollment")?.label).toBe(
        "SUBJ-1001"
      );

      const missingEnrollmentRes = await fetch(
        `${origin}/api/v1/execution/forms`,
        { headers: DEMO_HEADERS }
      );
      expect(missingEnrollmentRes.status).toBe(400);

      const enrollmentId = "00000000-0000-0000-0000-000000001001";
      const listRes = await fetch(
        `${origin}/api/v1/execution/forms?enrollment_id=${enrollmentId}`,
        { headers: DEMO_HEADERS }
      );
      expect(listRes.status).toBe(200);
      const initialForms = await listRes.json();
      expect(initialForms.length).toBe(1);
      expect(initialForms[0].collection_spec_id).toBe("form-vital-signs");
      expect(initialForms[0].collection_title).toBe("Vital Signs (CDASH VS)");
      expect(initialForms[0].item_count).toBe(4);
      expect(initialForms[0].recorded_count).toBe(4);
      expect(initialForms[0].complete).toBe(true);

      const openChemRes = await fetch(`${origin}/api/v1/execution/forms`, {
        method: "POST",
        headers: {
          ...DEMO_HEADERS,
          "X-Change-Reason": "Open chemistry panel in canvas",
        },
        body: JSON.stringify({
          enrollment_id: enrollmentId,
          collection_spec_id: "form-chem-panel",
          planned_activity_id: "visit-week4",
        }),
      });
      expect(openChemRes.status).toBe(201);

      const updatedListRes = await fetch(
        `${origin}/api/v1/execution/forms?enrollment_id=${enrollmentId}`,
        { headers: DEMO_HEADERS }
      );
      const updatedForms = await updatedListRes.json();
      expect(updatedForms.length).toBe(2);
      const chemSummary = updatedForms.find(
        (f) => f.collection_spec_id === "form-chem-panel"
      );
      expect(chemSummary?.collection_title).toBe(
        "Clinical Chemistry Panel (CDASH LB)"
      );
      expect(chemSummary?.complete).toBe(false);
    });

    it("serves stateful kernel entitlements and in-place tier upgrades without hitting 501 boundary", async () => {
      const initialRes = await fetch(`${origin}/api/v1/kernel/entitlements`, {
        headers: DEMO_HEADERS,
      });
      expect(initialRes.status).toBe(200);
      const initialEntitlements = await initialRes.json();
      expect(initialEntitlements.tier).toBe("Complete");

      const upgradeRes = await fetch(
        `${origin}/api/v1/kernel/entitlements/tier`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "Idempotency-Key": "demo-tier-select-1",
            "X-Change-Reason": "Evaluate Select tier in demo",
          },
          body: JSON.stringify({ tier: "Select" }),
        }
      );
      expect(upgradeRes.status).toBe(200);
      const upgraded = await upgradeRes.json();
      expect(upgraded.tier).toBe("Select");
      expect(upgraded.product_families).toEqual([
        "Blueprint",
        "Capture",
        "Orbit",
        "Engage",
        "Allocate",
      ]);

      const subsequentRes = await fetch(
        `${origin}/api/v1/kernel/entitlements`,
        { headers: DEMO_HEADERS }
      );
      const subsequent = await subsequentRes.json();
      expect(subsequent.tier).toBe("Select");

      setActivePinia(createPinia());
      vi.stubEnv("VITE_DEMO_MODE", "true");
      const authStore = useAuthStore();
      authStore.startDemoSession();
      const loaded = await authStore.loadEntitlements({ force: true });
      expect(loaded?.tier).toBe("Select");
      expect(authStore.entitlements?.tier).toBe("Select");
    });

    it("enforces RTSM scheme separation of duties, consent and kit gates, blinded subject detail, and emergency unblinding", async () => {
      const sampleIso = new Date(Date.UTC(2027, 5, 14)).toISOString();

      // 1. Draft v2 scheme as stat.author and verify separation-of-duties blocks self-approval
      const draftRes = await fetch(
        `${origin}/api/v1/execution/randomization/schemes`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-User-Id": "stat.author",
            "X-Change-Reason": "Draft v2 permuted block schedule",
          },
          body: JSON.stringify({
            study_id: "DEMO-01",
            method: "permuted_block",
            arms: { ACTIVE: 1, PLACEBO: 1 },
            stratification_factors: { AGE_GROUP: ["ADULT", "SENIOR"] },
            requires_kit: true,
          }),
        }
      );
      expect(draftRes.status).toBe(201);
      const drafted = await draftRes.json();
      expect(drafted.status).toBe("DRAFT");
      expect(drafted.version).toBe(2);

      const selfApproveRes = await fetch(
        `${origin}/api/v1/execution/randomization/schemes/${drafted.id}/approve`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-User-Id": "stat.author",
            "X-Sig-Token": "demo_sig_tok_1",
            "X-Change-Reason": "Attempt self-approval",
          },
        }
      );
      expect(selfApproveRes.status).toBe(403);

      const secondApproveRes = await fetch(
        `${origin}/api/v1/execution/randomization/schemes/${drafted.id}/approve`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-User-Id": "stat.approver",
            "X-Sig-Token": "demo_sig_tok_2",
            "X-Change-Reason": "Independent SAP verification",
          },
        }
      );
      expect(secondApproveRes.status).toBe(200);
      const approved = await secondApproveRes.json();
      expect(approved.status).toBe("APPROVED");

      // 2. Seed site kits and verify blind-safe inventory omits _treatment_arm
      const seedRes = await fetch(
        `${origin}/api/v1/execution/rtsm/kits/seed-inventory`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "Seed depot shipment for SITE-101",
          },
          body: JSON.stringify({
            study_id: "DEMO-01",
            site_id: "SITE-101",
            kits: [
              {
                kit_number: "KIT-2001",
                treatment_arm: "ACTIVE",
                lot_number: "LOT-DEMO-2",
                expiration_date: sampleIso,
              },
            ],
          }),
        }
      );
      expect(seedRes.status).toBe(200);

      const invRes = await fetch(
        `${origin}/api/v1/execution/rtsm/kits/inventory/DEMO-01/SITE-101`,
        { headers: DEMO_HEADERS }
      );
      expect(invRes.status).toBe(200);
      const inventory = await invRes.json();
      const seededKit = inventory.find((k) => k.kit_number === "KIT-2001");
      expect(seededKit).toBeTruthy();
      expect(seededKit._treatment_arm).toBeUndefined();
      expect(seededKit.treatment_arm).toBeUndefined();

      // 3. Direct PATCH to RANDOMIZED is rejected with 409 RANDOMIZATION_REQUIRES_ALLOCATION
      const enrollmentUuid = "00000000-0000-0000-0000-000000001001"; // deid-ignore
      const directPatchRes = await fetch(
        `${origin}/api/v1/execution/subjects/${enrollmentUuid}/state`,
        {
          method: "PATCH",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "Attempt direct RANDOMIZED state change",
          },
          body: JSON.stringify({ status: "RANDOMIZED" }),
        }
      );
      expect(directPatchRes.status).toBe(409);
      const directPatchBody = await directPatchRes.json();
      expect(directPatchBody.code).toBe("RANDOMIZATION_REQUIRES_ALLOCATION");

      // 4. Record explicit-date consent and randomize SUBJ-1001 via step-up signature
      const consentRes = await fetch(
        `${origin}/api/v1/execution/subjects/${enrollmentUuid}/consent`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "Signed ICF verified",
          },
          body: JSON.stringify({
            protocol_version: {
              study_id: "DEMO-01",
              version_tag: "2.0",
              version_index: 2,
            },
            icf_signed: true,
            icf_signed_date: sampleIso,
          }),
        }
      );
      expect(consentRes.status).toBe(200);

      const randRes = await fetch(
        `${origin}/api/v1/execution/subjects/${enrollmentUuid}/randomize`,
        {
          method: "POST",
          headers: {
            ...DEMO_HEADERS,
            "X-Sig-Token": "demo_sig_tok_rand",
            "X-Change-Reason": "Eligible for allocation",
          },
          body: JSON.stringify({
            stratification_factors: { AGE_GROUP: "SENIOR" },
          }),
        }
      );
      expect(randRes.status).toBe(200);
      const allocation = await randRes.json();
      expect(allocation.status).toBe("RANDOMIZED");
      expect(allocation.kit_number).toBeTruthy();
      expect(allocation.treatment_group).toBeUndefined();

      // 5. Blinded GET masks treatment_group; X-Unblinded-Access reveals it
      const blindedDetailRes = await fetch(
        `${origin}/api/v1/execution/subjects/SUBJ-1001`,
        { headers: DEMO_HEADERS }
      );
      const blindedDetail = await blindedDetailRes.json();
      expect(blindedDetail.status).toBe("RANDOMIZED");
      expect(blindedDetail.treatment_group).toBeNull();
      expect(blindedDetail.investigational_product_id).toBeNull();

      const unblindedDetailRes = await fetch(
        `${origin}/api/v1/execution/subjects/SUBJ-1001`,
        {
          headers: {
            ...DEMO_HEADERS,
            "X-Unblinded-Access": "true",
          },
        }
      );
      const unblindedDetail = await unblindedDetailRes.json();
      expect(unblindedDetail.treatment_group).toBe("ACTIVE");
      expect(unblindedDetail.investigational_product_id).toBe(
        allocation.kit_number
      );

      // 6. Emergency unblinding enforces >= 50-character clinical justification
      const shortUnblindRes = await fetch(
        `${origin}/api/v1/execution/subjects/${enrollmentUuid}/state`,
        {
          method: "PATCH",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason": "Too short reason",
          },
          body: JSON.stringify({ status: "UNBLINDED" }),
        }
      );
      expect(shortUnblindRes.status).toBe(400);

      const validUnblindRes = await fetch(
        `${origin}/api/v1/execution/subjects/${enrollmentUuid}/state`,
        {
          method: "PATCH",
          headers: {
            ...DEMO_HEADERS,
            "X-Change-Reason":
              "Suspected severe anaphylactic reaction requiring immediate antidote selection in ER.",
          },
          body: JSON.stringify({ status: "UNBLINDED" }),
        }
      );
      expect(validUnblindRes.status).toBe(200);
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
  });
});
