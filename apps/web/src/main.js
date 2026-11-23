// Cadence Clinical Vue SPA Bootstrap entrypoint
import { createApp } from "vue";
import { createPinia } from "pinia";
import { initHoverDetection, sanitizeReturnUrl } from "ui";
import App from "./App.vue";
import { installAuthorizedFetch } from "./api/authorization.js";
import { initKeycloakAuth } from "./auth/bootstrap.js";
import {
  consumeWebReturnUrl,
  peekWebReturnUrl,
} from "./auth/sessionCoordinator.js";
import { vKeyboardClick } from "./directives/keyboardClick";
import { i18n } from "./i18n/index.js";
import { router } from "./router";
import { useAuthStore } from "./stores/auth";
import { stateTrackingPlugin } from "./stores/plugins.js";
import { isDemoBuild } from "./utils/demoMode";

const app = createApp(App);
app.directive("keyboard-click", vKeyboardClick);
app.use(i18n);

const pinia = createPinia();
pinia.use(stateTrackingPlugin);
app.use(pinia);

const authStore = useAuthStore(pinia);

// Every gateway-bound fetch carries the Keycloak bearer token (ADR-2250).
installAuthorizedFetch(window);

// Dynamic Hover Pointer Capability Detection
initHoverDetection();

async function bootstrapApp() {
  // The MSW mock gateway runs only in a demo build, or when explicitly enabled for local UI work; it must never
  // answer in place of the real API unannounced, and in demo builds it must be ready before any view mounts (ADR-2329).
  if (
    import.meta.env.MODE === "demo" ||
    import.meta.env.VITE_DEMO_MODE === "true" ||
    import.meta.env.VITE_ENABLE_MSW === "true"
  ) {
    try {
      const { startMswWorker } = await import("./mocks/browser.js");
      await startMswWorker();
    } catch (err) {
      console.warn("[MSW] Failed to initialize worker:", err);
    }
  }

  // Sign-in is Keycloak only (authorization code + PKCE). The router is added
  // after the SSO check so the first navigation guard sees the real session.
  // A demo build has no identity provider: it signs in the offline demo identity
  // instead (ADR-2329, #4523).
  try {
    if (isDemoBuild()) {
      authStore.startDemoSession();
    } else {
      const { default: Keycloak } = await import("keycloak-js");
      await initKeycloakAuth({
        authStore,
        Keycloak,
        onSessionEnded: (ctx) => {
          const current = router.currentRoute.value;
          const returnTarget =
            sanitizeReturnUrl(current?.fullPath) ||
            ctx?.returnUrl ||
            peekWebReturnUrl();
          if (current.matched.some((record) => record.meta.requiresAuth)) {
            router.replace({
              path: "/login",
              query: { redirect: returnTarget || current.fullPath },
            });
          }
        },
      });
    }

    if (!isDemoBuild()) {
      // keycloak-js strips the OIDC callback parameters from the URL during
      // init. Start the router on the cleaned URL, not the snapshot it took at
      // import, so the one-time authorization code never returns to history.
      const base = (import.meta.env.BASE_URL || "/").replace(/\/$/, "");
      const { pathname, search, hash } = window.location;
      const path = pathname.startsWith(base)
        ? pathname.slice(base.length)
        : pathname;
      const currentTarget = `${path || "/"}${search}${hash}`;
      const savedReturnUrl =
        authStore.isAuthenticated &&
        (!path || path === "/" || path === "/login") &&
        !search
          ? consumeWebReturnUrl()
          : null;
      await router.replace(savedReturnUrl || currentTarget);
    }
  } finally {
    app.use(router);
    await router.isReady();
    app.mount("#app");
  }
}

bootstrapApp();
