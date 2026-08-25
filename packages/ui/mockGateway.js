/**
 * Mock Gateway Simulator and Local HMAC Verification Engine.
 * Emulates API Gateway path-prefix stripping, 403 Subject-role route blocking,
 * and dual-signature header validation using browser Crypto APIs.
 */

import {
  generateGatewaySignature,
  verifyGatewaySignature,
  generateCanonicalSignature,
  verifyCanonicalSignature,
  generateJwtHS256,
} from "./signing.js";

export {
  generateGatewaySignature,
  verifyGatewaySignature,
  generateCanonicalSignature,
  verifyCanonicalSignature,
  generateJwtHS256,
};

export const GATEWAY_SECRET = "internal-gateway-secret-12345"; // pragma: allowlist secret

export const VALID_SERVICE_PREFIXES = [
  "designer/",
  "execution/",
  "etmf/",
  "interop/",
  "ctms/",
  "notifications/",
  "quality/",
  "safety/",
  "tickets/",
  "eisf/",
  "org/",
  "econsent/",
  "terminology/",
  "dictionary/",
  "cdisc/",
  "usdm/",
  "ecoa/",
  "events/",
];

export const VALID_API_PREFIXES = ["api/v1/", "api/v2/", "health", "me"];

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
 * Checks if a route path is signature-gated.
 *
 * @param {string} pathLower - Lowercase HTTP route path.
 * @returns {boolean} True if signature-gated.
 */
export function isPathSignatureGated(pathLower) {
  if (!pathLower) return false;
  if (pathLower.includes("capture-consent")) return true;
  if (pathLower.includes("econsent")) return false; // eConsent handles internal subject consent

  for (const pattern of SIGNATURE_GATED_PATTERNS) {
    if (pattern === "sign") {
      const segments = pathLower.split("/");
      if (segments.includes("sign") || pathLower.endsWith("/sign")) {
        return true;
      }
    } else if (pathLower.includes(pattern)) {
      return true;
    }
  }
  return false;
}

/**
 * Checks if a route path is whitelisted for Subject (patient) role.
 *
 * @param {string} pathLower - Lowercase HTTP route path.
 * @param {string} methodUpper - Uppercase HTTP method.
 * @param {string|null} [userId=null] - Authenticated user identifier.
 * @returns {boolean} True if whitelisted for Subject principal.
 */
export function isWhitelistedSubjectRoute(
  pathLower,
  methodUpper,
  userId = null
) {
  if (!pathLower) return false;
  if (pathLower === "health" || pathLower === "me") return true;

  // eConsent endpoints
  if (
    pathLower.startsWith("econsent") ||
    pathLower.startsWith("api/v1/econsent")
  ) {
    return true;
  }

  // Normalize interop prefix
  const normalizedPath = pathLower.startsWith("interop/")
    ? pathLower.slice("interop/".length)
    : pathLower;

  const parts = normalizedPath.split("/").filter(Boolean);

  if (parts.length === 5) {
    // POST /api/v1/interop/epro/submit or epro/sync or GET /api/v1/interop/instruments
    if (parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop") {
      if (
        parts[3] === "epro" &&
        (parts[4] === "submit" || parts[4] === "sync") &&
        methodUpper === "POST"
      ) {
        return true;
      }
      if (parts[3] === "instruments" && methodUpper === "GET") {
        return true;
      }
    }
  }

  if (parts.length === 6) {
    if (parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop") {
      // GET /api/v1/interop/assignments/subject/{authenticated-subject-id}
      if (
        parts[3] === "assignments" &&
        parts[4] === "subject" &&
        methodUpper === "GET"
      ) {
        if (!userId || parts[5] === userId.toLowerCase()) {
          return true;
        }
      }
      // GET /api/v1/interop/subjects/{id}/instruments|compliance|notifications
      if (
        parts[3] === "subjects" &&
        (parts[5] === "instruments" ||
          parts[5] === "compliance" ||
          parts[5] === "notifications") &&
        methodUpper === "GET"
      ) {
        if (!userId || parts[4] === userId.toLowerCase()) {
          return true;
        }
      }
      // POST /api/v1/interop/notifications/{id}/acknowledge
      if (
        parts[3] === "notifications" &&
        parts[5] === "acknowledge" &&
        methodUpper === "POST"
      ) {
        return true;
      }
    }
  }

  return false;
}

