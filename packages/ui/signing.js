/**
 * Cryptographic signature helpers for the Cadence Clinical Platform.
 * Provides canonical JSON serialization, HMAC-SHA256 signature generation,
 * and gateway signature verification compliant with Version 1 (legacy)
 * and Version 2 (canonical JSON) specifications.
 */

/**
 * Serializes a payload into a deterministic, key-sorted, whitespace-stripped JSON string.
 * This is the JavaScript equivalent to Python's `json.dumps(obj, sort_keys=True, separators=(',', ':'))`.
 *
 * @param {any} payload - The JSON payload to canonically serialize.
 * @returns {string} The canonically serialized JSON string.
 */
export function canonicalSerialize(payload) {
  if (payload === null || typeof payload !== "object") {
    return JSON.stringify(payload);
  }
  if (Array.isArray(payload)) {
    return (
      "[" + payload.map((item) => canonicalSerialize(item)).join(",") + "]"
    );
  }
  const sortedKeys = Object.keys(payload).sort();
  const sortedObjStr = sortedKeys
    .map((key) => {
      const val = payload[key];
      return JSON.stringify(key) + ":" + canonicalSerialize(val);
    })
    .join(",");
  return "{" + sortedObjStr + "}";
}

/**
 * Generates an HMAC-SHA256 signature for a canonically serialized JSON payload.
 *
 * @param {Object} payload - The payload dictionary/object to sign.
 * @param {string|Uint8Array} secret - The HMAC shared secret key.
 * @returns {Promise<string>} A hex-encoded representation of the HMAC signature.
 */
export async function generateCanonicalSignature(payload, secret) {
  const secretKeyData =
    typeof secret === "string" ? new TextEncoder().encode(secret) : secret; // pragma: allowlist secret
  const serialized = canonicalSerialize(payload);
  const data = new TextEncoder().encode(serialized);

  const key = await globalThis.crypto.subtle.importKey(
    "raw",
    secretKeyData,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );

  const signatureBuffer = await globalThis.crypto.subtle.sign(
    "HMAC",
    key,
    data
  );
  const hashArray = Array.from(new Uint8Array(signatureBuffer));
  const hashHex = hashArray
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
  return hashHex;
}

/**
 * Verifies that the provided HMAC-SHA256 signature matches the canonically serialized JSON payload.
 *
 * @param {Object} payload - The payload to verify.
 * @param {string} signature - The expected hex signature to compare against.
 * @param {string|Uint8Array} secret - The HMAC shared secret key.
 * @returns {Promise<boolean>} True if the signature is valid, false otherwise.
 */
export async function verifyCanonicalSignature(payload, signature, secret) {
  const expectedSig = await generateCanonicalSignature(payload, secret);
  return expectedSig === signature;
}

/**
 * Generates an HMAC-SHA256 signature for API Gateway identity headers.
 * Supports Version 2 (canonical JSON format) exclusively.
 *
 * @param {string} userId - The unique user identifier.
 * @param {string} roles - Comma-separated roles assigned to the user.
 * @param {string} timestamp - The gateway-generated timestamp.
 * @param {string} [version="2"] - The signature format version (must be "2" or "v2").
 * @param {string|null} [changeReason=null] - The audit change justification (required for Version 2 mutations).
 * @param {string|Uint8Array} secret - The shared API gateway secret key.
 * @returns {Promise<string>} A hex-encoded representation of the signature.
 */
export async function generateGatewaySignature(
  userId,
  roles,
  timestamp,
  version = "2",
  changeReason = null,
  secret
) {
  if (
    version !== "2" &&
    version !== "v2" &&
    version !== "1" &&
    version !== "v1"
  ) {
    throw new Error(
      "Missing or obsolete signature format. Version 1 or Version 2 signature is required."
    );
  }
  if (version === "1" || version === "v1") {
    const serialized = `${userId}:${roles}:${timestamp}`;
    const secretKeyData =
      typeof secret === "string" ? new TextEncoder().encode(secret) : secret; // pragma: allowlist secret
    const data = new TextEncoder().encode(serialized);

    const key = await globalThis.crypto.subtle.importKey(
      "raw",
      secretKeyData,
      { name: "HMAC", hash: "SHA-256" },
      false,
      ["sign"]
    );

    const signatureBuffer = await globalThis.crypto.subtle.sign(
      "HMAC",
      key,
      data
    );
    const hashArray = Array.from(new Uint8Array(signatureBuffer));
    const hashHex = hashArray
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");
    return hashHex;
  }
  const cr =
    changeReason !== null && changeReason !== undefined ? changeReason : "";
  const payload = {
    change_reason: cr,
    roles: roles,
    timestamp: timestamp,
    user_id: userId,
  };
  return generateCanonicalSignature(payload, secret);
}

