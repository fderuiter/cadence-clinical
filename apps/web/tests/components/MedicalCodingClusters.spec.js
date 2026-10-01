import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import MedicalCodingView from "../../src/views/MedicalCodingView.vue";
import { useCodingStore } from "../../src/stores/coding.js";
import { useAuthStore } from "../../src/stores/auth.js";

const mockAssignments = [
  {
    id: "assign-1",
    subject_id: "SUBJ-101",
    verbatim_text: "SEVERE HEADACHE",
    dictionary_type: "MEDDRA",
    dictionary_version: "26.0",
    status: "SUGGESTED",
    source_field: "AETERM",
    observation_id: "obs-001",
    score: 0.95,
    model_id: "cadence-meddra-ai-v2.1",
    suggestions: [
      { code: "10019211", term: "Headache", pt_name: "Headache", llt_name: "Severe headache", score: 0.95 },
    ],
  },
  {
    id: "assign-2",
    subject_id: "SUBJ-102",
    verbatim_text: "Severe Headache",
    dictionary_type: "MEDDRA",
    dictionary_version: "26.0",
    status: "SUGGESTED",
    source_field: "AETERM",
    observation_id: "obs-002",
    score: 0.95,
    model_id: "cadence-meddra-ai-v2.1",
    suggestions: [
      { code: "10019211", term: "Headache", pt_name: "Headache", llt_name: "Severe headache", score: 0.95 },
    ],
  },
  {
    id: "assign-3",
    subject_id: "SUBJ-103",
    verbatim_text: "SEVERE HEADACHE",
    dictionary_type: "MEDDRA",
    dictionary_version: "26.0",
    status: "QUERY_PENDING",
    source_field: "AETERM",
    observation_id: "obs-003",
    has_query: true,
    score: 0.95,
    model_id: "cadence-meddra-ai-v2.1",
    suggestions: [
      { code: "10019211", term: "Headache", pt_name: "Headache", llt_name: "Severe headache", score: 0.95 },
    ],
  },
  {
    id: "assign-4",
    subject_id: "SUBJ-201",
    verbatim_text: "MILD NAUSEA",
    dictionary_type: "MEDDRA",
    dictionary_version: "26.0",
    status: "SUGGESTED",
    source_field: "AETERM",
    observation_id: "obs-004",
    score: 0.82,
    model_id: "cadence-meddra-ai-v2.1",
    suggestions: [
      { code: "10028813", term: "Nausea", pt_name: "Nausea", llt_name: "Mild nausea", score: 0.82 },
    ],
  },
  {
    id: "assign-5",
    subject_id: "SUBJ-301",
    verbatim_text: "ASPIRIN 81MG",
    dictionary_type: "WHODRUG",
    dictionary_version: "2024-03",
    status: "SUGGESTED",
    source_field: "CMTRT",
    observation_id: "obs-005",
    score: 0.65,
    model_id: "cadence-whodrug-ai-v1.0",
    suggestions: [
      { code: "00000101", term: "Acetylsalicylic acid", drug_name: "Aspirin", score: 0.65 },
    ],
  },
];