/**
 * Strips gateway service prefix from path if present.
 *
 * @param {string} path - Request path.
 * @returns {string} Stripped downstream path.
 */
export function stripGatewayPathPrefix(path) {
  if (!path) return "";
  for (const prefix of VALID_SERVICE_PREFIXES) {
    if (path.startsWith(prefix)) {
      return path.slice(prefix.length);
    }
  }
  return path;
}

/**
 * Validates whether a request path has a valid gateway prefix.
 *
 * @param {string} path - Request path.
 * @returns {boolean} True if valid path prefix.
 */
export function isValidGatewayPathPrefix(path) {
  if (!path || path === "health" || path === "me") return true;
  for (const prefix of VALID_SERVICE_PREFIXES) {
    if (path.startsWith(prefix)) return true;
  }
  for (const prefix of VALID_API_PREFIXES) {
    if (path.startsWith(prefix)) return true;
  }
  return false;
}

/**
 * Validates incoming gateway request.
 * Performs:
 * 1. Path-prefix validation & stripping
 * 2. 403 Forbidden Subject role-based route blocking
 * 3. Dual-signature header validation (X-Sig-Token & X-Gateway-Signature)
 *
 * @param {Request|Object} req - Intercepted Request object or simulated request object.
 * @param {Object} [options={}] - Custom options.
 * @returns {Promise<Object>} { valid: boolean, status: number, body: Object, strippedPath: string }
 */
