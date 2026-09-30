import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  DEV_KEYCLOAK_DEFAULTS,
  KeycloakConfigError,
  getKeycloakClient,
  resolveKeycloakConfig,
  setKeycloakClient,
} from "../src/auth/keycloak.js";
import { initKeycloakAuth } from "../src/auth/bootstrap.js";
import {
  installAuthorizedFetch,
  isGatewayRequest,
} from "../src/api/authorization.js";
import { apiClient } from "../src/api/apiClient";
import { useAuthStore } from "../src/stores/auth";

const PROD_ENV = {
  PROD: true,
  VITE_KEYCLOAK_URL: "https://id.example.org/",
  VITE_KEYCLOAK_REALM: "cadence",
  VITE_KEYCLOAK_CLIENT_ID: "cadence-web",
};

/** keycloak-js constructor double; `init` resolves to `authenticated`. */
function fakeKeycloakClass({ authenticated = true, initError = null } = {}) {
  const instances = [];
  class FakeKeycloak {
    constructor(config) {
      this.config = config;
      this.authenticated = false;
      instances.push(this);
    }
    async init(options) {
      this.initOptions = options;
      if (initError) throw initError;
      this.authenticated = authenticated;
      if (authenticated) {
        this.token = "access-token-1";
        this.tokenParsed = {
          sub: "sub-1",
          preferred_username: "designer_demo",
          realm_access: { roles: ["Sponsor Designer"] },
        };
      }
      return authenticated;
    }
    updateToken = vi.fn().mockResolvedValue(false);
  }
  return { FakeKeycloak, instances };
}

describe("Keycloak sign-in configuration", () => {
  it("uses local development defaults outside production builds", () => {
    expect(resolveKeycloakConfig({ PROD: false })).toEqual(
      DEV_KEYCLOAK_DEFAULTS
    );
  });

  it("uses the Vite env values when set", () => {
    expect(resolveKeycloakConfig(PROD_ENV)).toEqual({
      url: "https://id.example.org/",
      realm: "cadence",
      clientId: "cadence-web",
    });
  });

  it("fails closed in production builds when a value is missing", () => {
    const env = { ...PROD_ENV, VITE_KEYCLOAK_URL: "" };
    expect(() => resolveKeycloakConfig(env)).toThrow(KeycloakConfigError);
    expect(() => resolveKeycloakConfig(env)).toThrow("VITE_KEYCLOAK_URL");
  });
});

describe("Keycloak bootstrap", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    setKeycloakClient(null);
  });

  it("restores an SSO session with check-sso and PKCE S256", async () => {
    const { FakeKeycloak, instances } = fakeKeycloakClass();
    const authStore = useAuthStore();

    const signedIn = await initKeycloakAuth({
      authStore,
      Keycloak: FakeKeycloak,
      env: { PROD: false },
    });

    expect(signedIn).toBe(true);
    expect(instances[0].config).toEqual(DEV_KEYCLOAK_DEFAULTS);
    expect(instances[0].initOptions).toMatchObject({
      onLoad: "check-sso",
      pkceMethod: "S256",
    });
    expect(instances[0].initOptions.silentCheckSsoRedirectUri).toMatch(
      /silent-check-sso\.html$/
    );
    expect(getKeycloakClient()).toBe(instances[0]);
    expect(authStore.isAuthenticated).toBe(true);
    expect(authStore.identity.username).toBe("designer_demo");
    expect(authStore.normalizedRoles).toEqual(["sponsor_designer"]);
  });

  it("leaves the user signed out when there is no SSO session", async () => {
    const { FakeKeycloak } = fakeKeycloakClass({ authenticated: false });
    const authStore = useAuthStore();

    const signedIn = await initKeycloakAuth({
      authStore,
      Keycloak: FakeKeycloak,
      env: { PROD: false },
    });

    expect(signedIn).toBe(false);
    expect(authStore.isAuthenticated).toBe(false);
    expect(authStore.authError).toBeNull();
  });

  it("reports an unavailable identity provider without signing anyone in", async () => {
    const { FakeKeycloak } = fakeKeycloakClass({
      initError: new Error("network"),
    });
    const authStore = useAuthStore();
    const error = vi.spyOn(console, "error").mockImplementation(() => {});

    const signedIn = await initKeycloakAuth({
      authStore,
      Keycloak: FakeKeycloak,
      env: { PROD: false },
    });

    expect(signedIn).toBe(false);
    expect(authStore.isAuthenticated).toBe(false);
    expect(authStore.authError).toMatch(/sign-in service is unavailable/);
    expect(getKeycloakClient()).toBeNull();
    error.mockRestore();
  });

  it("reports missing production configuration", async () => {
    const { FakeKeycloak, instances } = fakeKeycloakClass();
    const authStore = useAuthStore();

    const signedIn = await initKeycloakAuth({
      authStore,
      Keycloak: FakeKeycloak,
      env: { PROD: true },
    });

    expect(signedIn).toBe(false);
    expect(instances).toHaveLength(0);
    expect(authStore.authError).toMatch(/VITE_KEYCLOAK_URL/);
  });

  it("ends the session when an expired token cannot be refreshed", async () => {
    const { FakeKeycloak, instances } = fakeKeycloakClass();
    const authStore = useAuthStore();
    const onSessionEnded = vi.fn();
    await initKeycloakAuth({
      authStore,
      Keycloak: FakeKeycloak,
      env: { PROD: false },
      onSessionEnded,
    });
    instances[0].updateToken.mockRejectedValue(new Error("expired"));

    instances[0].onTokenExpired();
    await vi.waitFor(() => expect(onSessionEnded).toHaveBeenCalled());

    expect(authStore.isAuthenticated).toBe(false);
  });
});

