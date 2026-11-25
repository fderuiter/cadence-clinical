import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import {
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalDropzone,
  ClinicalFieldLayout,
  ClinicalFormField,
  ClinicalIconButton,
  ClinicalInput,
  ClinicalModal,
  ClinicalQueryFlag,
  ClinicalToastHost,
  EmptyState,
  MediaPlayer,
  createSrOnlyActionButton,
} from "../index.js";

async function mountSetupSSR(Component, props = {}, emit = vi.fn()) {
  let state;
  const Harness = {
    setup() {
      state = Component.setup(props, {
        emit,
        attrs: {},
        slots: {},
        expose: () => {},
      });
      return () => h(Component, props);
    },
  };
  const html = await renderToString(createSSRApp(Harness));
  return { state, html };
}

describe("Shared UI Vue Components Behavior & SSR Rendering", () => {
  describe("ClinicalDataTable", () => {
    const columns = [
      { key: "id", label: "Subject ID", sortable: true },
      { key: "lab", label: "Glucose (mg/dL)", sortable: true },
      { key: "notes", label: "Notes", sortable: false },
    ];
    const rows = [
      { id: "SUBJ-003", lab: 112, notes: "Fasting" },
      { id: "SUBJ-001", lab: 89, notes: "Baseline" },
      { id: "SUBJ-002", lab: 104, notes: "Follow-up" },
    ];

    it("renders table headers, rows, pagination, and empty state via SSR", async () => {
      const html = await renderToString(
        createSSRApp(ClinicalDataTable, {
          columns,
          data: rows,
          pageSize: 2,
          selectable: true,
          title: "Lab Results",
        })
      );
      expect(html).toContain("Lab Results");
      expect(html).toContain("3 total record(s)");
      expect(html).toContain("Page 1 of 2");
      expect(html).toContain("SUBJ-003");
      expect(html).toContain('data-label="Subject ID"');
      expect(html).toContain('data-label="Glucose (mg/dL)"');

      const emptyHtml = await renderToString(
        createSSRApp(ClinicalDataTable, {
          columns,
          data: [],
        })
      );
      expect(emptyHtml).toContain("No clinical records found.");
    });

    it("sorts ascending/descending, paginates, and emits row selection", async () => {
      const emit = vi.fn();
      const { state } = await mountSetupSSR(
        ClinicalDataTable,
        {
          columns,
          data: rows,
          pageSize: 2,
          selectable: true,
          title: "Dataset",
        },
        emit
      );

      state.toggleSort(columns[2]);
      expect(state.sortKey.value).toBe("");

      state.toggleSort(columns[1]);
      expect(state.sortKey.value).toBe("lab");
      expect(state.sortOrder.value).toBe("asc");
      expect(state.sortedData.value.map((r) => r.lab)).toEqual([89, 104, 112]);
      expect(state.totalPages.value).toBe(2);
      expect(state.paginatedData.value).toHaveLength(2);

      state.toggleSort(columns[1]);
      expect(state.sortOrder.value).toBe("desc");
      expect(state.sortedData.value.map((r) => r.lab)).toEqual([112, 104, 89]);

      expect(state.isRowSelected(rows[0])).toBe(false);
      state.toggleRowSelection(rows[0]);
      expect(state.isRowSelected(rows[0])).toBe(true);
      expect(emit).toHaveBeenCalledWith("select", ["SUBJ-003"]);

      state.toggleRowSelection(rows[0]);
      expect(state.isRowSelected(rows[0])).toBe(false);
      expect(emit).toHaveBeenLastCalledWith("select", []);
    });
  });

  describe("MediaPlayer", () => {
    let origWindow;

    beforeEach(() => {
      origWindow = globalThis.window;
      globalThis.window = {
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      };
    });

    afterEach(() => {
      globalThis.window = origWindow;
    });

    it("categorizes PDF, image, audio, video, and unsupported artifacts and renders watermark", async () => {
      const imgHtml = await renderToString(
        createSSRApp(MediaPlayer, {
          src: "/files/lesion-scan.png",
          mimeType: "image/png",
          title: "Lesion Scan",
          isWatermarked: true,
          watermarkText: "BLINDED REVIEW",
        })
      );
      expect(imgHtml).toContain("IMAGE");
      expect(imgHtml).toContain("Lesion Scan");
      expect(imgHtml).toContain("BLINDED REVIEW");

      const { state: pdfState } = await mountSetupSSR(MediaPlayer, {
        src: "/files/protocol.pdf",
        mimeType: "application/pdf",
      });
      expect(pdfState.mediaCategory.value).toBe("pdf");

      const { state: audioState } = await mountSetupSSR(MediaPlayer, {
        src: "/files/cough.wav",
        mimeType: "audio/wav",
      });
      expect(audioState.mediaCategory.value).toBe("audio");

      const { state: videoState } = await mountSetupSSR(MediaPlayer, {
        src: "/files/gait.mp4",
        mimeType: "video/mp4",
      });
      expect(videoState.mediaCategory.value).toBe("video");

      const { state: unsupportedState } = await mountSetupSSR(MediaPlayer, {
        src: "/files/archive.bin",
        mimeType: "application/octet-stream",
      });
      expect(unsupportedState.mediaCategory.value).toBe("unsupported");
    });

    it("handles image zoom, rotation, pan drag, audio/video playback, and PDF pagination", async () => {
      const emit = vi.fn();
      const { state: imgState } = await mountSetupSSR(
        MediaPlayer,
        {
          src: "/files/xray.jpg",
          mimeType: "image/jpeg",
          initialZoom: 1,
        },
        emit
      );

      imgState.zoomIn();
      expect(imgState.zoomLevel.value).toBe(1.25);
      expect(emit).toHaveBeenCalledWith("zoom-change", 1.25);

      imgState.zoomOut();
      expect(imgState.zoomLevel.value).toBe(1);

      imgState.rotateClockwise();
      expect(imgState.rotation.value).toBe(90);

      imgState.handleMouseDown({ clientX: 100, clientY: 80 });
      expect(imgState.isDragging.value).toBe(true);
      imgState.handleMouseMove({ clientX: 130, clientY: 110 });
      expect(imgState.panOffset.value).toEqual({ x: 30, y: 30 });
      imgState.handleMouseUp();
      expect(imgState.isDragging.value).toBe(false);

      imgState.resetTransform();
      expect(imgState.rotation.value).toBe(0);
      expect(imgState.panOffset.value).toEqual({ x: 0, y: 0 });

      const mediaEmit = vi.fn();
      const { state: videoState } = await mountSetupSSR(
        MediaPlayer,
        {
          src: "/files/telehealth.mp4",
          mimeType: "video/mp4",
        },
        mediaEmit
      );
      const mockVideoEl = {
        play: vi.fn(),
        pause: vi.fn(),
        currentTime: 0,
        playbackRate: 1,
        muted: false,
      };
      videoState.videoRef.value = mockVideoEl;

      videoState.togglePlayPause();
      expect(mockVideoEl.play).toHaveBeenCalled();
      expect(videoState.isPlaying.value).toBe(true);
      expect(mediaEmit).toHaveBeenCalledWith("play");

      videoState.togglePlayPause();
      expect(mockVideoEl.pause).toHaveBeenCalled();
      expect(videoState.isPlaying.value).toBe(false);
      expect(mediaEmit).toHaveBeenCalledWith("pause");

      videoState.onLoadedMetadata({ target: { duration: 125 } });
      expect(videoState.duration.value).toBe(125);
      expect(videoState.formatTime(125)).toBe("02:05");
      expect(videoState.formatTime(0)).toBe("00:00");

      videoState.seekMedia({ target: { value: "42.5" } });
      expect(mockVideoEl.currentTime).toBe(42.5);

      videoState.setPlaybackRate(1.5);
      expect(mockVideoEl.playbackRate).toBe(1.5);

      videoState.toggleMute();
      expect(mockVideoEl.muted).toBe(true);

      videoState.onEnded();
      expect(mediaEmit).toHaveBeenCalledWith("ended");

      videoState.pdfTotalPages.value = 3;
      videoState.nextPdfPage();
      expect(videoState.pdfPage.value).toBe(2);
      videoState.prevPdfPage();
      expect(videoState.pdfPage.value).toBe(1);
    });

    it("converts PDF data URIs to blob: URLs for iframe rendering without emitting data: frame sources", async () => {
      const createSpy = vi
        .spyOn(URL, "createObjectURL")
        .mockReturnValue("blob:https://cadence.example/pdf-preview-1");
      const revokeSpy = vi
        .spyOn(URL, "revokeObjectURL")
        .mockImplementation(() => {});

      const pdfDataUri = "data:application/pdf;base64,JVBERi0xLjQK";
      const html = await renderToString(
        createSSRApp(MediaPlayer, {
          src: pdfDataUri,
          mimeType: "application/pdf",
          title: "Protocol Synopsis PDF",
        })
      );

      expect(createSpy).toHaveBeenCalledTimes(1);
      expect(html).toContain(
        'src="blob:https://cadence.example/pdf-preview-1"'
      );
      expect(html).not.toContain("data:application/pdf");

      const passthroughHtml = await renderToString(
        createSSRApp(MediaPlayer, {
          src: "blob:https://cadence.example/existing-pdf",
          mimeType: "application/pdf",
        })
      );
      expect(passthroughHtml).toContain(
        'src="blob:https://cadence.example/existing-pdf"'
      );

      createSpy.mockRestore();
      revokeSpy.mockRestore();
    });
  });

  describe("EmptyState & ClinicalFieldLayout", () => {
    it("renders EmptyState title, message, action button, and custom slots", async () => {
      const html = await renderToString(
        createSSRApp(EmptyState, {
          icon: "🧪",
          title: "No Samples Queued",
          message: "All laboratory aliquots have been reconciled.",
          actionText: "Add Sample",
        })
      );
      expect(html).toContain("🧪");
      expect(html).toContain("No Samples Queued");
      expect(html).toContain("All laboratory aliquots have been reconciled.");
      expect(html).toContain("Add Sample");
    });

    it("renders ClinicalFieldLayout label/legend, validation error status, and query flag", async () => {
      const divHtml = await renderToString(
        createSSRApp(
          ClinicalFieldLayout,
          {
            id: "sysbp",
            label: "Systolic Blood Pressure",
            error: "Value exceeds physiological range",
            gridSpan: 6,
          },
          {
            default: () => h("input", { id: "sysbp", type: "text" }),
          }
        )
      );
      expect(divHtml).toContain("Systolic Blood Pressure");
      expect(divHtml).toContain("Value exceeds physiological range");
      expect(divHtml).toContain('role="alert"');

      const fieldsetHtml = await renderToString(
        createSSRApp(ClinicalFieldLayout, {
          id: "sex",
          label: "Biological Sex",
          tag: "fieldset",
        })
      );
      expect(fieldsetHtml).toContain("<fieldset");
      expect(fieldsetHtml).toContain("<legend");
    });

    it("supports compact staff density, required markers, hints, and select/textarea rendering in ClinicalFieldLayout and ClinicalInput (ADR-2673)", async () => {
      const defaultInputHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "portal-given-name",
          label: "Given Name",
          modelValue: "Alex",
          required: true,
          hint: "Matches government ID",
        })
      );
      expect(defaultInputHtml).toContain('class="clinical-input grid-span-12"');
      expect(defaultInputHtml).not.toContain("clinical-input--compact");
      expect(defaultInputHtml).toContain('class="clinical-field-required"');
      expect(defaultInputHtml).toContain('id="field-hint-portal-given-name"');
      expect(defaultInputHtml).toContain(
        'aria-describedby="field-hint-portal-given-name"'
      );

      const compactSelectHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "staff-site-filter",
          label: "Clinical Site",
          as: "select",
          density: "compact",
          modelValue: "SITE-01",
          error: "Site scope required",
          hint: "Select active site",
          options: [
            { value: "", label: "All Sites" },
            { value: "SITE-01", label: "Site 01 - Boston" },
          ],
        })
      );
      expect(compactSelectHtml).toContain("clinical-input--compact");
      expect(compactSelectHtml).toContain("<select");
      expect(compactSelectHtml).toContain("Site 01 - Boston");
      expect(compactSelectHtml).toContain('aria-invalid="true"');
      expect(compactSelectHtml).toContain(
        'aria-describedby="field-hint-staff-site-filter validation-error-staff-site-filter"'
      );

      const textareaEmit = vi.fn();
      const { state: textareaState, html: textareaHtml } = await mountSetupSSR(
        ClinicalInput,
        {
          id: "staff-reason-note",
          label: "Reason for Change",
          as: "textarea",
          rows: 4,
          density: "compact",
          modelValue: "Initial note",
          disabled: false,
          readonly: false,
        },
        textareaEmit
      );
      expect(textareaHtml).toContain("<textarea");
      expect(textareaHtml).toContain('rows="4"');
      const inputEvent = { target: { value: "Updated justification" } };
      textareaState.onInput(inputEvent);
      expect(textareaEmit).toHaveBeenCalledWith(
        "update:modelValue",
        "Updated justification"
      );
      expect(textareaEmit).toHaveBeenCalledWith(
        "input",
        "Updated justification",
        inputEvent
      );
      const selectTarget = { value: "SITE-02" };
      textareaState.onSelectChange({ target: selectTarget });
      expect(textareaEmit).toHaveBeenCalledWith(
        "change",
        "SITE-02",
        selectTarget
      );

      const routedAttrHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "lock-obs-picker",
          label: "Observation",
          hideLabel: true,
          labelClass: "form-label",
          required: true,
          as: "select",
          density: "compact",
          modelValue: "OBS-1",
          options: [{ value: "OBS-1", label: "Systolic BP" }],
          attributes: {
            "data-cdash": "LOCKOBS",
            "data-testid": "lock-picker-observation",
            "aria-label": "Observation to lock",
          },
        })
      );
      expect(routedAttrHtml).toContain('data-cdash="LOCKOBS"');
      expect(routedAttrHtml).toContain('id="lock-obs-picker"');
      expect(routedAttrHtml).toContain('class="clinical-input-control"');
      expect(routedAttrHtml).toContain('data-testid="lock-picker-observation"');
      expect(routedAttrHtml).toContain('aria-label="Observation to lock"');
      expect(routedAttrHtml).toContain(
        'class="clinical-field-required text-danger"'
      );
    });
  });

  describe("ClinicalModal & ClinicalDrawer", () => {
    it("renders ClinicalModal with modelValue/open/isOpen aliases, overlayId, ARIA attributes, and locks dismissal when dismissible=false", async () => {
      const html = await renderToString(
        createSSRApp({
          render: () =>
            h(
              ClinicalModal,
              {
                open: true,
                teleport: false,
                overlayId: "enroll-subject-modal",
                title: "Enroll Subject",
                ariaDescribedby: "enroll-subject-description",
                showFooter: false,
              },
              {
                default: () =>
                  h(
                    "p",
                    { id: "enroll-subject-description" },
                    "Enrollment payload"
                  ),
              }
            ),
        })
      );
      expect(html).toContain('id="enroll-subject-modal"');
      expect(html).toContain('role="dialog"');
      expect(html).toContain('aria-modal="true"');
      expect(html).toContain('aria-describedby="enroll-subject-description"');
      expect(html).toContain("Enroll Subject");
      expect(html).toContain("Enrollment payload");
      expect(html).not.toContain("modal-footer");
      expect(html).toContain("modal-close-btn");

      const headerOnlyHtml = await renderToString(
        createSSRApp({
          render: () =>
            h(
              ClinicalModal,
              {
                isOpen: true,
                teleport: false,
                title: "Reason Required",
                showClose: false,
              },
              {
                default: () => h("p", "Document reason"),
              }
            ),
        })
      );
      expect(headerOnlyHtml).toContain("Reason Required");
      expect(headerOnlyHtml).not.toContain("modal-close-btn");

      const emit = vi.fn();
      const { state } = await mountSetupSSR(
        ClinicalModal,
        {
          isOpen: true,
          teleport: false,
          dismissible: false,
          ariaLabel: "Locked Signing Dialog",
        },
        emit
      );
      expect(state.modalOpen.value).toBe(true);
      state.closeModal();
      expect(emit).not.toHaveBeenCalled();

      const unlockedEmit = vi.fn();
      const { state: unlockedState } = await mountSetupSSR(
        ClinicalModal,
        {
          isOpen: true,
          teleport: false,
          dismissible: true,
          ariaLabel: "Unlocked Dialog",
        },
        unlockedEmit
      );
      unlockedState.closeModal();
      expect(unlockedEmit).toHaveBeenCalledWith("update:modelValue", false);
      expect(unlockedEmit).toHaveBeenCalledWith("update:isOpen", false);
      expect(unlockedEmit).toHaveBeenCalledWith("update:open", false);
      expect(unlockedEmit).toHaveBeenCalledWith("close");
    });

    it("renders ClinicalDrawer inline with accessible dialog role and locks dismissal when dismissible=false", async () => {
      const html = await renderToString(
        createSSRApp({
          render: () =>
            h(
              ClinicalDrawer,
              {
                isOpen: true,
                teleport: false,
                overlayId: "metadata-drawer-overlay",
                title: "Document Metadata",
              },
              {
                default: () => h("div", "DIA Taxonomy"),
              }
            ),
        })
      );
      expect(html).toContain('id="metadata-drawer-overlay"');
      expect(html).toContain('role="dialog"');
      expect(html).toContain('aria-modal="true"');
      expect(html).toContain("Document Metadata");
      expect(html).toContain("DIA Taxonomy");

      const emit = vi.fn();
      const { state } = await mountSetupSSR(
        ClinicalDrawer,
        {
          isOpen: true,
          teleport: false,
          dismissible: false,
          title: "QC Review Locked",
        },
        emit
      );
      state.closeDrawer();
      expect(emit).not.toHaveBeenCalled();

      const closedKeepMountedHtml = await renderToString(
        createSSRApp({
          render: () =>
            h(
              ClinicalDrawer,
              {
                isOpen: false,
                keepMounted: true,
                teleport: false,
                title: "Persistent Readability Drawer",
              },
              {
                default: () => h("div", "Cached readability state"),
                footer: () => h("button", { type: "button" }, "Apply"),
              }
            ),
        })
      );
      expect(closedKeepMountedHtml).toContain("Cached readability state");
      expect(closedKeepMountedHtml).toContain('aria-hidden="true"');
      expect(closedKeepMountedHtml).not.toContain('aria-modal="true"');
      expect(closedKeepMountedHtml).toContain("clinical-drawer-footer");
      expect(closedKeepMountedHtml).toContain("<svg");
      expect(closedKeepMountedHtml).not.toContain("clinical-drawer-subheader");
    });
  });

  describe("ClinicalIconButton, ClinicalDropzone, & Label-in-Name Helpers (ADR-2822)", () => {
    it("renders ClinicalIconButton with required aria-label, aria-hidden glyph wrapper, and disabled click guard", async () => {
      expect(ClinicalIconButton.props.label.validator("Remove row")).toBe(true);
      expect(ClinicalIconButton.props.label.validator("   ")).toBe(false);

      const html = await renderToString(
        createSSRApp({
          render: () =>
            h(
              ClinicalIconButton,
              {
                label: "Remove study arm 1",
                variant: "danger",
                size: "sm",
              },
              { default: () => "🗑️" }
            ),
        })
      );
      expect(html).toContain('aria-label="Remove study arm 1"');
      expect(html).toContain('class="clinical-icon-btn-glyph"');
      expect(html).toContain('aria-hidden="true"');

      const emit = vi.fn();
      const { state } = await mountSetupSSR(
        ClinicalIconButton,
        { label: "Close drawer", disabled: false },
        emit
      );
      const clickEvt = { preventDefault: vi.fn() };
      state.handleClick(clickEvt);
      expect(emit).toHaveBeenCalledWith("click", clickEvt);

      const disabledEmit = vi.fn();
      const { state: disabledState } = await mountSetupSSR(
        ClinicalIconButton,
        { label: "Locked action", disabled: true },
        disabledEmit
      );
      const blockedEvt = { preventDefault: vi.fn() };
      disabledState.handleClick(blockedEvt);
      expect(blockedEvt.preventDefault).toHaveBeenCalled();
      expect(disabledEmit).not.toHaveBeenCalled();
    });

    it("renders ClinicalDropzone with native browse button, non-hidden status slot, sibling remove button, and drag/input handlers", async () => {
      const html = await renderToString(
        createSSRApp({
          render: () =>
            h(
              ClinicalDropzone,
              {
                inputId: "test-upload-input",
                hasSelection: true,
                removable: true,
                buttonLabel: "Choose Receipt File",
                buttonContext: "for travel reimbursement",
                removeLabel: "Remove file",
                removeContext: "receipt.pdf",
                describedBy: "upload-hint-id",
              },
              {
                selected: () =>
                  h("span", { id: "selected-file" }, "receipt.pdf (42 KB)"),
              }
            ),
        })
      );
      expect(html).not.toContain('role="button"');
      expect(html).toContain('id="test-upload-input"');
      expect(html).toContain('type="button"');
      expect(html).toContain("Choose Receipt File");
      expect(html).toContain("for travel reimbursement");
      expect(html).toContain('role="status"');
      expect(html).toContain("receipt.pdf (42 KB)");
      expect(html).toContain("Remove file");

      const emit = vi.fn();
      const { state } = await mountSetupSSR(
        ClinicalDropzone,
        {
          inputId: "drop-input",
          hasSelection: true,
          removable: true,
        },
        emit
      );
      const mockFile = { name: "protocol.pdf", size: 1024 };
      state.onDragEnter({ preventDefault: vi.fn() });
      expect(state.isDragging.value).toBe(true);
      state.onDragLeave({ preventDefault: vi.fn() });
      expect(state.isDragging.value).toBe(false);
      state.onDrop({
        preventDefault: vi.fn(),
        dataTransfer: { files: [mockFile] },
      });
      expect(emit).toHaveBeenCalledWith("files-selected", [mockFile]);
      expect(emit).toHaveBeenCalledWith("file-selected", mockFile);

      const inputEvt = {
        target: { files: [mockFile], value: "C:\\fakepath\\protocol.pdf" },
      };
      state.onInputChange(inputEvt);
      expect(inputEvt.target.value).toBe("");
      expect(emit).toHaveBeenCalledWith("change", inputEvt);

      const focusedBrowse = vi.fn();
      const browseBtn = {
        tagName: "BUTTON",
        focus: focusedBrowse,
        hasAttribute: () => false,
        getAttribute: () => null,
        getClientRects: () => [{ width: 80 }],
      };
      vi.stubGlobal("document", {
        body: {},
        activeElement: null,
        contains: () => true,
        querySelector: () => null,
      });
      try {
        state.browseButtonRef.value = browseBtn;
        state.onRemove();
        expect(emit).toHaveBeenCalledWith("remove");
        await new Promise((resolve) => setTimeout(resolve, 0));
        expect(focusedBrowse).toHaveBeenCalled();
      } finally {
        vi.unstubAllGlobals();
      }
    });

    it("includes fieldLabel in ClinicalQueryFlag and escapes HTML in createSrOnlyActionButton", async () => {
      const queryHtml = await renderToString(
        createSSRApp(ClinicalQueryFlag, {
          id: "VS.SYSBP",
          query: { status: "OPEN" },
          fieldLabel: "Systolic Blood Pressure",
        })
      );
      expect(queryHtml).toContain(
        'aria-label="Query status for Systolic Blood Pressure: OPEN"'
      );
      expect(queryHtml).toContain('aria-hidden="true"');

      const btnHtml = createSrOnlyActionButton({
        label: "Start Survey",
        srContext: "Daily Health & Vital <Diary>",
        className: "btn-primary btn-start-task",
        dataId: "assign_01",
      });
      expect(btnHtml).toBe(
        '<button type="button" class="btn-primary btn-start-task" data-id="assign_01">Start Survey<span class="sr-only">: Daily Health &amp; Vital &lt;Diary&gt;</span></button>'
      );
    });

    it("renders ClinicalToastHost with split alert/status live roles, v-if title guard, persistent errors, and hover/focus timer pause/resume", async () => {
      vi.useFakeTimers();
      try {
        const toasts = [
          {
            id: 1,
            tone: "error",
            title: "Submission rejected",
            message: "Missing investigator signature",
            timeoutMs: 3500,
          },
          {
            id: 2,
            tone: "info",
            title: "",
            message: "Draft saved locally",
            timeoutMs: 2000,
            action: { label: "Open form", to: "/ecrf" },
          },
        ];

        const html = await renderToString(
          createSSRApp({
            render: () =>
              h(ClinicalToastHost, {
                toasts,
                latestToastId: "toast-container",
              }),
          })
        );

        expect(html).toContain('data-focus-trap-exempt="true"');
        expect(html).toContain('role="region"');
        expect(html).toContain('role="alert"');
        expect(html).toContain('aria-live="assertive"');
        expect(html).toContain('role="status"');
        expect(html).toContain('aria-live="polite"');
        expect(html).toContain('id="toast-container"');
        // Error toast renders <strong class="toast-title">, untitled info toast omits empty <strong>
        const titleMatches = html.match(/class="toast-title"/g) || [];
        expect(titleMatches).toHaveLength(1);
        expect(html).toContain("Open form");

        const emit = vi.fn();
        const { state } = await mountSetupSSR(
          ClinicalToastHost,
          { toasts, manageTimers: true },
          emit
        );

        // Advance past 3500ms while hovered: error toast (id 1) persists, timed info toast (id 2) pauses
        vi.advanceTimersByTime(1200);
        state.onMouseEnter(2);
        expect(emit).toHaveBeenCalledWith("pause", 2);
        vi.advanceTimersByTime(5000);
        expect(emit).not.toHaveBeenCalledWith("dismiss", 1);
        expect(emit).not.toHaveBeenCalledWith("dismiss", 2);

        // Resume hover: remaining ~800ms elapses and dismisses id 2 while id 1 stays open
        state.onMouseLeave(2);
        expect(emit).toHaveBeenCalledWith("resume", 2);
        vi.advanceTimersByTime(700);
        expect(emit).not.toHaveBeenCalledWith("dismiss", 2);
        vi.advanceTimersByTime(200);
        expect(emit).toHaveBeenCalledWith("dismiss", 2);
        expect(emit).not.toHaveBeenCalledWith("dismiss", 1);

        state.handleActionClick(state.normalizedToasts.value[1]);
        expect(emit).toHaveBeenCalledWith("action", toasts[1]);

        state.handleDismissClick(1);
        expect(emit).toHaveBeenCalledWith("dismiss", 1);
      } finally {
        vi.useRealTimers();
      }
    });
  });

  describe("ClinicalInput & ClinicalFormField Tabular Numeral Propagation (ADR-2670)", () => {
    it("resolves ClinicalInput type, inputmode, data-tabular-nums opt-in/opt-out, error aria attributes, and input/change events", async () => {
      const textHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "AE.TERM",
          label: "Adverse Event Term",
          modelValue: "Mild headache",
        })
      );
      expect(textHtml).toContain('type="text"');
      expect(textHtml).not.toContain("data-tabular-nums");

      const numericHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "VS.HR",
          label: "Heart Rate (bpm)",
          type: "numeric",
          modelValue: 72,
          error: "Out of reference range",
        })
      );
      expect(numericHtml).toContain('type="number"');
      expect(numericHtml).toContain('inputmode="numeric"');
      expect(numericHtml).toContain('data-tabular-nums="true"');
      expect(numericHtml).toContain('aria-invalid="true"');

      const decimalHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "VS.TEMP",
          label: "Temperature",
          inputmode: "decimal",
          modelValue: "36.8",
        })
      );
      expect(decimalHtml).toContain('inputmode="decimal"');
      expect(decimalHtml).toContain('data-tabular-nums="true"');

      const explicitOptInHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "LB.CUSTOM",
          label: "Formatted Titer",
          tabularNums: true,
          modelValue: "1:128",
        })
      );
      expect(explicitOptInHtml).toContain('data-tabular-nums="true"');

      const attrEmptyStringHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "LB.ATTR",
          label: "Attribute Opt-In",
          attributes: { "data-tabular-nums": "", "data-cdash": "--ORRES" },
          modelValue: "4.5",
        })
      );
      expect(attrEmptyStringHtml).toContain('data-tabular-nums="true"');
      expect(attrEmptyStringHtml).toContain('data-cdash="--ORRES"');

      const explicitOptOutHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "SUBJ.NOTES_NUM",
          label: "Opted-Out Numeric",
          type: "number",
          tabularNums: false,
          modelValue: 1,
        })
      );
      expect(explicitOptOutHtml).not.toContain("data-tabular-nums");

      const attrOptOutHtml = await renderToString(
        createSSRApp(ClinicalInput, {
          id: "SUBJ.ATTR_OFF",
          label: "Attr Opted-Out",
          attributes: { type: "date", "data-tabular-nums": "false" },
          modelValue: "2026-11-22",
        })
      );
      expect(attrOptOutHtml).toContain('type="date"');
      expect(attrOptOutHtml).not.toContain('data-tabular-nums="true"');
    });

    it("propagates data-tabular-nums from ClinicalFormField for numeric/date CDASH fields while leaving text fields proportional", async () => {
      const numberFieldHtml = await renderToString(
        createSSRApp(ClinicalFormField, {
          field: {
            id: "VS.SYSBP",
            label: "Systolic Blood Pressure",
            type: "number",
            cdash: "--ORRES",
          },
          modelValue: 120,
        })
      );
      expect(numberFieldHtml).toContain('data-cdash="--ORRES"');
      expect(numberFieldHtml).toContain('data-tabular-nums="true"');

      const textFieldHtml = await renderToString(
        createSSRApp(ClinicalFormField, {
          field: {
            id: "CM.NOTE",
            label: "Concomitant Medication Notes",
            type: "text",
          },
          modelValue: "Taken with meals",
        })
      );
      expect(textFieldHtml).not.toContain("data-tabular-nums");

      const explicitFieldHtml = await renderToString(
        createSSRApp(ClinicalFormField, {
          field: {
            id: "LB.RATIO",
            label: "Dilution Ratio",
            type: "text",
            tabularNums: true,
          },
          modelValue: "1:64",
        })
      );
      expect(explicitFieldHtml).toContain('data-tabular-nums="true"');
    });
  });
});
