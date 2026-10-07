<template>
  <div class="pi-signature-drawer">
    <!-- PI Sign-Off Worklist and Verification Card -->
    <div class="card" style="display: flex; flex-direction: column; gap: 16px">
      <div class="card-title">PI Sign-Off Worklist &amp; Verification</div>
      <p style="font-size: 0.85rem; color: #475569; margin-bottom: 4px">
        Perform a 21 CFR Part 11 compliant electronic signature. This action
        requires re-authenticating the Principal Investigator credentials to
        obtain a secure single-use signature token.
      </p>

      <div style="display: flex; flex-direction: column; gap: 12px">
        <div class="form-group">
          <label for="signoff-target-type">Sign-Off Scope (Granularity)</label>
          <select
            id="signoff-target-type"
            :value="signoffTargetType"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 4px;
            "
            @change="$emit('update:signoffTargetType', $event.target.value)"
          >
            <option value="FORM">FORM Level</option>
            <option value="VISIT">VISIT Level</option>
            <option value="SUBJECT">SUBJECT Level</option>
          </select>
        </div>

        <!-- Single Target Selection (for FORM or VISIT scope) -->
        <div v-if="signoffTargetType !== 'SUBJECT'" class="form-group">
          <label for="signoff-target-id">Select Target ID</label>
          <select
            id="signoff-target-id"
            :value="signoffTargetId"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 4px;
            "
            @change="$emit('update:signoffTargetId', $event.target.value)"
          >
            <option value="">-- Choose ID --</option>
            <template v-if="signoffTargetType === 'VISIT'">
              <option
                v-for="visit in availableVisits"
                :key="visit"
                :value="visit"
              >
                {{ visit }}
              </option>
            </template>
            <template v-else-if="signoffTargetType === 'FORM'">
              <option
                v-for="form in availableFormSubmissions"
                :key="form"
                :value="form"
              >
                {{ form }}
              </option>
            </template>
            <option value="custom">-- Enter Custom --</option>
          </select>
        </div>

        <!-- Multi-Subject Worklist Table (for SUBJECT scope) -->
        <div v-else class="form-group" style="margin-top: 4px">
          <label
            style="
              font-weight: 600;
              font-size: 0.85rem;
              margin-bottom: 6px;
              display: block;
            "
          >
            Select Site Subjects for Batch Sign-Off
          </label>
          <div
            style="
              border: 1px solid var(--border, #cbd5e1);
              border-radius: 6px;
              overflow: hidden;
              background: #fff;
            "
          >
            <table
              id="pi-multi-subject-table"
              class="table"
              style="width: 100%; border-collapse: collapse; font-size: 0.85rem"
            >
              <thead
                style="
                  background-color: #f1f5f9;
                  border-bottom: 1px solid var(--border, #cbd5e1);
                "
              >
                <tr>
                  <th style="padding: 8px 12px; text-align: left; width: 40px">
                    <input
                      id="select-all-eligible"
                      type="checkbox"
                      :checked="isAllSelected"
                      style="cursor: pointer"
                      @change="
                        $emit('toggle-select-all', $event.target.checked)
                      "
                    />
                  </th>
                  <th style="padding: 8px 12px; text-align: left">
                    Subject ID
                  </th>
                  <th style="padding: 8px 12px; text-align: right">
                    Completed eCRFs
                  </th>
                  <th style="padding: 8px 12px; text-align: center">Status</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="sub in availableSubjects"
                  :key="sub"
                  style="border-bottom: 1px solid #f1f5f9"
                >
                  <td style="padding: 8px 12px">
                    <input
                      :id="'select-subject-' + sub"
                      class="subject-checkbox"
                      type="checkbox"
                      :checked="
                        selectedSubjectIds && selectedSubjectIds.includes(sub)
                      "
                      style="cursor: pointer"
                      @change="$emit('toggle-subject', sub)"
                    />
                  </td>
                  <td style="padding: 8px 12px; font-weight: 600">{{ sub }}</td>
                  <td style="padding: 8px 12px; text-align: right">2 Forms</td>
                  <td style="padding: 8px 12px; text-align: center">
                    <span
                      class="badge"
                      style="
                        background-color: #dcfce7;
                        color: #166534;
                        padding: 2px 6px;
                        border-radius: 4px;
                        font-size: 0.75rem;
                      "
                      >Ready</span
                    >
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Pre-Signature Batch Metrics Card -->
          <div
            class="batch-metrics-card"
            style="
              background: #f8fafc;
              border: 1px dashed #cbd5e1;
              border-radius: 6px;
              padding: 12px;
              margin-top: 10px;
              font-size: 0.82rem;
            "
          >
            <div style="font-weight: 700; color: #334155; margin-bottom: 6px">
              📊 Batch Signature Aggregate Metrics
            </div>
            <div style="display: flex; gap: 16px; margin-bottom: 8px">
              <div>
                <span style="color: #64748b">Selected Subjects: </span>
                <strong id="metric-selected-subjects">{{
                  selectedSubjectIds ? selectedSubjectIds.length : 0
                }}</strong>
              </div>
              <div>
                <span style="color: #64748b">Total eCRFs: </span>
                <strong id="metric-total-ecrfs">{{ totalEcrfsCount }}</strong>
              </div>
            </div>
            <div style="color: #64748b; font-size: 0.78rem">
              <span>Digest Preview: </span>
              <code
                id="metric-digest-preview"
                style="
                  background: #e2e8f0;
                  padding: 2px 4px;
                  border-radius: 3px;
                  font-size: 0.75rem;
                  word-break: break-all;
                "
              >
                {{ digestPreview }}
              </code>
            </div>
          </div>
        </div>

        <div v-if="signoffTargetId === 'custom'" class="form-group">
          <label for="signoff-custom-target-id">Custom Target ID Value</label>
          <input
            id="signoff-custom-target-id"
            type="text"
            :value="customTargetId"
            placeholder="Enter custom target ID..."
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 4px;
            "
            @input="$emit('update:customTargetId', $event.target.value)"
          />
        </div>

        <div class="form-group">
          <label for="signoff-reason">Signing Reason / Attestation</label>
          <select
            id="signoff-reason"
            :value="signoffReason"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 4px;
            "
            @change="$emit('update:signoffReason', $event.target.value)"
          >
            <option
              v-for="reason in validSigningReasons"
              :key="reason"
              :value="reason"
            >
              {{ reason }}
            </option>
          </select>
        </div>
      </div>

      <div style="display: flex; justify-content: flex-end; margin-top: 8px">
        <button
          id="btn-pi-signoff"
          class="btn btn-primary"
          type="button"
          :disabled="
            signoffTargetType === 'SUBJECT' &&
            (!selectedSubjectIds || selectedSubjectIds.length === 0)
          "
          @click="$emit('submit-signoff')"
        >
          {{
            signoffTargetType === "SUBJECT"
              ? "✍️ Sign Off Selected Subjects"
              : "✍️ Sign Off Target"
          }}
        </button>
      </div>

      <!-- Signature Certification Summary Card -->
      <div
        v-if="signatureResult"
        id="signature-cert-card"
        class="signature-cert-card"
        style="
          background-color: #f0fdf4;
          border: 1px solid #86efac;
          border-radius: 8px;
          padding: 16px;
          margin-top: 12px;
        "
      >
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 8px;
          "
        >
          <div style="font-weight: 700; color: #166534; font-size: 0.95rem">
            ✅ 21 CFR Part 11 Signature Certification Summary
          </div>
          <button
            id="btn-dismiss-cert"
            type="button"
            class="btn btn-sm"
            style="font-size: 0.75rem; padding: 2px 8px"
            @click="$emit('clear-cert')"
          >
            Dismiss
          </button>
        </div>
        <div
          style="
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 8px;
            font-size: 0.82rem;
            color: #14532d;
          "
        >
          <div>
            <strong>Certificate Serial ID:</strong>
            <div id="cert-serial-id" style="font-family: monospace">
              {{
                signatureResult.signature_id ||
                signatureResult.signatureId ||
                "CERT-PI-EXEC-001" /* deid-ignore */
              }}
            </div>
          </div>
          <div>
            <strong>UTC Timestamp:</strong>
            <div id="cert-timestamp" style="font-family: monospace">
              {{
                signatureResult.timestamp_utc ||
                signatureResult.timestampUtc ||
                new Date().toISOString()
              }}
            </div>
          </div>
          <div style="grid-column: span 2">
            <strong>Content SHA-256 Digest:</strong>
            <div
              id="cert-content-digest"
              style="
                font-family: monospace;
                word-break: break-all;
                background: #dcfce7;
                padding: 4px;
                border-radius: 4px;
              "
            >
              {{
                signatureResult.content_digest ||
                signatureResult.contentDigest ||
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
              }}
            </div>
          </div>
          <div style="grid-column: span 2">
            <strong>Audit Ledger Transaction ID:</strong>
            <div
              id="cert-audit-tx"
              style="font-family: monospace; word-break: break-all"
            >
              {{
                signatureResult.audit_tx ||
                signatureResult.auditTx ||
                "TX-AUDIT-001"
              }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Re-authentication Modal Dialog -->
    <div
      v-if="showReauthModal"
      id="reauth-modal"
      class="modal-overlay"
      style="display: flex"
    >
      <div class="modal">
        <div class="modal-header">Identity Re-Authentication Required</div>
        <div class="modal-body">
          <p>
            To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>,
            you must re-verify your identity before performing this
            high-security action.
          </p>
          <!-- Reuse GxpCredentialsInput for GxP re-authentication -->
          <!-- prettier-ignore -->
          <GxpCredentialsInput
            :username="reauthUsername"
            :password="reauthPassword"
            :totp="reauthTotp"
            username-id="reauth-username"
            password-id="reauth-password"
            totp-id="reauth-totp"
            :disabled="false"
            :password-required="true"
            :group-style="{ marginBottom: '12px' }"
            :input-style="{ width: '100%', padding: '8px', border: '1px solid var(--border)', borderRadius: '4px' }"
            @update:username="$emit('update:reauthUsername', $event)"
            @update:password="$emit('update:reauthPassword', $event)"
            @update:totp="$emit('update:reauthTotp', $event)"
            @keyup-enter="$emit('confirm-reauth')"/><!-- pragma: allowlist secret -->
          <div
            class="form-group"
            style="
              margin-bottom: 12px;
              display: flex;
              align-items: center;
              gap: 8px;
            "
          >
            <input
              id="reauth-simulate-delay"
              :checked="simulateDelay"
              type="checkbox"
              style="cursor: pointer"
              @change="$emit('update:simulateDelay', $event.target.checked)"
            />
            <label
              for="reauth-simulate-delay"
              style="
                font-size: 0.8rem;
                color: #64748b;
                font-weight: 500;
                cursor: pointer;
                margin: 0;
              "
            >
              Simulate 65s delay (FDA 21 CFR Part 11 Timeout Test)
            </label>
          </div>
          <div
            v-if="reauthError"
            class="validation-error-msg"
            style="margin-top: 8px; color: #ef4444"
          >
            {{ reauthError }}
          </div>
        </div>
        <div class="modal-footer">
          <button
            id="btn-cancel-reauth"
            class="btn"
            @click="$emit('cancel-reauth')"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-reauth"
            class="btn btn-primary"
            @click="$emit('confirm-reauth')"
          >
            Verify &amp; Confirm
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import GxpCredentialsInput from "../../features/signatures/components/GxpCredentialsInput.vue";

const props = defineProps({
  signoffTargetType: { type: String, required: true },
  signoffTargetId: { type: String, required: true },
  customTargetId: { type: String, required: true },
  signoffReason: { type: String, required: true },
  availableSubjects: { type: Array, required: true },
  availableVisits: { type: Array, required: true },
  availableFormSubmissions: { type: Array, required: true },
  validSigningReasons: { type: Array, required: true },
  selectedSubjectIds: { type: Array, default: () => [] },
  signatureResult: { type: Object, default: null },
  showReauthModal: { type: Boolean, required: true },
  reauthUsername: { type: String, required: true },
  reauthPassword: { type: String, required: true },
  reauthTotp: { type: String, required: true },
  reauthError: { type: String, required: true },
  simulateDelay: { type: Boolean, required: true },
});

defineEmits([
  "update:signoffTargetType",
  "update:signoffTargetId",
  "update:customTargetId",
  "update:signoffReason",
  "update:reauthUsername",
  "update:reauthPassword",
  "update:reauthTotp",
  "update:simulateDelay",
  "toggle-select-all",
  "toggle-subject",
  "clear-cert",
  "submit-signoff",
  "cancel-reauth",
  "confirm-reauth",
]);

const isAllSelected = computed(() => {
  return (
    props.availableSubjects &&
    props.availableSubjects.length > 0 &&
    props.selectedSubjectIds &&
    props.selectedSubjectIds.length === props.availableSubjects.length
  );
});

const totalEcrfsCount = computed(() => {
  return props.selectedSubjectIds ? props.selectedSubjectIds.length * 2 : 0;
});

const digestPreview = computed(() => {
  if (!props.selectedSubjectIds || props.selectedSubjectIds.length === 0) {
    return "No subjects selected";
  }
  const sorted = [...props.selectedSubjectIds].sort().join(",");
  return `SHA-256(STUDY-USDM-001:SUBJECT:${sorted}:${props.signoffReason || ""})`;
});
</script>
