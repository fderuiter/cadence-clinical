<template>
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div v-if="isOpen" class="share-drawer-overlay" @click.self="$emit('close')">
    <div
      ref="drawerRef"
      class="share-drawer"
      role="dialog"
      aria-modal="true"
      aria-label="Share Management Drawer"
    >
      <!-- Drawer Header -->
      <div class="drawer-header">
        <div class="header-info">
          <span class="share-header-icon">🔗</span>
          <div>
            <h3 class="drawer-title">
              {{
                isBulk ? `Bulk Share (${files.length} Files)` : "Share Document"
              }}
            </h3>
            <p class="drawer-subtitle" :title="fileNamesDisplay">
              {{ fileNamesDisplay }}
            </p>
          </div>
        </div>
        <button
          class="close-drawer-btn"
          aria-label="Close share drawer"
          @click="$emit('close')"
        >
          ✕
        </button>
      </div>

      <!-- Tab Navigation -->
      <div class="tab-navigation">
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'internal' }"
          @click="activeTab = 'internal'"
        >
          👥 Internal Share
        </button>
        <button
          v-if="!isBulk"
          class="tab-btn"
          :class="{ active: activeTab === 'guest' }"
          @click="activeTab = 'guest'"
        >
          🌐 External Guest Link
        </button>
        <button
          v-if="!isBulk"
          class="tab-btn"
          :class="{ active: activeTab === 'active_grants' }"
          @click="activeTab = 'active_grants'"
        >
          🛡️ Active Grants ({{ activeGrantsList.length }})
        </button>
      </div>

      <!-- Drawer Body -->
      <div class="drawer-body">
        <!-- TAB 1: Internal Share Form -->
        <div v-if="activeTab === 'internal'" class="tab-content">
          <form @submit.prevent="submitInternalShare">
            <!-- User / Role Target -->
            <div class="form-group">
              <label for="share-user-input" class="form-label required">
                Grant Access To (User ID or Role)
              </label>
              <div class="autocomplete-wrapper">
                <input
                  id="share-user-input"
                  v-model="internalTarget"
                  type="text"
                  placeholder="e.g. cra_fderuiter, auditor, site_crc, data_manager..."
                  class="form-control"
                  required
                  @focus="showUserSuggestions = true"
                />
                <div
                  v-if="showUserSuggestions && userSuggestions.length > 0"
                  class="suggestions-dropdown"
                >
                  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
                  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
                  <div
                    v-for="sugg in userSuggestions"
                    :key="sugg.value"
                    class="suggestion-item"
                    @click="selectSuggestion(sugg)"
                  >
                    <span class="sugg-label">{{ sugg.label }}</span>
                    <span class="sugg-type">{{ sugg.type }}</span>
                  </div>
                </div>
              </div>
            </div>

            <!-- Scope Selector -->
            <div class="form-group">
              <label for="share-scope-select" class="form-label"
                >Delegation Scope</label
              >
              <select
                id="share-scope-select"
                v-model="shareScope"
                class="form-control"
              >
                <option value="individual">
                  Individual (Specific User ID)
                </option>
                <option value="role">Role (All Users in Assigned Role)</option>
                <option value="site">
                  Site Scope (All Investigators at Site)
                </option>
                <option value="study">Study Scope (Entire Study Team)</option>
              </select>
            </div>

            <!-- Permission Level Selector -->
            <div class="form-group">
              <label for="share-perm-select" class="form-label required"
                >Permission Level</label
              >
              <select
                id="share-perm-select"
                v-model="permissionLevel"
                class="form-control"
                required
              >
                <option value="view">
                  👁️ View (Read-Only Watermarked Preview)
                </option>
                <option value="comment">
                  💬 Comment (Review & Annotation)
                </option>
                <option value="download">
                  📥 Download (Direct Binary Access)
                </option>
                <option value="upload_revision">
                  📤 Upload Revision (New Version Drafting)
                </option>
                <option value="reshare">
                  🔁 Reshare (Sub-Delegation Authority)
                </option>
                <option value="approve">
                  ✅ Approve (Sign-off & Regulatory Acceptance)
                </option>
                <option value="expire_revoke">
                  🔒 Admin / Expire-Revoke (Full Delegation Authority)
                </option>
              </select>
            </div>

            <!-- Expiry Toggle -->
            <div class="form-group">
              <label class="checkbox-label">
                <input
                  v-model="hasExpiry"
                  type="checkbox"
                  class="custom-checkbox"
                />
                <span>Set Time-Bounded Expiration Date</span>
              </label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-if="hasExpiry"
                v-model="expiresAt"
                type="datetime-local"
                class="form-control expiry-input"
                required
              />
            </div>

            <!-- Mandatory Reason for Change (21 CFR Part 11) -->
            <div class="form-group">
              <label for="share-reason-input" class="form-label required">
                21 CFR Part 11 Reason for Delegation
              </label>
              <textarea
                id="share-reason-input"
                v-model="reasonForChange"
                rows="3"
                placeholder="Specify the regulatory or scientific justification for granting this permission (e.g. 'Site monitoring audit inspection', 'Protocol safety review delegation')."
                class="form-control"
                required
              />
            </div>

            <div v-if="submitSuccessMsg" class="success-alert">
              ✅ {{ submitSuccessMsg }}
            </div>

            <!-- Submit Button -->
            <div class="drawer-actions">
              <button
                type="button"
                class="btn btn-outline-secondary"
                @click="$emit('close')"
              >
                Cancel
              </button>
              <button
                type="submit"
                class="btn btn-primary btn-submit-grant"
                :disabled="
                  isSubmitting || !internalTarget || !reasonForChange.trim()
                "
              >
                <span v-if="isSubmitting">Delegating...</span>
                <span v-else>🔒 Grant Access</span>
              </button>
            </div>
          </form>
        </div>

        <!-- TAB 2: External Guest Link Generator -->
        <div v-else-if="activeTab === 'guest'" class="tab-content">
          <div class="guest-info-banner">
            <strong>🌐 Public Protected Guest Links:</strong>
            Generates a signed, time-limited URL for external reviewers (e.g.
            CRO partners, external IRBs). Access is strictly read-only and
            logged to the GxP audit ledger.
          </div>

          <form v-if="!generatedGuestUrl" @submit.prevent="submitGuestLink">
            <div class="form-group">
              <label for="guest-expiry-select" class="form-label required"
                >Link Expiration</label
              >
              <select
                id="guest-expiry-select"
                v-model="guestExpiryHours"
                class="form-control"
              >
                <option :value="24">24 Hours (Standard Review)</option>
                <option :value="72">3 Days (Weekend Window)</option>
                <option :value="168">7 Days (1 Week)</option>
                <option :value="720">30 Days (Full Regulatory Review)</option>
              </select>
            </div>

            <!-- Mandatory Reason for Change -->
            <div class="form-group">
              <label for="guest-reason-input" class="form-label required">
                21 CFR Part 11 Justification
              </label>
              <textarea
                id="guest-reason-input"
                v-model="guestReason"
                rows="3"
                placeholder="Specify the external recipient and justification (e.g. 'External IRB compliance audit review')."
                class="form-control"
                required
              />
            </div>

            <div class="drawer-actions">
              <button
                type="button"
                class="btn btn-outline-secondary"
                @click="$emit('close')"
              >
                Cancel
              </button>
              <button
                type="submit"
                class="btn btn-primary btn-create-guest-link"
                :disabled="isSubmitting || !guestReason.trim()"
              >
                <span v-if="isSubmitting">Generating...</span>
                <span v-else>🔗 Generate Guest Link</span>
              </button>
            </div>
          </form>

          <!-- Generated Link Display Card -->
          <div v-else class="generated-link-card">
            <div class="link-header">
              <span class="badge status-approved">LINK ACTIVE</span>
              <span class="expires-text"
                >Expires in {{ guestExpiryHours }}h</span
              >
            </div>
            <div class="url-copy-box">
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                type="text"
                :value="generatedGuestUrl"
                readonly
                class="form-control url-input"
              />
              <button
                type="button"
                class="btn btn-primary btn-copy-link"
                @click="copyGuestUrl"
              >
                {{ isCopied ? "✓ Copied!" : "📋 Copy Link" }}
              </button>
            </div>
            <p class="gxp-link-meta">
              Token HMAC signed and stored in audit ledger.
            </p>
            <button
              type="button"
              class="btn btn-sm btn-outline-secondary mt-3"
              @click="generatedGuestUrl = ''"
            >
              + Create Another Link
            </button>
          </div>
        </div>

        <!-- TAB 3: Active Grants List & Revocation -->
        <div v-else-if="activeTab === 'active_grants'" class="tab-content">
          <div
            v-if="activeGrantsList.length === 0"
            class="empty-grants-message"
          >
            No active share delegations found for this document.
          </div>

          <div v-else class="grants-list-container">
            <div
              v-for="grant in activeGrantsList"
              :key="grant.id"
              class="grant-row-card"
            >
              <div class="grant-details">
                <div class="grant-header-line">
                  <span class="grantee-name">{{
                    grant.granted_to_user_id || "All Users"
                  }}</span>
                  <span class="badge status-review">{{
                    grant.scope.toUpperCase()
                  }}</span>
                  <span class="badge status-approved">{{
                    grant.permission_level.toUpperCase()
                  }}</span>
                </div>
                <div class="grant-meta-line">
                  <span
                    >Granted By:
                    <strong>{{ grant.granted_by_user_id }}</strong></span
                  >
                  <span v-if="grant.expires_at"
                    >Expires: {{ formatDate(grant.expires_at) }}</span
                  >
                  <span v-else>No Expiration</span>
                </div>
                <div class="grant-reason-line">
                  <em>"{{ grant.reason_for_change }}"</em>
                </div>
              </div>
              <div class="grant-actions">
                <button
                  class="btn btn-sm btn-outline-danger btn-revoke-grant"
                  title="Revoke this grant"
                  @click="initiateRevoke(grant)"
                >
                  Revoke
                </button>
              </div>
            </div>
          </div>

          <!-- Revoke Confirmation Modal / Overlay -->
          <div v-if="grantToRevoke" class="revoke-confirm-card">
            <h4>Revoke Access Delegation</h4>
            <p>
              Are you sure you want to revoke access for
              <strong>{{ grantToRevoke.granted_to_user_id }}</strong
              >?
            </p>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label class="form-label required"
                >Revocation Reason (21 CFR Part 11):</label
              >
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="revokeReason"
                type="text"
                class="form-control"
                placeholder="Reason for revoking grant..."
                required
              />
            </div>
            <div class="drawer-actions">
              <button
                type="button"
                class="btn btn-sm btn-outline-secondary"
                @click="grantToRevoke = null"
              >
                Cancel
              </button>
              <button
                type="button"
                class="btn btn-sm btn-danger btn-confirm-revoke"
                :disabled="!revokeReason.trim()"
                @click="confirmRevoke"
              >
                Confirm Revocation
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from "vue";
import { useFileshareStore } from "../../stores/fileshare";
import { useEscapeClose } from "@/composables/useEscapeClose";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  files: {
    type: Array,
    default: () => [],
  },
});

