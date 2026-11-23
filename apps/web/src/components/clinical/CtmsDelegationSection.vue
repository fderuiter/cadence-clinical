<template>
  <section
    id="ctms-delegation-section"
    class="card ctms-section"
    aria-labelledby="ctms-delegation-title"
  >
    <div class="section-header-row">
      <div>
        <h3 id="ctms-delegation-title" class="card-title">Delegation log</h3>
        <p class="section-subtitle">
          Site staff duty delegations, Principal Investigator Part 11 sign-off,
          revocations, and compliant PDF export.
        </p>
      </div>
      <button
        v-if="loaded"
        id="btn-export-doa-pdf"
        type="button"
        class="btn btn-secondary"
        :disabled="!siteInput.trim() || exporting"
        @click="exportPdf"
      >
        {{ exporting ? "Exporting..." : "Export PDF" }}
      </button>
    </div>

    <CtmsSectionScopeBar
      v-model:site="siteInput"
      prefix="delegation"
      form-id="delegation-scope-form"
      aria-label="Delegation log scope"
      :show-study-input="false"
      :assigned-sites="assignedSites"
      submit-id="btn-load-doa"
      :submit-label="loading ? 'Loading...' : 'Load delegation log'"
      :submit-disabled="!siteInput.trim() || loading"
      :scope-guidance="scopeGuidance"
      :load-error="loadError"
      :idle-message="
        !loaded && !loading
          ? 'Enter a site code within your assigned scope to view its Delegation of Authority log.'
          : ''
      "
      @submit="loadDoaLog"
    />

    <template v-if="loaded && doaLog">
      <p v-if="doaLog.pi_name" class="pi-banner">
        Principal Investigator: <strong>{{ doaLog.pi_name }}</strong>
      </p>

      <p v-if="!doaLog.delegated_staff.length" class="muted-status">
        No delegated staff entries recorded for site {{ doaLog.site_id }}.
      </p>
      <div v-else class="table-wrap">
        <table id="doa-staff-table" class="data-table">
          <thead>
            <tr>
              <th scope="col">Staff</th>
              <th scope="col">Delegated tasks</th>
              <th scope="col">Start date</th>
              <th scope="col">Status</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="rec in doaLog.delegated_staff" :key="rec.record_id">
              <td>
                <strong>{{ recordLabel(rec) }}</strong>
                <span v-if="recordRole(rec)" class="role-hint">
                  · {{ recordRole(rec) }}</span
                >
              </td>
              <td>{{ rec.task_codes.join(", ") }}</td>
              <td>{{ rec.start_date }}</td>
              <td>
                <span
                  class="status-pill"
                  :class="`doa-${recordStatus(rec).toLowerCase()}`"
                >
                  {{ recordStatus(rec) }}
                </span>
              </td>
              <td class="action-cell">
                <button
                  v-if="
                    canSignOff && recordStatus(rec) === 'PENDING_PI_SIGNATURE'
                  "
                  type="button"
                  class="btn btn-primary btn-sm"
                  @click="prepareSignOff(rec)"
                >
                  PI sign-off<span class="sr-only">
                    for {{ rec.staff_name || rec.staff_user_id }}</span
                  >
                </button>
                <button
                  v-if="canManage && recordStatus(rec) !== 'REVOKED'"
                  type="button"
                  class="btn btn-secondary btn-sm"
                  @click="prepareRevoke(rec)"
                >
                  Revoke<span class="sr-only">
                    delegation for
                    {{ rec.staff_name || rec.staff_user_id }}</span
                  >
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <p
        v-if="
          !canSignOff &&
          doaLog.delegated_staff.some(
            (r) => recordStatus(r) === 'PENDING_PI_SIGNATURE'
          )
        "
        id="doa-pi-separation-note"
        class="readonly-note"
      >
        Separation of duties: Principal Investigator signature is required to
        activate pending delegations.
      </p>

      <div v-if="activeSignOffRecord && canSignOff" class="mutation-card">
        <h4>
          Principal Investigator sign-off for
          {{ recordLabel(activeSignOffRecord) }}
        </h4>
        <div class="form-row">
          <label for="doa-signoff-reason">Part 11 attestation</label>
          <input
            id="doa-signoff-reason"
            v-model="signOffReason"
            type="text"
            placeholder="I attest this staff member is qualified for the delegated tasks"
          />
          <button
            id="btn-confirm-doa-signoff"
            type="button"
            class="btn btn-primary"
            :disabled="!signOffReason.trim() || saving"
            @click="confirmSignOff"
          >
            {{ saving ? "Signing..." : "Sign delegation" }}
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="activeSignOffRecord = null"
          >
            Cancel
          </button>
        </div>
      </div>

      <div v-if="activeRevokeRecord && canManage" class="mutation-card">
        <h4>
          Revoke delegation for
          {{ recordLabel(activeRevokeRecord) }}
        </h4>
        <div class="form-row">
          <label for="doa-revoke-reason">Revocation justification</label>
          <input
            id="doa-revoke-reason"
            v-model="revokeReason"
            type="text"
            placeholder="Reason for revoking delegated tasks"
          />
          <button
            id="btn-confirm-doa-revoke"
            type="button"
            class="btn btn-primary"
            :disabled="!revokeReason.trim() || saving"
            @click="confirmRevoke"
          >
            {{ saving ? "Revoking..." : "Confirm revocation" }}
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="activeRevokeRecord = null"
          >
            Cancel
          </button>
        </div>
      </div>

      <form
        v-if="canManage"
        id="delegate-task-form"
        class="mutation-card"
        @submit.prevent="submitDelegation"
      >
        <h4>Delegate trial tasks</h4>
        <div class="form-row">
          <label for="del-staff-email">Colleague work email</label>
          <input
            id="del-staff-email"
            v-model="newStaffEmail"
            type="email"
            placeholder="Work email address"
          />
          <label for="del-staff-name">Colleague full name (optional)</label>
          <input
            id="del-staff-name"
            v-model="newStaffName"
            type="text"
            placeholder="Optional display name"
          />
          <label for="del-staff-user"
            >Staff user ID (optional with email)</label
          >
          <input
            id="del-staff-user"
            v-model="newStaffUserId"
            type="text"
            placeholder="crc_staff_01"
          />
          <label for="del-task-select">Primary duty</label>
          <select id="del-task-select" v-model="newTaskCode">
            <option value="SUBJECT_INFORMED_CONSENT">
              Subject Informed Consent
            </option>
            <option value="CRF_DATA_ENTRY">Case Report Form Entry</option>
            <option value="SUBJECT_SCREENING">Participant Screening</option>
            <option value="SAE_REPORTING">Safety Event Reporting</option>
            <option value="IP_DISPENSING">Investigational Supply</option>
          </select>
          <label for="del-start-date">Start date</label>
          <input id="del-start-date" v-model="newStartDate" type="date" />
          <label for="del-change-reason">Reason for change</label>
          <input
            id="del-change-reason"
            v-model="newReason"
            type="text"
            placeholder="Justification for delegating duty"
          />
          <button
            id="btn-delegate-task"
            type="submit"
            class="btn btn-primary"
            :disabled="
              !siteInput.trim() ||
              (!newStaffEmail.trim() && !newStaffUserId.trim()) ||
              !newStartDate ||
              !newReason.trim() ||
              saving
            "
          >
            {{ saving ? "Submitting..." : "Delegate duty" }}
          </button>
        </div>

        <div
          v-if="delegationExplanation"
          class="readonly-note"
          role="alert"
          data-testid="ctms-refusal-explanation"
        >
          <div>
            <strong>{{ delegationExplanation.claim }}</strong>
          </div>
          <ul
            v-if="
              delegationExplanation.conditions &&
              delegationExplanation.conditions.length > 0
            "
          >
            <li
              v-for="cond in delegationExplanation.conditions"
              :key="cond.code"
            >
              <code>{{ cond.code }}</code>: {{ cond.description }}
            </li>
          </ul>
          <div
            v-if="
              delegationExplanation.next_actions &&
              delegationExplanation.next_actions.length > 0
            "
          >
            <span
              v-for="(act, idx) in delegationExplanation.next_actions"
              :key="idx"
            >
              {{ act.action || act.description }}
            </span>
          </div>
        </div>
      </form>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ctmsApi, type DelegationRecord, type DoaLog } from "../../api/ctmsApi";
