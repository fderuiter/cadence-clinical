import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../../src/api/client";
import { runLogoutCleanups } from "../../src/auth/sessionCoordinator.js";
import { kernelApi } from "../../src/api/kernelApi";
import {
  clearKernelStudyCache,
  findKernelEnrollmentRef,
  findEnrollmentScheduleOptions,
  findKernelStudyRef,
  findStudyScheduleOptions,
  findStudySites,
  resetKernelStudySession,
} from "../../src/canvas/kernelStudy";
import {
  clearNodeResolutionCache,
  resetNodeResolutionSession,
} from "../../src/canvas/nodeResolver";
import { useAuthStore } from "../../src/stores/auth";
import { useStudyCanvasStore } from "../../src/stores/studyCanvas";

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    search: vi.fn(),
    getChildren: vi.fn(),
    lookupEnrollment: vi.fn(),
    listStudySites: vi.fn(),
    listProtocolVersions: vi.fn(),
    getScheduleOfActivities: vi.fn(),
    getEnrollmentSchedule: vi.fn(),
  },
}));

const STUDY_REF = "study:0192f3c4-5e6a-7b8c-9d0e-00000000c001@1";
const STUDY_REF_B = "study:0192f3c4-5e6a-7b8c-9d0e-00000000c999@1";
const SITE_A = "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000c002@1";
const SITE_B = "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000c003@1";
const ENROLLMENT_ID = "0192f3c4-5e6a-7b8c-9d0e-00000000c004";

function child(kind: string, ref: string | null, code: string) {
  return {
    node: { ref, kind, title: code, code, status: null, mask: null },
    child_count: null,
    group: null,
  };
}

function notFoundError() {
  return new ApiError(
    "No visible enrollment matches the requested codes in your tenant.",
    404,
    "Not found",
    {
      title: "Not found",
      detail:
        "No visible enrollment matches the requested codes in your tenant.",
    }
  );
}

