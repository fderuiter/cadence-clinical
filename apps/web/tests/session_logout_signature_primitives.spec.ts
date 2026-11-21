import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { resetDraftSourcesForTests } from "ui";
import { apiClient } from "../src/api/apiClient";
import { createOpenApiClient, unwrapResponse } from "../src/api/client";
import { ecrfFormsApi, type EcrfForm } from "../src/api/ecrfFormsApi";
import { kernelApi } from "../src/api/kernelApi";
import { initKeycloakAuth } from "../src/auth/bootstrap";
import { setKeycloakClient } from "../src/auth/keycloak";
import {
  clearWebReturnUrl,
  peekWebReturnUrl,
  resolveWebRedirectUri,
  saveWebReturnUrl,
} from "../src/auth/sessionCoordinator";
import AppShell from "../src/components/AppShell.vue";
import SignatureCaptureModal from "../src/features/signatures/components/SignatureCaptureModal.vue";
import { useEcrfForm } from "../src/composables/useEcrfForm";
import { router } from "../src/router";
import { useAuthStore } from "../src/stores/auth";
import { hasUserScopedDrafts } from "../src/utils/userScopedStorage";
import CtmsView from "../src/views/CtmsView.vue";
import LoginView from "../src/views/LoginView.vue";
import StudyCanvasView from "../src/views/StudyCanvasView.vue";

function buildMockForm(formId = "form-vitals-1"): EcrfForm {
  return {
    form_id: formId,
    study_id: "CADENCE-101",
    enrollment_id: "enr-101",
    visit_id: "visit-screening",
    form_code: "VITALS",
    title: "Vital Signs",
    version: 1,
    content_hash: "sha256:abc123",
    locks: [],
    signatures: [],
    items: [
      {
        item_id: "item-hr",
        code: "HR",
        label: "Heart Rate",
        value_type: "quantity",
        unit: "bpm",
        required: true,
        revision: 1,
        value: null,
        queries: [],
      } as unknown as EcrfForm["items"][number],
    ],
  } as unknown as EcrfForm;
}

