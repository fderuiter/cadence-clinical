import "fake-indexeddb/auto";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  CADENCE_THEME_KEY,
  THEME_APPEARANCE_KEY,
  initSurfaceThemeSync,
  setSurfaceTheme,
  toggleSurfaceTheme,
} from "ui";

const SHA256_HEX_PATTERN = /\b[a-f0-9]{64}\b/i;

describe("Participant Surface Density, Shared Primitives & Plain Copy Contract", () => {
  beforeEach(async () => {
    window.__MOCK_TEST_ENV__ = true;
    localStorage.clear();
    document.documentElement.className = "";
    document.documentElement.removeAttribute("data-theme");
    document.documentElement.removeAttribute("data-density");
    document.head.innerHTML = `<link rel="manifest" href="/subject-portal/manifest.json" />`;
    document.body.innerHTML = `<div id="app"></div>`;

    globalThis.fetch = vi.fn().mockImplementation(() =>
      Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ status: "success" }),
      })
    );

    const portal = await import("../src/index.js");
    portal.state.session.userId = "SUBJ-101-001";
    portal.state.session.roles = "Subject";
    portal.state.session.token = null;
    portal.state.session.isOfflineMode = true;
    portal.state.session.isDemoMode = true;
    portal.state.assignments = [];
    portal.state.instruments = {};
    portal.state.activeQuestionnaire = null;
    portal.state.ledgerBlocks = [];
    portal.state.consentLanguage = "en";
    portal.state.consentSigned = false;
    portal.state.consentPassed = false;
  });

  it("mounts ClinicalModal, ClinicalDrawer, ClinicalInput, ClinicalRadioGroup, and ClinicalSlider with participant density and zero raw overlays", async () => {
    const portal = await import("../src/index.js");
    await portal.initializeApp();

    expect(document.documentElement.getAttribute("data-density")).toBe(
      "participant"
    );
    expect(document.querySelectorAll(".modal-overlay")).toHaveLength(0);
    expect(document.querySelectorAll(".global-drawer")).toHaveLength(0);

    // SignatureModal mounts ClinicalModal in keepMounted mode
    expect(
      document.querySelector("#portal-sign-modal.clinical-modal-dialog")
    ).not.toBeNull();

    // Open SyncDrawer -> mounts ClinicalDrawer
    portal.state.syncDrawerOpen = true;
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(
      document.querySelector("#global-sync-drawer.clinical-drawer-panel")
    ).not.toBeNull();
    portal.state.syncDrawerOpen = false;

    // Start questionnaire -> mounts ClinicalInput, ClinicalRadioGroup, and ClinicalSlider declaratively
    await portal.startQuestionnaire("assign_01");
    const formContainer = document.getElementById(
      "questionnaire-form-container"
    );
    expect(formContainer.querySelector(".clinical-input")).not.toBeNull();
    expect(formContainer.querySelector(".clinical-radio-grid")).not.toBeNull();
    expect(formContainer.querySelector(".clinical-slider")).not.toBeNull();
  });

  it("omits internal study/site/subject codes, protocol v1.0, raw 64-char SHA-256 hashes, and clinical jargon across consent, session, support, audit, and survey screens", async () => {
    const portal = await import("../src/index.js");
    await portal.initializeApp();

    // 1. Session & Site Support (ParticipantCard)
    const participantCardText =
      document.querySelector(".participant-info-card")?.textContent || "";
    expect(participantCardText).toContain("Study Participant");
    expect(participantCardText).not.toMatch(/SUBJ-/i);
    expect(participantCardText).not.toMatch(/SITE-/i);
    expect(participantCardText).not.toContain("CADENCE-101");

    // 2. Survey screen (QuestionnaireView)
    await portal.startQuestionnaire("assign_01");
    const questionnaireText =
      document.getElementById("view-questionnaire")?.textContent || "";
    expect(questionnaireText).toContain("Your Survey Questions");
    expect(questionnaireText).not.toContain("Enter Clinical Findings");

    // Verify offline IndexedDB draft persistence still works seamlessly
    const sbpInput = document.getElementById("vssbp");
    sbpInput.value = "118";
    sbpInput.dispatchEvent(new Event("input", { bubbles: true }));
    await portal.saveAndLeaveQuestionnaire();

    await portal.startQuestionnaire("assign_01");
    await new Promise((resolve) => setTimeout(resolve, 30));
    expect(String(document.getElementById("vssbp").value)).toBe("118");

    // 3. Consent screen
    portal.showView("view-consent");
    await portal.loadConsentDetails();
    const consentText =
      document.getElementById("view-consent")?.textContent || "";
    expect(consentText).not.toContain("CADENCE-101");
    expect(consentText).not.toMatch(/\bv1\.0\b/i);
    expect(consentText).not.toMatch(/SUBJ-/i);
    expect(consentText).not.toMatch(/SITE-/i);

    // 4. Audit receipt footer
    const { logAuditRecord } = await import("../src/features/audit.js");
    await logAuditRecord(
      "EPRO_DIARY_SUBMITTED",
      {
        diary_id: "assign_01",
        subject_id: "SUBJ-101-001",
        site_id: "SITE-101",
      },
      "Survey signed and saved"
    );
    const ledgerText =
      document.getElementById("portal-ledger-timeline")?.textContent || "";
    expect(ledgerText).toContain("Saved securely on this device");
    expect(ledgerText).not.toMatch(SHA256_HEX_PATTERN);
    expect(ledgerText).not.toMatch(/SUBJ-/i);
    expect(ledgerText).not.toMatch(/SITE-/i);
  });

  it("synchronizes persisted light/dark mode across VitePress, staff /demo/, and participant /subject-portal/", () => {
    // 1. VitePress sets dark class on <html>
    const cleanupVp = initSurfaceThemeSync({ observeHtmlDarkClass: true });
    document.documentElement.classList.add("dark");
    return Promise.resolve().then(() => {
      expect(localStorage.getItem(THEME_APPEARANCE_KEY)).toBe("dark");
      expect(localStorage.getItem(CADENCE_THEME_KEY)).toBe("dark");
      cleanupVp();

      // 2. Navigating to /demo/ (staff density) inherits persisted dark theme
      document.documentElement.className = "";
      document.documentElement.removeAttribute("data-theme");
      const cleanupWeb = initSurfaceThemeSync({ density: "staff" });
      expect(document.documentElement.getAttribute("data-density")).toBe(
        "staff"
      );
      expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
      expect(document.documentElement.classList.contains("dark")).toBe(true);

      // Toggle to light inside /demo/
      toggleSurfaceTheme({ density: "staff" });
      expect(localStorage.getItem(THEME_APPEARANCE_KEY)).toBe("light");
      expect(localStorage.getItem(CADENCE_THEME_KEY)).toBe("light");
      cleanupWeb();

      // 3. Navigating to /subject-portal/ (participant density) inherits light theme, then switches to dark
      const cleanupPortal = initSurfaceThemeSync({ density: "participant" });
      expect(document.documentElement.getAttribute("data-density")).toBe(
        "participant"
      );
      expect(document.documentElement.getAttribute("data-theme")).toBe("light");
      setSurfaceTheme("dark", { density: "participant" });
      expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
      expect(localStorage.getItem(THEME_APPEARANCE_KEY)).toBe("dark");
      cleanupPortal();
    });
  });
});