describe("findKernelEnrollmentRef", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    clearKernelStudyCache();
    vi.mocked(kernelApi.search).mockReset();
    vi.mocked(kernelApi.getChildren).mockReset();
    vi.mocked(kernelApi.lookupEnrollment).mockReset();
  });

  it("resolves an uncached participant in 1 lookup request without walking getChildren and warms the study cache", async () => {
    vi.mocked(kernelApi.lookupEnrollment).mockResolvedValueOnce({
      study_ref: STUDY_REF,
      study_code: "CAD-101",
      study_title: "Study",
      study_site_ref: SITE_A,
      site_code: "017",
      site_title: "Site 017",
      enrollment_ref: `enrollment:${ENROLLMENT_ID}@2`,
      subject_code: "017-0001",
      status: "Enrolled",
    });

    await expect(
      findKernelEnrollmentRef("CAD-101", "017", "017-0001")
    ).resolves.toEqual({
      kind: "enrollment",
      id: ENROLLMENT_ID,
      version: 2,
    });

    expect(kernelApi.lookupEnrollment).toHaveBeenCalledTimes(1);
    expect(kernelApi.lookupEnrollment).toHaveBeenCalledWith({
      studyCode: "CAD-101",
      subjectCode: "017-0001",
      siteCode: "017",
    });
    expect(kernelApi.getChildren).not.toHaveBeenCalled();
    expect(kernelApi.search).not.toHaveBeenCalled();

    // Subsequent calls with or without siteCode hit the positive session cache, and findKernelStudyRef reuses study_ref.
    await expect(
      findKernelEnrollmentRef("CAD-101", null, "017-0001")
    ).resolves.toEqual({
      kind: "enrollment",
      id: ENROLLMENT_ID,
      version: 2,
    });
    await expect(findKernelStudyRef("CAD-101")).resolves.toBe(STUDY_REF);
    expect(kernelApi.lookupEnrollment).toHaveBeenCalledTimes(1);
    expect(kernelApi.search).not.toHaveBeenCalled();
  });

  it("resolves in 1 request when siteCode is null", async () => {
    vi.mocked(kernelApi.lookupEnrollment).mockResolvedValueOnce({
      study_ref: STUDY_REF,
      study_code: "CAD-101",
      study_title: "Study",
      study_site_ref: SITE_B,
      site_code: "018",
      site_title: "Site 018",
      enrollment_ref: `enrollment:${ENROLLMENT_ID}@1`,
      subject_code: "018-0001",
      status: "Screened",
    });

    await expect(
      findKernelEnrollmentRef("CAD-101", null, "018-0001")
    ).resolves.toEqual({
      kind: "enrollment",
      id: ENROLLMENT_ID,
      version: 1,
    });

    expect(kernelApi.lookupEnrollment).toHaveBeenCalledTimes(1);
    expect(kernelApi.lookupEnrollment).toHaveBeenCalledWith({
      studyCode: "CAD-101",
      subjectCode: "018-0001",
      siteCode: null,
    });
    expect(kernelApi.getChildren).not.toHaveBeenCalled();
  });

  it("returns null on 404 without negative caching so newly screened participants resolve on demand", async () => {
    vi.mocked(kernelApi.lookupEnrollment)
      .mockRejectedValueOnce(notFoundError())
      .mockResolvedValueOnce({
        study_ref: STUDY_REF,
        study_code: "CAD-101",
        study_title: "Study",
        study_site_ref: SITE_A,
        site_code: "017",
        site_title: "Site 017",
        enrollment_ref: `enrollment:${ENROLLMENT_ID}@1`,
        subject_code: "017-0099",
        status: "Screened",
      });

    await expect(
      findKernelEnrollmentRef("CAD-101", "017", "017-0099")
    ).resolves.toBeNull();
    await expect(
      findKernelEnrollmentRef("CAD-101", "017", "017-0099")
    ).resolves.toEqual({
      kind: "enrollment",
      id: ENROLLMENT_ID,
      version: 1,
    });

    expect(kernelApi.lookupEnrollment).toHaveBeenCalledTimes(2);
  });

  it("rethrows transient non-404 errors without caching so subsequent retries succeed", async () => {
    const unavailable = new ApiError(
      "Service temporarily unavailable.",
      503,
      "Service Unavailable",
      null
    );
    vi.mocked(kernelApi.lookupEnrollment)
      .mockRejectedValueOnce(unavailable)
      .mockResolvedValueOnce({
        study_ref: STUDY_REF,
        study_code: "CAD-101",
        study_title: "Study",
        study_site_ref: SITE_A,
        site_code: "017",
        site_title: "Site 017",
        enrollment_ref: `enrollment:${ENROLLMENT_ID}@1`,
        subject_code: "017-0001",
        status: "Enrolled",
      });

    await expect(
      findKernelEnrollmentRef("CAD-101", "017", "017-0001")
    ).rejects.toThrow("Service temporarily unavailable.");
    await expect(
      findKernelEnrollmentRef("CAD-101", "017", "017-0001")
    ).resolves.toEqual({
      kind: "enrollment",
      id: ENROLLMENT_ID,
      version: 1,
    });
    expect(kernelApi.lookupEnrollment).toHaveBeenCalledTimes(2);
  });

  it("deduplicates concurrent findKernelStudyRef calls onto an in-flight lookupEnrollment and falls back to search on 404", async () => {
    vi.mocked(kernelApi.lookupEnrollment).mockResolvedValueOnce({
      study_ref: STUDY_REF,
      study_code: "CAD-101",
      study_title: "Study",
      study_site_ref: SITE_A,
      site_code: "017",
      site_title: "Site 017",
      enrollment_ref: `enrollment:${ENROLLMENT_ID}@1`,
      subject_code: "017-0001",
      status: "Enrolled",
    });

    const [enrollmentRef, studyRefA, studyRefB] = await Promise.all([
      findKernelEnrollmentRef("CAD-101", null, "017-0001"),
      findKernelStudyRef("CAD-101"),
      findKernelStudyRef("CAD-101"),
    ]);

    expect(enrollmentRef).toEqual({
      kind: "enrollment",
      id: ENROLLMENT_ID,
      version: 1,
    });
    expect(studyRefA).toBe(STUDY_REF);
    expect(studyRefB).toBe(STUDY_REF);
    expect(kernelApi.lookupEnrollment).toHaveBeenCalledTimes(1);
    expect(kernelApi.search).not.toHaveBeenCalled();

    // When a cold lookupEnrollment returns 404 (participant not yet enrolled), concurrent findKernelStudyRef falls back to search.
    clearKernelStudyCache();
    vi.mocked(kernelApi.lookupEnrollment).mockRejectedValueOnce(
      notFoundError()
    );
    vi.mocked(kernelApi.search).mockResolvedValueOnce([
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

    const [missingEnrollment, fallbackStudyRef] = await Promise.all([
      findKernelEnrollmentRef("CAD-101", null, "017-9999"),
      findKernelStudyRef("CAD-101"),
    ]);

    expect(missingEnrollment).toBeNull();
    expect(fallbackStudyRef).toBe(STUDY_REF);
    expect(kernelApi.lookupEnrollment).toHaveBeenCalledTimes(2);
    expect(kernelApi.search).toHaveBeenCalledTimes(1);
  });
});

