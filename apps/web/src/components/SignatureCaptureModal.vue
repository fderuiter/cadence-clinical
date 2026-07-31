<template>
  <div
    v-if="isOpen"
    id="signature-capture-modal"
    class="modal-overlay"
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
      class="modal"
      style="
        background: white;
        border-radius: 8px;
        width: 100%;
        max-width: 450px;
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
        overflow: hidden;
        color: #333;
      "
    >
      <div
        class="modal-header"
        style="
          padding: 16px;
          border-bottom: 1px solid #e2e8f0;
          font-weight: 600;
          font-size: 16px;
          color: #1e293b;
        "
      >
        Electronic Signature & Identity Verification
      </div>
      <div class="modal-body" style="padding: 16px">
        <p
          style="
            margin-bottom: 16px;
            font-size: 13px;
            color: #64748b;
            line-height: 1.4;
          "
        >
          To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>, you
          must re-verify your credentials and select a controlled signing reason
          to apply this digital signature.
        </p>

        <div class="form-group" style="margin-bottom: 12px">
          <label
            for="sig-username"
            style="
              display: block;
              font-weight: 500;
              margin-bottom: 4px;
              font-size: 13px;
              color: #475569;
            "
            >Username</label
          >
          <input
            id="sig-username"
            v-model="usernameVal"
            type="text"
            placeholder="Username"
            :disabled="busy"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid #cbd5e1;
              border-radius: 4px;
              font-size: 13px;
            "
          />
        </div>

        <div class="form-group" style="margin-bottom: 12px">
          <label
            for="sig-password"
            style="
              display: block;
              font-weight: 500;
              margin-bottom: 4px;
              font-size: 13px;
              color: #475569;
            "
            >Password</label
          >
          <input
            id="sig-password"
            v-model="password"
            type="password"
            placeholder="Enter your password to confirm identity..."
            :disabled="busy"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid #cbd5e1;
              border-radius: 4px;
              font-size: 13px;
            "
            @keyup.enter="confirm"
          />
        </div>

        <div class="form-group" style="margin-bottom: 12px">
          <label
            for="sig-totp"
            style="
              display: block;
              font-weight: 500;
              margin-bottom: 4px;
              font-size: 13px;
              color: #475569;
            "
            >MFA/TOTP Token (Optional)</label
          >
          <input
            id="sig-totp"
            v-model="totp"
            type="text"
            placeholder="Enter 6-digit TOTP code..."
            :disabled="busy"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid #cbd5e1;
              border-radius: 4px;
              font-size: 13px;
            "
          />
        </div>

        <div class="form-group" style="margin-bottom: 16px">
          <label
            for="sig-reason"
            style="
              display: block;
              font-weight: 500;
              margin-bottom: 4px;
              font-size: 13px;
              color: #475569;
            "
            >Signing Reason</label
          >
          <select
            id="sig-reason"
            v-model="signingReason"
            :disabled="busy"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid #cbd5e1;
              border-radius: 4px;
              font-size: 13px;
              background: white;
            "
          >
            <option value="" disabled>-- Select Reason --</option>
            <option v-for="r in reasons" :key="r" :value="r">{{ r }}</option>
          </select>
        </div>

        <div
          v-if="error"
          id="sig-error-msg"
          style="
            margin-top: 8px;
            color: #ef4444;
            font-size: 13px;
            font-weight: 500;
          "
        >
          {{ error }}
        </div>
      </div>
      <div
        class="modal-footer"
        style="
          padding: 12px 16px;
          border-top: 1px solid #e2e8f0;
          display: flex;
          justify-content: flex-end;
          gap: 8px;
          background: #f8fafc;
        "
      >
        <button
          id="btn-cancel-sig"
          class="btn"
          style="
            padding: 6px 12px;
            font-size: 13px;
            cursor: pointer;
            border: 1px solid #cbd5e1;
            background: white;
            border-radius: 4px;
          "
          :disabled="busy"
          @click="cancel"
        >
          Cancel
        </button>
        <button
          id="btn-confirm-sig"
          class="btn btn-primary"
          style="
            padding: 6px 12px;
            font-size: 13px;
            cursor: pointer;
            background: #007bff;
            color: white;
            border: none;
            border-radius: 4px;
          "
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
import { ref, watch } from "vue";
import { etmfService } from "../api/etmf";

const props = defineProps({
  isOpen: {
    type: Boolean,
    required: true,
  },
  username: {
    type: String,
    default: "",
  },
  actionUrl: {
    type: String,
    required: true,
  },
  // async callback to handle the final sign request using sigToken and signingReason
  // e.g., (sigToken, signingReason) => Promise<any>
  onSign: {
    type: Function,
    default: null,
  },
});

const emit = defineEmits(["cancel", "success", "error"]);

const usernameVal = ref(props.username);
const password = ref("");
const totp = ref("");
const signingReason = ref("");
const busy = ref(false);
const error = ref("");

const reasons = [
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

  // IMMEDIATELY clear sensitive fields to comply with GxP no-leak mandates
  password.value = "";
  totp.value = "";
  error.value = "";
  busy.value = true;

  try {
    // Step 1: Gateway step-up re-authentication
    const reauthRes = await etmfService.verifySignature({
      username: u,
      password: p,
      totp: t,
      action: props.actionUrl,
    });

    const sigToken = reauthRes.sig_token;

    // Step 2: Call document sign-off (either via props callback or internal client)
    let signoffResult;
    if (props.onSign) {
      signoffResult = await props.onSign(sigToken, r);
    } else {
      // Extract document ID from actionUrl if possible, or assume it's handled by caller
      const docIdMatch = props.actionUrl.match(
        /\/documents\/([^/]+)\/sign-off/
      );
      if (!docIdMatch) {
        throw new Error("Unable to extract document ID from action URL.");
      }
      const documentId = docIdMatch[1];
      signoffResult = await etmfService.signDocument(
        documentId,
        { signingReason: r },
        { changeReason: `Part 11 Document Sign-off: Reason - ${r}`, sigToken }
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
</script>
