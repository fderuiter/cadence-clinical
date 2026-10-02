import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth.js";
import { useToastStore } from "../src/stores/toasts.js";
import MdrView from "../src/views/MdrView.vue";
import ClinicalGanttVisualizer from "../src/components/clinical/ClinicalGanttVisualizer.vue";

vi.mock("../src/api/apiClient.js", () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue([]),
    post: vi.fn().mockResolvedValue({}),
    put: vi.fn().mockResolvedValue({}),
    delete: vi.fn().mockResolvedValue(null),
  },
}));

function httpError(status) {
  const err = new Error(`Request failed with status ${status}`);
  err.status = status;
  return err;
}

const STATUSES = [500, 502, 504];

let pinia;
let store;

beforeEach(() => {
  pinia = createPinia();
  setActivePinia(pinia);
  const authStore = useAuthStore();
  authStore.isAuthenticated = true;
  authStore.isDemoMode = false;
  store = useClinicalStore();
  window.localStorage.clear();
});

// #4523: an SoA change the MDR rejected must not appear in the local study model.
describe("MdrView confirmMutation API failure handling (#4523)", () => {
  it.each(STATUSES)(
    "HTTP %i from pushSoAMutation leaves the study arms unchanged and shows an error toast",
    async (status) => {
      const wrapper = mount(MdrView, { global: { plugins: [pinia] } });
      await flushPromises();
      vi.spyOn(store, "pushSoAMutation").mockRejectedValue(httpError(status));
      const armsBefore = JSON.stringify(store.currentUsdm.arms ?? []);

      wrapper.vm.pendingMutation = {
        type: "arms",
        id: "ARM-4523",
        properties: { name: "Rejected Arm" },
      };
      await wrapper.vm.confirmMutation("Add exploratory arm");
      await flushPromises();

      expect(JSON.stringify(store.currentUsdm.arms ?? [])).toBe(armsBefore);
      const toasts = useToastStore().toasts;
      expect(toasts).toHaveLength(1);
      expect(toasts[0]).toMatchObject({
        tone: "error",
        title: "Study design change failed",
      });
      expect(toasts[0].message).toContain(`HTTP ${status}`);
    }
  );

  it("HTTP 502 from pushSoALink leaves the SoA cell unchanged", async () => {
    const wrapper = mount(MdrView, { global: { plugins: [pinia] } });
    await flushPromises();
    vi.spyOn(store, "pushSoALink").mockRejectedValue(httpError(502));
    const row = store.currentUsdm.rows[0];
    const cell = row.cells[0];
    const before = JSON.stringify(store.currentUsdm.rows);

    wrapper.vm.pendingMutation = {
      type: "link",
      linkType: "procedure-visit",
      payload: {
        procedure_id: row.activity_id,
        visit_id: cell.encounter_id,
        is_applicable: !cell.is_applicable,
      },
    };
    await wrapper.vm.confirmMutation("Toggle procedure");
    await flushPromises();

    expect(JSON.stringify(store.currentUsdm.rows)).toBe(before);
    expect(useToastStore().toasts[0]).toMatchObject({ tone: "error" });
  });

  it("applies the mutation locally once the API accepted it", async () => {
    const wrapper = mount(MdrView, { global: { plugins: [pinia] } });
    await flushPromises();
    vi.spyOn(store, "pushSoAMutation").mockResolvedValue(undefined);

    wrapper.vm.pendingMutation = {
      type: "arms",
      id: "ARM-4523",
      properties: { name: "Accepted Arm" },
    };
    await wrapper.vm.confirmMutation("Add exploratory arm");

    expect(store.currentUsdm.arms.map((a) => a.arm_id)).toContain("ARM-4523");
    expect(useToastStore().toasts).toHaveLength(0);
  });
});

describe("ClinicalGanttVisualizer saveTiming API failure handling (#4523)", () => {
  function seedStudy() {
    store.currentUsdm = {
      studyId: "TEST-001",
      arms: [{ arm_id: "ARM-1", arm_name: "Active" }],
      epochs: [{ epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 }],
      encounters: [
        {
          encounter_id: "V-SCR",
          encounter_name: "Visit 1",
          epoch_id: "EP-SCR",
          sequence: 1,
          planned_day: 5,
        },
      ],
    };
  }

  async function editAndSave(wrapper) {
    await wrapper.find("circle").trigger("click");
    await flushPromises();
    await wrapper.findAll("input")[0].setValue(12);
    await wrapper.findAll("input")[1].setValue(2);
    await wrapper.findAll("input")[2].setValue(4);
    await wrapper.find("textarea").setValue("Scientific timing justification.");
    await wrapper.find("button.btn-primary").trigger("click");
    await flushPromises();
  }

  it.each(STATUSES)(
    "HTTP %i keeps the visit unchanged, keeps the modal open and shows an error toast",
    async (status) => {
      seedStudy();
      vi.spyOn(store, "pushSoAMutation").mockRejectedValue(httpError(status));
      const wrapper = mount(ClinicalGanttVisualizer, {
        global: { plugins: [pinia] },
      });

      await editAndSave(wrapper);

      expect(store.currentUsdm.encounters[0].planned_day).toBe(5);
      expect(store.currentUsdm.encounters[0].min_offset).toBeUndefined();
      expect(wrapper.find(".gantt-modal-backdrop").exists()).toBe(true);
      expect(wrapper.find("textarea").element.value).toBe(
        "Scientific timing justification."
      );
      const toasts = useToastStore().toasts;
      expect(toasts).toHaveLength(1);
      expect(toasts[0]).toMatchObject({
        tone: "error",
        title: "Visit window update failed",
      });
      expect(toasts[0].message).toContain(`HTTP ${status}`);
    }
  );

  it("applies the change and closes the modal on success", async () => {
    seedStudy();
    vi.spyOn(store, "pushSoAMutation").mockResolvedValue(undefined);
    const wrapper = mount(ClinicalGanttVisualizer, {
      global: { plugins: [pinia] },
    });

    await editAndSave(wrapper);

    expect(store.currentUsdm.encounters[0].planned_day).toBe(12);
    expect(wrapper.find(".gantt-modal-backdrop").exists()).toBe(false);
    expect(useToastStore().toasts).toHaveLength(0);
  });
});