describe("findKernelStudyRef and findStudySites session caching", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    clearKernelStudyCache();
    vi.mocked(kernelApi.search).mockReset();
    vi.mocked(kernelApi.getChildren).mockReset();
    vi.mocked(kernelApi.lookupEnrollment).mockReset();
  });

  it("reuses the cached study reference across 9 same-study transitions with 1 search request", async () => {
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

    const results: Array<string | null> = [];
    for (let i = 0; i < 10; i++) {
      resetKernelStudySession("CAD-101");
      results.push(await findKernelStudyRef("CAD-101"));
    }

    expect(results).toEqual(Array(10).fill(STUDY_REF));
    expect(kernelApi.search).toHaveBeenCalledTimes(1);
    expect(kernelApi.search).toHaveBeenCalledWith({
      text: "CAD-101",
      kinds: ["study"],
      limit: 5,
    });
  });

  it("returns visible deduplicated study sites sorted by site code, omits masked sites, and caches positive results", async () => {
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
    await expect(findStudySites("CAD-101")).resolves.toEqual([
      { code: "SITE-17", label: "SITE-17" },
      { code: "SITE-22", label: "SITE-22 — Chicago Medical Center" },
    ]);
    expect(kernelApi.search).toHaveBeenCalledTimes(1);
    expect(kernelApi.getChildren).toHaveBeenCalledTimes(1);
  });

  it("returns an empty list when the study is unknown without negatively caching it", async () => {
    vi.mocked(kernelApi.search)
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([
        {
          ref: STUDY_REF,
          kind: "study",
          title: "Study",
          code: "CAD-404",
          detail: null,
          exact_code: true,
          rank: 1,
        },
      ]);
    vi.mocked(kernelApi.getChildren).mockResolvedValue([
      child("study_site", SITE_A, "SITE-17"),
    ]);

    await expect(findStudySites("CAD-404")).resolves.toEqual([]);
    await expect(findStudySites("CAD-404")).resolves.toEqual([
      { code: "SITE-17", label: "SITE-17" },
    ]);
  });

  it("clears cached study, site, and enrollment entries immediately when switching studies, tenants, accounts, personas, or signing out", async () => {
    vi.mocked(kernelApi.search).mockImplementation(async ({ text }) => [
      {
        ref: text === "CAD-202" ? STUDY_REF_B : STUDY_REF,
        kind: "study",
        title: text,
        code: text,
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.getChildren).mockResolvedValue([
      child("study_site", SITE_A, "017"),
    ]);
    vi.mocked(kernelApi.lookupEnrollment).mockResolvedValue({
      study_ref: STUDY_REF,
      study_code: "CAD-101",
      study_title: "Study",
      study_site_ref: SITE_A,
      site_code: "017",
      site_title: "Site 017",
      enrollment_ref: `enrollment:${ENROLLMENT_ID}@1`,
      subject_code: "017-0001",
      status: "Enrolled",
    });

    const auth = useAuthStore();
    auth.setAuth({
      authenticated: true,
      token: "tok-tenant-a",
      tokenParsed: {
        sub: "user-1",
        preferred_username: "crc1",
        tenant_id: "tenant-a",
        realm_access: { roles: ["Sponsor Admin", "CRC"] },
      },
    });

    async function warmCaches() {
      await findStudySites("CAD-101");
      await findKernelEnrollmentRef("CAD-101", "017", "017-0001");
    }

    async function expectColdReload() {
      const searchBefore = vi.mocked(kernelApi.search).mock.calls.length;
      const childrenBefore = vi.mocked(kernelApi.getChildren).mock.calls.length;
      const lookupBefore = vi.mocked(kernelApi.lookupEnrollment).mock.calls
        .length;

      await findStudySites("CAD-101");
      await findKernelEnrollmentRef("CAD-101", "017", "017-0001");

      expect(vi.mocked(kernelApi.search).mock.calls.length).toBe(
        searchBefore + 1
      );
      expect(vi.mocked(kernelApi.getChildren).mock.calls.length).toBe(
        childrenBefore + 1
      );
      expect(vi.mocked(kernelApi.lookupEnrollment).mock.calls.length).toBe(
        lookupBefore + 1
      );
    }

    await warmCaches();

    // 1. clearNodeResolutionCache
    clearNodeResolutionCache();
    await expectColdReload();

    // 2. resetNodeResolutionSession on study switch (while same-study preserves warm cache)
    resetNodeResolutionSession("CAD-101");
    const searchWarm = vi.mocked(kernelApi.search).mock.calls.length;
    await findKernelStudyRef("CAD-101");
    expect(vi.mocked(kernelApi.search).mock.calls.length).toBe(searchWarm);
    resetNodeResolutionSession("CAD-202");
    await expectColdReload();

    // 3. studyCanvas.openStudy switching studies
    const canvas = useStudyCanvasStore();
    canvas.openStudy("CAD-101");
    await warmCaches();
    canvas.openStudy("CAD-202");
    await expectColdReload();

    // 3b. Direct study switch via findKernelStudyRef / findKernelEnrollmentRef (e.g. EcrfView)
    await findKernelStudyRef("CAD-202");
    await expectColdReload();

    // 4. Persona switch
    auth.switchPersona("site_crc");
    await expectColdReload();

    // 5. Tenant switch via setAuth
    auth.setAuth({
      authenticated: true,
      token: "tok-tenant-b",
      tokenParsed: {
        sub: "user-1",
        preferred_username: "crc1",
        tenant_id: "tenant-b",
        realm_access: { roles: ["Sponsor Admin", "CRC"] },
      },
    });
    await expectColdReload();

    // 6. Account switch via setAuth
    auth.setAuth({
      authenticated: true,
      token: "tok-user-2",
      tokenParsed: {
        sub: "user-2",
        preferred_username: "crc2",
        tenant_id: "tenant-b",
        realm_access: { roles: ["Sponsor Admin", "CRC"] },
      },
    });
    await expectColdReload();

    // 7. Session expiry
    auth.expireSession("/studies/CAD-101");
    await expectColdReload();

    // 8. Sign-out (logout / clearAuth)
    await auth.logout();
    await expectColdReload();

    // 9. Demo session bootstrap
    vi.stubEnv("VITE_DEMO_MODE", "true");
    try {
      auth.startDemoSession("super_admin");
      await expectColdReload();
    } finally {
      vi.unstubAllEnvs();
    }
  });
});

