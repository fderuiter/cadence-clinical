// Cadence Clinical Vue SPA Bootstrap entrypoint
import { createApp } from "vue";
import { createPinia } from "pinia";
import App from "./App.vue";
import { router } from "./router";
import Keycloak from "keycloak-js";
import { useAuthStore } from "./stores/auth";
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

if (import.meta.env.MODE === "demo") {
  console.log(
    "Running in dedicated public demo mode. Seeding mock clinical credentials and mounting instantly."
  );
  authStore.isDemoMode = true;
  authStore.isAuthenticated = true;
  authStore.user = {
    username: "fderuiter",
    email: "fderuiter@example.com", // deid-ignore
    firstName: "Frans",
    lastName: "de Ruiter",
    id: "fderuiter-id-12345",
  };
  authStore.rawRoles = [
    "Sponsor Admin",
    "Sponsor Designer",
    "CRA",
    "Data Manager",
    "Site Investigator",
    "Auditor",
  ];
  authStore.persist();
  app.mount("#app");
} else {
  checkKeycloakReachable().then((reachable) => {
    const isProduction =
      (import.meta.env.PROD || import.meta.env.MODE === "production") &&
      import.meta.env.MODE !== "demo";
    if (reachable) {
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
        .catch((err) => {
          console.error("Keycloak initialization failed:", err);
          if (isProduction) {
            authStore.isDemoMode = false;
            throw new Error(
              "Production lockdown: Keycloak OIDC initialization failed. Refusing to run in offline demo mode."
            );
          } else {
            authStore.isDemoMode = true;
            app.mount("#app");
          }
        });
    } else {
      if (isProduction) {
        authStore.isDemoMode = false;
        console.error(
          "Production lockdown: Keycloak server is offline. Refusing to start in offline demo mode."
        );
        throw new Error("Production lockdown: Keycloak server is offline.");
      } else {
        console.log(
          "Keycloak server is offline. Mounting app in offline/demo mode."
        );
        authStore.isDemoMode = true;
        app.mount("#app");
      }
    }
  });
}

// Dynamic Hover Pointer Capability Detection
initHoverDetection();
