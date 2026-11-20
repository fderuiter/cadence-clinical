<template>
  <div id="section-plans" class="plans-view">
    <div
      id="plans-prerelease-banner"
      class="prerelease-banner"
      role="status"
      aria-live="polite"
    >
      <strong>Pre-release status:</strong>
      {{ PRE_RELEASE_ENVIRONMENT_NOTICE }} Marketing names, packaging tiers, and
      product family boundaries are provisional design hypotheses without live
      clinical validation or commercial pricing.
    </div>

    <header class="plans-hero">
      <p class="plans-eyebrow">Cadence Clinical &bull; Modular Packaging</p>
      <h1>Evaluate Clinical Kernel product families</h1>
      <p class="plans-lead">
        Compare <strong>Individual</strong>, <strong>Select</strong>, and
        <strong>Complete</strong> packaging across eight clinical product
        families. Start a self-service trial workspace for
        <strong>Blueprint</strong> and <strong>Capture</strong>, or upgrade an
        existing workspace in place while preserving your tenant, studies, and
        audit history.
      </p>
      <div class="plans-hero-actions">
        <button
          v-if="!authStore.isAuthenticated"
          id="btn-plans-hero-register"
          type="button"
          class="btn btn-primary"
          :disabled="isBusy"
          @click="handleSelectTier('Individual')"
        >
          {{
            isBusy
              ? "Redirecting…"
              : authStore.isDemoMode
                ? "Enter Demo Workspace"
                : "Create Trial Account (Keycloak OIDC)"
          }}
        </button>
        <button
          v-if="!authStore.isAuthenticated && !authStore.isDemoMode"
          id="btn-plans-hero-signin"
          type="button"
          class="btn btn-secondary"
          :disabled="isBusy"
          @click="router.push('/login')"
        >
          Sign in to existing account
        </button>
        <button
          v-if="authStore.isAuthenticated"
          id="btn-plans-open-workspace"
          type="button"
          class="btn btn-primary"
          @click="
            router.push(
              authStore.needsTrialOnboarding ? '/onboarding/trial' : '/studies'
            )
          "
        >
          {{
            authStore.needsTrialOnboarding
              ? "Complete Trial Workspace Setup"
              : "Return to Study Workspace"
          }}
        </button>
      </div>
      <p v-if="actionError" class="plans-alert plans-alert-error" role="alert">
        {{ actionError }}
      </p>
      <p v-if="actionNotice" class="plans-alert plans-alert-ok" role="status">
        {{ actionNotice }}
      </p>
    </header>

    <section class="plans-tiers-grid" aria-label="Commercial packaging tiers">
      <article
        v-for="tier in COMMERCIAL_TIERS"
        :key="tier.code"
        :id="`plan-card-${tier.code.toLowerCase()}`"
        class="plan-card"
        :class="{
          'is-active-tier':
            authStore.isAuthenticated &&
            !authStore.needsTrialOnboarding &&
            authStore.activeTier === tier.code,
        }"
      >
        <div class="plan-card-top">
          <div class="plan-badge-row">
            <span class="plan-pill">{{ tier.trialBadge }}</span>
            <span
              v-if="
                authStore.isAuthenticated &&
                !authStore.needsTrialOnboarding &&
                authStore.activeTier === tier.code
              "
              class="plan-current-pill"
            >
              Active Workspace Tier
            </span>
          </div>
          <h2>{{ tier.name }}</h2>
          <p class="plan-subtitle">{{ tier.subtitle }}</p>
          <p class="plan-scoping">{{ tier.scopingNote }}</p>
        </div>

        <div class="plan-families-block">
          <span class="plan-block-label">Included product families</span>
          <div class="plan-family-chips">
            <span
              v-for="familyCode in tier.families"
              :key="familyCode"
              class="family-chip"
            >
              {{ familyCode }}
            </span>
          </div>
        </div>

        <ul class="plan-highlights">
          <li v-for="point in tier.highlights" :key="point">{{ point }}</li>
        </ul>

        <div class="plan-card-footer">
          <button
            :id="`btn-select-tier-${tier.code.toLowerCase()}`"
            type="button"
            class="btn btn-primary plan-cta-btn"
            :disabled="
              isBusy ||
              (authStore.isAuthenticated &&
                !authStore.needsTrialOnboarding &&
                authStore.activeTier === tier.code &&
                !authStore.isTrialExpired)
            "
            @click="handleSelectTier(tier.code)"
          >
            {{ ctaLabelFor(tier.code) }}
          </button>
        </div>
      </article>
    </section>

    <section
      class="plans-section"
      aria-labelledby="heading-baseline-guarantees"
    >
      <h2 id="heading-baseline-guarantees">
        Cadence Core regulated baseline (never gated behind add-ons)
      </h2>
      <p class="plans-section-intro">
        Commercial subscriptions govern workspace module packaging and trial
        mutation windows. Clinical authorization, blinding, consent, electronic
        signatures, and tamper-evident audit chains remain enforced by the
        Clinical Kernel across every tier.
      </p>
      <div class="guarantees-grid">
        <div
          v-for="item in BASELINE_KERNEL_GUARANTEES"
          :key="item.id"
          class="guarantee-card"
        >
          <h3>{{ item.title }}</h3>
          <p>{{ item.detail }}</p>
        </div>
      </div>
    </section>

    <section class="plans-section" aria-labelledby="heading-product-families">
      <h2 id="heading-product-families">
        Eight clinical product families &amp; pre-release delivery status
      </h2>
      <div class="families-grid">
        <article
          v-for="family in PRODUCT_FAMILIES"
          :key="family.code"
          class="family-card"
        >
          <div class="family-card-header">
            <h3>{{ family.name }}</h3>
            <span class="family-tier-tag">{{ family.minimumTier }}+</span>
          </div>
          <p class="family-responsibility">{{ family.responsibility }}</p>
          <p class="family-summary">{{ family.summary }}</p>
          <p class="family-apps">
            <strong>Candidate applications:</strong>
            {{ family.candidateApps.join(", ") }}
          </p>
          <p class="family-status">{{ family.deliveryStatus }}</p>
        </article>
      </div>
    </section>

    <section class="plans-section" aria-labelledby="heading-modular-benchmark">
      <h2 id="heading-modular-benchmark">
        Modular study scoping benchmark (Castor &amp; specialist EDC workflows)
      </h2>
      <p class="plans-section-intro">
        Benchmarked against documented self-service onboarding and modular study
        scoping patterns in Castor EDC and Veeva SiteVault. Pricing metrics are
        intentionally unset during pre-release partner discovery; no payment
        card or billing details are collected.
      </p>
      <div class="benchmark-grid">
        <div
          v-for="row in MODULAR_SCOPING_BENCHMARK"
          :key="row.step"
          class="benchmark-card"
        >
          <h3>{{ row.step }}</h3>
          <p><strong>Industry pattern:</strong> {{ row.industryPattern }}</p>
          <p><strong>Cadence design:</strong> {{ row.cadenceApproach }}</p>
        </div>
      </div>
    </section>
  </div>
