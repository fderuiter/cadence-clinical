import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi, type KernelStudySummary } from "../../src/api/kernelApi";
import {
  authorizedParticipantSiteCodes,
  canManageParticipants,
  holdsParticipantRole,
} from "../../src/canvas/authority";
import StudySitesPanel from "../../src/components/canvas/StudySitesPanel.vue";
import { useAuthStore } from "../../src/stores/auth";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

describe("Kernel site setup, founder coordinator lens, and tenant-scoped participant enrollment", () => {
  let pinia: ReturnType<typeof createPinia>;
  let router: ReturnType<typeof createRouter>;

  beforeEach(() => {
    window.localStorage.clear();
    pinia = createPinia();
    setActivePinia(pinia);
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
          meta: { layout: "canvas" },
        },
        {
          path: "/ecrf",
          name: "ecrf",
          component: { template: "<div id='ecrf-stub' />" },
        },
      ],
    });
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("parses explicit site_id and study_ids claims while rejecting wildcard '*' and preserving AuthUser identity shape", () => {
    const auth = useAuthStore();
    auth.setAuth({
      authenticated: true,
      token: "founder-token",
      idToken: "founder-id-token",
      tokenParsed: {
        sub: "4e999999-1111-4444-8888-111111111111",
        preferred_username: "founder@meridian.example",
        email: "founder@meridian.example", // deid-ignore
        given_name: "Ada",
        family_name: "Lovelace",
        tenant_id: "TRIAL-4E9999991111",
        site_id: ["*", "SITE-4E9999991111", "site-4e9999991111"],
        study_ids: "*, CADENCE-101, CADENCE-102",
        realm_access: {
          roles: ["Sponsor Admin", "Sponsor Designer", "Site Investigator"],
        },
        exp: 1999999999,
      },
    });

    expect(auth.identity).toEqual({
      username: "founder@meridian.example",
      email: "founder@meridian.example", // deid-ignore
      firstName: "Ada",
      lastName: "Lovelace",
      id: "4e999999-1111-4444-8888-111111111111",
      tenantId: "TRIAL-4E9999991111",
    });
    expect(auth.assignedSiteIds).toEqual(["SITE-4E9999991111"]);
    expect(auth.assignedStudyIds).toEqual(["CADENCE-101", "CADENCE-102"]);
    expect(auth.canManageWorkspaceSites).toBe(true);
    expect(auth.currentPersona).toBe("super_admin");
  });

  it("gates participant mutations behind the Site Coordinator lens while scoping site codes to assignedSiteIds", () => {
    const founderSponsorLens = {
      rawRoles: ["Sponsor Admin", "Sponsor Designer", "Site Investigator"],
      currentPersona: "super_admin",
      assignedSiteIds: ["SITE-SANDBOX-1"],
    };

    expect(holdsParticipantRole(founderSponsorLens)).toBe(true);
    expect(canManageParticipants(founderSponsorLens)).toBe(false);
    expect(
      canManageParticipants({
        ...founderSponsorLens,
        currentPersona: "sponsor_designer",
      })
    ).toBe(false);
    expect(
      canManageParticipants({
        ...founderSponsorLens,
        currentPersona: "site_crc",
      })
    ).toBe(true);

    expect(
      authorizedParticipantSiteCodes(founderSponsorLens, [
        "SITE-SANDBOX-1",
        "SITE-OTHER-2",
      ])
    ).toEqual(["SITE-SANDBOX-1"]);
    expect(
      authorizedParticipantSiteCodes(
        { rawRoles: ["CRC"], assignedSiteIds: ["*"] },
        ["017", "042"]
      )
    ).toEqual(["017", "042"]);
  });

  it("sends X-Change-Reason and Idempotency-Key headers across kernelApi organization, site, study-site, and one-step study creation calls", async () => {
    const capturedRequests: Array<{
      url: string;
      method: string;
      headers: Record<string, string>;
      body: unknown;
    }> = [];

    vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
      const req = input instanceof Request ? input : new Request(input, init);
      const headers: Record<string, string> = {};
      req.headers.forEach((v, k) => {
        headers[k.toLowerCase()] = v;
      });
      const rawText = await req.clone().text();
      const body = rawText ? JSON.parse(rawText) : null;
      capturedRequests.push({
        url: req.url,
        method: req.method,
        headers,
        body,
      });

      return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    });

    await kernelApi.listOrganizations("SiteInstitution");
    await kernelApi.createOrganization({
      name: "University Medical Center Utrecht",
      code: "INST-UMCU",
      kind: "SiteInstitution",
      reason: "Register institution",
      idempotencyKey: "idem-org-1",
    });
    await kernelApi.listSites();
    await kernelApi.createSite({
      siteCode: "SITE-NL-01",
      name: "Utrecht Cardiology Unit",
      country: "NL",
      institutionRef: "organization:01960000-0000-7000-8000-000000000010",
      reason: "Register site",
      idempotencyKey: "idem-site-1",
    });
    await kernelApi.listStudySites("CADENCE-101");
    await kernelApi.linkSiteToStudy({
      studyRef: "CADENCE-101",
      siteRef: "site:01960000-0000-7000-8000-000000000020",
      reason: "Link site to study",
      idempotencyKey: "idem-link-1",
    });
    await kernelApi.createStudy({
      code: "CADENCE-101",
      title: "Cardiovascular Outcomes Study",
      phase: "Phase 2",
      sponsorRef: "organization:01960000-0000-7000-8000-000000000002",
      linkSandboxSite: true,
      initialSiteRef: "site:01960000-0000-7000-8000-000000000020",
      reason: "Create study with starter site",
      idempotencyKey: "idem-study-1",
    });

    expect(capturedRequests).toHaveLength(7);
    expect(capturedRequests[0].url).toContain(
      "/api/v1/kernel/organizations?kind=SiteInstitution"
    );
    expect(capturedRequests[1]).toMatchObject({
      method: "POST",
      headers: expect.objectContaining({
        "idempotency-key": "idem-org-1",
        "x-change-reason": "Register institution",
      }),
      body: {
        name: "University Medical Center Utrecht",
        code: "INST-UMCU",
        kind: "SiteInstitution",
      },
    });
    expect(capturedRequests[3]).toMatchObject({
      method: "POST",
      headers: expect.objectContaining({
        "idempotency-key": "idem-site-1",
        "x-change-reason": "Register site",
      }),
      body: expect.objectContaining({
        site_code: "SITE-NL-01",
        country: "NL",
      }),
    });
    expect(capturedRequests[5]).toMatchObject({
      method: "POST",
      headers: expect.objectContaining({
        "idempotency-key": "idem-link-1",
        "x-change-reason": "Link site to study",
      }),
      body: {
        site_ref: "site:01960000-0000-7000-8000-000000000020",
        site_code: null,
      },
    });
    expect(capturedRequests[6]).toMatchObject({
      method: "POST",
      headers: expect.objectContaining({
        "idempotency-key": "idem-study-1",
        "x-change-reason": "Create study with starter site",
      }),
      body: expect.objectContaining({
        study_code: "CADENCE-101",
        link_sandbox_site: true,
        initial_site_ref: "site:01960000-0000-7000-8000-000000000020",
      }),
    });
  });

  it("renders StudySitesPanel, links an unlinked workspace site, registers a new institution and site, and passes WCAG axe checks", async () => {
    const studySites = [
      {
        ref: "study_site:01960000-0000-7000-8000-000000000101",
        study_site_id: "01960000-0000-7000-8000-000000000101",
        study_site_ref: "study_site:01960000-0000-7000-8000-000000000101",
        id: "01960000-0000-7000-8000-000000000101",
        study_ref: "study:01960000-0000-7000-8000-000000000001",
        study_code: "CADENCE-101",
        site_ref: "site:01960000-0000-7000-8000-000000000011",
        site_code: "SITE-SANDBOX-1",
        site_name: "Meridian Sandbox Site",
        country: "US",
        institution_ref: "organization:01960000-0000-7000-8000-000000000021",
        institution_code: "INST-SANDBOX-1",
        institution_name: "Meridian Clinical Research Center",
        status: "Active",
        is_active: true,
        effective_versions: [],
      },
    ];
    const workspaceSites = [
      {
        ref: "site:01960000-0000-7000-8000-000000000011",
        id: "01960000-0000-7000-8000-000000000011",
        code: "SITE-SANDBOX-1",
        name: "Meridian Sandbox Site",
        country: "US",
        institution_ref: "organization:01960000-0000-7000-8000-000000000021",
        institution_code: "INST-SANDBOX-1",
        institution_name: "Meridian Clinical Research Center",
        is_active: true,
        linked_study_count: 1,
      },
      {
        ref: "site:01960000-0000-7000-8000-000000000012",
        id: "01960000-0000-7000-8000-000000000012",
        code: "SITE-BOSTON-2",
        name: "Boston Cardiology Site",
        country: "US",
        institution_ref: "organization:01960000-0000-7000-8000-000000000021",
        institution_code: "INST-SANDBOX-1",
        institution_name: "Meridian Clinical Research Center",
        is_active: true,
        linked_study_count: 0,
      },
    ];
    const institutions = [
      {
        ref: "organization:01960000-0000-7000-8000-000000000021",
        id: "01960000-0000-7000-8000-000000000021",
        code: "INST-SANDBOX-1",
        name: "Meridian Clinical Research Center",
        kind: "SiteInstitution",
      },
    ];

    vi.spyOn(kernelApi, "listStudySites").mockImplementation(async () => [
      ...studySites,
    ]);
    vi.spyOn(kernelApi, "listSites").mockImplementation(async () => [
      ...workspaceSites,
    ]);
    vi.spyOn(kernelApi, "listOrganizations").mockImplementation(async () => [
      ...institutions,
    ]);

    const linkSpy = vi
      .spyOn(kernelApi, "linkSiteToStudy")
      .mockImplementation(async ({ siteRef }) => {
        const matched = workspaceSites.find((s) => s.ref === siteRef)!;
        const linked = {
          ref: `study_site:${matched.id}`,
          study_site_id: matched.id,
          study_site_ref: `study_site:${matched.id}`,
          id: matched.id,
          study_ref: "study:01960000-0000-7000-8000-000000000001",
          study_code: "CADENCE-101",
          site_ref: matched.ref,
          site_code: matched.code,
          site_name: matched.name,
          country: matched.country,
          institution_ref: matched.institution_ref,
          institution_code: matched.institution_code,
          institution_name: matched.institution_name,
          status: "Active",
          is_active: true,
          effective_versions: [],
        };
        studySites.push(linked);
        return linked;
      });

    const createOrgSpy = vi
      .spyOn(kernelApi, "createOrganization")
      .mockImplementation(async ({ name, code }) => {
        const created = {
          ref: "organization:01960000-0000-7000-8000-000000000029",
          id: "01960000-0000-7000-8000-000000000029",
          code: code || "INST-UMCU",
          name,
          kind: "SiteInstitution",
        };
        institutions.push(created);
        return created;
      });

    const createSiteSpy = vi
      .spyOn(kernelApi, "createSite")
      .mockImplementation(
        async ({ siteCode, code, name, country, institutionRef }) => {
          const inst = institutions.find((i) => i.ref === institutionRef)!;
          const created = {
            ref: "site:01960000-0000-7000-8000-000000000019",
            id: "01960000-0000-7000-8000-000000000019",
            code: (siteCode ?? code ?? "").toUpperCase(),
            name,
            country: country.toUpperCase(),
            institution_ref: inst.ref,
            institution_code: inst.code,
            institution_name: inst.name,
            is_active: true,
            linked_study_count: 0,
          };
          workspaceSites.push(created);
          return created;
        }
      );

    const wrapper = mount(StudySitesPanel, {
      props: {
        studyCode: "CADENCE-101",
        canManageSites: true,
      },
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(wrapper.find("#study-site-SITE-SANDBOX-1").exists()).toBe(true);
    await expect(wrapper).toBeAccessible();

    // Link unlinked workspace site SITE-BOSTON-2
    await wrapper
      .find("#link-site-reason")
      .setValue("Link Boston site for cohort expansion");
    await wrapper.find("#link-existing-site-form").trigger("submit");
    await flushPromises();

    expect(linkSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        studyRef: "CADENCE-101",
        siteRef: "site:01960000-0000-7000-8000-000000000012",
        reason: "Link Boston site for cohort expansion",
      })
    );
    expect(wrapper.find("#study-site-SITE-BOSTON-2").exists()).toBe(true);

    // Register new institution + site and link immediately
    await wrapper.find("#select-site-institution").setValue("__new__");
    await wrapper
      .find("#input-institution-name")
      .setValue("University Medical Center Utrecht");
    await wrapper.find("#input-institution-code").setValue("INST-UMCU");
    await wrapper.find("#input-site-code").setValue("site-nl-01");
    await wrapper.find("#input-site-country").setValue("nl");
    await wrapper
      .find("#input-site-name")
      .setValue("Utrecht Cardiology Research Unit");
    await wrapper
      .find("#input-site-reason")
      .setValue("Register Dutch investigational site");
    await wrapper.find("#register-site-form").trigger("submit");
    await flushPromises();

    expect(createOrgSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "University Medical Center Utrecht",
        code: "INST-UMCU",
        kind: "SiteInstitution",
      })
    );
    expect(createSiteSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        siteCode: "SITE-NL-01",
        country: "NL",
        institutionRef: "organization:01960000-0000-7000-8000-000000000029",
      })
    );
    expect(wrapper.find("#study-site-SITE-NL-01").exists()).toBe(true);
    wrapper.unmount();
  });

  it("links the starter sandbox site on one-step study creation and lets a trial founder switch to the Site Coordinator lens to enroll SUBJ-101-001", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = {
      id: "4e999999-1111-4444-8888-111111111111",
      username: "founder@meridian.example",
      email: "founder@meridian.example", // deid-ignore
      firstName: "Ada",
      lastName: "Lovelace",
      tenantId: "TRIAL-4E9999991111",
    };
    auth.rawRoles = ["Sponsor Admin", "Sponsor Designer", "Site Investigator"];
    auth.siteIds = ["SITE-4E9999991111"];
    auth.studyIds = [];
    auth.currentPersona = "super_admin";

    const studies: KernelStudySummary[] = [];

    const starterSite = {
      ref: "site:01960000-0000-7000-8000-000000000011",
      id: "01960000-0000-7000-8000-000000000011",
      code: "SITE-4E9999991111",
      name: "Meridian Sandbox Site",
      country: "US",
      institution_ref: "organization:01960000-0000-7000-8000-000000000021",
      institution_code: "INST-4E9999991111",
      institution_name: "Meridian Clinical Research Center",
      is_active: true,
      linked_study_count: 1,
    };

    vi.spyOn(kernelApi, "listStudies").mockImplementation(async () => [
      ...studies,
    ]);
    vi.spyOn(kernelApi, "listOrganizations").mockImplementation(
      async (kind?: string) =>
        kind === "Sponsor"
          ? [
              {
                ref: "organization:01960000-0000-7000-8000-000000000002",
                id: "01960000-0000-7000-8000-000000000002",
                code: "ORG-4E9999991111",
                name: "Meridian Vascular Research",
                kind: "Sponsor",
              },
            ]
          : [
              {
                ref: starterSite.institution_ref,
                id: "01960000-0000-7000-8000-000000000021",
                code: starterSite.institution_code,
                name: starterSite.institution_name,
                kind: "SiteInstitution",
              },
            ]
    );
    vi.spyOn(kernelApi, "listSites").mockResolvedValue([starterSite]);
    vi.spyOn(kernelApi, "listStudySites").mockResolvedValue([
      {
        ref: "study_site:01960000-0000-7000-8000-000000000101",
        study_site_id: "01960000-0000-7000-8000-000000000101",
        study_site_ref: "study_site:01960000-0000-7000-8000-000000000101",
        study_ref: "study:01960000-0000-7000-8000-000000000099",
        study_code: "CADENCE-101",
        site_ref: starterSite.ref,
        site_code: starterSite.code,
        site_name: starterSite.name,
        country: starterSite.country,
        institution_ref: starterSite.institution_ref,
        institution_code: starterSite.institution_code,
        institution_name: starterSite.institution_name,
        status: "Active",
        is_active: true,
        effective_versions: [],
      },
    ]);
    vi.spyOn(kernelApi, "search").mockImplementation(async ({ text }) =>
      text === "CADENCE-101"
        ? [
            {
              ref: "study:01960000-0000-7000-8000-000000000099",
              kind: "Study",
              code: "CADENCE-101",
              title: "Meridian Outcomes Trial",
              detail: null,
              exact_code: true,
              rank: 1,
            },
          ]
        : []
    );
    vi.spyOn(kernelApi, "listProtocolVersions").mockResolvedValue([]);
    vi.spyOn(kernelApi, "listProposals").mockResolvedValue([]);
    vi.spyOn(kernelApi, "activity").mockResolvedValue({
      entries: [],
      next: null,
    });

    const createStudySpy = vi
      .spyOn(kernelApi, "createStudy")
      .mockImplementation(async ({ code, title, phase, sponsorRef }) => {
        const created: KernelStudySummary = {
          ref: "study:01960000-0000-7000-8000-000000000099",
          code,
          title,
          phase,
          sponsor_ref:
            sponsorRef || "organization:01960000-0000-7000-8000-000000000002",
          sponsor_code: "ORG-4E9999991111",
          sponsor_name: "Meridian Vascular Research",
          site_count: 1,
        };
        studies.push(created);
        return created;
      });

    const tenantParticipants: Array<{
      id: string;
      subject_id: string;
      study_id: string;
      site_id: string | null;
      status: string;
      active_protocol_version: string;
      enrollment_index: number;
    }> = [];

    vi.spyOn(executionApi, "listSubjects").mockImplementation(async () => ({
      items: [...tenantParticipants],
      total: tenantParticipants.length,
      limit: 50,
      offset: 0,
    }));

    const addSubjectSpy = vi
      .spyOn(executionApi, "addSubject")
      .mockImplementation(async ({ studyId, subjectCode, siteId }) => {
        const created = {
          id: "01960000-0000-7000-8000-000000000501",
          subject_id: subjectCode,
          study_id: studyId,
          site_id: siteId || null,
          status: "SCREENING",
          active_protocol_version: "1.0",
          enrollment_index: 1,
        };
        tenantParticipants.push(created);
        return {
          id: created.id,
          subject_id: created.subject_id,
          study_id: created.study_id,
          site_id: created.site_id,
          status: created.status,
          active_protocol_version: created.active_protocol_version,
          encrypted_demographics: null,
        };
      });

    await router.push("/studies");
    await router.isReady();
    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();

    // One-step sandbox site link checkbox is checked by default
    const linkCheckbox = wrapper.find("#create-study-link-sandbox-site");
    expect(linkCheckbox.exists()).toBe(true);
    expect((linkCheckbox.element as HTMLInputElement).checked).toBe(true);

    await wrapper.find("#create-study-code").setValue("CADENCE-101");
    await wrapper
      .find("#create-study-title")
      .setValue("Meridian Outcomes Trial");
    await wrapper
      .find("#create-study-reason")
      .setValue("Bootstrap CADENCE-101 in fresh trial tenant");
    await wrapper.find("#create-study-form").trigger("submit");
    await flushPromises();

    expect(createStudySpy).toHaveBeenCalledWith(
      expect.objectContaining({
        code: "CADENCE-101",
        linkSandboxSite: true,
        initialSiteRef: starterSite.ref,
      })
    );

    // Navigate to Conduct -> Participants in the newly created CADENCE-101 study
    await router.push(
      "/studies/CADENCE-101?stage=conduct&section=participants"
    );
    await flushPromises();

    // Zero leaked demo participants in the fresh trial tenant
    expect(wrapper.find("#participants-status-line").text()).toBe(
      "No participants in CADENCE-101 yet."
    );
    // Sponsor lens keeps participant creation hidden and prompts switching to Site Coordinator lens
    expect(wrapper.find("#btn-add-participant").exists()).toBe(false);
    expect(wrapper.find("#btn-switch-coordinator-lens").exists()).toBe(true);

    await wrapper.find("#btn-switch-coordinator-lens").trigger("click");
    await flushPromises();

    expect(auth.currentPersona).toBe("site_crc");
    expect(wrapper.find("#btn-add-participant").exists()).toBe(true);

    await wrapper.find("#btn-add-participant").trigger("click");
    await flushPromises();

    // Sandbox site is pre-selected from the founder's site_id scope and linked study sites
    expect(
      (wrapper.find("#add-participant-site").element as HTMLSelectElement).value
    ).toBe("SITE-4E9999991111");

    await wrapper.find("#add-participant-code").setValue("SUBJ-101-001");
    await wrapper
      .find("#add-participant-reason")
      .setValue("First screening participant in trial tenant");
    await wrapper.find("#add-participant-form").trigger("submit");
    await flushPromises();

    expect(addSubjectSpy).toHaveBeenCalledWith({
      studyId: "CADENCE-101",
      subjectCode: "SUBJ-101-001",
      siteId: "SITE-4E9999991111",
      reason: "First screening participant in trial tenant",
    });
    expect(wrapper.find("#participant-SUBJ-101-001").exists()).toBe(true);

    wrapper.unmount();
  });
});
