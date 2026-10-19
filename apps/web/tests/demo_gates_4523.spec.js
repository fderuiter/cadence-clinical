import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth";
import { useSignatureStore } from "../src/stores/signatures";
import { useEtmfStore } from "../src/stores/etmf";
import { useNotificationsStore } from "../src/stores/notifications";

/**
 * #4523: the local demo simulations (mock signatures, demo TMF documents, local notifications) may run only in a
 * demo build, whatever the auth store says. Outside one, a failing gateway must surface as an error.
 */
function failEveryRequest(status) {
  const fetchMock = vi.fn(() =>
    Promise.resolve(
      new Response(JSON.stringify({ detail: `Upstream failure ${status}` }), {
        status,
        headers: { "Content-Type": "application/json" },
      })
    )
  );
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

describe("demo simulations are gated on a demo build (#4523)", () => {
  let authStore;

  beforeEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
    setActivePinia(createPinia());
    authStore = useAuthStore();
    // Simulate stale/forged store state: demo flag on, no token.
    authStore.isDemoMode = true;
    authStore.token = null;
  });

  it("submitBatchSignature does not return a mock-sig result outside a demo build", async () => {
    const fetchMock = failEveryRequest(502);
    const store = useSignatureStore();

    await expect(
      store.submitBatchSignature({
        studyId: "STUDY-01",
        subjectId: "SUBJ-01",
        formIds: ["FORM-01"],
        password: "pw", // pragma: allowlist secret
        meaning: "APPROVED",
      })
    ).rejects.toMatchObject({ status: 502 });

    expect(fetchMock).toHaveBeenCalled();
    expect(store.lastSignatureResult).toBeNull();
    expect(store.signatureError).toBeTruthy();
    expect(window.localStorage.getItem("lastSignatureResult")).toBeNull();
  });

  it("does not restore a persisted demo signature outside a demo build", () => {
    window.localStorage.setItem(
      "lastSignatureResult",
      JSON.stringify({ status: "demo", signatures: [] })
    );
    setActivePinia(createPinia());
    expect(useSignatureStore().lastSignatureResult).toBeNull();
  });

  it("etmf fetchDocuments does not serve demo documents outside a demo build", async () => {
    failEveryRequest(500);
    const store = useEtmfStore();
    await store.fetchDocuments("01.01.01");
    expect(store.documentsList).toEqual([]);
    expect(window.localStorage.getItem("demo_documents")).toBeNull();
  });

  it("etmf uploadDocument does not fake a successful upload outside a demo build", async () => {
    failEveryRequest(504);
    const store = useEtmfStore();
    await expect(
      store.uploadDocument({
        filename: "protocol.pdf",
        reason_for_change: "Initial upload",
      })
    ).rejects.toMatchObject({ status: 504 });
    expect(window.localStorage.getItem("demo_documents")).toBeNull();
  });

  it("notifications start empty and report a failed fetch outside a demo build", async () => {
    failEveryRequest(500);
    const store = useNotificationsStore();
    expect(store.notifications).toEqual([]);
    await store.fetchNotifications();
    expect(store.notifications).toEqual([]);
    expect(store.errorStatus).toBe(500);
  });

  it("notifications acknowledge/resolve throw instead of changing local state outside a demo build", async () => {
    failEveryRequest(502);
    const store = useNotificationsStore();
    store.notifications = [{ id: "n-1", status: "OPEN", version_index: 1 }];
    await expect(store.acknowledge("n-1", "Seen")).rejects.toMatchObject({
      status: 502,
    });
    await expect(store.resolve("n-1", "Done")).rejects.toMatchObject({
      status: 502,
    });
    expect(store.notifications[0].status).toBe("OPEN");
  });

  it("the simulations still run in a demo build", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const fetchMock = failEveryRequest(500);
    setActivePinia(createPinia());
    const auth = useAuthStore();
    auth.isDemoMode = true;
    auth.token = null;

    expect(useNotificationsStore().notifications.length).toBeGreaterThan(0);
    const res = await useSignatureStore().submitBatchSignature({
      studyId: "STUDY-01",
      subjectId: "SUBJ-01",
      formIds: ["FORM-01"],
      password: "pw", // pragma: allowlist secret
      meaning: "APPROVED",
    });
    expect(res.status).toBe("demo");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
