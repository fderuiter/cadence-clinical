<template>
  <section
    id="study-locks-section"
    class="study-locks"
    aria-labelledby="study-locks-title"
    :aria-busy="studyLoading || locksLoading"
  >
    <header class="study-locks-header">
      <div>
        <h2 id="study-locks-title" tabindex="-1">Locks</h2>
        <p class="study-locks-subtitle">
          Signed <strong>Frozen</strong> and <strong>Locked</strong> assertions
          across study, site, participant, form, and observation scopes.
        </p>
      </div>
      <div class="study-locks-filters" data-testid="study-locks-filters">
        <label class="checkbox-label" for="study-locks-active-only">
          <input
            id="study-locks-active-only"
            v-model="activeOnly"
            type="checkbox"
            @change="reloadLocks"
          />
          Active locks only
        </label>

        <ClinicalInput
          id="study-locks-as-of"
          v-model="asOfInput"
          label="As of (ISO UTC)"
          type="text"
          density="compact"
          :show-query-flag="false"
          extra-class="locks-inline-field"
          autocomplete="off"
          placeholder="Latest truth"
        />
        <button
          id="study-locks-refresh"
          type="button"
          class="btn btn-secondary"
          :disabled="studyLoading || locksLoading"
          @click="reloadLocks"
        >
          Inspect
        </button>
      </div>
    </header>

    <p v-if="studyLoading" class="status-line" role="status">
      Looking up {{ studyCode }}…
    </p>
    <div
      v-else-if="studyError"
      id="study-locks-error"
      class="status-line error-banner"
      role="alert"
    >
      <p class="status-line">
        {{ studyCode }}'s locks couldn't be loaded.
        <button
          id="btn-retry-study-locks"
          type="button"
          class="btn btn-secondary"
          :disabled="studyLoading"
          @click="handleRetryStudyLocks"
        >
          Try again
        </button>
      </p>
      <SupportDetails :detail="studyError" />
    </div>
    <p v-else-if="!studyRef" id="study-locks-no-study" class="status-line">
      Study {{ studyCode }} wasn't found or isn't visible to your account, so
      its locks cannot be listed here.
    </p>

    <template v-else>
      <div class="locks-workspace-grid">
        <div class="locks-main-column">
          <LocksApplyForm
            v-if="canApply"
            v-model:apply-scope-kind="applyScopeKind"
            v-model:selected-site-ref="selectedSiteRef"
            v-model:selected-enrollment-ref="selectedEnrollmentRef"
            v-model:selected-form-ref="selectedFormRef"
            v-model:selected-observation-ref="selectedObservationRef"
            v-model:apply-kind="applyKind"
            v-model:selected-milestone="selectedMilestone"
            v-model:apply-reason="applyReason"
            :study-code="studyCode"
            :study-ref="studyRef"
            :verified-signer-role="verifiedSignerRole"
            :needs-site-picker="needsSitePicker"
            :needs-enrollment-picker="needsEnrollmentPicker"
            :needs-form-picker="needsFormPicker"
            :needs-observation-picker="needsObservationPicker"
            :site-picker-options="sitePickerOptions"
            :enrollment-picker-options="enrollmentPickerOptions"
            :form-picker-options="formPickerOptions"
            :observation-picker-options="observationPickerOptions"
            :enrollments-loading="enrollmentsLoading"
            :forms-loading="formsLoading"
            :observations-loading="observationsLoading"
            :apply-scope-ref="applyScopeRef"
            :readiness="readiness"
            :readiness-loading="readinessLoading"
            :readiness-gates="readinessGates"
            :is-apply-disabled="isApplyDisabled"
            :busy="busy"
            :can-freeze-by-readiness="canFreezeByReadiness"
            :is-lock-blocked-by-readiness="isLockBlockedByReadiness"
            @scope-kind-change="onScopeKindChange"
            @site-picker-change="onSitePickerChange"
            @enrollment-picker-change="onEnrollmentPickerChange"
            @form-picker-change="onFormPickerChange"
            @submit="openApplyModal"
            @quick-apply="openQuickApplyDialog"
          />

          <section
            v-else
            class="apply-card"
            data-testid="study-locks-readonly-posture"
            aria-labelledby="locks-readonly-heading"
          >
            <header class="apply-card-header">
              <div>
                <h3 id="locks-readonly-heading">
                  Milestone lock &amp; readiness
                </h3>
                <p class="apply-card-subtitle">
                  Trial Data Managers hold <code>lock.apply</code> authority;
                  Sponsor Admins hold segregated <code>lock.release</code>
                  authority.
                </p>
              </div>
              <span class="milestone-pill">Study Milestone</span>
            </header>
            <div class="apply-actions">
              <button type="button" class="btn btn-secondary" disabled>
                Sign Final Database Lock
              </button>
              <span
                class="oidc-role-badge"
                data-testid="locks-verified-oidc-role"
              >
                Verified OIDC Role: {{ verifiedSignerRole }} (read-only)
              </span>
            </div>
          </section>

          <div v-if="canRelease" class="release-reason-bar">
            <ClinicalInput
              id="lock-release-reason"
              v-model="releaseReason"
              label="Reason when releasing a lock"
              type="text"
              density="compact"
              :show-query-flag="false"
              extra-class="locks-release-field"
              autocomplete="off"
              placeholder="Reopening scope for documented source addendum"
            />
          </div>
        </div>

        <LocksGovernanceSidebar
          :study-code="studyCode"
          :site-picker-options="sitePickerOptions"
          :certificate-lock="latestCertificateLock"
        />
      </div>

      <p
        v-if="outcome"
        id="study-locks-outcome"
        :class="[
          'status-line',
          outcome.kind === 'ok' ? 'ok-banner' : 'error-banner',
        ]"
        :role="outcome.kind === 'ok' ? 'status' : 'alert'"
        aria-live="polite"
      >
        {{ outcome.message }}
      </p>

      <p v-if="locksLoading" class="status-line" role="status">
        Loading locks for {{ studyCode }}…
      </p>
      <div
        v-else-if="locksError"
        id="study-locks-load-error"
        class="status-line error-banner"
        role="alert"
      >
        <p class="status-line">
          {{ studyCode }}'s locks couldn't be loaded.
          <button
            id="btn-retry-locks-list"
            type="button"
            class="btn btn-secondary"
            :disabled="locksLoading"
            @click="handleRetryLocksList"
          >
            Try again
          </button>
        </p>
        <SupportDetails :detail="locksError" />
      </div>
      <template v-else>
        <p v-if="locks.length === 0" id="study-locks-empty" class="status-line">
          No locks recorded on {{ studyCode }} within your visible scope at this
          point in time.
        </p>
        <div v-else class="locks-table-card">
          <div class="locks-table-card-header">
            <div class="locks-table-title-group">
              <h3>Study locks</h3>
              <span class="locks-active-pill tabular-nums">
                {{ activeLockCount }} active
              </span>
            </div>
            <span class="locks-stream-caption">Audit log immutable stream</span>
          </div>
          <ClinicalDataTable
            title="Study locks"
            aria-label="Study locks"
            labelled-by="study-locks-title"
            hide-header
            plain
            wrapper-class="locks-table-scroll scrollable-table-wrapper"
            wrapper-test-id="study-locks-table-scroll"
            table-class="locks-table"
            table-test-id="study-locks-table"
            :columns="lockColumns"
            :data="locks"
            :row-key="(entry, idx) => entry.ref ?? `masked-${idx}`"
            :row-attrs="
              (entry) => (entry.masked ? { 'data-masked': 'true' } : undefined)
            "
            external-pagination
          >
            <template #cell-kind="{ row: entry }">
              <span v-if="entry.masked || !entry.kind" class="masked-pill">
                Masked ({{ entry.mask ?? "Restricted" }})
              </span>
              <span
                v-else
                :class="[
                  'clinical-badge',
                  'lock-badge',
                  entry.kind.toLowerCase() === 'locked'
                    ? 'clinical-badge--locked'
                    : 'clinical-badge--frozen',
                  `lock-badge--${entry.kind.toLowerCase()}`,
                ]"
              >
                {{ entry.kind }}
              </span>
            </template>
            <template #cell-scope="{ row: entry }">
              <template v-if="entry.masked">
                <span class="masked-pill" data-testid="study-lock-masked">
                  <template v-if="entry.scope_kind">
                    Masked ({{ entry.mask ?? "Restricted" }}):
                    {{ scopeLabel(entry.scope_kind) }}
                  </template>
                  <template v-else>
                    Masked ({{ entry.mask ?? "Restricted" }})
                  </template>
                </span>
              </template>
              <template v-else>
                <NodeRefChip
                  v-if="entry.scope_ref"
                  :node-ref="entry.scope_ref"
                  :fallback-title="entry.subject_code"
                  include-kind
                />
                <span v-else>—</span>
              </template>
            </template>
            <template #cell-participant="{ row: entry }">
              {{ entry.masked ? "masked" : (entry.subject_code ?? "—") }}
            </template>
            <template #cell-signed_by="{ row: entry }">
              <template v-if="entry.masked"> masked </template>
              <template v-else>
                <NodeRefChip
                  v-if="
                    entry.locked_by && normalizeLookupNodeRef(entry.locked_by)
                  "
                  :node-ref="normalizeLookupNodeRef(entry.locked_by)!"
                />
                <template v-else>{{ entry.locked_by ?? "—" }}</template>
                <span v-if="entry.attestation_meaning" class="meaning-note">
                  ({{ entry.attestation_meaning }})
                </span>
              </template>
            </template>
            <template #cell-locked_at="{ row: entry }">
              {{ entry.masked ? "—" : (entry.locked_at ?? "—") }}
            </template>
            <template #cell-reason="{ row: entry }">
              {{ entry.masked ? "masked" : (entry.reason_for_change ?? "—") }}
            </template>
            <template #cell-state="{ row: entry }">
              <template
                v-if="
                  entry.masked ||
                  entry.released === null ||
                  entry.released === undefined
                "
              >
                masked
              </template>
              <template v-else-if="entry.released">
                Released
                <span v-if="entry.released_at" class="meaning-note">
                  at {{ entry.released_at }}
                </span>
              </template>
              <template v-else>
                Active
                <span
                  v-if="coveringAncestorLock(entry)"
                  class="covered-parent-badge"
                  data-testid="study-lock-covered-badge"
                >
                  Covered by parent lock
                </span>
              </template>
            </template>
            <template #cell-action="{ row: entry }">
              <div
                v-if="
                  !entry.masked && !entry.released && entry.ref && entry.kind
                "
                class="release-cell"
              >
                <button
                  :data-testid="`release-lock-${entry.ref}`"
                  type="button"
                  class="btn btn-secondary btn-mini-unlock"
                  :disabled="
                    busy ||
                    isSelfLocked(entry) ||
                    Boolean(coveringAncestorLock(entry))
                  "
                  @click="openReleaseModal(entry.ref, entry.kind)"
                >
                  Release<span class="sr-only">
                    lock for
                    {{
                      entry.subject_code ?? entry.scope_ref ?? entry.ref
                    }}</span
                  >
                </button>
                <span
                  v-if="coveringAncestorLock(entry)"
                  class="release-guard-note"
                >
                  Covered by parent lock
                </span>
                <span
                  v-else-if="isSelfLocked(entry)"
                  class="release-guard-note"
                >
                  Dual-signer release required (cannot release own lock)
                </span>
              </div>
            </template>
          </ClinicalDataTable>
        </div>
        <p
          v-if="hiddenLocks.more_exist"
          data-testid="study-locks-hidden-notice"
          class="status-line hidden-scope-notice"
          role="status"
        >
          Additional locks exist outside your visible scope{{
            hiddenLocks.reasons.length > 0
              ? ` (${hiddenLocks.reasons.join(", ")})`
              : ""
          }}.
        </p>
      </template>

      <p
        v-if="!canApply && !canRelease"
        id="study-locks-note"
        class="status-line"
      >
        A data manager with electronic signature authority applies or releases
        study locks.
      </p>
    </template>

    <LocksQuickReasonModal
      v-model:quick-dialog-reason="quickDialogReason"
      :study-code="studyCode"
      :apply-scope-ref="applyScopeRef"
      :quick-dialog="quickDialog"
      :quick-dialog-title="quickDialogTitle"
      :busy="busy"
      @close="closeQuickDialog"
      @confirm="confirmQuickDialog"
    />

    <SignatureCaptureModal
      v-if="modalOp"
      v-bind="signatureModalProps"
      @cancel="closeModal"
      @success="closeModal"
    />
  </section>
