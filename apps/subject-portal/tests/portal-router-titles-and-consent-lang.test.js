import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import { initializeApp, unmountApp, state, showView } from "../src/index.js";
import {
  loadConsentDetails,
  resolveConsentLanguage,
} from "../src/features/consent.js";
import {
  PORTAL_BRAND_TITLE_SUFFIX,
  PORTAL_ROUTE_TITLES,
  formatPortalRouteAnnouncement,
  formatPortalViewAnnouncement,
  resolvePortalRouteTitle,
  resolvePortalViewTitle,
} from "../src/router/availability.js";
import { getPortalRouter } from "../src/router/index.js";

const settle = async () => {
  await nextTick();
  await new Promise((resolve) => setTimeout(resolve, 25));
  await nextTick();
};

const UX1_FORBIDDEN_ACRONYMS = /\b(EDC|CTMS|eTMF|RTSM|P21|QMS|Safety)\b/;

describe("Subject Portal Router Titles, Polite Announcements, and Localized Consent Lang Attributes", () => {
  beforeEach(async () => {
    window.__MOCK_TEST_ENV__ = true;
    vi.stubEnv("VITE_DEMO_MODE", "true");
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve([]),
    });
    document.documentElement.lang = "en";
    document.title = "";
    document.body.innerHTML = `<div id="app"></div>`;
    state.session.isDemoMode = true;
    state.session.isOfflineMode = true;
    state.session.userId = "subject_pii_secret_99";
    state.currentView = "view-tasks";
    state.consentLanguage = "en";
    await initializeApp();
    await settle();
  });

  afterEach(() => {
    unmountApp();
    vi.unstubAllEnvs();
  });

  it("maps all 8 portal routes to UX-1 compliant study-centric titles without participant PII", () => {
    const allRouteNames = [
      "tasks",
      "questionnaire",
      "compliance",
      "inbox",
      "consent",
      "wallet",
      "lay-summaries",
      "unavailable",
    ];

    for (const routeName of allRouteNames) {
      expect(PORTAL_ROUTE_TITLES[routeName]).toBeTruthy();
      const title = resolvePortalRouteTitle({
        name: routeName,
        query: {
          subjectId: "SUBJ-PII-999",
          participant: "Jane Doe",
          email: "jane@example.com",
        },
      });
      expect(title).toContain(`— ${PORTAL_BRAND_TITLE_SUFFIX}`);
      expect(title).not.toMatch(UX1_FORBIDDEN_ACRONYMS);
      expect(title).not.toContain("SUBJ-PII-999");
      expect(title).not.toContain("Jane Doe");
      expect(title).not.toContain("jane@example.com");
      expect(title).not.toContain("subject_pii_secret_99");

      const announcement = formatPortalRouteAnnouncement({
        name: routeName,
        query: { subjectId: "SUBJ-PII-999", participant: "Jane Doe" },
      });
      expect(announcement).toMatch(/^Navigated to /);
      expect(announcement).not.toMatch(UX1_FORBIDDEN_ACRONYMS);
      expect(announcement).not.toContain("SUBJ-PII-999");
      expect(announcement).not.toContain("Jane Doe");
    }

    expect(
      resolvePortalRouteTitle({
        name: "unavailable",
        query: { screen: "consent" },
      })
    ).toBe(
      "Informed Consent Review (Unavailable) — Cadence Participant Portal"
    );
    expect(resolvePortalViewTitle("view-wallet")).toBe(
      "Visit Stipends & Reimbursements — Cadence Participant Portal"
    );
    expect(formatPortalViewAnnouncement("view-lay-summaries")).toBe(
      "Navigated to Plain-Language Study Results"
    );
  });

  it("keeps #portal-route-announcer silent on initial load and announces subsequent view and router transitions politely", async () => {
    const announcer = document.getElementById("portal-route-announcer");
    expect(announcer).not.toBeNull();
    expect(announcer.getAttribute("role")).toBe("status");
    expect(announcer.getAttribute("aria-live")).toBe("polite");
    expect(announcer.getAttribute("aria-atomic")).toBe("true");

    // Initial load sets document.title without firing a redundant live announcement
    expect(document.title).toBe(
      "Study Tasks & Daily Diaries — Cadence Participant Portal"
    );
    expect(announcer.textContent.trim()).toBe("");

    // Imperative showView updates document.title and announces politely
    showView("view-compliance");
    await settle();
    expect(document.title).toBe(
      "Visit & Diary Completion — Cadence Participant Portal"
    );
    expect(announcer.textContent.trim()).toBe(
      "Navigated to Visit & Diary Completion"
    );

    // Router push across remaining routes updates document.title and live announcement
    const router = getPortalRouter();
    await router.push({ name: "inbox" });
    await settle();
    expect(document.title).toBe(
      "Study Coordinator Messages — Cadence Participant Portal"
    );
    expect(announcer.textContent.trim()).toBe(
      "Navigated to Study Coordinator Messages"
    );

    await router.push({ name: "wallet" });
    await settle();
    expect(document.title).toBe(
      "Visit Stipends & Reimbursements — Cadence Participant Portal"
    );
    expect(announcer.textContent.trim()).toBe(
      "Navigated to Visit Stipends & Reimbursements"
    );

    await router.push({ name: "lay-summaries" });
    await settle();
    expect(document.title).toBe(
      "Plain-Language Study Results — Cadence Participant Portal"
    );
    expect(announcer.textContent.trim()).toBe(
      "Navigated to Plain-Language Study Results"
    );

    await router.push({ name: "unavailable", query: { screen: "tasks" } });
    await settle();
    expect(document.title).toBe(
      "Study Tasks & Daily Diaries (Unavailable) — Cadence Participant Portal"
    );
    expect(announcer.textContent.trim()).toBe(
      "Navigated to Study Tasks & Daily Diaries (Unavailable)"
    );
  });

  it("applies localized BCP 47 lang attributes to #consent-template-title and #consent-clauses-container without mutating English surrounding chrome (WCAG 2.2 SC 3.1.2)", async () => {
    expect(resolveConsentLanguage("es")).toBe("es");
    expect(resolveConsentLanguage("NL")).toBe("nl");
    expect(resolveConsentLanguage("fr")).toBe("fr");
    expect(resolveConsentLanguage("unsupported")).toBe("en");

    showView("view-consent");
    await settle();

    const titleEl = document.getElementById("consent-template-title");
    const clausesEl = document.getElementById("consent-clauses-container");
    const langSelect = document.getElementById("consent-lang-selector");
    const englishStepHeading = document.querySelector(
      "#consent-comprehension-card h3"
    );
    const viewHeading = document.getElementById("consent-view-heading");

    expect(titleEl).not.toBeNull();
    expect(clausesEl).not.toBeNull();
    expect(langSelect).not.toBeNull();
    expect(englishStepHeading).not.toBeNull();
    expect(viewHeading).not.toBeNull();

    // Default English load
    expect(document.documentElement.lang).toBe("en");
    expect(titleEl.getAttribute("lang")).toBe("en");
    expect(clausesEl.getAttribute("lang")).toBe("en");

    // Switch to Spanish (es)
    langSelect.value = "es";
    langSelect.dispatchEvent(new Event("change", { bubbles: true }));
    await loadConsentDetails();
    await settle();

    expect(titleEl.getAttribute("lang")).toBe("es");
    expect(clausesEl.getAttribute("lang")).toBe("es");
    expect(titleEl.textContent).toContain("Consentimiento Informado");
    expect(clausesEl.textContent).toContain("Riesgos y Efectos Secundarios");
    expect(document.documentElement.lang).toBe("en");
    expect(englishStepHeading.hasAttribute("lang")).toBe(false);

    // Switch to Dutch (nl) and French (fr)
    for (const [code, expectedTitleSnippet] of [
      ["nl", "Geïnformeerde Toestemming"],
      ["fr", "Consentement Éclairé"],
      ["en", "Informed Consent"],
    ]) {
      langSelect.value = code;
      await loadConsentDetails();
      await settle();
      expect(titleEl.getAttribute("lang")).toBe(code);
      expect(clausesEl.getAttribute("lang")).toBe(code);
      expect(titleEl.textContent).toContain(expectedTitleSnippet);
      expect(document.documentElement.lang).toBe("en");
    }
  });
});
