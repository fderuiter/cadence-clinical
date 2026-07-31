<template>
  <div
    v-if="isOpen"
    id="batch-signature-modal"
    class="modal-backdrop"
    style="
      display: flex;
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      align-items: center;
      justify-content: center;
      z-index: 1000;
    "
  >
    <div
      class="modal-dialog"
      style="
        background: white;
        border-radius: 8px;
        width: 100%;
        max-width: 550px;
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
        overflow: hidden;
        color: #333;
      "
    >
      <header
        class="modal-header"
        style="
          padding: 16px;
          border-bottom: 1px solid #e2e8f0;
          display: flex;
          justify-content: space-between;
          align-items: center;
        "
      >
        <h3
          style="margin: 0; font-size: 1.1rem; font-weight: 600; color: #1e293b"
        >
          21 CFR Part 11 Electronic Signature Sign-Off
        </h3>
        <button
          class="btn-close"
          style="
            background: none;
            border: none;
            font-size: 1.5rem;
            cursor: pointer;
            color: #64748b;
          "
          @click="$emit('close')"
        >
          &times;
        </button>
      </header>

      <div
        class="modal-body"
        style="padding: 16px; font-size: 14px"
      >
        <!-- Success Confirmation Banner (Step 5) -->
        <div
          v-if="lastSignatureResult"
          class="confirmation-banner"
          style="
            background: #f0fdf4;
            border: 1px solid #bbf7d0;
            border-radius: 6px;
            padding: 16px;
            margin-bottom: 16px;
            color: #166534;
          "
        >
          <h4 style="margin: 0 0 8px 0; font-weight: bold; color: #15803d">
            ✓ Signature Executed Successfully
          </h4>
          <p
            style="
              margin: 0 0 12px 0;
              font-size: 13px;
              color: #166534;
              line-height: 1.4;
            "
          >
            Your Part 11 electronic signature has been recorded securely to the
            immutable audit ledger.
          </p>
          <div
            style="
              font-size: 12px;
              font-family: monospace;
              line-height: 1.5;
              color: #14532d;
            "
          >
            <div>
              <strong>Cert Serial:</strong>
              {{ lastSignatureResult.signature_id }}
            </div>
            <div>
              <strong>Timestamp UTC:</strong>
              {{ lastSignatureResult.timestamp_utc }}
            </div>
            <div>
              <strong>Content Digest:</strong>
              {{ lastSignatureResult.content_digest }}
            </div>
            <div>
              <strong>Audit TX:</strong> {{ lastSignatureResult.audit_tx }}
            </div>
          </div>
          <div style="margin-top: 12px; text-align: right">
            <button
              class="btn-close-confirm btn"
              style="
                padding: 6px 12px;
                font-size: 12px;
                cursor: pointer;
                background: #15803d;
                color: white;
                border: none;
                border-radius: 4px;
              "
              @click="$emit('close')"
            >
              Close
            </button>
          </div>
        </div>

        <template v-else>
          <p
            class="summary-text"
            style="color: #475569; margin-bottom: 16px; line-height: 1.4"
          >
            You are signing <strong>{{ selectedForms.length }}</strong> eCRF
            forms for Subject <strong>{{ subjectId }}</strong>.
          </p>

          <!-- Form Summary Table (Step 1) -->
          <div style="margin-bottom: 16px">
            <h4
              style="
                margin: 0 0 8px 0;
                font-weight: 600;
                color: #334155;
                font-size: 13px;
              "
            >
              eCRF Batch Package Preview:
            </h4>
            <div
              style="
                max-height: 150px;
                overflow-y: auto;
                border: 1px solid #cbd5e1;
                border-radius: 6px;
              "
            >
              <table
                style="
                  width: 100%;
                  border-collapse: collapse;
                  font-size: 12px;
                  text-align: left;
                "
              >
                <thead
                  style="
                    background: #f8fafc;
                    border-bottom: 1px solid #cbd5e1;
                    color: #475569;
                  "
                >
                  <tr>
                    <th style="padding: 8px">
                      eCRF ID
                    </th>
                    <th style="padding: 8px">
                      SHA-256 Preview Hash
                    </th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="form in selectedForms"
                    :key="form"
                    style="border-bottom: 1px solid #e2e8f0"
                  >
                    <td style="padding: 8px; font-weight: 500; color: #1e293b">
                      {{ form }}
                    </td>
                    <td
                      style="
                        padding: 8px;
                        font-family: monospace;
                        color: #64748b;
                      "
                    >
                      {{ formHashes[form] || "Computing hash..." }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <!-- Re-Authentication Form & Controls (Step 2 & 3) -->
          <form @submit.prevent="handleExecuteSign">
            <div
              class="form-group"
              style="margin-bottom: 12px"
            >
              <label
                style="
                  display: block;
                  font-weight: 500;
                  margin-bottom: 4px;
                  font-size: 13px;
                  color: #475569;
                "
              >
                Signature Meaning / Attestation
              </label>
              <select
                v-model="signatureMeaning"
                class="signature-meaning-picker"
                required
                style="
                  width: 100%;
                  padding: 8px;
                  border: 1px solid #cbd5e1;
                  border-radius: 4px;
                  font-size: 13px;
                  background: white;
                "
              >
                <option value="APPROVED">
                  Principal Investigator Approval
                </option>
                <option value="REVIEWED">
                  CRA Monitor Review
                </option>
                <option value="VERIFIED_SDV">
                  Source Data Verification (SDV) Confirmed
                </option>
              </select>
            </div>

            <div
              class="form-group"
              style="margin-bottom: 12px"
            >
              <label
                style="
                  display: block;
                  font-weight: 500;
                  margin-bottom: 4px;
                  font-size: 13px;
                  color: #475569;
                "
              >
                Password Re-Authentication
              </label>
              <input
                v-model="password"
                type="password"
                class="password-input"
                required
                placeholder="Re-enter password"
                style="
                  width: 100%;
                  padding: 8px;
                  border: 1px solid #cbd5e1;
                  border-radius: 4px;
                  font-size: 13px;
                "
              >
            </div>

            <div
              class="form-group"
              style="margin-bottom: 16px"
            >
              <label
                style="
                  display: block;
                  font-weight: 500;
                  margin-bottom: 4px;
                  font-size: 13px;
                  color: #475569;
                "
              >
                MFA / TOTP Token (Optional)
              </label>
              <input
                v-model="totp"
                type="text"
                class="totp-input"
                placeholder="Enter 6-digit TOTP code"
                style="
                  width: 100%;
                  padding: 8px;
                  border: 1px solid #cbd5e1;
                  border-radius: 4px;
                  font-size: 13px;
                "
              >
            </div>

            <!-- Error Notification Banner -->
            <div
              v-if="localError"
              class="error-banner"
              style="
                background: #fef2f2;
                border: 1px solid #fca5a5;
                border-radius: 4px;
                padding: 10px;
                color: #991b1b;
                font-size: 13px;
                margin-bottom: 16px;
              "
            >
              {{ localError }}
            </div>

            <!-- Signing In-Progress Indicator (Step 4) -->
            <div
              v-if="isSigning"
              class="loading-spinner-container"
              style="
                display: flex;
                align-items: center;
                justify-content: center;
                gap: 8px;
                margin-bottom: 16px;
                color: #2563eb;
                font-size: 13px;
                font-weight: 500;
              "
            >
              <span
                class="spinner"
                style="
                  border: 2px solid #e2e8f0;
                  border-top: 2px solid #2563eb;
                  border-radius: 50%;
                  width: 16px;
                  height: 16px;
                  animation: spin 1s linear infinite;
                "
              />
              Executing Electronic Signature...
            </div>

            <div
              class="modal-actions"
              style="display: flex; justify-content: flex-end; gap: 8px"
            >
              <button
                type="button"
                class="btn-cancel btn"
                style="
                  padding: 6px 12px;
                  font-size: 13px;
                  cursor: pointer;
                  border: 1px solid #cbd5e1;
                  background: white;
                  border-radius: 4px;
                "
                @click="$emit('close')"
              >
                Cancel
              </button>
              <button
                type="submit"
                class="btn-primary"
                style="
                  padding: 6px 12px;
                  font-size: 13px;
                  cursor: pointer;
                  background: #2563eb;
                  color: white;
                  border: none;
                  border-radius: 4px;
                "
                :disabled="isSigning || !password || !signatureMeaning"
              >
                {{ isSigning ? "Signing..." : "Confirm & Execute Signature" }}
              </button>
            </div>
          </form>
        </template>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, onMounted } from "vue";
