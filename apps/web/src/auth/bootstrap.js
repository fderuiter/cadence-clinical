import { TOKEN_MIN_VALIDITY_SECONDS } from "../api/authorization.js";
import { resolveAssetUrl } from "../utils/url";
import {
  KEYCLOAK_BUILD_ENV,
  resolveKeycloakConfig,
  setKeycloakClient,
} from "./keycloak.js";

/**
 * Creates the Keycloak client, restores an existing SSO session (check-sso,
 * authorization code + PKCE S256) and keeps the auth store in step with it.
 *
 * Never throws: when Keycloak is misconfigured or unreachable the store gets
 * an `authError`, nobody is signed in, and the sign-in page shows the error.
 *
 * @param {object} deps
 * @param {ReturnType<import("../stores/auth").useAuthStore>} deps.authStore
 * @param {new (config: object) => any} deps.Keycloak - keycloak-js constructor
 * @param {Record<string, unknown>} [deps.env] - build env (`KEYCLOAK_BUILD_ENV`)
 * @param {() => void} [deps.onSessionEnded] - called when the session ends or
 *   can no longer be refreshed, so the app can route to sign-in.
 * @returns {Promise<boolean>} whether a user is signed in
 */
export async function initKeycloakAuth({
  authStore,
  Keycloak,
  env = KEYCLOAK_BUILD_ENV,
  onSessionEnded = () => {},
}) {
  let config;
  try {
    config = resolveKeycloakConfig(env);
  } catch (err) {
    authStore.authError = err.message;
    authStore.clearAuth();
    return false;
  }

  const keycloak = new Keycloak(config);
  setKeycloakClient(keycloak);

  const endSession = () => {
    authStore.clearAuth();
    onSessionEnded();
  };

  keycloak.onAuthRefreshSuccess = () => authStore.setAuth(keycloak);
  keycloak.onAuthLogout = endSession;
  keycloak.onTokenExpired = () => {
    authStore.refresh(TOKEN_MIN_VALIDITY_SECONDS).catch(endSession);
  };

  try {
    const authenticated = await keycloak.init({
      onLoad: "check-sso",
      pkceMethod: "S256",
      silentCheckSsoRedirectUri: resolveAssetUrl("silent-check-sso.html"),
    });
    authStore.setAuth(authenticated ? keycloak : null);
    return Boolean(authenticated);
  } catch (err) {
    console.error("Keycloak initialisation failed:", err);
    setKeycloakClient(null);
    authStore.authError =
      "The sign-in service is unavailable. Try again later or contact your administrator.";
    authStore.clearAuth();
    return false;
  }
}