export async function validateGatewayRequest(req, options = {}) {
  const secret = options.secret || GATEWAY_SECRET;

  // Extract URL & path
  let urlStr =
    typeof req.url === "string" ? req.url : req.url?.toString() || "";
  let methodUpper = (req.method || "GET").toUpperCase();

  let url;
  try {
    url = new URL(urlStr, "http://localhost:8000");
  } catch {
    url = new URL("http://localhost:8000/" + urlStr);
  }

  const rawPath = url.pathname.replace(/^\/+/, "");
  const pathLower = rawPath.toLowerCase();

  // 1. Path prefix validation
  if (!isValidGatewayPathPrefix(rawPath)) {
    console.error(
      `[MSW Mock Gateway] Invalid path prefix intercepted: "${rawPath}"`
    );
    return {
      valid: false,
      status: 400,
      body: {
        detail: `Invalid gateway path prefix: ${rawPath.split("/")[0]}`,
        error: "INVALID_PATH_PREFIX",
      },
      strippedPath: rawPath,
    };
  }

  const strippedPath = stripGatewayPathPrefix(rawPath);

  // Extract Headers
  const getHeader = (name) => {
    if (!req.headers) return null;
    if (typeof req.headers.get === "function") {
      return req.headers.get(name) || req.headers.get(name.toLowerCase());
    }
    const keys = Object.keys(req.headers);
    const key = keys.find((k) => k.toLowerCase() === name.toLowerCase());
    return key ? req.headers[key] : null;
  };

  // Extract User ID and Roles
  let userId =
    getHeader("X-User-Id") ||
    getHeader("x-user-id") ||
    options.defaultUserId ||
    "";
  let rolesStr =
    getHeader("X-User-Roles") ||
    getHeader("x-user-roles") ||
    getHeader("X-Mock-Roles") ||
    options.defaultRoles ||
    "";

  // If Bearer token present, try decoding JWT claims if missing roles/userId
  const authHeader = getHeader("Authorization") || getHeader("authorization");
  if (authHeader && authHeader.startsWith("Bearer ")) {
    const token = authHeader.split(" ")[1];
    try {
      const parts = token.split(".");
      if (parts.length === 3) {
        const payloadJson = JSON.parse(
          atob(parts[1].replace(/-/g, "+").replace(/_/g, "/"))
        );
        if (!userId) userId = payloadJson.sub || payloadJson.userId || "";
        if (!rolesStr) {
          const rList = [];
          if (payloadJson.realm_access?.roles) {
            rList.push(...payloadJson.realm_access.roles);
          }
          if (payloadJson.roles) {
            if (Array.isArray(payloadJson.roles))
              rList.push(...payloadJson.roles);
            else rList.push(payloadJson.roles);
          }
          rolesStr = rList.join(",");
        }
      }
    } catch {
      // Ignore token parse failure
    }
  }

  const rolesList = rolesStr
    .split(",")
    .map((r) => r.trim().toLowerCase())
    .filter(Boolean);

  // 2. Role-based route blocking for Subject role
  if (rolesList.includes("subject")) {
    const isWhitelisted = isWhitelistedSubjectRoute(
      pathLower,
      methodUpper,
      userId
    );
    if (!isWhitelisted) {
      console.error(
        `[MSW Mock Gateway] 403 Forbidden: Subject role attempted to access administrative route: "${rawPath}"`
      );
      return {
        valid: false,
        status: 403,
        body: {
          detail:
            "Access denied: Subject principal is not authorized to access this route",
        },
        strippedPath,
      };
    }
  }

  // 3. Signature Gating & Dual-Signature Header Validation
  const isMutation = ["POST", "PUT", "DELETE", "PATCH"].includes(methodUpper);
  const isGated = isPathSignatureGated(pathLower);

  if (isMutation && isGated) {
    const sigToken = getHeader("X-Sig-Token") || getHeader("x-sig-token");
    if (!sigToken) {
      console.error(
        `[MSW Mock Gateway] Missing or invalid e-signature header (X-Sig-Token) for signature-gated route: "${rawPath}"`
      );
      return {
        valid: false,
        status: 401,
        body: {
          detail: "REAUTHENTICATION_REQUIRED",
          error: "REAUTHENTICATION_REQUIRED",
          message:
            "Missing or invalid electronic signature header (X-Sig-Token) for signature-gated operation",
        },
        strippedPath,
      };
    }
  }

  // Gateway Identity Signature Validation (X-Gateway-Signature)
  const gatewaySig =
    getHeader("X-Gateway-Signature") || getHeader("x-gateway-signature");
  if (gatewaySig) {
    const timestamp =
      getHeader("X-Gateway-Timestamp") ||
      getHeader("x-gateway-timestamp") ||
      "";
    const version =
      getHeader("X-Signature-Version") ||
      getHeader("x-signature-version") ||
      "2";
    const changeReason =
      getHeader("X-Change-Reason") || getHeader("x-change-reason") || null;

    if (version !== "2" && version !== "v2") {
      console.error(
        `[MSW Mock Gateway] Obsolete signature version: "${version}"`
      );
      return {
        valid: false,
        status: 401,
        body: {
          detail:
            "Missing or obsolete signature format. Version 2 signature is required.",
        },
        strippedPath,
      };
    }

    const isValidSig = await verifyGatewaySignature(
      gatewaySig,
      userId,
      rolesStr,
      timestamp,
      version,
      changeReason,
      secret
    );

    if (!isValidSig) {
      console.error(
        `[MSW Mock Gateway] Invalid or tampered X-Gateway-Signature header: "${gatewaySig}"`
      );
      return {
        valid: false,
        status: 401,
        body: { detail: "Invalid gateway signature" },
        strippedPath,
      };
    }
  }

  return {
    valid: true,
    status: 200,
    body: { status: "ok" },
    strippedPath,
  };
}