describe("Bearer token attachment", () => {
  const location = new URL("http://localhost:3000/cadence-clinical/mdr");

  beforeEach(() => {
    setActivePinia(createPinia());
    setKeycloakClient(null);
  });

  it("recognises gateway requests only", () => {
    expect(isGatewayRequest("/api/v1/ctms/sites", location)).toBe(true);
    expect(isGatewayRequest("http://localhost:8000/api/v1/x", location)).toBe(
      true
    );
    expect(isGatewayRequest("https://cdn.example.org/api/v1/x", location)).toBe(
      false
    );
    expect(isGatewayRequest("/cadence-clinical/logo.svg", location)).toBe(
      false
    );
  });

  describe("installAuthorizedFetch", () => {
    let target;
    let calls;

    beforeEach(() => {
      calls = [];
      target = {
        fetch: vi.fn(async (input, init) => {
          calls.push({ input, init });
          return new Response("{}");
        }),
      };
      installAuthorizedFetch(target);
    });

    afterEach(() => {
      vi.restoreAllMocks();
    });

    it("adds the bearer token to gateway requests", async () => {
      useAuthStore().setAuth({
        authenticated: true,
        token: "access-token-1",
        tokenParsed: { realm_access: { roles: [] } },
      });

      await target.fetch("http://localhost:8000/api/v1/execution/labs");

      expect(calls[0].init.headers.get("Authorization")).toBe(
        "Bearer access-token-1"
      );
    });

    it("never sends the token to other origins", async () => {
      useAuthStore().setAuth({
        authenticated: true,
        token: "access-token-1",
        tokenParsed: { realm_access: { roles: [] } },
      });

      await target.fetch("https://cdn.example.org/api/v1/x");

      expect(calls[0].init).toEqual({});
    });

    it("keeps an Authorization header the caller already set", async () => {
      useAuthStore().setAuth({
        authenticated: true,
        token: "access-token-1",
        tokenParsed: { realm_access: { roles: [] } },
      });

      await target.fetch("http://localhost:8000/api/v1/x", {
        headers: { Authorization: "Bearer caller-token" },
      });

      expect(calls[0].init.headers.get("Authorization")).toBe(
        "Bearer caller-token"
      );
    });

    it("sends no Authorization header when nobody is signed in", async () => {
      await target.fetch("http://localhost:8000/api/v1/x");

      expect(calls[0].init.headers.has("Authorization")).toBe(false);
    });

    it("is installed only once", () => {
      const wrapped = target.fetch;
      installAuthorizedFetch(target);
      expect(target.fetch).toBe(wrapped);
    });
  });

  it("apiClient refreshes the token before attaching it", async () => {
    const keycloak = {
      authenticated: true,
      token: "old-token",
      tokenParsed: { realm_access: { roles: [] } },
      updateToken: vi.fn(async () => {
        keycloak.token = "fresh-token";
        return true;
      }),
    };
    setKeycloakClient(keycloak);
    useAuthStore().setAuth(keycloak);
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response("{}", { status: 200 }));

    await apiClient.get("/api/v1/notifications");

    expect(keycloak.updateToken).toHaveBeenCalledWith(30);
    expect(fetchMock.mock.calls[0][1].headers.Authorization).toBe(
      "Bearer fresh-token"
    );
    fetchMock.mockRestore();
  });
});
