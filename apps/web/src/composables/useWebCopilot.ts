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
        recommendations.value = response.recommendations;
        return response.recommendations;
      } else if (response && Array.isArray(response.Recommendations)) {
        recommendations.value = response.Recommendations.map((r: any) => ({
          id: r.id || r.Id,
          name: r.name || r.Name,
          description: r.description || r.Description,
          category: r.category || r.Category || "[Suggested]",
          icon: r.icon || r.Icon || "🤖",
          targetRoute: r.targetRoute || r.TargetRoute,
          action: r.action || r.Action,
          confidence: r.confidence ?? r.Confidence ?? 1.0,
          roles: r.roles || r.Roles || [],
        }));
        return recommendations.value;
      }

      recommendations.value = [];
      return [];
    } catch (err: any) {
      // Fallback: AI Gateway error falls back gracefully to static filtering without UI errors
      console.warn(
        "AI Gateway intent routing request failed, falling back to local search:",
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
