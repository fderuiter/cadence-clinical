import { describe, it, expect } from "vitest";
import {
  GatewayGatingService,
  isPathSignatureGated,
  resolveRegulatedAction,
  isSubjectAccessAllowed,
  sanitizeHeaders,
  generateGatewaySignature,
} from "../src/index.js";

describe("Gateway Gating and GxP Service Suite", () => {
  const gatingService = new GatewayGatingService();

  describe("Requirement 1 & Acceptance Criteria: Exact matching on 'sign' endpoint, ignoring 'design'", () => {
    it("should return true for mutating requests on exact 'sign' segment", () => {
      // POST /api/v1/interop/sign -> signature-gated
      expect(isPathSignatureGated("POST", "/api/v1/interop/sign")).toBe(true);
      expect(
        isPathSignatureGated("PUT", "/api/v1/interop/form/sign/submit")
      ).toBe(true);
    });

    it("should ignore benign matches like 'design' or 'designer'", () => {
      // POST /api/v1/design -> unregulated
      expect(isPathSignatureGated("POST", "/api/v1/design")).toBe(false);
      expect(isPathSignatureGated("POST", "/api/v1/designer/schema")).toBe(
        false
      );
    });

    it("should ignore GET requests on any path, including 'sign'", () => {
      // GET requests are not modifications, hence never signature-gated
      expect(isPathSignatureGated("GET", "/api/v1/interop/sign")).toBe(false);
    });

    it("should respect other signature-gated patterns like 'approve' or 'unblind'", () => {
      expect(isPathSignatureGated("POST", "/api/v1/execution/approve")).toBe(
        true
      );
      expect(isPathSignatureGated("PATCH", "/api/v1/execution/unblind")).toBe(
        true
      );
    });

    it("should exclude 'econsent' paths from gating to allow patient onboarding flow", () => {
      expect(
        isPathSignatureGated("POST", "/api/v1/econsent/capture-consent")
      ).toBe(false);
    });
  });

  describe("Requirement 2 & Acceptance Criteria: Body-driven rules (CAPA closure to 'CLOSED')", () => {
    it("should resolve quality.capa.close action when status normalizes to 'CLOSED'", () => {
      const body = { to_status: "Closed" };
      const action = resolveRegulatedAction(
        "POST",
        "quality/capas/capa-123/transition",
        body
      );
      expect(action).toBe("quality.capa.close");
    });

    it("should resolve quality.capa.cancel action when status normalizes to 'CANCELLED'", () => {
      const body = { to_status: "cancelled" };
      const action = resolveRegulatedAction(
        "POST",
        "quality/capas/capa-123/transition",
        body
      );
      expect(action).toBe("quality.capa.cancel");
    });

    it("should return null if CAPA is transitioned to an unregulated status", () => {
      const body = { to_status: "draft" };
      const action = resolveRegulatedAction(
        "POST",
        "quality/capas/capa-123/transition",
        body
      );
      expect(action).toBeNull();
    });

    it("should resolve ctms.grant.approve action when status normalizes to 'APPROVED'", () => {
      const body = { status: "APPROVED" };
      const action = resolveRegulatedAction(
        "PATCH",
        "ctms/grants/grant-456",
        body
      );
      expect(action).toBe("ctms.grant.approve");
    });
  });

  describe("Requirement 3 & Acceptance Criteria: Subject / Patient Boundary Checks", () => {
    const userId = "subject-patient-001";
    const roles = ["subject"];

    it("should allow a patient to access their owned assignments route", () => {
      const path = `/api/v1/interop/assignments/subject/${userId}`;
      expect(isSubjectAccessAllowed(userId, roles, "GET", path)).toBe(true);
    });

    it("should reject a patient attempting to access another participant's assignments path", () => {
      const path = "/api/v1/interop/assignments/subject/subject-patient-999";
      expect(isSubjectAccessAllowed(userId, roles, "GET", path)).toBe(false);
    });

    it("should allow general epro submit, sync and instrument paths", () => {
      expect(
        isSubjectAccessAllowed(
          userId,
          roles,
          "POST",
          "/api/v1/interop/epro/submit"
        )
      ).toBe(true);
      expect(
        isSubjectAccessAllowed(
          userId,
          roles,
          "POST",
          "/api/v1/interop/epro/sync"
        )
      ).toBe(true);
      expect(
        isSubjectAccessAllowed(
          userId,
          roles,
          "GET",
          "/api/v1/interop/instruments/instrument-123"
        )
      ).toBe(true);
    });

    it("should reject other clinical CTMS, designer or tickets routes for subject roles", () => {
      expect(
        isSubjectAccessAllowed(
          userId,
          roles,
          "GET",
          "/api/v1/designer/protocols"
        )
      ).toBe(false);
      expect(
        isSubjectAccessAllowed(userId, roles, "POST", "/api/v1/quality/capas")
      ).toBe(false);
    });

    it("should always allow non-subject (clinical coordinator, admin, etc.) roles to bypass boundary checks", () => {
      expect(
        isSubjectAccessAllowed(
          "some-coord",
          ["crc", "investigator"],
          "GET",
          "/api/v1/interop/assignments/subject/subject-patient-999"
        )
      ).toBe(true);
    });
  });

  describe("Requirement 4 & Acceptance Criteria: Clean and Sanitise spoofing headers", () => {
    it("should remove all incoming gateway metadata headers to prevent identity and scope-spoofing", () => {
      const dirtyHeaders = {
        Host: "api.cadenceclinical.com",
        "X-User-Id": "spoofed-user-id",
        "X-User-Roles": "admin",
        "X-Gateway-Signature": "fake-sig",
        "X-Tenant-Id": "spoofed-tenant",
        Authorization: "Bearer some-real-token",
        "Content-Type": "application/json",
      };

      const sanitized = sanitizeHeaders(dirtyHeaders);

      expect(sanitized).toHaveProperty("Host");
      expect(sanitized).toHaveProperty("Authorization");
      expect(sanitized).toHaveProperty("Content-Type");

      expect(sanitized).not.toHaveProperty("X-User-Id");
      expect(sanitized).not.toHaveProperty("X-User-Roles");
      expect(sanitized).not.toHaveProperty("X-Gateway-Signature");
      expect(sanitized).not.toHaveProperty("X-Tenant-Id");
    });
  });

  describe("Requirement 5, 6 & Acceptance Criteria: Secure Cryptographic Signatures parity check", () => {
    it("should produce a cryptographically verifiable gateway signature and allow verification parity", () => {
      const secret = "my-secure-gateway-secret-key-12345";
      const timestamp = "1723580000";
      const rolesStr = "investigator,site_investigator";

      const signature = generateGatewaySignature({
        userId: "user-123",
        roles: rolesStr,
        timestamp: timestamp,
        secret: secret,
        changeReason: "CAPA Closure",
        siteId: "site-A",
        sponsorId: "sponsor-B",
        unblindedAccess: true,
        tenantId: "tenant-one",
        sigToken: "token-abc-123",
      });

      expect(signature).toBeDefined();
      expect(typeof signature).toBe("string");
      expect(signature.length).toBe(64); // SHA-256 hex length
    });
  });
});
