import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { setKeycloakClient } from "../src/auth/keycloak.js";
import { useAuthStore, PERSONA_PRESETS } from "../src/stores/auth";
import { useSignatureStore } from "../src/stores/signatures";
import { useNotificationsStore } from "../src/stores/notifications";
import { useEtmfStore } from "../src/stores/etmf";
import { apiClient } from "../src/api/apiClient";
import { notificationsService } from "../src/api/notifications";
import { etmfService } from "../src/api/etmf";

/** A signed-in keycloak-js double carrying the given realm roles. */
function keycloakWith(roles, overrides = {}) {
  return {
    authenticated: true,
    token: "access-token-1",
    idToken: "id-token-1",
    tokenParsed: {
      sub: "user-uuid-9999",
      preferred_username: "testuser",
      email: "testuser@example.com",
      given_name: "Test",
      family_name: "User",
      exp: 2000000000,
      realm_access: { roles },
    },
    login: vi.fn().mockResolvedValue(undefined),
    logout: vi.fn().mockResolvedValue(undefined),
    updateToken: vi.fn().mockResolvedValue(false),
    ...overrides,
  };
}

describe("useAuthStore - Keycloak & OIDC Authentication Store", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    setKeycloakClient(null);
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  describe("Signed-out state", () => {
    it("starts with no identity, roles, token or persona", () => {
      const authStore = useAuthStore();

      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.token).toBeNull();
      expect(authStore.identity).toBeNull();
      expect(authStore.userId).toBeNull();
      expect(authStore.normalizedRoles).toEqual([]);
      expect(authStore.currentPersona).toBeNull();
      expect(authStore.availablePersonas).toEqual([]);
      expect(authStore.isDemoMode).toBe(false);
    });

    it("ignores and purges tokens persisted in web storage by older builds", () => {
      const legacy = JSON.stringify({
        isAuthenticated: true,
        accessToken: "stolen-or-stale-token",
        rawRoles: ["Sponsor Admin"],
        isDemoMode: true,
      });
      window.sessionStorage.setItem("cadence_auth", legacy);
      window.localStorage.setItem("cadence_auth", legacy);

      const authStore = useAuthStore();

      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.token).toBeNull();
      expect(authStore.normalizedRoles).toEqual([]);
      expect(window.sessionStorage.getItem("cadence_auth")).toBeNull();
      expect(window.localStorage.getItem("cadence_auth")).toBeNull();
    });

    it("never writes tokens to web storage", () => {
      const authStore = useAuthStore();
      authStore.setAuth(keycloakWith(["CRA"]));

      expect(window.sessionStorage.length).toBe(0);
      expect(window.localStorage.getItem("cadence_auth")).toBeNull();
    });
  });

  describe("OIDC Integration and Keycloak Claims Normalization", () => {
    it("sets state and parses claims correctly from a standard keycloak token object", () => {
      const authStore = useAuthStore();

      authStore.setAuth(
        keycloakWith([
          "Sponsor Admin",
          "CRA",
          "Data Manager",
          "Site Investigator",
        ])
      );

      expect(authStore.isAuthenticated).toBe(true);
      expect(authStore.token).toBe("access-token-1");
      expect(authStore.idToken).toBe("id-token-1");
      expect(authStore.tokenExpiresAt).toBe(2000000000 * 1000);
      expect(authStore.identity).toEqual({
        username: "testuser",
        email: "testuser@example.com",
        firstName: "Test",
        lastName: "User",
        id: "user-uuid-9999",
      });
      expect(authStore.userId).toBe("user-uuid-9999");
      expect(authStore.normalizedRoles).toEqual([
        "sponsor_admin",
        "cra",
        "data_manager",
        "site_investigator",
      ]);
      expect(authStore.currentPersona).toBe("super_admin");
    });

    it("normalizes roles with extra whitespaces, mixed cases, and special characters", () => {
      const authStore = useAuthStore();

      authStore.setAuth(
        keycloakWith([
          "  Sponsor Admin  ",
          "cRa",
          "quality_manager",
          "QA-LEAD",
          "quality oversight",
          "Subject",
          "Auditor",
        ])
      );

      expect(authStore.normalizedRoles).toEqual([
        "sponsor_admin",
        "cra",
        "quality_manager",
        "qa_lead",
        "quality_oversight",
        "subject",
        "auditor",
      ]);
    });

    it("normalizes Sponsor Designer, study_designer, and designer roles into the canonical sponsor_designer token", () => {
      const authStore = useAuthStore();

      authStore.setAuth(
        keycloakWith(["Sponsor Designer", "study_designer", "designer"])
      );

      expect(authStore.normalizedRoles).toEqual([
        "sponsor_designer",
        "sponsor_designer",
        "sponsor_designer",
      ]);
      expect(authStore.currentPersona).toBe("sponsor_designer");
    });

    it("resets authentication state when setAuth is called with a falsy or unauthenticated object", () => {
      const authStore = useAuthStore();
      authStore.setAuth(keycloakWith(["CRA"]));
      expect(authStore.isAuthenticated).toBe(true);

      authStore.setAuth(null);
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.token).toBeNull();
      expect(authStore.identity).toBeNull();
      expect(authStore.normalizedRoles).toEqual([]);
      expect(authStore.currentPersona).toBeNull();

      authStore.setAuth(keycloakWith(["CRA"], { authenticated: false }));
      expect(authStore.isAuthenticated).toBe(false);
    });

    it("treats an authenticated flag without an access token as signed out", () => {
      const authStore = useAuthStore();
      authStore.setAuth(keycloakWith(["Sponsor Admin"], { token: undefined }));

      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.normalizedRoles).toEqual([]);
    });
  });

  describe("Keycloak sign-in, sign-out and token refresh", () => {
    it("refuses to sign in when Keycloak could not be initialised", async () => {
      const authStore = useAuthStore();

      await expect(authStore.login()).rejects.toThrow(
        "Keycloak sign-in is unavailable"
      );
      expect(authStore.isAuthenticated).toBe(false);
    });

    it("delegates sign-in to the Keycloak redirect", async () => {
      const keycloak = keycloakWith([]);
      setKeycloakClient(keycloak);
      const authStore = useAuthStore();

      await authStore.login({ redirectUri: "http://localhost:3000/x" });

      expect(keycloak.login).toHaveBeenCalledWith({
        redirectUri: "http://localhost:3000/x",
      });
    });

    it("clears the session and ends the Keycloak SSO session on logout", async () => {
      const keycloak = keycloakWith(["Auditor"]);
      setKeycloakClient(keycloak);
      const authStore = useAuthStore();
      authStore.setAuth(keycloak);

      await authStore.logout({ redirectUri: "http://localhost:3000/login" });

      expect(keycloak.logout).toHaveBeenCalledWith({
        redirectUri: "http://localhost:3000/login",
      });
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.token).toBeNull();
      expect(authStore.currentPersona).toBeNull();
    });

    it("clears local state on logout even without a Keycloak client", async () => {
      const authStore = useAuthStore();
      authStore.setAuth(keycloakWith(["Auditor"]));

      await authStore.logout();

      expect(authStore.isAuthenticated).toBe(false);
    });

    it("refreshes the access token through keycloak.updateToken", async () => {
      const keycloak = keycloakWith(["CRA"]);
      setKeycloakClient(keycloak);
      const authStore = useAuthStore();
      authStore.setAuth(keycloak);

      keycloak.updateToken.mockImplementation(async () => {
        keycloak.token = "access-token-2";
        return true;
      });

      await expect(authStore.refresh(30)).resolves.toBe(true);
      expect(keycloak.updateToken).toHaveBeenCalledWith(30);
      expect(authStore.token).toBe("access-token-2");
      await expect(authStore.ensureFreshToken()).resolves.toBe(
        "access-token-2"
      );
    });

    it("ends the session when the token can no longer be refreshed", async () => {
      const keycloak = keycloakWith(["CRA"]);
      setKeycloakClient(keycloak);
      const authStore = useAuthStore();
      authStore.setAuth(keycloak);
      keycloak.updateToken.mockRejectedValue(new Error("refresh expired"));

      await expect(authStore.refresh()).rejects.toThrow("refresh expired");
      expect(authStore.isAuthenticated).toBe(false);
      await expect(authStore.ensureFreshToken()).resolves.toBeNull();
    });

    it("returns no token when nobody is signed in", async () => {
      const authStore = useAuthStore();
      await expect(authStore.ensureFreshToken()).resolves.toBeNull();
      await expect(authStore.refresh()).resolves.toBe(false);
    });
  });

  describe("Workspace personas follow token roles", () => {
    it("offers only the personas the token's realm roles entitle", () => {
      const authStore = useAuthStore();
      authStore.setAuth(keycloakWith(["Site Investigator"]));

      expect(authStore.availablePersonas.map((p) => p.key)).toEqual([
        "site_crc",
      ]);
      expect(authStore.currentPersona).toBe("site_crc");
    });

    it("offers every persona to a Sponsor Admin", () => {
      const authStore = useAuthStore();
      authStore.setAuth(keycloakWith(["Sponsor Admin"]));

      expect(authStore.availablePersonas.map((p) => p.key)).toEqual(
        PERSONA_PRESETS.map((p) => p.key)
      );
    });

    it("switches the workspace view without changing roles", () => {
      const authStore = useAuthStore();
      authStore.setAuth(keycloakWith(["Sponsor Admin"]));

      expect(authStore.switchPersona("site_crc")).toBe(true);
      expect(authStore.currentPersona).toBe("site_crc");
      expect(authStore.normalizedRoles).toEqual(["sponsor_admin"]);
    });

    it("refuses a persona the token does not entitle and grants no roles", () => {
      const authStore = useAuthStore();
      authStore.setAuth(keycloakWith(["Site Investigator"]));

      expect(authStore.switchPersona("super_admin")).toBe(false);
      expect(authStore.switchPersona("invalid_hacked_persona_role")).toBe(
        false
      );
      expect(authStore.currentPersona).toBe("site_crc");
      expect(authStore.normalizedRoles).toEqual(["site_investigator"]);
      expect(authStore.hasRole("sponsor_admin")).toBe(false);
    });

    it("refuses every persona when signed out", () => {
      const authStore = useAuthStore();
      expect(authStore.switchPersona("super_admin")).toBe(false);
      expect(authStore.currentPersona).toBeNull();
    });
  });

  describe("Stores call the API instead of fabricating data", () => {
    it("submits batch signatures to the API and stores the server response", async () => {
      const response = { signature_id: "sig-1", signed_forms_count: 2 };
      const post = vi.spyOn(apiClient, "post").mockResolvedValue(response);
      const signatureStore = useSignatureStore();

      const res = await signatureStore.submitBatchSignature({
        studyId: "STUDY-01",
        subjectId: "SUBJ-01",
        formIds: ["FORM-01", "FORM-02"],
        password: "my-password", // pragma: allowlist secret
        meaning: "APPROVED",
      });

      expect(post).toHaveBeenCalledWith(
        "/api/v1/execution/signatures/batch-sign-off",
        expect.objectContaining({ target_ids: ["FORM-01", "FORM-02"] }),
        { changeReason: "APPROVED" }
      );
      expect(res).toEqual(response);
      expect(window.localStorage.getItem("lastSignatureResult")).toBeNull();
      post.mockRestore();
    });

    it("starts with no notifications and reports API failures", async () => {
      const failure = Object.assign(new Error("Unauthorized"), {
        status: 401,
      });
      const get = vi
        .spyOn(notificationsService, "getNotifications")
        .mockRejectedValue(failure);
      const notificationsStore = useNotificationsStore();

      expect(notificationsStore.notifications).toEqual([]);
      await notificationsStore.fetchNotifications();

      expect(get).toHaveBeenCalled();
      expect(notificationsStore.notifications).toEqual([]);
      expect(notificationsStore.errorStatus).toBe(401);
      get.mockRestore();
    });

    it("loads eTMF documents from the API only", async () => {
      const get = vi.spyOn(etmfService, "getDocuments").mockResolvedValue([]);
      const etmfStore = useEtmfStore();

      await etmfStore.fetchDocuments("01.01.01");

      expect(get).toHaveBeenCalled();
      expect(etmfStore.documentsList).toEqual([]);
      expect(window.localStorage.getItem("demo_documents")).toBeNull();
      get.mockRestore();
    });
  });

  describe("Demo build sign-in (ADR-2329)", () => {
    it("refuses demo sign-in outside a demo build", () => {
      const authStore = useAuthStore();

      expect(authStore.isDemoMode).toBe(false);
      expect(() => authStore.startDemoSession()).toThrow(
        "Demo sign-in is only available in a demo build."
      );
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.identity).toBeNull();
    });

    it("ignores a stored demo flag outside a demo build", () => {
      window.sessionStorage.setItem(
        "cadence_auth",
        JSON.stringify({ isAuthenticated: true, isDemoMode: true })
      );

      const authStore = useAuthStore();
      expect(authStore.isDemoMode).toBe(false);
      expect(authStore.isAuthenticated).toBe(false);
    });

    it("signs in the offline demo identity without Keycloak in a demo build", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      const authStore = useAuthStore();

      await authStore.login();

      expect(authStore.isDemoMode).toBe(true);
      expect(authStore.isAuthenticated).toBe(true);
      expect(authStore.token).toBeNull();
      expect(authStore.identity).toEqual({
        username: "fderuiter",
        email: "fderuiter@example.com", // deid-ignore
        firstName: "Frans",
        lastName: "de Ruiter",
        id: "fderuiter-id-12345",
      });
      expect(authStore.currentPersona).toBe("super_admin");

      await authStore.logout();
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.identity).toBeNull();
    });
  });

  describe("Demo build stores simulate the API locally (ADR-2329)", () => {
    let authStore;

    beforeEach(() => {
      // These local simulations exist only in a demo build (#4523).
      vi.stubEnv("VITE_DEMO_MODE", "true");
      authStore = useAuthStore();
      authStore.startDemoSession();
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.clear();
      }
    });

    describe("Signatures Store Simulation", () => {
      it("intercepts batch signature submission and persists to localStorage in demo mode", async () => {
        authStore.isDemoMode = true;
        const signatureStore = useSignatureStore();

        const payload = {
          studyId: "STUDY-01",
          subjectId: "SUBJ-01",
          formIds: ["FORM-01", "Form-02"],
          password: "my-password", // pragma: allowlist secret
          meaning: "APPROVED",
        };

        const res = await signatureStore.submitBatchSignature(payload);

        expect(res.signature_id).toBeDefined();
        expect(res.signature_id.startsWith("mock-sig-")).toBe(true);
        expect(res.signed_forms_count).toBe(2);
        expect(signatureStore.lastSignatureResult).toEqual(res);

        // Verify localStorage persistence
        const stored = window.localStorage.getItem("lastSignatureResult");
        expect(stored).toBeDefined();
        expect(JSON.parse(stored).signature_id).toBe(res.signature_id);
      });
    });

    describe("Notifications Store Simulation", () => {
      it("fetches static alert templates locally in demo mode and applies filters", async () => {
        authStore.isDemoMode = true;
        const notificationsStore = useNotificationsStore();

        // Clear filters
        notificationsStore.filters.category = "";
        notificationsStore.filters.priority = "";
        notificationsStore.filters.status = "";

        const list = await notificationsStore.fetchNotifications();
        expect(list.length).toBe(3); // our 3 default templates
        expect(list[0].id).toBe("notif-001");

        // Filter by category
        notificationsStore.filters.category = "ALERTS";
        const filtered = await notificationsStore.fetchNotifications();
        expect(filtered.length).toBe(1);
        expect(filtered[0].category).toBe("ALERTS");
      });

      it("supports interactive acknowledge and resolve actions locally in demo mode", async () => {
        authStore.isDemoMode = true;
        const notificationsStore = useNotificationsStore();

        // Initialize notifications
        await notificationsStore.fetchNotifications();

        // Acknowledge notification 1
        const ackRes = await notificationsStore.acknowledge(
          "notif-001",
          "Acknowledge clinical warning"
        );
        expect(ackRes.status).toBe("ACKNOWLEDGED");
        expect(ackRes.version_index).toBe(2);
        expect(ackRes.reason_for_change).toBe("Acknowledge clinical warning");

        // Resolve notification 2
        const resRes = await notificationsStore.resolve(
          "notif-002",
          "Checked subject CRF and resolved"
        );
        expect(resRes.status).toBe("RESOLVED");
        expect(resRes.version_index).toBe(2);
        expect(resRes.reason_for_change).toBe(
          "Checked subject CRF and resolved"
        );

        // Check localStorage persistence (demo notifications are scoped per persona)
        expect(authStore.currentPersona).toBe("super_admin");
        expect(window.localStorage.getItem("demo_notifications")).toBeNull();
        const stored = window.localStorage.getItem(
          "demo_notifications_super_admin"
        );
        expect(stored).not.toBeNull();
        expect(stored).toBeDefined();
        const storedList = JSON.parse(stored);
        expect(storedList[0].status).toBe("ACKNOWLEDGED");
        expect(storedList[1].status).toBe("RESOLVED");
      });
    });

    describe("eTMF Store Simulation", () => {
      it("fetches pre-seeded eTMF documents locally in demo mode", async () => {
        authStore.isDemoMode = true;
        const etmfStore = useEtmfStore();

        await etmfStore.fetchDocuments("01.01.01");
        expect(etmfStore.documentsList.length).toBe(1);
        expect(etmfStore.documentsList[0].filename).toBe(
          "protocol_v1_draft.pdf"
        );
      });

      it("uploads and files new documents into the tree locally in demo mode", async () => {
        authStore.isDemoMode = true;
        const etmfStore = useEtmfStore();
        etmfStore.selectedArtifactId = "01.01.01";

        const fileData = {
          study_id: "STUDY-USDM-001",
          artifact_type: "Clinical Trial Protocol",
          filename: "uploaded_doc.pdf",
          content: "some base64 content",
          mime_type: "application/pdf",
          artifact_code: "01.01.01",
          zone: 1,
          section: "01.01",
          reason_for_change: "New protocol version uploaded during demo",
        };

        const res = await etmfStore.uploadDocument(fileData);
        expect(res.status).toBe("success");
        expect(res.document_id).toBeDefined();

        // Should be fetched locally after upload
        expect(
          etmfStore.documentsList.some(
            (doc) => doc.filename === "uploaded_doc.pdf"
          )
        ).toBe(true);

        // Verify localStorage persistence
        const stored = window.localStorage.getItem("demo_documents");
        expect(stored).toBeDefined();
        const storedList = JSON.parse(stored);
        expect(
          storedList.some((doc) => doc.filename === "uploaded_doc.pdf")
        ).toBe(true);
      });
    });

    describe("Clean Reset Simulation", () => {
      it("successfully clears local storage and returns the stores back to their static baseline states", async () => {
        authStore.isDemoMode = true;
        const signatureStore = useSignatureStore();
        const notificationsStore = useNotificationsStore();
        const etmfStore = useEtmfStore();

        // Mutate all stores
        await signatureStore.submitBatchSignature({
          studyId: "S",
          subjectId: "SU",
          formIds: ["F"],
          password: "P",
          meaning: "M", // pragma: allowlist secret
        });
        await notificationsStore.fetchNotifications();
        await notificationsStore.acknowledge("notif-001", "R1");
        await etmfStore.uploadDocument({
          filename: "uploaded_doc.pdf",
          artifact_code: "01.01.01",
        });

        // Verify they are mutated/stored in localStorage
        expect(
          window.localStorage.getItem("lastSignatureResult")
        ).not.toBeNull();
        // Demo notifications are persisted under the active persona's key
        expect(authStore.currentPersona).toBe("super_admin");
        expect(
          window.localStorage.getItem("demo_notifications_super_admin")
        ).not.toBeNull();
        expect(window.localStorage.getItem("demo_documents")).not.toBeNull();

        // Trigger reset Demo Storage on each store
        signatureStore.resetDemoStorage();
        notificationsStore.resetDemoStorage();
        etmfStore.resetDemoStorage();

        // Verify localStorage is cleared
        expect(window.localStorage.getItem("lastSignatureResult")).toBeNull();
        expect(
          window.localStorage.getItem("demo_notifications_super_admin")
        ).toBeNull();
        expect(window.localStorage.getItem("demo_documents")).toBeNull();

        // Verify in-memory state is back to pristine default state
        expect(signatureStore.lastSignatureResult).toBeNull();
        expect(notificationsStore.notifications[0].status).toBe("OPEN"); // originally "OPEN", ack changed it to "ACKNOWLEDGED"
        expect(
          etmfStore.documentsList.some(
            (doc) => doc.filename === "uploaded_doc.pdf"
          )
        ).toBe(false);
      });
    });
  });

  describe("Demo Mode Build Configurations", () => {
    it("should permit login and logout fallbacks in demo build mode even if PROD is true", async () => {
      const authStore = useAuthStore();

      vi.stubEnv("PROD", true);
      vi.stubEnv("MODE", "demo");

      // This should succeed because of the check excluding MODE === "demo" from isProduction
      await authStore.login();
      expect(authStore.isAuthenticated).toBe(true);
      expect(authStore.rawRoles).toContain("Sponsor Designer");

      await authStore.logout();
      expect(authStore.isAuthenticated).toBe(false);
    });
  });
});
