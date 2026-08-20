<template>
  <div
    v-if="isOpen"
    id="signature-capture-modal"
    ref="modalRef"
    class="modal-overlay"
    role="dialog"
    aria-modal="true"
    aria-label="Electronic Signature Capture Modal"
  >
    <div class="modal">
      <div class="modal-header">
        Electronic Signature & Identity Verification
      </div>
      <div class="modal-body">
        <p class="modal-desc">
          To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>, you
          must re-verify your credentials and select a controlled signing reason
          to apply this digital signature.
        </p>

        <!-- Signer Identity & Role Fields -->
        <div class="form-group">
          <label for="sig-signer-name">Signer Full Name</label>
          <input
            id="sig-signer-name"
            v-model="signerNameVal"
            type="text"
            placeholder="e.g. John Doe"
            :disabled="busy"
            class="form-control"
          />
        </div>

        <div class="form-group">
          <label for="sig-role">Signer Role</label>
          <select id="sig-role" v-model="signerRoleVal" :disabled="busy">
            <option value="Subject">Subject / Participant</option>
            <option value="PI">PI / Principal Investigator</option>
            <option value="Site CRC">Site CRC</option>
            <option value="Investigator">Investigator</option>
            <option value="Sponsor Designer">Sponsor Designer</option>
            <option value="Data Manager">Data Manager</option>
            <option value="Auditor">Auditor</option>
          </select>
        </div>

        <!-- Reuse GxpCredentialsInput -->
        <GxpCredentialsInput
          v-model:username="usernameVal"
          v-model:password="password"
          v-model:totp="totp"
          :disabled="busy"
          @keyup-enter="confirm"
        />

        <div class="form-group last-group">
          <label for="sig-reason">Meaning of Signing / Signing Reason</label>
          <select
            id="sig-reason"
            v-model="signingReason"
            data-testid="sig-meaning-select"
            :disabled="busy"
          >
            <option value="" disabled>-- Select Meaning / Reason --</option>
            <option v-for="r in reasons" :key="r" :value="r">
              {{ r }}
            </option>
          </select>
        </div>

        <div v-if="error" id="sig-error-msg" class="error-msg">
          {{ error }}
        </div>
      </div>
      <div class="modal-footer">
        <button
          id="btn-cancel-sig"
          class="btn btn-cancel"
          :disabled="busy"
          @click="cancel"
        >
          Cancel
        </button>
        <button
          id="btn-confirm-sig"
          class="btn btn-primary"
          :disabled="busy"
          @click="confirm"
        >
          {{ busy ? "Signing..." : "Verify & Sign" }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, inject } from "vue";
import { useFocusTrap } from "@/composables/useFocusTrap";
import { useEscapeClose } from "@/composables/useEscapeClose";
import GxpCredentialsInput from "./GxpCredentialsInput.vue";

const props = defineProps({
  isOpen: {
    type: Boolean,
    required: true,
  },
  username: {
    type: String,
    default: "",
  },
  signerName: {
    type: String,
    default: "",
  },
  role: {
    type: String,
    default: "Subject",
  },
  signerRole: {
    type: String,
    default: "",
  },
  actionUrl: {
    type: String,
    required: true,
  },
  // async callback to handle the final sign request using sigToken and signingReason
  // e.g., (sigToken, signingReason, metadata) => Promise<any>
  onSign: {
    type: Function,
    default: null,
  },
});

const emit = defineEmits(["cancel", "success", "error"]);

const modalRef = ref(null);
useFocusTrap(modalRef);
useEscapeClose(() => emit("cancel"));

const usernameVal = ref(props.username);
const signerNameVal = ref(props.signerName || props.username || "");
const signerRoleVal = ref(props.signerRole || props.role || "Subject");
const password = ref(""); // pragma: allowlist secret
const totp = ref("");
const signingReason = ref("");
const busy = ref(false);
const error = ref("");

// Inject dynamic decoupled signature service
const signatureService = inject("signatureService", null);

const reasons = [
  "I agree to participate",
  "Investigator Certification",
  "I have read, understood, and agree to participate in this research study",
  "AUTHOR",
  "REVIEW",
  "APPROVAL",
  "SPONSOR_APPROVAL",
  "INVESTIGATOR_SIGNATURE",
  "TECHNICAL_QC",
  "CLINICAL_QC",
  "DATA_LOCK",
  "SYSTEM_SEAL",
];

watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      usernameVal.value = props.username;
      signerNameVal.value = props.signerName || props.username || "";
      signerRoleVal.value = props.role || "Subject";
      password.value = "";
      totp.value = "";
      signingReason.value = "";
      busy.value = false;
      error.value = "";
    }
  }
);

watch(
  () => props.username,
  (newVal) => {
    usernameVal.value = newVal;
    if (!signerNameVal.value) {
      signerNameVal.value = newVal;
    }
  }
);

watch(
  () => props.signerName,
  (newVal) => {
    if (newVal) signerNameVal.value = newVal;
  }
);

watch(
  () => props.role,
  (newVal) => {
    if (newVal) signerRoleVal.value = newVal;
  }
);

function cancel() {
  password.value = "";
  totp.value = "";
  error.value = "";
  busy.value = false;
  emit("cancel");
}

