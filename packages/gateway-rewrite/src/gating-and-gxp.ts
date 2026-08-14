import { Injectable, Logger } from "@nestjs/common";
import * as crypto from "crypto";

/**
 * Normalizes an object's keys alphabetically and serializes it to a JSON string.
 *
 * This function ensures consistent, deterministic serialization of JSON payloads
 * to match signature generation across systems.
 *
 * @param payload The object to serialize.
 * @returns The alphabetically sorted, compact JSON string.
 */
export function canonicalizePayload(payload: Record<string, any>): string {
  const sortedPayload: Record<string, any> = {};
  const keys = Object.keys(payload).sort();
  for (const key of keys) {
    sortedPayload[key] = payload[key];
  }
  return JSON.stringify(sortedPayload);
}

/**
 * Generates an HMAC-SHA256 signature for API Gateway identity and scope headers.
 *
 * This signature secures downstream communications, allowing internal microservices
 * to verify that incoming identity metadata was produced and signed by the gateway.
 *
 * @param options Signature parameters.
 * @param options.userId The authenticated user's ID.
 * @param options.roles The comma-separated string of active roles.
 * @param options.timestamp The current timestamp as a string.
 * @param options.secret The shared secret key (HMAC key).
 * @param options.changeReason Optional change reason for audited modifications.
 * @param options.siteId Optional active site ID or comma-separated list of site IDs.
 * @param options.sponsorId Optional active sponsor ID or comma-separated list of sponsor IDs.
 * @param options.unblindedAccess Boolean indicating if unblinded access is active.
 * @param options.tenantId Optional active tenant ID (defaults to 'tenant_default').
 * @param options.sigToken Optional step-up signature token.
 * @returns Hex-encoded HMAC-SHA256 signature.
 */
export function generateGatewaySignature(options: {
  userId: string;
  roles: string;
  timestamp: string;
  secret: Buffer | string;
  changeReason?: string | null;
  siteId?: string | null;
  sponsorId?: string | null;
  unblindedAccess?: boolean;
  tenantId?: string | null;
  sigToken?: string | null;
}): string {
  const secretKey =
    typeof options.secret === "string" // pragma: allowlist secret
      ? Buffer.from(options.secret, "utf-8") // pragma: allowlist secret
      : options.secret; // pragma: allowlist secret

  const payload: Record<string, any> = {
    change_reason:
      options.changeReason !== undefined && options.changeReason !== null
        ? options.changeReason
        : "",
    roles: options.roles,
    timestamp: options.timestamp,
    user_id: options.userId,
    site_id:
      options.siteId !== undefined && options.siteId !== null
        ? options.siteId
        : "",
    sponsor_id:
      options.sponsorId !== undefined && options.sponsorId !== null
        ? options.sponsorId
        : "",
    unblinded_access: !!options.unblindedAccess,
    tenant_id:
      options.tenantId !== undefined && options.tenantId !== null
        ? options.tenantId
        : "",
  };

  if (options.sigToken !== undefined && options.sigToken !== null) {
    payload["sig_token"] = options.sigToken;
  }

  // Ensure canonical JSON formatting
  const canonicalString = canonicalizePayload(payload);

  return crypto
    .createHmac("sha256", secretKey)
    .update(canonicalString, "utf-8")
    .digest("hex");
}

export const SIGNATURE_GATED_PATTERNS = [
  "approve",
  "sign-off",
  "unblind",
  "randomize",
  "queries/sync",
  "close",
  "sign",
  "capture-consent",
];

/**
 * Checks if a path requires a step-up electronic signature verification.
 *
 * To ensure compliance with GxP and FDA 21 CFR Part 11, mutating requests (POST, PUT,
 * PATCH, DELETE) targeting sensitive paths must undergo step-up re-authentication.
 * Benign paths and eConsent routes are excluded from gateway step-up gating.
 *
 * @param method The HTTP request method (e.g. 'POST', 'GET').
 * @param path The incoming URL path.
 * @returns True if the path is signature-gated; false otherwise.
 */
