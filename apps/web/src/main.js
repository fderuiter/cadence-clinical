// Cadence Clinical Vue SPA Bootstrap entrypoint
import { createApp } from "vue";
import { createPinia } from "pinia";
import App from "./App.vue";
import { router } from "./router";
import Keycloak from "keycloak-js";
import { DEMO_IDENTITY, DEMO_ROLES, useAuthStore } from "./stores/auth";
import { isDemoBuild } from "./utils/demoMode";
import { initHoverDetection } from "ui";
import { resolveAssetUrl } from "./utils/url";
import { vKeyboardClick } from "./directives/keyboardClick";

import { stateTrackingPlugin } from "./stores/plugins.js";

const app = createApp(App);
app.directive("keyboard-click", vKeyboardClick);

const pinia = createPinia();
pinia.use(stateTrackingPlugin);

app.use(pinia);
app.use(router);

// Graceful Keycloak / OIDC setup
const keycloakUrl =
  import.meta.env.VITE_KEYCLOAK_URL || "http://localhost:8080/";
const keycloakRealm = import.meta.env.VITE_KEYCLOAK_REALM || "cadence";
const keycloakClientId =
  import.meta.env.VITE_KEYCLOAK_CLIENT_ID || "cadence-web";

const keycloakConfig = {
  url: keycloakUrl,
  realm: keycloakRealm,
  clientId: keycloakClientId,
};

const keycloak = new Keycloak(keycloakConfig);
window.keycloakInstance = keycloak;

const authStore = useAuthStore(pinia);

// Quick check if Keycloak is reachable to prevent long-hanging initialization
const checkKeycloakReachable = async () => {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 1000);
  try {
    // Check OIDC discovery endpoint
    const urlClean = keycloakUrl.replace(/\/$/, "");
    await fetch(
      `${urlClean}/realms/${keycloakRealm}/.well-known/openid-configuration`,
      {
        signal: controller.signal,
        mode: "no-cors",
      }
    );
    clearTimeout(timeoutId);
    return true;
  } catch {
    clearTimeout(timeoutId);
    return false;
  }
};

// Mounts without a session when Keycloak cannot be used, so the login page can say so. Only a demo build
// falls back to the offline demo identity (#4523).
const mountWithoutIdentityProvider = (reason, err) => {
  console.error(
    `Keycloak unavailable (${reason}); signing in is disabled.`,
    err ?? ""
  );
  window.keycloakInstance = null;
  authStore.isDemoMode = false;
  authStore.setAuth(null);
  app.mount("#app");
};

if (isDemoBuild()) {
  console.log(
    "Demo build: seeding the offline demo identity; API calls may be served by the MSW mock worker."
  );
  authStore.isDemoMode = true;
  authStore.isAuthenticated = true;
  authStore.user = { ...DEMO_IDENTITY };
  authStore.rawRoles = [...DEMO_ROLES];
  authStore.persist();
  app.mount("#app");
} else {
  checkKeycloakReachable().then((reachable) => {
    if (!reachable) {
      mountWithoutIdentityProvider("server unreachable");
      return;
    }
    keycloak
      .init({
        onLoad: "check-sso",
        silentCheckSsoRedirectUri: resolveAssetUrl("silent-check-sso.html"),
        pkceMethod: "S256",
      })
      .then((authenticated) => {
        console.log(`Keycloak initialized. Authenticated: ${authenticated}`);
        authStore.setAuth(keycloak);
        app.mount("#app");
      })
      .catch((err) =>
        mountWithoutIdentityProvider("initialization failed", err)
      );
  });
}

// The MSW mock gateway runs only in a demo build, or when explicitly enabled for local UI work; it must never
// answer in place of the real API unannounced.
if (
  typeof window !== "undefined" &&
  (isDemoBuild() || import.meta.env.VITE_ENABLE_MSW === "true")
) {
  import("./mocks/browser.js")
    .then(({ startMswWorker }) => startMswWorker())
    .catch((err) => console.warn("[MSW] Failed to initialize worker:", err));
}

// Dynamic Hover Pointer Capability Detection
initHoverDetection();
