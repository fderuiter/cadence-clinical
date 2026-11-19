import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { reactive, ref, nextTick } from "vue";
import { createPinia, setActivePinia } from "pinia";
import { useEcrfDrafts } from "../src/composables/useEcrfDrafts.js";
import { useEcrfForm } from "../src/composables/useEcrfForm";
import { ecrfFormsApi } from "../src/api/ecrfFormsApi";
import { useAuthStore } from "../src/stores/auth.js";
import { getStorageKey, useClinicalStore } from "../src/stores/clinical";
import {
  purgeUserScopedStorage,
  scopedKey,
} from "../src/utils/userScopedStorage.js";

vi.mock("../src/auth/keycloak.js", () => ({
  getKeycloakClient: () => null,
}));

/**
 * Browser draft containment for the eCRF (#5369): a draft belongs to one
 * tenant and user, never to a fallback identity, and does not survive sign-out
 * or another account signing in on the same browser.
 */

function keycloakSession(sub, tenantId, roles = ["CRC"]) {
  return {
    authenticated: true,
    token: `token-${sub}`,
    tokenParsed: {
      sub,
      ...(tenantId === undefined ? {} : { tenant_id: tenantId }),
      realm_access: { roles },
    },
  };
}

function signIn(sub, tenantId, roles) {
  const auth = useAuthStore();
  auth.setAuth(keycloakSession(sub, tenantId, roles));
  return auth;
}

/** The composable as EcrfView uses it: scope comes from the auth store. */
function openForm(subjectId = "SUBJ-001", values = { vssbp: "" }) {
  const formValues = reactive({ ...values });
  const drafts = useEcrfDrafts(
    ref("STUDY-1"),
    ref(subjectId),
    ref("Screening"),
    formValues
  );
  drafts.setInitialValues({ ...values });
  return { formValues, ...drafts };
}

/** Every key in browser storage (works with the setup's Storage mock). */
function storedKeys() {
  const keys = [];
  for (let i = 0; i < localStorage.length; i++) keys.push(localStorage.key(i));
  return keys.sort();
}

async function typeAndAutosave(form, fieldId, value) {
  form.formValues[fieldId] = value;
  await nextTick();
  vi.advanceTimersByTime(600);
}

const BACKED_FORM_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000f099";

function backedFormFixture() {
  return {
    form_id: BACKED_FORM_ID,
    enrollment_id: "e",
    participant_id: "p",
    study_id: "s",
    study_site_id: "ss",
    protocol_version_id: "pv",
    collection_spec_id: "VS",
    planned_activity_id: "WEEK2",
    collected_at: "2027-03-14T09:00:00Z",
    opened_by: "crc1",
    opened_at: "2027-03-14T09:00:00Z",
    version: 1,
    complete: false,
    missing_required: ["SYSBP"],
    items: [
      {
        item_id: "SYSBP",
        concept: null,
        unit: "mm[Hg]",
        value_kind: "Quantity",
        required: true,
        value: { type: "quantity", value: 120, unit: "mm[Hg]" },
        observation_id: "obs-1",
        revision: 2,
        recorded_by: "crc1",
        recorded_at: "2027-03-14T09:00:00Z",
      },
    ],
    locks: [],
    assertions: [],
    signatures: [],
    content_hash: "sha256:form-v1",
  };
}