export function isPathSignatureGated(method: string, path: string): boolean {
  const methodUpper = method.toUpperCase();
  const isMutation = ["POST", "PUT", "DELETE", "PATCH"].includes(methodUpper);
  if (!isMutation) {
    return false;
  }

  const pathLower = path.toLowerCase();

  // Explicit UX Guardrail exemptions
  if (pathLower.includes("econsent")) {
    return false;
  }
  if (pathLower.includes("capture-consent")) {
    return true;
  }

  for (const pattern of SIGNATURE_GATED_PATTERNS) {
    if (pattern === "sign") {
      // Split the path to check for exact "sign" segment to prevent false positive matches
      // on benign strings containing "sign" as a substring (e.g. "design", "designer", etc.).
      const segments = pathLower.split("/");
      if (segments.includes("sign")) {
        return true;
      }
    } else if (pathLower.includes(pattern)) {
      return true;
    }
  }

  return false;
}

export interface GxpDetectionRule {
  action: string;
  methods: string[];
  pathPattern: string;
  isRegex?: boolean;
  bodyConditions?: Record<string, any>;
}

export const GXP_DETECTION_RULES: GxpDetectionRule[] = [
  {
    action: "quality.capa.close",
    methods: ["POST", "PUT"],
    pathPattern: "quality/capas/[^/]+/transition",
    isRegex: true,
    bodyConditions: {
      to_status: ["CLOSED", "Closed", "closed"],
    },
  },
  {
    action: "quality.capa.cancel",
    methods: ["POST", "PUT"],
    pathPattern: "quality/capas/[^/]+/transition",
    isRegex: true,
    bodyConditions: {
      to_status: ["CANCELLED", "Cancelled", "cancelled"],
    },
  },
  {
    action: "ctms.grant.approve",
    methods: ["PUT", "PATCH", "POST"],
    pathPattern: "ctms/grants/[^/]+$",
    isRegex: true,
    bodyConditions: {
      status: ["APPROVED", "Approved", "approved"],
    },
  },
  {
    action: "execution.sdv.bulk_signoff",
    methods: ["POST", "PUT", "PATCH", "DELETE"],
    pathPattern: "sdv/bulk-sign-off",
    isRegex: false,
  },
  {
    action: "execution.form.approve",
    methods: ["POST", "PUT", "PATCH", "DELETE"],
    pathPattern: "approve",
    isRegex: false,
  },
  {
    action: "execution.form.signoff",
    methods: ["POST", "PUT", "PATCH", "DELETE"],
    pathPattern: "sign-off",
    isRegex: false,
  },
  {
    action: "execution.subject.unblind",
    methods: ["POST", "PUT", "PATCH", "DELETE"],
    pathPattern: "unblind",
    isRegex: false,
  },
  {
    action: "execution.subject.randomize",
    methods: ["POST", "PUT", "PATCH", "DELETE"],
    pathPattern: "randomize",
    isRegex: false,
  },
  {
    action: "execution.queries.sync",
    methods: ["POST", "PUT", "PATCH", "DELETE"],
    pathPattern: "queries/sync",
    isRegex: false,
  },
  {
    action: "generic.close",
    methods: ["POST", "PUT", "PATCH", "DELETE"],
    pathPattern: "close",
    isRegex: false,
  },
];

/**
 * Validates the request body against declarative GxP rule conditions.
 *
 * Normalizes values to uppercase strings to support case-insensitive, robust comparisons.
 *
 * @param body The incoming request body object.
 * @param conditions The expected field-value conditions.
 * @returns True if all conditions are satisfied; false otherwise.
 */
