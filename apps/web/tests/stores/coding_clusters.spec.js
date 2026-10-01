import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useCodingStore } from "../../src/stores/coding.js";
import { apiClient } from "../../src/api/apiClient.js";

vi.mock("../../src/api/apiClient.js", () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

describe("Coding Store - Verbatim Clusters and Confidence Threshold Workbench", () => {
  let store;

  beforeEach(() => {
    setActivePinia(createPinia());
    store = useCodingStore();
    vi.clearAllMocks();
  });

  const mockAssignments = [
    {
      id: "OBS-1",
      verbatim_text: "SEVERE HEADACHE",
      dictionary_type: "MEDDRA",
      dictionary_version: "26.0",
      status: "SUGGESTED",
      score: 0.95,
      suggestions: [
        { code: "10019211", pt_name: "Headache", llt_name: "Severe headache", score: 0.95 },
      ],
    },
    {
      id: "OBS-2",
      verbatim_text: "severe headache",
      dictionary_type: "MEDDRA",
      dictionary_version: "26.0",
      status: "SUGGESTED",
      score: 0.95,
      suggestions: [
        { code: "10019211", pt_name: "Headache", llt_name: "Severe headache", score: 0.95 },
      ],
    },
    {
      id: "OBS-3",
      verbatim_text: "Nausea",
      dictionary_type: "MEDDRA",
      dictionary_version: "26.0",
      status: "SUGGESTED",
      score: 0.82,
      suggestions: [
        { code: "10028813", pt_name: "Nausea", score: 0.82 },
      ],
    },
    {
      id: "OBS-4",
      verbatim_text: "Mild Rash",
      dictionary_type: "MEDDRA",
      dictionary_version: "26.0",
      status: "UNCODED",
      score: 0.55,
      suggestions: [
        { code: "10037844", pt_name: "Rash", score: 0.55 },
      ],
    },
    {
      id: "OBS-5",
      verbatim_text: "ASPIRIN 81MG",
      dictionary_type: "WHODRUG",
      dictionary_version: "2024-03",
      status: "SUGGESTED",
      score: 0.98,
      suggestions: [
        { code: "00001201", drug_name: "Acetylsalicylic acid", score: 0.98 },
      ],
    },
    {
      id: "OBS-6",
      verbatim_text: "SEVERE HEADACHE",
      dictionary_type: "MEDDRA",
      dictionary_version: "26.0",
      status: "QUERY_PENDING",
      score: 0.95,
      is_blocked: true,
      suggestions: [
        { code: "10019211", pt_name: "Headache", score: 0.95 },
      ],
    },
  ];

  it("toggles viewMode between INDIVIDUAL and CLUSTERS", () => {
    expect(store.viewMode).toBe("INDIVIDUAL");
    store.setViewMode("CLUSTERS");
    expect(store.viewMode).toBe("CLUSTERS");
  });

  it("aggregates verbatim terms into clusters with normalized text and instance counts", () => {
    store.assignments = mockAssignments;
    const clusters = store.verbatimClusters;

    // Check cluster count
    expect(clusters.length).toBe(4);

    // Find SEVERE HEADACHE cluster (OBS-1, OBS-2, OBS-6)
    const headacheCluster = clusters.find((c) =>
      c.verbatim_text.toUpperCase().includes("SEVERE HEADACHE")
    );
    expect(headacheCluster).toBeDefined();
    expect(headacheCluster.instance_count).toBe(3);
    expect(headacheCluster.unblocked_items.length).toBe(2);
    expect(headacheCluster.blocked_items.length).toBe(1);
    expect(headacheCluster.has_blocked_items).toBe(true);
    expect(headacheCluster.top_suggestion.code).toBe("10019211");
    expect(headacheCluster.top_suggestion.term).toBe("Headache");
    expect(headacheCluster.confidence_category).toBe("HIGH");
  });

  it("filters coding queue items and clusters by confidence threshold", () => {
    store.assignments = mockAssignments;

    // High confidence filter (>= 90%)
    store.filters.confidenceThreshold = "HIGH";
    let filtered = store.filteredAssignments;
    expect(filtered.map((a) => a.id)).toEqual(["OBS-1", "OBS-2", "OBS-5", "OBS-6"]);
    let clusters = store.verbatimClusters;
    expect(clusters.length).toBe(2);

    // Medium confidence filter (70% - 89%)
    store.filters.confidenceThreshold = "MEDIUM";
    filtered = store.filteredAssignments;
    expect(filtered.map((a) => a.id)).toEqual(["OBS-3"]);
    clusters = store.verbatimClusters;
    expect(clusters.length).toBe(1);

    // Low confidence filter (< 70%)
    store.filters.confidenceThreshold = "LOW";
    filtered = store.filteredAssignments;
    expect(filtered.map((a) => a.id)).toEqual(["OBS-4"]);
    clusters = store.verbatimClusters;
    expect(clusters.length).toBe(1);
  });

  it("calculates summary metrics for clusters, instances, and auto-coding progress", () => {
    store.assignments = mockAssignments;

    expect(store.totalClusterCount).toBe(4);
    expect(store.totalInstanceCount).toBe(6);
    expect(store.batchAutoCodingProgress).toBe(0);

    // Set 3 items as CODED
    store.assignments[0].status = "CODED";
    store.assignments[1].status = "CODED";
    store.assignments[4].status = "AUTO_CODED";

    expect(store.batchAutoCodingProgress).toBe(50); // 3 of 6 = 50%
  });

  it("executes batchApproveClusters and excludes blocked instances with GxP change reason", async () => {
    store.assignments = mockAssignments;
    apiClient.post.mockResolvedValue({ status: "SUCCESS", updated_count: 2 });
    apiClient.get.mockResolvedValue(mockAssignments);

    const headacheCluster = store.verbatimClusters.find((c) =>
      c.verbatim_text.toUpperCase().includes("SEVERE HEADACHE")
    );

    await store.batchApproveClusters({
      clusterKeys: [headacheCluster.clusterKey],
      reason: "High confidence verbatim cluster batch approval",
    });

    // Verify API payload sent unblocked IDs OBS-1 and OBS-2, excluding blocked OBS-6
    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/execution/coding/assignments/batch-assign",
      expect.objectContaining({
        assignment_ids: ["OBS-1", "OBS-2"],
        action: "ACCEPT",
        reason: "High confidence verbatim cluster batch approval",
      }),
      expect.objectContaining({
        changeReason: "High confidence verbatim cluster batch approval",
      })
    );
  });

  it("executes batchCodeClusters for WHODrug multi-cluster selection", async () => {
    store.assignments = mockAssignments;
    apiClient.post.mockResolvedValue({ status: "SUCCESS", updated_count: 1 });
    apiClient.get.mockResolvedValue(mockAssignments);

    const whoCluster = store.verbatimClusters.find(
      (c) => c.dictionary_type === "WHODRUG"
    );

    await store.batchCodeClusters({
      clusterKeys: [whoCluster.clusterKey],
      code: "00001201",
      term: "Acetylsalicylic acid",
      dictionaryType: "WHODRUG",
      reason: "Batch code WHODrug brand variations",
    });

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/execution/coding/assignments/batch-assign",
      expect.objectContaining({
        assignment_ids: ["OBS-5"],
        code: "00001201",
        term: "Acetylsalicylic acid",
        dictionary_type: "WHODRUG",
        action: "OVERRIDE",
        reason: "Batch code WHODrug brand variations",
      }),
      expect.objectContaining({
        changeReason: "Batch code WHODrug brand variations",
      })
    );
  });
});
