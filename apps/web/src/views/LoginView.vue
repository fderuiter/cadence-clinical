<template>
  <div
    id="section-login"
    class="dashboard-section active"
    style="max-width: 600px; margin: 40px auto; font-family: sans-serif"
  >
    <div
      class="section-header"
      style="text-align: center; margin-bottom: 24px"
    >
      <h2>System Authentication</h2>
      <p style="color: #64748b">
        Identity Gateway and Security Assertion Portal
      </p>
    </div>

    <!-- MAIN CONTAINER CARD -->
    <div
      class="card"
      style="
        border: 1px solid #e2e8f0;
        border-radius: 8px;
        box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1);
        background: #fff;
        overflow: hidden;
      "
    >
      <div
        class="card-title"
        style="
          justify-content: center;
          font-size: 1.25rem;
          padding: 16px;
          border-bottom: 1px solid #e2e8f0;
          background-color: #f8fafc;
          text-align: center;
          font-weight: 600;
        "
      >
        <span>Sign In to Cadence Clinical Sandbox</span>
      </div>

      <div style="padding: 24px">
        <!-- 1. ALREADY AUTHENTICATED STATE -->
        <div
          v-if="authStore.isAuthenticated"
          class="auth-success-alert"
          style="text-align: center; color: #16a34a; font-weight: 600"
        >
          <p style="font-size: 1.1rem; margin-bottom: 8px">
            ✓ You are securely authenticated as
            <strong>{{ authStore.identity?.username }}</strong>
          </p>
          <p
            style="
              font-size: 0.9rem;
              color: #64748b;
              font-weight: normal;
              margin-bottom: 20px;
            "
          >
            Assigned Roles: {{ authStore.normalizedRoles.join(", ") }}
          </p>
          <button
            class="btn btn-primary"
            style="padding: 10px 24px; font-weight: 600; cursor: pointer"
            @click="goToLanding"
          >
            Go to My Workspace
          </button>
        </div>

        <!-- 2. PIN SETUP FLOW -->
        <div
          v-else-if="showPinSetup"
          style="max-width: 400px; margin: 0 auto"
        >
          <h3 style="margin-bottom: 12px; text-align: center; color: #1e293b">
            Set Up Personalized Recovery PIN
          </h3>
          <p
            style="
              font-size: 0.875rem;
              color: #475569;
              margin-bottom: 20px;
              text-align: center;
            "
          >
            This 4-to-6 digit PIN wraps your cryptographic escrow key
            client-side, securing offline submission queues for multi-user
            tablets.
          </p>

          <div style="margin-bottom: 16px">
            <label
              style="
                display: block;
                font-size: 0.875rem;
                font-weight: 600;
                margin-bottom: 6px;
                color: #334155;
              "
            >
              Create 4-to-6 Digit PIN:
            </label>
            <input
              v-model="pinInput"
              type="password"
              maxlength="6"
              placeholder="••••"
              class="form-control"
              style="
                width: 100%;
                padding: 10px;
                border: 1px solid #cbd5e1;
                border-radius: 4px;
                box-sizing: border-box;
                text-align: center;
                font-size: 1.25rem;
                letter-spacing: 0.25em;
              "
              @input="pinInput = pinInput.replace(/\D/g, '')"
            >
          </div>

          <div style="margin-bottom: 20px">
            <label
              style="
                display: block;
                font-size: 0.875rem;
                font-weight: 600;
                margin-bottom: 6px;
                color: #334155;
              "
            >
              Confirm Recovery PIN:
            </label>
            <input
              v-model="pinConfirmInput"
              type="password"
              maxlength="6"
              placeholder="••••"
              class="form-control"
              style="
                width: 100%;
                padding: 10px;
                border: 1px solid #cbd5e1;
                border-radius: 4px;
                box-sizing: border-box;
                text-align: center;
                font-size: 1.25rem;
                letter-spacing: 0.25em;
              "
              @input="pinConfirmInput = pinConfirmInput.replace(/\D/g, '')"
            >
          </div>

          <div
            v-if="pinSetupError"
            style="
              color: #dc2626;
              font-size: 0.875rem;
              margin-bottom: 16px;
              text-align: center;
              font-weight: 600;
            "
          >
            {{ pinSetupError }}
          </div>

          <div style="display: flex; gap: 12px">
            <button
              class="btn btn-secondary"
              style="
                flex: 1;
                padding: 10px;
                cursor: pointer;
                background: #f1f5f9;
                border: 1px solid #cbd5e1;
                color: #475569;
                border-radius: 4px;
              "
              @click="cancelPinFlow"
            >
              Cancel
            </button>
            <button
              class="btn btn-primary"
              style="
                flex: 1;
                padding: 10px;
                cursor: pointer;
                font-weight: 600;
                border-radius: 4px;
              "
              @click="submitPinSetup"
            >
              Confirm & Save
            </button>
          </div>
        </div>

        <!-- 3. PIN UNLOCK / INTERACTIVE CHALLENGE -->
        <div
          v-else-if="showPinUnlock"
          style="max-width: 400px; margin: 0 auto"
        >
          <h3 style="margin-bottom: 12px; text-align: center; color: #1e293b">
            Unlock Session Key Escrow
          </h3>
          <p
            style="
              font-size: 0.875rem;
              color: #475569;
              margin-bottom: 20px;
              text-align: center;
            "
          >
            A dormant offline queue is protected by a PIN-wrapped key. Enter PIN
            to decrypt and process.
          </p>

          <div style="margin-bottom: 16px">
            <label
              style="
                display: block;
                font-size: 0.875rem;
                font-weight: 600;
                margin-bottom: 6px;
                color: #334155;
              "
            >
              Select Local User Profile:
            </label>
            <select
              v-model="selectedUserToUnlock"
              class="form-control"
              style="
                width: 100%;
                padding: 10px;
                border: 1px solid #cbd5e1;
                border-radius: 4px;
                box-sizing: border-box;
              "
              @change="checkUserLockStatus"
            >
              <option
                v-for="user in existingUsers"
                :key="user"
                :value="user"
              >
                User: {{ user }}
              </option>
            </select>
          </div>

          <div style="margin-bottom: 20px">
            <label
              style="
                display: block;
                font-size: 0.875rem;
                font-weight: 600;
                margin-bottom: 6px;
                color: #334155;
              "
            >
              Enter 4-to-6 Digit PIN:
            </label>
            <input
              v-model="unlockPin"
              type="password"
              maxlength="6"
              placeholder="••••"
              :disabled="isUserLocked"
              class="form-control"
              style="
                width: 100%;
                padding: 10px;
                border: 1px solid #cbd5e1;
                border-radius: 4px;
                box-sizing: border-box;
                text-align: center;
                font-size: 1.25rem;
                letter-spacing: 0.25em;
              "
              @input="unlockPin = unlockPin.replace(/\D/g, '')"
            >
          </div>

          <div
            v-if="unlockError"
            style="
              color: #dc2626;
              font-size: 0.875rem;
              margin-bottom: 16px;
              text-align: center;
              font-weight: 600;
            "
          >
            {{ unlockError }}
          </div>

          <div style="display: flex; flex-direction: column; gap: 10px">
            <button
              class="btn btn-primary"
              :disabled="isUserLocked || !unlockPin"
              style="
                width: 100%;
                padding: 12px;
                font-weight: 600;
                cursor: pointer;
                border-radius: 4px;
              "
              @click="submitPinUnlock"
            >
              Unlock Queue & Sync
            </button>

            <button
              class="btn btn-secondary"
              style="
                width: 100%;
                padding: 10px;
                cursor: pointer;
                background: #f1f5f9;
                border: 1px solid #cbd5e1;
                color: #475569;
                border-radius: 4px;
              "
              @click="cancelPinFlow"
            >
              Back to Main Login
            </button>

            <button
              v-if="existingUsers.length > 0"
              class="btn btn-danger"
              style="
                width: 100%;
                padding: 10px;
                cursor: pointer;
                background: #fee2e2;
                border: 1px solid #fca5a5;
                color: #b91c1c;
                border-radius: 4px;
                margin-top: 10px;
              "
              @click="purgeUserEscrow"
            >
              Purge User Escrow Key (Discard Queue)
            </button>
          </div>
        </div>

        <!-- 4. DEFAULT LOGIN PATH SELECTOR -->
        <div v-else>
          <p
            style="
              margin-bottom: 24px;
              color: #475569;
              font-size: 0.95rem;
              text-align: center;
            "
          >
            Access to this clinical repository is governed by 21 CFR Part 11 and
            GxP requirements. Please select your authentication path below.
          </p>

          <div
            style="
              display: flex;
              flex-direction: column;
              gap: 16px;
              max-width: 320px;
              margin: 0 auto;
            "
          >
            <!-- Keycloak authentication -->
            <button
              class="btn btn-primary btn-login-keycloak"
              style="
                width: 100%;
                padding: 12px;
                font-weight: 600;
                cursor: pointer;
                border-radius: 4px;
              "
              @click="initiateLoginFlow('keycloak')"
            >
              🔐 Authenticate via Keycloak
            </button>

            <!-- Offline Demo mode fallback -->
            <button
              class="btn btn-secondary btn-login-demo"
              style="
                width: 100%;
                padding: 12px;
                font-weight: 600;
                background-color: #f1f5f9;
                border: 1px solid #cbd5e1;
                color: #334155;
                cursor: pointer;
                border-radius: 4px;
              "
              @click="initiateLoginFlow('demo')"
            >
              💻 Access via Offline Demo Mode
            </button>

            <!-- Unlock existing session (if detected) -->
            <button
              v-if="existingUsers.length > 0"
              class="btn btn-info"
              style="
                width: 100%;
                padding: 12px;
                font-weight: 600;
                background-color: #ecfdf5;
                border: 1px solid #a7f3d0;
                color: #047857;
                cursor: pointer;
                border-radius: 4px;
              "
              @click="showPinUnlock = true"
            >
              🔑 Restore Dormant Queue ({{ existingUsers.length }} Cached)
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from "vue";
import { useAuthStore } from "../stores/auth";
import { useRouter, useRoute } from "vue-router";
import { offlineAuthManager } from "../utils/offlineAuth";

