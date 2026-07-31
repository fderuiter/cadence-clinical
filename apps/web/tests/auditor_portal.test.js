import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import AuditView from "../src/views/AuditView.vue";
import { auditorService } from "../src/api/auditor";
import { etmfService } from "../src/api/etmf";

// Setup router for testing
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/audit", name: "audit", component: AuditView },
    {
      path: "/login",
      name: "login",
      component: { template: "<div>Login</div>" },
    },
    {
      path: "/forbidden",
      name: "forbidden",
      component: { template: "<div>Forbidden</div>" },
    },
  ],
});

// Mock Auditor and eTMF APIs
vi.mock("../src/api/auditor", () => ({
  auditorService: {
    getAuditLogs: vi.fn(),
    getExecutionIntegrity: vi.fn(),
    getWatermarkedDownloadUrl: vi.fn(),
    getBinderExportUrl: vi.fn(),
  },
}));

vi.mock("../src/api/etmf", () => ({
  etmfService: {
    getDocuments: vi.fn(),
    getCompleteness: vi.fn(),
  },
}));

let pinia;

describe("Auditor Portal - eTMF Inspection & Execution Sealing Integration", () => {
  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);

    // Default mock implementation resets
    vi.clearAllMocks();

    auditorService.getAuditLogs.mockResolvedValue({
      items: [
        {
          id: "log-1",
          timestamp: "2026-08-20T10:00:00Z",
          user_id: "auditor_user",
          user_role: "auditor",
          action: "AUDIT_VIEW",
          document_id: null,
          details: "Accessed eTMF immutable audit trail logs.",
        },
        {
          id: "log-2",
          timestamp: "2026-08-20T09:30:00Z",
          user_id: "crc_user",
          user_role: "site_investigator",
          action: "INGEST",
          document_id: "doc-123",
          details:
            "Ingested artifact type 'FDA Form 1572' for study 'study_001'.",
        },
      ],
      total_count: 2,
    });

    auditorService.getExecutionIntegrity.mockResolvedValue({
      verified: true,
      message:
        "GxP clinical execution ledger chain fully verified and structurally intact.",
    });

    auditorService.getWatermarkedDownloadUrl.mockReturnValue(
      "http://localhost:8000/api/v1/etmf/documents/doc-123/watermark"
    );

    auditorService.getBinderExportUrl.mockReturnValue(
      "http://localhost:8000/api/v1/etmf/studies/study_001/binder?include_history=false"
    );

    etmfService.getDocuments.mockResolvedValue([
      {
        id: "doc-123",
        study_id: "study_001",
        zone: 5,
        section: "02",
        artifact_type: "FDA Form 1572",
        filename: "form_1572_v1.txt",
        status: "SIGNED",
        version_index: 1,
      },
    ]);

    etmfService.getCompleteness.mockResolvedValue({
      study_id: "study_001",
      site_id: null,
      milestone: "INITIATION",
      is_complete: true,
      scope: "study",
      present_artifacts: ["FDA Form 1572"],
      missing_artifacts: [],
      per_artifact_detail: [
        {
          artifact_type: "FDA Form 1572",
          scope: "study",
          status: "SIGNED",
          document_id: "doc-123",
          version_index: 1,
        },
      ],
    });

    // Stub out global fetch for document secure previews
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      text: () => Promise.resolve("MOCK DOCUMENT WATERMARKED CONTENT PREVIEW"),
    });

    // Mock URL helper
    global.window.URL.createObjectURL = vi
      .fn()
      .mockReturnValue("blob:mock-url");
    global.window.URL.revokeObjectURL = vi.fn();
  });

  it("mounts correctly and fetches audit logs, documents, and integrity checks", async () => {
    const wrapper = mount(AuditView, {
      global: { plugins: [pinia, router] },
    });

    // Verify initial layout renders
    expect(wrapper.text()).toContain("Regulatory Auditor & Inspection Portal");
    expect(wrapper.text()).toContain("GxP Execution Ledger Chain Verification");

    // Wait for async calls on onMounted
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(auditorService.getAuditLogs).toHaveBeenCalled();
    expect(etmfService.getDocuments).toHaveBeenCalled();
    expect(auditorService.getExecutionIntegrity).toHaveBeenCalled();

    // Verify loaded elements are rendered
    expect(wrapper.text()).toContain("INTEGRITY VERIFIED");
    expect(wrapper.text()).toContain("form_1572_v1.txt");
    expect(wrapper.text()).toContain("Ingested artifact type 'FDA Form 1572'");
    expect(wrapper.text()).toContain("auditor_user");
  });

  it("applies filters correctly on eTMF audit logs when requested by the auditor", async () => {
    const wrapper = mount(AuditView, {
      global: { plugins: [pinia, router] },
    });

    // Set filter values
    const userInput = wrapper.find(".filter-user-id");
    await userInput.setValue("test_actor");

    const actionSelect = wrapper.find(".filter-action");
    await actionSelect.setValue("INGEST");

    const docInput = wrapper.find(".filter-document-id");
    await docInput.setValue("doc-123");

    // Click apply filters
    await wrapper.find(".btn-apply-filters").trigger("click");

    expect(auditorService.getAuditLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({
        user_id: "test_actor",
        action: "INGEST",
        document_id: "doc-123",
      })
    );
  });

  it("clears filters and returns back to standard audit logs list", async () => {
    const wrapper = mount(AuditView, {
      global: { plugins: [pinia, router] },
    });

    const userInput = wrapper.find(".filter-user-id");
    await userInput.setValue("test_actor");

    await wrapper.find(".btn-clear-filters").trigger("click");

    expect(filtersFromLastCall(auditorService.getAuditLogs)).toEqual({
      limit: 20,
      offset: 0,
      user_id: undefined,
      action: undefined,
      document_id: undefined,
    });
  });

  it("loads and displays secure watermarked document preview in viewer panel with visual overlay", async () => {
    const wrapper = mount(AuditView, {
      global: { plugins: [pinia, router] },
    });

    await new Promise((resolve) => setTimeout(resolve, 50));

    // Click preview on the first document in the table
    const btnPreview = wrapper.find(".btn-preview-doc");
    expect(btnPreview.exists()).toBe(true);
    await btnPreview.trigger("click");

    // Wait for secure content fetch
    await new Promise((resolve) => setTimeout(resolve, 50));

    // Verify viewer panel is displayed
    const previewPanel = wrapper.find(".secure-preview-panel");
    expect(previewPanel.exists()).toBe(true);
    expect(previewPanel.text()).toContain("Secure Preview: form_1572_v1.txt");
    expect(previewPanel.text()).toContain(
      "MOCK DOCUMENT WATERMARKED CONTENT PREVIEW"
    );

    // Verify presence of client-side visual watermark overlay
    const overlay = wrapper.find(".watermark-overlay");
    expect(overlay.exists()).toBe(true);
    expect(overlay.attributes("style")).toContain("background-repeat: repeat");

    // Close preview
    await wrapper.find(".btn-close-preview").trigger("click");
    expect(wrapper.find(".secure-preview-panel").exists()).toBe(false);
  });

  it("triggers GxP regulatory binder ZIP export with proper auth header downloads", async () => {
    const wrapper = mount(AuditView, {
      global: { plugins: [pinia, router] },
    });

    const mockAnchor = {
      click: vi.fn(),
      href: "",
      download: "",
    };
    const originalCreateElement = document.createElement;
    vi.spyOn(document, "createElement").mockImplementation((tag) => {
      if (tag === "a") return mockAnchor;
      return originalCreateElement.call(document, tag);
    });
    vi.spyOn(document.body, "appendChild").mockImplementation(() => {});
    vi.spyOn(document.body, "removeChild").mockImplementation(() => {});

    // Trigger Binder ZIP export
    const btnExport = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Export Regulatory Binder"));
    expect(btnExport.exists()).toBe(true);
    await btnExport.trigger("click");

    expect(auditorService.getBinderExportUrl).toHaveBeenCalledWith(
      "study_001",
      false
    );
    expect(global.fetch).toHaveBeenCalled();
  });

  it("loads and displays eTMF completeness tracking results correctly in live-updating dashboard", async () => {
    const wrapper = mount(AuditView, {
      global: { plugins: [pinia, router] },
    });

    // Wait for initial completeness auto-load
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(etmfService.getCompleteness).toHaveBeenCalledWith({
      study_id: "study_001",
      milestone: "INITIATION",
    });

    expect(wrapper.text()).toContain(
      "eTMF Completeness Tracking & Verification"
    );
    expect(wrapper.text()).toContain("MILESTONE COMPLIANT");
    expect(wrapper.text()).toContain("1 / 1 Artifacts Present");
    expect(wrapper.text()).toContain("FDA Form 1572");
    expect(wrapper.text()).toContain("SIGNED");
  });

  it("handles completeness tracking errors and shows standard error alert", async () => {
    etmfService.getCompleteness.mockRejectedValueOnce(
      new Error("Database connection timeout during EDL check")
    );

    const wrapper = mount(AuditView, {
      global: { plugins: [pinia, router] },
    });

    // Wait for completeness load to reject
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(wrapper.text()).toContain(
      "Database connection timeout during EDL check"
    );
  });

  it("ensures write and ledger purge controls are completely removed and unavailable to Auditor role", () => {
    const wrapper = mount(AuditView, {
      global: { plugins: [pinia, router] },
    });

    // Check that there is no purge control section anymore
    expect(wrapper.text()).not.toContain(
      "Sandbox / Non-Production Demo Status Controls"
    );
    expect(wrapper.find("#btn-clear-ledger").exists()).toBe(false);
  });
});

function filtersFromLastCall(mockFn) {
  return mockFn.mock.calls[mockFn.mock.calls.length - 1][0];
}