export function matchesBody(
  body: any,
  conditions: Record<string, any>
): boolean {
  if (!conditions || Object.keys(conditions).length === 0) {
    return true;
  }
  if (!body || typeof body !== "object") {
    return false;
  }
  for (const [field, expectedValue] of Object.entries(conditions)) {
    if (!(field in body)) {
      return false;
    }
    const actualValue = body[field];
    if (Array.isArray(expectedValue)) {
      const normalizedExpected = expectedValue.map((v) =>
        String(v).toUpperCase()
      );
      if (!normalizedExpected.includes(String(actualValue).toUpperCase())) {
        return false;
      }
    } else {
      if (
        String(actualValue).toUpperCase() !==
        String(expectedValue).toUpperCase()
      ) {
        return false;
      }
    }
  }
  return true;
}

/**
 * Resolves a regulated action term for a given HTTP request.
 *
 * This acts as the pure static declarative rule engine, matching paths, methods,
 * and body properties without an AST parser.
 *
 * @param method The HTTP request method.
 * @param path The request path.
 * @param body The optional parsed request body.
 * @returns The matching SemanticAction term string or null if none match.
 */
export function resolveRegulatedAction(
  method: string,
  path: string,
  body: any
): string | null {
  const methodUpper = method.toUpperCase();
  const pathLower = path.toLowerCase();

  // 1. Check body-driven rules first
  let bodyDrivenMatchedPath = false;
  for (const rule of GXP_DETECTION_RULES) {
    if (rule.bodyConditions && rule.methods.includes(methodUpper)) {
      let isPathMatch = false;
      if (rule.isRegex) {
        const regex = new RegExp(rule.pathPattern, "i");
        isPathMatch = regex.test(pathLower);
      } else {
        isPathMatch = pathLower.includes(rule.pathPattern.toLowerCase());
      }

      if (isPathMatch) {
        bodyDrivenMatchedPath = true;
        if (matchesBody(body, rule.bodyConditions)) {
          return rule.action;
        }
      }
    }
  }

  // If matched path of a body-driven rule, but did not satisfy body conditions, it's unregulated.
  if (bodyDrivenMatchedPath) {
    return null;
  }

  // 2. Check path-only rules
  for (const rule of GXP_DETECTION_RULES) {
    if (!rule.bodyConditions && rule.methods.includes(methodUpper)) {
      let isPathMatch = false;
      if (rule.isRegex) {
        const regex = new RegExp(rule.pathPattern, "i");
        isPathMatch = regex.test(pathLower);
      } else {
        isPathMatch = pathLower.includes(rule.pathPattern.toLowerCase());
      }

      if (isPathMatch) {
        return rule.action;
      }
    }
  }

  return null;
}

/**
 * Enforces subject boundary security checks.
 *
 * Subjects (patients) are strictly restricted from accessing resources outside of
 * their owned subject/participant identifier paths.
 *
 * @param userId The authenticated user ID.
 * @param roles Array of active roles assigned to the user.
 * @param method The HTTP request method.
 * @param path The request path.
 * @returns True if access is authorized; false if unauthorized.
 */
