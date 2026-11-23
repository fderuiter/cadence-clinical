<template>
  <div id="section-trial-setup" class="trial-setup-view">
    <div
      id="trial-setup-prerelease-banner"
      class="prerelease-banner"
      role="status"
    >
      <strong>Pre-release status:</strong>
      {{ PRE_RELEASE_ENVIRONMENT_NOTICE }} Use synthetic study data; no payment
      card or billing commitment is collected.
    </div>

    <div class="setup-card">
      <header class="setup-header">
        <p class="setup-eyebrow">Trial Workspace Setup</p>
        <h1>Set up your trial workspace</h1>
        <p class="setup-subtitle">
          Signed in as
          <strong>{{ authStore.identity?.username || "your account" }}</strong>.
          Create an isolated study workspace for your sponsor organization,
          starter clinical site institution, and sandbox study site with Sponsor
          Administrator, Protocol Designer, and Site Investigator access and
          starter product entitlements.
        </p>
      </header>

      <form id="form-trial-onboarding" @submit.prevent="submitOnboarding">
        <div class="form-field">
          <label for="input-organization-name">
            Sponsor or research organization name
          </label>
          <input
            id="input-organization-name"
            v-model="organizationName"
            type="text"
            maxlength="200"
            required
            placeholder="e.g. Meridian Vascular Research"
            :disabled="isSubmitting"
          />
          <span class="field-hint">
            Recorded as your research organization. Personal names and email
            addresses are kept separate from clinical participant records to
            protect pseudonymous identity.
          </span>
        </div>

        <div class="form-field">
          <label for="select-residency">Data residency region</label>
          <select
            id="select-residency"
            v-model="residency"
            :disabled="isSubmitting"
          >
            <option value="">Unrestricted (global evaluation)</option>
            <option value="EU">European Union (EU)</option>
            <option value="US">United States (US)</option>
            <option value="GB">United Kingdom (GB)</option>
            <option value="CA">Canada (CA)</option>
            <option value="APAC">Asia-Pacific (APAC)</option>
          </select>
          <span class="field-hint">
            Sets your workspace data isolation and regional residency boundary,
            independent of your subscription plan.
          </span>
        </div>

        <div class="form-field">
          <label for="select-trial-tier">Evaluation packaging tier</label>
          <select
            id="select-trial-tier"
            v-model="selectedTier"
            :disabled="isSubmitting"
          >
            <option
              v-for="tier in COMMERCIAL_TIERS"
              :key="tier.code"
              :value="tier.code"
            >
              {{ tier.name }} — {{ tier.families.join(", ") }}
            </option>
          </select>
          <span class="field-hint">
            Every trial workspace includes <strong>Blueprint</strong> +
            <strong>Capture</strong> plus Cadence Core audit, electronic
            signatures, and clinical authorization.
          </span>
        </div>

        <p v-if="errorMessage" class="setup-error" role="alert">
          {{ errorMessage }}
        </p>

        <div class="setup-actions">
          <button
            id="btn-submit-trial-onboarding"
            type="submit"
            class="btn btn-primary"
            :disabled="isSubmitting || !organizationName.trim()"
          >
            {{
              isSubmitting
                ? "Setting up trial workspace…"
                : "Launch Trial Workspace"
            }}
          </button>
          <router-link to="/plans" class="btn btn-secondary">
            Compare plans
          </router-link>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { kernelApi } from "../api/kernelApi";
import {
  COMMERCIAL_TIERS,
  findCommercialTier,
  PRE_RELEASE_ENVIRONMENT_NOTICE,
} from "../catalog/productCatalog.js";
import { useAuthStore } from "../stores/auth";

const authStore = useAuthStore();
const route = useRoute();
const router = useRouter();

const initialTier = findCommercialTier(route.query.tier).code;
const organizationName = ref("");
const residency = ref("");
const selectedTier = ref(initialTier);
const isSubmitting = ref(false);
const errorMessage = ref("");

async function submitOnboarding() {
  const trimmedName = organizationName.value.trim();
  if (!trimmedName) {
    errorMessage.value = "Enter a sponsor or research organization name.";
    return;
  }

  errorMessage.value = "";
  isSubmitting.value = true;
  try {
    const response = await kernelApi.bootstrapTrial({
      organization_name: trimmedName,
      residency: residency.value || null,
      tier: selectedTier.value,
    });

    authStore.entitlements = response.entitlements;
    authStore.entitlementsLoaded = true;

    try {
      await authStore.refresh(-1);
    } catch {
      // In test or offline environments without a live token endpoint, keep local state consistent.
    }

    if (authStore.user && !authStore.user.tenantId && response.tenant_code) {
      authStore.user = {
        ...authStore.user,
        tenantId: response.tenant_code,
      };
    }
    if (
      Array.isArray(response.assigned_roles) &&
      response.assigned_roles.length > 0 &&
      authStore.availablePersonas.length === 0
    ) {
      authStore.rawRoles = [
        ...new Set([...authStore.rawRoles, ...response.assigned_roles]),
      ];
    }
    const fallbackSites = Array.isArray(response.assigned_site_ids)
      ? response.assigned_site_ids
      : response.sandbox_site_code
        ? [response.sandbox_site_code]
        : [];
    if (fallbackSites.length > 0 && authStore.assignedSiteIds.length === 0) {
      authStore.siteIds = [...fallbackSites];
    }
    if (!authStore.currentPersona && authStore.availablePersonas.length > 0) {
      authStore.currentPersona = authStore.availablePersonas[0].key;
    }

    await authStore.loadEntitlements({ force: true });
    await router.push("/studies");
  } catch (err) {
    const detail =
      err?.data?.detail ||
      err?.message ||
      "Could not provision trial workspace. Please retry.";
    errorMessage.value = detail;
  } finally {
    isSubmitting.value = false;
  }
}
</script>

<style scoped>
.trial-setup-view {
  max-width: 680px;
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
  margin: 28px auto 56px;
  padding: 0 20px;
  color: var(--semantic-color-text);
  overflow-wrap: anywhere;
}

.prerelease-banner {
  background: var(--semantic-color-warning-bg-light);
  border: 1px solid var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-warning-text);
  border-radius: 8px;
  padding: 12px 16px;
  font-size: 0.88rem;
  line-height: 1.45;
  margin-bottom: 20px;
}

.setup-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 12px;
  padding: 28px;
  box-shadow: 0 4px 12px rgb(15 23 42 / 0.06);
}

.setup-eyebrow {
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-size: 0.76rem;
  font-weight: 700;
  color: var(--semantic-color-interactive-primary);
  margin: 0 0 6px;
}

.setup-header h1 {
  font-size: 1.6rem;
  margin: 0 0 8px;
}

.setup-subtitle {
  color: var(--semantic-color-text-secondary);
  font-size: 0.94rem;
  line-height: 1.5;
  margin-bottom: 24px;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 18px;
}

.form-field label {
  font-weight: 600;
  font-size: 0.92rem;
}

.form-field input,
.form-field select {
  padding: 10px 12px;
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-radius: 6px;
  font-size: 0.95rem;
}

.field-hint {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.4;
}

.setup-error {
  background: var(--semantic-color-error-bg);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-dark);
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.9rem;
  margin-bottom: 16px;
}

.setup-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
}
</style>
