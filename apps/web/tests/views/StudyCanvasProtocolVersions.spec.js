import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    listProposals: vi.fn(async () => []),
    search: vi.fn(),
    listProtocolVersions: vi.fn(),
    getProtocolVersion: vi.fn(),
    getCommands: vi.fn(),
    trace: vi.fn(),
    dependents: vi.fn(),
    amendmentImpact: vi.fn(),
  },
  isMasked: (object) => object.mask !== null,
}));

const STUDY = "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1@2";
const V1_ID = "0192f3c4-5e6a-7b8c-9d0e-0000000000b1";
const V2_ID = "0192f3c4-5e6a-7b8c-9d0e-0000000000b2";
const V1 = `protocol_version:${V1_ID}@3`;
const V2 = `protocol_version:${V2_ID}@1`;

const version = (overrides) => ({
  ref: null,
  study: STUDY,
  number: null,
  parent_version: null,
  status: null,
  content_hash: null,
  amendment_intent: null,
  approval_attestation: null,
  withdrawal_reason: null,
  mask: null,
  ...overrides,
});

const FIRST = version({
  ref: V1,
  number: 1,
  status: "Approved",
  content_hash: "sha256:aa",
});
const AMENDMENT = version({
  ref: V2,
  number: 2,
  status: "Draft",
  // The parent is named without its revision; the list still finds it.
  parent_version: `protocol_version:${V1_ID}`,
  amendment_intent: "Add a PK visit",
});
const HIDDEN = version({ mask: "NotPermitted" });

