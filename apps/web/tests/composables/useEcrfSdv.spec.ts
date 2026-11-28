import { describe, it, expect, vi, beforeEach } from "vitest";
import { computed, ref } from "vue";
import { ApiError } from "../../src/api/client";
import type {
  EcrfForm,
  EcrfFormAssertion,
  EcrfFormItem,
} from "../../src/api/ecrfFormsApi";
import { kernelApi } from "../../src/api/kernelApi";
import { useEcrfSdv } from "../../src/composables/useEcrfSdv";

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/kernelApi")>();
  return {
    ...actual,
    kernelApi: {
      ...actual.kernelApi,
      verifyObservation: vi.fn(),
      retractAssertion: vi.fn(),
    },
  };
});

function makeItem(overrides: Partial<EcrfFormItem> = {}): EcrfFormItem {
  return {
    item_id: "SYSBP",
    concept: "C25298@1",
    unit: "mm[Hg]",
    value_kind: "Quantity",
    required: true,
    value: { type: "quantity", value: 120, unit: "mm[Hg]" },
    observation_id: "obs-1",
    revision: 3,
    recorded_by: "crc1",
    recorded_at: "2027-03-14T09:05:00Z",
    ...overrides,
  };
}

function makeAssertion(
  overrides: Partial<EcrfFormAssertion> = {}
): EcrfFormAssertion {
  return {
    assertion_id: "assertion-1",
    kind: "Verified",
    target: "observation:obs-1",
    revision: 3,
    text: null,
    asserted_at: "2027-03-14T11:00:00Z",
    asserted_by: "cra1",
    reason: "Checked against chart",
    retracted_at: null,
    ...overrides,
  };
}

function makeForm(overrides: Partial<EcrfForm> = {}): EcrfForm {
  return {
    form_id: "form-1",
    enrollment_id: "enrollment-1",
    participant_id: "participant-1",
    study_id: "study-1",
    study_site_id: "site-1",
    protocol_version_id: "pv-1",
    collection_spec_id: "VS",
    planned_activity_id: "WEEK2",
    collected_at: "2027-03-14T09:00:00Z",
    opened_by: "crc1",
    opened_at: "2027-03-14T09:00:00Z",
    version: 1,
    can_sign: false,
    open_query_count: 0,
    answered_query_count: 0,
    open_error_finding_count: 0,
    complete: false,
    missing_required: [],
    items: [makeItem()],
    locks: [],
    assertions: [],
    signatures: [],
    content_hash: "sha256:v1",
    ...overrides,
  };
}

