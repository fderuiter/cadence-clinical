/**
 * Keycloak (OIDC) client configuration and the single shared client instance.
 *
 * The web app is a public OIDC client: it signs in with the authorization code
 * flow plus PKCE (S256) and holds no client secret. Tokens live only in the
 * keycloak-js instance's memory; they are never written to web storage.
 * See docs/adr/2026-09-29-keycloak-only-web-sign-in.md and ADR-2250.
 */

/** Local development defaults, used only by `vite` dev and test builds. */
export const DEV_KEYCLOAK_DEFAULTS = Object.freeze({
  url: import.meta.env.PROD ? "" : "http://localhost:8080/",
  realm: "cadence",
  clientId: "cadence-web",
});

export class KeycloakConfigError extends Error {
  constructor(missing) {
    super(
      `Keycloak sign-in is not configured. Set ${missing.join(", ")} at build time.`
    );
    this.name = "KeycloakConfigError";
    this.missing = missing;
  }
}

/**
 * The build-time values this module reads. Each key is referenced explicitly
 * so Vite inlines only these, never the whole `import.meta.env` object.
 */
export const KEYCLOAK_BUILD_ENV = Object.freeze({
  PROD: import.meta.env.PROD,
  VITE_KEYCLOAK_URL: import.meta.env.VITE_KEYCLOAK_URL,
  VITE_KEYCLOAK_REALM: import.meta.env.VITE_KEYCLOAK_REALM,
  VITE_KEYCLOAK_CLIENT_ID: import.meta.env.VITE_KEYCLOAK_CLIENT_ID,
});

/**
 * Resolves the Keycloak client settings from Vite env vars.
 *
 * Production builds must set all three variables: there is no production
 * default, so a missing value fails closed instead of pointing at localhost.
 *
 * @param {Record<string, unknown>} env - `KEYCLOAK_BUILD_ENV` or a test stand-in.
 * @returns {{ url: string, realm: string, clientId: string }}
 */
export function resolveKeycloakConfig(env = KEYCLOAK_BUILD_ENV) {
  const values = {
    url: env?.VITE_KEYCLOAK_URL,
    realm: env?.VITE_KEYCLOAK_REALM,
    clientId: env?.VITE_KEYCLOAK_CLIENT_ID,
  };

  if (env?.PROD) {
    const names = {
      url: "VITE_KEYCLOAK_URL",
      realm: "VITE_KEYCLOAK_REALM",
      clientId: "VITE_KEYCLOAK_CLIENT_ID",
    };
    const missing = Object.keys(names)
      .filter((key) => !values[key])
      .map((key) => names[key]);
    if (missing.length > 0) {
      throw new KeycloakConfigError(missing);
    }
    return values;
  }

  return {
    url: values.url || DEV_KEYCLOAK_DEFAULTS.url,
    realm: values.realm || DEV_KEYCLOAK_DEFAULTS.realm,
    clientId: values.clientId || DEV_KEYCLOAK_DEFAULTS.clientId,
  };
}

let keycloakClient = null;

/** Registers the keycloak-js instance created at bootstrap (or a test double). */
export function setKeycloakClient(client) {
  keycloakClient = client || null;
}

/** Returns the registered keycloak-js instance, or null before bootstrap. */
export function getKeycloakClient() {
  return keycloakClient;
}
