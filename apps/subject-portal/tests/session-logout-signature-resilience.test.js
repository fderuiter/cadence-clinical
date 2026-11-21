import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createApp, nextTick } from "vue";
import ParticipantCard from "../src/components/ParticipantCard.vue";
import ReconsentModal from "../src/components/ReconsentModal.vue";
import SignatureModal from "../src/components/SignatureModal.vue";
import { pinia } from "../src/stores/pinia.js";
import {
  dispatchApi,
  login,
  logout,
  openReconsentModal,
  openSignatureModal,
  portalSessionCoordinator,
  state,
  submitReconsentSignature,
  verifyAndSubmitSignature,
} from "../src/index.js";

function mountComponent(Component) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const app = createApp(Component);
  app.use(pinia);
  app.mount(host);
  return {
    host,
    query: (selector) => host.querySelector(selector),
    unmount() {
      app.unmount();
      host.remove();
    },
  };
}

describe("Subject Portal OIDC Session Resilience, Draft-Safe Logout & Part 11 Modals", () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    window.__MOCK_TEST_ENV__ = true;
    sessionStorage.clear();
    localStorage.clear();
    portalSessionCoordinator.reset();
    delete window.keycloakInstance;

    state.session.userId = "subject.site101";
    state.session.token = "valid-access-token";
    state.session.isDemoMode = false;
    state.session.isOfflineMode = false;
    state.session.sessionExpired = false;
    state.authError = "";
    state.activeQuestionnaire = null;
    state.draftPresence = {};
    state.reconsentModalOpen = false;
    state.reconsentModalError = "";
    state.signatureModalOpen = false;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    delete window.keycloakInstance;
    vi.restoreAllMocks();
  });

  it("attempts a single-flight refresh on HTTP 401 and retries the request once with the rotated token", async () => {
    const keycloak = {
      authenticated: true,
      token: "stale-token",
      refreshToken: "valid-refresh-token",
      updateToken: vi.fn(async (minValidity) => {
        if (minValidity < 0) {
          keycloak.token = "rotated-fresh-token";
          return true;
        }
        return false;
      }),
    };
    window.keycloakInstance = keycloak;
    portalSessionCoordinator.attachKeycloak(keycloak);
    state.session.token = "stale-token";

    const seenAuthHeaders = [];
    globalThis.fetch = vi.fn(async (_url, opts) => {
      const auth = opts?.headers?.Authorization || "";
      seenAuthHeaders.push(auth);
      if (auth === "Bearer stale-token") {
        return {
          ok: false,
          status: 401,
          json: async () => ({ detail: "Token expired" }),
        };
      }
      return {
        ok: true,
        status: 200,
        json: async () => [{ id: "assign-1" }],
      };
    });

    const data = await dispatchApi("assignments/subject/subject.site101");
    expect(data).toEqual([{ id: "assign-1" }]);
    expect(keycloak.updateToken).toHaveBeenCalledWith(-1);
    expect(seenAuthHeaders).toEqual([
      "Bearer stale-token",
      "Bearer rotated-fresh-token",
    ]);
    expect(state.session.token).toBe("rotated-fresh-token");
    expect(state.session.sessionExpired).toBe(false);
  });

  it("transitions ParticipantCard out of Authenticated on unrecoverable 401 while preserving active questionnaire drafts and passing explicit redirectUri on login", async () => {
    window.history.replaceState(
      null,
      "",
      "/subject-portal/?study=CADENCE-101#view-questionnaire"
    );
    const loginSpy = vi.fn().mockResolvedValue(undefined);
    const keycloak = {
      authenticated: true,
      token: "expired-token",
      refreshToken: "expired-refresh-token",
      updateToken: vi.fn().mockRejectedValue(new Error("Session expired")),
      login: loginSpy,
    };
    window.keycloakInstance = keycloak;
    portalSessionCoordinator.attachKeycloak(keycloak);

    state.activeQuestionnaire = {
      assignment: { id: "assign-q1" },
      instrument: {
        id: "inst-q1",
        items: { pain_score: { type: "text", label: "Pain Score" } },
      },
      answers: { pain_score: "7" },
    };
    state.draftPresence = { "assign-q1": true };

    globalThis.fetch = vi.fn(async () => ({
      ok: false,
      status: 401,
      json: async () => ({ detail: "Bearer expired" }),
    }));

    const card = mountComponent(ParticipantCard);
    await nextTick();
    expect(card.query("#session-auth-status")?.textContent?.trim()).toBe(
      "Authenticated"
    );

    await expect(
      dispatchApi("assignments/subject/subject.site101")
    ).rejects.toThrow();
    await nextTick();

    expect(state.session.token).toBeNull();
    expect(state.session.sessionExpired).toBe(true);
    expect(state.activeQuestionnaire?.answers).toEqual({ pain_score: "7" });
    expect(state.draftPresence).toEqual({ "assign-q1": true });
    expect(card.query("#session-auth-status")?.textContent?.trim()).toBe(
      "Session Expired"
    );
    expect(card.query("#btn-login")?.textContent?.trim()).toBe(
      "Sign In to Continue"
    );

    await login();
    expect(loginSpy).toHaveBeenCalledTimes(1);
    expect(loginSpy).toHaveBeenCalledWith({
      redirectUri: expect.stringContaining(
        "/subject-portal/?study=CADENCE-101#view-questionnaire"
      ),
    });
    card.unmount();
  });

  it("flushes active questionnaire drafts and requires confirmation in ParticipantCard before Logout resets activeQuestionnaire", async () => {
    state.activeQuestionnaire = {
      assignment: { id: "assign-draft-1" },
      instrument: {
        id: "inst-draft-1",
        items: { symptom_note: { type: "text", label: "Symptom Note" } },
      },
      answers: { symptom_note: "Mild headache" },
    };
    state.draftPresence = { "assign-draft-1": true };

    const card = mountComponent(ParticipantCard);
    await nextTick();

    card.query("#btn-logout").click();
    await vi.waitFor(() => {
      expect(
        card.query('[data-testid="signout-confirm-modal"]')
      ).not.toBeNull();
    });
    expect(state.activeQuestionnaire).not.toBeNull();

    card.query('[data-testid="signout-stay-btn"]').click();
    await nextTick();
    expect(card.query('[data-testid="signout-confirm-modal"]')).toBeNull();
    expect(state.activeQuestionnaire?.answers).toEqual({
      symptom_note: "Mild headache",
    });
    expect(state.session.token).toBe("valid-access-token");

    card.query("#btn-logout").click();
    await vi.waitFor(() => {
      expect(card.query('[data-testid="signout-confirm-btn"]')).not.toBeNull();
    });
    card.query('[data-testid="signout-confirm-btn"]').click();
    await vi.waitFor(() => {
      expect(state.activeQuestionnaire).toBeNull();
      expect(state.session.token).toBeNull();
    });
    card.unmount();
  });

  it("uses alphanumeric password inputs and rejects blank custom reason when 'Other' is selected in SignatureModal and ReconsentModal", async () => {
    const sigWrapper = mountComponent(SignatureModal);
    await nextTick();

    const sigPwd = sigWrapper.query("#sign-password");
    expect(sigPwd.getAttribute("type")).toBe("password");
    expect(sigPwd.getAttribute("inputmode")).toBe("text");
    expect(sigPwd.getAttribute("autocomplete")).toBe("current-password");

    openSignatureModal("consent");
    const reasonEl = document.getElementById("sign-reason");
    reasonEl.value = "Other";
    reasonEl.dispatchEvent(new Event("change"));
    await nextTick();

    expect(
      sigWrapper.query("#sign-reason-custom").getAttribute("aria-required")
    ).toBe("true");
    document.getElementById("sign-username").value = "subject.site101";
    document.getElementById("sign-password").value = "Alphanumeric!Pass9"; // pragma: allowlist secret
    document.getElementById("sign-reason-custom").value = "   ";

    globalThis.fetch = vi.fn();
    await verifyAndSubmitSignature();
    expect(globalThis.fetch).not.toHaveBeenCalled();
    expect(document.getElementById("modal-error-banner").textContent).toMatch(
      /custom signing reason/i
    );
    sigWrapper.unmount();

    openReconsentModal();
    const reconsentWrapper = mountComponent(ReconsentModal);
    await nextTick();

    const reconsentPwd = reconsentWrapper.query("#reconsent-sign-password");
    expect(reconsentPwd.getAttribute("type")).toBe("password");
    expect(reconsentPwd.getAttribute("inputmode")).toBe("text");
    expect(reconsentPwd.getAttribute("autocomplete")).toBe("current-password");

    state.reconsentForm.username = "subject.site101";
    state.reconsentForm.password = "Alphanumeric!Pass9"; // pragma: allowlist secret
    state.reconsentForm.reason = "Other";
    state.reconsentForm.customReason = "   ";
    await nextTick();

    expect(
      reconsentWrapper
        .query("#reconsent-sign-reason-custom")
        .getAttribute("aria-required")
    ).toBe("true");

    await submitReconsentSignature();
    expect(globalThis.fetch).not.toHaveBeenCalled();
    expect(state.reconsentModalError).toMatch(/custom signing reason/i);
    reconsentWrapper.unmount();
  });

  it("clears reactive session identity and PIN modal state synchronously on logout before async cleanup settles", async () => {
    let resolveKeycloakLogout;
    window.keycloakInstance = {
      logout: vi.fn(
        () =>
          new Promise((resolve) => {
            resolveKeycloakLogout = resolve;
          })
      ),
    };
    state.pinUnlock.isOpen = true;
    state.activeQuestionnaire = {
      assignment: { id: "assign-sync-1" },
      instrument: { id: "inst-sync-1", items: {} },
      answers: {},
    };

    const logoutPromise = logout();

    expect(state.session.userId).toBeNull();
    expect(state.session.token).toBeNull();
    expect(state.pinUnlock.isOpen).toBe(false);
    expect(state.activeQuestionnaire).toBeNull();

    await vi.waitFor(() => {
      expect(window.keycloakInstance.logout).toHaveBeenCalledTimes(1);
    });
    resolveKeycloakLogout();
    await logoutPromise;
  });
});
