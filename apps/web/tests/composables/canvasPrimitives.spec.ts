import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { computed, defineComponent, ref } from "vue";
import type { LocationQuery } from "vue-router";
import { kernelApi } from "../../src/api/kernelApi";
import { findKernelStudySiteRef } from "../../src/canvas/kernelStudy";
import { useCanvasSelection } from "../../src/composables/useCanvasSelection";
import { useRovingToolbar } from "../../src/composables/useRovingToolbar";
import {
  STUDY_SITE_ACTIVATE_CAPABILITY,
  useKernelCanI,
} from "../../src/composables/useKernelCanI";
import CanvasErrorAlert from "../../src/components/canvas/CanvasErrorAlert.vue";

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    search: vi.fn(),
    getChildren: vi.fn(),
    canI: vi.fn(),
  },
}));

const STUDY_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000010";
const SITE_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000020";
const PARTICIPANT_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000001";
const FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000222";
const OBS_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000333";
const PROPOSAL_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000444";
const VERSION_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000555";

describe("Clinical Canvas shared composables and error alert primitive", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("useCanvasSelection", () => {
    it("resolves active NodeRef priority across ?node=, ?observation=, ?form=, ?proposal=, and ?participant=", () => {
      const query = ref<LocationQuery>({});
      const participant = {
        id: PARTICIPANT_ID,
        subject_id: "017-0001",
        study_id: "CAD-101",
        site_id: "017",
        status: "ACTIVE",
        active_protocol_version: "1.0",
        enrollment_index: 1,
      };

      const selection = useCanvasSelection({
        query: () => query.value,
        knownParticipants: () => ({ [PARTICIPANT_ID]: participant }),
        canResolveRemotely: () => false,
      });

      expect(selection.inspectedNodeText.value).toBe("");

      // Priority 5: ?participant=
      query.value = { participant: `participant:${PARTICIPANT_ID}` };
      expect(selection.inspectedNodeText.value).toBe(
        `participant:${PARTICIPANT_ID}`
      );
      expect(selection.inspectedParticipant.value?.subject_id).toBe("017-0001");
      expect(selection.selectedGenericNodeRef.value).toBe("");

      // Guardrail: ?form=new stays bound to participant:<id> until onFormLoaded assigns collection_instance:<id>
      query.value = {
        participant: `participant:${PARTICIPANT_ID}`,
        form: "new",
      };
      expect(selection.isNewForm.value).toBe(true);
      expect(selection.inspectedNodeText.value).toBe(
        `participant:${PARTICIPANT_ID}`
      );
      expect(selection.selectedGenericNodeRef.value).toBe("");

      // Priority 4: ?proposal= overrides ?participant=
      query.value = {
        participant: `participant:${PARTICIPANT_ID}`,
        proposal: `proposal:${PROPOSAL_ID}`,
      };
      expect(selection.inspectedNodeText.value).toBe(`proposal:${PROPOSAL_ID}`);
      expect(selection.selectedGenericNodeRef.value).toBe(
        `proposal:${PROPOSAL_ID}`
      );
      expect(selection.inspectedParticipant.value).toBeNull();

      // Priority 3: ?form=collection_instance:<id> overrides ?proposal= and ?participant=
      query.value = {
        participant: `participant:${PARTICIPANT_ID}`,
        proposal: `proposal:${PROPOSAL_ID}`,
        form: `collection_instance:${FORM_ID}`,
      };
      expect(selection.inspectedNodeText.value).toBe(
        `collection_instance:${FORM_ID}`
      );
      expect(selection.selectedGenericNodeRef.value).toBe(
        `collection_instance:${FORM_ID}`
      );

      // Priority 2: ?observation=observation:<id> overrides ?form=
      query.value = {
        participant: `participant:${PARTICIPANT_ID}`,
        form: `collection_instance:${FORM_ID}`,
        observation: `observation:${OBS_ID}`,
      };
      expect(selection.inspectedNodeText.value).toBe(`observation:${OBS_ID}`);
      expect(selection.selectedGenericNodeRef.value).toBe(
        `observation:${OBS_ID}`
      );

      // Priority 1: ?node= overrides all other parameters
      query.value = {
        participant: `participant:${PARTICIPANT_ID}`,
        form: `collection_instance:${FORM_ID}`,
        observation: `observation:${OBS_ID}`,
        proposal: `proposal:${PROPOSAL_ID}`,
        node: `protocol_version:${VERSION_ID}@2`,
      };
      expect(selection.inspectedNodeText.value).toBe(
        `protocol_version:${VERSION_ID}@2`
      );
      expect(selection.selectedVersionText.value).toBe(
        `protocol_version:${VERSION_ID}@2`
      );
      expect(selection.selectedGenericNodeRef.value).toBe("");
    });
  });

  describe("useRovingToolbar", () => {
    it("maintains a single tabindex=0 stop and cycles focus with ArrowRight, ArrowLeft, Home, and End without auto-clicking", async () => {
      const showOptional = ref(true);
      const clicked = vi.fn();

      const ToolbarHarness = defineComponent({
        setup() {
          const toolbarRef = ref<HTMLElement | null>(null);
          const itemIds = computed(() => [
            "btn-a",
            "btn-b-disabled",
            ...(showOptional.value ? ["btn-c"] : []),
          ]);
          const { getTabIndex } = useRovingToolbar(toolbarRef, { itemIds });
          return { toolbarRef, showOptional, getTabIndex, clicked };
        },
        template: `
          <div ref="toolbarRef" role="toolbar" aria-label="Test toolbar">
            <button id="btn-a" type="button" :tabindex="getTabIndex('btn-a')" @click="clicked('a')">A</button>
            <button id="btn-b-disabled" type="button" aria-disabled="true" :tabindex="getTabIndex('btn-b-disabled')" @click="clicked('b')">B</button>
            <button v-if="showOptional" id="btn-c" type="button" :tabindex="getTabIndex('btn-c')" @click="clicked('c')">C</button>
          </div>
        `,
      });

      const wrapper = mount(ToolbarHarness, { attachTo: document.body });

      const tabIndices = () =>
        wrapper
          .findAll("button")
          .map((btn) => [btn.attributes("id"), btn.attributes("tabindex")]);

      expect(tabIndices()).toEqual([
        ["btn-a", "0"],
        ["btn-b-disabled", "-1"],
        ["btn-c", "-1"],
      ]);

      const btnA = wrapper.find("#btn-a").element as HTMLButtonElement;
      btnA.focus();
      expect(document.activeElement?.id).toBe("btn-a");

      // ArrowRight moves focus to aria-disabled button without clicking it
      btnA.dispatchEvent(
        new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })
      );
      await flushPromises();
      expect(document.activeElement?.id).toBe("btn-b-disabled");
      expect(tabIndices()).toEqual([
        ["btn-a", "-1"],
        ["btn-b-disabled", "0"],
        ["btn-c", "-1"],
      ]);
      expect(clicked).not.toHaveBeenCalled();

      // End jumps to last button
      (document.activeElement as HTMLElement).dispatchEvent(
        new KeyboardEvent("keydown", { key: "End", bubbles: true })
      );
      await flushPromises();
      expect(document.activeElement?.id).toBe("btn-c");

      // ArrowRight wraps from last to first
      (document.activeElement as HTMLElement).dispatchEvent(
        new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })
      );
      await flushPromises();
      expect(document.activeElement?.id).toBe("btn-a");

      // ArrowLeft wraps from first to last
      (document.activeElement as HTMLElement).dispatchEvent(
        new KeyboardEvent("keydown", { key: "ArrowLeft", bubbles: true })
      );
      await flushPromises();
      expect(document.activeElement?.id).toBe("btn-c");

      // Home jumps to first button, then End back to btn-c and remove btn-c -> clamps back to btn-a
      (document.activeElement as HTMLElement).dispatchEvent(
        new KeyboardEvent("keydown", { key: "Home", bubbles: true })
      );
      await flushPromises();
      expect(document.activeElement?.id).toBe("btn-a");

      (document.activeElement as HTMLElement).dispatchEvent(
        new KeyboardEvent("keydown", { key: "End", bubbles: true })
      );
      await flushPromises();
      expect(document.activeElement?.id).toBe("btn-c");

      showOptional.value = false;
      await flushPromises();
      expect(tabIndices()).toEqual([
        ["btn-a", "0"],
        ["btn-b-disabled", "-1"],
      ]);

      wrapper.unmount();
    });
  });

  describe("findKernelStudySiteRef and useKernelCanI", () => {
    it("resolves study_site NodeRef and probes study_site.activate verbatim via kernelApi.canI", async () => {
      vi.mocked(kernelApi.search).mockResolvedValue([
        {
          ref: `study:${STUDY_ID}@1`,
          kind: "study",
          code: "CAD-101",
          title: "Cadence 101",
          exact_code: true,
        },
      ]);
      vi.mocked(kernelApi.getChildren).mockResolvedValue([
        {
          node: {
            ref: `study_site:${SITE_ID}@1`,
            kind: "study_site",
            code: "SITE-7",
            title: "Boston Medical",
            status: "PENDING",
            mask: null,
          },
          child_count: 0,
        },
      ]);
      const kernelDecision = {
        capability: STUDY_SITE_ACTIVATE_CAPABILITY,
        verb_class: "Execute",
        resource: `study_site:${SITE_ID}`,
        permitted: false,
        reason: "No grant of study_site.activate covers SITE-7.",
        explanation: {
          subject: `study_site:${SITE_ID}`,
          claim: "You may not activate study_site SITE-7.",
          producer: "kernel.authorization",
          steps: [{ claim: "Evaluated active grants.", evidence: [] }],
          conditions: [
            {
              code: "grant.active",
              description: "Hold study_site.activate on SITE-7",
              met: false,
              evidence: [],
            },
          ],
          next_actions: [],
        },
      };
      vi.mocked(kernelApi.canI).mockResolvedValue(kernelDecision);

      const siteRef = await findKernelStudySiteRef("CAD-101", "site-7");
      expect(siteRef).toBe(`study_site:${SITE_ID}`);

      const canI = useKernelCanI();
      const result = await canI.probeStudySiteActivate("CAD-101", "SITE-7");
      expect(kernelApi.canI).toHaveBeenCalledWith(
        "study_site.activate",
        `study_site:${SITE_ID}`
      );
      expect(result).toEqual(kernelDecision);
      expect(canI.decision.value).toEqual(kernelDecision);
    });
  });

  describe("CanvasErrorAlert", () => {
    it("renders message, Retry, Explain, and SupportDetails and passes axe audit", async () => {
      const onRetry = vi.fn();
      const onExplain = vi.fn();

      const wrapper = mount(CanvasErrorAlert, {
        props: {
          message: "Queries couldn't be loaded right now.",
          detail: "HTTP 503 Service Unavailable",
          code: "UPSTREAM_UNAVAILABLE",
          reference: "req-99",
          detailId: "custom-error-detail",
          onRetry,
          onExplain,
        },
      });

      expect(wrapper.attributes("role")).toBe("alert");
      expect(wrapper.text()).toContain("Queries couldn't be loaded right now.");
      expect(wrapper.find("#custom-error-detail").text()).toBe(
        "HTTP 503 Service Unavailable"
      );
      expect(wrapper.find("details").text()).toContain("UPSTREAM_UNAVAILABLE");
      expect(wrapper.find("details").text()).toContain("req-99");

      await wrapper.find('[data-testid="canvas-error-retry"]').trigger("click");
      await wrapper
        .find('[data-testid="canvas-error-explain"]')
        .trigger("click");
      expect(onRetry).toHaveBeenCalledTimes(1);
      expect(onExplain).toHaveBeenCalledTimes(1);

      await expect(wrapper.element).toBeAccessible();
    });
  });
});
