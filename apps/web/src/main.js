import { createApp } from "vue";
import { createPinia } from "pinia";
import App from "./App.vue";
import { router } from "./router";
import Keycloak from "keycloak-js";
import { useAuthStore } from "./stores/auth";

const app = createApp(App);
const pinia = createPinia();

app.use(pinia);
app.use(router);

// Graceful Keycloak / OIDC setup
const keycloakConfig = {
  url: "http://localhost:8080/",
  realm: "cadence",
  clientId: "cadence-web",
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
    await fetch(
      "http://localhost:8080/realms/cadence/.well-known/openid-configuration",
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

checkKeycloakReachable().then((reachable) => {
  if (reachable) {
    keycloak
      .init({
        onLoad: "check-sso",
        silentCheckSsoRedirectUri:
          window.location.origin + "/silent-check-sso.html",
        pkceMethod: "S256",
      })
      .then((authenticated) => {
        console.log(`Keycloak initialized. Authenticated: ${authenticated}`);
        authStore.setAuth(keycloak);
        app.mount("#app");
      })
      .catch((err) => {
        console.warn(
          "Keycloak initialization failed (fallback to offline/demo):",
          err
        );
        authStore.isDemoMode = true;
        app.mount("#app");
      });
  } else {
    console.log(
      "Keycloak server is offline. Mounting app in offline/demo mode."
    );
    authStore.isDemoMode = true;
    app.mount("#app");
  }
});