const authStore = useAuthStore();
const router = useRouter();
const route = useRoute();

// Flow state
const showPinSetup = ref(false);
const showPinUnlock = ref(false);
const pendingLoginType = ref("");

// Setup variables
const pinInput = ref("");
const pinConfirmInput = ref("");
const pinSetupError = ref("");

// Unlock variables
const existingUsers = ref([]);
const selectedUserToUnlock = ref("");
const unlockPin = ref("");
const unlockError = ref("");
const isUserLocked = ref(false);

onMounted(async () => {
  await scanKeys();
  // Listen for the interactive PIN challenge event triggered by queue processing
  window.addEventListener("pin-challenge-required", () => {
    showPinUnlock.value = true;
  });
});

async function scanKeys() {
  try {
    const ids = await offlineAuthManager.getStoredUserIds();
    existingUsers.value = ids;
    if (ids.length > 0) {
      selectedUserToUnlock.value = ids[0];
      await checkUserLockStatus();
    }
  } catch (err) {
    console.error("Failed to scan IndexedDB session keys:", err);
  }
}

async function checkUserLockStatus() {
  if (!selectedUserToUnlock.value) return;
  const metadata = await offlineAuthManager.getSessionMetadata(
    selectedUserToUnlock.value
  );
  if (metadata) {
    isUserLocked.value = metadata.locked;
    if (metadata.locked) {
      unlockError.value = "Key recovery locked. Too many failed attempts.";
    } else {
      unlockError.value = "";
    }
  } else {
    isUserLocked.value = false;
    unlockError.value = "";
  }
}

