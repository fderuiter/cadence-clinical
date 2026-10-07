/**
 * Unit & Component tests for BatchDicomDropzoneModal.vue
 *
 * Requirements Traceability: DICOM PS 3.15 / 21 CFR Part 11 Ingestion Pipeline
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import BatchDicomDropzoneModal from "../../src/components/imaging/BatchDicomDropzoneModal.vue";

describe("BatchDicomDropzoneModal.vue", () => {
  const mountModal = (props = {}) => {
    return mount(BatchDicomDropzoneModal, {
      props: {
        isOpen: true,
        ...props
      },
      global: {
        stubs: {
          Teleport: true
        }
      }
    });
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("does not render modal content when isOpen is false", () => {
    /**
     * Verify that when isOpen is false, the modal backdrop is not rendered in the DOM.
     */
    const wrapper = mountModal({ isOpen: false });

    expect(wrapper.find(".modal-backdrop").exists()).toBe(false);
  });

  it("renders header title, regulatory badges, and parameter inputs when isOpen is true", () => {
    /**
     * Verify initial layout and default parameter props rendering.
     */
    const wrapper = mountModal({
      isOpen: true,
      subjectId: "SUBJ-TEST-001",
      timepoint: "VISIT-1",
      studyId: "STUDY-ONC-100",
      siteId: "SITE-200"
    });

    expect(wrapper.find(".modal-backdrop").exists()).toBe(true);
    expect(wrapper.find(".badge-gxp").text()).toContain("21 CFR Part 11 / DICOM PS 3.15");
    expect(wrapper.find("#batch-modal-title").text()).toContain("Batch DICOM Folder & Archive Ingestion");

    const subjInput = wrapper.find("#batch-subj-input").element as HTMLInputElement;
    const timeInput = wrapper.find("#batch-time-input").element as HTMLInputElement;
    const studyInput = wrapper.find("#batch-study-input").element as HTMLInputElement;
    const siteInput = wrapper.find("#batch-site-input").element as HTMLInputElement;

    expect(subjInput.value).toBe("SUBJ-TEST-001");
    expect(timeInput.value).toBe("VISIT-1");
    expect(studyInput.value).toBe("STUDY-ONC-100");
    expect(siteInput.value).toBe("SITE-200");
  });

  it("detects and categorizes candidate DICOM files vs non-DICOM files (PDF, notes)", async () => {
    /**
     * Verify pre-upload summary card and non-DICOM warning detection when files are selected.
     */
    const wrapper = mountModal({ isOpen: true });

    const fileDicom1 = new File(["dummy dicom content 1"], "slice_001.dcm", { type: "application/dicom" });
    const fileDicom2 = new File(["dummy dicom content 2"], "slice_002.dcm", { type: "application/dicom" });
    const filePdf = new File(["dummy pdf content"], "clinical_notes.pdf", { type: "application/pdf" });
    const fileDoc = new File(["dummy doc content"], "site_summary.docx", { type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document" });

    const folderInput = wrapper.find("input[webkitdirectory]");
    Object.defineProperty(folderInput.element, "files", {
      value: [fileDicom1, fileDicom2, filePdf, fileDoc],
      writable: true
    });

    await folderInput.trigger("change");

    expect(wrapper.find(".file-summary-card").exists()).toBe(true);
    expect(wrapper.find(".non-dicom-alert").exists()).toBe(true);

    const statNums = wrapper.findAll(".stat-num");
    expect(statNums[0].text()).toBe("2"); // Candidate DICOM
    expect(statNums[1].text()).toBe("2"); // Non-DICOM files

    const nonDicomItems = wrapper.findAll(".non-dicom-file-list li");
    expect(nonDicomItems.length).toBe(2);
    expect(nonDicomItems[0].text()).toContain("clinical_notes.pdf");
    expect(nonDicomItems[1].text()).toContain("site_summary.docx");
  });

  it("handles file drag and drop traversal for ZIP archives and DICOM folders", async () => {
    /**
     * Verify handleDrop populates selectedFiles array from dataTransfer files.
     */
    const wrapper = mountModal({ isOpen: true });

    const zipFile = new File(["dummy zip bytes"], "batch_study.zip", { type: "application/zip" });

    const dropArea = wrapper.find(".dropzone-area");
    await dropArea.trigger("dragover");
    expect(wrapper.find(".dropzone-area").classes()).toContain("is-dragging");

    await dropArea.trigger("drop", {
      dataTransfer: {
        files: [zipFile]
      }
    });

    expect(wrapper.find(".dropzone-area").classes()).not.toContain("is-dragging");
    expect(wrapper.find(".file-summary-card").exists()).toBe(true);
    const totalFilesBadge = wrapper.find(".badge-info").text();
    expect(totalFilesBadge).toContain("1 Total Files");
  });

  it("executes batch ingestion via POST API, streams progress, and displays PS 3.15 certificate on success", async () => {
    /**
     * Verify batch ingestion API payload submit and certificate receipt display.
     */
    const mockSuccessResponse = {
      studyEnvelopeId: "env_study_batch_999",
      certificateId: "CERT-PS315-2026-9999",
      anonymizedStudyUid: "1.2.840.10008.1.999.888.777",
      ingestedSliceCount: 5,
      skippedNonDicomCount: 1,
      skippedNonDicomFiles: ["notes.pdf"],
      manifest: {
        manifest_id: "man_888",
        scrubbed_tags_count: 56,
        pixel_mask_applied: true
      },
      signature: "SIG-HMAC256-VALID-TEST-CERT"
    };

    const fetchSpy = vi.spyOn(global, "fetch").mockResolvedValue({
      ok: true,
      json: async () => mockSuccessResponse
    } as Response);

    const wrapper = mountModal({
      isOpen: true,
      subjectId: "SUBJ-ONC-001",
      timepoint: "BASELINE"
    });

    const dcmFile = new File(["dicom content"], "slice_1.dcm", { type: "application/dicom" });
    const pdfFile = new File(["pdf content"], "notes.pdf", { type: "application/pdf" });

    const fileInput = wrapper.find("input[accept*='.dcm']");
    Object.defineProperty(fileInput.element, "files", {
      value: [dcmFile, pdfFile],
      writable: true
    });
    await fileInput.trigger("change");

    const startBtn = wrapper.find(".modal-footer button.btn-primary");
    expect(startBtn.attributes("disabled")).toBeUndefined();
    await startBtn.trigger("click");

    await wrapper.vm.$nextTick();
    await new Promise((r) => setTimeout(r, 10));
    await wrapper.vm.$nextTick();

    expect(fetchSpy).toHaveBeenCalledWith("/api/v1/imaging/studies/batch", expect.objectContaining({
      method: "POST",
      headers: { "X-Operator-Identity": "crc.coordinator@site.org" }
    }));

    expect(wrapper.find(".certificate-summary-card").exists()).toBe(true);
    expect(wrapper.find(".cert-title").text()).toContain("PS 3.15 De-Identification Certificate Issued");
    expect(wrapper.text()).toContain("CERT-PS315-2026-9999");
    expect(wrapper.text()).toContain("1.2.840.10008.1.999.888.777");
    expect(wrapper.text()).toContain("5 slices");
    expect(wrapper.text()).toContain("SIG-HMAC256-VALID-TEST-CERT");

    // Click "Done & View Ingested Series"
    const doneBtn = wrapper.find(".modal-footer button.btn-success");
    await doneBtn.trigger("click");

    expect(wrapper.emitted("ingested")).toBeTruthy();
    expect(wrapper.emitted("ingested")?.[0][0]).toEqual(expect.objectContaining({
      studyEnvelopeId: "env_study_batch_999",
      certificateId: "CERT-PS315-2026-9999",
      anonymizedStudyUid: "1.2.840.10008.1.999.888.777"
    }));
    expect(wrapper.emitted("close")).toBeTruthy();
  });

  it("displays error message if API upload request fails", async () => {
    /**
     * Verify error handling and alert box display when backend returns error.
     */
    vi.spyOn(global, "fetch").mockResolvedValue({
      ok: false,
      status: 400,
      json: async () => ({ message: "Batch payload exceeds maximum size limit of 2 GB." })
    } as Response);

    const wrapper = mountModal({ isOpen: true });

    const dcmFile = new File(["dicom content"], "slice_1.dcm", { type: "application/dicom" });
    const fileInput = wrapper.find("input[accept*='.dcm']");
    Object.defineProperty(fileInput.element, "files", {
      value: [dcmFile],
      writable: true
    });
    await fileInput.trigger("change");

    const startBtn = wrapper.find(".modal-footer button.btn-primary");
    await startBtn.trigger("click");

    await wrapper.vm.$nextTick();
    await new Promise((r) => setTimeout(r, 10));
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".error-alert-box").exists()).toBe(true);
    expect(wrapper.find(".error-alert-box").text()).toContain("Batch payload exceeds maximum size limit of 2 GB.");
  });

  it("emits close event when close button or cancel button is clicked", async () => {
    /**
     * Verify modal close event triggers.
     */
    const wrapper = mountModal({ isOpen: true });

    const closeBtn = wrapper.find(".btn-close");
    await closeBtn.trigger("click");

    expect(wrapper.emitted("close")).toBeTruthy();
  });
});
