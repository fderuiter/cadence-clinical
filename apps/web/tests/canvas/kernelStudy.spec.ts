import { describe, it, expect, vi, beforeEach } from "vitest";
import { runLogoutCleanups } from "../../src/auth/sessionCoordinator.js";
import { kernelApi } from "../../src/api/kernelApi";
import {
  clearKernelStudyCache,
  findEnrollmentScheduleOptions,
  findKernelEnrollmentRef,
  findKernelStudyRef,
  findStudySites,
  resetKernelStudySession,
} from "../../src/canvas/kernelStudy";

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    search: vi.fn(),
    getChildren: vi.fn(),
    getEnrollmentScheduleOfActivities: vi.fn(),
  },
}));

const STUDY_REF = "study:0192f3c4-5e6a-7b8c-9d0e-00000000c001";
const SITE_A = "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000c002";
const SITE_B = "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000c003";
const ENROLLMENT_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000c004";

function child(kind: string, ref: string | null, code: string) {
  return {
    node: { ref, kind, title: code, code, status: null, mask: null },
    child_count: null,
    group: null,
  };
}

describe("findKernelEnrollmentRef (#5461)", () => {
  beforeEach(() => {
    clearKernelStudyCache();
    vi.mocked(kernelApi.search).mockReset();
    vi.mocked(kernelApi.getChildren).mockReset();
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: STUDY_REF,
        kind: "study",
        title: "Study",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.getChildren).mockImplementation(async (ref) => {
      if (ref === STUDY_REF)
        return [
          child("study_site", SITE_A, "017"),
          child("study_site", SITE_B, "018"),
        ];
      if (ref === SITE_A)
        return [
          child("enrollment", `enrollment:${ENROLLMENT_ID}@2`, "017-0001"),
          child(
            "enrollment",
            "enrollment:0192f3c4-5e6a-7b8c-9d0e-00000000c005@1",
            "017-0002"
          ),
        ];
      return [
        child(
          "enrollment",
          "enrollment:0192f3c4-5e6a-7b8c-9d0e-00000000c006@1",
          "017-0001"
        ),
      ];
    });
  });

  it("walks study, study site and enrollment by code", async () => {
    expect(await findKernelEnrollmentRef("CAD-101", "017", "017-0001")).toEqual(
      {
        kind: "enrollment",
        id: ENROLLMENT_ID,
        version: 2,
      }
    );
    expect(kernelApi.search).toHaveBeenCalledWith({
      text: "CAD-101",
      kinds: ["study"],
      limit: 5,
    });
    expect(kernelApi.getChildren).not.toHaveBeenCalledWith(SITE_B);
  });

  it("returns null rather than guessing between two enrollments with the code", async () => {
    expect(
      await findKernelEnrollmentRef("CAD-101", null, "017-0001")
    ).toBeNull();
  });

  it("returns null when the kernel has no such study or enrollment", async () => {
    expect(
      await findKernelEnrollmentRef("CAD-101", "017", "017-0099")
    ).toBeNull();
    vi.mocked(kernelApi.search).mockResolvedValue([]);
    expect(
      await findKernelEnrollmentRef("CAD-999", "017", "017-0001")
    ).toBeNull();
  });
});

describe("findStudySites", () => {
  beforeEach(() => {
    clearKernelStudyCache();
    vi.mocked(kernelApi.search).mockReset();
    vi.mocked(kernelApi.getChildren).mockReset();
  });

  it("returns visible deduplicated study sites sorted by site code and omits masked sites", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: STUDY_REF,
        kind: "study",
        title: "Study",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.getChildren).mockResolvedValue([
      {
        node: {
          ref: SITE_B,
          kind: "study_site",
          title: "Chicago Medical Center",
          code: "SITE-22",
          status: null,
          mask: null,
        },
        child_count: 3,
        group: null,
      },
      child("study_site", SITE_A, "SITE-17"),
      child("study_site", SITE_A, "SITE-17"),
      {
        node: {
          ref: null,
          kind: "study_site",
          title: null,
          code: "SITE-99",
          status: null,
          mask: "NotPermitted",
        },
        child_count: null,
        group: null,
      },
      child("protocol_version", "protocol_version:v1", "1.0"),
    ]);

    await expect(findStudySites("CAD-101")).resolves.toEqual([
      { code: "SITE-17", label: "SITE-17" },
      { code: "SITE-22", label: "SITE-22 — Chicago Medical Center" },
    ]);
  });

  it("returns an empty list when the study is unknown", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([]);
    await expect(findStudySites("CAD-404")).resolves.toEqual([]);
  });
});