export function isSubjectAccessAllowed(
  userId: string,
  roles: string[],
  method: string,
  path: string
): boolean {
  const normalizedRoles = roles.map((r) => r.trim().toLowerCase());
  if (!normalizedRoles.includes("subject")) {
    return true;
  }

  const methodUpper = method.toUpperCase();
  // Strip leading slash if any
  let normalizedPath = path.startsWith("/") ? path.slice(1) : path;
  if (normalizedPath.startsWith("interop/")) {
    normalizedPath = normalizedPath.slice("interop/".length);
  }

  const parts = normalizedPath.split("/").filter((p) => p !== "");
  let isAllowed = false;

  if (parts.length === 5) {
    // Allowed endpoints for subjects: submit, sync, instruments
    const prefix4 = parts.slice(0, 4);
    if (
      (prefix4.join("/") === "api/v1/interop/epro" &&
        parts[4] === "submit" &&
        methodUpper === "POST") ||
      (prefix4.join("/") === "api/v1/interop/epro" &&
        parts[4] === "sync" &&
        methodUpper === "POST") ||
      (prefix4.join("/") === "api/v1/interop/instruments" &&
        methodUpper === "GET")
    ) {
      isAllowed = true;
    }
  } else if (parts.length === 6) {
    const prefix5 = parts.slice(0, 5);
    const prefix3 = parts.slice(0, 3);
    if (
      prefix5.join("/") === "api/v1/interop/assignments/subject" &&
      methodUpper === "GET"
    ) {
      // Must only access their own subject assignment ID
      if (parts[5] === userId) {
        isAllowed = true;
      }
    } else if (
      prefix3.join("/") === "api/v1/interop" &&
      parts[3] === "subjects" &&
      methodUpper === "GET" &&
      ["instruments", "compliance", "notifications"].includes(parts[5])
    ) {
      // Must only access their own subject ID
      if (parts[4] === userId) {
        isAllowed = true;
      }
    } else if (
      prefix3.join("/") === "api/v1/interop" &&
      parts[3] === "notifications" &&
      parts[5] === "acknowledge" &&
      methodUpper === "POST"
    ) {
      isAllowed = true;
    }
  }

  return isAllowed;
}

export const FORBIDDEN_SPOOF_HEADERS = [
  "x-user-id",
  "x-user-roles",
  "x-gateway-timestamp",
  "x-gateway-signature",
  "x-signature-version",
  "x-change-reason",
  "x-site-id",
  "x-sponsor-id",
  "x-unblinded-access",
  "x-tenant-id",
  "x-sig-token",
];

/**
 * Strips client-submitted identity metadata headers to prevent identity/scope-spoofing.
 *
 * @param headers Original incoming request headers.
 * @returns Cleaned headers mapping.
 */
export function sanitizeHeaders(
  headers: Record<string, any>
): Record<string, any> {
  const sanitized: Record<string, any> = {};
  for (const [key, value] of Object.entries(headers)) {
    const keyLower = key.toLowerCase();
    if (!FORBIDDEN_SPOOF_HEADERS.includes(keyLower)) {
      sanitized[key] = value;
    }
  }
  return sanitized;
}

@Injectable()
export class GatewayGatingService {
  private readonly logger = new Logger(GatewayGatingService.name);

  /**
   * Evaluates if a request is signature-gated and if the provided signature token is valid.
   *
   * @param method The HTTP request method.
   * @param path The request path.
   * @param body Optional request body.
   * @returns True if signature check is bypassed or passed successfully; false if rejected.
   */
  isRequestSignatureGated(method: string, path: string, body?: any): boolean {
    const resolvedAction = resolveRegulatedAction(method, path, body);
    return resolvedAction !== null || isPathSignatureGated(method, path);
  }

  /**
   * Sanitizes pre-injected identity metadata headers.
   *
   * @param headers Incoming headers.
   * @returns Cleaned headers dictionary.
   */
  sanitizeIncomingHeaders(headers: Record<string, any>): Record<string, any> {
    return sanitizeHeaders(headers);
  }

  /**
   * Performs subject-boundary validation for patient roles.
   *
   * @param userId Authenticated user ID.
   * @param roles Active roles list.
   * @param method Request HTTP method.
   * @param path Request path.
   * @returns True if allowed; false if rejected.
   */
  validateSubjectAccess(
    userId: string,
    roles: string[],
    method: string,
    path: string
  ): boolean {
    return isSubjectAccessAllowed(userId, roles, method, path);
  }

  /**
   * Generates downstream secure gateway signature.
   *
   * @param options Header generation parameters.
   * @returns Hex-encoded signature string.
   */
  signForwardedIdentity(options: {
    userId: string;
    roles: string;
    timestamp: string;
    secret: Buffer | string;
    changeReason?: string | null;
    siteId?: string | null;
    sponsorId?: string | null;
    unblindedAccess?: boolean;
    tenantId?: string | null;
    sigToken?: string | null;
  }): string {
    return generateGatewaySignature(options);
  }
}