const emit = defineEmits([
  "close",
  "grant-created",
  "grant-revoked",
  "guest-link-created",
]);

const fileshareStore = useFileshareStore();
const drawerRef = ref(null);

useEscapeClose(() => emit("close"));

const activeTab = ref("internal");
const isSubmitting = ref(false);
const submitSuccessMsg = ref("");

// Internal share form state
const internalTarget = ref("");
const shareScope = ref("individual");
const permissionLevel = ref("view");
const hasExpiry = ref(false);
const expiresAt = ref("");
const reasonForChange = ref("");
const showUserSuggestions = ref(false);

// Guest link state
const guestExpiryHours = ref(24);
const guestReason = ref("");
const generatedGuestUrl = ref("");
const isCopied = ref(false);

// Revocation state
const grantToRevoke = ref(null);
const revokeReason = ref("");

const isBulk = computed(() => props.files.length > 1);

const fileNamesDisplay = computed(() => {
  if (props.files.length === 0) return "No files selected";
  if (props.files.length === 1) return props.files[0].filename;
  return `${props.files[0].filename} and ${props.files.length - 1} other files`;
});

const activeGrantsList = computed(() => {
  if (props.files.length === 1) {
    return fileshareStore.getGrantsForFile(props.files[0].id);
  }
  return [];
});