describe("Shared Session Resilience, Draft-Safe Sign-Out, and Part 11 Signing in Web", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(async () => {
    pinia = createPinia();
    setActivePinia(pinia);
    setKeycloakClient(null);
    clearWebReturnUrl();
    resetDraftSourcesForTests();
    window.localStorage.clear();
    window.sessionStorage.clear();
    document.body.innerHTML = "";
    await router.replace("/login");
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
    resetDraftSourcesForTests();
    setKeycloakClient(null);
  });

  it("deduplicates concurrent HTTP 401 refreshes across apiClient and openApiClient and retries once", async () => {
    let refreshResolver: (() => void) | null = null;
    const keycloak = {
      authenticated: true,
      token: "expired-jwt",
      refreshToken: "refresh-jwt",
      tokenParsed: {
        sub: "user-pi-1",
        preferred_username: "pi.site101",
        tenant_id: "cadence-local",
        realm_access: { roles: ["Site Investigator"] },
      },
      updateToken: vi.fn((minValidity: number) => {
        if (minValidity >= 0) {
          return Promise.resolve(false);
        }
        return new Promise<boolean>((resolve) => {
          refreshResolver = () => {
            keycloak.token = "rotated-fresh-jwt";
            resolve(true);
          };
        });
      }),
    };

    setKeycloakClient(keycloak);
    const authStore = useAuthStore();
    authStore.setAuth(keycloak);

    const seenAuthHeaders: string[] = [];
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async (input, init) => {
        const authHeader =
          init?.headers instanceof Headers
            ? init.headers.get("Authorization")
            : input instanceof Request
              ? input.headers.get("Authorization")
              : (init?.headers as Record<string, string> | undefined)
                  ?.Authorization;
        seenAuthHeaders.push(authHeader || "");

        if (authHeader === "Bearer expired-jwt") {
          return new Response(JSON.stringify({ detail: "Token expired" }), {
            status: 401,
            headers: { "Content-Type": "application/json" },
          });
        }
        return new Response(JSON.stringify({ ok: true }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      });

    const typedClient = createOpenApiClient<{
      "/api/v1/kernel/studies": {
        get: {
          responses: {
            200: { content: { "application/json": { ok: boolean } } };
          };
        };
      };
    }>("http://localhost:8000");

    const p1 = apiClient.get("/api/v1/notifications");
    const p2 = unwrapResponse(typedClient.GET("/api/v1/kernel/studies"));

    await vi.waitFor(() => {
      expect(refreshResolver).toBeTypeOf("function");
    });

    const forcedRefreshCalls = keycloak.updateToken.mock.calls.filter(
      ([validity]) => validity < 0
    );
    expect(forcedRefreshCalls).toHaveLength(1);

    refreshResolver!();
    const [r1, r2] = await Promise.all([p1, p2]);

    expect(r1).toEqual({ ok: true });
    expect(r2).toEqual({ ok: true });
    expect(authStore.accessToken).toBe("rotated-fresh-jwt");
    expect(
      seenAuthHeaders.filter((h) => h === "Bearer rotated-fresh-jwt")
    ).toHaveLength(2);

    fetchSpy.mockRestore();
  });

  it("expires session on unrecoverable HTTP 401 while preserving userScopedStorage drafts and ignoring step-up 401s", async () => {
    const onSessionEnded = vi.fn();
    class FakeKeycloak {
      authenticated = true;
      token = "stale-jwt";
      refreshToken = "stale-refresh";
      tokenParsed = {
        sub: "crc-sub-1",
        preferred_username: "crc.site101",
        tenant_id: "cadence-local",
        realm_access: { roles: ["CRC"] },
      };
      init = vi.fn().mockResolvedValue(true);
      updateToken = vi.fn((minValidity: number) => {
        if (minValidity >= 0) return Promise.resolve(false);
        return Promise.reject(new Error("Refresh session expired"));
      });
    }

    const authStore = useAuthStore();
    await initKeycloakAuth({
      authStore,
      Keycloak: FakeKeycloak as unknown as new (c: object) => unknown,
      env: { PROD: false },
      onSessionEnded,
    });

    window.history.replaceState({}, "", "/ecrf?form=vitals-1");
    const draftKey =
      "cadence_user:cadence-local:crc-sub-1:ecrf_form_draft:vitals-1";
    window.localStorage.setItem(
      draftKey,
      JSON.stringify({
        form_id: "vitals-1",
        items: { "item-hr": { text: "74", missing: "", revision: 1 } },
      })
    );

    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async (input) => {
        const url = input instanceof Request ? input.url : String(input);
        if (url.includes("/api/v1/auth/signature-verification")) {
          return new Response(
            JSON.stringify({
              code: "INVALID_CREDENTIALS",
              detail: "Invalid password",
            }),
            { status: 401, headers: { "Content-Type": "application/json" } }
          );
        }
        return new Response(JSON.stringify({ detail: "Bearer expired" }), {
          status: 401,
          headers: { "Content-Type": "application/json" },
        });
      });

    // Step-up 401 MUST NOT expire the clinician's OIDC session
    await expect(
      apiClient.post("/api/v1/auth/signature-verification", {
        username: "crc.site101",
        password: "wrong", // pragma: allowlist secret
        action: "/api/v1/etmf/documents/doc-1/sign-off",
        semantic_action: "etmf.document.sign_off",
      })
    ).rejects.toMatchObject({ status: 401 });
    expect(authStore.isAuthenticated).toBe(true);
    expect(onSessionEnded).not.toHaveBeenCalled();

    // Unrecoverable bearer 401 MUST expire session, preserve drafts, and save return URL
    await expect(apiClient.get("/api/v1/kernel/studies")).rejects.toMatchObject(
      { status: 401 }
    );
    expect(authStore.isAuthenticated).toBe(false);
    expect(authStore.sessionExpired).toBe(true);
    expect(onSessionEnded).toHaveBeenCalledTimes(1);
    expect(window.localStorage.getItem(draftKey)).not.toBeNull();
    expect(peekWebReturnUrl()).toBe("/ecrf?form=vitals-1");

    fetchSpy.mockRestore();
  });

  it("restores saved return URL during LoginView Keycloak redirect", async () => {
    const loginSpy = vi.fn().mockResolvedValue(undefined);
    setKeycloakClient({ login: loginSpy });
    saveWebReturnUrl("/ctms?siteId=SITE-101");

    const wrapper = mount(LoginView, {
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    await wrapper.find(".btn-login-keycloak").trigger("click");
    await flushPromises();

    expect(loginSpy).toHaveBeenCalledTimes(1);
    expect(loginSpy.mock.calls[0][0].redirectUri).toContain(
      "/ctms?siteId=SITE-101"
    );
    wrapper.unmount();
  });

  it("flushes debounced useEcrfForm drafts and prompts SignOutConfirmModal in AppShell and StudyCanvasView before purging storage", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    vi.spyOn(ecrfFormsApi, "getForm").mockResolvedValue(buildMockForm());
    vi.spyOn(kernelApi, "entitlements").mockResolvedValue(null as never);

    const authStore = useAuthStore();
    const logoutKeycloak = vi.fn().mockResolvedValue(undefined);
    const keycloak = {
      authenticated: true,
      token: "active-jwt",
      tokenParsed: {
        sub: "crc-sub-2",
        preferred_username: "crc.site101",
        tenant_id: "cadence-local",
        realm_access: { roles: ["CRC"] },
      },
      logout: logoutKeycloak,
      updateToken: vi.fn().mockResolvedValue(false),
    };
    setKeycloakClient(keycloak);
    authStore.setAuth(keycloak);

    const ecrf = useEcrfForm();
    await ecrf.load("form-vitals-1");
    ecrf.entries["item-hr"].text = "82";
    await flushPromises();

    const draftKey = ecrf.getDraftStorageKey("form-vitals-1")!;
    // Before 500ms debounce elapses, localStorage does not yet have the draft
    expect(window.localStorage.getItem(draftKey)).toBeNull();

    const shellWrapper = mount(AppShell, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });

    await shellWrapper.find("#btn-logout").trigger("click");
    await flushPromises();

    // Debounced edit was synchronously flushed before confirmation dialog opened!
    expect(window.localStorage.getItem(draftKey)).not.toBeNull();
    expect(hasUserScopedDrafts()).toBe(true);
    expect(
      shellWrapper.find('[data-testid="signout-confirm-modal"]').exists()
    ).toBe(true);
    expect(authStore.isAuthenticated).toBe(true);

    // Stay signed in preserves session and draft
    await shellWrapper
      .find('[data-testid="signout-stay-btn"]')
      .trigger("click");
    await flushPromises();
    expect(
      shellWrapper.find('[data-testid="signout-confirm-modal"]').exists()
    ).toBe(false);
    expect(authStore.isAuthenticated).toBe(true);
    expect(window.localStorage.getItem(draftKey)).not.toBeNull();
    shellWrapper.unmount();

    // Standalone StudyCanvasView also guards #btn-canvas-logout until confirmed
    const canvasWrapper = mount(StudyCanvasView, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await canvasWrapper.find("#btn-canvas-logout").trigger("click");
    await flushPromises();
    expect(
      canvasWrapper.find('[data-testid="signout-confirm-modal"]').exists()
    ).toBe(true);
    expect(authStore.isAuthenticated).toBe(true);

    await canvasWrapper
      .find('[data-testid="signout-confirm-btn"]')
      .trigger("click");
    await flushPromises();

    expect(authStore.isAuthenticated).toBe(false);
    expect(window.localStorage.getItem(draftKey)).toBeNull();
    expect(logoutKeycloak).toHaveBeenCalledTimes(1);
    canvasWrapper.unmount();
  });

  it("sends semantic_action ctms.doa.sign_off from CtmsView and scrubs credentials on submit and cancel", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const authStore = useAuthStore();
    authStore.setAuth({
      authenticated: true,
      token: "pi-token",
      tokenParsed: {
        sub: "pi-sub",
        preferred_username: "pi.site101",
        tenant_id: "cadence-local",
        realm_access: { roles: ["Site Investigator"] },
      },
    });

    const capturedBodies: Array<{ url: string; body: unknown }> = [];
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async (input, init) => {
        const url = input instanceof Request ? input.url : String(input);
        if (init?.body && typeof init.body === "string") {
          capturedBodies.push({ url, body: JSON.parse(init.body) });
        }
        if (url.includes("/api/v1/auth/signature-verification")) {
          return new Response(
            JSON.stringify({ sig_token: "doa-sig-token-1", expires_in: 300 }),
            { status: 200, headers: { "Content-Type": "application/json" } }
          );
        }
        return new Response(JSON.stringify([]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      });

    const wrapper = mount(CtmsView, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    const signedTokens: string[] = [];
    (
      wrapper.vm as unknown as {
        promptSignature: (cb: (tok: string, reason: string) => void) => void;
      }
    ).promptSignature((tok) => signedTokens.push(tok));
    await flushPromises();

    const pwdInput = wrapper.find("#sig-password");
    expect(pwdInput.attributes("inputmode")).toBe("text");
    await pwdInput.setValue("AlphaPass!99"); // pragma: allowlist secret
    await wrapper.find("#sig-totp").setValue("246810");
    await wrapper
      .find("#sig-reason-for-change")
      .setValue("PI review of delegation log");

    await wrapper
      .findAll(".modal-footer .btn-primary")
      .at(-1)!
      .trigger("click");
    await flushPromises();

    const stepUpCall = capturedBodies.find((entry) =>
      entry.url.includes("/api/v1/auth/signature-verification")
    );
    expect(stepUpCall?.body).toEqual({
      username: "pi.site101",
      password: "AlphaPass!99", // pragma: allowlist secret
      totp: "246810",
      action: "/api/v1/ctms/doa/sign-off",
      semantic_action: "ctms.doa.sign_off",
    });
    expect(signedTokens).toEqual(["doa-sig-token-1"]);
    expect((wrapper.vm as unknown as { sigPassword: string }).sigPassword).toBe(
      ""
    );
    expect((wrapper.vm as unknown as { sigTotp: string }).sigTotp).toBe("");

    fetchSpy.mockRestore();
    wrapper.unmount();
  });

  it("derives clinician signer role from OIDC claims and shows an inline password re-entry cue on Retry", async () => {
    const authStore = useAuthStore();
    authStore.setAuth({
      authenticated: true,
      token: "pi-token",
      tokenParsed: {
        sub: "pi-sub",
        preferred_username: "pi.site101",
        tenant_id: "cadence-local",
        realm_access: { roles: ["Site Investigator"] },
      },
    });

    const mockSignatureService = {
      verifySignature: vi
        .fn()
        .mockRejectedValueOnce(new Error("Invalid credentials"))
        .mockResolvedValueOnce({ sig_token: "fresh-sig-tok" }),
    };
    const mockOnSign = vi
      .fn()
      .mockResolvedValue({ kind: "signed", meaning: "approved" });

    const wrapper = mount(SignatureCaptureModal, {
      attachTo: document.body,
      props: {
        isOpen: true,
        username: "pi.site101",
        signerName: "Dr. Principal Investigator",
        actionUrl: "/api/v1/execution/forms/form-1/signatures",
        semanticAction: "collection.sign",
        onSign: mockOnSign,
      },
      global: {
        plugins: [pinia],
        provide: {
          signatureService: mockSignatureService,
        },
      },
    });

    // Default role derived from OIDC claims ("PI" rather than "Subject")
    expect((wrapper.find("#sig-role").element as HTMLSelectElement).value).toBe(
      "PI"
    );
    expect(wrapper.find("#sig-password").attributes("inputmode")).toBe("text");
    expect(
      wrapper.find('[data-testid="sig-password-reentry-cue"]').exists()
    ).toBe(false);

    await wrapper.find("#sig-password").setValue("WrongPassword!1"); // pragma: allowlist secret
    await wrapper.find("#sig-reason").setValue("Investigator Certification");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    // Password wiped immediately on submit; error alert and inline re-entry cue visible
    expect(wrapper.vm.password).toBe("");
    expect(wrapper.find("#sig-error-msg").text()).toContain(
      "Identity verification failed"
    );

    const retryBtn = wrapper.find('[data-testid="modal-error-retry-btn"]');
    await retryBtn.trigger("click");
    await flushPromises();

    expect(document.activeElement?.id).toBe("sig-password");
    const cue = wrapper.find('[data-testid="sig-password-reentry-cue"]');
    expect(cue.exists()).toBe(true);
    expect(cue.text()).toContain("Re-enter your password to retry");
    expect(wrapper.find("#sig-error-msg").text()).toContain(
      "Identity verification failed"
    );

    // Typing a new password clears the re-entry cue and Retry succeeds
    await wrapper.find("#sig-password").setValue("CorrectPassword!2"); // pragma: allowlist secret
    await flushPromises();
    expect(
      wrapper.find('[data-testid="sig-password-reentry-cue"]').exists()
    ).toBe(false);

    await retryBtn.trigger("click");
    await flushPromises();

    expect(mockOnSign).toHaveBeenCalledWith(
      "fresh-sig-tok",
      "Investigator Certification",
      expect.objectContaining({ signerRole: "PI" })
    );
    expect(wrapper.emitted("success")).toBeTruthy();
    wrapper.unmount();
  });

  it("synchronously flushes sub-500ms debounced useEcrfForm edits before expireSession clears authStore.user", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    vi.spyOn(ecrfFormsApi, "getForm").mockResolvedValue(
      buildMockForm("form-vitals-exp")
    );

    const authStore = useAuthStore();
    authStore.setAuth({
      authenticated: true,
      token: "expiring-jwt",
      tokenParsed: {
        sub: "crc-sub-exp",
        preferred_username: "crc.site101",
        tenant_id: "cadence-local",
        realm_access: { roles: ["CRC"] },
      },
    });

    const ecrf = useEcrfForm();
    await ecrf.load("form-vitals-exp");
    ecrf.entries["item-hr"].text = "91";
    await flushPromises();

    const draftKey = ecrf.getDraftStorageKey("form-vitals-exp")!;
    expect(window.localStorage.getItem(draftKey)).toBeNull();

    // Sudden session expiry before the 500ms debounce timer fires MUST flush
    // the active edit while user/tenant scope is still intact.
    authStore.expireSession("/study/CADENCE-101?form=form-vitals-exp");
    await flushPromises();

    expect(authStore.isAuthenticated).toBe(false);
    expect(authStore.sessionExpired).toBe(true);
    const persisted = JSON.parse(window.localStorage.getItem(draftKey) || "{}");
    expect(persisted.items?.["item-hr"]?.text).toBe("91");
    expect(peekWebReturnUrl()).toBe("/study/CADENCE-101?form=form-vitals-exp");
  });

  it("consumes saved return URL while preserving /onboarding/trial redirect for newly registered trial accounts", async () => {
    saveWebReturnUrl("/ctms?siteId=SITE-101");
    const authStore = useAuthStore();
    authStore.setAuth({
      authenticated: true,
      token: "new-trial-jwt",
      tokenParsed: {
        sub: "self-reg-sub-1",
        preferred_username: "founder@example.com", // deid-ignore
        email: "founder@example.com", // deid-ignore
        tenant_id: "",
        realm_access: { roles: [] },
      },
    });
    expect(authStore.needsTrialOnboarding).toBe(true);

    const pushSpy = vi.spyOn(router, "push").mockResolvedValue(undefined);
    const wrapper = mount(LoginView, {
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    await wrapper.find(".auth-success-alert .btn-primary").trigger("click");
    await flushPromises();

    expect(pushSpy).toHaveBeenCalledWith("/onboarding/trial");
    expect(peekWebReturnUrl()).toBeNull();
    wrapper.unmount();
  });

  it("suppresses duplicate beforeunload prompts on StudyCanvasView when sessionExpired or isLoggingOut is active", async () => {
    const authStore = useAuthStore();
    const logoutKeycloak = vi.fn().mockResolvedValue(undefined);
    const keycloak = {
      authenticated: true,
      token: "canvas-jwt",
      tokenParsed: {
        sub: "crc-sub-canvas",
        preferred_username: "crc.site101",
        tenant_id: "cadence-local",
        realm_access: { roles: ["CRC"] },
      },
      logout: logoutKeycloak,
    };
    setKeycloakClient(keycloak);
    authStore.setAuth(keycloak);

    const canvasWrapper = mount(StudyCanvasView, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    // Mark the embedded form workspace dirty
    (
      canvasWrapper.vm as unknown as { formWorkspaceDirty: boolean }
    ).formWorkspaceDirty = true;

    const normalUnload = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(normalUnload);
    expect(normalUnload.defaultPrevented).toBe(true);

    // Session expiry redirect must not trigger browser unload prompt
    authStore.expireSession("/study/CADENCE-101");
    const expiredUnload = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(expiredUnload);
    expect(expiredUnload.defaultPrevented).toBe(false);

    // Re-authenticate, then voluntary logout must also suppress browser unload prompt
    authStore.setAuth(keycloak);
    await authStore.logout();
    const logoutUnload = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(logoutUnload);
    expect(logoutUnload.defaultPrevented).toBe(false);

    canvasWrapper.unmount();
  });

  it("resolves web OIDC redirect URIs under subpath BASE_URL and preserves #5894 demo MSW entitlements lookup", async () => {
    vi.stubEnv("BASE_URL", "/cadence-clinical/");
    const redirected = resolveWebRedirectUri("/studies/CADENCE-101?stage=data");
    expect(redirected).toContain(
      "/cadence-clinical/studies/CADENCE-101?stage=data"
    );

    vi.stubEnv("VITE_DEMO_MODE", "true");
    setActivePinia(createPinia());
    const authStore = useAuthStore();
    authStore.startDemoSession();

    const entitlementsSpy = vi
      .spyOn(kernelApi, "entitlements")
      .mockResolvedValueOnce({
        tenant_ref: "tenant:demo",
        tenant_id: "demo",
        subscription_ref: "sub:select",
        tier: "Select",
        status: "Active",
        started_at: null,
        trial_ends_at: null,
        is_read_only: false,
        product_families: ["Blueprint", "Capture", "Orbit"],
        baseline_capabilities: ["audit_trail", "electronic_signatures"],
        environment_notice: "Demo",
      });

    const snapshot = await authStore.loadEntitlements({ force: true });
    expect(entitlementsSpy).toHaveBeenCalledTimes(1);
    expect(snapshot?.tier).toBe("Select");
    expect(authStore.activeTier).toBe("Select");
  });
});
