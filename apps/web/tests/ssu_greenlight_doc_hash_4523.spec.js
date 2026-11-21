import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createHash } from "node:crypto";
import SsuGreenlightConsole from "../src/components/clinical/SsuGreenlightConsole.vue";
import { useAuthStore } from "../src/stores/auth";
import { useToastStore } from "../src/stores/toasts";
import { signInAs } from "./helpers/signIn.js";

const EMPTY_SHA256 =
  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

/**
 * #4523 / #4829: an essential document is staged to /api/v1/etmf/intelligence/stage-qc through the
 * authenticated OpenAPI client with the SHA-256 of the selected file, and HTTP failures are surfaced.
 */
describe("SsuGreenlightConsole essential document staging (#4523)", () => {
  let pinia;
  let recordedStageCalls;
  let submitResponse;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    signInAs(useAuthStore(), ["Data Manager"], {
      username: "dm_user",
      token: "live-jwt-token",
    });
    recordedStageCalls = [];
    submitResponse = () =>
      new Response(
        JSON.stringify({
          documentId: "doc-1",
          studyId: "PROTOCOL-2026-01",
          siteId: "SITE-101",
          filename: "FDA_1572_Site101.pdf",
          status: "TECHNICAL_QC",
          stagedAt: "2026-10-01T10:00:00Z",
          stagedBy: "dm_user",
          qcAssignedTo: null,
          discrepancyNotes: [],
          intelligenceReport: {
            filename: "FDA_1572_Site101.pdf",
            mimeType: "application/pdf",
            sha256Hash: "abc123",
            modality: "PDF_BINARY",
            confidenceTier: "HIGH",
            qcRecommendation: "AUTO_CLASSIFY",
            taxonomyVersion: "v3.2.0-complete",
            primaryClassification: {
              zone: 5,
              section: "05.02",
              artifactCode: "05.02.02",
              artifactType: "Form FDA 1572",
              confidence: 0.96,
              rationale: "Matched Form FDA 1572",
            },
            alternativeCandidates: [],
          },
        }),
        {
          status: 201,
          headers: { "Content-Type": "application/json" },
        }
      );

    const fetchMock = vi.fn(async (input, init) => {
      const url =
        typeof input === "string"
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url;
      const method =
        init?.method ?? (input instanceof Request ? input.method : "GET");

      if (
        url.endsWith("/api/v1/etmf/intelligence/stage-qc") &&
        method === "POST"
      ) {
        const rawBody =
          typeof init?.body === "string"
            ? init.body
            : input && typeof input.clone === "function"
              ? await input.clone().text()
              : "{}";
        const authHeader =
          input && input.headers && typeof input.headers.get === "function"
            ? input.headers.get("Authorization")
            : new Headers(init?.headers).get("Authorization");
        recordedStageCalls.push({
          url,
          method,
          authHeader,
          body: JSON.parse(rawBody),
        });
        return submitResponse();
      }

      if (url.includes("/greenlight")) {
        return new Response(
          JSON.stringify({
            id: "gate-site-101",
            study_id: "PROTOCOL-2026-01",
            site_id: "SITE-101",
            site_name: "Site SITE-101",
            country_code: "US",
            overall_status: "BLOCKED",
            readiness_percentage: 50,
            facet_regulatory_irb: false,
            facet_cta_budget: true,
            facet_eisf_baseline: false,
            facet_doa_log: true,
            facet_staff_training: true,
            facet_ip_release: false,
            is_compliant: false,
            greenlight_token: null,
            greenlight_certified_by: null,
            greenlight_certified_at: null,
            is_overridden: false,
            unfulfilled_reasons: ["Pending IRB"],
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        );
      }

      return new Response("[]", {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    });

    vi.stubGlobal("fetch", fetchMock);
    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.spyOn(console, "warn").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  async function mountConsole() {
    const wrapper = mount(SsuGreenlightConsole, {
      props: { initialViewMode: "single" },
      global: { plugins: [pinia] },
    });
    await flushPromises();
    useToastStore().toasts = [];
    wrapper.vm.showUploadDocModal = true;
    await wrapper.vm.$nextTick();
    return wrapper;
  }

  it("refuses to submit without a selected file", async () => {
    const wrapper = await mountConsole();

    await wrapper.vm.submitDoc();

    expect(recordedStageCalls).toHaveLength(0);
    expect(useToastStore().toasts[0]).toMatchObject({ tone: "error" });
    expect(wrapper.vm.showUploadDocModal).toBe(true);
  });

  it("sends Bearer auth and the SHA-256 of the selected file's bytes to /api/v1/etmf/intelligence/stage-qc", async () => {
    const bytes = "%PDF-1.7 signed FDA 1572";
    const expectedHash = createHash("sha256").update(bytes).digest("hex");
    const wrapper = await mountConsole();
    const input = wrapper.find('[data-testid="ssu-doc-file"]');
    const file = new File([bytes], "FDA_1572_Site101.pdf", {
      type: "application/pdf",
    });
    Object.defineProperty(input.element, "files", { value: [file] });
    await input.trigger("change");

    await wrapper.vm.submitDoc();
    await flushPromises();

    expect(recordedStageCalls).toHaveLength(1);
    const [call] = recordedStageCalls;
    expect(call.authHeader).toBe("Bearer live-jwt-token");
    expect(call.body.filename).toBe("FDA_1572_Site101.pdf");
    expect(call.body.studyId).toBe("PROTOCOL-2026-01");
    expect(call.body.siteId).toBe("SITE-101");
    expect(call.body.artifactHint).toContain(`[sha256:${expectedHash}]`);
    expect(call.body.artifactHint).not.toContain(EMPTY_SHA256);
    expect(wrapper.vm.showUploadDocModal).toBe(false);
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it.each([500, 502, 504])(
    "reports HTTP %i and keeps the submission dialog open",
    async (status) => {
      submitResponse = () =>
        new Response(JSON.stringify({ title: "Gateway failure" }), {
          status,
          headers: { "Content-Type": "application/problem+json" },
        });
      const wrapper = await mountConsole();
      wrapper.vm.docForm.file = new File(["x"], "IRB.pdf");

      await wrapper.vm.submitDoc();
      await flushPromises();

      expect(recordedStageCalls).toHaveLength(1);
      expect(wrapper.vm.showUploadDocModal).toBe(true);
      const toasts = useToastStore().toasts;
      expect(toasts[0]).toMatchObject({ tone: "error" });
      expect(toasts[0].message).toContain(`HTTP ${status}`);
    }
  );
});
