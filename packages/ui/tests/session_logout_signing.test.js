import { beforeEach, describe, expect, it, vi } from "vitest";
import { createSSRApp, effectScope, h } from "vue";
import { renderToString } from "vue/server-renderer";
import {
  DEFAULT_AUTH_RETURN_URL_KEY,
  SEMANTIC_ACTIONS,
  SEMANTIC_ACTION_PATTERN,
  SIGNATURE_OTHER_REASON,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  STEP_UP_ACTION_MAX_LENGTH,
  SignOutConfirmModal,
  buildStepUpVerificationPayload,
  clearAuthReturnUrl,
  consumeAuthReturnUrl,
  createOidcSessionCoordinator,
  debounce,
  deriveSignerRoleFromClaims,
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  hasRegisteredUnsavedDrafts,
  isStepUpAuthFailure,
  isValidSemanticAction,
  isValidStepUpAction,
  peekAuthReturnUrl,
  registerDraftSource,
  requiresCustomSignatureReason,
  resetDraftSourcesForTests,
  resolveOidcRedirectUri,
  resolveSignatureReason,
  sanitizeReturnUrl,
  saveAuthReturnUrl,
  useSignOutGuard,
} from "../index.js";

function createMemoryStorage() {
  const map = new Map();
  return {
    getItem: (key) => (map.has(key) ? map.get(key) : null),
    setItem: (key, value) => map.set(key, String(value)),
    removeItem: (key) => map.delete(key),
    dump: () => Object.fromEntries(map.entries()),
  };
}

