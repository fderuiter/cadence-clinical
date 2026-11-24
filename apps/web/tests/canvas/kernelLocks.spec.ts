import { beforeEach, describe, expect, it, vi } from "vitest";
import { kernelApi, type KernelLock } from "../../src/api/kernelApi";
import {
  fetchStudyLocks,
  toAssertionLockRef,
  toCoveringScopeLocks,
  toDirectScopeLocks,
} from "../../src/canvas/kernelLocks";

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    listStudyLocks: vi.fn(),
  },
}));

const STUDY_REF = "study:01960000-0000-7000-8000-000000000001";
const ENROLLMENT_REF = "enrollment:01960000-0000-7000-8000-000000000002";
const FORM_REF = "collection_instance:01960000-0000-7000-8000-000000000003";
const OBS_REF = "observation:01960000-0000-7000-8000-000000000004";
const LOCK_UUID = "01960000-0000-7000-8000-000000000005";

describe("kernelLocks utilities", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (kernelApi as { listStudyLocks?: unknown }).listStudyLocks = vi.fn();
  });

  describe("toAssertionLockRef", () => {
    it("normalizes bare UUIDs and preserves canonical assertion: prefixes", () => {
      expect(toAssertionLockRef(LOCK_UUID)).toBe(`assertion:${LOCK_UUID}`);
      expect(toAssertionLockRef(`assertion:${LOCK_UUID}`)).toBe(
        `assertion:${LOCK_UUID}`
      );
      expect(toAssertionLockRef("")).toBe("");
      expect(toAssertionLockRef(null)).toBe("");
      expect(toAssertionLockRef(undefined)).toBe("");
    });
  });

  describe("fetchStudyLocks", () => {
    it("returns an empty array without calling API when studyRef is empty or listStudyLocks is absent", async () => {
      await expect(fetchStudyLocks(null)).resolves.toEqual([]);
      await expect(fetchStudyLocks("")).resolves.toEqual([]);
      expect(kernelApi.listStudyLocks).not.toHaveBeenCalled();

      (kernelApi as { listStudyLocks?: unknown }).listStudyLocks = undefined;
      await expect(fetchStudyLocks(STUDY_REF)).resolves.toEqual([]);
    });

    it("queries listStudyLocks with optional parameters and defaults missing locks to []", async () => {
      const sampleLock = {
        ref: `assertion:${LOCK_UUID}`,
        study_ref: STUDY_REF,
        scope_ref: ENROLLMENT_REF,
        scope_kind: "enrollment",
        kind: "Frozen",
        masked: false,
        released: false,
      } as KernelLock;

      vi.mocked(kernelApi.listStudyLocks)
        .mockResolvedValueOnce({
          study_ref: STUDY_REF,
          study_code: "CAD-101",
          valid_at: "2026-06-01T10:00:00Z",
          known_at: "2026-06-01T10:00:00Z",
          locks: [sampleLock],
          hidden: { more_exist: false, reasons: [] },
        })
        .mockResolvedValueOnce({} as never);

      await expect(
        fetchStudyLocks(STUDY_REF, {
          activeOnly: true,
          asOf: "2026-06-01T10:00:00Z",
        })
      ).resolves.toEqual([sampleLock]);

      expect(kernelApi.listStudyLocks).toHaveBeenCalledWith(STUDY_REF, {
        activeOnly: true,
        asOf: "2026-06-01T10:00:00Z",
      });

      await expect(fetchStudyLocks(STUDY_REF)).resolves.toEqual([]);
    });
  });

  describe("toDirectScopeLocks", () => {
    it("filters held form/observation locks by target scope and prefixes assertion:", () => {
      const held = [
        { lock_id: LOCK_UUID, kind: "Frozen", scope: FORM_REF },
        {
          lock_id: `assertion:${LOCK_UUID}-2`,
          kind: "Locked",
          scope: FORM_REF,
        },
        { lock_id: `${LOCK_UUID}-other`, kind: "Locked", scope: OBS_REF },
      ];

      expect(toDirectScopeLocks(held, FORM_REF)).toEqual([
        { lockRef: `assertion:${LOCK_UUID}`, kind: "Frozen" },
        { lockRef: `assertion:${LOCK_UUID}-2`, kind: "Locked" },
      ]);
      expect(toDirectScopeLocks(null, FORM_REF)).toEqual([]);
      expect(toDirectScopeLocks(held, "")).toEqual([]);
    });

    it("filters study KernelLock records to active, unmasked locks matching target scope", () => {
      const studyLocks: KernelLock[] = [
        {
          ref: `assertion:${LOCK_UUID}`,
          study_ref: STUDY_REF,
          scope_ref: ENROLLMENT_REF,
          scope_kind: "enrollment",
          study_site_ref: null,
          enrollment_ref: ENROLLMENT_REF,
          subject_code: "SUBJ-A",
          kind: "Frozen",
          attestation_ref: null,
          attestation_meaning: "Approved",
          locked_at: "2026-06-01T09:30:00Z",
          locked_by: "dm@cadence.test",
          reason_for_change: "Freeze",
          released: false,
          release_ref: null,
          released_at: null,
          released_by: null,
          release_reason: null,
          masked: false,
          mask: null,
        },
        {
          ref: `assertion:${LOCK_UUID}-released`,
          study_ref: STUDY_REF,
          scope_ref: ENROLLMENT_REF,
          scope_kind: "enrollment",
          study_site_ref: null,
          enrollment_ref: ENROLLMENT_REF,
          subject_code: "SUBJ-A",
          kind: "Locked",
          attestation_ref: null,
          attestation_meaning: "Approved",
          locked_at: "2026-06-01T09:30:00Z",
          locked_by: "dm@cadence.test",
          reason_for_change: "Lock",
          released: true,
          release_ref: "assertion:rel",
          released_at: "2026-06-01T10:00:00Z",
          released_by: "dm@cadence.test",
          release_reason: "Reopen",
          masked: false,
          mask: null,
        },
        {
          ref: null,
          study_ref: STUDY_REF,
          scope_ref: ENROLLMENT_REF,
          scope_kind: "enrollment",
          study_site_ref: null,
          enrollment_ref: null,
          subject_code: null,
          kind: "Locked",
          attestation_ref: null,
          attestation_meaning: null,
          locked_at: null,
          locked_by: null,
          reason_for_change: null,
          released: false,
          release_ref: null,
          released_at: null,
          released_by: null,
          release_reason: null,
          masked: true,
          mask: "NotPermitted",
        },
      ];

      expect(toDirectScopeLocks(studyLocks, ENROLLMENT_REF)).toEqual([
        { lockRef: `assertion:${LOCK_UUID}`, kind: "Frozen" },
      ]);
    });
  });

  describe("toCoveringScopeLocks", () => {
    it("extracts active, unmasked ancestor locks whose scope differs from targetScope", () => {
      const held = [
        { lock_id: LOCK_UUID, kind: "Frozen", scope: FORM_REF },
        { lock_id: `${LOCK_UUID}-study`, kind: "Locked", scope: STUDY_REF },
      ];
      expect(toCoveringScopeLocks(held, FORM_REF)).toEqual([
        { kind: "Locked", scope: STUDY_REF },
      ]);
      expect(toCoveringScopeLocks(null, FORM_REF)).toEqual([]);
      expect(toCoveringScopeLocks(held, "")).toEqual([]);

      const studyLocks = [
        {
          ref: `assertion:${LOCK_UUID}`,
          kind: "Frozen",
          scope_ref: STUDY_REF,
          masked: false,
          released: false,
        },
        {
          ref: `assertion:${LOCK_UUID}-direct`,
          kind: "Locked",
          scope_ref: ENROLLMENT_REF,
          masked: false,
          released: false,
        },
        {
          ref: `assertion:${LOCK_UUID}-rel`,
          kind: "Locked",
          scope_ref: STUDY_REF,
          masked: false,
          released: true,
        },
        {
          ref: null,
          kind: "Locked",
          scope_ref: STUDY_REF,
          masked: true,
          released: false,
        },
      ];
      expect(toCoveringScopeLocks(studyLocks, ENROLLMENT_REF)).toEqual([
        { kind: "Frozen", scope: STUDY_REF },
      ]);
    });
  });
});