describe("useEcrfSdv indexed verification and selection lookups", () => {
  beforeEach(() => {
    vi.mocked(kernelApi.verifyObservation).mockReset();
    vi.mocked(kernelApi.retractAssertion).mockReset();
  });

  it("matches observation:<id> and observation:<id>@<version> targets without prefix collisions", () => {
    const itemObs1 = makeItem({
      item_id: "ITEM_1",
      observation_id: "obs-1",
      revision: 2,
    });
    const itemObs10 = makeItem({
      item_id: "ITEM_10",
      observation_id: "obs-10",
      revision: 2,
    });
    const form = ref<EcrfForm | null>(
      makeForm({
        items: [itemObs1, itemObs10],
        assertions: [
          makeAssertion({
            assertion_id: "a-obs-10",
            target: "observation:obs-10@2",
            revision: 2,
          }),
          makeAssertion({
            assertion_id: "a-non-obs",
            target: "collection_instance:obs-1",
            revision: 2,
          }),
          makeAssertion({
            assertion_id: "a-empty-obs",
            target: "observation:@2",
            revision: 2,
          }),
        ],
      })
    );
    const items = computed(() => form.value?.items ?? []);
    const sdv = useEcrfSdv({ form, items, reload: vi.fn() });

    expect(sdv.activeVerification(itemObs1)).toBeNull();
    expect(sdv.needsReverification(itemObs1)).toBe(false);
    expect(sdv.activeVerification(itemObs10)?.assertion_id).toBe("a-obs-10");
    expect(sdv.unverifiedItems.value.map((i) => i.item_id)).toEqual(["ITEM_1"]);
  });

  it("returns the newest unretracted verified assertion for duplicate (observation_id, revision) pairs", () => {
    const item = makeItem({ observation_id: "obs-1", revision: 3 });
    const form = ref<EcrfForm | null>(
      makeForm({
        items: [item],
        assertions: [
          makeAssertion({
            assertion_id: "a-older",
            kind: "verified",
            target: "observation:obs-1",
            revision: 3,
          }),
          makeAssertion({
            assertion_id: "a-newer",
            kind: "VERIFIED",
            target: "observation:obs-1@3",
            revision: "3",
          }),
          makeAssertion({
            assertion_id: "a-retracted-newest",
            kind: "Verified",
            target: "observation:obs-1",
            revision: 3,
            retracted_at: "2027-03-14T12:00:00Z",
          }),
          makeAssertion({
            assertion_id: "a-query",
            kind: "Queried",
            target: "observation:obs-1",
            revision: 3,
          }),
        ],
      })
    );
    const items = computed(() => form.value?.items ?? []);
    const sdv = useEcrfSdv({ form, items, reload: vi.fn() });

    expect(sdv.activeVerification(item)?.assertion_id).toBe("a-newer");
    expect(sdv.needsReverification(item)).toBe(false);
    expect(sdv.unverifiedItems.value).toEqual([]);
  });

  it("preserves needsReverification edge cases for retracted assertions, unrecorded items, null revisions, and non-numeric revisions", () => {
    const staleVerifiedItem = makeItem({
      item_id: "STALE_ITEM",
      observation_id: "obs-stale",
      revision: 4,
    });
    const retractedOnlyItem = makeItem({
      item_id: "RETRACTED_ITEM",
      observation_id: "obs-retracted",
      revision: 4,
    });
    const nullRevisionItem = makeItem({
      item_id: "NULL_REV_ITEM",
      observation_id: "obs-null-rev",
      revision: 4,
    });
    const unrecordedItem = makeItem({
      item_id: "UNRECORDED_ITEM",
      observation_id: "obs-unrecorded",
      value: null,
      revision: 4,
    });

    const form = ref<EcrfForm | null>(
      makeForm({
        items: [
          staleVerifiedItem,
          retractedOnlyItem,
          nullRevisionItem,
          unrecordedItem,
        ],
        assertions: [
          makeAssertion({
            assertion_id: "a-stale",
            target: "observation:obs-stale",
            revision: 2,
          }),
          makeAssertion({
            assertion_id: "a-retracted",
            target: "observation:obs-retracted",
            revision: 2,
            retracted_at: "2027-03-14T11:30:00Z",
          }),
          makeAssertion({
            assertion_id: "a-null-rev",
            target: "observation:obs-null-rev",
            revision: null,
          }),
          makeAssertion({
            assertion_id: "a-nan-rev",
            target: "observation:obs-null-rev",
            revision: "not-a-number",
          }),
          makeAssertion({
            assertion_id: "a-unrecorded",
            target: "observation:obs-unrecorded",
            revision: 1,
          }),
        ],
      })
    );
    const items = computed(() => form.value?.items ?? []);
    const sdv = useEcrfSdv({ form, items, reload: vi.fn() });

    expect(sdv.needsReverification(staleVerifiedItem)).toBe(true);
    expect(sdv.needsReverification(retractedOnlyItem)).toBe(false);
    expect(sdv.needsReverification(nullRevisionItem)).toBe(false);
    expect(sdv.needsReverification(unrecordedItem)).toBe(false);
    expect(sdv.activeVerification(unrecordedItem)).toBeNull();
    expect(sdv.unverifiedItems.value.map((i) => i.item_id)).toEqual([
      "STALE_ITEM",
      "RETRACTED_ITEM",
      "NULL_REV_ITEM",
    ]);
  });

  it("invalidates the computed assertion index immediately when form.value is replaced", () => {
    const item = makeItem({ observation_id: "obs-1", revision: 2 });
    const form = ref<EcrfForm | null>(
      makeForm({ items: [item], assertions: [] })
    );
    const items = computed(() => form.value?.items ?? []);
    const sdv = useEcrfSdv({ form, items, reload: vi.fn() });

    expect(sdv.activeVerification(item)).toBeNull();
    expect(sdv.unverifiedItems.value).toHaveLength(1);

    form.value = makeForm({
      version: 2,
      items: [item],
      assertions: [
        makeAssertion({
          assertion_id: "a-v2",
          target: "observation:obs-1",
          revision: 2,
        }),
      ],
    });

    expect(sdv.activeVerification(item)?.assertion_id).toBe("a-v2");
    expect(sdv.unverifiedItems.value).toHaveLength(0);

    const bumpedItem = makeItem({ observation_id: "obs-1", revision: 3 });
    form.value = makeForm({
      version: 3,
      items: [bumpedItem],
      assertions: [
        makeAssertion({
          assertion_id: "a-v2",
          target: "observation:obs-1",
          revision: 2,
        }),
      ],
    });

    expect(sdv.activeVerification(bumpedItem)).toBeNull();
    expect(sdv.needsReverification(bumpedItem)).toBe(true);
    expect(sdv.unverifiedItems.value).toHaveLength(1);
  });

  it("keeps isSdvSelected synchronized with selection controls and bulk verification outcomes", async () => {
    const itemA = makeItem({
      item_id: "SYSBP",
      observation_id: "obs-a",
      revision: 1,
    });
    const itemB = makeItem({
      item_id: "DIABP",
      observation_id: "obs-b",
      revision: 1,
    });
    const form = ref<EcrfForm | null>(
      makeForm({ items: [itemA, itemB], assertions: [] })
    );
    const items = computed(() => form.value?.items ?? []);
    const reload = vi.fn().mockResolvedValue(undefined);
    const sdv = useEcrfSdv({ form, items, reload });

    expect(sdv.isSdvSelected("SYSBP")).toBe(false);
    expect(sdv.isSdvSelected("DIABP")).toBe(false);

    sdv.selectAllUnverifiedSdv();
    expect(sdv.selectedSdvItems.value).toEqual(["SYSBP", "DIABP"]);
    expect(sdv.isSdvSelected("SYSBP")).toBe(true);
    expect(sdv.isSdvSelected("DIABP")).toBe(true);

    sdv.toggleSdvItem("SYSBP", false);
    expect(sdv.isSdvSelected("SYSBP")).toBe(false);
    expect(sdv.isSdvSelected("DIABP")).toBe(true);

    sdv.toggleSdvItem("SYSBP", true);
    sdv.toggleSdvItem("SYSBP", true);
    expect(sdv.selectedSdvItems.value).toEqual(["DIABP", "SYSBP"]);
    expect(sdv.isSdvSelected("SYSBP")).toBe(true);

    vi.mocked(kernelApi.verifyObservation)
      .mockResolvedValueOnce({
        ref: "assertion:1",
        kind: "Verified",
        target: "observation:obs-a",
        revision: 1,
        retracts: null,
        asserted_at: "2027-03-14T11:00:00Z",
        asserted_by: "cra1",
      })
      .mockRejectedValueOnce(
        new ApiError("Conflict", 409, null, {
          code: "K.Assertion.RevisionStale",
        })
      );

    sdv.setBulkSdvReason("Source chart checked");
    await sdv.submitBulkSdv();

    expect(sdv.selectedSdvItems.value).toEqual(["DIABP"]);
    expect(sdv.isSdvSelected("SYSBP")).toBe(false);
    expect(sdv.isSdvSelected("DIABP")).toBe(true);

    sdv.clearSdvSelection();
    expect(sdv.selectedSdvItems.value).toEqual([]);
    expect(sdv.isSdvSelected("DIABP")).toBe(false);
  });

  it("excludes NullFlavor.NotAsked placeholders from active SDV queries and unverifiedItems (K-FACT-3)", () => {
    const notAskedItem = makeItem({
      item_id: "NOT_ASKED_ITEM",
      observation_id: "obs-not-asked",
      value: { type: "missing", reason: "NotAsked" },
      revision: 2,
    });
    const notDoneItem = makeItem({
      item_id: "NOT_DONE_ITEM",
      observation_id: "obs-not-done",
      value: {
        type: "missing",
        reason: "NotDone",
        detail: "Participant declined orthostatic stand",
      },
      revision: 1,
    });
    const form = ref<EcrfForm | null>(
      makeForm({
        items: [notAskedItem, notDoneItem],
        assertions: [
          makeAssertion({
            assertion_id: "a-not-asked-r1",
            target: "observation:obs-not-asked",
            revision: 1,
          }),
          makeAssertion({
            assertion_id: "a-not-asked-r2",
            target: "observation:obs-not-asked",
            revision: 2,
          }),
        ],
      })
    );
    const items = computed(() => form.value?.items ?? []);
    const sdv = useEcrfSdv({ form, items, reload: vi.fn() });

    expect(sdv.hasRecordedValue(notAskedItem)).toBe(false);
    expect(sdv.activeVerification(notAskedItem)).toBeNull();
    expect(sdv.needsReverification(notAskedItem)).toBe(false);
    expect(sdv.hasRecordedValue(notDoneItem)).toBe(true);
    expect(sdv.unverifiedItems.value.map((i) => i.item_id)).toEqual([
      "NOT_DONE_ITEM",
    ]);
  });
});
