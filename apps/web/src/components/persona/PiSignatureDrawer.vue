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

        <div class="form-group">
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
            <template v-if="signoffTargetType === 'SUBJECT'">
              <option v-for="sub in availableSubjects" :key="sub" :value="sub">
                {{ sub }}
              </option>
            </template>
            <template v-else-if="signoffTargetType === 'VISIT'">
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
          @click="$emit('submit-signoff')"
        >
          ✍️ Sign Off Target
        </button>
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
            :password="reauthPassword" data-pragma="pragma: allowlist secret"
            :totp="reauthTotp"
            username-id="reauth-username"
            password-id="reauth-password"
            totp-id="reauth-totp"
            :disabled="false"
            :password-required="true"
            :group-style="{ marginBottom: '12px' }"
            :input-style="{
              width: '100%',
              padding: '8px',
              border: '1px solid var(--border)',
              borderRadius: '4px',
            }"
            @update:username="$emit('update:reauthUsername', $event)"
            @update:password="$emit('update:reauthPassword', $event)"
            @update:totp="$emit('update:reauthTotp', $event)"
            @keyup-enter="$emit('confirm-reauth')"
          />
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
import GxpCredentialsInput from "../../features/signatures/components/GxpCredentialsInput.vue";

defineProps({
  signoffTargetType: { type: String, required: true },
  signoffTargetId: { type: String, required: true },
  customTargetId: { type: String, required: true },
  signoffReason: { type: String, required: true },
  availableSubjects: { type: Array, required: true },
  availableVisits: { type: Array, required: true },
  availableFormSubmissions: { type: Array, required: true },
  validSigningReasons: { type: Array, required: true },
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
  "submit-signoff",
  "cancel-reauth",
  "confirm-reauth",
]);
</script>
