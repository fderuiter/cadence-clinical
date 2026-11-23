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

    <section
      v-if="
        authStore.isAuthenticated &&
        !authStore.needsTrialOnboarding &&
        authStore.isTrialExpired
      "
      id="plans-expired-banner"
      class="plans-status-banner is-expired"
      aria-label="Expired trial workspace status"
    >
      <div>
        <h2>Trial Expired &mdash; Read &amp; Export Mode Active</h2>
        <p>
          Your 30-day workspace trial
          <template v-if="authStore.trialEndsAtFormatted">
            ended on <strong>{{ authStore.trialEndsAtFormatted }}</strong>
          </template>
          <template v-else>has ended</template>. Clinical record viewing,
          regulated exports, study lock closeout, and form signing remain
          available while workspace mutations are paused. Direct tier switching
          is disabled; submit the structured study scoping intake below with a
          signed commercial quote token to activate full write access.
        </p>
      </div>
      <a
        id="btn-jump-scoping-intake"
        href="#scoped-upgrade-intake"
        class="btn btn-primary"
      >
        Go to Study Scoping Intake
      </a>
    </section>

    <section
      v-else-if="
        authStore.isAuthenticated &&
        !authStore.needsTrialOnboarding &&
        authStore.subscriptionStatus === 'Trialing'
      "
      id="plans-trial-status-banner"
      class="plans-status-banner is-trialing"
      aria-label="Active trial countdown status"
    >
      <div>
        <h2>
          Active {{ authStore.activeTier }} Trial<template
            v-if="
              authStore.trialDaysRemaining !== null &&
              authStore.trialEndsAtFormatted
            "
          >
            &mdash; {{ authStore.trialDaysRemaining }}
            {{ authStore.trialDaysRemaining === 1 ? "day" : "days" }} left (ends
            {{ authStore.trialEndsAtFormatted }})
          </template>
        </h2>
        <p>
          Switching tiers during an active trial updates enabled product
          families immediately while keeping your workspace in trial status and
          preserving the original 30-day trial end timestamp. To transition to
          an active commercial subscription, complete the study scoping intake
          below.
        </p>
      </div>
      <a href="#scoped-upgrade-intake" class="btn btn-secondary">
        Activate Subscription
      </a>
    </section>

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
                (authStore.isTrialExpired ||
                  authStore.activeTier === tier.code))
            "
            @click="handleSelectTier(tier.code)"
          >
            {{ ctaLabelFor(tier.code) }}
          </button>
        </div>
      </article>
    </section>

    <section
      v-if="
        authStore.isAuthenticated &&
        !authStore.needsTrialOnboarding &&
        authStore.hasRole('Sponsor Admin')
      "
      id="scoped-upgrade-intake"
      class="plans-section scoping-intake-section"
      aria-labelledby="heading-scoped-upgrade"
    >
      <h2 id="heading-scoped-upgrade">Scoped Study Subscription Activation</h2>
      <p class="plans-section-intro">
        Transitioning a workspace from Trial or Expired status to an Active
        commercial subscription requires structured study scoping and a
        cryptographically signed commercial quote token. Live payment card
        processing is not used; Clinical Kernel visibility, study grants,
        blinding, and consent remain governed separately from commercial tiers.
      </p>

      <form
        id="form-scoped-activation"
        class="scoping-form"
        novalidate
        @submit.prevent="handleActivateSubscription"
      >
        <div class="scoping-grid">
          <div class="scoping-field">
            <label for="select-activation-tier">Target packaging tier</label>
            <select
              id="select-activation-tier"
              v-model="scopingForm.tier"
              :disabled="isBusy"
            >
              <option
                v-for="tierOption in COMMERCIAL_TIERS"
                :key="tierOption.code"
                :value="tierOption.code"
              >
                {{ tierOption.name }}
              </option>
            </select>
            <p v-if="scopingErrors.tier" class="field-error" role="alert">
              {{ scopingErrors.tier.join(" ") }}
            </p>
          </div>

          <div class="scoping-field">
            <label for="select-study-phase">Primary study phase</label>
            <select
              id="select-study-phase"
              v-model="scopingForm.studyPhase"
              :disabled="isBusy"
            >
              <option value="">Select study phase</option>
              <option value="Phase I">Phase I</option>
              <option value="Phase I/II">Phase I/II</option>
              <option value="Phase II">Phase II</option>
              <option value="Phase II/III">Phase II/III</option>
              <option value="Phase III">Phase III</option>
              <option value="Phase IV">Phase IV</option>
              <option value="Observational">Observational</option>
            </select>
            <p
              v-if="scopingErrors.study_phase"
              id="error-study-phase"
              class="field-error"
              role="alert"
            >
              {{ scopingErrors.study_phase.join(" ") }}
            </p>
          </div>

          <div class="scoping-field">
            <label for="input-study-duration-months">
              Planned study duration (months)
            </label>
            <input
              id="input-study-duration-months"
              v-model="scopingForm.studyDurationMonths"
              type="number"
              min="1"
              max="240"
              placeholder="e.g. 18"
              :disabled="isBusy"
            />
            <p
              v-if="scopingErrors.study_duration_months"
              id="error-study-duration-months"
              class="field-error"
              role="alert"
            >
              {{ scopingErrors.study_duration_months.join(" ") }}
            </p>
          </div>

          <div class="scoping-field">
            <label for="input-study-countries">
              Participating countries (comma-separated ISO codes or names)
            </label>
            <input
              id="input-study-countries"
              v-model="scopingForm.countriesInput"
              type="text"
              placeholder="e.g. US, DE, NL"
              :disabled="isBusy"
            />
            <p
              v-if="scopingErrors.countries"
              id="error-countries"
              class="field-error"
              role="alert"
            >
              {{ scopingErrors.countries.join(" ") }}
            </p>
          </div>

          <div class="scoping-field">
            <label for="input-site-count">Planned clinical site count</label>
            <input
              id="input-site-count"
              v-model="scopingForm.siteCount"
              type="number"
              min="1"
              max="10000"
              placeholder="e.g. 12"
              :disabled="isBusy"
            />
            <p
              v-if="scopingErrors.site_count"
              id="error-site-count"
              class="field-error"
              role="alert"
            >
              {{ scopingErrors.site_count.join(" ") }}
            </p>
          </div>

          <div class="scoping-field">
            <label for="input-participant-count">
              Planned participant enrollment count
            </label>
            <input
              id="input-participant-count"
              v-model="scopingForm.participantCount"
              type="number"
              min="1"
              :max="MAX_PARTICIPANT_COUNT"
              placeholder="e.g. 240"
              :disabled="isBusy"
            />
            <p
              v-if="scopingErrors.participant_count"
              id="error-participant-count"
              class="field-error"
              role="alert"
            >
              {{ scopingErrors.participant_count.join(" ") }}
            </p>
          </div>

          <div class="scoping-field">
            <label for="input-active-study-count">
              Concurrent active study count
            </label>
            <input
              id="input-active-study-count"
              v-model="scopingForm.activeStudyCount"
              type="number"
              min="1"
              max="1000"
              placeholder="e.g. 2"
              :disabled="isBusy"
            />
            <p
              v-if="scopingErrors.active_study_count"
              id="error-active-study-count"
              class="field-error"
              role="alert"
            >
              {{ scopingErrors.active_study_count.join(" ") }}
            </p>
          </div>

          <div class="scoping-field">
            <label for="input-billing-confirmation">
              Signed commercial quote token
            </label>
            <input
              id="input-billing-confirmation"
              v-model="scopingForm.billingConfirmation"
              type="text"
              placeholder="e.g. qtk1.eyJ0ZW5hbnRfaWQiOi..."
              :disabled="isBusy"
            />
            <p
              v-if="scopingErrors.billing_confirmation"
              id="error-billing-confirmation"
              class="field-error"
              role="alert"
            >
              {{ scopingErrors.billing_confirmation.join(" ") }}
            </p>
          </div>
        </div>

        <p
          v-if="activationError"
          id="scoped-activation-error"
          class="plans-alert plans-alert-error"
          role="alert"
        >
          {{ activationError }}
        </p>
        <p
          v-if="activationSuccess"
          id="scoped-activation-success"
          class="plans-alert plans-alert-ok"
          role="status"
        >
          {{ activationSuccess }}
        </p>

        <div class="scoping-actions">
          <button
            id="btn-submit-scoped-activation"
            type="submit"
            class="btn btn-primary"
            :disabled="isBusy"
          >
            {{
              isBusy
                ? "Validating & Activating…"
                : `Confirm Study Scoping & Activate ${scopingForm.tier}`
            }}
          </button>
        </div>
      </form>
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
import { onMounted, reactive, ref, watch } from "vue";
import { useRouter } from "vue-router";
import {
  describeKernelError,
  extractKernelFieldErrors,
  kernelApi,
} from "../api/kernelApi";
import {
  BASELINE_KERNEL_GUARANTEES,
  COMMERCIAL_TIERS,
  MODULAR_SCOPING_BENCHMARK,
  PRE_RELEASE_ENVIRONMENT_NOTICE,
  PRODUCT_FAMILIES,
} from "../catalog/productCatalog.js";
import { useAuthStore } from "../stores/auth";
import { demoNowMs, nextDemoStep } from "../utils/demoMode.js";

