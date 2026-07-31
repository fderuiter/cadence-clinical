<template>
  <div
    v-if="isOpen"
    ref="modalRef"
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
    @keydown="handleKeyDown"
  >
    <div
      class="modal"
      style="
        background: white;
        border-radius: 8px;
        width: 100%;
        max-width: 500px;
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
        overflow: hidden;
        color: #333;
        font-family: system-ui, -apple-system, sans-serif;
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
          display: flex;
          justify-content: space-between;
          align-items: center;
        "
      >
        <span>Electronic Signature & Identity Verification</span>
        <button
          style="
            background: none;
            border: none;
            font-size: 1.5rem;
            cursor: pointer;
            color: #64748b;
            line-height: 1;
            padding: 0;
          "
          :disabled="busy"
          @click="cancel"
        >
          &times;
        </button>
      </div>

      <div class="modal-body" style="padding: 16px; max-height: 70vh; overflow-y: auto;">
        <p
          style="
            margin: 0 0 16px 0;
            font-size: 13px;
            color: #64748b;
            line-height: 1.4;
          "
        >
          To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>, you
          must re-verify your credentials and select a controlled signing reason
          to apply this digital signature.
        </p>

        <!-- Cryptographic Preview Section -->
        <div
          v-if="computedItems.length > 0"
          class="preview-section"
          style="
            margin-bottom: 16px;
            border: 1px solid #cbd5e1;
            border-radius: 6px;
            overflow: hidden;
          "
        >
          <div
            style="
              background: #f8fafc;
              padding: 8px 12px;
              font-size: 12px;
              font-weight: 600;
              color: #475569;
              border-bottom: 1px solid #cbd5e1;
            "
          >
            Cryptographic Preview Hashes
          </div>
          <div style="max-height: 120px; overflow-y: auto; padding: 4px 8px;">
            <table style="width: 100%; border-collapse: collapse; font-size: 11px;">
              <tbody>
                <tr
                  v-for="item in computedItems"
                  :key="item"
                  style="border-bottom: 1px solid #f1f5f9;"
                >
                  <td style="padding: 6px; font-weight: 500; color: #1e293b; max-width: 150px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
                    {{ item }}
                  </td>
                  <td style="padding: 6px; font-family: monospace; color: #64748b; text-align: right;">
                    {{ formHashes[item] || "Computing hash..." }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Identity Inputs -->
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
              box-sizing: border-box;
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
              box-sizing: border-box;
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
              box-sizing: border-box;
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
              box-sizing: border-box;
              padding: 8px;
              border: 1px solid #cbd5e1;
              border-radius: 4px;
              font-size: 13px;
              background: white;
            "
          >
            <option value="" disabled>-- Select Reason --</option>
            <option v-for="r in reasonsList" :key="r.value" :value="r.value">
              {{ r.label }}
            </option>
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
          :disabled="busy || !password || !signingReason"
          @click="confirm"
        >
          {{ busy ? "Signing..." : "Verify & Sign" }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, computed, onMounted } from "vue";

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
    default: "",
  },
  previewPayload: {
    type: String,
    default: "",
  },
  selectedForms: {
    type: Array,
    default: () => [],
  },
  reasons: {
    type: Array,
    default: () => [],
  },
  onSign: {
    type: Function,
    default: null,
  },
});

const emit = defineEmits(["cancel", "success", "error", "close", "signed"]);

const modalRef = ref(null);
const usernameVal = ref(props.username);
const password = ref("");
const totp = ref("");
const signingReason = ref("");
const busy = ref(false);
const error = ref("");
const formHashes = ref({});

const defaultReasons = [
  { value: "APPROVED", label: "Principal Investigator Approval" },
  { value: "REVIEWED", label: "CRA Monitor Review" },
  { value: "VERIFIED_SDV", label: "Source Data Verification (SDV) Confirmed" },
  { value: "AUTHOR", label: "Author / Document Creation" },
  { value: "REVIEW", label: "Review / QC Approved" },
  { value: "APPROVAL", label: "Approval Sign-Off" },
  { value: "SPONSOR_APPROVAL", label: "Sponsor Representative Approval" },
  { value: "INVESTIGATOR_SIGNATURE", label: "Investigator Signature" },
  { value: "TECHNICAL_QC", label: "Technical QC Passed" },
  { value: "CLINICAL_QC", label: "Clinical QC Passed" },
  { value: "DATA_LOCK", label: "Data Lock Authorization" },
  { value: "SYSTEM_SEAL", label: "System Execution Seal" },
];

const reasonsList = computed(() => {
  if (props.reasons && props.reasons.length > 0) {
    return props.reasons.map((r) => {
      if (typeof r === "string") {
        const found = defaultReasons.find((dr) => dr.value === r);
        return found ? found : { value: r, label: r };
      }
      return r;
    });
  }
  return defaultReasons;
});

const computedItems = computed(() => {
  if (props.selectedForms && props.selectedForms.length > 0) {
    return props.selectedForms;
  }
  if (props.previewPayload) {
    return [props.previewPayload];
  }
  return [];
});

const computeHashes = async () => {
  const hashes = {};
  for (const item of computedItems.value) {
    try {
      const msgBuffer = new TextEncoder().encode(item);
      const hashBuffer = await crypto.subtle.digest("SHA-256", msgBuffer);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      hashes[item] = hashArray
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");
    } catch {
      hashes[item] = `hash-error-${item}`;
    }
  }
  formHashes.value = hashes;
};

const handleKeyDown = (e) => {
  if (e.key === "Tab") {
    if (!modalRef.value) return;
    const focusableSelectors =
      "input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled])";
    const focusableElements = Array.from(
      modalRef.value.querySelectorAll(focusableSelectors)
    );
    if (focusableElements.length === 0) return;

    const first = focusableElements[0];
    const last = focusableElements[focusableElements.length - 1];

    if (e.shiftKey) {
      if (document.activeElement === first) {
        last.focus();
        e.preventDefault();
      }
    } else {
      if (document.activeElement === last) {
        first.focus();
        e.preventDefault();
      }
    }
  }
};

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
      computeHashes();
      // focus username on open
      setTimeout(() => {
        if (modalRef.value) {
          const input = modalRef.value.querySelector("#sig-username");
          if (input) input.focus();
        }
      }, 50);
    }
  }
);

watch(
  () => computedItems.value,
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

function cancel() {
  password.value = "";
  totp.value = "";
  error.value = "";
  busy.value = false;
  emit("cancel");
  emit("close");
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

  // AUTOMATIC CREDENTIAL HYGIENE: immediately clear reactive password states
  password.value = "";
  totp.value = "";
  error.value = "";
  busy.value = true;

  try {
    if (props.onSign) {
      const result = await props.onSign({
        username: u,
        password: p,
        totp: t,
        signingReason: r,
        actionUrl: props.actionUrl,
      });
      emit("success", result);
      emit("signed", result);
    } else {
      // standard mock sign behavior if callback is not provided
      const dummyResult = {
        status: "success",
        signature_id: "sig_" + Math.random().toString(36).substring(2, 9),
        timestamp_utc: new Date().toISOString(),
        content_digest: formHashes.value[computedItems.value[0]] || "digest-placeholder",
        audit_tx: "tx_" + Math.random().toString(36).substring(2, 9),
      };
      emit("success", dummyResult);
      emit("signed", dummyResult);
    }
  } catch (err) {
    error.value = err.message || "An unexpected error occurred during digital signing.";
    emit("error", err);
  } finally {
    busy.value = false;
  }
}
</script>
