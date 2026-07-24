import { createApp } from "vue";
import { createPinia } from "pinia";
import App from "./App.vue";
import { router } from "./router";
import Keycloak from "keycloak-js";

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

// Initialize Keycloak gracefully (in offline / demo mode, we handle failure to connect)
keycloak
  .init({
    onLoad: "check-sso",
    silentCheckSsoRedirectUri: window.location.origin + "/silent-check-sso.html",
    pkceMethod: "S256",
  })
  .then((authenticated) => {
    console.log(`Keycloak initialized. Authenticated: ${authenticated}`);
    app.mount("#app");
  })
  .catch((err) => {
    console.warn("Keycloak initialization failed or timed out (offline/demo fallback):", err);
    // Proceed mounting the application even if Keycloak server is not running,
    // ensuring the standalone web demo remains fully functional for sandbox testing.
    app.mount("#app");
  });
