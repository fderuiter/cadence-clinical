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
  if (version !== "2" && version !== "v2") {
    throw new Error("Missing or obsolete signature format. Version 2 canonical JSON signature is required.");
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
 * Supports Version 2 (canonical JSON format) exclusively.
 *
 * @param {string} signature - The signature to verify.
 * @param {string} userId - The unique user identifier.
 * @param {string} roles - Comma-separated roles assigned to the user.
 * @param {string} timestamp - The gateway-generated timestamp.
 * @param {string} [version="2"] - The signature format version (must be "2" or "v2").
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
  if (version !== "2" && version !== "v2") {
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