const MAX_PARTICIPANT_COUNT = 1e6;
const authStore = useAuthStore();
const router = useRouter();

const isBusy = ref(false);
const actionError = ref("");
const actionNotice = ref("");
const activationError = ref("");
const activationSuccess = ref("");
const scopingErrors = ref({});

const scopingForm = reactive({
  tier: authStore.activeTier || "Select",
  studyPhase: "",
  studyDurationMonths: "",
  countriesInput: "",
  siteCount: "",
  participantCount: "",
  activeStudyCount: "",
  billingConfirmation: "",
});

function populateScopingFromEntitlements(entitlements) {
  if (!entitlements) return;
  if (entitlements.tier) {
    scopingForm.tier = entitlements.tier;
  }
  const existing = entitlements.scoping;
  if (!existing) return;
  scopingForm.studyPhase = existing.study_phase || "";
  scopingForm.studyDurationMonths =
    existing.study_duration_months != null
      ? String(existing.study_duration_months)
      : "";
  scopingForm.countriesInput = Array.isArray(existing.countries)
    ? existing.countries.join(", ")
    : "";
  scopingForm.siteCount =
    existing.site_count != null ? String(existing.site_count) : "";
  scopingForm.participantCount =
    existing.participant_count != null
      ? String(existing.participant_count)
      : "";
  scopingForm.activeStudyCount =
    existing.active_study_count != null
      ? String(existing.active_study_count)
      : "";
  scopingForm.billingConfirmation = existing.billing_confirmation || "";
}

