import { describe, it, expect, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth";
import { useSignatureStore } from "../src/stores/signatures";
import { useNotificationsStore } from "../src/stores/notifications";
import { useEtmfStore } from "../src/stores/etmf";
import { submitBatchSignature } from "../src/api/execution";

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

  describe("Fallback Offline / Demo Mode Behavior", () => {
    it("should initialize in demo mode with fallback identity and normalized roles", () => {
      const authStore = useAuthStore();

      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.isDemoMode).toBe(true);
      expect(authStore.token).toBeNull();

      // Check fallback identity details
      expect(authStore.identity).toEqual({
        username: "fderuiter",
        email: "fderuiter@example.com",
        firstName: "Frans",
        lastName: "de Ruiter",
        id: "fderuiter-id-12345",
      });

      // Check fallback normalized roles
      expect(authStore.normalizedRoles).toEqual(["monitor", "sponsor_admin"]);
    });

    it("allows mockup login and logout when keycloak is not initialized", async () => {
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

  describe("Production Lockdown Constraints", () => {
    it("should refuse to login with offline mock in production environments", async () => {
      const authStore = useAuthStore();

      // Stub production environment
      const originalProd = import.meta.env.PROD;
      const originalMode = import.meta.env.MODE;
      import.meta.env.PROD = true;
      import.meta.env.MODE = "production";

      authStore.isDemoMode = true;

      await expect(authStore.login()).rejects.toThrow(
        "Offline login fallback is disabled in production environments."
      );

      // Clean up
      import.meta.env.PROD = originalProd;
      import.meta.env.MODE = originalMode;
    });

    it("should refuse to logout with offline mock in production environments", async () => {
      const authStore = useAuthStore();

      // Stub production environment
      const originalProd = import.meta.env.PROD;
      const originalMode = import.meta.env.MODE;
      import.meta.env.PROD = true;
      import.meta.env.MODE = "production";

      authStore.isDemoMode = true;

      await expect(authStore.logout()).rejects.toThrow(
        "Offline logout fallback is disabled in production environments."
      );

      // Clean up
      import.meta.env.PROD = originalProd;
      import.meta.env.MODE = originalMode;
    });
  });

  describe("Stores Demo Mode Local Simulation tests", () => {
    let authStore;

    beforeEach(() => {
      authStore = useAuthStore();
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.clear();
      }
    });

    describe("Signatures Store Simulation", () => {
      it("intercepts batch signature submission and persists to localStorage in demo mode", async () => {
        authStore.isDemoMode = true;

        const payload = {
          studyId: "STUDY-01",
          subjectId: "SUBJ-01",
          formIds: ["FORM-01", "Form-02"],
          password: "my-password", // pragma: allowlist secret
          meaning: "APPROVED",
        };

        const res = await submitBatchSignature(payload);

        expect(res.signature_id).toBeDefined();
        expect(res.signature_id.startsWith("mock-sig-")).toBe(true);
        expect(res.signed_forms_count).toBe(2);

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

        // Check localStorage persistence
        const stored = window.localStorage.getItem("demo_notifications");
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
        await submitBatchSignature({
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
        expect(
          window.localStorage.getItem("demo_notifications")
        ).not.toBeNull();
        expect(window.localStorage.getItem("demo_documents")).not.toBeNull();

        // Trigger reset Demo Storage on each store
        signatureStore.resetDemoStorage();
        notificationsStore.resetDemoStorage();
        etmfStore.resetDemoStorage();

        // Verify localStorage is cleared
        expect(window.localStorage.getItem("lastSignatureResult")).toBeNull();
        expect(window.localStorage.getItem("demo_notifications")).toBeNull();
        expect(window.localStorage.getItem("demo_documents")).toBeNull();

        // Verify in-memory state is back to pristine default state
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

      // Stub demo environment with PROD = true
      const originalProd = import.meta.env.PROD;
      const originalMode = import.meta.env.MODE;
      import.meta.env.PROD = true;
      import.meta.env.MODE = "demo";

      authStore.isDemoMode = true;

      // This should succeed because of the check excluding MODE === "demo" from isProduction
      await authStore.login();
      expect(authStore.isAuthenticated).toBe(true);
      expect(authStore.rawRoles).toContain("Sponsor Designer");

      await authStore.logout();
      expect(authStore.isAuthenticated).toBe(false);

      // Clean up
      import.meta.env.PROD = originalProd;
      import.meta.env.MODE = originalMode;
    });
  });
});
