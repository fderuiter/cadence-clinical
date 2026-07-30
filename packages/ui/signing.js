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

/**
 * Constructs a ledger block with a cryptographic hash.
 *
 * @param {number} index - Index of the block.
 * @param {string} timestamp - ISO timestamp.
 * @param {string} action - Action identifier.
 * @param {Object} details - Details payload.
 * @param {string} reason - Justification reason.
 * @param {string} prevHash - Hash of the previous block.
 * @returns {Promise<Object>} The completed ledger block containing the hash.
 */
export async function buildLedgerBlock(
  index,
  timestamp,
  action,
  details,
  reason,
  prevHash
) {
  const payloadString = `${index}|${timestamp}|${action}|${JSON.stringify(details)}|${reason}|${prevHash}`;
  const hash = await sha256(payloadString);
  return {
    index,
    timestamp,
    action,
    details,
    reason,
    prevHash,
    hash,
  };
}

function arrayBufferToBase64(buffer) {
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

function base64ToUint8Array(base64) {
  const binaryString = atob(base64);
  const bytes = new Uint8Array(binaryString.length);
  for (let i = 0; i < binaryString.length; i++) {
    bytes[i] = binaryString.charCodeAt(i);
  }
  return bytes;
}

function intTo4BytesBigEndian(value) {
  const bytes = new Uint8Array(4);
  bytes[0] = (value >> 24) & 0xff;
  bytes[1] = (value >> 16) & 0xff;
  bytes[2] = (value >> 8) & 0xff;
  bytes[3] = value & 0xff;
  return bytes;
}

function bytesToIntBigEndian(bytes) {
  return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
}

/**
 * Encrypts a payload dictionary using AES-GCM and packages it in a versioned envelope.
 * Produces base64(version(4B, big-endian) || nonce(12B) || ciphertext+tag).
 *
 * @param {Object} payload - The payload to encrypt.
 * @param {Uint8Array} rawKey - The 256-bit symmetric key.
 * @param {number} [version=1] - Envelope version.
 * @param {Uint8Array|null} [aad=null] - Authenticated Additional Data.
 * @returns {Promise<string>} Base64-encoded envelope.
 */
export async function encryptAESGCM(payload, rawKey, version = 1, aad = null) {
  const serialized = canonicalSerialize(payload);
  const plaintextBytes = new TextEncoder().encode(serialized);

  // 12-byte random nonce
  const nonce = globalThis.crypto.getRandomValues(new Uint8Array(12));

  const cryptoKey = await globalThis.crypto.subtle.importKey(
    "raw",
    rawKey,
    { name: "AES-GCM" },
    false,
    ["encrypt"]
  );

  const encryptParams = {
    name: "AES-GCM",
    iv: nonce,
  };
  if (aad) {
    encryptParams.additionalData = aad;
  }

  const encryptedBuffer = await globalThis.crypto.subtle.encrypt(
    encryptParams,
    cryptoKey,
    plaintextBytes
  );

  const ciphertextAndTag = new Uint8Array(encryptedBuffer);

  const versionBytes = intTo4BytesBigEndian(version);
  const packed = new Uint8Array(4 + 12 + ciphertextAndTag.length);
  packed.set(versionBytes, 0);
  packed.set(nonce, 4);
  packed.set(ciphertextAndTag, 16);

  return arrayBufferToBase64(packed);
}

/**
 * Decrypts a versioned AES-GCM envelope and returns the parsed JSON payload.
 *
 * @param {string} encryptedStr - Base64-encoded envelope.
 * @param {Uint8Array} rawKey - The 256-bit symmetric key.
 * @param {number} [expectedVersion=1] - Expected envelope version.
 * @param {Uint8Array|null} [aad=null] - Authenticated Additional Data.
 * @returns {Promise<Object>} Decrypted payload object.
 */
export async function decryptAESGCM(
  encryptedStr,
  rawKey,
  expectedVersion = 1,
  aad = null
) {
  let packedBytes;
  try {
    packedBytes = base64ToUint8Array(encryptedStr);
  } catch (err) {
    throw new Error("Invalid base64 payload", { cause: err });
  }

  if (packedBytes.length < 16) {
    throw new Error("Invalid envelope format: payload too short");
  }

  const versionBytes = packedBytes.slice(0, 4);
  const version = bytesToIntBigEndian(versionBytes);
  if (version !== expectedVersion) {
    throw new Error(`Unrecognized version marker: ${version}`);
  }

  const nonce = packedBytes.slice(4, 16);
  const ciphertextAndTag = packedBytes.slice(16);

  const cryptoKey = await globalThis.crypto.subtle.importKey(
    "raw",
    rawKey,
    { name: "AES-GCM" },
    false,
    ["decrypt"]
  );

  const decryptParams = {
    name: "AES-GCM",
    iv: nonce,
  };
  if (aad) {
    decryptParams.additionalData = aad;
  }

  let decryptedBuffer;
  try {
    decryptedBuffer = await globalThis.crypto.subtle.decrypt(
      decryptParams,
      cryptoKey,
      ciphertextAndTag
    );
  } catch (err) {
    throw new Error("Decryption failed: tampered ciphertext, nonce, or AAD", {
      cause: err,
    });
  }

  const decryptedStr = new TextDecoder().decode(decryptedBuffer);
  try {
    return JSON.parse(decryptedStr);
  } catch (err) {
    throw new Error("Deserialization failed: invalid JSON", { cause: err });
  }
}

/**
 * Derives a 256-bit session key using HKDF-SHA256.
 *
 * @param {string|Uint8Array} sessionMaterial - Input session keying material.
 * @param {string|Uint8Array} salt - HKDF salt.
 * @param {string|Uint8Array} info - HKDF info payload.
 * @returns {Promise<Uint8Array>} 256-bit derived key bytes.
 */
export async function deriveSessionKey(sessionMaterial, salt, info) {
  const encoder = new TextEncoder();
  const materialBytes =
    typeof sessionMaterial === "string"
      ? encoder.encode(sessionMaterial)
      : sessionMaterial;
  const saltBytes = typeof salt === "string" ? encoder.encode(salt) : salt;
  const infoBytes = typeof info === "string" ? encoder.encode(info) : info;

  const baseKey = await globalThis.crypto.subtle.importKey(
    "raw",
    materialBytes,
    "HKDF",
    false,
    ["deriveBits"]
  );

  const derivedBits = await globalThis.crypto.subtle.deriveBits(
    {
      name: "HKDF",
      hash: "SHA-256",
      salt: saltBytes,
      info: infoBytes,
    },
    baseKey,
    256
  );

  return new Uint8Array(derivedBits);
}
