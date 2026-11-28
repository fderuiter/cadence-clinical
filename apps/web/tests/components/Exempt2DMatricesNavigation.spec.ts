import { beforeEach, describe, expect, it, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { nextTick } from "vue";
import { createMemoryHistory, createRouter } from "vue-router";
import { EXEMPT_2D_CLINICAL_MATRICES } from "../../../../scripts/eslint/allowlists/index.mjs";
import ParticipantFormsPanel from "../../src/components/canvas/ParticipantFormsPanel.vue";
import ClinicalSoAMatrix from "../../src/components/clinical/ClinicalSoAMatrix.vue";
import SoAMatrixEditor from "../../src/components/clinical/SoAMatrixEditor.vue";
import RepeatingLogGridWidget from "../../src/components/crf/widgets/RepeatingLogGridWidget.vue";
import DocumentGrid from "../../src/components/etmf/DocumentGrid.vue";
import RactMatrixViewer from "../../src/components/quality/RactMatrixViewer.vue";
import OncologyCalipers from "../../src/components/imaging/OncologyCalipers.vue";
import { ecrfFormsApi } from "../../src/api/ecrfFormsApi";
import { useEtmfStore } from "../../src/stores/etmf.js";

vi.mock("../../src/api/ecrfFormsApi", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("../../src/api/ecrfFormsApi")>();
  return {
    ...actual,
    ecrfFormsApi: {
      ...actual.ecrfFormsApi,
      listForEnrollment: vi.fn().mockResolvedValue([]),
    },
  };
});

async function assertRoving2DTraversal(
  wrapper: ReturnType<typeof mount>,
  maxColIndex: number
) {
  const allGridCells = wrapper.findAll("[data-row][data-col]");
  expect(allGridCells.length).toBeGreaterThanOrEqual(4);

  const activeBefore = allGridCells.filter(
    (c) => c.attributes("tabindex") === "0"
  );
  const inactiveBefore = allGridCells.filter(
    (c) => c.attributes("tabindex") === "-1"
  );
  expect(activeBefore).toHaveLength(1);
  expect(inactiveBefore).toHaveLength(allGridCells.length - 1);

  const cell00 = wrapper.find('[data-row="0"][data-col="0"]');
  expect(cell00.exists()).toBe(true);
  expect(cell00.attributes("tabindex")).toBe("0");

  await cell00.trigger("keydown", { key: "ArrowRight" });
  await nextTick();
  const cell01 = wrapper.find('[data-row="0"][data-col="1"]');
  expect(cell01.attributes("tabindex")).toBe("0");
  expect(cell00.attributes("tabindex")).toBe("-1");

  await cell01.trigger("keydown", { key: "ArrowDown" });
  await nextTick();
  const cell11 = wrapper.find('[data-row="1"][data-col="1"]');
  expect(cell11.attributes("tabindex")).toBe("0");
  expect(cell01.attributes("tabindex")).toBe("-1");

  await cell11.trigger("keydown", { key: "End" });
  await nextTick();
  const cellEnd = wrapper.find(`[data-row="1"][data-col="${maxColIndex}"]`);
  expect(cellEnd.attributes("tabindex")).toBe("0");

  await cellEnd.trigger("keydown", { key: "Home" });
  await nextTick();
  const cell10 = wrapper.find('[data-row="1"][data-col="0"]');
  expect(cell10.attributes("tabindex")).toBe("0");
}

