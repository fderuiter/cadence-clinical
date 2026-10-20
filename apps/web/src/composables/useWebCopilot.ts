/**
 * Vue 3 composable for command palette navigation suggestions from the rule-based intent router.
 */
import { ref } from "vue";
import { apiClient } from "../api/apiClient";

export interface IntentRecommendation {
  id: string;
  name: string;
  description: string;
  category: string;
  icon: string;
  targetRoute: string;
  action?: string;
  confidence: number;
  roles: string[];
}

export interface IntentRouteResponse {
  query: string;
  recommendations: IntentRecommendation[];
}

/** The kernel host's JSON is snake_case (`target_route`); the palette reads camelCase. */
function fromWire(r: any): IntentRecommendation {
  return {
    id: r.id,
    name: r.name,
    description: r.description,
    category: r.category || "[Suggested]",
    icon: r.icon || "🤖",
    targetRoute: r.target_route,
    action: r.action ?? undefined,
    confidence: r.confidence ?? 1.0,
    roles: r.roles || [],
  };
}

export function useWebCopilot() {
  const recommendations = ref<IntentRecommendation[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;

  async function fetchIntentRecommendations(
    query: string,
    context?: Record<string, any>
  ): Promise<IntentRecommendation[]> {
    const trimmedQuery = query?.trim() || "";
    if (!trimmedQuery) {
      recommendations.value = [];
      loading.value = false;
      error.value = null;
      return [];
    }

    loading.value = true;
    error.value = null;

    try {
      const response: any = await apiClient.post("/api/v1/ai/intent/route", {
        query: trimmedQuery,
        context,
      });

      if (response && Array.isArray(response.recommendations)) {
        recommendations.value = response.recommendations.map(fromWire);
        return recommendations.value;
      }

      recommendations.value = [];
      return [];
    } catch (err: any) {
      // A failed request falls back to the palette's local search without a UI error.
      console.warn(
        "Intent routing request failed, falling back to local search:",
        err?.message || err
      );
      error.value = err?.message || "Failed to fetch intent recommendations";
      recommendations.value = [];
      return [];
    } finally {
      loading.value = false;
    }
  }

  function fetchIntentDebounced(
    query: string,
    context?: Record<string, any>,
    delayMs = 250
  ): Promise<IntentRecommendation[]> {
    if (debounceTimer) {
      clearTimeout(debounceTimer);
    }

    return new Promise<IntentRecommendation[]>((resolve) => {
      debounceTimer = setTimeout(async () => {
        const res = await fetchIntentRecommendations(query, context);
        resolve(res);
      }, delayMs);
    });
  }

  function clearRecommendations() {
    if (debounceTimer) {
      clearTimeout(debounceTimer);
      debounceTimer = null;
    }
    recommendations.value = [];
    loading.value = false;
    error.value = null;
  }

  return {
    recommendations,
    loading,
    error,
    fetchIntentRecommendations,
    fetchIntentDebounced,
    clearRecommendations,
  };
}

export default useWebCopilot;