const availableUsersAndRoles = [
  {
    value: "cra_fderuiter",
    label: "cra_fderuiter (CRA Monitor)",
    type: "User",
  },
  { value: "crc_user_101", label: "crc_user_101 (Site CRC)", type: "User" },
  { value: "crc_user_102", label: "crc_user_102 (Site CRC)", type: "User" },
  {
    value: "auditor_user_01",
    label: "auditor_user_01 (GxP Auditor)",
    type: "User",
  },
  {
    value: "data_manager_01",
    label: "data_manager_01 (Data Manager)",
    type: "User",
  },
  {
    value: "sponsor_designer_01",
    label: "sponsor_designer_01 (Protocol Designer)",
    type: "User",
  },
  {
    value: "cra",
    label: "cra (Role: All Clinical Research Associates)",
    type: "Role",
  },
  {
    value: "crc",
    label: "crc (Role: All Clinical Research Coordinators)",
    type: "Role",
  },
  { value: "auditor", label: "auditor (Role: All GxP Auditors)", type: "Role" },
  {
    value: "data_manager",
    label: "data_manager (Role: All Data Managers)",
    type: "Role",
  },
];

const userSuggestions = computed(() => {
  const q = internalTarget.value.trim().toLowerCase();
  if (!q) return availableUsersAndRoles;
  return availableUsersAndRoles.filter(
    (u) =>
      u.value.toLowerCase().includes(q) || u.label.toLowerCase().includes(q)
  );
});

