import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import {
  AiActionButton,
  AuditLogViewer,
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalDropzone,
  ClinicalFieldLayout,
  ClinicalIconButton,
  ClinicalModal,
  ClinicalQueryFlag,
  EmptyState,
  MediaPlayer,
  PersonaSwitcher,
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

  describe("AuditLogViewer", () => {
    const sampleLogs = [
      {
        id: "aud-1",
        timestamp: "2026-11-19T10:00:00Z",
        user_id: "crc_jones",
        action: "UPDATE",
        entity_type: "VitalSigns",
        entity_id: "vs-101",
        reason_for_change: "Corrected transcription typo",
        changes: [{ field: "sysbp", old_val: "120", new_val: "128" }],
      },
      {
        id: "aud-2",
        timestamp: "2026-11-19T10:05:00Z",
        user_id: "pi_smith",
        action: "SIGN",
        entity_type: "ConsentForm",
        entity_id: "icf-202",
        reason_for_change: "Investigator e-signature",
        changes: [],
      },
    ];

    it("renders 21 CFR Part 11 timeline entries and field diffs", async () => {
      const html = await renderToString(
        createSSRApp(AuditLogViewer, {
          logs: sampleLogs,
          title: "Subject Audit Trail",
        })
      );
      expect(html).toContain("Subject Audit Trail");
      expect(html).toContain("21 CFR Part 11 GxP Immutable");
      expect(html).toContain("Corrected transcription typo");
      expect(html).toContain("sysbp");
      expect(html).toContain("128");
    });

    it("filters audit entries by action type and search query", async () => {
      const { state } = await mountSetupSSR(AuditLogViewer, {
        logs: sampleLogs,
        title: "21 CFR Part 11 Audit Trail",
      });

      expect(state.filteredLogs.value).toHaveLength(2);

      state.selectedFilter.value = "SIGN";
      expect(state.filteredLogs.value).toHaveLength(1);
      expect(state.filteredLogs.value[0].id).toBe("aud-2");

      state.selectedFilter.value = "ALL";
      state.searchQuery.value = "transcription";
      expect(state.filteredLogs.value).toHaveLength(1);
      expect(state.filteredLogs.value[0].user_id).toBe("crc_jones");

      state.searchQuery.value = "nonexistent-token";
      expect(state.filteredLogs.value).toHaveLength(0);
    });
  });

  describe("PersonaSwitcher", () => {
    const defaultPersonas = [
      {
        id: "super_admin",
        label: "Super Administrator",
        role: "admin",
        badge: "Admin",
      },
      {
        id: "cra_monitor",
        label: "CRA Monitor",
        role: "cra_monitor",
        badge: "Monitor",
      },
    ];

    it("renders active persona badge and emits selection updates", async () => {
      const html = await renderToString(
        createSSRApp(PersonaSwitcher, {
          modelValue: "cra_monitor",
          personas: defaultPersonas,
        })
      );
      expect(html).toContain("CRA Monitor");
      expect(html).toContain("Monitor");
      expect(html).toContain('class="sr-only"');
      expect(html).toContain("Switch User Persona or Role");
      expect(html).not.toContain('aria-label="Switch User Persona / Role"');

      const emit = vi.fn();
      const { state } = await mountSetupSSR(
        PersonaSwitcher,
        {
          modelValue: "unknown_role",
          personas: defaultPersonas,
        },
        emit
      );
      expect(state.activePersona.value.id).toBe("super_admin");

      state.isOpen.value = true;
      state.selectPersona(defaultPersonas[1]);
      expect(emit).toHaveBeenCalledWith("update:modelValue", "cra_monitor");
      expect(emit).toHaveBeenCalledWith("change", defaultPersonas[1]);
      expect(state.isOpen.value).toBe(false);
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
      expect(divHtml).toContain('role="status"');

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
                showFooter: false,
              },
              {
                default: () => h("p", "Enrollment payload"),
              }
            ),
        })
      );
      expect(html).toContain('id="enroll-subject-modal"');
      expect(html).toContain('role="dialog"');
      expect(html).toContain('aria-modal="true"');
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

      state.onRemove();
      expect(emit).toHaveBeenCalledWith("remove");
    });

    it("keeps visible text first in AiActionButton, includes fieldLabel in ClinicalQueryFlag, and escapes HTML in createSrOnlyActionButton", async () => {
      const { state: aiState } = await mountSetupSSR(AiActionButton, {
        label: "Generate Draft",
        loadingText: "Synthesizing...",
        requiredRoles: ["study_designer"],
        userRoles: [],
        unauthorizedTooltip: "Requires Study Designer role",
      });
      expect(aiState.computedAriaLabel.value).toBe(
        "Generate Draft — Requires Study Designer role"
      );

      const { state: loadingAiState } = await mountSetupSSR(AiActionButton, {
        label: "Generate Draft",
        loading: true,
        loadingText: "Synthesizing...",
        requiredRoles: ["study_designer"],
        userRoles: ["study_designer"],
      });
      expect(loadingAiState.computedAriaLabel.value).toBe("Synthesizing...");

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
  });
});