describe("findEnrollmentScheduleOptions (PRD-SUB-007)", () => {
  beforeEach(() => {
    vi.mocked(kernelApi.getEnrollmentScheduleOfActivities).mockReset();
  });

  it("normalizes enrollment refs and maps governing version, schedule, and pending re-consent transitions", async () => {
    vi.mocked(kernelApi.getEnrollmentScheduleOfActivities).mockResolvedValue({
      enrollment_ref: `enrollment:${ENROLLMENT_ID}`,
      governing_version: {
        ref: "protocol_version:0192f3c4-5e6a-7b8c-9d0e-00000000b001",
        study: STUDY_REF,
        number: 1,
        parent_version: null,
        status: "Superseded",
        content_hash: "sha256:v1",
        amendment_intent: null,
        approval_attestation: null,
        withdrawal_reason: null,
        mask: null,
      },
      schedule_of_activities: {
        version: "protocol_version:0192f3c4-5e6a-7b8c-9d0e-00000000b001",
        content_hash: "sha256:soa-v1",
        hash: "sha256:soa-v1",
        title: "CAD-101 Protocol v1.0",
        epochs: [],
        columns: [
          {
            activity_id: "pa.screening",
            name: "Screening",
            epoch_id: "screening",
            study_day: -7,
            window_before: 3,
            window_after: 0,
            locations: [],
            footnotes: [],
          },
        ],
        rows: [
          {
            collection_spec_id: "cs.vitals",
            name: "Vital Signs",
            item_ids: ["sysbp"],
            concepts: ["concept:sysbp@1"],
            columns: [0],
            frequency: 1,
            footnotes: [],
          },
        ],
        footnotes: [],
      },
      requires_reconsent: true,
      is_inconsistent: false,
      inconsistency_reason: null,
      pending_transitions: [
        {
          target_version_ref:
            "protocol_version:0192f3c4-5e6a-7b8c-9d0e-00000000b002",
          target_version_number: 2,
          amendment_intent: "Safety ECG monitoring",
          source_effectivity_ref:
            "effectivity:0192f3c4-5e6a-7b8c-9d0e-00000000e901",
          required_since: "2026-10-15T12:00:00Z",
        },
      ],
    });

    const result = await findEnrollmentScheduleOptions(`${ENROLLMENT_ID}@2`);
    expect(kernelApi.getEnrollmentScheduleOfActivities).toHaveBeenCalledWith(
      `enrollment:${ENROLLMENT_ID}`,
      undefined
    );
    expect(result).not.toBeNull();
    expect(result?.governingVersionNumber).toBe(1);
    expect(result?.schedule?.title).toBe("CAD-101 Protocol v1.0");
    expect(result?.requiresReconsent).toBe(true);
    expect(result?.pendingTransitions).toEqual([
      {
        targetVersionRef:
          "protocol_version:0192f3c4-5e6a-7b8c-9d0e-00000000b002",
        targetVersionNumber: 2,
        amendmentIntent: "Safety ECG monitoring",
        sourceEffectivityRef:
          "effectivity:0192f3c4-5e6a-7b8c-9d0e-00000000e901",
        requiredSince: "2026-10-15T12:00:00Z",
      },
    ]);
  });
});

describe("kernelStudy session cache", () => {
  beforeEach(() => {
    clearKernelStudyCache();
    vi.mocked(kernelApi.search).mockReset();
    vi.mocked(kernelApi.getChildren).mockReset();
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: STUDY_REF,
        kind: "study",
        title: "Study",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.getChildren).mockImplementation(async (ref) => {
      if (ref === STUDY_REF) {
        return [child("study_site", SITE_A, "017")];
      }
      return [child("enrollment", `enrollment:${ENROLLMENT_ID}@2`, "017-0001")];
    });
  });

  it("reuses positive lookups within a study session and invalidates on study switch or logout", async () => {
    resetKernelStudySession("CAD-101");

    await expect(findKernelStudyRef("CAD-101")).resolves.toBe(STUDY_REF);
    await expect(findStudySites("CAD-101")).resolves.toEqual([
      { code: "017", label: "017" },
    ]);
    await expect(findStudySites("CAD-101")).resolves.toEqual([
      { code: "017", label: "017" },
    ]);
    await expect(
      findKernelEnrollmentRef("CAD-101", "017", "017-0001")
    ).resolves.toEqual({
      kind: "enrollment",
      id: ENROLLMENT_ID,
      version: 2,
    });
    await expect(
      findKernelEnrollmentRef("CAD-101", "017", "017-0001")
    ).resolves.toEqual({
      kind: "enrollment",
      id: ENROLLMENT_ID,
      version: 2,
    });

    expect(kernelApi.search).toHaveBeenCalledTimes(1);

    // Calling resetKernelStudySession with the same study preserves the warm cache.
    resetKernelStudySession("CAD-101");
    await findKernelStudyRef("CAD-101");
    expect(kernelApi.search).toHaveBeenCalledTimes(1);

    // Switching studies clears the cache.
    resetKernelStudySession("CAD-202");
    await findKernelStudyRef("CAD-101");
    expect(kernelApi.search).toHaveBeenCalledTimes(2);

    // Running logout cleanups also clears the cache.
    runLogoutCleanups();
    await findKernelStudyRef("CAD-101");
    expect(kernelApi.search).toHaveBeenCalledTimes(3);
  });

  it("never caches null, empty, or rejected lookups", async () => {
    vi.mocked(kernelApi.search)
      .mockResolvedValueOnce([])
      .mockRejectedValueOnce(new Error("Temporary 503"))
      .mockResolvedValueOnce([
        {
          ref: STUDY_REF,
          kind: "study",
          title: "Study",
          code: "CAD-101",
          detail: null,
          exact_code: true,
          rank: 1,
        },
      ]);

    await expect(findKernelStudyRef("CAD-101")).resolves.toBeNull();
    await expect(findKernelStudyRef("CAD-101")).rejects.toThrow(
      "Temporary 503"
    );
    await expect(findKernelStudyRef("CAD-101")).resolves.toBe(STUDY_REF);
    expect(kernelApi.search).toHaveBeenCalledTimes(3);
  });
});