function selectSuggestion(sugg) {
  internalTarget.value = sugg.value;
  if (sugg.type === "Role") {
    shareScope.value = "role";
  } else {
    shareScope.value = "individual";
  }
  showUserSuggestions.value = false;
}

watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      submitSuccessMsg.value = "";
      generatedGuestUrl.value = "";
      grantToRevoke.value = null;
      if (props.files.length === 1) {
        fileshareStore.fetchGrants(props.files[0].id);
      }
    }
  }
);

async function submitInternalShare() {
  if (!internalTarget.value || !reasonForChange.value.trim()) return;

  isSubmitting.value = true;
  submitSuccessMsg.value = "";

  try {
    const payload = {
      granted_to_user_id: internalTarget.value.trim(),
      scope: shareScope.value,
      permission_level: permissionLevel.value,
      expires_at:
        hasExpiry.value && expiresAt.value
          ? new Date(expiresAt.value).toISOString()
          : null,
      reason_for_change: reasonForChange.value.trim(),
    };

    for (const file of props.files) {
      await fileshareStore.addGrant(file.id, payload);
    }

    submitSuccessMsg.value = `Access successfully granted to ${internalTarget.value}!`;
    emit("grant-created", payload);
    internalTarget.value = "";
    reasonForChange.value = "";
  } catch (err) {
    console.error("Failed to create share grant:", err);
  } finally {
    isSubmitting.value = false;
  }
}

async function submitGuestLink() {
  if (props.files.length === 0 || !guestReason.value.trim()) return;

  isSubmitting.value = true;
  try {
    const file = props.files[0];
    const payload = {
      expires_in_hours: Number(guestExpiryHours.value),
      reason_for_change: guestReason.value.trim(),
    };

    const link = await fileshareStore.generateGuestLink(file.id, payload);
    if (link) {
      generatedGuestUrl.value =
        link.guest_url || `https://cadence.example.com/share/${link.id}`;
      emit("guest-link-created", link);
    }
  } catch (err) {
    console.error("Failed to create guest link:", err);
  } finally {
    isSubmitting.value = false;
  }
}

function copyGuestUrl() {
  if (!generatedGuestUrl.value) return;
  if (
    navigator.clipboard &&
    typeof navigator.clipboard.writeText === "function"
  ) {
    navigator.clipboard.writeText(generatedGuestUrl.value);
  }
  isCopied.value = true;
  setTimeout(() => {
    isCopied.value = false;
  }, 2000);
}

function initiateRevoke(grant) {
  grantToRevoke.value = grant;
  revokeReason.value =
    "Delegation access expired or superseded by revised protocol duties.";
}

async function confirmRevoke() {
  if (!grantToRevoke.value || !revokeReason.value.trim()) return;

  const grant = grantToRevoke.value;
  const fileId = props.files[0]?.id;
  if (fileId) {
    await fileshareStore.revokeGrant(
      fileId,
      grant.id,
      revokeReason.value.trim()
    );
    emit("grant-revoked", { fileId, grantId: grant.id });
  }
  grantToRevoke.value = null;
  revokeReason.value = "";
}

function formatDate(dateStr) {
  if (!dateStr) return "N/A";
  try {
    const d = new Date(dateStr);
    return d.toLocaleString("en-US", {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return dateStr;
  }
}

// Click outside handler for autocomplete suggestions dropdown
function handleDocumentClick(e) {
  if (drawerRef.value && !e.target.closest(".autocomplete-wrapper")) {
    showUserSuggestions.value = false;
  }
}

onMounted(() => {
  document.addEventListener("click", handleDocumentClick);
});

onUnmounted(() => {
  document.removeEventListener("click", handleDocumentClick);
});
</script>

<style scoped>
.share-drawer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(2px);
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
}

.share-drawer {
  width: 480px;
  max-width: 95vw;
  background: #ffffff;
  height: 100%;
  display: flex;
  flex-direction: column;
  box-shadow: -4px 0 20px rgba(0, 0, 0, 0.15);
  animation: slideInRight 0.25s ease-out;
}

@keyframes slideInRight {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
  background-color: #f8fafc;
}

.header-info {
  display: flex;
  align-items: center;
  gap: 10px;
}

.share-header-icon {
  font-size: 1.4rem;
}

.drawer-title {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--primary);
}

