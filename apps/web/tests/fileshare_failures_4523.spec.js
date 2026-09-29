import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useFileshareStore, SEED_FILES } from "../src/stores/fileshare";
import { useToastStore } from "../src/stores/toasts.js";

/**
 * #4523: a failed fileshare call must not be shown as success. The real openapi-fetch client is used; only the
 * network (globalThis.fetch) is replaced with a gateway that answers every request with the given status.
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

function respondWith(body) {
  vi.stubGlobal(
    "fetch",
    vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify(body), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      )
    )
  );
}

const FILE = {
  id: "file-real-001",
  study_id: "STUDY-USDM-001",
  site_id: "SITE-101",
  filename: "Protocol.pdf",
  uploaded_by: "cra_user",
  is_on_hold: false,
  hold_reason: null,
  status: "ACTIVE",
  grant_count: 1,
};

const GRANT = {
  id: "grant-real-001",
  file_record_id: FILE.id,
  granted_to_user_id: "auditor_user_01",
  is_active: true,
};

const LINK = {
  id: "guest-real-001",
  file_record_id: FILE.id,
  guest_url: "https://cadence.example.com/share/real",
};

describe("fileshare store reports backend failures instead of fabricating (#4523)", () => {
  let store;
  let toasts;

  beforeEach(() => {
    vi.unstubAllGlobals();
    setActivePinia(createPinia());
    store = useFileshareStore();
    toasts = useToastStore();
    store.files = [{ ...FILE }];
    store.activeGrants = { [FILE.id]: [{ ...GRANT }] };
    store.activeGuestLinks = { [FILE.id]: [{ ...LINK }] };
  });

  it("starts with no sample files, grants or guest links outside a demo build", () => {
    setActivePinia(createPinia());
    const fresh = useFileshareStore();
    expect(fresh.files).toEqual([]);
    expect(fresh.activeGrants).toEqual({});
    expect(fresh.activeGuestLinks).toEqual({});
  });

  it("seeds the sample files only in a demo build", () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    setActivePinia(createPinia());
    expect(useFileshareStore().files.length).toBe(SEED_FILES.length);
  });

  it("treats an empty server response as an empty file list", async () => {
    respondWith([]);
    await store.fetchFiles();
    expect(store.files).toEqual([]);
    expect(toasts.toasts).toHaveLength(0);
  });

  it("treats an empty grants response as no grants", async () => {
    respondWith([]);
    await store.fetchGrants(FILE.id);
    expect(store.activeGrants[FILE.id]).toEqual([]);
  });

  describe.each([500, 502, 504])(
    "when the gateway answers HTTP %i",
    (status) => {
      beforeEach(() => {
        failEveryRequest(status);
      });

      function expectErrorToast(title) {
        expect(toasts.toasts).toHaveLength(1);
        expect(toasts.toasts[0].tone).toBe("error");
        expect(toasts.toasts[0].title).toBe(title);
        expect(toasts.toasts[0].message).toContain(`HTTP ${status}`);
      }

      it("fetchFiles keeps the current list and reports the error", async () => {
        await store.fetchFiles();
        expect(store.files).toEqual([FILE]);
        expect(store.error).toBeTruthy();
        expectErrorToast("Could not load documents");
      });

      it("fetchGrants keeps the current grants and reports the error", async () => {
        await store.fetchGrants(FILE.id);
        expect(store.activeGrants[FILE.id]).toEqual([GRANT]);
        expectErrorToast("Could not load share grants");
      });

      it("addGrant does not push a local grant and rethrows", async () => {
        await expect(
          store.addGrant(FILE.id, {
            granted_to_user_id: "someone",
            reason_for_change: "Access for review",
          })
        ).rejects.toMatchObject({ status });
        expect(store.activeGrants[FILE.id]).toEqual([GRANT]);
        expect(store.files[0].grant_count).toBe(1);
        expectErrorToast("Could not create share grant");
      });

      it("revokeGrant keeps the grant", async () => {
        await expect(
          store.revokeGrant(FILE.id, GRANT.id, "No longer needed")
        ).resolves.toBe(false);
        expect(store.activeGrants[FILE.id]).toEqual([GRANT]);
        expectErrorToast("Could not revoke share grant");
      });

      it("generateGuestLink does not invent a gxt_ token and rethrows", async () => {
        await expect(
          store.generateGuestLink(FILE.id, {
            expires_in_hours: 24,
            reason_for_change: "IRB review",
          })
        ).rejects.toMatchObject({ status });
        expect(store.activeGuestLinks[FILE.id]).toEqual([LINK]);
        expect(JSON.stringify(store.activeGuestLinks)).not.toContain("gxt_");
        expectErrorToast("Could not create guest link");
      });

      it("revokeGuestLink keeps the link", async () => {
        await expect(
          store.revokeGuestLink(FILE.id, LINK.id, "Review finished")
        ).resolves.toBe(false);
        expect(store.activeGuestLinks[FILE.id]).toEqual([LINK]);
        expectErrorToast("Could not revoke guest link");
      });

      it("setHold does not mark the file as on hold", async () => {
        await expect(store.setHold(FILE.id, "Legal hold")).resolves.toBe(false);
        expect(store.files[0].is_on_hold).toBe(false);
        expect(store.files[0].status).toBe("ACTIVE");
        expectErrorToast("Could not set retention hold");
      });

      it("clearHold does not release a held file", async () => {
        Object.assign(store.files[0], {
          is_on_hold: true,
          hold_reason: "Audit freeze",
          status: "ON_HOLD",
        });
        await expect(store.clearHold(FILE.id, "Audit done")).resolves.toBe(
          false
        );
        expect(store.files[0].is_on_hold).toBe(true);
        expect(store.files[0].hold_reason).toBe("Audit freeze");
        expect(store.files[0].status).toBe("ON_HOLD");
        expectErrorToast("Could not clear retention hold");
      });

      it("deleteFile keeps the file in the list and selection", async () => {
        store.selectedFileIds = [FILE.id];
        await expect(store.deleteFile(FILE.id, "Duplicate")).resolves.toBe(
          false
        );
        expect(store.files).toEqual([FILE]);
        expect(store.selectedFileIds).toEqual([FILE.id]);
        expectErrorToast("Could not delete document");
      });

      it("fetchActivityFeed rethrows instead of inventing an audit feed", async () => {
        await expect(store.fetchActivityFeed(FILE.id)).rejects.toMatchObject({
          status,
        });
      });
    }
  );
});
