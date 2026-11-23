import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createMemoryHistory } from "vue-router";
import { mount } from "@vue/test-utils";
import {
  FileUploadModal,
  resetTimingPreferencesForTests,
  useTimingPreferences,
} from "ui";
import AppShell from "../src/components/AppShell.vue";
import ToastHost from "../src/components/ToastHost.vue";
import LoginView from "../src/views/LoginView.vue";
import { useAuthStore } from "../src/stores/auth";
import { useToastStore } from "../src/stores/toasts";
import {
  resetWebSessionWarningState,
  syncKeycloakClientWithCoordinator,
  webSessionWarningState,
} from "../src/auth/sessionCoordinator";

function makeJwtWithExp(expSeconds, sub = "crc-01") {
  const encode = (obj) =>
    btoa(JSON.stringify(obj))
      .replace(/\+/g, "-")
      .replace(/\//g, "_")
      .replace(/=+$/, "");
  return `${encode({ alg: "HS256", typ: "JWT" })}.${encode({ sub, exp: expSeconds })}.sig`;
}

function createStubRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/", name: "home", component: { template: "<div>Home</div>" } },
      { path: "/login", name: "login", component: LoginView },
      {
        path: "/plans",
        name: "plans",
        component: { template: "<div>Plans</div>" },
      },
      {
        path: "/studies",
        name: "studies",
        component: { template: "<div>Studies</div>" },
      },
      {
        path: "/ecrf",
        name: "ecrf",
        component: { template: "<div>eCRF</div>" },
      },
      {
        path: "/tickets",
        name: "tickets",
        component: { template: "<div>Tickets</div>" },
      },
      {
        path: "/knowledge",
        name: "knowledge",
        component: { template: "<div>Knowledge</div>" },
      },
    ],
  });
}

