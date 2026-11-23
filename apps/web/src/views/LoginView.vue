<template>
  <div
    id="section-login"
    class="dashboard-section active"
    style="
      max-width: 600px;
      margin: 40px auto;
      padding: 0 12px;
      box-sizing: border-box;
      font-family: sans-serif;
    "
  >
    <div
      id="login-prerelease-banner"
      role="status"
      style="
        background: var(--semantic-color-warning-bg-soft);
        border: 1px solid var(--semantic-color-warning-border-strong);
        color: var(--semantic-color-warning-dark);
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
      <p style="color: var(--semantic-color-text-secondary)">
        Access is governed by 21 CFR Part 11. Sign in with your Cadence account
        or register a self-service evaluation workspace.
      </p>
    </div>

    <div
      class="card"
      style="
        border: 1px solid var(--semantic-color-border);
        border-radius: 8px;
        box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1);
        background: var(--semantic-color-surface);
        overflow: hidden;
      "
    >
      <div
        class="card-title"
        style="
          justify-content: center;
          font-size: 1.25rem;
          padding: 16px;
          border-bottom: 1px solid var(--semantic-color-border);
          background-color: var(--semantic-color-surface-muted);
          text-align: center;
          font-weight: 600;
        "
      >
        <span>Sign in to Cadence Clinical</span>
      </div>

      <div style="padding: clamp(16px, 5vw, 24px); text-align: center">
        <!-- Signed in -->
        <div v-if="authStore.isAuthenticated" class="auth-success-alert">
          <p
            style="
              font-size: 1.1rem;
              margin-bottom: 8px;
              color: var(--semantic-color-success);
              font-weight: 600;
              overflow-wrap: anywhere;
            "
          >
            You are signed in as
            <strong>{{ authStore.identity?.username }}</strong>
          </p>
          <p
            style="
              font-size: 0.9rem;
              color: var(--semantic-color-text-secondary);
              margin-bottom: 20px;
              overflow-wrap: anywhere;
            "
          >
            Roles: {{ formattedRoles }}
          </p>
          <div
            style="
              display: flex;
              flex-wrap: wrap;
              gap: 12px;
              justify-content: center;
            "
          >
            <button
              class="btn btn-primary"
              style="
                padding: 10px 24px;
                font-weight: 600;
                cursor: pointer;
                max-width: 100%;
                white-space: normal;
              "
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
              style="
                padding: 10px 24px;
                cursor: pointer;
                max-width: 100%;
                white-space: normal;
              "
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
              color: var(--semantic-color-error-dark);
              background: var(--semantic-color-error-bg);
              border: 1px solid var(--semantic-color-error-border);
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
              {{ isRedirecting ? "Redirecting…" : "Sign in with your account" }}
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
              border-top: 1px solid var(--semantic-color-border);
              font-size: 0.9rem;
            "
          >
            <router-link
              id="link-login-plans"
              to="/plans"
              style="
                color: var(--semantic-color-interactive-primary);
                font-weight: 600;
                text-decoration: none;
              "
            >
              Compare Individual, Select, and Complete plans &rarr;
            </router-link>
          </div>
        </div>

        <DemoResetButton variant="login" />
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { sanitizeReturnUrl } from "ui";
import {
  consumeWebReturnUrl,
  peekWebReturnUrl,
  saveWebReturnUrl,
} from "../auth/sessionCoordinator.js";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/productCatalog.js";
import { useAuthStore } from "../stores/auth";
import { formatRoleList } from "../utils/clinicalLabels";
import DemoResetButton from "./demo/DemoResetButton.vue";

const authStore = useAuthStore();
const router = useRouter();
const route = useRoute();

const isRedirecting = ref(false);
const signInError = ref("");
const errorMessage = computed(() => authStore.authError || signInError.value);
const formattedRoles = computed(() =>
  formatRoleList(authStore.normalizedRoles, "None assigned")
);

/** In-app path to return to after sign-in; never an external URL. */
function redirectTarget() {
  const fromQuery = sanitizeReturnUrl(
    typeof route.query.redirect === "string" ? route.query.redirect : ""
  );
  if (fromQuery) {
    return fromQuery;
  }
  const fromSession = peekWebReturnUrl();
  if (fromSession) {
    return fromSession;
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
    const target = redirectTarget();
    saveWebReturnUrl(target);
    const href = router.resolve(target).href;
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
  const target = redirectTarget();
  consumeWebReturnUrl();
  if (authStore.needsTrialOnboarding) {
    router.push("/onboarding/trial");
    return;
  }
  router.push(target);
}
</script>
