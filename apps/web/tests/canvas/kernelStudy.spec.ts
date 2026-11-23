import { describe, it, expect, vi, beforeEach } from "vitest";
import { runLogoutCleanups } from "../../src/auth/sessionCoordinator.js";
import { kernelApi } from "../../src/api/kernelApi";
import {
  clearKernelStudyCache,
  findKernelEnrollmentRef,
  findKernelStudyRef,
  findStudySites,
  resetKernelStudySession,
} from "../../src/canvas/kernelStudy";

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: { search: vi.fn(), getChildren: vi.fn() },
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
