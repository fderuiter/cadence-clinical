import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { reactive } from "vue";
import { kernelApi } from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import { useClinicalStore } from "../../src/stores/clinical";
import { useSchemaIngestion } from "../../src/composables/useSchemaIngestion";
import DesignerSchemaPanel from "../../src/components/persona/DesignerSchemaPanel.vue";

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      listProtocolTemplates: vi.fn(async () => []),
      importProtocolDraft: vi.fn(),
    },
  };
});

describe("DesignerSchemaPanel synchronous ODM/USDM & CDISC eCRF import", () => {
  let pinia;
  let store;
  let alertSpy;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    store = useClinicalStore();
    store.activeStudyId = "CADENCE-101";
    vi.mocked(kernelApi.listProtocolTemplates).mockReset();
    vi.mocked(kernelApi.importProtocolDraft).mockReset();
    alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  function mountPanel() {
    const schema = reactive(useSchemaIngestion(store));
    const wrapper = mount({
      components: { DesignerSchemaPanel },
      setup() {
        return { store, schema };
      },
      template: `
        <DesignerSchemaPanel
          v-model:import-change-reason="schema.importChangeReason"
          v-model:selected-template-ids="schema.selectedTemplateIds"
          :store="store"
          :selected-file-name="schema.selectedFileName"
          :available-templates="schema.availableTemplates"
          :importing-draft="schema.importingDraft"
          :import-success-message="schema.importSuccessMessage"
          :import-error-message="schema.importErrorMessage"
          :imported-draft-summary="schema.importedDraftSummary"
          :unreviewed-count="schema.unreviewedCount"
          :get-confidence-class="schema.getConfidenceClass"
          :get-status-class="schema.getStatusClass"
          @register-file-input="schema.setFileInputElement"
          @trigger-document-upload="schema.triggerDocumentUpload"
          @import-selected-templates="schema.importSelectedTemplates"
        />
      `,
    });
    return { wrapper, schema };
  }

  it("opens the XML/JSON file chooser reliably and imports USDM v4 JSON without window.alert", async () => {
    vi.mocked(kernelApi.importProtocolDraft).mockResolvedValue({
      version: {
        ref: "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000d1@1",
        study: "study:0192f3c4-5e6a-7b8c-9d0e-000000000020@1",
        number: 1,
        parent_version: null,
        status: "Draft",
        content_hash: null,
        amendment_intent: null,
        approval_attestation: null,
        withdrawal_reason: null,
        mask: null,
      },
      title: "USDM Cardiology Draft",
      form_count: 1,
      item_count: 1,
      forms: [
        {
          id: "ECG_FORM",
          display_order: 1,
          name: "12-Lead ECG",
          item_count: 1,
          required_item_count: 1,
          items: [
            {
              id: "QT_INT",
              display_order: 1,
              prompt: "QT Interval (ms)",
              concept: "concept:0192f3c4-5e6a-7b8c-9d0e-0000000000e1@1",
              required: true,
              value_kind: "Quantity",
              unit: "ms",
            },
          ],
        },
      ],
      import_notes: [
        {
          source: "Study_1",
          message: "Optional vendor extension recorded.",
        },
      ],
    });

    const { wrapper } = mountPanel();
    await flushPromises();

    const fileInput = wrapper.find("#designer-schema-file-input");
    expect(fileInput.attributes("accept")).toContain(".xml");
    expect(fileInput.attributes("accept")).toContain(".json");

    const clickSpy = vi.spyOn(fileInput.element, "click");
    await wrapper.find("#btn-select-protocol-file").trigger("click");
    expect(clickSpy).toHaveBeenCalled();

    const usdmFile = new File(['{"usdmVersion":"4.0.0"}'], "study-usdm.json", {
      type: "application/json",
    });
    Object.defineProperty(fileInput.element, "files", {
      value: [usdmFile],
      configurable: true,
    });
    await fileInput.trigger("change");
    await flushPromises();

    expect(kernelApi.importProtocolDraft).toHaveBeenCalledWith(
      expect.objectContaining({
        studyRef: "CADENCE-101",
        file: usdmFile,
        reason: "Import protocol draft into study",
      })
    );
    expect(wrapper.find("#schema-import-success").text()).toContain(
      "Imported Draft version 1"
    );
    expect(wrapper.find("#designer-import-notes").text()).toContain(
      "Optional vendor extension recorded."
    );
    expect(wrapper.find("#designer-imported-draft-summary").text()).toContain(
      "QT Interval (ms)"
    );
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it("displays an actionable inline error banner on malformed USDM JSON with zero window.alert calls", async () => {
    vi.mocked(kernelApi.importProtocolDraft).mockRejectedValue(
      new ApiError("USDM v4 JSON is missing 'study.versions'.", 422)
    );

    const { wrapper } = mountPanel();
    await flushPromises();

    const fileInput = wrapper.find("#designer-schema-file-input");
    const badJson = new File(["{broken"], "broken.json", {
      type: "application/json",
    });
    Object.defineProperty(fileInput.element, "files", {
      value: [badJson],
      configurable: true,
    });
    await fileInput.trigger("change");
    await flushPromises();

    expect(wrapper.find("#schema-import-error").text()).toContain(
      "USDM v4 JSON is missing 'study.versions'."
    );
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it("rejects .pdf uploads with actionable ODM XML / USDM JSON guidance and never calls store.uploadProtocolDocument (@req:PRD-CRF-006)", async () => {
    const uploadDocSpy = vi
      .spyOn(store, "uploadProtocolDocument")
      .mockResolvedValue({});

    const { wrapper } = mountPanel();
    await flushPromises();

    const fileInput = wrapper.find("#designer-schema-file-input");
    const pdfFile = new File(["%PDF-1.7"], "protocol-v1.pdf", {
      type: "application/pdf",
    });
    Object.defineProperty(fileInput.element, "files", {
      value: [pdfFile],
      configurable: true,
    });
    await fileInput.trigger("change");
    await flushPromises();

    expect(uploadDocSpy).not.toHaveBeenCalled();
    expect(kernelApi.importProtocolDraft).not.toHaveBeenCalled();
    expect(wrapper.find("#schema-import-error").text()).toContain(
      "PDF protocol digitization is not yet available for 'protocol-v1.pdf'. Upload an ODM v1.3.2 XML (.xml) or USDM v4 JSON (.json) file."
    );
    expect(alertSpy).not.toHaveBeenCalled();
  });
});