/**
 * Verifies an API Gateway identity signature against expected values.
 * Supports Version 1 (legacy colon concatenated format) and Version 2 (canonical JSON format).
 *
 * @param {string} signature - The signature to verify.
 * @param {string} userId - The unique user identifier.
 * @param {string} roles - Comma-separated roles assigned to the user.
 * @param {string} timestamp - The gateway-generated timestamp.
 * @param {string} [version="2"] - The signature format version (must be "2" or "v2", or "1" or "v1").
 * @param {string|null} [changeReason=null] - The audit change justification.
 * @param {string|Uint8Array} secret - The shared API gateway secret key.
 * @returns {Promise<boolean>} True if valid, false otherwise.
 */
export async function verifyGatewaySignature(
  signature,
  userId,
  roles,
  timestamp,
  version = "2",
  changeReason = null,
  secret
) {
  if (
    version !== "2" &&
    version !== "v2" &&
    version !== "1" &&
    version !== "v1"
  ) {
    return false;
  }
  const expectedSig = await generateGatewaySignature(
    userId,
    roles,
    timestamp,
    version,
    changeReason,
    secret
  );
  return expectedSig === signature;
}

/**
 * Generates a standard HS256-signed JWT token.
 * Used for FDA 21 CFR Part 11 single-use signature re-authentication tokens (X-Sig-Token).
 *
 * @param {Object} payload - The JWT payload to sign.
 * @param {string|Uint8Array} secret - The HMAC shared secret key.
 * @returns {Promise<string>} The fully constructed and signed JWT token.
 */
export async function generateJwtHS256(payload, secret) {
  const encoder = new TextEncoder();

  function base64url(arr) {
    const binary = String.fromCharCode(...arr);
    const base64 = btoa(binary);
    return base64.replace(/=/g, "").replace(/\+/g, "-").replace(/\//g, "_");
  }

  const header = { alg: "HS256", typ: "JWT" };
  const headerStr = base64url(encoder.encode(JSON.stringify(header)));
  const payloadStr = base64url(encoder.encode(JSON.stringify(payload)));

  const tokenInput = headerStr + "." + payloadStr;
  const keyData = typeof secret === "string" ? encoder.encode(secret) : secret; // pragma: allowlist secret
  const data = encoder.encode(tokenInput);

  const key = await globalThis.crypto.subtle.importKey(
    "raw",
    keyData,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );

  const signatureBuffer = await globalThis.crypto.subtle.sign(
    "HMAC",
    key,
    data
  );
  const signatureStr = base64url(new Uint8Array(signatureBuffer));
  return tokenInput + "." + signatureStr;
}

/**
 * Computes a standard SHA-256 hash of a message using Web Crypto APIs.
 *
 * @param {string} message - The plaintext message to hash.
 * @returns {Promise<string>} The hexadecimal SHA-256 digest.
 */
export async function sha256(message) {
  const msgBuffer = new TextEncoder().encode(message);
  const hashBuffer = await globalThis.crypto.subtle.digest(
    "SHA-256",
    msgBuffer
  );
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  const hashHex = hashArray
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
  return hashHex;
}

/**
 * Standard CDASH clinical standard field validation logic.
 *
 * @param {Object} fieldMeta - Metadata of the field containing validation rules.
 * @param {any} val - Entered value of the field.
 * @param {Object} [context={}] - Evaluation context for constraints.
 * @param {Function|null} [evaluateASTFn=null] - AST evaluation callback.
 * @returns {Object} { valid: boolean, message?: string }
 */
export function validateField(
  fieldMeta,
  val,
  context = {},
  evaluateASTFn = null
) {
  if (!fieldMeta) return { valid: true };

  const rules = fieldMeta.validation || {};

  // Required check
  if (
    rules.required &&
    (val === undefined || val === null || val.toString().trim() === "")
  ) {
    return { valid: false, message: "This field is required." };
  }

  // Only perform format/range validation if there is a value entered
  if (val !== undefined && val !== null && val.toString().trim() !== "") {
    // Pattern (Regex) check
    if (rules.pattern) {
      const regex = new RegExp(rules.pattern);
      if (!regex.test(val)) {
        return { valid: false, message: rules.message || "Invalid format." };
      }
    }

    // Min / Max (Numeric check)
    if (rules.min !== undefined || rules.max !== undefined) {
      const num = parseFloat(val);
      if (isNaN(num)) {
        return { valid: false, message: "Value must be a number." };
      }
      if (rules.min !== undefined && num < rules.min) {
        return {
          valid: false,
          message: rules.message || `Minimum value is ${rules.min}.`,
        };
      }
      if (rules.max !== undefined && num > rules.max) {
        return {
          valid: false,
          message: rules.message || `Maximum value is ${rules.max}.`,
        };
      }
    }
  }

  // Constraint validation (must evaluate to true/truthy, otherwise invalid)
  if (fieldMeta.constraint) {
    if (evaluateASTFn) {
      const isOk = evaluateASTFn(
        fieldMeta.constraint.condition || fieldMeta.constraint,
        context
      );
      if (isOk === false) {
        return {
          valid: false,
          message:
            fieldMeta.constraint.query_message ||
            "Constraint validation failed.",
        };
      }
    }
  }

  return { valid: true };
}