import { canManageDoa, canSignOffDoa } from "../../canvas/authority";
import { useScopedWorkspaceSection } from "../../composables/useScopedWorkspaceSection";
import CtmsSectionScopeBar from "./CtmsSectionScopeBar.vue";
import "./ctmsWorkspaceSection.css";

interface RefusalCondition {
  code: string;
  description: string;
  met?: boolean;
}

interface RefusalNextAction {
  action?: string;
  description?: string;
}

interface RefusalExplanation {
  claim?: string;
  conditions?: RefusalCondition[];
  next_actions?: RefusalNextAction[];
}

type DelegationDisplayRecord = DelegationRecord & {
  staff_name?: string | null;
  role?: string | null;
  status?: string | null;
};

const props = withDefaults(
  defineProps<{
    studyCode?: string;
    siteId?: string;
  }>(),
  {
    studyCode: "",
    siteId: "",
  }
);

const {
  authStore,
  clinicalStore,
  toasts,
  assignedSites,
  siteInput,
  loading,
  loaded,
  saving,
  loadError,
  scopeGuidance,
  syncActiveScope,
  recordLoadFailure,
} = useScopedWorkspaceSection(props, {
  onSitePropChange: (val) => {
    if (val) void loadDoaLog();
  },
  fallbackErrorTitle: "Delegation log could not be loaded",
  fallbackErrorMessage: "Delegation log could not be loaded.",
});

