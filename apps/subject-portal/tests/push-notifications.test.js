import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createApp, nextTick } from "vue";
import App from "../src/App.vue";
import {
  state,
  urlBase64ToUint8Array,
  enablePushNotifications,
} from "../src/index.js";

describe("Native Web Push Notifications Engine & VAPID Subscription", () => {
  let activeApp = null;

  beforeEach(() => {
    document.body.innerHTML = `<div id="app"></div>`;
    state.pushSubscribed = false;
    state.pushPermission = "default";
    state.pushLoading = false;
    state.pushError = null;

    // Mock global Notification
    globalThis.Notification = {
      permission: "default",
      requestPermission: vi.fn().mockResolvedValue("granted"),
    };

    // Mock Service Worker & PushManager
    const mockSubscription = {
      endpoint: "https://push.services.mozilla.com/mock-endpoint-123",
      toJSON: () => ({
        endpoint: "https://push.services.mozilla.com/mock-endpoint-123",
        keys: {
          p256dh: "mock_p256dh_key",
          auth: "mock_auth_secret",
        },
      }),
    };

    const mockPushManager = {
      subscribe: vi.fn().mockResolvedValue(mockSubscription),
    };

    Object.defineProperty(globalThis.navigator, "serviceWorker", {
      value: {
        ready: Promise.resolve({
          pushManager: mockPushManager,
        }),
        register: vi.fn().mockResolvedValue({
          pushManager: mockPushManager,
        }),
      },
      configurable: true,
      writable: true,
    });

    globalThis.PushManager = mockPushManager;
  });

  afterEach(() => {
    if (activeApp) {
      try {
        activeApp.unmount();
      } catch {
        // Ignore unmount errors during teardown
      }
      activeApp = null;
    }
    vi.restoreAllMocks();
  });

  it("urlBase64ToUint8Array properly converts URL-safe base64 VAPID key to Uint8Array", () => {
    const vapidKey =
      "BCadeNCeVapiDPubliCKeYSeEd2026SecureKeyForSubjectPortalDemoPushNotificationEnrolment123456=";
    const array = urlBase64ToUint8Array(vapidKey);

    expect(array).toBeInstanceOf(Uint8Array);
    expect(array.length).toBeGreaterThan(0);
  });

  it("enablePushNotifications requests browser permission, fetches VAPID key, and registers subscription", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockImplementation((url) => {
      if (url.toString().includes("vapid-public-key")) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              publicKey:
                "BCadeNCeVapiDPubliCKeYSeEd2026SecureKeyForSubjectPortalDemoPushNotificationEnrolment123456=",
            }),
            { status: 200 }
          )
        );
      }
      if (url.toString().includes("subscriptions")) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              id: "sub_123",
              subjectId: "subject_001",
              endpoint: "https://push.services.mozilla.com/mock-endpoint-123",
              isActive: true,
            }),
            { status: 200 }
          )
        );
      }
      return Promise.resolve(new Response("{}", { status: 200 }));
    });

    const sub = await enablePushNotifications();

    expect(Notification.requestPermission).toHaveBeenCalled();
    expect(state.pushSubscribed).toBe(true);
    expect(sub.endpoint).toBe(
      "https://push.services.mozilla.com/mock-endpoint-123"
    );
  });

  it("renders Web Push Enrollment Card in Subject Portal App", async () => {
    activeApp = createApp(App);
    activeApp.mount("#app");
    await nextTick();

    const card = document.getElementById("push-enrollment-card");
    expect(card).not.toBeNull();

    const enableBtn = document.getElementById("btn-enable-push");
    expect(enableBtn).not.toBeNull();
    expect(enableBtn.textContent).toContain("Enable Push Alerts");

    // Simulate clicking the enable push alerts button
    state.pushSubscribed = true;
    await nextTick();

    const activeBadge = document.getElementById("push-subscribed-badge");
    expect(activeBadge).not.toBeNull();
    expect(activeBadge.textContent).toContain("✓ Active");
  });
});
