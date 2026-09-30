import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import DocumentGrid from "../src/components/etmf/DocumentGrid.vue";
import { useEtmfStore } from "../src/stores/etmf";
import { useToastStore } from "../src/stores/toasts";

vi.mock("../src/api/etmf", () => ({
  etmfService: {
    getDocuments: vi.fn(() => Promise.resolve([])),
    ingestDocument: vi.fn(() => Promise.resolve({ id: "DOC-9" })),
  },
}));

/**
 * #4523: an eTMF upload never files placeholder text as document content. An unreadable or empty file aborts
 * the upload, and a failed ingestion is reported.
 */
describe("DocumentGrid upload without placeholder content (#4523)", () => {
  let pinia;
  let etmfStore;
  let uploadSpy;
  const RealFileReader = globalThis.FileReader;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    etmfStore = useEtmfStore();
    uploadSpy = vi.spyOn(etmfStore, "uploadDocument").mockResolvedValue({});
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    globalThis.FileReader = RealFileReader;
    vi.restoreAllMocks();
  });

  function mountWithFile(file) {
    const wrapper = mount(DocumentGrid, {
      props: { documents: [] },
      global: { plugins: [pinia] },
    });
    wrapper.vm.selectedFile = file;
    wrapper.vm.reasonForChange = "Initial filing of protocol";
    wrapper.vm.artifactCode = "01.01.01";
    wrapper.vm.artifactType = "Clinical Trial Protocol";
    return wrapper;
  }

  function expectAbortedWithToast(wrapper) {
    expect(uploadSpy).not.toHaveBeenCalled();
    expect(wrapper.vm.uploadError).not.toBe("");
    const toasts = useToastStore().toasts;
    expect(toasts.some((t) => t.tone === "error")).toBe(true);
  }

  it("aborts the upload when the file cannot be read", async () => {
    globalThis.FileReader = class {
      readAsText() {
        this.error = new Error("NotReadableError");
        setTimeout(() => this.onerror?.(), 0);
      }
    };
    const wrapper = mountWithFile(
      new File(["%PDF-1.7"], "protocol.pdf", { type: "application/pdf" })
    );

    await wrapper.vm.submitUpload();

    expectAbortedWithToast(wrapper);
    expect(wrapper.vm.uploadError).toContain("could not be read");
  });

  it("aborts the upload when the file is empty", async () => {
    const wrapper = mountWithFile(
      new File([], "empty.pdf", { type: "application/pdf" })
    );

    await wrapper.vm.submitUpload();

    expectAbortedWithToast(wrapper);
    expect(wrapper.vm.uploadError).toContain("empty");
  });

  it.each([500, 502, 504])(
    "reports an ingestion failure with HTTP %i",
    async (status) => {
      uploadSpy.mockRejectedValue(
        Object.assign(new Error("Ingestion failed"), { status })
      );
      const wrapper = mountWithFile(
        new File(["%PDF-1.7 protocol body"], "protocol.pdf", {
          type: "application/pdf",
        })
      );

      await wrapper.vm.submitUpload();

      expect(uploadSpy).toHaveBeenCalledTimes(1);
      const toasts = useToastStore().toasts;
      expect(toasts[0]).toMatchObject({ tone: "error" });
      expect(toasts[0].message).toContain(`HTTP ${status}`);
      expect(wrapper.vm.uploadError).toBe("Ingestion failed");
    }
  );

  it("uploads the file's real text content", async () => {
    const wrapper = mountWithFile(
      new File(["%PDF-1.7 protocol body"], "protocol.pdf", {
        type: "application/pdf",
      })
    );

    await wrapper.vm.submitUpload();

    expect(uploadSpy).toHaveBeenCalledTimes(1);
    const content = uploadSpy.mock.calls[0][0].get("content");
    expect(content).toContain("%PDF-1.7 protocol body");
    expect(content).not.toMatch(/mock/i);
    expect(useToastStore().toasts).toHaveLength(0);
  });
});
