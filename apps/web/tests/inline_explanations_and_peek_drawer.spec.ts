import { describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { ClinicalExplanation, ClinicalGatedAction, EntityLink } from "ui";
import ToastHost from "../src/components/ToastHost.vue";
import EntityPeekDrawerHost from "../src/components/canvas/EntityPeekDrawerHost.vue";
import { resolveEntityPeek } from "../src/canvas/entityPeekResolver";
import { useEntityPeekDrawer } from "../src/composables/useEntityPeekDrawer";
import { useToastStore } from "../src/stores/toasts.js";
import {
  describeApiError,
  extractProblemExplanation,
} from "../src/utils/apiErrors.js";
import {
  formatProblemExplanationSummary,
  extractExplanationTargets,
} from "../src/api/kernel/shared";

describe("Inline explanation primitives, peek drawer state, and 403/422/423 ProblemDetails", () => {
  it("opens peek drawer state on EntityLink click without navigating away from current route", async () => {
    const { state } = useEntityPeekDrawer();
    const link = mount(EntityLink, {
      props: {
        nodeRef:
          "qualification_requirement:11111111-2222-3333-4444-555555555555",
        label: "GCP Protocol v2 Training",
      },
    });

    await link.find("button").trigger("click");
    expect(state.open).toBe(true);
    expect(state.current?.nodeRef).toBe(
      "qualification_requirement:11111111-2222-3333-4444-555555555555"
    );
  });

  it("extracts and formats 403, 422, and 423 ProblemDetails explanations and clickable NextAction targets", () => {
    const locked423 = {
      status: 423,
      response: {
        status: 423,
        data: {
          title: "Locked",
          status: 423,
          code: "observation.locked",
          detail: "Site is locked.",
          explanation: {
            subject: "enrollment:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@1",
            claim:
              "Screening visit cannot be modified while a hard lock is in force.",
            producer: "kernel.locks",
            conditions: [
              {
                code: "lock.unlocked",
                met: false,
                description:
                  "Release the active site lock before recording observations.",
                evidence: ["assertion:99999999-8888-7777-6666-555555555555@1"],
              },
            ],
            next_actions: [
              {
                description: "Release active site lock",
                capability: "lock.release",
                target: "assertion:99999999-8888-7777-6666-555555555555@1",
              },
            ],
          },
        },
      },
    };

    expect(describeApiError(locked423)).toContain(
      "Screening visit cannot be modified while a hard lock is in force."
    );
    expect(extractProblemExplanation(locked423)?.nextActions[0]?.target).toBe(
      "assertion:99999999-8888-7777-6666-555555555555@1"
    );
    expect(formatProblemExplanationSummary(locked423.response.data)).toContain(
      "Screening visit cannot be modified"
    );
    expect(extractExplanationTargets(locked423.response.data)).toHaveLength(1);
  });

  it("renders gated actions visibly with plain-English RBAC explanations and zero raw permission tokens", () => {
    const wrapper = mount(ClinicalGatedAction, {
      props: {
        permitted: false,
        inline: true,
        label: "Sign Case Report Form",
        reason: "Requires investigator authority (collection.sign)",
        explanation: {
          claim:
            "Clinical Research Coordinators cannot sign case report form collections.",
          producer: "security.rbac",
          conditions: [],
          next_actions: [],
        },
      },
    });
    expect(wrapper.text()).toContain("Sign Case Report Form");
    expect(wrapper.text()).toContain(
      "Clinical Research Coordinators cannot sign case report form collections."
    );
    expect(wrapper.text()).not.toContain("(collection.sign)");
    expect(wrapper.findComponent(ClinicalExplanation).exists()).toBe(true);
  });

  it("renders NextAction EntityLinks inside ClinicalToastHost error alerts and opens the peek drawer on click", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const toasts = useToastStore();
    const { state } = useEntityPeekDrawer();

    toasts.apiError("Observation blocked", {
      status: 423,
      data: {
        title: "Locked",
        status: 423,
        detail: "Site is locked.",
        explanation: {
          claim:
            "Screening visit cannot be modified while a hard lock is in force.",
          producer: "kernel.locks",
          conditions: [],
          next_actions: [
            {
              description: "Release active site lock",
              capability: "lock.release",
              target: "assertion:99999999-8888-7777-6666-555555555555@1",
            },
          ],
        },
      },
    });

    const wrapper = mount(ToastHost, { global: { plugins: [pinia] } });
    await vi.dynamicImportSettled();
    await flushPromises();

    const errorToast = wrapper.find(".toast-error");
    expect(errorToast.exists()).toBe(true);
    expect(errorToast.attributes("role")).toBe("alert");
    expect(errorToast.text()).toContain("Release active site lock");

    const entityBtn = errorToast.findComponent(EntityLink).find("button");
    await entityBtn.trigger("click");

    expect(state.open).toBe(true);
    expect(state.current?.nodeRef).toBe(
      "assertion:99999999-8888-7777-6666-555555555555@1"
    );
    expect(toasts.toasts).toHaveLength(0);
    wrapper.unmount();
  });

  it("preserves Clinical Canvas studyCode, site, and participant scope via centralized scoped study location helpers when opening full view", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
      ],
    });
    await router.push({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        section: "participants",
        site: "SITE-101",
        participant: "SUBJ-101-001",
      },
    });
    await router.isReady();

    const { openPeek, state } = useEntityPeekDrawer();
    openPeek({
      nodeRef: "assertion:99999999-8888-7777-6666-555555555555@1",
      label: "Active Site Lock",
    });

    const directResolved = resolveEntityPeek(
      "assertion:99999999-8888-7777-6666-555555555555@1",
      router.currentRoute.value.query as Record<string, unknown>,
      { studyCode: "CADENCE-101" }
    );
    expect(directResolved.fullViewLocation).toEqual({
      name: "study",
      params: { studyCode: "CADENCE-101" },
      query: {
        stage: "conduct",
        section: "participants",
        site: "SITE-101",
        participant: "SUBJ-101-001",
        node: "assertion:99999999-8888-7777-6666-555555555555@1",
      },
    });

    const host = mount(EntityPeekDrawerHost, {
      global: { plugins: [pinia, router], stubs: { teleport: true } },
    });
    await flushPromises();

    expect(host.text()).toContain("CADENCE-101 | SITE-101 | SUBJ-101-001");
    const fullViewBtn = host
      .findAll("button")
      .find((b) => b.text().includes("Open full view"));
    expect(fullViewBtn).toBeDefined();
    await fullViewBtn!.trigger("click");
    await flushPromises();

    expect(state.open).toBe(false);
    expect(router.currentRoute.value.params.studyCode).toBe("CADENCE-101");
    expect(router.currentRoute.value.query).toMatchObject({
      stage: "conduct",
      section: "participants",
      site: "SITE-101",
      participant: "SUBJ-101-001",
      node: "assertion:99999999-8888-7777-6666-555555555555@1",
    });
    host.unmount();
  });
});
