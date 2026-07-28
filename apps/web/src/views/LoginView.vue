<template>
  <div
    id="section-login"
    class="dashboard-section active"
    style="max-width: 600px; margin: 40px auto"
  >
    <div class="section-header" style="text-align: center">
      <h2>System Authentication</h2>
      <p>Identity Gateway and Security Assertion Portal</p>
    </div>

    <div class="card">
      <div
        class="card-title"
        style="justify-content: center; font-size: 1.25rem"
      >
        <span>Sign In to Cadence Clinical Sandbox</span>
      </div>

      <div style="padding: 20px; text-align: center">
        <div
          v-if="authStore.isAuthenticated"
          class="auth-success-alert"
          style="margin-bottom: 24px; color: var(--success); font-weight: 600"
        >
          <p>
            ✓ You are securely authenticated as
            <strong>{{ authStore.identity?.username }}</strong>
          </p>
          <p style="font-size: 0.9rem; color: #64748b; font-weight: normal">
            Assigned Roles: {{ authStore.normalizedRoles.join(", ") }}
          </p>
          <button
            class="btn btn-primary"
            style="margin-top: 16px"
            @click="goToLanding"
          >
            Go to My Workspace
          </button>
        </div>

        <div v-else>
          <p style="margin-bottom: 24px; color: #475569; font-size: 0.95rem">
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
              style="width: 100%; padding: 12px; font-weight: 600"
              @click="loginWithKeycloak"
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
              "
              @click="loginWithDemo"
            >
              💻 Access via Offline Demo Mode
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { useAuthStore } from "../stores/auth";
import { useRouter, useRoute } from "vue-router";

const authStore = useAuthStore();
const router = useRouter();
const route = useRoute();

async function loginWithKeycloak() {
  try {
    // Disable demo mode and trigger Keycloak login redirect
    authStore.isDemoMode = false;
    const redirectUrl = route.query.redirect || "/";
    await authStore.login({
      redirectUri: window.location.origin + router.resolve(redirectUrl).href,
    });
  } catch (err) {
    console.error("Keycloak login initiation failed:", err);
  }
}

async function loginWithDemo() {
  authStore.isDemoMode = true;
  await authStore.login();
  goToLanding();
}

function goToLanding() {
  const redirectUrl = route.query.redirect || "/";
  router.push(redirectUrl);
}
</script>