describe("kernelStudy session cache", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    clearKernelStudyCache();
    vi.mocked(kernelApi.search).mockReset();
    vi.mocked(kernelApi.getChildren).mockReset();
    vi.mocked(kernelApi.lookupEnrollment).mockReset();
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
      child("study_site", SITE_A, "017"),
    ]);
    vi.mocked(kernelApi.lookupEnrollment).mockResolvedValue({
      study_ref: STUDY_REF,
      study_code: "CAD-101",
      study_title: "Study",
      study_site_ref: SITE_A,
      site_code: "017",
      site_title: "Site 017",
      enrollment_ref: `enrollment:${ENROLLMENT_ID}@2`,
      subject_code: "017-0001",
      status: "Enrolled",
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

describe("site-scoped schedule governance and enrollment schedule windows", () => {
  const V1_REF = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-00000000b001";
  const V2_REF = "protocol_version:0192f3c4-5e6a-7b8c-9d0e-00000000b002";

  beforeEach(() => {
    setActivePinia(createPinia());
    clearKernelStudyCache();
    vi.mocked(kernelApi.search).mockReset();
    vi.mocked(kernelApi.listStudySites).mockReset();
    vi.mocked(kernelApi.listProtocolVersions).mockReset();
    vi.mocked(kernelApi.getScheduleOfActivities).mockReset();
    vi.mocked(kernelApi.getEnrollmentSchedule).mockReset();
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
  });

  it("selects Site A's governing Version 1 from site.effective_versions even when Version 2 is Approved", async () => {
    vi.mocked(kernelApi.listStudySites).mockResolvedValue([
      {
        study_site_ref: SITE_A,
        study_ref: STUDY_REF,
        site_ref: "site:1",
        site_code: "SITE-A",
        site_name: "Tokyo Site",
        country: "JP",
        status: "Active",
        effective_versions: [
          {
            protocol_version_ref: V1_REF,
            number: 1,
            effectivity_ref: "effectivity:1",
            level: "StudySite",
          },
        ],
      },
    ]);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([
      {
        ref: V2_REF,
        study: STUDY_REF,
        number: 2,
        parent_version: V1_REF,
        status: "Approved",
        content_hash: "sha256:v2",
        amendment_intent: "Amendment",
        approval_attestation: null,
        withdrawal_reason: null,
        mask: null,
      },
      {
        ref: V1_REF,
        study: STUDY_REF,
        number: 1,
        parent_version: null,
        status: "Approved",
        content_hash: "sha256:v1",
        amendment_intent: null,
        approval_attestation: null,
        withdrawal_reason: null,
        mask: null,
      },
    ]);
    vi.mocked(kernelApi.getScheduleOfActivities).mockResolvedValue({
      version: V1_REF,
      content_hash: "sha256:v1",
      title: "CAD-101 v1",
      epochs: [
        {
          epoch_id: "ep.1",
          name: "Treatment",
          order: 1,
          first_column: 0,
          column_count: 1,
        },
      ],
      columns: [
        {
          activity_id: "v.day1",
          name: "Baseline",
          epoch_id: "ep.1",
          study_day: 1,
          window_before: 0,
          window_after: 1,
          locations: [],
          footnotes: [],
        },
      ],
      rows: [
        {
          collection_spec_id: "cs.vs",
          name: "Vital Signs",
          item_ids: ["sbp"],
          concepts: ["concept:sbp@1"],
          columns: [0],
          frequency: 1,
          footnotes: [],
        },
      ],
      footnotes: [],
    });

    const resolved = await findStudyScheduleOptions("CAD-101", "SITE-A");
    expect(resolved?.governanceStatus).toBe("Effective");
    expect(resolved?.versionRef).toBe(V1_REF);
    expect(resolved?.versionNumber).toBe(1);
    expect(kernelApi.getScheduleOfActivities).toHaveBeenCalledWith(V1_REF);
  });

  it("returns NoEffectiveVersion guidance instead of falling back to Draft when a site has zero effective versions", async () => {
    vi.mocked(kernelApi.listStudySites).mockResolvedValue([
      {
        study_site_ref: SITE_A,
        study_ref: STUDY_REF,
        site_ref: "site:1",
        site_code: "SITE-DRAFT",
        site_name: "Draft Site",
        country: "US",
        status: "Selected",
        effective_versions: [],
      },
    ]);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([
      {
        ref: V1_REF,
        study: STUDY_REF,
        number: 1,
        parent_version: null,
        status: "Draft",
        content_hash: null,
        amendment_intent: null,
        approval_attestation: null,
        withdrawal_reason: null,
        mask: null,
      },
    ]);

    const resolved = await findStudyScheduleOptions("CAD-101", "SITE-DRAFT");
    expect(resolved?.governanceStatus).toBe("NoEffectiveVersion");
    expect(resolved?.columns).toEqual([]);
    expect(resolved?.scheduledRows).toEqual([]);
    expect(resolved?.guidance).toContain("Draft");
    expect(kernelApi.getScheduleOfActivities).not.toHaveBeenCalled();
  });

  it("maps server-computed targetDate, windowStatus, and repeating metadata from getEnrollmentSchedule", async () => {
    vi.mocked(kernelApi.getEnrollmentSchedule).mockResolvedValue({
      enrollment: `enrollment:${ENROLLMENT_ID}@1`,
      study: STUDY_REF,
      study_site: SITE_A,
      governance_status: "Effective",
      governing_version: V1_REF,
      governing_version_number: 1,
      latest_study_version_status: "Approved",
      guidance: null,
      reference_date: "2026-03-01",
      as_of_date: "2026-03-15",
      time_zone: "Asia/Tokyo",
      schedule: {
        version: V1_REF,
        content_hash: "sha256:v1",
        title: "CAD-101 v1",
        epochs: [
          {
            epoch_id: "ep.1",
            name: "Treatment",
            order: 1,
            first_column: 0,
            column_count: 2,
          },
        ],
        columns: [
          {
            activity_id: "v.day1",
            name: "Baseline",
            epoch_id: "ep.1",
            study_day: 1,
            window_before: 0,
            window_after: 1,
            locations: [],
            footnotes: [],
          },
          {
            activity_id: "v.day15",
            name: "Day 15",
            epoch_id: "ep.1",
            study_day: 15,
            window_before: 2,
            window_after: 2,
            repeating: true,
            locations: [],
            footnotes: [],
          },
        ],
        rows: [
          {
            collection_spec_id: "cs.vs",
            name: "Vital Signs",
            item_ids: ["sbp"],
            concepts: ["concept:sbp@1"],
            columns: [0, 1],
            frequency: 2,
            footnotes: [],
          },
        ],
        footnotes: [],
      },
      expected_visits: [
        {
          activity_id: "v.day1",
          activity_name: "Baseline",
          epoch_id: "ep.1",
          study_day: 1,
          target_date: "2026-03-01",
          window_open: "2026-03-01",
          window_close: "2026-03-02",
          window_status: "Closed",
          collection_spec_ids: ["cs.vs"],
        },
        {
          activity_id: "v.day15",
          activity_name: "Day 15",
          epoch_id: "ep.1",
          study_day: 15,
          target_date: "2026-03-15",
          window_open: "2026-03-13",
          window_close: "2026-03-17",
          window_status: "Open",
          repeating: true,
          collection_spec_ids: ["cs.vs"],
        },
      ],
    });

    const mapped = await findEnrollmentScheduleOptions(ENROLLMENT_ID);
    expect(mapped?.governanceStatus).toBe("Effective");
    expect(
      mapped?.columns.map((c) => [
        c.activityId,
        c.targetDate,
        c.windowStatus,
        c.repeating,
      ])
    ).toEqual([
      ["v.day1", "2026-03-01", "Closed", false],
      ["v.day15", "2026-03-15", "Open", true],
    ]);
  });
});