async function confirm() {
  if (!usernameVal.value) {
    error.value = "Username is required.";
    return;
  }
  if (!password.value) {
    error.value = "Password is required.";
    return;
  }
  if (!signingReason.value) {
    error.value = "Signing reason is required.";
    return;
  }

  const u = usernameVal.value;
  const p = password.value;
  const t = totp.value || null;
  const r = signingReason.value;
  const sName = signerNameVal.value || u;
  const sRole = signerRoleVal.value || "Subject";

  // IMMEDIATELY clear sensitive fields to comply with GxP no-leak mandates
  password.value = "";
  totp.value = "";
  error.value = "";
  busy.value = true;

  try {
    const service = signatureService;
    if (!service) {
      throw new Error("No decoupled signature service is provided.");
    }

    // Step 1: Gateway step-up re-authentication
    const reauthRes = await service.verifySignature({
      username: u,
      password: p,
      totp: t,
      action: props.actionUrl,
    });

    const sigToken = reauthRes.sig_token;

    // Step 2: Call document sign-off (either via props callback or internal client)
    let signoffResult;
    const metadata = {
      signerName: sName,
      signerRole: sRole,
      meaningOfSigning: r,
      signingReason: r,
    };

    if (props.onSign) {
      signoffResult = await props.onSign(sigToken, r, metadata);
    } else {
      // Extract document ID from actionUrl if possible, or assume it's handled by caller
      const docIdMatch = props.actionUrl.match(
        /\/documents\/([^/]+)\/sign-off/
      );
      if (!docIdMatch) {
        throw new Error("Unable to extract document ID from action URL.");
      }
      const documentId = docIdMatch[1];
      signoffResult = await service.signDocument(
        documentId,
        { signingReason: r },
        {
          changeReason: `Part 11 Document Sign-off: Reason - ${r}`,
          sigToken,
        }
      );
    }

    emit("success", signoffResult);
  } catch (err) {
    console.error("Signature verification / sign-off error:", err);

    // Map invalid credentials, ROLE_INSUFFICIENT, expired/missing step-up token, and signing failures to actionable UI messages.
    let displayError = "An unexpected error occurred during digital signing.";

    const errMessage = err.message || "";
    const errResponse = err.response || {};
    const errData = errResponse.data || {};
    const errDetail = errData.detail || "";

    if (
      errMessage.includes("ROLE_INSUFFICIENT") ||
      errDetail === "ROLE_INSUFFICIENT"
    ) {
      displayError =
        "Forbidden: Your role does not possess permissions to sign documents.";
    } else if (
      errMessage.includes("REAUTHENTICATION_REQUIRED") ||
      errDetail === "REAUTHENTICATION_REQUIRED" ||
      errMessage.includes("expired") ||
      errDetail.includes("expired")
    ) {
      displayError =
        "Identity verification expired or invalid. Please try again.";
    } else if (
      errMessage.includes("Invalid credentials") ||
      errDetail.includes("Invalid credentials") ||
      errMessage.includes("401") ||
      errResponse.status === 401
    ) {
      displayError = "Identity verification failed: Invalid credentials.";
    } else if (errDetail) {
      displayError = `Signing failed: ${errDetail}`;
    } else if (errMessage) {
      displayError = `Signing failed: ${errMessage}`;
    }

    error.value = displayError;
    emit("error", err);
  } finally {
    busy.value = false;
  }
}

// Expose internal state variables for compatibility with test assertions
defineExpose({
  password,
  totp,
  signerNameVal,
  signerRoleVal,
  signingReason,
});
</script>

<style scoped>
.modal-overlay {
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
}

.modal {
  background: white;
  border-radius: 8px;
  width: 100%;
  max-width: 450px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  overflow: hidden;
  color: #333;
}

.modal-header {
  padding: 16px;
  border-bottom: 1px solid #e2e8f0;
  font-weight: 600;
  font-size: 16px;
  color: white;
}

.modal-body {
  padding: 16px;
}

.modal-desc {
  margin-bottom: 16px;
  font-size: 13px;
  color: #334155;
  line-height: 1.4;
}

.form-group {
  margin-bottom: 12px;
}

.form-group.last-group {
  margin-bottom: 16px;
}

label {
  display: block;
  font-weight: 500;
  margin-bottom: 4px;
  font-size: 13px;
  color: #475569;
}

select {
  width: 100%;
  padding: 8px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 13px;
  background: white;
  box-sizing: border-box;
}

.error-msg {
  margin-top: 8px;
  color: #ef4444;
  font-size: 13px;
  font-weight: 500;
}

.modal-footer {
  padding: 12px 16px;
  border-top: 1px solid #e2e8f0;
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  background: #f8fafc;
}

.btn {
  padding: 6px 12px;
  font-size: 13px;
  cursor: pointer;
  border-radius: 4px;
  font-weight: 500;
}

.btn-cancel {
  border: 1px solid #cbd5e1;
  background: white;
}

.btn-primary {
  background: var(--accent);
  color: white;
  border: none;
}

.btn-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* Accessible focus outline ring */
select:focus,
button:focus {
  outline: none;
}

select:focus-visible,
button:focus-visible {
  outline: 3px solid var(--accent, #2563eb) !important;
  outline-offset: 2px !important;
}

/* Touch Target Sizes for mobile and tablet devices */
@media (max-width: 1024px) {
  select,
  .btn {
    min-height: 48px;
    padding: 10px 14px;
    font-size: 16px;
  }
}
</style>