import { useSignatureStore } from "../../stores/signatures";

const props = defineProps({
  isOpen: {
    type: Boolean,
    required: true,
  },
  studyId: {
    type: String,
    default: "",
  },
  subjectId: {
    type: String,
    required: true,
  },
  selectedForms: {
    type: Array,
    required: true,
  },
});

const emit = defineEmits(["close", "signed"]);
const signatureStore = useSignatureStore();

const password = ref("");
const totp = ref("");
const signatureMeaning = ref("APPROVED");
const isSigning = ref(false);
const localError = ref("");
const lastSignatureResult = ref(null);

const formHashes = ref({});

const computeHashes = async () => {
  const hashes = {};
  for (const form of props.selectedForms) {
    try {
      const msgBuffer = new TextEncoder().encode(form);
      const hashBuffer = await crypto.subtle.digest("SHA-256", msgBuffer);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      hashes[form] = hashArray
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");
    } catch {
      hashes[form] = `hash-mock-${form}`;
    }
  }
  formHashes.value = hashes;
};

// Compute hashes when component is opened or selectedForms change
watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      password.value = "";
      totp.value = "";
      signatureMeaning.value = "APPROVED";
      localError.value = "";
      lastSignatureResult.value = null;
      computeHashes();
    }
  }
);

watch(
  () => props.selectedForms,
  () => {
    if (props.isOpen) {
      computeHashes();
    }
  },
  { deep: true }
);

onMounted(() => {
  if (props.isOpen) {
    computeHashes();
  }
});

const handleExecuteSign = async () => {
  isSigning.value = true;
  localError.value = "";
  try {
    const studyId = props.studyId || "STUDY-USDM-001";
    const res = await signatureStore.submitBatchSignature({
      studyId,
      subjectId: props.subjectId,
      formIds: props.selectedForms,
      password: password.value,
      meaning: signatureMeaning.value,
    });
    lastSignatureResult.value = res;
    emit("signed", res);
  } catch (err) {
    // Automatically clear password on authentication failure
    password.value = "";

    // Handle re-auth error
    const errStatus = err.status || (err.response ? err.response.status : null);
    if (errStatus === 401) {
      localError.value =
        "Authentication failed: Invalid credentials or expired session (HTTP 401).";
    } else {
      localError.value =
        err.message || "An unexpected error occurred during re-authentication.";
    }
  } finally {
    isSigning.value = false;
  }
};
</script>

<style scoped>
@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}
</style>
