import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { ref, nextTick } from "vue";
import { use2DGrid } from "../../src/composables/use2DGrid.js";
import CodingQueueTable from "../../src/components/CodingQueueTable.vue";
import DocumentGrid from "../../src/components/etmf/DocumentGrid.vue";
import { useCodingStore } from "../../src/stores/coding.js";
import { useEtmfStore } from "../../src/stores/etmf.js";

describe("2D Grid Keyboard Navigation & ARIA Semantics Engine", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  describe("Composable: use2DGrid", () => {
    it("initializes active cell coordinates to (0,0) and manages roving tabindex", () => {
      const rows = ref(5);
      const cols = ref(4);
      const grid = use2DGrid(rows, cols);

      expect(grid.activeRowIndex.value).toBe(0);
      expect(grid.activeColIndex.value).toBe(0);

      // Only (0,0) has tabindex 0
      expect(grid.getCellTabindex(0, 0)).toBe(0);
      expect(grid.getCellTabindex(0, 1)).toBe(-1);
      expect(grid.getCellTabindex(1, 0)).toBe(-1);
      expect(grid.isCellActive(0, 0)).toBe(true);
      expect(grid.isCellActive(0, 1)).toBe(false);
    });

    it("moves focus across adjacent matrix cells using arrow keys", () => {
      const rows = ref(3);
      const cols = ref(3);
      const grid = use2DGrid(rows, cols);

      // ArrowDown -> (1, 0)
      const eventDown = new KeyboardEvent("keydown", { key: "ArrowDown" });
      grid.handleCellKeyDown(eventDown, 0, 0);
      expect(grid.activeRowIndex.value).toBe(1);
      expect(grid.activeColIndex.value).toBe(0);

      // ArrowRight -> (1, 1)
      const eventRight = new KeyboardEvent("keydown", { key: "ArrowRight" });
      grid.handleCellKeyDown(eventRight, 1, 0);
      expect(grid.activeRowIndex.value).toBe(1);
      expect(grid.activeColIndex.value).toBe(1);

      // ArrowUp -> (0, 1)
      const eventUp = new KeyboardEvent("keydown", { key: "ArrowUp" });
      grid.handleCellKeyDown(eventUp, 1, 1);
      expect(grid.activeRowIndex.value).toBe(0);
      expect(grid.activeColIndex.value).toBe(1);

      // ArrowLeft -> (0, 0)
      const eventLeft = new KeyboardEvent("keydown", { key: "ArrowLeft" });
      grid.handleCellKeyDown(eventLeft, 0, 1);
      expect(grid.activeRowIndex.value).toBe(0);
      expect(grid.activeColIndex.value).toBe(0);
    });

    it("enforces strict boundary clamping when navigating past matrix edges", () => {
      const rows = ref(2);
      const cols = ref(2);
      const grid = use2DGrid(rows, cols);

      // Attempt moving Up past top boundary
      const eventUp = new KeyboardEvent("keydown", { key: "ArrowUp" });
      grid.handleCellKeyDown(eventUp, 0, 0);
      expect(grid.activeRowIndex.value).toBe(0);

      // Move to bottom right (1, 1)
      grid.selectCell(1, 1, false);

      // Attempt moving Down past bottom boundary
      const eventDown = new KeyboardEvent("keydown", { key: "ArrowDown" });
      grid.handleCellKeyDown(eventDown, 1, 1);
      expect(grid.activeRowIndex.value).toBe(1);

      // Attempt moving Right past right boundary
      const eventRight = new KeyboardEvent("keydown", { key: "ArrowRight" });
      grid.handleCellKeyDown(eventRight, 1, 1);
      expect(grid.activeColIndex.value).toBe(1);
    });

    it("supports Home and End key navigation across matrix rows", () => {
      const rows = ref(4);
      const cols = ref(5);
      const grid = use2DGrid(rows, cols, { initialRow: 2, initialCol: 2 });

      // End key moves to last column in row
      const eventEnd = new KeyboardEvent("keydown", { key: "End" });
      grid.handleCellKeyDown(eventEnd, 2, 2);
      expect(grid.activeColIndex.value).toBe(4);

      // Home key moves to first column in row
      const eventHome = new KeyboardEvent("keydown", { key: "Home" });
      grid.handleCellKeyDown(eventHome, 2, 4);
      expect(grid.activeColIndex.value).toBe(0);
    });

    it("clamps active coordinates when dataset size shrinks due to search filtering", async () => {
      const rows = ref(10);
      const cols = ref(4);
      const grid = use2DGrid(rows, cols, { initialRow: 8, initialCol: 2 });

      expect(grid.activeRowIndex.value).toBe(8);

      // Filter applies, reducing dataset to 3 items
      rows.value = 3;
      await nextTick();

      // Coordinates clamp automatically to last valid row (index 2)
      expect(grid.activeRowIndex.value).toBe(2);
    });

    it("switches to edit mode and returns focus to cell upon exiting edit mode", async () => {
      const rows = ref(3);
      const cols = ref(3);
      const grid = use2DGrid(rows, cols);

      grid.startEditing(1, 1);
      expect(grid.isEditing.value).toBe(true);
      expect(grid.activeRowIndex.value).toBe(1);
      expect(grid.activeColIndex.value).toBe(1);

      await grid.exitEditing();
      expect(grid.isEditing.value).toBe(false);
    });
  });

  describe("Component: CodingQueueTable.vue ARIA & Keyboard Navigation", () => {
    const mockCodingAssignments = [
      {
        id: "CODE-1",
        verbatim_text: "Severe Headache",
        status: "SUGGESTED",
        source_field: "AE.AETERM",
        domain: "AE",
        observation_id: "OBS-123456789",
        dictionary_type: "MEDDRA",
        dictionary_version: "26.0",
        coded_code: null,
        coded_term: null,
        suggestions: [
          {
            pt_name: "Headache",
            llt_name: "Severe headache",
            code: "10019211",
            score: 0.95,
          },
        ],
      },
      {
        id: "CODE-2",
        verbatim_text: "Nausea",
        status: "UNCODED",
        source_field: "AE.AETERM",
        domain: "AE",
        observation_id: "OBS-987654321",
        dictionary_type: "MEDDRA",
        dictionary_version: "26.0",
        coded_code: null,
        coded_term: null,
        suggestions: [],
      },
    ];

    it("exposes explicit ARIA grid roles and column header scope attributes", () => {
      const codingStore = useCodingStore(pinia);
      codingStore.assignments = mockCodingAssignments;

      const wrapper = mount(CodingQueueTable, {
        global: { plugins: [pinia] },
      });

      const table = wrapper.find("table");
      expect(table.attributes("role")).toBe("grid");
      expect(table.attributes("aria-label")).toBe("Medical Coding Queue Table");

      const headers = wrapper.findAll("th");
      expect(headers.length).toBeGreaterThan(0);
      headers.forEach((th) => {
        expect(th.attributes("scope")).toBe("col");
      });

      const rows = wrapper.findAll("tr.coding-row");
      expect(rows.length).toBe(2);

      const cells = wrapper.findAll("td");
      expect(cells.length).toBe(14);
      cells.forEach((cell) => {
        expect(cell.attributes("role")).toBe("gridcell");
      });
    });

    it("enforces 2D roving tabindex so exactly one cell per table has tabindex 0", () => {
      const codingStore = useCodingStore(pinia);
      codingStore.assignments = mockCodingAssignments;

      const wrapper = mount(CodingQueueTable, {
        global: { plugins: [pinia] },
      });

      const cells = wrapper.findAll("td[role='gridcell']");
      const activeCells = cells.filter((c) => c.attributes("tabindex") === "0");
      const inactiveCells = cells.filter(
        (c) => c.attributes("tabindex") === "-1"
      );

      expect(activeCells.length).toBe(1);
      expect(inactiveCells.length).toBe(cells.length - 1);

      // Active cell is top-left (0,0)
      const firstCell = wrapper.find('td[data-row="0"][data-col="0"]');
      expect(firstCell.attributes("tabindex")).toBe("0");
    });

    it("navigates active focus between cells when arrow keys are pressed", async () => {
      const codingStore = useCodingStore(pinia);
      codingStore.assignments = mockCodingAssignments;

      const wrapper = mount(CodingQueueTable, {
        global: { plugins: [pinia] },
      });

      const cell00 = wrapper.find('td[data-row="0"][data-col="0"]');
      await cell00.trigger("click");
      expect(cell00.classes()).toContain("cell-active");

      // Press ArrowDown to move to (1, 0)
      await cell00.trigger("keydown", { key: "ArrowDown" });
      const cell10 = wrapper.find('td[data-row="1"][data-col="0"]');
      expect(cell10.classes()).toContain("cell-active");
      expect(cell10.attributes("tabindex")).toBe("0");
      expect(cell00.attributes("tabindex")).toBe("-1");

      // Press ArrowRight to move to (1, 1)
      await cell10.trigger("keydown", { key: "ArrowRight" });
      const cell11 = wrapper.find('td[data-row="1"][data-col="1"]');
      expect(cell11.classes()).toContain("cell-active");
      expect(cell11.attributes("tabindex")).toBe("0");
    });

    it("passes accessibility audit with zero WCAG violations", async () => {
      const codingStore = useCodingStore(pinia);
      codingStore.assignments = mockCodingAssignments;

      const wrapper = mount(CodingQueueTable, {
        global: { plugins: [pinia] },
      });

      await expect(wrapper).toBeAccessible();
    });
  });

  describe("Component: DocumentGrid.vue ARIA & Keyboard Navigation", () => {
    const mockDocuments = [
      {
        id: "DOC-101",
        study_id: "STUDY-001",
        zone: 1,
        section: "01.01",
        artifact_code: "01.01.01",
        artifact_type: "Trial Protocol",
        filename: "protocol_v1.pdf",
        mime_type: "application/pdf",
        created_at: "2026-08-01T12:00:00Z",
        created_by: "fderuiter",
        version_index: 1,
        status: "APPROVED",
      },
    ];

    it("exposes explicit ARIA grid roles and column header scope attributes", async () => {
      const store = useEtmfStore(pinia);
      await store.fetchBinderTree();

      const wrapper = mount(DocumentGrid, {
        props: { documents: mockDocuments },
        global: { plugins: [pinia] },
      });

      const table = wrapper.find("table");
      expect(table.attributes("role")).toBe("grid");
      expect(table.attributes("aria-label")).toBe(
        "Artifact Documents Registry"
      );

      const headers = wrapper.findAll("th");
      headers.forEach((th) => {
        expect(th.attributes("scope")).toBe("col");
      });

      const cells = wrapper.findAll("td");
      cells.forEach((cell) => {
        expect(cell.attributes("role")).toBe("gridcell");
      });
    });

    it("enforces single active tabindex=0 cell in 2D roving tabindex model", async () => {
      const store = useEtmfStore(pinia);
      await store.fetchBinderTree();

      const wrapper = mount(DocumentGrid, {
        props: { documents: mockDocuments },
        global: { plugins: [pinia] },
      });

      const cells = wrapper.findAll("td[role='gridcell']");
      const activeCells = cells.filter((c) => c.attributes("tabindex") === "0");

      expect(activeCells.length).toBe(1);
    });

    it("passes accessibility audit with zero WCAG violations", async () => {
      const store = useEtmfStore(pinia);
      await store.fetchBinderTree();

      const wrapper = mount(DocumentGrid, {
        props: { documents: mockDocuments },
        global: { plugins: [pinia] },
      });

      await expect(wrapper).toBeAccessible();
    });
  });
});