describe("Clinical Canvas protocol versions and amendment impact (ADR-2540, K-PROT-2)", () => {
  let pinia;
  let router;
  let wrapper;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
          meta: { layout: "canvas" },
        },
        { path: "/ecrf", name: "ecrf", component: { template: "<div />" } },
      ],
    });
    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    });
    for (const method of Object.values(kernelApi))
      vi.mocked(method).mockReset();
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: STUDY,
        kind: "study",
        title: "Cadence 101",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([
      AMENDMENT,
      HIDDEN,
      FIRST,
    ]);
    vi.mocked(kernelApi.getProtocolVersion).mockImplementation(async (ref) =>
      ref === V2 ? AMENDMENT : FIRST
    );
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  async function open(path) {
    await router.push(path);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
  }

  it("lists the study's versions from the kernel, a hidden one in place", async () => {
    await open("/studies/CAD-101?stage=design");
    await wrapper.find("#nav-protocol-versions").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(kernelApi.search).toHaveBeenCalledWith({
      text: "CAD-101",
      kinds: ["study"],
      limit: 5,
    });
    expect(kernelApi.listProtocolVersions).toHaveBeenCalledWith(STUDY);
    expect(wrapper.find("#protocol-versions-status").text()).toBe(
      "3 versions, 1 of them hidden from you."
    );
    const rows = wrapper.findAll("#protocol-versions-section tbody tr");
    expect(
      rows.map((r) => r.findAll("th, td").map((cell) => cell.text().trim()))
    ).toEqual([
      ["2", "Draft", "Version 1", "Add a PK visit"],
      ["A version you may not see (not permitted)"],
      ["1", "Approved", "Nothing (first version)", "None recorded"],
    ]);
  });

  it("says in plain terms when the study wasn't found or isn't visible", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([]);

    await open("/studies/CAD-101?section=protocol-versions");

    expect(wrapper.find("#protocol-versions-status").text()).toBe(
      "Study CAD-101 wasn't found or isn't visible to your account."
    );
    expect(kernelApi.listProtocolVersions).not.toHaveBeenCalled();
  });

  it("presents a plain status message and collapsible support details when versions cannot be read", async () => {
    vi.mocked(kernelApi.listProtocolVersions).mockRejectedValue(
      new ApiError("Not visible", 403)
    );

    await open("/studies/CAD-101?section=protocol-versions");

    expect(wrapper.find("#protocol-versions-status").text()).toContain(
      "Protocol versions couldn't be loaded right now."
    );
    expect(wrapper.find("#protocol-versions-section details").text()).toContain(
      "Not visible"
    );
  });

  it("shows an amendment in the inspector, and Impact lists what it changes and what depends on the version it amends", async () => {
    vi.mocked(kernelApi.amendmentImpact).mockResolvedValue({
      amended: V2,
      parent: `protocol_version:${V1_ID}`,
      impacted_elements: ["schedule_of_activities"],
      changed_elements: [
        {
          element_id: "act-pk",
          instance_type: "Activity",
          change: "Added",
          properties: [],
        },
        {
          element_id: "enc-v3",
          instance_type: "Encounter",
          change: "Changed",
          properties: ["/window/upper"],
        },
      ],
      dependents: {
        start: V1,
        depth: 10,
        groups: [
          {
            kind: "participant",
            count: 2,
            objects: [
              {
                handle: "masked:1",
                kind: "participant",
                ref: null,
                mask: "NotPermitted",
                distance: 1,
              },
            ],
          },
        ],
        total: 2,
        truncated: false,
      },
    });
    await open("/studies/CAD-101?section=protocol-versions");

    await wrapper.find("#protocol-version-2").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(router.currentRoute.value.query.node).toBe(V2);
    expect(kernelApi.getProtocolVersion).toHaveBeenCalledWith(V2);
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("Version 2");
    expect(wrapper.find("#inspector-version-status").text()).toBe("Draft");

    await wrapper.find("#verb-impact").trigger("click");
    await flushPromises();

    expect(kernelApi.amendmentImpact).toHaveBeenCalledWith(V2);
    expect(kernelApi.dependents).not.toHaveBeenCalled();
    expect(wrapper.find("#kernel-impact-title").text()).toBe(
      "What this amendment changes"
    );
    const changes = wrapper.findAll("#kernel-amendment-changes li");
    expect(changes.map((c) => c.text().replace(/\s+/g, " "))).toEqual([
      "Added activity act-pk",
      "Changed encounter enc-v3 (Window · Upper)",
    ]);
    expect(wrapper.find("#kernel-impact-total").text()).toBe(
      "2 dependent objects"
    );
    expect(wrapper.find("#kernel-impact").text()).toContain(
      "Hidden participant (not permitted)"
    );
  });

  it("uses the plain dependents read for a study's first version, which amends nothing", async () => {
    vi.mocked(kernelApi.dependents).mockResolvedValue({
      start: V1,
      depth: 10,
      groups: [],
      total: 0,
      truncated: false,
    });
    await open(`/studies/CAD-101?section=protocol-versions&node=${V1}`);

    await wrapper.find("#verb-impact").trigger("click");
    await flushPromises();

    expect(kernelApi.dependents).toHaveBeenCalledWith(V1);
    expect(kernelApi.amendmentImpact).not.toHaveBeenCalled();
    expect(wrapper.find("#kernel-impact-title").text()).toBe(
      "What depends on this"
    );
  });

  it("traces a protocol version through the kernel's lineage", async () => {
    vi.mocked(kernelApi.trace).mockResolvedValue({
      nodes: [],
      edges: [],
      truncated: false,
    });
    await open(`/studies/CAD-101?section=protocol-versions&node=${V2}`);

    await wrapper.find("#verb-trace").trigger("click");
    await flushPromises();

    expect(kernelApi.trace).toHaveBeenCalledWith(V2);
  });

  it("presents a plain status message and support details when a version named in the URL couldn't be read", async () => {
    vi.mocked(kernelApi.getProtocolVersion).mockRejectedValue(
      new ApiError(`${V2} does not exist in your tenant.`, 404)
    );

    await open(`/studies/CAD-101?section=protocol-versions&node=${V2}`);

    expect(wrapper.find("#inspector-version-error").text()).toContain(
      "This protocol version couldn't be loaded."
    );
    expect(wrapper.find("#canvas-inspector details").text()).toContain(
      "does not exist in your tenant"
    );
  });
});
