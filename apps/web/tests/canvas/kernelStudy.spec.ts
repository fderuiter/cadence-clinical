import { describe, it, expect, vi, beforeEach } from "vitest";
import { kernelApi } from "../../src/api/kernelApi";
import {
  findKernelEnrollmentRef,
  findStudySites,
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
      { code: "SITE-17", title: "Site SITE-17" },
      { code: "SITE-22", title: "SITE-22 — Chicago Medical Center" },
    ]);
  });

  it("returns an empty list when the study is unknown", async () => {
    vi.mocked(kernelApi.search).mockResolvedValue([]);
    await expect(findStudySites("CAD-404")).resolves.toEqual([]);
  });
});
