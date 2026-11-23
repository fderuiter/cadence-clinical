import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { ApiError } from "../../src/api/client";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(async () => ({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    })),
    getSubjectCounts: vi.fn(async () => ({ total: 0, counts: {} })),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

vi.mock("../../src/api/kernelApi", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    kernelApi: {
      listProposals: vi.fn(async () => []),
      search: vi.fn(async () => []),
      getChildren: vi.fn(async () => []),
      listProtocolVersions: vi.fn(async () => []),
      getProtocolVersion: vi.fn(),
      getScheduleOfActivities: vi.fn(),
      getCommands: vi.fn(),
      getObservation: vi.fn(),
      getZoomPath: vi.fn(async () => []),
      listStudies: vi.fn(async () => []),
      listOrganizations: vi.fn(async () => []),
      createStudy: vi.fn(),
      listProtocolTemplates: vi.fn(async () => []),
      getProtocolDesignSummary: vi.fn(),
      importProtocolDraft: vi.fn(),
    },
  };
});

const SPONSOR_ORG = {
  ref: "organization:0192f3c4-5e6a-7b8c-9d0e-000000000010@1",
  code: "CADENCE-PHARMA",
  name: "Cadence Pharma (demo sponsor)",
  kind: "Sponsor",
};

const DEMO_STUDY = {
  ref: "study:0192f3c4-5e6a-7b8c-9d0e-000000000020@2",
  code: "CADENCE-101",
  title: "Cadence demo study: cardiovascular outcomes",
  phase: "Phase 3",
  sponsor_ref: SPONSOR_ORG.ref,
  sponsor_code: SPONSOR_ORG.code,
  sponsor_name: SPONSOR_ORG.name,
  site_count: 2,
};

const TEMPLATES = [
  {
    id: "vital-signs-vs",
    category: "Findings",
    form_oid: "VS",
    form_name: "Vital Signs",
    file_name: "CDISC_Vital_Signs.xml",
    item_count: 8,
  },
  {
    id: "demographics-dm",
    category: "Special-Purpose",
    form_oid: "DM",
    form_name: "Demographics",
    file_name: "CDISC_Demographics.xml",
    item_count: 6,
  },
];