describe("Shared OIDC session coordinator (session.js)", () => {
  it("sanitizes internal return URLs and rejects external or login URLs", () => {
    expect(
      sanitizeReturnUrl("/study/CADENCE-101?stage=conduct#ecrf", {
        baseOrigin: "http://localhost:5173",
      })
    ).toBe("/study/CADENCE-101?stage=conduct#ecrf");

    expect(
      sanitizeReturnUrl(
        "http://localhost:5174/subject-portal/?tab=tasks#view-questionnaire",
        { baseOrigin: "http://localhost:5174" }
      )
    ).toBe("/subject-portal/?tab=tasks#view-questionnaire");

    expect(
      sanitizeReturnUrl("https://evil.example/phish", {
        baseOrigin: "http://localhost:5173",
      })
    ).toBeNull();
    expect(sanitizeReturnUrl("//evil.example/path")).toBeNull();
    expect(sanitizeReturnUrl("relative/no-leading-slash")).toBeNull();
    expect(sanitizeReturnUrl("/login?redirect=/study")).toBeNull();
    expect(sanitizeReturnUrl("   ")).toBeNull();
    expect(sanitizeReturnUrl(null)).toBeNull();
  });

  it("saves, peeks, consumes, and clears return URLs without storing tokens", () => {
    const storage = createMemoryStorage();
    expect(
      saveAuthReturnUrl("/ctms?tab=doa", {
        storage,
        baseOrigin: "http://localhost:5173",
      })
    ).toBe("/ctms?tab=doa");

    expect(storage.dump()[DEFAULT_AUTH_RETURN_URL_KEY]).toBe("/ctms?tab=doa");
    expect(peekAuthReturnUrl({ storage })).toBe("/ctms?tab=doa");
    expect(consumeAuthReturnUrl({ storage })).toBe("/ctms?tab=doa");
    expect(peekAuthReturnUrl({ storage })).toBeNull();

    saveAuthReturnUrl("/ecrf?formId=f-1", { storage });
    clearAuthReturnUrl({ storage });
    expect(peekAuthReturnUrl({ storage })).toBeNull();
  });

  it("builds explicit OIDC redirect URIs under the current origin and configured subpath", () => {
    expect(
      resolveOidcRedirectUri("/subject-portal/#view-questionnaire", {
        origin: "http://localhost:5174",
        basePath: "/subject-portal/",
      })
    ).toBe("http://localhost:5174/subject-portal/#view-questionnaire");

    expect(
      resolveOidcRedirectUri("/", {
        origin: "http://localhost:5173",
        basePath: "/cadence-clinical/",
      })
    ).toBe("http://localhost:5173/cadence-clinical/");

    expect(
      resolveOidcRedirectUri("/studies/CADENCE-101?stage=conduct", {
        origin: "http://localhost:5173",
        basePath: "/cadence-clinical/",
      })
    ).toBe(
      "http://localhost:5173/cadence-clinical/studies/CADENCE-101?stage=conduct"
    );

    expect(
      resolveOidcRedirectUri("/compliance", {
        origin: "https://fderuiter.github.io",
        basePath: "/cadence-clinical/subject-portal/",
      })
    ).toBe(
      "https://fderuiter.github.io/cadence-clinical/subject-portal/compliance"
    );

    expect(
      resolveOidcRedirectUri("https://attacker.invalid/steal", {
        origin: "http://localhost:5174",
        basePath: "/subject-portal/",
      })
    ).toBe("http://localhost:5174/subject-portal/");
  });

  it("distinguishes step-up 401 errors from expired OIDC bearer tokens", () => {
    expect(
      isStepUpAuthFailure("/api/v1/auth/signature-verification", 401, null)
    ).toBe(true);
    expect(
      isStepUpAuthFailure("/api/v1/execution/forms/123/signatures", 401, {
        code: "REAUTHENTICATION_REQUIRED",
      })
    ).toBe(true);
    expect(
      isStepUpAuthFailure("/api/v1/ctms/doa/sign-off", 401, {
        detail: "Missing or expired X-Sig-Token header.",
      })
    ).toBe(true);
    expect(
      isStepUpAuthFailure("/api/v1/kernel/studies", 401, {
        title: "Unauthorized",
      })
    ).toBe(false);
    expect(
      isStepUpAuthFailure("/api/v1/auth/signature-verification", 400, null)
    ).toBe(false);
  });

  it("deduplicates concurrent updateToken calls and wires onTokenExpired + onAuthRefreshSuccess", async () => {
    const storage = createMemoryStorage();
    const refreshedTokens = [];
    const expiredEvents = [];

    let resolveRefresh;
    const keycloak = {
      authenticated: true,
      token: "initial-jwt",
      refreshToken: "refresh-jwt",
      updateToken: vi.fn(
        () =>
          new Promise((resolve) => {
            resolveRefresh = () => {
              keycloak.token = "rotated-jwt";
              resolve(true);
            };
          })
      ),
    };

    const coordinator = createOidcSessionCoordinator({
      storage,
      getCurrentReturnUrl: () => "/study/CADENCE-101?section=forms",
      onTokenRefreshed: (tok) => refreshedTokens.push(tok),
      onSessionExpired: (ctx) => expiredEvents.push(ctx),
    });
    coordinator.attachKeycloak(keycloak);

    expect(typeof keycloak.onTokenExpired).toBe("function");
    expect(typeof keycloak.onAuthRefreshSuccess).toBe("function");

    const p1 = coordinator.ensureFreshToken(30);
    const p2 = coordinator.ensureFreshToken(30);
    const p3 = keycloak.onTokenExpired();
    expect(coordinator.isRefreshing()).toBe(true);
    expect(keycloak.updateToken).toHaveBeenCalledTimes(1);

    resolveRefresh();
    const [r1, r2, r3] = await Promise.all([p1, p2, p3]);

    expect(r1).toEqual({ ok: true, refreshed: true, token: "rotated-jwt" });
    expect(r2).toEqual(r1);
    expect(r3).toEqual(r1);
    expect(coordinator.isRefreshing()).toBe(false);
    expect(refreshedTokens).toContain("rotated-jwt");
    expect(expiredEvents).toHaveLength(0);

    keycloak.token = "hook-jwt";
    keycloak.onAuthRefreshSuccess();
    expect(refreshedTokens.at(-1)).toBe("hook-jwt");
  });

  it("handles failed single-flight 401 refresh by scrubbing tokens and saving return URL", async () => {
    const storage = createMemoryStorage();
    const expiredEvents = [];
    const keycloak = {
      authenticated: true,
      token: "stale-jwt",
      refreshToken: "expired-refresh",
      idToken: "id-jwt",
      updateToken: vi
        .fn()
        .mockRejectedValue(new Error("refresh_grant_expired")),
    };

    const coordinator = createOidcSessionCoordinator({
      storage,
      getCurrentReturnUrl: () => "/subject-portal/#view-questionnaire",
      onSessionExpired: (ctx) => expiredEvents.push(ctx),
    });
    coordinator.attachKeycloak(keycloak);

    const [a, b] = await Promise.all([
      coordinator.refreshOnUnauthorized(),
      coordinator.refreshOnUnauthorized(),
    ]);

    expect(keycloak.updateToken).toHaveBeenCalledTimes(1);
    expect(keycloak.updateToken).toHaveBeenCalledWith(-1);
    expect(a.ok).toBe(false);
    expect(b.ok).toBe(false);
    expect(keycloak.token).toBeUndefined();
    expect(keycloak.refreshToken).toBeUndefined();
    expect(keycloak.authenticated).toBe(false);
    expect(coordinator.isSessionExpired()).toBe(true);
    expect(expiredEvents).toHaveLength(1);
    expect(expiredEvents[0].returnUrl).toBe(
      "/subject-portal/#view-questionnaire"
    );
    expect(coordinator.peekReturnUrl()).toBe(
      "/subject-portal/#view-questionnaire"
    );

    coordinator.beginVoluntaryLogout();
    expect(coordinator.isSessionExpired()).toBe(false);
    expect(coordinator.peekReturnUrl()).toBeNull();
  });
});

