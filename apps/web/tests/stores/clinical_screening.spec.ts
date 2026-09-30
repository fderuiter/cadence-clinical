import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../../src/stores/clinical";
import { useAuthStore } from "../../src/stores/auth";
import { useToastStore } from "../../src/stores/toasts.js";

describe("Clinical Store & Auth Store - Subject Screening & CRC Gating Specification", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
    if (typeof window !== "undefined" && window.localStorage) {
      window.localStorage.clear();
    }
  });

  it("authStore.hasRole and authStore.isCrc resolve CRC and investigator roles correctly", () => {
    const authStore = useAuthStore();

    // Set CRC role
    authStore.rawRoles = ["Site Investigator"];
    expect(authStore.isCrc).toBe(true);
    expect(authStore.hasRole("crc")).toBe(true);

    // Set Monitor role
    authStore.rawRoles = ["CRA", "Monitor"];
    expect(authStore.isCrc).toBe(false);
    expect(authStore.hasRole("cra")).toBe(true);

    // Set Super Admin
    authStore.rawRoles = ["Super Admin"];
    expect(authStore.isCrc).toBe(true);
    expect(authStore.hasRole("sponsor_designer")).toBe(true);
  });

  it("clinicalStore.subjectIds exposes reactive array of all subject IDs", () => {
    const store = useClinicalStore();
    store.subjects = [
      {
        id: "SUBJ-001",
        status: "ENROLLED",
        active_protocol_version: "1.0.0",
        isGated: false,
      },
      {
        id: "SUBJ-002",
        status: "ENROLLED",
        active_protocol_version: "1.0.0",
        isGated: true,
      },
    ];
    expect(store.subjectIds).toBeDefined();
    expect(store.subjectIds).toContain("SUBJ-001");
    expect(store.subjectIds).toContain("SUBJ-002");
  });

  it("clinicalStore.screenSubject refuses and changes nothing, since no service evaluates eligibility (#4523)", () => {
    const store = useClinicalStore();
    const subjects = JSON.stringify(store.subjects);
    const blocks = store.ledgerBlocks.length;

    expect(store.screenSubject()).toBeNull();

    expect(JSON.stringify(store.subjects)).toBe(subjects);
    expect(store.ledgerBlocks).toHaveLength(blocks);
    expect(useToastStore().toasts).toEqual([
      expect.objectContaining({
        tone: "error",
        title: "Screening a subject isn't available yet",
      }),
    ]);
  });
});
