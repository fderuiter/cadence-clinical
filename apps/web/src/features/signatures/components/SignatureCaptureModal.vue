<template>
  <ClinicalModal
    id="signature-capture-modal"
    :is-open="isOpen"
    :teleport="false"
    :dismissible="!busy"
    size="sm"
    dialog-class="modal signature-capture-dialog"
    title="Electronic Signature & Identity Verification"
    aria-label="Electronic Signature Capture Modal"
    close-label="Close electronic signature dialog"
    @close="cancel"
  >
    <div ref="bodyRootRef" class="signature-capture-fields">
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
          <option value="Unblinded Statistician">Unblinded Statistician</option>
          <option value="Pharmacist">Pharmacist</option>
          <option value="Data Manager">Data Manager</option>
          <option value="Auditor">Auditor</option>
        </select>
      </div>

      <!-- Reuse GxpCredentialsInput -->
      <!-- prettier-ignore -->
      <GxpCredentialsInput
        v-model:username="usernameVal"
        v-model:totp="totp"
        :password-reentry-required="needsPasswordReentry"
        data-pragma="pragma: allowlist secret"
        :disabled="busy"
        @keyup-enter="confirm"
        v-model:password="password"/><!-- pragma: allowlist secret -->

      <div class="form-group last-group">
        <label for="sig-reason">Meaning of Signing / Signing Reason</label>
        <select
          id="sig-reason"
          v-model="signingReason"
          data-testid="sig-meaning-select"
          :disabled="busy"
        >
          <option value="" disabled>-- Select Meaning / Reason --</option>
          <option v-for="r in availableReasons" :key="r" :value="r">
            {{ r }}
          </option>
        </select>
      </div>

      <ModalErrorAlert
        id="sig-error-msg"
        :error="error"
        retry-label="Retry"
        dismiss-label="Dismiss"
        @retry="handleRetry"
        @dismiss="error = ''"
      />
    </div>

    <template #footer>
      <button
        id="btn-cancel-sig"
        type="button"
        class="btn btn-cancel"
        :disabled="busy"
        @click="cancel"
      >
        Cancel
      </button>
      <button
        id="btn-confirm-sig"
        type="button"
        class="btn btn-primary"
        :disabled="busy"
        @click="confirm"
      >
        {{ busy ? "Signing..." : "Verify & Sign" }}
      </button>
    </template>
  </ClinicalModal>
</template>

<script>
export const SHARED_SIGNATURE_MODAL_PROPS = {
  username: { type: String, default: "" },
  signerName: { type: String, default: "" },
  signerRole: { type: String, default: "" },
  role: { type: String, default: "" },
  semanticAction: { type: String, default: "" },
  onSign: { type: Function, default: null },
  reasons: { type: Array, default: null },
};
</script>

<script setup>
import { computed, ref, watch, inject } from "vue";
import { ClinicalModal, deriveSignerRoleFromClaims, ModalErrorAlert } from "ui";
import { useAuthStore } from "../../../stores/auth.js";
import GxpCredentialsInput from "./GxpCredentialsInput.vue";

const props = defineProps({
  ...SHARED_SIGNATURE_MODAL_PROPS,
  isOpen: {
    type: Boolean,
    required: true,
  },
  actionUrl: {
    type: String,
    required: true,
  },
});

const emit = defineEmits(["cancel", "success", "error"]);

const bodyRootRef = ref(null);

function resolveDefaultSignerRole() {
  const explicit = props.signerRole || props.role;
  if (explicit) {
    return explicit;
  }
  try {
    const authStore = useAuthStore();
    return deriveSignerRoleFromClaims(
      {
        username: authStore.user?.username || props.username || "",
        roles: authStore.rawRoles || [],
      },
      "Subject"
    );
  } catch {
    return deriveSignerRoleFromClaims(
      { username: props.username || "", roles: [] },
      "Subject"
    );
  }
}

const usernameVal = ref(props.username);
const signerNameVal = ref(props.signerName || props.username || "");
const signerRoleVal = ref(resolveDefaultSignerRole());
const password = ref("");
const totp = ref("");
const signingReason = ref("");
const busy = ref(false);
const error = ref("");
const needsPasswordReentry = ref(false);

// Inject dynamic decoupled signature service
const signatureService = inject("signatureService", null);