watch(
  () => authStore.entitlements,
  (nextEntitlements) => {
    populateScopingFromEntitlements(nextEntitlements);
  },
  { immediate: true }
);

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
  if (authStore.isTrialExpired) {
    return `Requires Scoped Activation`;
  }
  if (authStore.activeTier === tierCode) {
    return authStore.subscriptionStatus === "Trialing"
      ? `Active Trial (${tierCode})`
      : `Active (${tierCode})`;
  }
  if (authStore.hasRole("Sponsor Admin")) {
    return authStore.subscriptionStatus === "Trialing"
      ? `Switch Trial to ${tierCode}`
      : `Switch Workspace to ${tierCode}`;
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

  if (authStore.isTrialExpired) {
    scopingForm.tier = tierCode;
    actionError.value =
      "Direct tier switching is unavailable once a trial expires. Complete the study scoping intake below to activate your workspace.";
    return;
  }

  isBusy.value = true;
  try {
    const idempotencyKey = `web-tier-switch-${authStore.identity?.tenantId || "tenant"}-${tierCode.toLowerCase()}-${demoNowMs(nextDemoStep("plans-tier-switch"))}`;
    const updated = await kernelApi.upgradeSubscriptionTier({
      tier: tierCode,
      idempotencyKey,
      reason: `Switch workspace commercial tier to ${tierCode}`,
    });
    authStore.applyEntitlements(updated);
    scopingForm.tier = updated.tier;
    actionNotice.value =
      updated.status === "Trialing"
        ? `Trial tier switched to ${updated.tier} (${updated.product_families.join(", ")}). Trial countdown and existing clinical records remain unchanged.`
        : `Workspace updated to ${updated.tier} (${updated.product_families.join(", ")}). Existing tenant, studies, and audit records were preserved.`;
  } catch (err) {
    actionError.value =
      describeKernelError(err) ||
      "Could not update workspace tier entitlement.";
  } finally {
    isBusy.value = false;
  }
}

function parseOptionalInt(raw) {
  if (raw === "" || raw === null || raw === undefined) return null;
  const parsed = Number.parseInt(String(raw), 10);
  return Number.isNaN(parsed) ? -1 : parsed;
}

async function handleActivateSubscription() {
  activationError.value = "";
  activationSuccess.value = "";
  scopingErrors.value = {};

  const countries = String(scopingForm.countriesInput || "")
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);

  isBusy.value = true;
  try {
    const idempotencyKey = `web-scoped-activate-${authStore.identity?.tenantId || "tenant"}-${scopingForm.tier.toLowerCase()}-${demoNowMs(nextDemoStep("plans-scoped-activate"))}`;
    const updated = await kernelApi.activateSubscription({
      tier: scopingForm.tier,
      studyPhase: scopingForm.studyPhase.trim() || null,
      studyDurationMonths: parseOptionalInt(scopingForm.studyDurationMonths),
      countries,
      siteCount: parseOptionalInt(scopingForm.siteCount),
      participantCount: parseOptionalInt(scopingForm.participantCount),
      activeStudyCount: parseOptionalInt(scopingForm.activeStudyCount),
      billingConfirmation: scopingForm.billingConfirmation.trim() || null,
      idempotencyKey,
      reason: `Activate ${scopingForm.tier} commercial subscription with validated study scoping`,
    });
    authStore.applyEntitlements(updated);
    activationSuccess.value = `Activated ${updated.tier} subscription (${updated.product_families.join(", ")}). Full workspace write access is active.`;
  } catch (err) {
    const extracted = extractKernelFieldErrors(err);
    scopingErrors.value = extracted;
    activationError.value =
      describeKernelError(err) ||
      "Study scoping validation failed. Review highlighted fields and try again.";
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
  box-sizing: border-box;
  color: var(--semantic-color-text);
}