function initiateLoginFlow(type) {
  pendingLoginType.value = type;
  showPinSetup.value = true;
  pinSetupError.value = "";
  pinInput.value = "";
  pinConfirmInput.value = "";
}

function cancelPinFlow() {
  showPinSetup.value = false;
  showPinUnlock.value = false;
  pinSetupError.value = "";
  unlockError.value = "";
}

async function submitPinSetup() {
  if (!pinInput.value || pinInput.value.length < 4) {
    pinSetupError.value = "PIN must be between 4 and 6 digits.";
    return;
  }
  if (pinInput.value !== pinConfirmInput.value) {
    pinSetupError.value = "PIN inputs do not match.";
    return;
  }

  try {
    if (pendingLoginType.value === "keycloak") {
      authStore.isDemoMode = false;
      const redirectUrl = route.query.redirect || "/";
      await authStore.login({
        redirectUri: window.location.origin + router.resolve(redirectUrl).href,
      });
      // In a real Keycloak flow redirect happens, but if we bypass or return:
      const session = {
        userId: authStore.user?.id || "keycloak-user-" + Date.now(),
        userRoles: authStore.normalizedRoles,
        offlineToken: authStore.accessToken || "token-k-" + Date.now(),
        createdAt: new Date().toISOString(),
        maxOfflineHours: 72,
      };
      await offlineAuthManager.storeEncryptedSession(pinInput.value, session);
    } else {
      authStore.isDemoMode = true;
      await authStore.login();
      const identity = authStore.identity || {
        id: "demo-user-id",
        username: "fderuiter",
      };
      const session = {
        userId: identity.id,
        userRoles: authStore.normalizedRoles,
        offlineToken: "demo-token-" + Date.now(),
        createdAt: new Date().toISOString(),
        maxOfflineHours: 72,
      };
      await offlineAuthManager.storeEncryptedSession(pinInput.value, session);
      goToLanding();
    }
  } catch (err) {
    pinSetupError.value = err.message;
  }
}

async function submitPinUnlock() {
  if (!unlockPin.value) return;
  try {
    const session = await offlineAuthManager.unlockOfflineSession(
      unlockPin.value,
      selectedUserToUnlock.value
    );

    // Decrypt succeeded, set auth state in store
    authStore.isAuthenticated = true;
    authStore.isDemoMode = true;
    authStore.user = {
      id: session.userId,
      username: session.userId,
      email: session.userId + "@example.com",
    };
    authStore.rawRoles = session.userRoles;
    authStore.persist();

    // Reset UI
    unlockPin.value = "";
    unlockError.value = "";
    showPinUnlock.value = false;

    // Trigger flush queue to upload dormant items
    if (window.syncEngine) {
      window.syncEngine.flushQueue();
    }

    goToLanding();
  } catch {
    // Refresh locks/attempts status
    await checkUserLockStatus();
    if (!isUserLocked.value) {
      unlockError.value = "Incorrect PIN. Please try again.";
    }
  }
}

async function purgeUserEscrow() {
  if (!selectedUserToUnlock.value) return;
  if (
    confirm(
      `Are you sure you want to completely delete the offline key escrow and sync queue for ${selectedUserToUnlock.value}? This cannot be undone.`
    )
  ) {
    await offlineAuthManager.clearOfflineSession(selectedUserToUnlock.value);
    await scanKeys();
    showPinUnlock.value = existingUsers.value.length > 0;
  }
}

function goToLanding() {
  const redirectUrl = route.query.redirect || "/";
  router.push(redirectUrl);
}
</script>