describe("Clinical Canvas study directory, study creation, and ODM/USDM protocol import", () => {
  let pinia;
  let router;
  let wrapper;
  let alertSpy;

  beforeEach(() => {
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
        { path: "/ecrf", name: "ecrf", component: { template: "<div />" } },
      ],
    });
    for (const fn of Object.values(kernelApi)) {
      if (typeof fn === "function" && "mockReset" in fn) {
        vi.mocked(fn).mockReset();
      }
    }
    vi.mocked(kernelApi.listProposals).mockResolvedValue([]);
    vi.mocked(kernelApi.getZoomPath).mockResolvedValue([]);
    vi.mocked(kernelApi.listStudies).mockResolvedValue([DEMO_STUDY]);
    vi.mocked(kernelApi.listOrganizations).mockResolvedValue([SPONSOR_ORG]);
    vi.mocked(kernelApi.listProtocolTemplates).mockResolvedValue(TEMPLATES);
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: DEMO_STUDY.ref,
        kind: "study",
        title: DEMO_STUDY.title,
        code: "CADENCE-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([]);
    alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  async function open(url) {
    await router.push(url);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
    return wrapper;
  }

  it("displays CADENCE-101 placeholder and opens CADENCE-101 from the visible study list in one click", async () => {
    await open("/studies");

    const studyInput = wrapper.find("#open-study-code");
    expect(studyInput.attributes("placeholder")).toContain("CADENCE-101");
    expect(wrapper.find("#visible-studies-list").text()).toContain(
      "CADENCE-101"
    );
    expect(wrapper.find("#visible-studies-list").text()).toContain(
      "Cadence demo study: cardiovascular outcomes"
    );

    await wrapper.find("#btn-open-study-CADENCE-101").trigger("click");
    await router.isReady();
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe("/studies/CADENCE-101");
    expect(wrapper.find("#canvas-study-title").text()).toBe("CADENCE-101");
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it("registers a new study with a signed reason for change and makes it immediately openable from the visible study list", async () => {
    const createdStudy = {
      ref: "study:0192f3c4-5e6a-7b8c-9d0e-000000000099@1",
      code: "CADENCE-202",
      title: "Phase 2 Renal Outcomes Trial",
      phase: "Phase 2",
      sponsor_ref: SPONSOR_ORG.ref,
      sponsor_code: SPONSOR_ORG.code,
      sponsor_name: SPONSOR_ORG.name,
      site_count: 0,
    };
    vi.mocked(kernelApi.createStudy)
      .mockResolvedValueOnce(createdStudy)
      .mockResolvedValue(createdStudy);
    vi.mocked(kernelApi.listStudies)
      .mockResolvedValueOnce([DEMO_STUDY])
      .mockResolvedValueOnce([DEMO_STUDY, createdStudy]);

    await open("/studies");

    expect(
      wrapper.find("#create-study-code").attributes("placeholder")
    ).toContain("CADENCE-101");
    await wrapper.find("#create-study-code").setValue("CADENCE-202");
    await wrapper
      .find("#create-study-title")
      .setValue("Phase 2 Renal Outcomes Trial");
    await wrapper.find("#create-study-phase").setValue("Phase 2");
    await wrapper
      .find("#create-study-reason")
      .setValue("Register Phase 2 renal protocol workspace");

    await wrapper.find("#create-study-form").trigger("submit");
    await flushPromises();

    expect(kernelApi.createStudy).toHaveBeenCalledWith(
      expect.objectContaining({
        code: "CADENCE-202",
        title: "Phase 2 Renal Outcomes Trial",
        phase: "Phase 2",
        sponsorRef: SPONSOR_ORG.ref,
        reason: "Register Phase 2 renal protocol workspace",
        idempotencyKey: expect.any(String),
      })
    );
    expect(wrapper.find("#create-study-success").text()).toContain(
      "CADENCE-202"
    );
    expect(wrapper.find("#btn-open-study-CADENCE-202").exists()).toBe(true);

    await wrapper.find("#btn-open-study-CADENCE-202").trigger("click");
    await router.isReady();
    await flushPromises();
    expect(router.currentRoute.value.fullPath).toBe("/studies/CADENCE-202");
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it("uploads ODM XML or selects CDISC templates to create a viewable Draft protocol version with preserved prompts, order, mandatory flags, and import notes", async () => {
    const draftVersion = {
      ref: "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000b9@1",
      study: DEMO_STUDY.ref,
      number: 1,
      parent_version: null,
      status: "Draft",
      content_hash: null,
      amendment_intent: null,
      approval_attestation: null,
      withdrawal_reason: null,
      mask: null,
    };
    const draftSummary = {
      version: draftVersion,
      title: "CADENCE-101 Draft",
      form_count: 1,
      item_count: 2,
      forms: [
        {
          id: "F_VITALS",
          display_order: 1,
          name: "Vital Signs",
          item_count: 2,
          required_item_count: 1,
          items: [
            {
              id: "IT_SYSBP",
              display_order: 1,
              prompt: "Systolic Blood Pressure (mmHg)",
              concept: "concept:0192f3c4-5e6a-7b8c-9d0e-0000000000c1@1",
              required: true,
              value_kind: "Quantity",
              unit: "mm[Hg]",
            },
            {
              id: "IT_POS",
              display_order: 2,
              prompt: "Body Position",
              concept: "concept:0192f3c4-5e6a-7b8c-9d0e-0000000000c2@1",
              required: false,
              value_kind: "Text",
              unit: null,
            },
          ],
        },
      ],
      import_notes: [
        {
          source: "F.VITALS/IT.POS",
          message:
            "Unmapped terminology CodeList 'CL.POS' on item 'VSPOS' was recorded without terminology binding.",
        },
      ],
    };

    vi.mocked(kernelApi.importProtocolDraft).mockResolvedValue(draftSummary);
    vi.mocked(kernelApi.getProtocolDesignSummary).mockResolvedValue(
      draftSummary
    );
    vi.mocked(kernelApi.getProtocolVersion).mockResolvedValue(draftVersion);
    vi.mocked(kernelApi.listProtocolVersions)
      .mockResolvedValueOnce([])
      .mockResolvedValue([draftVersion]);

    await open("/studies/CADENCE-101?stage=design&section=protocol-versions");

    // Clicking the Choose button reliably invokes click() on the hidden file input.
    const fileInput = wrapper.find("#protocol-import-file");
    const clickSpy = vi.spyOn(fileInput.element, "click");
    await wrapper.find("#protocol-import-choose-file").trigger("click");
    expect(clickSpy).toHaveBeenCalledTimes(1);

    // Select an ODM XML file and a bundled CDISC template.
    const odmFile = new File(["<ODM/>"], "vitals.xml", {
      type: "application/xml",
    });
    Object.defineProperty(fileInput.element, "files", {
      value: [odmFile],
      configurable: true,
    });
    await fileInput.trigger("change");

    await wrapper.find("#protocol-import-tpl-demographics-dm").setValue(true);
    await wrapper
      .find("#protocol-import-reason")
      .setValue("Import initial ODM vital signs and CDISC demographics");

    await wrapper.find("#protocol-import-form").trigger("submit");
    await flushPromises();

    expect(kernelApi.importProtocolDraft).toHaveBeenCalledWith(
      expect.objectContaining({
        studyRef: DEMO_STUDY.ref,
        file: odmFile,
        templateIds: ["demographics-dm"],
        reason: "Import initial ODM vital signs and CDISC demographics",
        idempotencyKey: expect.any(String),
      })
    );
    expect(wrapper.find("#protocol-import-success").text()).toContain(
      "Imported Draft version 1"
    );
    expect(wrapper.find("#protocol-import-notes").text()).toContain(
      "Unmapped terminology CodeList 'CL.POS'"
    );
    const items = wrapper.findAll(".draft-item-row");
    expect(items).toHaveLength(2);
    expect(items[0].text()).toContain("Systolic Blood Pressure (mmHg)");
    expect(items[0].text()).toContain("Mandatory");
    expect(items[1].text()).toContain("Body Position");
    expect(items[1].text()).toContain("Optional");
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it("renders actionable inline error messages on malformed files with zero window.alert calls", async () => {
    vi.mocked(kernelApi.importProtocolDraft).mockRejectedValue(
      new ApiError(
        "Malformed ODM XML at line 3, column 9: unexpected end of file.",
        422,
        "Unprocessable Entity",
        {
          detail:
            "Malformed ODM XML at line 3, column 9: unexpected end of file.",
        }
      )
    );

    await open("/studies/CADENCE-101?stage=design&section=protocol-versions");

    const fileInput = wrapper.find("#protocol-import-file");
    const brokenFile = new File(["<ODM><Study>"], "broken.xml", {
      type: "application/xml",
    });
    Object.defineProperty(fileInput.element, "files", {
      value: [brokenFile],
      configurable: true,
    });
    await fileInput.trigger("change");
    await wrapper
      .find("#protocol-import-reason")
      .setValue("Attempt import of broken XML");

    await wrapper.find("#protocol-import-form").trigger("submit");
    await flushPromises();

    const errorBanner = wrapper.find("#protocol-import-error");
    expect(errorBanner.exists()).toBe(true);
    expect(errorBanner.text()).toContain("Malformed ODM XML at line 3");
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it("creates a study and imports an ODM XML file in one flow, preserving form inputs on malformed retry and opening the draft immediately on success", async () => {
    const createdStudy = {
      ref: "study:0192f3c4-5e6a-7b8c-9d0e-000000000033@1",
      code: "CADENCE-303",
      title: "Castor ODM Migration Study",
      phase: "Phase 3",
      sponsor_ref: SPONSOR_ORG.ref,
      sponsor_code: SPONSOR_ORG.code,
      sponsor_name: SPONSOR_ORG.name,
      site_count: 0,
    };
    const draftVersion = {
      ref: "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000d3@1",
      study: createdStudy.ref,
      number: 1,
      parent_version: null,
      status: "Draft",
      content_hash: null,
      amendment_intent: null,
      approval_attestation: null,
      withdrawal_reason: null,
      mask: null,
    };
    const draftSummary = {
      version: draftVersion,
      title: "Castor ODM Migration Study",
      form_count: 1,
      item_count: 1,
      forms: [
        {
          id: "F_AE",
          display_order: 1,
          name: "Adverse Events",
          item_count: 1,
          required_item_count: 1,
          items: [
            {
              id: "AETERM",
              display_order: 1,
              prompt: "What is the adverse event term?",
              concept: "concept:0192f3c4-5e6a-7b8c-9d0e-0000000000e1@1",
              required: true,
              value_kind: "Text",
              unit: null,
            },
          ],
        },
      ],
      import_notes: [],
    };

    vi.mocked(kernelApi.createStudy).mockResolvedValue(createdStudy);
    vi.mocked(kernelApi.importProtocolDraft)
      .mockRejectedValueOnce(
        new ApiError(
          "ODM v1.3.2 XML must contain a <MetaDataVersion> element.",
          422,
          "Unprocessable Entity",
          {
            detail: "ODM v1.3.2 XML must contain a <MetaDataVersion> element.",
          }
        )
      )
      .mockResolvedValueOnce(draftSummary);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([draftVersion]);
    vi.mocked(kernelApi.getProtocolVersion).mockResolvedValue(draftVersion);
    vi.mocked(kernelApi.getProtocolDesignSummary).mockResolvedValue(
      draftSummary
    );

    await open("/studies");

    await wrapper.find("#create-study-code").setValue("CADENCE-303");
    await wrapper
      .find("#create-study-title")
      .setValue("Castor ODM Migration Study");
    await wrapper.find("#create-study-phase").setValue("Phase 3");
    await wrapper
      .find("#create-study-reason")
      .setValue("Initial Castor ODM migration");

    const fileInput = wrapper.find("#create-study-file");
    const chooseSpy = vi.spyOn(fileInput.element, "click");
    await wrapper.find("#create-study-choose-file").trigger("click");
    expect(chooseSpy).toHaveBeenCalledTimes(1);

    const invalidOdm = new File(["<ODM></ODM>"], "invalid.xml", {
      type: "application/xml",
    });
    Object.defineProperty(fileInput.element, "files", {
      value: [invalidOdm],
      configurable: true,
    });
    await fileInput.trigger("change");
    expect(wrapper.find("#btn-create-study").text()).toContain(
      "Create study & import draft"
    );

    // First attempt: malformed ODM file -> shows inline validation error and preserves form inputs.
    await wrapper.find("#create-study-form").trigger("submit");
    await flushPromises();

    expect(wrapper.find("#create-study-error").text()).toContain(
      "MetaDataVersion"
    );
    expect(wrapper.find("#create-study-code").element.value).toBe(
      "CADENCE-303"
    );
    expect(wrapper.find("#create-study-title").element.value).toBe(
      "Castor ODM Migration Study"
    );
    expect(alertSpy).not.toHaveBeenCalled();

    // Retry with corrected ODM file -> reuses created study without duplicate registration and opens draft immediately.
    const validOdm = new File(["<ODM><Study/></ODM>"], "castor-ae.xml", {
      type: "application/xml",
    });
    Object.defineProperty(fileInput.element, "files", {
      value: [validOdm],
      configurable: true,
    });
    await fileInput.trigger("change");

    await wrapper.find("#create-study-form").trigger("submit");
    await router.isReady();
    await flushPromises();

    expect(kernelApi.createStudy).toHaveBeenCalledTimes(1);
    expect(kernelApi.importProtocolDraft).toHaveBeenCalledTimes(2);
    expect(router.currentRoute.value.path).toBe("/studies/CADENCE-303");
    expect(router.currentRoute.value.query).toMatchObject({
      stage: "design",
      section: "protocol-versions",
      node: draftVersion.ref,
    });
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it("passes customized template visit assignments and highlights repeating CDISC templates", async () => {
    vi.mocked(kernelApi.listProtocolTemplates).mockResolvedValue([
      ...TEMPLATES,
      {
        id: "tobacco-product-events-or-malfunctions",
        category: "Events",
        form_oid: "TPEM",
        form_name: "Tobacco Product Events or Malfunctions",
        file_name: "TIG_Tobacco_Product_Events_or_Malfunctions_XML.xml",
        item_count: 9,
        repeating: true,
      },
    ]);
    const draftVersion = {
      ref: "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000f9@1",
      study: DEMO_STUDY.ref,
      number: 1,
      parent_version: null,
      status: "Draft",
      content_hash: null,
      amendment_intent: null,
      approval_attestation: null,
      withdrawal_reason: null,
      mask: null,
    };
    const draftSummary = {
      version: draftVersion,
      title: "CADENCE-101 Scheduled Template Draft",
      form_count: 2,
      item_count: 15,
      forms: [],
      import_notes: [],
    };
    vi.mocked(kernelApi.importProtocolDraft).mockResolvedValue(draftSummary);
    vi.mocked(kernelApi.getProtocolDesignSummary).mockResolvedValue(
      draftSummary
    );
    vi.mocked(kernelApi.getProtocolVersion).mockResolvedValue(draftVersion);
    vi.mocked(kernelApi.listProtocolVersions).mockResolvedValue([draftVersion]);

    await open("/studies/CADENCE-101?stage=design&section=protocol-versions");

    expect(wrapper.text()).toContain("Repeating");

    await wrapper.find("#protocol-import-tpl-vital-signs-vs").setValue(true);
    await wrapper
      .find("#protocol-import-tpl-tobacco-product-events-or-malfunctions")
      .setValue(true);
    await flushPromises();

    expect(wrapper.find("#protocol-import-template-assignments").exists()).toBe(
      true
    );
    await wrapper
      .find("#protocol-import-assign-mode-vital-signs-vs")
      .setValue("custom");
    await flushPromises();

    // Uncheck Week 4 and End of Study so Vital Signs is assigned only to Screening and Baseline
    await wrapper
      .find('[id="protocol-import-assign-visit-vital-signs-vs-V.WEEK4"]')
      .setValue(false);
    await wrapper
      .find('[id="protocol-import-assign-visit-vital-signs-vs-V.EOS"]')
      .setValue(false);
    await wrapper
      .find(
        "#protocol-import-assign-mode-tobacco-product-events-or-malfunctions"
      )
      .setValue("unscheduled");
    await wrapper
      .find("#protocol-import-reason")
      .setValue("Import scheduled Vital Signs and repeating event log");

    await wrapper.find("#protocol-import-form").trigger("submit");
    await flushPromises();

    expect(kernelApi.importProtocolDraft).toHaveBeenCalledWith(
      expect.objectContaining({
        studyRef: DEMO_STUDY.ref,
        templateIds: [
          "vital-signs-vs",
          "tobacco-product-events-or-malfunctions",
        ],
        templateAssignments: expect.arrayContaining([
          expect.objectContaining({
            templateId: "vital-signs-vs",
            activityIds: ["V.SCREENING", "V.BASELINE"],
          }),
          expect.objectContaining({
            templateId: "tobacco-product-events-or-malfunctions",
            activityIds: [],
          }),
        ]),
      })
    );
  });
});
