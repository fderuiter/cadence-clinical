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
                <strong>{{ rec.staff_name || rec.staff_user_id }}</strong>
                <span v-if="rec.role" class="role-hint"> · {{ rec.role }}</span>
              </td>
              <td>{{ rec.task_codes.join(", ") }}</td>
              <td>{{ rec.start_date }}</td>
              <td>
                <span
                  class="status-pill"
                  :class="`doa-${rec.status.toLowerCase()}`"
                >
                  {{ rec.status }}
                </span>
              </td>
              <td class="action-cell">
                <button
                  v-if="canSignOff && rec.status === 'PENDING_PI_SIGNATURE'"
                  :data-testid="`btn-doa-signoff-${rec.record_id}`"
                  type="button"
                  class="btn btn-primary btn-sm"
                  @click="prepareSignOff(rec)"
                >
                  PI sign-off
                </button>
                <button
                  v-if="canManage && rec.status !== 'REVOKED'"
                  type="button"
                  class="btn btn-secondary btn-sm"
                  @click="prepareRevoke(rec)"
                >
                  Revoke
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
            (r) => r.status === 'PENDING_PI_SIGNATURE'
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
          {{
            activeSignOffRecord.staff_name || activeSignOffRecord.staff_user_id
          }}
        </h4>
        <div class="form-row">
          <label for="doa-signoff-password">Password (Part 11)</label>
          <input
            id="doa-signoff-password"
            v-model="signOffPassword"
            v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
            placeholder="Re-enter password to sign"
          />
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
            :disabled="!signOffReason.trim() || !signOffPassword || saving"
            @click="confirmSignOff"
          >
            {{ saving ? "Signing..." : "Sign delegation" }}
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="cancelSignOff"
          >
            Cancel
          </button>
        </div>
      </div>

      <div v-if="activeRevokeRecord && canManage" class="mutation-card">
        <h4>
          Revoke delegation for
          {{
            activeRevokeRecord.staff_name || activeRevokeRecord.staff_user_id
          }}
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
          <label for="del-staff-user">Staff user ID</label>
          <input
            id="del-staff-user"
            v-model="newStaffUserId"
            type="text"
            placeholder="crc_staff_01"
          />
          <label for="del-staff-name">Staff name</label>
          <input
            id="del-staff-name"
            v-model="newStaffName"
            type="text"
            placeholder="Optional full name"
          />
          <label for="del-staff-role">Clinical role</label>
          <input
            id="del-staff-role"
            v-model="newStaffRole"
            type="text"
            placeholder="e.g. Clinical Research Coordinator"
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
              !newStaffUserId.trim() ||
              !newStartDate ||
              !newReason.trim() ||
              saving
            "
          >
            {{ saving ? "Submitting..." : "Delegate duty" }}
          </button>
        </div>
      </form>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { SIGNATURE_PASSWORD_INPUT_ATTRS } from "ui";
import { ctmsApi, type DelegationRecord, type DoaLog } from "../../api/ctmsApi";
import { etmfService } from "../../api/etmf";
import { canManageDoa, canSignOffDoa } from "../../canvas/authority";
import { useScopedWorkspaceSection } from "../../composables/useScopedWorkspaceSection";
import CtmsSectionScopeBar from "./CtmsSectionScopeBar.vue";
import "./ctmsWorkspaceSection.css";

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

const newStaffUserId = ref("");
const newStaffName = ref("");
const newStaffRole = ref("");
const newTaskCode = ref("CRF_DATA_ENTRY");
const newStartDate = ref("");
const newReason = ref("");

const activeSignOffRecord = ref<DelegationRecord | null>(null);
const signOffReason = ref("");
const signOffPassword = ref("");

const activeRevokeRecord = ref<DelegationRecord | null>(null);
const revokeReason = ref("");

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
  const staff = newStaffUserId.value.trim();
  const reason = newReason.value.trim();
  if (!site || !staff || !newStartDate.value || !reason || !canManage.value) {
    return;
  }

  saving.value = true;
  try {
    await ctmsApi.delegateSiteTasks(
      {
        site_id: site,
        staff_user_id: staff,
        staff_name: newStaffName.value.trim() || null,
        role: newStaffRole.value.trim() || null,
        task_codes: [newTaskCode.value],
        start_date: newStartDate.value,
        reason_for_change: reason,
      },
      reason
    );
    newStaffUserId.value = "";
    newStaffName.value = "";
    newStaffRole.value = "";
    newReason.value = "";
    toasts.push({
      tone: "success",
      title: "Task delegated",
      message: `Delegation created for ${staff} pending PI signature.`,
    });
    await loadDoaLog();
  } catch (err) {
    toasts.apiError("Delegation could not be created", err);
  } finally {
    saving.value = false;
  }
}

function prepareSignOff(rec: DelegationRecord) {
  activeSignOffRecord.value = rec;
  signOffReason.value = "";
  signOffPassword.value = "";
}

function cancelSignOff() {
  signOffPassword.value = "";
  signOffReason.value = "";
  activeSignOffRecord.value = null;
}

async function confirmSignOff() {
  const rec = activeSignOffRecord.value;
  const reason = signOffReason.value.trim();
  const password = signOffPassword.value;
  signOffPassword.value = "";
  if (!rec || !reason || !password || !canSignOff.value) return;

  saving.value = true;
  try {
    const signerUsername =
      authStore.identity?.username ||
      authStore.user?.username ||
      authStore.userId ||
      "";
    const verifyRes = (await etmfService.verifySignature({
      username: signerUsername,
      password,
      totp: null,
      action: "/api/v1/ctms/doa/sign-off",
      semanticAction: "ctms.doa.sign_off",
    })) as { sig_token?: string } | null;

    const sigToken = verifyRes?.sig_token;
    if (!sigToken) {
      throw new Error(
        "Signature verification did not return a signature token."
      );
    }

    await ctmsApi.signOffDelegation(
      {
        record_id: rec.record_id,
        reason_for_change: reason,
      },
      reason,
      sigToken
    );
    activeSignOffRecord.value = null;
    signOffReason.value = "";
    toasts.push({
      tone: "success",
      title: "PI signature recorded",
      message: `Delegation active for ${rec.staff_name || rec.staff_user_id}.`,
    });
    await loadDoaLog();
  } catch (err) {
    toasts.apiError("PI sign-off failed", err);
  } finally {
    saving.value = false;
  }
}

function prepareRevoke(rec: DelegationRecord) {
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
      message: `Delegation revoked for ${rec.staff_name || rec.staff_user_id}.`,
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