describe("eCRF draft isolation (#5369)", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
    setActivePinia(createPinia());
    vi.spyOn(ecrfFormsApi, "getForm").mockImplementation(async () =>
      structuredClone(backedFormFixture())
    );
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it("does not restore a draft that a different user typed on the same browser", async () => {
    signIn("user-a", "tenant-1");
    const formA = openForm();
    await typeAndAutosave(formA, "vssbp", "141");
    expect(formA.hasDraft.value).toBe(true);

    // User B signs in on the same browser without A signing out.
    signIn("user-b", "tenant-1");
    const formB = openForm();

    expect(formB.restoreDraft()).toBe(false);
    expect(formB.formValues.vssbp).toBe("");
    expect(formB.draftRestored.value).toBe(false);
    // A's draft was purged when B signed in, not merely hidden.
    expect(
      localStorage.getItem(
        scopedKey(
          { tenantId: "tenant-1", userId: "user-a" },
          "ecrf_draft",
          "STUDY-1",
          "SUBJ-001",
          "Screening"
        )
      )
    ).toBeNull();
  });

  it("keeps the same user's draft across a token refresh or re-sign-in", async () => {
    signIn("user-a", "tenant-1");
    await typeAndAutosave(openForm(), "vssbp", "141");

    signIn("user-a", "tenant-1");
    const again = openForm();

    expect(again.restoreDraft()).toBe(true);
    expect(again.formValues.vssbp).toBe("141");
  });

  it("isolates backed ecrf_form_draft entries per account, restores for the same user, and purges another user's draft on sign-in", async () => {
    signIn("user-a", "tenant-1");
    const formA = useEcrfForm();
    await formA.load(BACKED_FORM_ID);
    formA.entries.SYSBP.text = "138";
    await nextTick();
    vi.advanceTimersByTime(550);

    const userAKey = scopedKey(
      { tenantId: "tenant-1", userId: "user-a" },
      "ecrf_form_draft",
      BACKED_FORM_ID
    );
    expect(localStorage.getItem(userAKey)).toContain("138");

    // Re-signing in as the same account restores the draft when revision matches.
    signIn("user-a", "tenant-1");
    const sameUser = useEcrfForm();
    await sameUser.load(BACKED_FORM_ID);
    expect(sameUser.entries.SYSBP.text).toBe("138");
    expect(sameUser.dirty.value).toBe(true);

    // Signing in as user-b purges user-a's backed form draft and restores nothing.
    signIn("user-b", "tenant-1");
    expect(localStorage.getItem(userAKey)).toBeNull();
    const otherUser = useEcrfForm();
    await otherUser.load(BACKED_FORM_ID);
    expect(otherUser.entries.SYSBP.text).toBe("120");
    expect(otherUser.dirty.value).toBe(false);
  });

  it("separates the same user id in two tenants", () => {
    const a = scopedKey({ tenantId: "t1", userId: "u" }, "ecrf_draft", "S");
    const b = scopedKey({ tenantId: "t2", userId: "u" }, "ecrf_draft", "S");
    expect(a).not.toBe(b);
  });

  it("encodes key segments so an identifier cannot reach another account's namespace", () => {
    const crafted = scopedKey({ tenantId: "t1", userId: "a:ecrf_draft" }, "x");
    const victim = scopedKey(
      { tenantId: "t1", userId: "a" },
      "ecrf_draft",
      "x"
    );
    expect(crafted).not.toBe(victim);
    expect(crafted.startsWith("cadence_user:t1:a:")).toBe(false);
  });

  it("discards a legacy unscoped draft instead of restoring it to whoever signs in", () => {
    // Older builds wrote drafts with no owner, and fell back to this exact key
    // when no study, subject or visit was selected.
    const legacyKey = "cadence_ecrf_drafts:STUDY-USDM-001:SUBJ-001:Screening";
    localStorage.setItem(legacyKey, JSON.stringify({ vssbp: "188" }));
    localStorage.setItem(
      "clinical_site_crc_formValues",
      JSON.stringify({ vssbp: "188" })
    );

    signIn("user-b", "tenant-1");

    expect(localStorage.getItem(legacyKey)).toBeNull();
    expect(localStorage.getItem("clinical_site_crc_formValues")).toBeNull();
    const form = useEcrfDrafts(
      ref("STUDY-USDM-001"),
      ref("SUBJ-001"),
      ref("Screening"),
      reactive({ vssbp: "" })
    );
    expect(form.restoreDraft()).toBe(false);
  });

  it("saves and restores nothing without a tenant claim", async () => {
    signIn("user-a", undefined);
    const form = openForm();

    expect(form.getStorageKey()).toBeNull();
    await typeAndAutosave(form, "vssbp", "141");
    form.saveDraft();
    expect(localStorage.length).toBe(0);
    expect(form.hasDraft.value).toBe(false);
  });

  it("saves and restores nothing when no one is signed in", async () => {
    const form = openForm();

    expect(form.getStorageKey()).toBeNull();
    await typeAndAutosave(form, "vssbp", "141");
    expect(localStorage.length).toBe(0);
  });

  it("uses no fallback study, subject or visit", () => {
    signIn("user-a", "tenant-1");
    const noSubject = useEcrfDrafts(
      ref("STUDY-1"),
      ref(""),
      ref("Screening"),
      reactive({})
    );
    const noStudy = useEcrfDrafts(
      ref(null),
      ref("SUBJ-001"),
      ref("Screening"),
      reactive({})
    );
    expect(noSubject.getStorageKey()).toBeNull();
    expect(noStudy.getStorageKey()).toBeNull();
  });

  it("purges every account's drafts and the in-memory form on sign-out", async () => {
    const auth = signIn("user-a", "tenant-1");
    await typeAndAutosave(openForm(), "vssbp", "141");
    const backed = useEcrfForm();
    await backed.load(BACKED_FORM_ID);
    backed.entries.SYSBP.text = "144";
    await nextTick();
    vi.advanceTimersByTime(600);
    expect(
      localStorage.getItem(backed.getDraftStorageKey(BACKED_FORM_ID))
    ).not.toBeNull();
    const clinical = useClinicalStore();
    clinical.formValues.vssbp = "141";
    localStorage.setItem(
      getStorageKey("formValues"),
      JSON.stringify({ vssbp: "141" })
    );
    localStorage.setItem("cadence_user:tenant-9:user-z:ecrf_draft:S:P:V", "{}");
    localStorage.setItem(
      `cadence_user:tenant-9:user-z:ecrf_form_draft:${BACKED_FORM_ID}`,
      "{}"
    );
    localStorage.setItem("user_locale", "en");

    await auth.logout();

    const remaining = storedKeys();
    expect(remaining).toEqual(["user_locale"]);
    expect(clinical.formValues.vssbp).not.toBe("141");
  });

  it("drops a pending autosave when the account changes before it fires", async () => {
    signIn("user-a", "tenant-1");
    const form = openForm();
    form.formValues.vssbp = "141";
    const backed = useEcrfForm();
    await backed.load(BACKED_FORM_ID);
    backed.entries.SYSBP.text = "144";
    await nextTick();

    // The debounce has not fired yet when B signs in.
    signIn("user-b", "tenant-1");
    vi.advanceTimersByTime(600);
    await nextTick();

    expect(localStorage.length).toBe(0);
  });

  it("restores the clinical store's form values only for the account that saved them", async () => {
    signIn("user-a", "tenant-1", ["Sponsor Admin"]);
    const clinical = useClinicalStore();
    clinical.formValues.vssbp = "141";
    await clinical.addLedgerBlock("TEST", { vssbp: "141" }, "test");
    const persona = useAuthStore().currentPersona;
    expect(
      localStorage.getItem(getStorageKey("formValues", persona))
    ).toContain("141");

    signIn("user-b", "tenant-1", ["Sponsor Admin"]);

    expect(clinical.formValues.vssbp).not.toBe("141");
    expect(storedKeys().some((k) => k.includes("user-a"))).toBe(false);
  });

  it("purgeUserScopedStorage keeps only the given account and unrelated keys", () => {
    localStorage.setItem("cadence_user:t1:a:ecrf_draft:x", "1");
    localStorage.setItem("cadence_user:t1:b:ecrf_draft:x", "1");
    localStorage.setItem("cadence_user:t2:a:ecrf_draft:x", "1");
    localStorage.setItem("onboarding_site_crc_disabled", "true");

    const removed = purgeUserScopedStorage({ tenantId: "t1", userId: "a" });

    expect(removed).toBe(2);
    expect(storedKeys()).toEqual([
      "cadence_user:t1:a:ecrf_draft:x",
      "onboarding_site_crc_disabled",
    ]);
  });
});