describe("apps/web WCAG 2.2 timing adjustability, dual live regions, session warnings, & LoginView expiry alert", () => {
  let pinia;
  const mountedWrappers = [];

  function track(wrapper) {
    mountedWrappers.push(wrapper);
    return wrapper;
  }

  beforeEach(() => {
    resetTimingPreferencesForTests();
    resetWebSessionWarningState();
    syncKeycloakClientWithCoordinator(null);
    pinia = createPinia();
    setActivePinia(pinia);
  });

  afterEach(() => {
    while (mountedWrappers.length > 0) {
      mountedWrappers.pop()?.unmount();
    }
    resetTimingPreferencesForTests();
    resetWebSessionWarningState();
    syncKeycloakClientWithCoordinator(null);
    vi.restoreAllMocks();
  });

  it("separates polite status confirmations from persistent error alerts, supports explicit Pause/Resume toggle, and scales with timing preferences", async () => {
    vi.useFakeTimers();
    vi.spyOn(console, "error").mockImplementation(() => {});
    try {
      const toasts = useToastStore();
      const prefs = useTimingPreferences();

      toasts.push({
        tone: "success",
        title: "Document Ingested",
        message: "Protocol uploaded to eTMF.",
        timeoutMs: 3000,
      });
      toasts.apiError("Signature rejected", new Error("Invalid credentials"));

      const wrapper = track(
        mount(ToastHost, {
          global: { plugins: [pinia] },
        })
      );

      const politeRegion = wrapper.find('[data-testid="toast-region-polite"]');
      const assertiveRegion = wrapper.find(
        '[data-testid="toast-region-assertive"]'
      );
      expect(politeRegion.attributes("role")).toBe("status");
      expect(politeRegion.attributes("aria-live")).toBe("polite");
      expect(politeRegion.text()).toContain("Document Ingested");
      expect(politeRegion.text()).not.toContain("Signature rejected");

      expect(assertiveRegion.attributes("role")).toBe("alert");
      expect(assertiveRegion.attributes("aria-live")).toBe("assertive");
      expect(assertiveRegion.text()).toContain("Signature rejected");

      // Persistent error alert omits pause toggle; timed polite status renders pause button
      expect(assertiveRegion.find(".toast-pause-btn").exists()).toBe(false);
      const pauseBtn = politeRegion.find(".toast-pause-btn");
      expect(pauseBtn.exists()).toBe(true);
      expect(pauseBtn.attributes("aria-pressed")).toBe("false");

      // Explicit pause prevents dismissal past 3000ms
      await pauseBtn.trigger("click");
      expect(pauseBtn.attributes("aria-pressed")).toBe("true");
      vi.advanceTimersByTime(10000);
      await nextTick();
      expect(politeRegion.find(".toast-success").exists()).toBe(true);

      // Explicit resume dismisses after remaining duration
      await pauseBtn.trigger("click");
      expect(pauseBtn.attributes("aria-pressed")).toBe("false");
      vi.advanceTimersByTime(3100);
      await nextTick();
      expect(politeRegion.find(".toast-success").exists()).toBe(false);
      expect(assertiveRegion.find(".toast-error").exists()).toBe(true);

      // Manual ("off") mode hides pause button and disables auto-dismiss
      prefs.setTimingMode("off");
      await nextTick();
      toasts.push({
        tone: "info",
        title: "Saved",
        message: "Draft stored locally.",
        timeoutMs: 2000,
      });
      await nextTick();
      expect(politeRegion.find(".toast-pause-btn").exists()).toBe(false);
      vi.advanceTimersByTime(30000);
      await nextTick();
      expect(politeRegion.find(".toast-info").exists()).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });

  it("renders polite upload verification and Done button in FileUploadModal without closing at 800ms, and passes axe-core", async () => {
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({
          file_id: "f_101",
          object_key: "studies/CADENCE-101/icf.pdf",
          upload_url: "https://storage.local/upload",
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
      });

    const hostNode = document.createElement("div");
    document.body.appendChild(hostNode);
    const onClose = vi.fn();

    try {
      const wrapper = track(
        mount(FileUploadModal, {
          attachTo: hostNode,
          props: {
            modelValue: true,
            studyId: "CADENCE-101",
            onClose,
          },
        })
      );
      await nextTick();

      const pdf = new File(["%PDF-1.4"], "informed-consent.pdf", {
        type: "application/pdf",
      });
      await wrapper.vm.handleSelectedFile(pdf);
      wrapper.vm.reasonForChange = "Approved IRB consent form upload";
      await nextTick();

      await wrapper.vm.startUpload();
      await nextTick();

      const statusRegion = document.body.querySelector(
        '[data-testid="upload-success-status"]'
      );
      expect(statusRegion).not.toBeNull();
      expect(statusRegion.getAttribute("role")).toBe("status");
      expect(statusRegion.getAttribute("aria-live")).toBe("polite");
      expect(statusRegion.textContent).toContain("informed-consent.pdf");

      const doneBtn = document.body.querySelector(
        '[data-testid="upload-done-btn"]'
      );
      expect(doneBtn).not.toBeNull();
      expect(doneBtn.textContent.trim()).toBe("Done");
      expect(onClose).not.toHaveBeenCalled();

      await expect(document.body).toBeAccessible();

      doneBtn.click();
      await nextTick();
      expect(onClose).toHaveBeenCalledTimes(1);
    } finally {
      hostNode.remove();
    }
  });

  it("wires #timing-mode-select and SessionExpiryModal in AppShell to extend active OIDC sessions", async () => {
    const router = createStubRouter();
    await router.push("/studies");
    await router.isReady();

    const authStore = useAuthStore();
    const nowSec = Math.floor(Date.now() / 1000);
    const mockKeycloak = {
      authenticated: true,
      token: makeJwtWithExp(nowSec + 45, "crc.site101"),
      refreshToken: "refresh-01",
      tokenParsed: {
        sub: "crc-uuid-1",
        preferred_username: "crc.site101",
        tenant_id: "tenant-1",
        exp: nowSec + 45,
        realm_access: { roles: ["crc"] },
      },
      updateToken: vi.fn(async () => {
        const nextExp = Math.floor(Date.now() / 1000) + 300;
        mockKeycloak.token = makeJwtWithExp(nextExp, "crc.site101");
        mockKeycloak.tokenParsed.exp = nextExp;
        return true;
      }),
    };

    authStore.setAuth(mockKeycloak);
    vi.spyOn(authStore, "loadEntitlements").mockResolvedValue(null);
    syncKeycloakClientWithCoordinator(mockKeycloak);

    const mountNode = document.createElement("div");
    document.body.appendChild(mountNode);
    try {
      const wrapper = track(
        mount(AppShell, {
          attachTo: mountNode,
          global: { plugins: [pinia, router] },
          slots: { default: "<div>Workspace</div>" },
        })
      );
      await nextTick();
      await nextTick();

      const timingSelect = wrapper.find("#timing-mode-select");
      expect(timingSelect.exists()).toBe(true);
      await timingSelect.setValue("extended");
      expect(useTimingPreferences().timingMode.value).toBe("extended");

      // Session expires in 45s (< 60s lead), so SessionExpiryModal opens as alertdialog
      expect(webSessionWarningState.isOpen).toBe(true);
      const modalEl = document.getElementById("shell-session-expiry-modal");
      expect(modalEl).not.toBeNull();
      expect(modalEl.getAttribute("role")).toBe("alertdialog");

      const extendBtn = modalEl.querySelector(
        '[data-testid="session-extend-btn"]'
      );
      expect(extendBtn).not.toBeNull();
      extendBtn.click();
      await Promise.resolve();
      await Promise.resolve();
      await nextTick();
      await nextTick();
      expect(webSessionWarningState.isOpen).toBe(false);
      expect(mockKeycloak.updateToken).toHaveBeenCalled();
    } finally {
      mountNode.remove();
    }
  });

  it("renders a role=alert banner on LoginView when authStore.sessionExpired is true confirming local drafts remain preserved", async () => {
    const router = createStubRouter();
    await router.push("/login");
    await router.isReady();

    const authStore = useAuthStore();
    authStore.expireSession("/ecrf");

    const mountNode = document.createElement("div");
    document.body.appendChild(mountNode);
    try {
      const wrapper = track(
        mount(LoginView, {
          attachTo: mountNode,
          global: { plugins: [pinia, router] },
        })
      );
      await nextTick();

      const expiredAlert = wrapper.find("#login-session-expired-alert");
      expect(expiredAlert.exists()).toBe(true);
      expect(expiredAlert.attributes("role")).toBe("alert");
      expect(expiredAlert.text()).toContain("Session expired");
      expect(expiredAlert.text()).toContain("drafts remain preserved");

      await expect(mountNode).toBeAccessible();
    } finally {
      mountNode.remove();
    }
  });
});
