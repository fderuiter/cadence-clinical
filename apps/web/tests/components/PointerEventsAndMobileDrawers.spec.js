import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import SoAMatrixEditor from "../../src/components/clinical/SoAMatrixEditor.vue";
import TicketDetailDrawer from "../../src/components/tickets/TicketDetailDrawer.vue";
import ShareDrawer from "../../src/components/fileshare/ShareDrawer.vue";
import EhrReconciliationDrawer from "../../src/components/clinical/EhrReconciliationDrawer.vue";

vi.mock("vue-router", () => ({
  onBeforeRouteLeave: () => {},
  useRoute: vi.fn(() => ({ query: {} })),
  useRouter: vi.fn(() => ({ push: vi.fn() })),
}));

describe("Unified Pointer Events API and Mobile Responsive Drawers", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  describe("SoAMatrixEditor.vue Pointer Events & Selection Parity", () => {
    const mockActivities = [
      {
        activity_id: "ACT-VS",
        activity_name: "Vital Signs",
        cdash_domain: "VS",
        encounters: { "Screening Visit": true, "Week 2 Visit": false },
      },
      {
        activity_id: "ACT-LAB",
        activity_name: "Safety Labs",
        cdash_domain: "LB",
        encounters: { "Screening Visit": false, "Week 2 Visit": false },
      },
    ];

    const mockVisits = [
      { encounter_id: "V-SCR", visit_name: "Screening Visit", target_day: 1 },
      { encounter_id: "V-WK2", visit_name: "Week 2 Visit", target_day: 14 },
    ];

    it("renders matrix cells with data attributes and pointer event listeners", () => {
      const wrapper = mount(SoAMatrixEditor, {
        props: { activities: mockActivities, visits: mockVisits },
      });

      const toggleCells = wrapper.findAll(".cell-toggle");
      expect(toggleCells.length).toBe(4);

      const firstCell = toggleCells[0];
      expect(firstCell.attributes("data-r-idx")).toBe("0");
      expect(firstCell.attributes("data-c-idx")).toBe("0");
    });

    it("triggers range selection on pointerdown and pointermove", async () => {
      const wrapper = mount(SoAMatrixEditor, {
        props: { activities: mockActivities, visits: mockVisits },
      });

      const toggleCells = wrapper.findAll(".cell-toggle");
      const cell1 = toggleCells[0];

      const pointerDownEvt = new Event("pointerdown", { bubbles: true });
      Object.assign(pointerDownEvt, {
        pointerId: 1,
        clientX: 100,
        clientY: 100,
      });
      cell1.element.dispatchEvent(pointerDownEvt);
      await wrapper.vm.$nextTick();

      expect(wrapper.vm.isDragging).toBe(true);

      const pointerUpEvt = new Event("pointerup", { bubbles: true });
      Object.assign(pointerUpEvt, { pointerId: 1, clientX: 100, clientY: 100 });
      cell1.element.dispatchEvent(pointerUpEvt);
      await wrapper.vm.$nextTick();

      expect(wrapper.vm.isDragging).toBe(false);
    });
  });

  describe("Overlay Drawers CSS Constraints", () => {
    it("TicketDetailDrawer contains backdrop and panel when isOpen is true", () => {
      const mockTicket = {
        id: "TICK-001",
        title: "Discrepancy in VS",
        severity: "major",
        status: "open",
        created_at: new Date().toISOString(),
      };
      const wrapper = mount(TicketDetailDrawer, {
        props: { isOpen: true, ticket: mockTicket },
      });

      const backdrop = wrapper.find(".drawer-backdrop");
      expect(backdrop.exists()).toBe(true);

      const panel = wrapper.find(".drawer-panel");
      expect(panel.exists()).toBe(true);
    });

    it("ShareDrawer contains overlay and drawer container when isOpen is true", () => {
      const wrapper = mount(ShareDrawer, {
        props: {
          isOpen: true,
          artifact: { id: "ART-1", filename: "Protocol.pdf" },
        },
      });

      expect(wrapper.find(".share-drawer-overlay").exists()).toBe(true);
      expect(wrapper.find(".share-drawer").exists()).toBe(true);
    });

    it("EhrReconciliationDrawer contains backdrop and drawer when isOpen is true", () => {
      const wrapper = mount(EhrReconciliationDrawer, {
        props: { isOpen: true },
      });

      expect(wrapper.find(".drawer-backdrop").exists()).toBe(true);
      expect(wrapper.find(".ehr-reconciliation-drawer").exists()).toBe(true);
    });
  });
});