describe("EXEMPT_2D_CLINICAL_MATRICES shared use2DGrid roving tabindex & arrow-key traversal", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
    vi.mocked(ecrfFormsApi.listForEnrollment).mockResolvedValue([]);
  });

  it("covers all 7 exempted clinical matrices", () => {
    expect(EXEMPT_2D_CLINICAL_MATRICES).toHaveLength(7);
  });

  it("1. ParticipantFormsPanel.vue navigates 2x2 casebook cells via roving tabindex", async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: "/:pathMatch(.*)*", component: { template: "<div />" } },
      ],
    });
    const wrapper = mount(ParticipantFormsPanel, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
      props: {
        studyCode: "STUDY-01",
        siteCode: "SITE-101",
        subjectCode: "SUBJ-001",
        enrollmentId: "ENR-001",
        formLocation: (id: string) => `/forms/${id}`,
        newFormLocation: "/forms/new",
        newFormCellLocation: (spec: string, act?: string | null) =>
          `/forms/new?spec=${spec}&act=${act || ""}`,
        schedule: {
          versionRef: "v1.0",
          title: "Protocol v1.0",
          governanceStatus: "Effective",
          epochs: [
            { epochId: "EP-1", name: "Screening & Baseline", columnCount: 2 },
          ],
          columns: [
            {
              activityId: "V1",
              name: "Screening",
              epochId: "EP-1",
              sequence: 1,
            },
            { activityId: "V2", name: "Day 1", epochId: "EP-1", sequence: 2 },
          ],
          scheduledRows: [
            {
              collectionSpecId: "DM",
              name: "Demographics",
              columnIndices: [0, 1],
              repeatable: false,
            },
            {
              collectionSpecId: "VS",
              name: "Vital Signs",
              columnIndices: [0, 1],
              repeatable: true,
            },
          ],
          unscheduledRows: [],
        } as never,
      },
    });
    try {
      await flushPromises();
      await nextTick();
      expect(wrapper.find(".matrix-corner").exists()).toBe(true);
      expect(wrapper.find(".matrix-row-header").exists()).toBe(true);
      await assertRoving2DTraversal(wrapper, 1);
    } finally {
      wrapper.unmount();
    }
  });

  it("2. ClinicalSoAMatrix.vue navigates 2x2 USDM schedule cells via roving tabindex", async () => {
    const wrapper = mount(ClinicalSoAMatrix, {
      attachTo: document.body,
      props: {
        soaData: {
          epochs: [{ epoch_id: "E1", epoch_name: "Treatment" }],
          encounters: [
            {
              encounter_id: "ENC1",
              encounter_name: "Cycle 1 Day 1",
              epoch_id: "E1",
            },
            {
              encounter_id: "ENC2",
              encounter_name: "Cycle 1 Day 15",
              epoch_id: "E1",
            },
          ],
          rows: [
            {
              activity_id: "ACT1",
              activity_name: "ECG 12-Lead",
              cells: {
                ENC1: { applicable: true },
                ENC2: { applicable: false },
              },
            },
            {
              activity_id: "ACT2",
              activity_name: "Hematology Panel",
              cells: { ENC1: { applicable: true }, ENC2: { applicable: true } },
            },
          ],
        },
      },
    });
    try {
      await nextTick();
      expect(wrapper.find(".corner-header").exists()).toBe(true);
      await assertRoving2DTraversal(wrapper, 1);
    } finally {
      wrapper.unmount();
    }
  });

  it("3. SoAMatrixEditor.vue navigates 2x2 interactive visit assignment cells via roving tabindex", async () => {
    const wrapper = mount(SoAMatrixEditor, {
      attachTo: document.body,
      props: {
        visits: [
          { visit_name: "Screening", target_day: -7 },
          { visit_name: "Baseline", target_day: 1 },
        ],
        activities: [
          {
            activity_name: "Vital Signs",
            cdash_domain: "VS",
            assigned_visit_names: ["Screening"],
          },
          {
            activity_name: "Adverse Events",
            cdash_domain: "AE",
            assigned_visit_names: ["Baseline"],
          },
        ],
      },
    });
    try {
      await nextTick();
      expect(wrapper.find(".col-procedure-header").exists()).toBe(true);
      expect(wrapper.find(".cell-procedure-name").exists()).toBe(true);
      await assertRoving2DTraversal(wrapper, 1);
    } finally {
      wrapper.unmount();
    }
  });

  it("4. RepeatingLogGridWidget.vue navigates 2x3 log grid cells via roving tabindex", async () => {
    const wrapper = mount(RepeatingLogGridWidget, {
      attachTo: document.body,
      props: {
        modelValue: [
          { medication: "Aspirin", dose_mg: 81, frequency: "QD" },
          { medication: "Metformin", dose_mg: 500, frequency: "BID" },
        ],
      },
    });
    try {
      await nextTick();
      expect(wrapper.find(".row-num-header").exists()).toBe(true);
      expect(wrapper.find("th.row-num").exists()).toBe(true);
      await assertRoving2DTraversal(wrapper, 2);
    } finally {
      wrapper.unmount();
    }
  });

  it("5. DocumentGrid.vue navigates 2x7 eTMF artifact cells via roving tabindex", async () => {
    const etmfStore = useEtmfStore(pinia);
    await etmfStore.fetchBinderTree();
    const wrapper = mount(DocumentGrid, {
      attachTo: document.body,
      global: { plugins: [pinia] },
      props: {
        documents: [
          {
            id: "DOC-1",
            study_id: "STUDY-1",
            zone: 1,
            section: "01.01",
            artifact_code: "01.01.01",
            artifact_type: "Trial Protocol",
            filename: "protocol_v1.pdf",
            mime_type: "application/pdf",
            created_at: "2026-08-01T12:00:00Z",
            created_by: "cra_1",
            version_index: 1,
            status: "APPROVED",
          },
          {
            id: "DOC-2",
            study_id: "STUDY-1",
            zone: 5,
            section: "05.02",
            artifact_code: "05.02.02",
            artifact_type: "FDA Form 1572",
            filename: "fda_1572_site101.pdf",
            mime_type: "application/pdf",
            created_at: "2026-08-02T12:00:00Z",
            created_by: "cra_1",
            version_index: 1,
            status: "SIGNED",
          },
        ],
      },
    });
    try {
      await nextTick();
      expect(wrapper.find(".doc-name-cell").exists()).toBe(true);
      await assertRoving2DTraversal(wrapper, 6);
    } finally {
      wrapper.unmount();
    }
  });

  it("6. RactMatrixViewer.vue navigates 2x10 RACT risk factor cells via roving tabindex", async () => {
    const mockFetch = vi.fn().mockImplementation((url: string) => {
      if (String(url).includes("/ract/factors")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve([
              {
                id: "RF-1",
                study_id: "STUDY-ONC-2026",
                category: "SAFETY",
                risk_topic: "DLT under-reporting",
                risk_description: "Delayed SAE escalation",
                probability_score: 2,
                impact_score: 3,
                detectability_score: 2,
                total_risk_score: 12,
                risk_tier: "HIGH",
                mitigation_strategy: "24h automated safety alert",
                linked_kri_code: "KRI-SAE-01",
                is_active: true,
                created_at: "2026-08-01T00:00:00Z",
                created_by: "qa_lead",
                version_index: 1,
                reason_for_change: "Initial RACT",
              },
              {
                id: "RF-2",
                study_id: "STUDY-ONC-2026",
                category: "DATA_INTEGRITY",
                risk_topic: "Endpoint missingness",
                risk_description: "Primary tumor scan window drift",
                probability_score: 2,
                impact_score: 2,
                detectability_score: 2,
                total_risk_score: 8,
                risk_tier: "MEDIUM",
                mitigation_strategy: "Visit window KRI surveillance",
                linked_kri_code: "KRI-VIS-02",
                is_active: true,
                created_at: "2026-08-01T00:00:00Z",
                created_by: "qa_lead",
                version_index: 1,
                reason_for_change: "Initial RACT",
              },
            ]),
        });
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve([]) });
    });
    vi.stubGlobal("fetch", mockFetch);

    const wrapper = mount(RactMatrixViewer, {
      attachTo: document.body,
      global: { plugins: [pinia] },
    });
    try {
      await flushPromises();
      await nextTick();
      expect(wrapper.find(".sticky-col-left").exists()).toBe(true);
      await assertRoving2DTraversal(wrapper, 9);
    } finally {
      wrapper.unmount();
      vi.unstubAllGlobals();
    }
  });

  it("7. OncologyCalipers.vue navigates 2x8 RECIST lesion caliper cells via roving tabindex", async () => {
    const wrapper = mount(OncologyCalipers, {
      attachTo: document.body,
      props: {
        criteria: "RECIST_1_1",
        annotations: [
          {
            id: "LES-1",
            lesion_id: "TL1",
            lesion_type: "TARGET",
            organ_site: "LIVER",
            measurement_type: "MANUAL",
            longest_diameter_mm: 24.5,
            perpendicular_diameter_mm: 18.0,
          },
          {
            id: "LES-2",
            lesion_id: "TL2",
            lesion_type: "TARGET",
            organ_site: "LUNG",
            measurement_type: "AI_AUTO_CONTOUR",
            longest_diameter_mm: 16.2,
            perpendicular_diameter_mm: 11.4,
          },
        ],
      },
    });
    try {
      await nextTick();
      expect(wrapper.find(".sticky-col-left").exists()).toBe(true);
      await assertRoving2DTraversal(wrapper, 7);
    } finally {
      wrapper.unmount();
    }
  });
});