const canManage = computed(() => canManageDoa(authStore));
const canSignOff = computed(() => canSignOffDoa(authStore));
const exporting = ref(false);
const doaLog = ref<DoaLog | null>(null);

const newStaffEmail = ref("");
const newStaffName = ref("");
const newStaffUserId = ref("");
const newTaskCode = ref("CRF_DATA_ENTRY");
const newStartDate = ref("");
const newReason = ref("");
const delegationExplanation = ref<RefusalExplanation | null>(null);

const activeSignOffRecord = ref<DelegationDisplayRecord | null>(null);
const signOffReason = ref("");

const activeRevokeRecord = ref<DelegationDisplayRecord | null>(null);
const revokeReason = ref("");

function recordLabel(rec: DelegationDisplayRecord): string {
  return rec.staff_name || rec.staff_user_id;
}

function recordRole(rec: DelegationDisplayRecord): string | null {
  return rec.role ?? null;
}

function recordStatus(rec: DelegationDisplayRecord): string {
  if (typeof rec.status === "string" && rec.status.length > 0) {
    return rec.status;
  }
  if (!rec.is_active) return "REVOKED";
  return rec.signed_off ? "ACTIVE" : "PENDING_PI_SIGNATURE";
}

async function loadDoaLog() {
  const site = siteInput.value.trim();
  if (!site) return;

  syncActiveScope(undefined, site);
  loading.value = true;
  loadError.value = "";
  scopeGuidance.value = "";

  try {
    doaLog.value = await ctmsApi.getSiteDoaLog(site);
    loaded.value = true;
  } catch (err) {
    doaLog.value = null;
    recordLoadFailure(
      err,
      props.studyCode || clinicalStore.activeStudyId || undefined,
      site
    );
  } finally {
    loading.value = false;
  }
}