</template>

<script setup>
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { kernelApi } from "../api/kernelApi";
import {
  BASELINE_KERNEL_GUARANTEES,
  COMMERCIAL_TIERS,
  MODULAR_SCOPING_BENCHMARK,
  PRE_RELEASE_ENVIRONMENT_NOTICE,
  PRODUCT_FAMILIES,
} from "../catalog/productCatalog.js";
import { useAuthStore } from "../stores/auth";

const authStore = useAuthStore();
const router = useRouter();

const isBusy = ref(false);
const actionError = ref("");
const actionNotice = ref("");

onMounted(() => {
  if (authStore.isAuthenticated) {
    authStore.loadEntitlements();
  }
});

function ctaLabelFor(tierCode) {
  if (!authStore.isAuthenticated) {
    return authStore.isDemoMode
      ? `Explore ${tierCode} in Demo`
      : `Start ${tierCode} Trial`;
  }
  if (authStore.needsTrialOnboarding || !authStore.identity?.tenantId) {
    return `Set up ${tierCode} Trial Workspace`;
  }
  if (authStore.activeTier === tierCode && !authStore.isTrialExpired) {
    return `Active (${tierCode})`;
  }
  if (authStore.hasRole("Sponsor Admin")) {
    return `Activate ${tierCode} in Workspace`;
  }
  return `Open Study Workspace`;
}

async function handleSelectTier(tierCode) {
  actionError.value = "";
  actionNotice.value = "";

  if (!authStore.isAuthenticated) {
    if (authStore.isDemoMode) {
      await authStore.login();
      await router.push("/studies");
      return;
    }
    isBusy.value = true;
    try {
      const targetHref = router.resolve({
        path: "/onboarding/trial",
        query: { tier: tierCode },
      }).href;
      await authStore.register({
        redirectUri: new URL(targetHref, window.location.origin).toString(),
      });
    } catch (err) {
      actionError.value = err?.message || "Could not start registration.";
      isBusy.value = false;
    }
    return;
  }

  if (authStore.needsTrialOnboarding || !authStore.identity?.tenantId) {
    await router.push({ path: "/onboarding/trial", query: { tier: tierCode } });
    return;
  }

  if (!authStore.hasRole("Sponsor Admin")) {
    await router.push("/studies");
    return;
  }

  isBusy.value = true;
  try {
    const idempotencyKey = `web-tier-upgrade-${authStore.identity?.tenantId || "tenant"}-${tierCode.toLowerCase()}-${Date.now()}`;
    const updated = await kernelApi.upgradeSubscriptionTier({
      tier: tierCode,
      idempotencyKey,
      reason: `Evaluate ${tierCode} commercial tier in pre-release workspace`,
    });
    authStore.entitlements = updated;
    authStore.entitlementsLoaded = true;
    actionNotice.value = `Workspace updated to ${updated.tier} (${updated.product_families.join(", ")}). Existing tenant, studies, and audit records were preserved.`;
  } catch (err) {
    actionError.value =
      err?.message || "Could not update workspace tier entitlement.";
  } finally {
    isBusy.value = false;
  }
}
</script>