.prerelease-banner {
  background: var(--semantic-color-warning-bg-light);
  border: 1px solid var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-warning-text);
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
  color: var(--semantic-color-interactive-primary);
  margin-bottom: 8px;
}

.plans-hero h1 {
  font-size: 2rem;
  margin: 0 0 12px;
}

.plans-lead {
  color: var(--semantic-color-text-secondary);
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
  background: var(--semantic-color-error-bg);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-dark);
}

.plans-alert-ok {
  background: var(--semantic-color-success-bg);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.plans-status-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 16px;
  padding: 18px 22px;
  border-radius: 12px;
  margin-bottom: 28px;
}

.plans-status-banner h2 {
  font-size: 1.15rem;
  margin: 0 0 6px;
}

.plans-status-banner p {
  margin: 0;
  max-width: 780px;
  line-height: 1.5;
  font-size: 0.92rem;
}

.plans-status-banner.is-trialing {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-text);
}

.plans-status-banner.is-expired {
  background: var(--semantic-color-error-bg);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-dark);
}

.scoping-intake-section {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 12px;
  padding: clamp(16px, 4vw, 28px);
}

.scoping-form {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.scoping-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 240px), 1fr));
  gap: 16px;
}

.scoping-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.scoping-field label {
  font-size: 0.86rem;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.scoping-field input,
.scoping-field select {
  width: 100%;
  max-width: 100%;
  min-width: 0;
  box-sizing: border-box;
  padding: 9px 11px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: var(--semantic-font-size-control);
}

.field-error {
  margin: 0;
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-error-dark);
}

.scoping-actions {
  display: flex;
  justify-content: flex-start;
}

.plans-tiers-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 300px), 1fr));
  gap: 20px;
  margin-bottom: 40px;
}

.plan-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 12px;
  padding: clamp(16px, 4vw, 24px);
  min-width: 0;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  box-shadow: 0 2px 6px rgb(15 23 42 / 0.05);
}

.plan-card.is-active-tier {
  border: 2px solid var(--semantic-color-interactive-primary);
}

.plan-badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 10px;
}

.plan-pill {
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
  font-size: 0.75rem;
  font-weight: 700;
  padding: 3px 10px;
  border-radius: 999px;
}

.plan-current-pill {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
  font-size: 0.75rem;
  font-weight: 700;
  padding: 3px 10px;
  border-radius: 999px;
}

.plan-subtitle {
  color: var(--semantic-color-text-body);
  font-weight: 600;
  margin: 6px 0 8px;
}

.plan-scoping {
  font-size: 0.86rem;
  color: var(--semantic-color-text-secondary);
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
  color: var(--semantic-color-text-secondary);
  margin-bottom: 8px;
}

.plan-family-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.family-chip {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-muted);
  color: var(--semantic-color-text);
  font-size: 0.8rem;
  font-weight: 600;
  padding: 3px 9px;
  border-radius: 6px;
}

.plan-highlights {
  padding-left: 18px;
  margin: 0 0 20px;
  color: var(--semantic-color-text-body);
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
  color: var(--semantic-color-text-secondary);
  margin-bottom: 18px;
  max-width: 820px;
}

.guarantees-grid,
.families-grid,
.benchmark-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 250px), 1fr));
  gap: 16px;
}

.guarantee-card,
.family-card,
.benchmark-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 10px;
  padding: 18px;
  min-width: 0;
}

.guarantee-card h3,
.family-card h3,
.benchmark-card h3 {
  font-size: 1rem;
  margin: 0 0 8px;
}

.family-card-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

@media (max-width: 480px) {
  .plans-view {
    padding: 16px 8px 40px;
  }

  .plans-status-banner {
    padding: 14px 16px;
  }
}

.family-tier-tag {
  background: var(--semantic-color-surface-subtle);
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 999px;
  padding: 2px 8px;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
}

.family-responsibility {
  font-size: 0.84rem;
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
  margin-bottom: 6px;
}

.family-summary,
.family-apps {
  font-size: 0.86rem;
  color: var(--semantic-color-text-secondary);
  margin-bottom: 8px;
}

.family-status {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-success);
  margin: 0;
}
</style>