async function submitDelegation() {
  const site = siteInput.value.trim();
  const email = newStaffEmail.value.trim();
  const staffName = newStaffName.value.trim();
  const staff = newStaffUserId.value.trim();
  const reason = newReason.value.trim();
  if (
    !site ||
    (!email && !staff) ||
    !newStartDate.value ||
    !reason ||
    !canManage.value
  ) {
    return;
  }
  saving.value = true;
  delegationExplanation.value = null;
  try {
    await ctmsApi.delegateSiteTasks(
      {
        site_id: site,
        study_id: props.studyCode || clinicalStore.activeStudyId || null,
        staff_user_id: staff || null,
        staff_email: email || null,
        staff_name: staffName || null,
        task_codes: [newTaskCode.value],
        start_date: newStartDate.value,
        reason_for_change: reason,
      },
      reason
    );
    newStaffEmail.value = "";
    newStaffName.value = "";
    newStaffUserId.value = "";
    newReason.value = "";
    toasts.push({
      tone: "success",
      title: "Task delegated",
      message: `Delegation created for ${staff || "colleague"} pending PI signature.`,
    });
    await loadDoaLog();
  } catch (err) {
    const explanation = (
      err as { data?: { explanation?: RefusalExplanation } } | null
    )?.data?.explanation;
    delegationExplanation.value = explanation ?? null;
    toasts.apiError("Delegation could not be created", err);
  } finally {
    saving.value = false;
  }
}

function prepareSignOff(rec: DelegationDisplayRecord) {
  activeSignOffRecord.value = rec;
  signOffReason.value = "";
}

async function confirmSignOff() {
  const rec = activeSignOffRecord.value;
  const reason = signOffReason.value.trim();
  if (!rec || !reason || !canSignOff.value) return;

  saving.value = true;
  try {
    await ctmsApi.signOffDelegation(
      {
        record_id: rec.record_id,
        reason_for_change: reason,
      },
      reason
    );
    activeSignOffRecord.value = null;
    signOffReason.value = "";
    toasts.push({
      tone: "success",
      title: "PI signature recorded",
      message: `Delegation active for ${recordLabel(rec)}.`,
    });
    await loadDoaLog();
  } catch (err) {
    toasts.apiError("PI sign-off failed", err);
  } finally {
    saving.value = false;
  }
}

function prepareRevoke(rec: DelegationDisplayRecord) {
  activeRevokeRecord.value = rec;
  revokeReason.value = "";
}

async function confirmRevoke() {
  const rec = activeRevokeRecord.value;
  const reason = revokeReason.value.trim();
  if (!rec || !reason || !canManage.value) return;

  saving.value = true;
  try {
    await ctmsApi.revokeSiteTasks(
      {
        record_id: rec.record_id,
        reason_for_change: reason,
      },
      reason
    );
    activeRevokeRecord.value = null;
    revokeReason.value = "";
    toasts.push({
      tone: "success",
      title: "Delegation revoked",
      message: `Delegation revoked for ${recordLabel(rec)}.`,
    });
    await loadDoaLog();
  } catch (err) {
    toasts.apiError("Revocation failed", err);
  } finally {
    saving.value = false;
  }
}

async function exportPdf() {
  const site = siteInput.value.trim();
  if (!site) return;
  exporting.value = true;
  try {
    const blob = await ctmsApi.exportSiteDoaPdf(site);
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `DOA_Log_${site}.pdf`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(url);
  } catch (err) {
    toasts.apiError("PDF export failed", err);
  } finally {
    exporting.value = false;
  }
}

onMounted(() => {
  if (siteInput.value.trim()) {
    void loadDoaLog();
  }
});
</script>

<style scoped>
.pi-banner {
  margin: 0;
  font-size: 0.9rem;
  color: var(--semantic-color-text-dark);
}

.role-hint {
  color: var(--semantic-color-text-secondary);
  font-size: 0.82rem;
}

.doa-active {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.doa-pending_pi_signature {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text);
}

.doa-revoked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}

.data-table {
  font-variant-numeric: tabular-nums;
}
</style>