<style scoped>
.plans-view {
  max-width: 1120px;
  margin: 0 auto;
  padding: 24px 20px 56px;
  color: #0f172a;
}

.prerelease-banner {
  background: #fffbeb;
  border: 1px solid #f59e0b;
  color: #92400e;
  border-radius: 8px;
  padding: 12px 16px;
  font-size: 0.9rem;
  line-height: 1.45;
  margin-bottom: 24px;
}

.plans-hero {
  text-align: center;
  max-width: 780px;
  margin: 0 auto 32px;
}

.plans-eyebrow {
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-size: 0.78rem;
  font-weight: 700;
  color: #1d4ed8;
  margin-bottom: 8px;
}

.plans-hero h1 {
  font-size: 2rem;
  margin: 0 0 12px;
}

.plans-lead {
  color: #475569;
  line-height: 1.6;
  margin-bottom: 20px;
}

.plans-hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  justify-content: center;
}

.plans-alert {
  margin-top: 14px;
  padding: 10px 14px;
  border-radius: 6px;
  font-size: 0.9rem;
}

.plans-alert-error {
  background: #fef2f2;
  border: 1px solid #fca5a5;
  color: #991b1b;
}

.plans-alert-ok {
  background: #f0fdf4;
  border: 1px solid #86efac;
  color: #166534;
}

.plans-tiers-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 20px;
  margin-bottom: 40px;
}

.plan-card {
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 12px;
  padding: 24px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  box-shadow: 0 2px 6px rgb(15 23 42 / 0.05);
}

.plan-card.is-active-tier {
  border: 2px solid #2563eb;
}

.plan-badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 10px;
}

.plan-pill {
  background: #eff6ff;
  color: #1d4ed8;
  font-size: 0.75rem;
  font-weight: 700;
  padding: 3px 10px;
  border-radius: 999px;
}

.plan-current-pill {
  background: #dcfce7;
  color: #166534;
  font-size: 0.75rem;
  font-weight: 700;
  padding: 3px 10px;
  border-radius: 999px;
}

.plan-subtitle {
  color: #334155;
  font-weight: 600;
  margin: 6px 0 8px;
}

.plan-scoping {
  font-size: 0.86rem;
  color: #64748b;
  margin-bottom: 16px;
}

.plan-families-block {
  margin-bottom: 16px;
}

.plan-block-label {
  display: block;
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  color: #475569;
  margin-bottom: 8px;
}

.plan-family-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.family-chip {
  background: #f1f5f9;
  border: 1px solid #cbd5e1;
  color: #0f172a;
  font-size: 0.8rem;
  font-weight: 600;
  padding: 3px 9px;
  border-radius: 6px;
}

.plan-highlights {
  padding-left: 18px;
  margin: 0 0 20px;
  color: #334155;
  font-size: 0.9rem;
  line-height: 1.5;
}

.plan-highlights li {
  margin-bottom: 8px;
}

.plan-cta-btn {
  width: 100%;
  justify-content: center;
}

.plans-section {
  margin-top: 40px;
}

.plans-section h2 {
  font-size: 1.35rem;
  margin-bottom: 8px;
}

.plans-section-intro {
  color: #475569;
  margin-bottom: 18px;
  max-width: 820px;
}

.guarantees-grid,
.families-grid,
.benchmark-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 16px;
}

.guarantee-card,
.family-card,
.benchmark-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 18px;
}

.guarantee-card h3,
.family-card h3,
.benchmark-card h3 {
  font-size: 1rem;
  margin: 0 0 8px;
}

.family-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.family-tier-tag {
  background: #f8fafc;
  border: 1px solid #cbd5e1;
  border-radius: 999px;
  padding: 2px 8px;
  font-size: 0.75rem;
  font-weight: 600;
  color: #334155;
}

.family-responsibility {
  font-size: 0.84rem;
  font-weight: 600;
  color: #1d4ed8;
  margin-bottom: 6px;
}

.family-summary,
.family-apps {
  font-size: 0.86rem;
  color: #475569;
  margin-bottom: 8px;
}

.family-status {
  font-size: 0.8rem;
  font-weight: 600;
  color: #0f766e;
  margin: 0;
}
</style>
