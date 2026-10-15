import { describe, it, expect, vi, beforeEach } from "vitest";
import { useWebCopilot } from "../useWebCopilot";
import { apiClient } from "../../api/apiClient";

vi.mock("../../api/apiClient", () => ({
  apiClient: {
    post: vi.fn(),
  },
}));

describe("useWebCopilot composable", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("returns empty array when query is empty or whitespace", async () => {
    const { fetchIntentRecommendations, recommendations } = useWebCopilot();

    const res = await fetchIntentRecommendations("   ");
    expect(res).toEqual([]);
    expect(recommendations.value).toEqual([]);
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it("fetches intent recommendations from AI Gateway endpoint", async () => {
    const mockRecommendations = [
      {
        id: "ai-intent-log-ae-101",
        name: "Log Adverse Event for Subject SUBJ-101",
        description: "Directly navigate to eCRF data entry for subject SUBJ-101",
        category: "[AI Copilot]",
        icon: "🩺",
        targetRoute: "/ecrf?subjectId=SUBJ-101&action=log_ae",
        action: "log_ae",
        confidence: 0.98,
        roles: ["site_investigator", "crc"],
      },
    ];

    (apiClient.post as any).mockResolvedValueOnce({
      query: "log adverse event for subject 101",
      recommendations: mockRecommendations,
    });

    const { fetchIntentRecommendations, recommendations, loading, error } =
      useWebCopilot();

    const res = await fetchIntentRecommendations(
      "log adverse event for subject 101"
    );

    expect(apiClient.post).toHaveBeenCalledWith("/api/v1/ai/intent/route", {
      query: "log adverse event for subject 101",
      context: undefined,
    });
    expect(res).toEqual(mockRecommendations);
    expect(recommendations.value).toEqual(mockRecommendations);
    expect(loading.value).toBe(false);
    expect(error.value).toBeNull();
  });

  it("debounces query calls to AI Gateway endpoint", async () => {
    (apiClient.post as any).mockResolvedValue({
      query: "log ae",
      recommendations: [],
    });

    const { fetchIntentDebounced } = useWebCopilot();

    const promise1 = fetchIntentDebounced("log", {}, 100);
    const promise2 = fetchIntentDebounced("log ae", {}, 100);

    await promise2;

    expect(apiClient.post).toHaveBeenCalledTimes(1);
    expect(apiClient.post).toHaveBeenCalledWith("/api/v1/ai/intent/route", {
      query: "log ae",
      context: {},
    });
  });

  it("falls back gracefully on network or API failure", async () => {
    (apiClient.post as any).mockRejectedValueOnce(
      new Error("Network Error or HTTP 500")
    );

    const { fetchIntentRecommendations, recommendations, error } =
      useWebCopilot();

    const res = await fetchIntentRecommendations("trigger error");

    expect(res).toEqual([]);
    expect(recommendations.value).toEqual([]);
    expect(error.value).toBe("Network Error or HTTP 500");
  });
});
