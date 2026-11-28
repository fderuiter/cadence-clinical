<template>
  <div class="locks-sidebar-stack">
    <section
      class="governance-card"
      data-testid="locks-governance-card"
      aria-labelledby="locks-governance-heading"
    >
      <header class="governance-card-header">
        <h3 id="locks-governance-heading">
          Scope Hierarchy &amp; Role Separation
        </h3>
        <span class="governance-study-code">{{ studyCode }}</span>
      </header>

      <div class="governance-coverage">
        <div class="governance-coverage-label">
          <template v-if="sitePickerOptions.length > 0">
            Study Coverage ({{ sitePickerOptions.length }}
            {{ sitePickerOptions.length === 1 ? "Site" : "Sites" }})
          </template>
          <template v-else>Scope Coverage Hierarchy</template>
        </div>
        <div v-if="sitePickerOptions.length > 0" class="coverage-pill-list">
          <span
            v-for="site in sitePickerOptions"
            :key="site.key"
            :class="[
              'coverage-site-pill',
              site.masked ? 'coverage-site-pill--masked' : '',
            ]"
          >
            {{ site.label }}
          </span>
        </div>
        <div v-else class="coverage-pill-list">
          <span class="coverage-site-pill coverage-site-pill--primary">
            Study
          </span>
          <span class="coverage-site-pill">Site</span>
          <span class="coverage-site-pill">Participant</span>
          <span class="coverage-site-pill">Form</span>
          <span class="coverage-site-pill">Observation</span>
        </div>
      </div>

      <div class="role-matrix-box" data-testid="locks-role-matrix">
        <div class="role-matrix-heading">Role Separation Matrix</div>
        <div class="role-matrix-row">
          <span class="role-matrix-action">Apply assertion:</span>
          <strong class="role-matrix-role">Trial Data Manager</strong>
        </div>
        <div class="role-matrix-row">
          <span class="role-matrix-action">Release assertion:</span>
          <strong class="role-matrix-role">Sponsor Admin</strong>
        </div>
      </div>

      <p class="governance-policy-note">
        Dual-signer policy &amp; four-eyes principle enforced: a signatory
        cannot self-release locks they asserted, and ancestor locks cover child
        scopes.
      </p>
    </section>

    <section
      class="governance-card"
      data-testid="locks-certificate-card"
      aria-labelledby="locks-certificate-heading"
    >
      <header class="governance-card-header">
        <h3 id="locks-certificate-heading">Database Lock Certificate (SCE)</h3>
        <span
          v-if="certificate && !certificate.revoked"
          class="clinical-badge clinical-badge--verified cert-status-badge"
          data-testid="locks-certificate-status"
        >
          <ClinicalIcon name="check-circle" :size="14" />
          <span>Verified · Unrevoked</span>
        </span>
        <span
          v-else-if="certificate && certificate.revoked"
          class="clinical-badge clinical-badge--error cert-status-badge"
          data-testid="locks-certificate-status"
        >
          <ClinicalIcon name="alert-triangle" :size="14" />
          <span>Revoked</span>
        </span>
        <span
          v-else
          class="clinical-badge cert-status-badge cert-status-badge--pending"
          data-testid="locks-certificate-status"
        >
          <span>Awaiting Study Lock</span>
        </span>
      </header>

      <template v-if="certificate">
        <div class="cert-fields">
          <div class="cert-field">
            <div class="cert-field-label">Milestone Assertion</div>
            <div class="cert-field-value">
              {{ formatMilestoneLabel(certificate.milestone) }} (Study Locked
              &amp; Signed)
            </div>
          </div>
          <div class="cert-field">
            <div class="cert-field-label">Lock ID</div>
            <div
              class="cert-field-mono"
              data-testid="locks-certificate-lock-id"
            >
              {{ certificate.lock_ref }}
            </div>
          </div>
          <div class="cert-field">
            <div class="cert-field-label">ECDSA SHA-256 Fingerprint</div>
            <div
              class="cert-fingerprint-box"
              data-testid="locks-certificate-fingerprint"
            >
              {{ certificate.fingerprint_sha256 }}
            </div>
          </div>
          <div class="cert-meta-row">
            <span>
              Signed by:
              <strong class="cert-signer">{{ certificate.signed_by }}</strong>
            </span>
            <span class="cert-timestamp">{{ certificate.issued_at }}</span>
          </div>
        </div>

        <button
          type="button"
          class="btn btn-secondary cert-copy-btn"
          data-testid="btn-copy-sce-certificate"
          @click="copyCertificate"
        >
          <ClinicalIcon :name="copyFeedback ? 'check' : 'copy'" :size="14" />
          <span>{{ copyFeedback || "Copy SCE Certificate" }}</span>
        </button>
        <span v-if="copyFeedback" class="sr-only" role="status">
          {{ copyFeedback }}
        </span>
      </template>

      <p v-else class="cert-empty-note" data-testid="locks-certificate-empty">
        Committing a study-level <strong>Locked</strong> assertion mints an
        asymmetric ECDSA-P256-SHA256 certificate verified offline by Statistical
        Computing Environment (SCE) analysis runs.
      </p>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from "vue";
import { ClinicalIcon } from "ui";
import type {
  KernelDatabaseLockCertificate,
  KernelLock,
} from "../../api/kernelApi";
import type { PickerOption } from "./useLockHierarchyPickers";

const props = defineProps<{
  studyCode: string;
  sitePickerOptions: PickerOption[];
  certificateLock: KernelLock | null;
}>();

const copyFeedback = ref("");

const certificate = computed<KernelDatabaseLockCertificate | null>(
  () => props.certificateLock?.certificate ?? null
);

function formatMilestoneLabel(milestone: string | null | undefined): string {
  if (milestone === "FinalDatabaseLock") {
    return "Final Database Lock";
  }
  return "Interim Cut";
}

async function copyCertificate() {
  if (!certificate.value) return;
  const serialized = JSON.stringify(certificate.value, null, 2);
  try {
    if (
      typeof navigator !== "undefined" &&
      navigator.clipboard &&
      typeof navigator.clipboard.writeText === "function"
    ) {
      await navigator.clipboard.writeText(serialized);
    }
    copyFeedback.value = "Copied SCE Certificate";
  } catch {
    copyFeedback.value = "Certificate ready";
  }
}
</script>

<style scoped src="./locksSection.css"></style>