const DEFAULT_REASONS = [
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

const availableReasons = computed(() =>
  Array.isArray(props.reasons) && props.reasons.length > 0
    ? props.reasons
    : DEFAULT_REASONS
);

watch(availableReasons, (nextReasons) => {
  if (signingReason.value && !nextReasons.includes(signingReason.value)) {
    signingReason.value = "";
  }
});

watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      usernameVal.value = props.username;
      signerNameVal.value = props.signerName || props.username || "";
      signerRoleVal.value = resolveDefaultSignerRole();
      password.value = "";
      totp.value = "";
      signingReason.value = "";
      busy.value = false;
      error.value = "";
      needsPasswordReentry.value = false;
    }
  }
);

watch(password, (nextPassword) => {
  if (nextPassword) {
    needsPasswordReentry.value = false;
  }
});

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
  () => props.signerRole || props.role,
  () => {
    signerRoleVal.value = resolveDefaultSignerRole();
  }
);

function cancel() {
  password.value = "";
  totp.value = "";
  error.value = "";
  busy.value = false;
  needsPasswordReentry.value = false;
  emit("cancel");
}

function focusModalField(selector) {
  const el =
    bodyRootRef.value?.querySelector(selector) ||
    (typeof document !== "undefined" ? document.querySelector(selector) : null);
  if (el && typeof el.focus === "function") {
    el.focus();
  }
}

async function handleRetry() {
  if (!usernameVal.value) {
    focusModalField("#sig-username");
    return;
  }
  if (!password.value) {
    needsPasswordReentry.value = true;
    focusModalField("#sig-password");
    return;
  }
  await confirm();
}

function isFailedSignoffOutcome(result) {
  if (result === false) return true;
  if (!result || typeof result !== "object") return false;
  if (result.error || result.persisted === false) return true;
  if (
    typeof result.kind === "string" &&
    result.kind !== "signed" &&
    result.kind !== "saved"
  ) {
    return true;
  }
  if (
    typeof result.status === "string" &&
    ["FAILED", "ERROR", "REJECTED", "DENIED"].includes(
      result.status.toUpperCase()
    )
  ) {
    return true;
  }
  return false;
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
  const sRole = signerRoleVal.value || resolveDefaultSignerRole();

  // IMMEDIATELY clear sensitive fields to comply with GxP no-leak mandates
  password.value = "";
  totp.value = "";
  needsPasswordReentry.value = false;
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
      semanticAction:
        props.semanticAction ||
        (props.actionUrl.includes("capture-consent")
          ? "econsent.consent.capture"
          : undefined),
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
      if (isFailedSignoffOutcome(signoffResult)) {
        const failureMsg =
          (signoffResult &&
            typeof signoffResult === "object" &&
            (signoffResult.message || signoffResult.error)) ||
          "Signature action could not be completed.";
        throw new Error(String(failureMsg));
      }
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
    needsPasswordReentry.value = !password.value;
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
  needsPasswordReentry,
});
</script>

<style scoped>
:deep(.signature-capture-dialog) {
  max-width: 450px;
  color: var(--semantic-color-text-dark);
}

.signature-capture-fields {
  min-width: 0;
}

.modal-desc {
  margin: 0 0 16px 0;
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-body);
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
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-secondary);
}

.form-control,
select {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  padding: 8px;
  font-size: var(--font-size-md);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--radius-sm, 4px);
}

.btn {
  padding: 6px 12px;
  font-size: var(--font-size-xs);
  cursor: pointer;
  border-radius: var(--radius-sm, 4px);
  font-weight: 500;
  max-width: 100%;
  box-sizing: border-box;
}

.btn-cancel {
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-dark);
}

.btn-primary {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border: 1px solid var(--semantic-color-primary-dark);
}

.btn-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* Accessible focus outline ring */
select:focus:not(:focus-visible),
button:focus:not(:focus-visible) {
  outline: none;
}

select:focus-visible,
button:focus-visible {
  outline: 3px solid var(--accent) !important;
  outline-offset: 2px !important;
  box-shadow: 0 0 0 2px var(--semantic-color-text-on-solid) !important;
}

/* Touch Target Sizes for mobile and tablet devices */
@media (max-width: 1024px) {
  select,
  .btn {
    min-height: 48px;
    padding: 10px 14px;
    font-size: var(--font-size-md);
  }
}
</style>
