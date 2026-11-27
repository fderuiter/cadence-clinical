import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { getResponse } from "msw";
import AmendmentDiffView from "@/views/AmendmentDiffView.vue";
import { AVAILABLE_SCREENS } from "@/router/availability";
import { router as appRouter } from "@/router";
import { useAuthStore } from "@/stores/auth";
import { useStudyCanvasStore } from "@/stores/studyCanvas";
import { createWebDemoStore } from "@/mocks/demoStore";
import { buildWebGatewayHandlers } from "@/mocks/handlers";

function collectSourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "node_modules" || entry.name === "dist") continue;
      collectSourceFiles(full, acc);
    } else if (/\.(ts|js|vue)$/.test(entry.name)) {
      acc.push(full);
    }
  }
  return acc;
}

describe("Consolidated Protocol Amendments & Stateful MSW Kernel Governance", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Designer"];
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("retires all legacy designer/amendments and execution/amendments references across apps/web", () => {
    const webRoot = join(__dirname, "..", "..");
    const files = [
      ...collectSourceFiles(join(webRoot, "src")),
      ...collectSourceFiles(join(webRoot, "tests")),
    ].filter((file) => !file.endsWith("AmendmentDiffView.spec.ts"));

    const legacyDesignerPath = ["designer", "amendments"].join("/");
    const legacyExecutionPath = ["execution", "amendments"].join("/");

    const offending = files.filter((file) => {
      const text = readFileSync(file, "utf8");
      return (
        text.includes(legacyDesignerPath) || text.includes(legacyExecutionPath)
      );
    });

    expect(offending).toEqual([]);
    expect(AVAILABLE_SCREENS.has("amendment-diff")).toBe(true);
  });

  it("redirects /amendment-diff to /studies/:studyCode?stage=design&section=protocol-versions", async () => {
    const canvasStore = useStudyCanvasStore();
    canvasStore.studyCode = "CADENCE-101";

    await appRouter.push("/amendment-diff");
    await appRouter.isReady();

    expect(appRouter.currentRoute.value.name).toBe("study");
    expect(appRouter.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(appRouter.currentRoute.value.query).toMatchObject({
      stage: "design",
      section: "protocol-versions",
    });
  });

  it("mounts consolidated AmendmentDiffView with ProtocolVersionsSection and Part 11 Governance modal", async () => {
    const demoStore = createWebDemoStore();
    const handlers = buildWebGatewayHandlers({
      forceDemo: true,
      store: demoStore,
    });
    const origFetch = globalThis.fetch;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url =
          input instanceof Request
            ? input.url
            : new URL(String(input), "http://localhost").href;
        const headers = new Headers(
          input instanceof Request ? input.headers : init?.headers
        );
        headers.set("X-Demo-Persona", "sponsor_designer");
        const req = new Request(url, {
          method:
            init?.method || (input instanceof Request ? input.method : "GET"),
          headers,
          body:
            init?.body ??
            (input instanceof Request &&
            input.method !== "GET" &&
            input.method !== "HEAD"
              ? await input.clone().text()
              : undefined),
        });
        const res = await getResponse(handlers, req);
        return res ?? new Response("Not handled", { status: 501 });
      })
    );

    try {
      const stubRouter = createRouter({
        history: createMemoryHistory(),
        routes: [
          {
            path: "/studies/:studyCode?",
            name: "study",
            component: { template: "<div />" },
          },
        ],
      });
      await stubRouter.push("/studies/CADENCE-101");
      await stubRouter.isReady();

      const wrapper = mount(AmendmentDiffView, {
        global: { plugins: [pinia, stubRouter] },
        attachTo: document.body,
      });
      await flushPromises();

      expect(wrapper.find("#amendment-diff-view").exists()).toBe(true);
      expect(wrapper.find("#protocol-versions-section").exists()).toBe(true);
      expect(
        wrapper
          .find("[data-testid='open-canvas-protocol-versions-link']")
          .exists()
      ).toBe(true);

      await wrapper
        .find("[data-testid='open-amendment-governance-modal']")
        .trigger("click");
      await flushPromises();
      expect(wrapper.text()).toContain(
        "21 CFR Part 11 Protocol Amendment Governance"
      );

      wrapper.unmount();
    } finally {
      vi.stubGlobal("fetch", origFetch);
    }
  });

  it("executes fork, submit-for-review, simulate, save simulation report, Reviewed sign-off, approval-readiness, approve, and withdraw in MSW without HTTP 501", async () => {
    const demoStore = createWebDemoStore();
    const handlers = buildWebGatewayHandlers({
      forceDemo: true,
      store: demoStore,
    });

    let idempSeq = 0;
    async function dispatchDemo(
      method: string,
      path: string,
      body?: unknown,
      extraHeaders: Record<string, string> = {}
    ) {
      const headers = new Headers({
        "X-Demo-Persona": "sponsor_designer",
        ...extraHeaders,
      });
      if (method !== "GET") {
        headers.set("Idempotency-Key", `demo-gov-${++idempSeq}`);
        headers.set("X-Change-Reason", "Automated MSW governance verification");
        if (body !== undefined) {
          headers.set("Content-Type", "application/json");
        }
      }
      const req = new Request(`http://localhost${path}`, {
        method,
        headers,
        body: body !== undefined ? JSON.stringify(body) : undefined,
      });
      const res = await getResponse(handlers, req);
      expect(res, `${method} ${path} should be handled`).toBeDefined();
      expect(res!.status, `${method} ${path} must not return 501`).not.toBe(
        501
      );
      const json = await res!.json();
      return { status: res!.status, body: json };
    }

    // 1. List initial versions -> Version 2 is Approved
    const initialList = await dispatchDemo(
      "GET",
      "/api/v1/kernel/studies/CADENCE-101/protocol-versions"
    );
    expect(initialList.status).toBe(200);
    const v2 = initialList.body.find((v: any) => v.number === 2);
    expect(v2.status).toBe("Approved");

    // 2. Fork Version 3 Draft from Version 2
    const forked = await dispatchDemo(
      "POST",
      `/api/v1/kernel/protocol-versions/${v2.ref}/fork`,
      {
        intent: "Extend Week 4 window to +/-3 days",
        rationale: "Reduce out-of-window deviations",
        classification: "NonSubstantial",
        transition_policy: "NewParticipantsOnly",
      }
    );
    expect(forked.status).toBe(201);
    expect(forked.body.number).toBe(3);
    expect(forked.body.status).toBe("Draft");
    const v3Ref = String(forked.body.ref).split("@")[0];

    // 3. Submit Version 3 for review -> InReview
    const submitted = await dispatchDemo(
      "POST",
      `/api/v1/kernel/protocol-versions/${v3Ref}/submit-for-review`
    );
    expect(submitted.status).toBe(200);
    expect(submitted.body.status).toBe("InReview");

    // 4. Readiness before simulation report save -> ready: false
    const unready = await dispatchDemo(
      "GET",
      `/api/v1/kernel/protocol-versions/${v3Ref}/approval-readiness`
    );
    expect(unready.status).toBe(200);
    expect(unready.body.ready).toBe(false);
    expect(unready.body.simulation_report_saved).toBe(false);
    expect(unready.body.review_signed).toBe(false);
    expect(unready.body.blocking_code).toBe(
      "K.Protocol.SimulationReviewRequired"
    );

    // 5. Attempting approve before simulation review returns 409 K.Protocol.SimulationReviewRequired
    const blockedApprove = await dispatchDemo(
      "POST",
      `/api/v1/kernel/protocol-versions/${v3Ref}/approve`,
      { meaning: "Approved" },
      { "X-Sig-Token": "demo_sig_tok_blocked" }
    );
    expect(blockedApprove.status).toBe(409);
    expect(blockedApprove.body.code).toBe(
      "K.Protocol.SimulationReviewRequired"
    );

    // 6. Simulate candidate against parent and save SimulationReport
    const simulated = await dispatchDemo(
      "POST",
      "/api/v1/kernel/simulation/simulate",
      {
        stored_version_ref: v3Ref,
        base_version_ref: v2.ref,
      }
    );
    expect(simulated.status).toBe(200);
    expect(simulated.body.structural_diff.length).toBeGreaterThan(0);

    const savedReport = await dispatchDemo(
      "POST",
      "/api/v1/kernel/studies/CADENCE-101/simulation-reports",
      {
        title: "Protocol Version 3 Simulation Report",
        result: simulated.body,
      }
    );
    expect(savedReport.status).toBe(201);
    expect(savedReport.body.kind).toBe("SimulationReport");
    const reportVerRef = savedReport.body.artifact_version_ref;

    // 7. Sign Part 11 Reviewed attestation on the SimulationReport
    const reviewed = await dispatchDemo(
      "POST",
      `/api/v1/kernel/artifact-versions/${reportVerRef}/review`,
      {
        meaning: "Reviewed",
        expected_content_hash: savedReport.body.content_hash,
      },
      { "X-Sig-Token": "demo_sig_tok_review" }
    );
    expect(reviewed.status).toBe(200);
    expect(reviewed.body.meaning).toBe("Reviewed");

    // 8. Readiness is now ready: true with review_attestation_ref
    const ready = await dispatchDemo(
      "GET",
      `/api/v1/kernel/protocol-versions/${v3Ref}/approval-readiness`
    );
    expect(ready.status).toBe(200);
    expect(ready.body.ready).toBe(true);
    expect(ready.body.simulation_report_saved).toBe(true);
    expect(ready.body.review_signed).toBe(true);
    expect(ready.body.review_attestation_ref).toBe(
      reviewed.body.attestation_ref
    );

    // 9. Approve Version 3 with Part 11 signature
    const approved = await dispatchDemo(
      "POST",
      `/api/v1/kernel/protocol-versions/${v3Ref}/approve`,
      {
        meaning: "Approved",
        expected_content_hash: submitted.body.content_hash,
        review_attestation_ref: ready.body.review_attestation_ref,
      },
      { "X-Sig-Token": "demo_sig_tok_approve" }
    );
    expect(approved.status).toBe(200);
    expect(approved.body.status).toBe("Approved");

    // 10. Withdraw Version 3 with Part 11 signature
    const withdrawn = await dispatchDemo(
      "POST",
      `/api/v1/kernel/protocol-versions/${v3Ref}/withdraw`,
      {
        meaning: "Withdrawn",
        expected_content_hash: submitted.body.content_hash,
      },
      { "X-Sig-Token": "demo_sig_tok_withdraw" }
    );
    expect(withdrawn.status).toBe(200);
    expect(withdrawn.body.status).toBe("Withdrawn");
  });
});
