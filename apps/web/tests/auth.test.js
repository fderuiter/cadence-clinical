import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth";
import { useSignatureStore } from "../src/stores/signatures";
import { useNotificationsStore } from "../src/stores/notifications";
import { useEtmfStore } from "../src/stores/etmf";

describe("useAuthStore - Keycloak & OIDC Authentication Store", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    if (typeof window !== "undefined") {
      delete window.keycloakInstance;
      window.localStorage.clear();
      window.sessionStorage.clear();
    }
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  describe("Demo mode is opt-in (#4523)", () => {
    it("starts outside demo mode, with no identity or roles, unless the build is a demo build", () => {
      const authStore = useAuthStore();

      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.isDemoMode).toBe(false);
      expect(authStore.token).toBeNull();
      expect(authStore.identity).toBeNull();
      expect(authStore.normalizedRoles).toEqual([]);
    });

    it("ignores a stored demo flag outside a demo build", () => {
      window.sessionStorage.setItem(
        "cadence_auth",
        JSON.stringify({ isAuthenticated: false, isDemoMode: true })
      );

      expect(useAuthStore().isDemoMode).toBe(false);
    });

    it("refuses offline sign-in outside a demo build", async () => {
      const authStore = useAuthStore();

      await expect(authStore.login()).rejects.toThrow(
        "Sign-in is unavailable: the identity provider could not be reached."
      );
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.isDemoMode).toBe(false);
    });

    it("starts in demo mode with the demo identity and roles in a demo build", () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      const authStore = useAuthStore();

      expect(authStore.isDemoMode).toBe(true);
      expect(authStore.identity).toEqual({
        username: "fderuiter",
        email: "fderuiter@example.com", // deid-ignore
        firstName: "Frans",
        lastName: "de Ruiter",
        id: "fderuiter-id-12345",
      });
      expect(authStore.normalizedRoles).toEqual([
        "sponsor_admin",
        "sponsor_designer",
        "data_manager",
        "site_investigator",
        "crc",
        "cra",
        "monitor",
        "auditor",
      ]);
    });

    it("allows offline login and logout in a demo build", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      const authStore = useAuthStore();

      await authStore.login();
      expect(authStore.isAuthenticated).toBe(true);
      expect(authStore.isDemoMode).toBe(true);

      await authStore.logout();
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.isDemoMode).toBe(true);
    });
  });

  describe("OIDC Integration and Keycloak Claims Normalization", () => {
    it("sets state and parses claims correctly from a standard keycloak token object", () => {
      const authStore = useAuthStore();

      const mockKeycloak = {
        authenticated: true,
        token: "mock-access-token-xyz-123",
        idToken: "mock-id-token-abc",
        refreshToken: "mock-refresh-token-pqr",
        tokenParsed: {
          sub: "user-uuid-9999",
          preferred_username: "testuser",
          email: "testuser@example.com",
          given_name: "Test",
          family_name: "User",
          realm_access: {
            roles: [
              "Sponsor Admin",
              "CRA",
              "Data Manager",
              "Site Investigator",
            ],
          },
        },
      };

      authStore.setAuth(mockKeycloak);

      expect(authStore.isAuthenticated).toBe(true);
      expect(authStore.isDemoMode).toBe(false);
      expect(authStore.token).toBe("mock-access-token-xyz-123");
      expect(authStore.idToken).toBe("mock-id-token-abc");
      expect(authStore.refreshToken).toBe("mock-refresh-token-pqr");

      // Verify identity parsing
      expect(authStore.identity).toEqual({
        username: "testuser",
        email: "testuser@example.com",
        firstName: "Test",
        lastName: "User",
        id: "user-uuid-9999",
      });

      // Verify exact role normalization mapping
      expect(authStore.normalizedRoles).toEqual([
        "sponsor_admin",
        "cra",
        "data_manager",
        "site_investigator",
      ]);
    });

    it("normalizes roles with extra whitespaces, mixed cases, and special characters", () => {
      const authStore = useAuthStore();

      const mockKeycloak = {
        authenticated: true,
        tokenParsed: {
          realm_access: {
            roles: [
              "  Sponsor Admin  ",
              "cRa",
              "quality_manager",
              "QA-LEAD",
              "quality oversight",
              "Subject",
              "Auditor",
            ],
          },
        },
      };

      authStore.setAuth(mockKeycloak);

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

      const mockKeycloak = {
        authenticated: true,
        tokenParsed: {
          realm_access: {
            roles: ["Sponsor Designer", "study_designer", "designer"],
          },
        },
      };

      authStore.setAuth(mockKeycloak);

      expect(authStore.normalizedRoles).toEqual([
        "sponsor_designer",
        "sponsor_designer",
        "sponsor_designer",
      ]);
    });

    it("resets authentication state when setAuth is called with a falsy or unauthenticated object", () => {
      const authStore = useAuthStore();

      // First set authenticated
      authStore.setAuth({
        authenticated: true,
        token: "token",
        tokenParsed: {
          realm_access: { roles: ["CRA"] },
        },
      });
      expect(authStore.isAuthenticated).toBe(true);

      // Now clear auth
      authStore.setAuth(null);
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.token).toBeNull();
      expect(authStore.identity).toBeNull();
      expect(authStore.normalizedRoles).toEqual([]);
    });
  });

  describe("Stores Demo Mode Local Simulation tests", () => {
    let authStore;

    beforeEach(() => {
      // These local simulations exist only in a demo build (#4523).
      vi.stubEnv("VITE_DEMO_MODE", "true");
      authStore = useAuthStore();
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

  describe("Session-Bound Persona Serialization & Reload Recovery", () => {
    // Persona presets are a demo-build feature.
    beforeEach(() => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
    });

    it("includes currentPersona in cadence_auth JSON payload in session storage on persona switch", async () => {
      const authStore = useAuthStore();
      await authStore.login();

      authStore.switchPersona("site_crc");

      expect(authStore.currentPersona).toBe("site_crc");
      expect(authStore.normalizedRoles).toContain("crc");
      expect(authStore.normalizedRoles).toContain("site_investigator");

      const storedRaw = window.sessionStorage.getItem("cadence_auth");
      expect(storedRaw).not.toBeNull();
      const payload = JSON.parse(storedRaw);
      expect(payload.currentPersona).toBe("site_crc");
      expect(payload.rawRoles).toEqual(["Site Investigator", "CRC"]);
    });

    it("restores active persona key and corresponding role mappings upon store re-initialization", () => {
      const savedAuth = {
        isAuthenticated: true,
        accessToken: "mock-token",
        user: { username: "crc_user" },
        currentPersona: "cra_monitor",
        rawRoles: ["CRA", "Monitor"],
        isDemoMode: true,
      };
      window.sessionStorage.setItem("cadence_auth", JSON.stringify(savedAuth));

      const newPinia = createPinia();
      setActivePinia(newPinia);
      const restoredStore = useAuthStore();

      expect(restoredStore.currentPersona).toBe("cra_monitor");
      expect(restoredStore.normalizedRoles).toEqual(["cra", "monitor"]);
      expect(restoredStore.isAuthenticated).toBe(true);
    });

    it("falls back to super_admin when stored persona data is invalid or corrupted", () => {
      const corruptedAuth = {
        isAuthenticated: true,
        currentPersona: "invalid_hacked_persona_role",
        rawRoles: ["SomeRole"],
      };
      window.sessionStorage.setItem(
        "cadence_auth",
        JSON.stringify(corruptedAuth)
      );

      const newPinia = createPinia();
      setActivePinia(newPinia);
      const restoredStore = useAuthStore();

      expect(restoredStore.currentPersona).toBe("super_admin");
      expect(restoredStore.isAuthenticated).toBe(true);
    });

    it("clears session storage and resets in-memory persona state upon logout", async () => {
      const authStore = useAuthStore();
      await authStore.login();
      authStore.switchPersona("auditor");

      expect(window.sessionStorage.getItem("cadence_auth")).not.toBeNull();
      expect(authStore.currentPersona).toBe("auditor");

      await authStore.logout();

      expect(window.sessionStorage.getItem("cadence_auth")).toBeNull();
      expect(authStore.currentPersona).toBe("super_admin");
      expect(authStore.isAuthenticated).toBe(false);
    });

    it("synchronizes role permissions with persona preset when restoring saved persona", () => {
      const savedAuthWithoutRoles = {
        isAuthenticated: true,
        currentPersona: "site_crc",
        isDemoMode: true,
      };
      window.sessionStorage.setItem(
        "cadence_auth",
        JSON.stringify(savedAuthWithoutRoles)
      );

      const newPinia = createPinia();
      setActivePinia(newPinia);
      const restoredStore = useAuthStore();

      expect(restoredStore.currentPersona).toBe("site_crc");
      expect(restoredStore.rawRoles).toEqual(["Site Investigator", "CRC"]);
      expect(restoredStore.normalizedRoles).toEqual([
        "site_investigator",
        "crc",
      ]);
    });
  });
});