</template>

<script setup lang="ts">
import { ClinicalDataTable, ClinicalInput } from "ui";
import { normalizeLookupNodeRef } from "../../canvas/nodeResolver";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import LocksApplyForm from "./LocksApplyForm.vue";
import LocksGovernanceSidebar from "./LocksGovernanceSidebar.vue";
import LocksQuickReasonModal from "./LocksQuickReasonModal.vue";
import NodeRefChip from "./NodeRefChip.vue";
import SupportDetails from "./SupportDetails.vue";
import { scopeLabel } from "./useLockHierarchyPickers";
import { useStudyLocksController } from "./useStudyLocksController";

const props = defineProps<{
  studyCode: string;
}>();

const {
  activeLockCount,
  activeOnly,
  applyKind,
  applyReason,
  applyScopeKind,
  applyScopeRef,
  asOfInput,
  busy,
  canApply,
  canFreezeByReadiness,
  canRelease,
  closeModal,
  closeQuickDialog,
  confirmQuickDialog,
  coveringAncestorLock,
  enrollmentPickerOptions,
  enrollmentsLoading,
  formPickerOptions,
  formsLoading,
  handleRetryLocksList,
  handleRetryStudyLocks,
  hiddenLocks,
  isApplyDisabled,
  isLockBlockedByReadiness,
  isSelfLocked,
  latestCertificateLock,
  lockColumns,
  locks,
  locksError,
  locksLoading,
  modalOp,
  needsEnrollmentPicker,
  needsFormPicker,
  needsObservationPicker,
  needsSitePicker,
  observationPickerOptions,
  observationsLoading,
  onEnrollmentPickerChange,
  onFormPickerChange,
  onScopeKindChange,
  onSitePickerChange,
  openApplyModal,
  openQuickApplyDialog,
  openReleaseModal,
  outcome,
  quickDialog,
  quickDialogReason,
  quickDialogTitle,
  readiness,
  readinessGates,
  readinessLoading,
  releaseReason,
  reloadLocks,
  selectedEnrollmentRef,
  selectedFormRef,
  selectedMilestone,
  selectedObservationRef,
  selectedSiteRef,
  signatureModalProps,
  sitePickerOptions,
  studyError,
  studyLoading,
  studyRef,
  verifiedSignerRole,
} = useStudyLocksController(() => props.studyCode);
</script>

<style scoped src="./locksSection.css"></style>
<style scoped src="./canvasBanners.css"></style>
