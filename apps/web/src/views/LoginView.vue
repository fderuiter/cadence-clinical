<template>
  <div
    id="section-login"
    class="dashboard-section active"
    style="max-width: 600px; margin: 40px auto; font-family: sans-serif"
  >
    <div
      id="login-prerelease-banner"
      role="status"
      style="
        background: #fffbeb;
        border: 1px solid #f59e0b;
        color: #92400e;
        border-radius: 8px;
        padding: 10px 14px;
        font-size: 0.86rem;
        line-height: 1.45;
        margin-bottom: 20px;
      "
    >
      <strong>Pre-release notice:</strong>
      {{ PRE_RELEASE_ENVIRONMENT_NOTICE }}
    </div>

    <div class="section-header" style="text-align: center; margin-bottom: 24px">
      <h2>Sign in or start a trial workspace</h2>
      <p style="color: #64748b">
        Access is governed by 21 CFR Part 11. Sign in with your Cadence account
        or register a self-service evaluation workspace.
      </p>
    </div>

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
        <span>Sign in to Cadence Clinical</span>
      </div>

      <div style="padding: 24px; text-align: center">
        <!-- Signed in -->
        <div v-if="authStore.isAuthenticated" class="auth-success-alert">
          <p
            style="
              font-size: 1.1rem;
              margin-bottom: 8px;
              color: #16a34a;
              font-weight: 600;
            "
          >
            You are signed in as
            <strong>{{ authStore.identity?.username }}</strong>
          </p>
          <p style="font-size: 0.9rem; color: #64748b; margin-bottom: 20px">
            Roles: {{ authStore.normalizedRoles.join(", ") || "none" }}
          </p>
          <div style="display: flex; gap: 12px; justify-content: center">
            <button
              class="btn btn-primary"
              style="padding: 10px 24px; font-weight: 600; cursor: pointer"
              @click="goToLanding"
            >
              {{
                authStore.needsTrialOnboarding
                  ? "Complete Trial Setup"
                  : "Go to My Workspace"
              }}
            </button>
            <button
              class="btn btn-secondary btn-logout"
              style="padding: 10px 24px; cursor: pointer"
              @click="signOut"
            >
              Sign out
            </button>
          </div>
        </div>

        <!-- Signed out -->
        <div v-else>
          <p
            v-if="errorMessage"
            class="login-error"
            role="alert"
            style="
              color: #b91c1c;
              background: #fee2e2;
              border: 1px solid #fca5a5;
              border-radius: 4px;
              padding: 10px;
              margin-bottom: 16px;
            "
          >
            {{ errorMessage }}
          </p>
          <button
            v-if="authStore.authError"
            class="btn btn-secondary btn-login-retry"
            style="padding: 12px 24px; font-weight: 600; cursor: pointer"
            @click="retry"
          >
            Try again
          </button>
          <button
            v-else-if="authStore.isDemoMode"
            class="btn btn-primary btn-login-demo"
            style="
              width: 100%;
              max-width: 320px;
              padding: 12px;
              font-weight: 600;
              cursor: pointer;
              border-radius: 4px;
            "
            @click="signIn"
          >
            Enter the demo (offline sample data)
          </button>
          <div
            v-else
            style="
              display: flex;
              flex-direction: column;
              align-items: center;
              gap: 12px;
            "
          >
            <button
              class="btn btn-primary btn-login-keycloak"
              style="
                width: 100%;
                max-width: 320px;
                padding: 12px;
                font-weight: 600;
                cursor: pointer;
                border-radius: 4px;
              "
              :disabled="isRedirecting"
              @click="signIn"
            >
              {{ isRedirecting ? "Redirecting…" : "Sign in with Keycloak" }}
            </button>
            <button
              id="btn-login-register"
              type="button"
              class="btn btn-secondary btn-register-keycloak"
              style="
                width: 100%;
                max-width: 320px;
                padding: 11px 12px;
                font-weight: 600;
                cursor: pointer;
                border-radius: 4px;
              "
              :disabled="isRedirecting"
              @click="startRegistration"
            >
              Create trial account
            </button>
          </div>

          <div
            style="
              margin-top: 18px;
              padding-top: 16px;
              border-top: 1px solid #e2e8f0;
              font-size: 0.9rem;
            "
          >
            <router-link
              id="link-login-plans"
              to="/plans"
              style="color: #1d4ed8; font-weight: 600; text-decoration: none"
            >
              Compare Individual, Select, and Complete plans &rarr;
            </router-link>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/productCatalog.js";
import { useAuthStore } from "../stores/auth";

const authStore = useAuthStore();
const router = useRouter();
const route = useRoute();

const isRedirecting = ref(false);
const signInError = ref("");
const errorMessage = computed(() => authStore.authError || signInError.value);

/** In-app path to return to after sign-in; never an external URL. */
function redirectTarget() {
  const target = route.query.redirect;
  if (
    typeof target === "string" &&
    target.startsWith("/") &&
    !target.startsWith("//") &&
    !target.startsWith("/\\")
  ) {
    return target;
  }
  return "/";
}

async function signIn() {
  signInError.value = "";
  if (authStore.isDemoMode) {
    // Demo build only (ADR-2329): no identity provider, just the offline demo identity.
    await authStore.login();
    goToLanding();
    return;
  }
  isRedirecting.value = true;
  try {
    const href = router.resolve(redirectTarget()).href;
    await authStore.login({
      redirectUri: new URL(href, window.location.origin).toString(),
    });
  } catch (err) {
    signInError.value = err?.message || "Sign-in failed.";
    isRedirecting.value = false;
  }
}

async function startRegistration() {
  signInError.value = "";
  isRedirecting.value = true;
  try {
    const href = router.resolve("/onboarding/trial").href;
    await authStore.register({
      redirectUri: new URL(href, window.location.origin).toString(),
    });
  } catch (err) {
    signInError.value = err?.message || "Registration failed.";
    isRedirecting.value = false;
  }
}

async function signOut() {
  await authStore.logout({
    redirectUri: new URL(
      router.resolve("/login").href,
      window.location.origin
    ).toString(),
  });
}

function retry() {
  window.location.reload();
}

function goToLanding() {
  if (authStore.needsTrialOnboarding) {
    router.push("/onboarding/trial");
    return;
  }
  router.push(redirectTarget());
}
</script>
