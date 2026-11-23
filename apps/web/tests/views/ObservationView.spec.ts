import { describe, it, expect, beforeEach, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import ObservationView from "@/views/ObservationView.vue";
import { ApiError } from "@/api/client";
import { kernelApi } from "@/api/kernelApi";

const OBSERVATION = "observation:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d70";
const ENROLLMENT = "enrollment:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d71";

function mmhg(value: number) {
  return { type: "quantity", value, unit: "mm[Hg]", comparator: null };
}

function observation(overrides: Record<string, unknown> = {}) {
  return {
    ref: `${OBSERVATION}@1`,
    enrollment_ref: ENROLLMENT,
    concept: "concept:systolic@1",
    value: mmhg(182),
    observed_at: "2026-10-15T09:00:00Z",
    version: 1,
    locks: [],
    revisions: [
      {
        number: 1,
        value: mmhg(182),
        reason_for_change: "Initial entry",
        recorded_by: "crc@site.test",
        recorded_at: "2026-10-15T09:05:00Z",
        recorded_until: null,
      },
    ],
    assertions: [],
    audit: [],
    ...overrides,
  };
}

async function mountView() {
  const wrapper = mount(ObservationView, {
    props: { observationRef: OBSERVATION },
    attachTo: document.body,
  });
  await flushPromises();
  return wrapper;
}

async function correctTo(
  wrapper: Awaited<ReturnType<typeof mountView>>,
  value: string,
  reason = "Transcription error"
) {
  await wrapper.find("#observation-new-value").setValue(value);
  await wrapper.find("form.observation-correction").trigger("submit");
  await wrapper
    .find("#observation-change-reason-select")
    .setValue("Typographical Error");
  await wrapper.find("#observation-change-reason-text").setValue(reason);
  await wrapper.find("#observation-btn-save-change").trigger("click");
  await flushPromises();
}

describe("ObservationView (ADR-2620)", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.restoreAllMocks();
  });

  it("shows the saved state only after the kernel commits, then the reloaded audit trail", async () => {
    const corrected = observation({
      ref: `${OBSERVATION}@2`,
      value: mmhg(128),
      version: 2,
      audit: [
        {
          sequence: 7,
          recorded_at: "2026-10-15T09:10:00Z",
          actor_id: "crc@site.test",
          action: "Updated",
          subject: "Observation",
          version: 2,
          reason_for_change: "Typographical Error: Transcription error",
        },
      ],
    });
    vi.spyOn(kernelApi, "getObservation")
      .mockResolvedValueOnce(observation())
      .mockResolvedValueOnce(corrected);
    let commit!: (value: unknown) => void;
    const correct = vi
      .spyOn(kernelApi, "correctObservation")
      .mockImplementation(
        () => new Promise((resolve) => (commit = resolve)) as never
      );
    const wrapper = await mountView();

    await wrapper.find("#observation-new-value").setValue("128");
    await wrapper.find("form.observation-correction").trigger("submit");
    await wrapper
      .find("#observation-change-reason-select")
      .setValue("Typographical Error");
    await wrapper
      .find("#observation-change-reason-text")
      .setValue("Transcription error");
    await wrapper.find("#observation-btn-save-change").trigger("click");
    await flushPromises();

    // Waiting on the kernel: nothing is reported saved and the shown value is still the kernel's.
    expect(wrapper.find("[data-testid=observation-outcome]").exists()).toBe(
      false
    );
    expect(wrapper.find("[data-testid=observation-value]").text()).toBe(
      "182 mm[Hg]"
    );
    expect(wrapper.find("#observation-save").text()).toBe("Saving…");
    expect(correct).toHaveBeenCalledWith(
      expect.objectContaining({
        observationRef: `${OBSERVATION}@1`,
        enrollmentRef: ENROLLMENT,
        value: {
          type: "quantity",
          value: "128",
          unit: "mm[Hg]",
          comparator: null,
        },
        expectedVersion: 1,
        reason: "Typographical Error: Transcription error",
      })
    );

    commit({
      ref: `${OBSERVATION}@2`,
      value: mmhg(128),
      version: 2,
      revision: 2,
    });
    await flushPromises();

    expect(wrapper.find("[data-testid=observation-outcome]").text()).toContain(
      "Correction saved"
    );
    expect(wrapper.find("[data-testid=observation-value]").text()).toBe(
      "128 mm[Hg]"
    );
    expect(wrapper.find("[data-testid=observation-audit]").text()).toContain(
      "Typographical Error: Transcription error"
    );
    wrapper.unmount();
  });

  it("reloads and asks again when the version is stale", async () => {
    const newer = observation({
      ref: `${OBSERVATION}@2`,
      value: mmhg(130),
      version: 2,
    });
    const get = vi
      .spyOn(kernelApi, "getObservation")
      .mockResolvedValueOnce(observation())
      .mockResolvedValueOnce(newer);
    vi.spyOn(kernelApi, "correctObservation").mockRejectedValueOnce(
      new ApiError("stale", 409)
    );
    const wrapper = await mountView();

    await correctTo(wrapper, "128");

    expect(get).toHaveBeenCalledTimes(2);
    expect(wrapper.find("[data-testid=observation-outcome]").text()).toContain(
      "changed this value after you opened it"
    );
    expect(wrapper.find("[data-testid=observation-value]").text()).toBe(
      "130 mm[Hg]"
    );
    wrapper.unmount();
  });

  it("reports a locked observation and offers no correction", async () => {
    const lockedView = observation({
      locks: [
        {
          lock_id: "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d72",
          kind: "Locked",
          scope: "study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d73",
          attestation_id: "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d74",
          locked_at: "2026-10-15T10:00:00Z",
          locked_by: "dm@sponsor.test",
          reason_for_change: "Data review complete",
        },
      ],
    });
    vi.spyOn(kernelApi, "getObservation")
      .mockResolvedValueOnce(observation())
      .mockResolvedValueOnce(lockedView);
    vi.spyOn(kernelApi, "correctObservation").mockRejectedValueOnce(
      new ApiError("locked", 423)
    );
    const wrapper = await mountView();

    await correctTo(wrapper, "128");

    expect(wrapper.find("[data-testid=observation-outcome]").text()).toContain(
      "locked"
    );
    expect(wrapper.find("[data-testid=observation-status]").text()).toContain(
      "Locked at study level by dm@sponsor.test"
    );
    expect(wrapper.find("form.observation-correction").exists()).toBe(false);
    wrapper.unmount();
  });

  it("retries a failed request with the same idempotency key", async () => {
    vi.spyOn(kernelApi, "getObservation").mockResolvedValue(observation());
    const correct = vi
      .spyOn(kernelApi, "correctObservation")
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockRejectedValueOnce(new TypeError("Failed to fetch"));
    const wrapper = await mountView();

    await correctTo(wrapper, "128");
    expect(wrapper.find("[data-testid=observation-outcome]").text()).toContain(
      "Failed to fetch"
    );
    await correctTo(wrapper, "128");

    const keys = correct.mock.calls.map(([c]) => c.idempotencyKey);
    expect(keys[0]).toBe(keys[1]);
    wrapper.unmount();
  });

  it("does not offer to save an unchanged or empty value", async () => {
    vi.spyOn(kernelApi, "getObservation").mockResolvedValue(observation());
    const wrapper = await mountView();

    await wrapper.find("#observation-new-value").setValue("182");
    expect(
      wrapper.find("#observation-save").attributes("disabled")
    ).toBeDefined();
    await wrapper.find("#observation-new-value").setValue("182.0");
    expect(
      wrapper.find("#observation-save").attributes("disabled")
    ).toBeDefined();
    await wrapper.find("#observation-new-value").setValue("  ");
    expect(
      wrapper.find("#observation-save").attributes("disabled")
    ).toBeDefined();
    wrapper.unmount();
  });

  it("keeps the unit and refuses text that is not a quantity (ADR-2623)", async () => {
    vi.spyOn(kernelApi, "getObservation").mockResolvedValue(observation());
    const wrapper = await mountView();

    expect(wrapper.find("[data-testid=observation-unit]").text()).toBe(
      "mm[Hg]"
    );
    await wrapper.find("#observation-new-value").setValue("high");
    expect(
      wrapper.find("#observation-save").attributes("disabled")
    ).toBeDefined();
    expect(wrapper.find("#observation-new-value-hint").text()).toContain(
      "Enter a number in mm[Hg]"
    );
    await wrapper.find("#observation-new-value").setValue("<90");
    expect(
      wrapper.find("#observation-save").attributes("disabled")
    ).toBeUndefined();
    wrapper.unmount();
  });

  it("displays the clinical concept and one-click return navigation when embedded in Clinical Canvas", async () => {
    vi.spyOn(kernelApi, "getObservation").mockResolvedValue(observation());
    const zoomSpy = vi.spyOn(kernelApi, "getZoomPath");
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
        {
          path: "/observations/:observationRef",
          name: "observation",
          component: ObservationView,
          props: true,
        },
      ],
    });
    await router.push("/studies/CAD-101");
    await router.isReady();

    const wrapper = mount(ObservationView, {
      props: {
        observationRef: OBSERVATION,
        embedded: true,
        returnTo: "/studies/CAD-101?stage=conduct&participant=participant:p-1",
        returnLabel: "Back to 017-0001",
      },
      global: { plugins: [router] },
      attachTo: document.body,
    });
    await flushPromises();

    expect(wrapper.find("[data-testid=observation-concept]").text()).toBe(
      "concept:systolic@1"
    );
    const backLink = wrapper.find("#observation-back");
    expect(backLink.exists()).toBe(true);
    expect(backLink.text()).toBe("Back to 017-0001");
    expect(backLink.attributes("href")).toBe(
      "/studies/CAD-101?stage=conduct&participant=participant:p-1"
    );
    expect(zoomSpy).not.toHaveBeenCalled();
    wrapper.unmount();
  });

  it("resolves study lineage and redirects authorized standalone observation links into Clinical Canvas", async () => {
    vi.spyOn(kernelApi, "getObservation").mockResolvedValue(observation());
    vi.spyOn(kernelApi, "getZoomPath").mockResolvedValue([
      {
        level: "study",
        node: "study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d80",
        label: "CAD-101",
        mask: null,
      },
      {
        level: "study_site",
        node: "study_site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d81",
        label: "017",
        mask: null,
      },
      {
        level: "enrollment",
        node: ENROLLMENT,
        label: "017-0001",
        mask: null,
      },
    ]);

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div id='canvas-stub'>Canvas</div>" },
        },
        {
          path: "/observations/:observationRef",
          name: "observation",
          component: ObservationView,
          props: true,
        },
      ],
    });
    await router.push(`/observations/${OBSERVATION}`);
    await router.isReady();

    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [router] }, attachTo: document.body }
    );
    await flushPromises();

    expect(kernelApi.getZoomPath).toHaveBeenCalledWith(ENROLLMENT);
    expect(router.currentRoute.value.name).toBe("study");
    expect(router.currentRoute.value.params.studyCode).toBe("CAD-101");
    expect(router.currentRoute.value.query).toEqual({
      stage: "conduct",
      site: "017",
      observation: OBSERVATION,
    });
    wrapper.unmount();
  });

  it("keeps standalone observation view when scope lineage is masked for the caller", async () => {
    vi.spyOn(kernelApi, "getObservation").mockResolvedValue(observation());
    vi.spyOn(kernelApi, "getZoomPath").mockResolvedValue([
      {
        level: "study",
        node: null,
        label: null,
        mask: { kind: "study", reason: "unauthorized" },
      },
    ]);

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
        {
          path: "/observations/:observationRef",
          name: "observation",
          component: ObservationView,
          props: true,
        },
      ],
    });
    await router.push(`/observations/${OBSERVATION}`);
    await router.isReady();

    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [router] }, attachTo: document.body }
    );
    await flushPromises();

    expect(router.currentRoute.value.name).toBe("observation");
    expect(wrapper.find("[data-testid=observation-concept]").text()).toBe(
      "concept:systolic@1"
    );
    wrapper.unmount();
  });

  it("wraps the six-column revisions and audit trail tables in keyboard-focusable horizontal scroll regions", async () => {
    vi.spyOn(kernelApi, "getObservation").mockResolvedValue(
      observation({
        audit: [
          {
            sequence: 1,
            recorded_at: "2026-10-15T09:05:00Z",
            actor_id: "crc@site.test",
            action: "Recorded",
            subject: "Observation",
            version: 1,
            reason_for_change: "Initial entry",
          },
        ],
      })
    );
    const wrapper = await mountView();

    const revisionsScroll = wrapper.find(
      '[data-testid="observation-revisions-scroll"]'
    );
    expect(revisionsScroll.exists()).toBe(true);
    expect(revisionsScroll.attributes("role")).toBe("region");
    expect(revisionsScroll.attributes("tabindex")).toBe("0");
    expect(revisionsScroll.attributes("aria-label")).toBe(
      "Observation revisions"
    );

    const auditScroll = wrapper.find(
      '[data-testid="observation-audit-scroll"]'
    );
    expect(auditScroll.exists()).toBe(true);
    expect(auditScroll.attributes("role")).toBe("region");
    expect(auditScroll.attributes("tabindex")).toBe("0");
    expect(auditScroll.attributes("aria-label")).toBe(
      "Observation audit trail"
    );

    await expect(wrapper.element).toBeAccessible();
    wrapper.unmount();
  });

  it("forwards form, item, stage, and section query parameters on Clinical Canvas redirects and standalone back links", async () => {
    const FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f001";
    vi.spyOn(kernelApi, "getObservation").mockResolvedValue(observation());
    vi.spyOn(kernelApi, "getZoomPath").mockResolvedValue([
      {
        level: "study",
        node: "study:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d80",
        label: "CAD-101",
        mask: null,
      },
      {
        level: "study_site",
        node: "study_site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d81",
        label: "017",
        mask: null,
      },
      {
        level: "enrollment",
        node: ENROLLMENT,
        label: "017-0001",
        mask: null,
      },
    ]);

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div id='canvas-stub'>Canvas</div>" },
        },
        {
          path: "/ecrf",
          name: "ecrf",
          component: { template: "<div id='ecrf-stub'>eCRF</div>" },
        },
        {
          path: "/observations/:observationRef",
          name: "observation",
          component: ObservationView,
          props: true,
        },
      ],
    });
    await router.push({
      name: "observation",
      params: { observationRef: OBSERVATION },
      query: {
        stage: "data",
        section: "queries",
        form: `collection_instance:${FORM_ID}`,
        item: "SYSBP",
      },
    });
    await router.isReady();

    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [router] }, attachTo: document.body }
    );
    await flushPromises();

    expect(router.currentRoute.value.name).toBe("study");
    expect(router.currentRoute.value.params.studyCode).toBe("CAD-101");
    expect(router.currentRoute.value.query).toEqual({
      stage: "data",
      section: "queries",
      site: "017",
      form: `collection_instance:${FORM_ID}`,
      item: "SYSBP",
      observation: OBSERVATION,
    });
    wrapper.unmount();

    // When opened from standalone /ecrf with an explicit returnTo, stays on ObservationView and links back with formId & itemId.
    const returnUrl = `/ecrf?studyId=CAD-101&formId=${FORM_ID}&itemId=SYSBP`;
    await router.push({
      name: "observation",
      params: { observationRef: OBSERVATION },
      query: {
        studyId: "CAD-101",
        form: `collection_instance:${FORM_ID}`,
        item: "SYSBP",
        returnTo: returnUrl,
        returnLabel: "Back to Vital Signs · WEEK2",
      },
    });
    const standaloneWrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [router] }, attachTo: document.body }
    );
    await flushPromises();

    expect(router.currentRoute.value.name).toBe("observation");
    const backLink = standaloneWrapper.find("#observation-back");
    expect(backLink.exists()).toBe(true);
    expect(backLink.text()).toBe("Back to Vital Signs · WEEK2");
    expect(backLink.attributes("href")).toBe(returnUrl);
    standaloneWrapper.unmount();
  });
});