describe("Verbatim Cluster Batch Auto-Coding & Confidence Workbench", () => {
  let pinia;
  let codingStore;
  let authStore;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    codingStore = useCodingStore(pinia);
    authStore = useAuthStore(pinia);

    // Mock fetchAssignments so component mount doesn't overwrite state
    vi.spyOn(codingStore, "fetchAssignments").mockImplementation(async () => {});

    // Grant Data Manager role
    authStore.rawRoles = ["Data Manager"];
    authStore.user = { id: "usr-dm1", email: "datamanager@cadence.clinical", name: "Lead Data Manager" };

    // Set initial mock assignments
    codingStore.assignments = JSON.parse(JSON.stringify(mockAssignments));
  });

  it("aggregates individual assignments into verbatim clusters correctly", () => {
    const clusters = codingStore.verbatimClusters;
    expect(clusters.length).toBe(3); // SEVERE HEADACHE (MEDDRA), MILD NAUSEA (MEDDRA), ASPIRIN 81MG (WHODRUG)

    const headacheCluster = clusters.find((c) => c.verbatim_text === "SEVERE HEADACHE");
    expect(headacheCluster).toBeDefined();
    expect(headacheCluster.instance_count).toBe(3); // 3 instances
    expect(headacheCluster.dictionary_type).toBe("MEDDRA");
    expect(headacheCluster.top_suggested_term).toBe("Headache");
    expect(headacheCluster.top_suggested_code).toBe("10019211");
    expect(headacheCluster.score).toBe(0.95);
    expect(headacheCluster.blocked_count).toBe(1); // assign-3 has_query
    expect(headacheCluster.unblocked_count).toBe(2);
  });

  it("switches queue view mode between Individual Items and Verbatim Clusters", async () => {
    const wrapper = mount(MedicalCodingView, {
      global: { plugins: [pinia] },
    });

    expect(codingStore.viewMode).toBe("INDIVIDUAL");

    // Click Verbatim Clusters view button
    const modeBtns = wrapper.findAll(".mode-btn");
    const clusterBtn = modeBtns.find((b) => b.text().includes("Verbatim Clusters"));
    expect(clusterBtn).toBeDefined();
    await clusterBtn.trigger("click");

    expect(codingStore.viewMode).toBe("CLUSTERS");
    expect(wrapper.find(".cluster-table").exists()).toBe(true);
  });

  it("filters queue and clusters by confidence threshold options (>= 90%, 70-89%, < 70%)", () => {
    // All Scores
    codingStore.setConfidenceThreshold("ALL");
    expect(codingStore.filteredClusters.length).toBe(3);
    expect(codingStore.filteredAssignments.length).toBe(5);

    // High Confidence (>= 90%)
    codingStore.setConfidenceThreshold("HIGH");
    expect(codingStore.filteredClusters.length).toBe(1);
    expect(codingStore.filteredClusters[0].verbatim_text).toBe("SEVERE HEADACHE");
    expect(codingStore.filteredAssignments.length).toBe(3);

    // Medium Confidence (70-89%)
    codingStore.setConfidenceThreshold("MEDIUM");
    expect(codingStore.filteredClusters.length).toBe(1);
    expect(codingStore.filteredClusters[0].verbatim_text).toBe("MILD NAUSEA");
    expect(codingStore.filteredAssignments.length).toBe(1);

    // Low Confidence (< 70%)
    codingStore.setConfidenceThreshold("LOW");
    expect(codingStore.filteredClusters.length).toBe(1);
    expect(codingStore.filteredClusters[0].verbatim_text).toBe("ASPIRIN 81MG");
    expect(codingStore.filteredAssignments.length).toBe(1);
  });

  it("executes 1-step Batch Approve Cluster with GxP change reason and active query exclusion", async () => {
    const headacheCluster = codingStore.verbatimClusters.find((c) => c.verbatim_text === "SEVERE HEADACHE");
    expect(headacheCluster).toBeDefined();

    await codingStore.batchApproveCluster({
      clusterId: headacheCluster.id,
      reason: "Batch approve high-confidence headache cluster",
      reviewerId: "Lead Data Manager",
    });

    // Check updated statuses
    const inst1 = codingStore.assignments.find((a) => a.id === "assign-1");
    const inst2 = codingStore.assignments.find((a) => a.id === "assign-2");
    const inst3 = codingStore.assignments.find((a) => a.id === "assign-3");

    expect(inst1.status).toBe("AUTO_CODED");
    expect(inst1.coded_code).toBe("10019211");
    expect(inst1.coded_term).toBe("Headache");

    expect(inst2.status).toBe("AUTO_CODED");

    // Blocked query instance remains QUERY_PENDING
    expect(inst3.status).toBe("QUERY_PENDING");

    // Alert message generated
    expect(codingStore.batchAlert).not.toBeNull();
    expect(codingStore.batchAlert.message).toContain("1 instance(s) excluded due to active queries or lock conflicts");
  });

  it("writes 21 CFR Part 11 dual-attribution ledger logs with reviewer and AI model identity", async () => {
    const nauseaCluster = codingStore.verbatimClusters.find((c) => c.verbatim_text === "MILD NAUSEA");

    await codingStore.batchApproveCluster({
      clusterId: nauseaCluster.id,
      reason: "Batch approve nausea cluster",
      reviewerId: "Dr. Jane Compliance",
    });

    expect(codingStore.auditLedger.length).toBeGreaterThan(0);
    const auditEntry = codingStore.auditLedger.find((e) => e.assignment_id === "assign-4");

    expect(auditEntry).toBeDefined();
    expect(auditEntry.reviewer_identity).toBe("Dr. Jane Compliance");
    expect(auditEntry.ai_model_identity).toBe("cadence-meddra-ai-v2.1");
    expect(auditEntry.compliance_standard).toBe("21 CFR Part 11 Dual-Attribution");
    expect(auditEntry.reason_for_change).toBe("Batch approve nausea cluster");
    expect(auditEntry.coded_code).toBe("10028813");
  });

  it("supports multi-cluster coding review and synonym propagation", async () => {
    const aspirinCluster = codingStore.verbatimClusters.find((c) => c.verbatim_text === "ASPIRIN 81MG");

    await codingStore.batchCodeClusters({
      clusterIds: [aspirinCluster.id],
      code: "00000101",
      term: "Acetylsalicylic Acid",
      dictionaryType: "WHODRUG",
      dictionaryVersion: "2024-03",
      reason: "Batch WHODrug assignment and synonym propagation",
      reviewerId: "Medical Coder",
    });

    const inst5 = codingStore.assignments.find((a) => a.id === "assign-5");
    expect(inst5.status).toBe("CODED");
    expect(inst5.coded_code).toBe("00000101");
    expect(inst5.coded_term).toBe("Acetylsalicylic Acid");

    // Synonym expansion rule generated
    expect(codingStore.synonymRules.length).toBe(1);
    expect(codingStore.synonymRules[0].verbatim_text).toBe("ASPIRIN 81MG");
    expect(codingStore.synonymRules[0].target_code).toBe("00000101");
  });

  it("renders cluster summary metrics cards in verbatim clusters view mode", async () => {
    codingStore.setViewMode("CLUSTERS");
    const wrapper = mount(MedicalCodingView, {
      global: { plugins: [pinia] },
    });

    const statCards = wrapper.findAll(".stat-card");
    expect(statCards.length).toBe(5);

    // Total Clusters stat
    expect(statCards[0].text()).toContain("Verbatim Clusters");
    expect(statCards[0].text()).toContain("3");

    // Total Subject Instances stat
    expect(statCards[1].text()).toContain("Subject Instances");
    expect(statCards[1].text()).toContain("5");
  });
});
