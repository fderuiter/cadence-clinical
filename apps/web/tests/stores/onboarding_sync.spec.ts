import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useOnboardingStore } from "../../src/stores/onboarding";
import * as onboardingApi from "../../src/api/onboarding.js";

vi.mock("../../src/api/onboarding.js", () => ({
  getOnboardingPreferences: vi.fn(),
  saveOnboardingPreferences: vi.fn(),
}));

describe("Onboarding Pinia Store Hybrid Client-Server Sync", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
    vi.useFakeTimers();
    if (typeof window !== "undefined" && window.localStorage) {
      window.localStorage.clear();
    }
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("loads state from local storage synchronously on startup", () => {
    window.localStorage.setItem("onboarding_site_crc_current_step", "3");
    window.localStorage.setItem("onboarding_site_crc_is_active", "true");
    window.localStorage.setItem("onboarding_site_crc_disabled", "false");
    window.localStorage.setItem(
      "onboarding_site_crc_updated_at",
      "2026-10-01T10:00:00.000Z"
    );

    vi.mocked(onboardingApi.getOnboardingPreferences).mockImplementation(
      () => new Promise(() => {}) // pending promise
    );

    const store = useOnboardingStore();
    store.loadPersonaState("site_crc");

    expect(store.currentPersona).toBe("site_crc");
    expect(store.currentStep).toBe(3);
    expect(store.isActive).toBe(true);
    expect(store.disabled).toBe(false);
    expect(store.updatedAt).toBe("2026-10-01T10:00:00.000Z");
  });

  it("adopts remote backend state when remote timestamp is newer", async () => {
    window.localStorage.setItem("onboarding_site_crc_current_step", "2");
    window.localStorage.setItem(
      "onboarding_site_crc_updated_at",
      "2026-10-01T10:00:00.000Z"
    );

    vi.mocked(onboardingApi.getOnboardingPreferences).mockResolvedValue({
      persona: "site_crc",
      currentStep: 5,
      isActive: true,
      disabled: false,
      eventsJson: JSON.stringify([
        { id: "e1", type: "step_completed", timestamp: "2026-10-02T12:00:00.000Z" },
      ]),
      updatedAt: "2026-10-02T12:00:00.000Z",
    });

    const store = useOnboardingStore();
    store.loadPersonaState("site_crc");

    await vi.waitFor(() => {
      expect(store.currentStep).toBe(5);
    });

    expect(store.events.length).toBe(1);
    expect(store.updatedAt).toBe("2026-10-02T12:00:00.000Z");
    expect(
      window.localStorage.getItem("onboarding_site_crc_current_step")
    ).toBe("5");
  });

  it("pushes local state to backend when local timestamp is newer", async () => {
    const newerDate = "2026-10-02T15:00:00.000Z";
    window.localStorage.setItem("onboarding_site_crc_current_step", "4");
    window.localStorage.setItem("onboarding_site_crc_updated_at", newerDate);

    vi.mocked(onboardingApi.getOnboardingPreferences).mockResolvedValue({
      persona: "site_crc",
      currentStep: 2,
      isActive: true,
      disabled: false,
      eventsJson: "[]",
      updatedAt: "2026-10-01T10:00:00.000Z", // older remote date
    });

    vi.mocked(onboardingApi.saveOnboardingPreferences).mockResolvedValue({
      persona: "site_crc",
      currentStep: 4,
      isActive: true,
      disabled: false,
      eventsJson: "[]",
      updatedAt: newerDate,
    });

    const store = useOnboardingStore();
    store.loadPersonaState("site_crc");

    await vi.waitFor(() => {
      expect(onboardingApi.saveOnboardingPreferences).toHaveBeenCalledWith(
        "site_crc",
        expect.objectContaining({ currentStep: 4 })
      );
    });

    expect(store.currentStep).toBe(4);
  });

  it("sends debounced backend updates on step navigation", async () => {
    vi.mocked(onboardingApi.getOnboardingPreferences).mockResolvedValue({
      persona: "sponsor_designer",
      currentStep: 1,
      isActive: true,
      disabled: false,
      eventsJson: "[]",
      updatedAt: "1970-01-01T00:00:00.000Z",
    });

    vi.mocked(onboardingApi.saveOnboardingPreferences).mockResolvedValue({
      persona: "sponsor_designer",
      currentStep: 3,
      isActive: true,
      disabled: false,
      eventsJson: "[]",
      updatedAt: "2026-10-02T12:05:00.000Z",
    });

    const store = useOnboardingStore();
    store.loadPersonaState("sponsor_designer");

    store.nextStep("Step 1 done");
    store.nextStep("Step 2 done");

    expect(store.currentStep).toBe(3);
    expect(onboardingApi.saveOnboardingPreferences).not.toHaveBeenCalled();

    // Advance past 300ms debounce timer
    vi.advanceTimersByTime(350);

    expect(onboardingApi.saveOnboardingPreferences).toHaveBeenCalledTimes(1);
    expect(onboardingApi.saveOnboardingPreferences).toHaveBeenCalledWith(
      "sponsor_designer",
      expect.objectContaining({ currentStep: 3 })
    );
  });

  it("restores onboarding progress when local storage has been cleared", async () => {
    // Local storage is empty (cleared)
    vi.mocked(onboardingApi.getOnboardingPreferences).mockResolvedValue({
      persona: "cra_monitor",
      currentStep: 4,
      isActive: false,
      disabled: false,
      eventsJson: JSON.stringify([
        { id: "e1", type: "step_completed", timestamp: "2026-10-02T10:00:00.000Z" },
      ]),
      updatedAt: "2026-10-02T10:00:00.000Z",
    });

    const store = useOnboardingStore();
    store.loadPersonaState("cra_monitor");

    // Initially 1 (default from empty storage)
    expect(store.currentStep).toBe(1);

    await vi.waitFor(() => {
      expect(store.currentStep).toBe(4);
    });

    expect(store.isActive).toBe(false);
    expect(
      window.localStorage.getItem("onboarding_cra_monitor_current_step")
    ).toBe("4");
  });

  it("handles network failures gracefully without blocking UI or throwing errors", async () => {
    vi.mocked(onboardingApi.getOnboardingPreferences).mockRejectedValue(
      new Error("Network Error / Offline")
    );

    const store = useOnboardingStore();
    expect(() => store.loadPersonaState("super_admin")).not.toThrow();

    store.nextStep("Step 1 done");
    expect(store.currentStep).toBe(2);

    expect(() => store.disableTour()).not.toThrow();
    expect(store.disabled).toBe(true);
  });
});