.drawer-subtitle {
  margin: 2px 0 0 0;
  font-size: 0.78rem;
  color: var(--neutral-dark);
  max-width: 320px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.close-drawer-btn {
  background: none;
  border: none;
  font-size: 1.2rem;
  color: #64748b;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}
.close-drawer-btn:hover {
  background-color: #e2e8f0;
  color: #0f172a;
}

.tab-navigation {
  display: flex;
  border-bottom: 1px solid var(--border);
  background-color: #ffffff;
}

.tab-btn {
  flex: 1;
  padding: 12px 8px;
  border: none;
  background: none;
  font-size: 0.82rem;
  font-weight: 600;
  color: #64748b;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  transition: all 0.2s;
}

.tab-btn:hover {
  color: var(--accent);
  background-color: #f8fafc;
}

.tab-btn.active {
  color: var(--accent);
  border-bottom-color: var(--accent);
  background-color: #ffffff;
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.tab-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-group {
  margin-bottom: 14px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-label {
  font-size: 0.82rem;
  font-weight: 600;
  color: #334155;
}

.form-label.required::after {
  content: " *";
  color: #ef4444;
}

.form-control {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.88rem;
  font-family: inherit;
  box-sizing: border-box;
}

.form-control:focus {
  outline: none;
  border-color: var(--accent);
  box-shadow: 0 0 0 2px rgba(2, 132, 199, 0.15);
}

.autocomplete-wrapper {
  position: relative;
}

.suggestions-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  background: #ffffff;
  border: 1px solid var(--border);
  border-radius: 6px;
  box-shadow: 0 6px 16px rgba(0, 0, 0, 0.1);
  z-index: 50;
  max-height: 200px;
  overflow-y: auto;
  margin-top: 4px;
}

.suggestion-item {
  padding: 8px 12px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  cursor: pointer;
  font-size: 0.82rem;
  border-bottom: 1px solid #f1f5f9;
}

.suggestion-item:hover {
  background-color: #eff6ff;
}

.sugg-label {
  font-weight: 600;
  color: var(--primary);
}

.sugg-type {
  font-size: 0.72rem;
  color: #64748b;
  background-color: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.82rem;
  color: #334155;
  cursor: pointer;
}

.expiry-input {
  margin-top: 6px;
}

.drawer-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 16px;
}

.success-alert {
  padding: 10px 12px;
  background-color: #f0fdf4;
  border: 1px solid #bbf7d0;
  color: #166534;
  font-size: 0.82rem;
  border-radius: 6px;
}

.guest-info-banner {
  padding: 10px 12px;
  background-color: #eff6ff;
  border-left: 3px solid var(--accent);
  color: #1e40af;
  font-size: 0.82rem;
  line-height: 1.4;
  border-radius: 4px;
}

.generated-link-card {
  padding: 16px;
  background-color: #f8fafc;
  border: 1px solid var(--border);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.link-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.expires-text {
  font-size: 0.78rem;
  color: #64748b;
}

.url-copy-box {
  display: flex;
  gap: 8px;
}

.url-input {
  font-family: monospace;
  font-size: 0.8rem;
  background-color: #ffffff;
}

.gxp-link-meta {
  margin: 0;
  font-size: 0.72rem;
  color: #64748b;
  font-style: italic;
}

/* Grants List */
.empty-grants-message {
  padding: 24px;
  text-align: center;
  color: #64748b;
  font-size: 0.85rem;
  font-style: italic;
}

.grants-list-container {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.grant-row-card {
  padding: 12px;
  background-color: #f8fafc;
  border: 1px solid var(--border);
  border-radius: 6px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
}

.grant-details {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.8rem;
}

.grant-header-line {
  display: flex;
  align-items: center;
  gap: 6px;
}

.grantee-name {
  font-weight: 700;
  color: var(--primary);
}

.grant-meta-line {
  color: #64748b;
  font-size: 0.75rem;
  display: flex;
  gap: 12px;
}

.grant-reason-line {
  color: #475569;
  font-size: 0.75rem;
}

.revoke-confirm-card {
  margin-top: 16px;
  padding: 16px;
  background-color: #fef2f2;
  border: 1px solid #fca5a5;
  border-radius: 6px;
}

.revoke-confirm-card h4 {
  margin: 0 0 6px 0;
  color: #dc2626;
  font-size: 0.9rem;
}

.revoke-confirm-card p {
  margin: 0 0 10px 0;
  font-size: 0.8rem;
  color: #991b1b;
}
</style>