describe("Debounce flush/cancel and shared sign-out guard/modal", () => {
  beforeEach(() => {
    resetDraftSourcesForTests();
  });

  it("flushes or cancels pending debounced calls synchronously", () => {
    vi.useFakeTimers();
    try {
      const spy = vi.fn((val) => `saved:${val}`);
      const debouncedSave = debounce(spy, 400);

      expect(debouncedSave.pending()).toBe(false);
      expect(debouncedSave.flush()).toBeUndefined();

      debouncedSave("draft-1");
      debouncedSave("draft-2");
      expect(debouncedSave.pending()).toBe(true);
      expect(spy).not.toHaveBeenCalled();

      const flushedResult = debouncedSave.flush();
      expect(flushedResult).toBe("saved:draft-2");
      expect(spy).toHaveBeenCalledTimes(1);
      expect(debouncedSave.pending()).toBe(false);

      debouncedSave("draft-3");
      debouncedSave.cancel();
      vi.advanceTimersByTime(500);
      expect(spy).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it("flushes registered draft sources, prompts when dirty, and auto-disposes with Vue scope", async () => {
    let flushedBuffer = "";
    let pendingBuffer = "systolic=128";
    const onConfirmSignOut = vi.fn();

    const scope = effectScope();
    scope.run(() => {
      registerDraftSource({
        flush: () => {
          if (pendingBuffer) {
            flushedBuffer = pendingBuffer;
          }
        },
        hasDrafts: () => Boolean(flushedBuffer),
      });
    });

    const guard = useSignOutGuard({ onConfirmSignOut });
    const outcome = await guard.requestSignOut();

    expect(flushedBuffer).toBe("systolic=128");
    expect(outcome).toEqual({ prompted: true, signedOut: false });
    expect(guard.isConfirmOpen.value).toBe(true);
    expect(onConfirmSignOut).not.toHaveBeenCalled();

    guard.cancelSignOut();
    expect(guard.isConfirmOpen.value).toBe(false);
    expect(onConfirmSignOut).not.toHaveBeenCalled();

    await guard.requestSignOut();
    await guard.confirmSignOut();
    expect(guard.isConfirmOpen.value).toBe(false);
    expect(onConfirmSignOut).toHaveBeenCalledTimes(1);

    scope.stop();
    expect(hasRegisteredUnsavedDrafts()).toBe(false);
    await flushRegisteredDraftSources();
    flushRegisteredDraftSourcesSync();

    const cleanOutcome = await guard.requestSignOut();
    expect(cleanOutcome).toEqual({ prompted: false, signedOut: true });
    expect(onConfirmSignOut).toHaveBeenCalledTimes(2);

    let syncFlushed = false;
    const unregisterSync = registerDraftSource({
      flush: () => {
        syncFlushed = true;
      },
      hasDrafts: () => syncFlushed,
    });
    flushRegisteredDraftSourcesSync();
    expect(syncFlushed).toBe(true);
    unregisterSync();
  });

  it("renders SignOutConfirmModal with accessible alertdialog markup and action controls", async () => {
    const app = createSSRApp({
      render: () =>
        h(SignOutConfirmModal, {
          open: true,
          title: "Unsaved eCRF Changes",
          description: "Your eCRF draft will be cleared if you sign out.",
        }),
    });
    const html = await renderToString(app);

    expect(html).toContain('role="alertdialog"');
    expect(html).toContain('aria-modal="true"');
    expect(html).toContain("Unsaved eCRF Changes");
    expect(html).toContain('data-testid="signout-stay-btn"');
    expect(html).toContain('data-testid="signout-confirm-btn"');
  });
});

describe("Shared Part 11 step-up verification & credential helpers (signing.js)", () => {
  it("validates step-up action paths and semantic_action v3 grammar", () => {
    expect(STEP_UP_ACTION_MAX_LENGTH).toBe(512);
    for (const value of Object.values(SEMANTIC_ACTIONS)) {
      expect(SEMANTIC_ACTION_PATTERN.test(value)).toBe(true);
    }

    expect(isValidStepUpAction("/api/v1/ctms/doa/sign-off")).toBe(true);
    expect(isValidStepUpAction("/api/v2/ctms/doa/sign-off")).toBe(false);
    expect(isValidStepUpAction("/api/v1/ctms/../admin")).toBe(false);
    expect(isValidStepUpAction("/api/v1/ctms/doa/sign off")).toBe(false);
    expect(isValidStepUpAction(`/api/v1/${"a".repeat(520)}`)).toBe(false);
    expect(isValidStepUpAction("")).toBe(false);
    expect(isValidStepUpAction(null)).toBe(false);

    expect(isValidSemanticAction("ctms.doa.sign_off")).toBe(true);
    expect(isValidSemanticAction("econsent.consent.capture")).toBe(true);
    expect(isValidSemanticAction("collection.sign")).toBe(true);
    expect(isValidSemanticAction("CTMS.doa.sign_off")).toBe(false);
    expect(isValidSemanticAction("single_segment")).toBe(false);
    expect(isValidSemanticAction("a.b.c.d.e.f")).toBe(false);
    expect(isValidSemanticAction(undefined)).toBe(false);
  });

  it("builds valid v3 step-up payloads and rejects invalid fields", () => {
    const built = buildStepUpVerificationPayload({
      username: " pi.site101 ",
      password: "AlphanumericPassword!42", // pragma: allowlist secret
      totp: " 654321 ",
      action: "/api/v1/ctms/doa/sign-off",
      semanticAction: SEMANTIC_ACTIONS.CTMS_DOA_SIGN_OFF,
      batchId: " batch-77 ",
    });

    expect(built).toEqual({
      username: "pi.site101",
      password: "AlphanumericPassword!42", // pragma: allowlist secret
      totp: "654321",
      action: "/api/v1/ctms/doa/sign-off",
      semantic_action: "ctms.doa.sign_off",
      batch_id: "batch-77",
    });

    expect(() =>
      buildStepUpVerificationPayload({
        password: "",
        action: "/api/v1/ctms/doa/sign-off",
        semanticAction: "ctms.doa.sign_off",
      })
    ).toThrow(/password/i);

    expect(() =>
      buildStepUpVerificationPayload({
        password: "secret", // pragma: allowlist secret
        action: "/ctms/doa/sign-off",
        semanticAction: "ctms.doa.sign_off",
      })
    ).toThrow(/action path/i);

    expect(() =>
      buildStepUpVerificationPayload({
        password: "secret", // pragma: allowlist secret
        action: "/api/v1/ctms/doa/sign-off",
        semanticAction: "",
      })
    ).toThrow(/semantic_action/i);
  });

  it("exports alphanumeric password input attributes and enforces Other custom reason", () => {
    expect(SIGNATURE_PASSWORD_INPUT_ATTRS).toEqual({
      type: "password",
      inputmode: "text",
      autocomplete: "current-password",
      autocapitalize: "off",
      autocorrect: "off",
      spellcheck: "false",
    });
    expect(SIGNATURE_PASSWORD_INPUT_ATTRS.inputmode).not.toBe("numeric");

    expect(requiresCustomSignatureReason(SIGNATURE_OTHER_REASON)).toBe(true);
    expect(requiresCustomSignatureReason("I consent to participate")).toBe(
      false
    );

    expect(resolveSignatureReason("", "")).toEqual({
      valid: false,
      reason: "",
      error: "Please select a signing reason.",
    });
    expect(resolveSignatureReason("Other", "   ")).toEqual({
      valid: false,
      reason: "",
      error: "Please specify a custom signing reason when 'Other' is selected.",
    });
    expect(
      resolveSignatureReason("Other", "  Signed with bilingual witness  ")
    ).toEqual({
      valid: true,
      reason: "Signed with bilingual witness",
      error: null,
    });
    expect(
      resolveSignatureReason("I have read and agree to participate", "")
    ).toEqual({
      valid: true,
      reason: "I have read and agree to participate",
      error: null,
    });
  });

  it("derives default signer roles from OIDC claims instead of defaulting clinicians to Subject", () => {
    expect(deriveSignerRoleFromClaims(["pi"])).toBe("PI");
    expect(
      deriveSignerRoleFromClaims({
        username: "pi.site101",
        roles: ["site_investigator"],
      })
    ).toBe("PI");
    expect(deriveSignerRoleFromClaims(["site_investigator"])).toBe(
      "Investigator"
    );
    expect(deriveSignerRoleFromClaims("crc")).toBe("Site CRC");
    expect(
      deriveSignerRoleFromClaims({
        realm_access: { roles: ["Sponsor Designer"] },
      })
    ).toBe("Sponsor Designer");
    expect(deriveSignerRoleFromClaims(["data_manager"])).toBe("Data Manager");
    expect(deriveSignerRoleFromClaims(["auditor"])).toBe("Auditor");
    expect(deriveSignerRoleFromClaims(["Subject"])).toBe("Subject");
    expect(deriveSignerRoleFromClaims(null)).toBe("Subject");
  });
});
