import { apiClient } from "./apiClient.js";

/**
 * Fetches user onboarding preferences for a specific persona from Org service.
 * @param {string} persona
 * @returns {Promise<{ persona: string, currentStep: number, isActive: boolean, disabled: boolean, eventsJson: string, updatedAt: string }>}
 */
export async function getOnboardingPreferences(persona = "super_admin") {
  const cleanPersona = (persona || "super_admin").trim().toLowerCase();
  return await apiClient.get(
    `/api/v1/org/preferences/onboarding/${encodeURIComponent(cleanPersona)}`
  );
}

/**
 * Saves user onboarding preferences for a specific persona to Org service.
 * @param {string} persona
 * @param {{ currentStep: number, isActive: boolean, disabled: boolean, events: Array<unknown> | string, updatedAt?: string }} data
 * @returns {Promise<{ persona: string, currentStep: number, isActive: boolean, disabled: boolean, eventsJson: string, updatedAt: string }>}
 */
export async function saveOnboardingPreferences(persona, data) {
  const cleanPersona = (persona || "super_admin").trim().toLowerCase();
  const eventsJson =
    typeof data.events === "string"
      ? data.events
      : JSON.stringify(data.events || []);

  const payload = {
    persona: cleanPersona,
    currentStep: Number(data.currentStep) || 1,
    isActive: Boolean(data.isActive),
    disabled: Boolean(data.disabled),
    eventsJson,
    updatedAt: data.updatedAt || new Date().toISOString(),
  };

  return await apiClient.put(
    `/api/v1/org/preferences/onboarding/${encodeURIComponent(cleanPersona)}`,
    payload
  );
}
