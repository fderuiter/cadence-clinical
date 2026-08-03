import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useEtmfStore } from "../../src/stores/etmf";
import DocumentGrid from "../../src/components/etmf/DocumentGrid.vue";
import { etmfService } from "../../src/api/etmf";

vi.mock("../../src/api/etmf", () => ({
  etmfService: {
    tagDocument: vi.fn(() => Promise.resolve({ success: true })),
    getDocuments: vi.fn(() => Promise.resolve([])),
  },
}));

describe("2D Interactive Excel-Like Grid (DocumentGrid.vue)", () => {
  let pinia;
  let writeTextMock;

  const mockDocuments = [
    {
      id: "DOC-1",
      study_id: "STUDY-USDM-001",
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      filename: "protocol_v1.pdf",
      mime_type: "application/pdf",
      created_at: "2026-08-01T12:00:00Z",
      created_by: "fderuiter",
      version_index: 1,
      status: "APPROVED",
    },
    {
      id: "DOC-2",
      study_id: "STUDY-USDM-001",
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.02",
      artifact_type: "Clinical Trial Protocol Amendment",
      filename: "amendment_v1.pdf",
      mime_type: "application/pdf",
      created_at: "2026-08-02T12:00:00Z",
      created_by: "jules",
      version_index: 2,
      status: "DRAFT",
    },
  ];

  beforeEach(async () => {
    pinia = createPinia();
    setActivePinia(pinia);

    // Mock clipboard
    writeTextMock = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, "clipboard", {
      value: {
        writeText: writeTextMock,
      },
      writable: true,
      configurable: true,
    });

    // Initialize store TMF structure
    const store = useEtmfStore(pinia);
    await store.fetchBinderTree();

    vi.clearAllMocks();
  });

  it("handles mouse selection and applies high-contrast focus styling class", async () => {
    const wrapper = mount(DocumentGrid, {
      props: {
        documents: mockDocuments,
      },
      global: {
        plugins: [pinia],
      },
    });

    const firstCell = wrapper.find('td[data-row="0"][data-col="0"]');
    expect(firstCell.exists()).toBe(true);

    await firstCell.trigger("click");
    expect(firstCell.classes()).toContain("cell-active");
  });

  it("navigates across 2D matrix rows and columns using arrow keys", async () => {
    const wrapper = mount(DocumentGrid, {
      props: {
        documents: mockDocuments,
      },
      global: {
        plugins: [pinia],
      },
    });

    const firstCell = wrapper.find('td[data-row="0"][data-col="0"]');
    await firstCell.trigger("click");
    expect(firstCell.classes()).toContain("cell-active");

    // Move Down to row 1, col 0
    await firstCell.trigger("keydown", { key: "ArrowDown" });
    const row1col0 = wrapper.find('td[data-row="1"][data-col="0"]');
    expect(row1col0.classes()).toContain("cell-active");

    // Move Right to row 1, col 1
    await row1col0.trigger("keydown", { key: "ArrowRight" });
    const row1col1 = wrapper.find('td[data-row="1"][data-col="1"]');
    expect(row1col1.classes()).toContain("cell-active");

    // Move Up to row 0, col 1
    await row1col1.trigger("keydown", { key: "ArrowUp" });
    const row0col1 = wrapper.find('td[data-row="0"][data-col="1"]');
    expect(row0col1.classes()).toContain("cell-active");

    // Move Left to row 0, col 0
    await row0col1.trigger("keydown", { key: "ArrowLeft" });
    expect(firstCell.classes()).toContain("cell-active");
  });

  it("performs cell copy to clipboard via hotkey combination", async () => {
    const wrapper = mount(DocumentGrid, {
      props: {
        documents: mockDocuments,
      },
      global: {
        plugins: [pinia],
      },
    });

    // Select Doc name cell
    const nameCell = wrapper.find('td[data-row="0"][data-col="0"]');
    await nameCell.trigger("click");
    await nameCell.trigger("keydown", { ctrlKey: true, key: "c" });
    expect(writeTextMock).toHaveBeenCalledWith("protocol_v1.pdf");

    // Select Taxonomy cell
    const taxCell = wrapper.find('td[data-row="0"][data-col="1"]');
    await taxCell.trigger("click");
    await taxCell.trigger("keydown", { metaKey: true, key: "C" });
    expect(writeTextMock).toHaveBeenCalledWith("Z1 - S01.01 [01.01.01]");

    // Select Version cell
    const verCell = wrapper.find('td[data-row="0"][data-col="2"]');
    await verCell.trigger("click");
    await verCell.trigger("keydown", { ctrlKey: true, key: "c" });
    expect(writeTextMock).toHaveBeenCalledWith("v1.0");
  });

  it("toggles inline edit taxonomy select dropdown on Enter key press and commits update on select option change", async () => {
    const wrapper = mount(DocumentGrid, {
      props: {
        documents: mockDocuments,
      },
      global: {
        plugins: [pinia],
      },
    });

    const cell = wrapper.find('td[data-row="1"][data-col="1"]');
    await cell.trigger("click");
    await cell.trigger("keydown", { key: "Enter" });

    // Dropdown select must be visible
    const select = wrapper.find("select.inline-select");
    expect(select.exists()).toBe(true);

    // Let's choose "01.01.03" protocol signoff and save
    await select.setValue("01.01.03");
    await select.trigger("change");

    expect(etmfService.tagDocument).toHaveBeenCalledWith(
      "DOC-2",
      {
        zone: 1,
        section: "01.01",
        artifact_code: "01.01.03",
      },
      {
        changeReason: "Corrected taxonomy classification via interactive grid navigation",
      }
    );
  });
});
