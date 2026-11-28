import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { defineComponent, h } from "vue";
import { createMemoryHistory, createRouter } from "vue-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import EntityPeekDrawerHost from "../../src/components/canvas/EntityPeekDrawerHost.vue";
import ScopeLockControls from "../../src/components/canvas/ScopeLockControls.vue";
import { useEntityPeekDrawer } from "../../src/composables/useEntityPeekDrawer";
import { useAuthStore } from "../../src/stores/auth";

const { mockCanI, mockGetStudyLockReadiness } = vi.hoisted(() => ({
  mockCanI: vi.fn(),
  mockGetStudyLockReadiness: vi.fn(),
}));

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      canI: mockCanI,
      getStudyLockReadiness: mockGetStudyLockReadiness,
    },
  };
});

describe("ClinicalGovernanceBanner LIFO focus containment & inert cleanup", () => {
  const STUDY_REF = "study:01960000-0000-7000-8000-000000000101";
  const FORM_REF = "collection_instance:01960000-0000-7000-8000-000000000103";
  const OBS_REF = "observation:01960000-0000-7000-8000-000000000104";

  beforeEach(() => {
    vi.clearAllMocks();
    mockCanI.mockResolvedValue({ permitted: true });
    mockGetStudyLockReadiness.mockResolvedValue({
      study_ref: STUDY_REF,
      scope_ref: FORM_REF,
      scope_kind: "collection_instance",
      ready_for_lock: false,
      can_freeze: true,
      open_query_count: 1,
      missing_required_item_count: 0,
      blockers: [
        {
          kind: "OpenQuery",
          owner: "Site coordinator",
          target_ref: OBS_REF,
          target_kind: "observation",
          subject_code: "SUBJ-01",
          collection_spec_id: "VS",
          collection_title: "Vital Signs",
          item_id: "SYSBP",
          item_label: "Systolic BP",
          query_ref: null,
          query_text: "Verify reading",
          answered: false,
          next_action: `Answer open query on ${OBS_REF}.`,
          masked: false,
          mask: null,
        },
      ],
      valid_at: "2026-06-01T10:00:00Z",
      known_at: "2026-06-01T10:00:00Z",
    });
  });

  afterEach(() => {
    const peek = useEntityPeekDrawer();
    peek.closePeek();
    if (peek.state.docked) {
      peek.toggleDocked();
    }
    for (const el of Array.from(document.querySelectorAll("[inert]"))) {
      el.removeAttribute("inert");
    }
  });

  it("preserves LIFO focus containment and cleans up all inert attributes when clicking NodeRefChip or opening SignatureCaptureModal from inside the governance ClinicalDrawer", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const auth = useAuthStore();
    auth.user = {
      id: "u-dm",
      username: "dm.lead",
      firstName: "Dana",
      lastName: "Mercer",
      roles: ["data_manager"],
    } as never;

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: defineComponent({ render: () => h("div") }),
        },
        {
          path: "/observations/:observationRef",
          name: "observation",
          component: defineComponent({ render: () => h("div") }),
        },
      ],
    });
    await router.push("/studies/CAD-101?stage=conduct");
    await router.isReady();

    // Even if user previously docked EntityPeekDrawer on desktop, opening from inside a modal trap must force modal LIFO
    const peek = useEntityPeekDrawer();
    if (!peek.state.docked) {
      peek.toggleDocked();
    }

    const Host = defineComponent({
      setup() {
        return () =>
          h("div", { id: "lifo-test-root" }, [
            h(
              "button",
              { id: "outside-workspace-btn", type: "button" },
              "Outside"
            ),
            h(ScopeLockControls, {
              studyRef: STUDY_REF,
              scopeRef: FORM_REF,
              scopeLabel: "form",
              idPrefix: "ecrf-lock",
            }),
            h(EntityPeekDrawerHost),
          ]);
      },
    });

    const wrapper = mount(Host, {
      global: { plugins: [pinia, router] },
      attachTo: document.body,
    });
    await flushPromises();

    // 1. Open blocker drawer and click NodeRefChip -> opens EntityPeekDrawerHost in LIFO modal mode
    const reviewBtn = wrapper.find("#ecrf-lock-review-blockers");
    expect(reviewBtn.exists()).toBe(true);
    await reviewBtn.trigger("click");
    await flushPromises();

    const chip = wrapper.find(
      "[data-testid='ecrf-lock-readiness-blockers'] .node-ref-chip"
    );
    expect(chip.exists()).toBe(true);
    (chip.element as HTMLElement).focus();
    await chip.trigger("click");
    await flushPromises();

    expect(peek.state.open).toBe(true);
    expect(peek.state.openedOverModal).toBe(true);

    const peekOverlay = document.querySelector(".entity-peek-drawer-overlay");
    expect(peekOverlay).not.toBeNull();
    const peekDialog = peekOverlay?.querySelector('[role="dialog"]');
    expect(peekDialog).not.toBeNull();
    expect(peekDialog?.getAttribute("aria-modal")).toBe("true");
    expect(peekOverlay?.hasAttribute("inert")).toBe(false);
    expect(peekDialog?.contains(document.activeElement)).toBe(true);

    // First Escape closes EntityPeekDrawerHost and restores focus to NodeRefChip inside governance drawer
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await flushPromises();
    expect(peek.state.open).toBe(false);
    expect(
      wrapper.find("[data-testid='ecrf-lock-readiness-blockers']").exists()
    ).toBe(true);
    expect(document.activeElement).toBe(chip.element);

    // Second Escape closes governance ClinicalDrawer, restores focus to trigger, and leaves zero inert elements
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await flushPromises();
    expect(
      wrapper.find("[data-testid='ecrf-lock-readiness-blockers']").exists()
    ).toBe(false);
    expect(document.activeElement).toBe(reviewBtn.element);
    expect(document.querySelectorAll("[inert]").length).toBe(0);

    // 2. Open lock drawer and launch real SignatureCaptureModal -> LIFO modal over drawer
    const manageBtn = wrapper.find("#ecrf-lock-manage");
    await manageBtn.trigger("click");
    await flushPromises();

    await wrapper.find("#ecrf-lock-reason").setValue("Freeze form for review");
    const freezeBtn = wrapper.find("#ecrf-lock-freeze");
    (freezeBtn.element as HTMLElement).focus();
    await freezeBtn.trigger("click");
    await flushPromises();

    const sigBackdrop = document.querySelector(".signature-capture-backdrop");
    expect(sigBackdrop).not.toBeNull();
    expect(sigBackdrop?.hasAttribute("inert")).toBe(false);
    const sigDialog = sigBackdrop?.querySelector('[role="dialog"]');
    expect(sigDialog).not.toBeNull();
    expect(sigDialog?.contains(document.activeElement)).toBe(true);

    // First Escape closes SignatureCaptureModal and returns focus inside governance drawer
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await flushPromises();
    expect(document.querySelector(".signature-capture-backdrop")).toBeNull();
    expect(wrapper.find("#ecrf-lock-reason").exists()).toBe(true);
    expect(document.activeElement).toBe(freezeBtn.element);

    // Second Escape closes governance drawer and leaves zero orphaned inert attributes
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await flushPromises();
    expect(wrapper.find("#ecrf-lock-reason").exists()).toBe(false);
    expect(document.activeElement).toBe(manageBtn.element);
    expect(document.querySelectorAll("[inert]").length).toBe(0);

    wrapper.unmount();
  });
});
