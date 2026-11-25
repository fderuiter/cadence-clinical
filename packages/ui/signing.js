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
  if (payload === undefined) {
    return undefined;
  }
  if (payload instanceof Date) {
    return JSON.stringify(payload);
  }
  if (payload === null || typeof payload !== "object") {
    return JSON.stringify(payload);
  }
  if (Array.isArray(payload)) {
    return (
      "[" +
      payload
        .map((item) => {
          if (item === undefined) return "null";
          const res = canonicalSerialize(item);
          return res === undefined ? "null" : res;
        })
        .join(",") +
      "]"
    );
  }
  const sortedKeys = Object.keys(payload)
    .filter((key) => payload[key] !== undefined)
    .sort();
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
    throw new Error(
      "Missing or obsolete signature format. Version 2 signature is required."
    );
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
      if (!isOk) {
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
  const payloadString = `${index}|${timestamp}|${action}|${canonicalSerialize(details)}|${reason}|${prevHash}`;
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

  const cryptoKey =
    rawKey && typeof rawKey === "object" && rawKey.algorithm
      ? rawKey
      : await globalThis.crypto.subtle.importKey(
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

  const cryptoKey =
    rawKey && typeof rawKey === "object" && rawKey.algorithm
      ? rawKey
      : await globalThis.crypto.subtle.importKey(
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

/**
 * Derives a 256-bit key-wrapping key from a PIN and a salt using PBKDF2-HMAC-SHA256.
 *
 * @param {string} pin - The numeric PIN.
 * @param {Uint8Array} salt - The PBKDF2 salt.
 * @param {number} [iterations=100000] - PBKDF2 iteration count.
 * @returns {Promise<Uint8Array>} 256-bit derived key bytes.
 */
export async function deriveKeyFromPIN(pin, salt, iterations = 100000) {
  const encoder = new TextEncoder();
  const pinBytes = encoder.encode(pin);
  const baseKey = await globalThis.crypto.subtle.importKey(
    "raw",
    pinBytes,
    "PBKDF2",
    false,
    ["deriveBits"]
  );
  const derivedBits = await globalThis.crypto.subtle.deriveBits(
    {
      name: "PBKDF2",
      salt: salt,
      iterations: iterations,
      hash: "SHA-256",
    },
    baseKey,
    256
  );
  return new Uint8Array(derivedBits);
}

/**
 * Cryptographic Contract v3: governed `semantic_action` grammar enforced by
 * `SignatureTokenEndpoints.cs` (`^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*){1,4}$`).
 */
export const SEMANTIC_ACTION_PATTERN =
  /^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*){1,4}$/;

/**
 * Maximum `/api/v1/...` target path length accepted by the gateway step-up endpoint.
 */
export const STEP_UP_ACTION_MAX_LENGTH = 512;

/**
 * Canonical `semantic_action` identifiers shared across Cadence Clinical workflows.
 */
export const SEMANTIC_ACTIONS = Object.freeze({
  CTMS_DOA_SIGN_OFF: "ctms.doa.sign_off",
  ECONSENT_CAPTURE: "econsent.consent.capture",
  ECONSENT_RECONSENT_SIGN: "econsent.reconsent.sign",
  CEC_CHAIR_ADJUDICATE: "execution.cec.chair_adjudicate",
  CGT_BEDSIDE_VERIFY: "execution.cgt.bedside_verify",
  CGT_DESTRUCTION_CERTIFICATE: "execution.cgt.destruction_certificate",
  ETMF_DOCUMENT_SIGN_OFF: "etmf.document.sign_off",
  COLLECTION_SIGN: "collection.sign",
  SOA_APPROVE: "designer.soa.approve",
});

/**
 * Validates that a step-up `action` path conforms to `SignatureTokenEndpoints.IsValidActionPath`:
 * starts with `/api/v1/`, length `<= 512`, no whitespace, and no `..` traversal segment.
 *
 * @param {string|null|undefined} action
 * @returns {boolean}
 */
export function isValidStepUpAction(action) {
  if (typeof action !== "string") {
    return false;
  }
  if (action.length === 0 || action.length > STEP_UP_ACTION_MAX_LENGTH) {
    return false;
  }
  if (!action.startsWith("/api/v1/")) {
    return false;
  }
  if (/\s/.test(action) || action.includes("..")) {
    return false;
  }
  return true;
}

/**
 * Validates that a step-up `semantic_action` conforms to the v3 dot-separated snake_case contract.
 *
 * @param {string|null|undefined} semanticAction
 * @returns {boolean}
 */
export function isValidSemanticAction(semanticAction) {
  if (typeof semanticAction !== "string") {
    return false;
  }
  return SEMANTIC_ACTION_PATTERN.test(semanticAction);
}

function createStepUpPayloadError(message) {
  const err = new Error(message);
  err.code = "INVALID_STEP_UP_PAYLOAD";
  return err;
}

/**
 * Builds and validates a Version 3 `/api/v1/auth/signature-verification` request body
 * per `docs/SDLC/Signature_Token_Cryptographic_Contract.md` and `SignatureTokenEndpoints.cs`.
 *
 * @param {{
 *   username?: string | null,
 *   password: string,
 *   totp?: string | null,
 *   action: string,
 *   semanticAction?: string,
 *   semantic_action?: string,
 *   batchId?: string | null,
 *   batch_id?: string | null,
 * }} input
 * @returns {{
 *   username?: string,
 *   password: string,
 *   totp?: string,
 *   action: string,
 *   semantic_action: string,
 *   batch_id?: string,
 * }}
 */
export function buildStepUpVerificationPayload(input = {}) {
  const candidate = input?.password;
  if (typeof candidate !== "string" || candidate.length === 0) {
    throw createStepUpPayloadError(
      "Step-up verification requires a non-empty password."
    );
  }

  const action = typeof input?.action === "string" ? input.action : "";
  if (!isValidStepUpAction(action)) {
    throw createStepUpPayloadError(
      "Step-up verification requires an action path starting with '/api/v1/' (max 512 chars, no whitespace or '..')."
    );
  }

  const rawSemantic = input?.semanticAction ?? input?.semantic_action ?? "";
  const semanticAction =
    typeof rawSemantic === "string" ? rawSemantic.trim() : "";
  if (!isValidSemanticAction(semanticAction)) {
    throw createStepUpPayloadError(
      "Step-up verification requires a valid semantic_action (e.g. 'ctms.doa.sign_off')."
    );
  }

  const payload = {
    password: candidate,
    action,
    semantic_action: semanticAction,
  };

  if (typeof input?.username === "string" && input.username.trim().length > 0) {
    payload.username = input.username.trim();
  }
  if (typeof input?.totp === "string" && input.totp.trim().length > 0) {
    payload.totp = input.totp.trim();
  }
  const rawBatchId = input?.batchId ?? input?.batch_id;
  if (typeof rawBatchId === "string" && rawBatchId.trim().length > 0) {
    payload.batch_id = rawBatchId.trim();
  }

  return payload;
}

/**
 * Formats the canonical target-record binding identifier (`batch_id` claim) for CGT bedside dual verification.
 * Matches `CgtBedsideVerificationRequest.TargetRecordBindingId()` (`{studyId}:{subjectId}:{din}:{batchId}`).
 *
 * @param {{ studyId?: string | null, subjectId?: string | null, din?: string | null, batchId?: string | null }} input
 * @returns {string}
 */
export function formatCgtBedsideBindingId(input = {}) {
  const studyId = String(input?.studyId ?? "").trim();
  const subjectId = String(input?.subjectId ?? "").trim();
  const din = String(input?.din ?? "").trim();
  const batchId = String(input?.batchId ?? "").trim();
  return `${studyId}:${subjectId}:${din}:${batchId}`;
}

/**
 * Formats the canonical target-record binding identifier (`batch_id` claim) for CGT destruction certification.
 * Matches `CgtDestructionCertificateCreateRequest.TargetRecordBindingId()` (`{studyId}:{siteId}:{lotNumber}:{din}`).
 *
 * @param {{ studyId?: string | null, siteId?: string | null, lotNumber?: string | null, din?: string | null }} input
 * @returns {string}
 */
export function formatCgtDestructionBindingId(input = {}) {
  const studyId = String(input?.studyId ?? "").trim();
  const siteId = String(input?.siteId ?? "").trim();
  const lotNumber = String(input?.lotNumber ?? "").trim();
  const din = String(input?.din ?? "").trim();
  return `${studyId}:${siteId}:${lotNumber}:${din}`;
}

/**
 * Canonical gateway path for 21 CFR Part 11 step-up signature verification.
 */
export const STEP_UP_VERIFICATION_PATH = "/api/v1/auth/signature-verification";

/**
 * Resolves the canonical default `semantic_action` for a step-up `action` path
 * when the caller does not pass an explicit `semanticAction`.
 *
 * @param {string | null | undefined} action
 * @returns {string}
 */
export function resolveDefaultSemanticAction(action = "") {
  const normalized = typeof action === "string" ? action : "";
  if (normalized.includes("reconsent")) {
    return SEMANTIC_ACTIONS.ECONSENT_RECONSENT_SIGN;
  }
  if (normalized.includes("capture-consent")) {
    return SEMANTIC_ACTIONS.ECONSENT_CAPTURE;
  }
  if (normalized.includes("doa/sign-off")) {
    return SEMANTIC_ACTIONS.CTMS_DOA_SIGN_OFF;
  }
  if (normalized.includes("sign-off")) {
    return SEMANTIC_ACTIONS.ETMF_DOCUMENT_SIGN_OFF;
  }
  if (normalized.includes("chair-adjudicate")) {
    return SEMANTIC_ACTIONS.CEC_CHAIR_ADJUDICATE;
  }
  if (normalized.includes("bedside-verify")) {
    return SEMANTIC_ACTIONS.CGT_BEDSIDE_VERIFY;
  }
  if (normalized.includes("destruction-certificate")) {
    return SEMANTIC_ACTIONS.CGT_DESTRUCTION_CERTIFICATE;
  }
  if (normalized.includes("/forms/")) {
    return SEMANTIC_ACTIONS.COLLECTION_SIGN;
  }
  if (normalized.includes("/soa/")) {
    return SEMANTIC_ACTIONS.SOA_APPROVE;
  }
  return "execution.signature.verify";
}

/**
 * Builds a validated v3 step-up verification payload, dispatches it to
 * `/api/v1/auth/signature-verification` via the caller-provided `post` transport,
 * and validates the returned `sig_token` unless `requireSigToken` is `false`.
 *
 * @param {{
 *   username?: string | null,
 *   password?: string,
 *   totp?: string | null,
 *   action: string,
 *   semanticAction?: string,
 *   semantic_action?: string,
 *   batchId?: string | null,
 *   batch_id?: string | null,
 * }} input
 * @param {{
 *   post: (path: string, body: Record<string, unknown>) => Promise<any>,
 *   requireSigToken?: boolean,
 *   missingTokenMessage?: string,
 * }} options
 * @returns {Promise<any>}
 */
export async function requestStepUpSignature(input = {}, options = {}) {
  const {
    post,
    requireSigToken = true,
    missingTokenMessage = "Signature verification did not return a signature token.",
  } = options || {};

  if (typeof post !== "function") {
    throw new TypeError(
      "requestStepUpSignature requires an options.post(path, body) transport function."
    );
  }

  const rawSemantic = input?.semanticAction ?? input?.semantic_action;
  const semanticAction =
    typeof rawSemantic === "string" && rawSemantic.trim().length > 0
      ? rawSemantic.trim()
      : resolveDefaultSemanticAction(input?.action);

  const body = buildStepUpVerificationPayload({
    ...input,
    password: input?.password ?? "",
    semanticAction,
  });

  const response = await post(STEP_UP_VERIFICATION_PATH, body);

  if (requireSigToken !== false) {
    const sigToken = response?.sig_token;
    if (typeof sigToken !== "string" || sigToken.length === 0) {
      throw new Error(missingTokenMessage);
    }
  }

  return response;
}

/**
 * Controlled signing reason sentinel requiring free-text specification.
 */
export const SIGNATURE_OTHER_REASON = "Other";

/**
 * Shared HTML input attributes for 21 CFR Part 11 re-authentication password fields.
 * Uses alphanumeric `inputmode="text"` so mobile virtual keyboards never lock out
 * Keycloak account passwords with digits-only keypads.
 */
export const SIGNATURE_PASSWORD_INPUT_ATTRS = Object.freeze({
  type: "password",
  inputmode: "text",
  autocomplete: "current-password",
  autocapitalize: "off",
  autocorrect: "off",
  spellcheck: "false",
});

/**
 * Returns true when the selected controlled signing reason requires custom text.
 *
 * @param {string|null|undefined} selectedReason
 * @returns {boolean}
 */
export function requiresCustomSignatureReason(selectedReason) {
  return (
    typeof selectedReason === "string" &&
    selectedReason.trim() === SIGNATURE_OTHER_REASON
  );
}

/**
 * Validates and resolves the effective 21 CFR Part 11 signing reason, rejecting
 * blank or whitespace-only custom explanations when `"Other"` is selected.
 *
 * @param {string|null|undefined} selectedReason
 * @param {string|null|undefined} [customReason=""]
 * @returns {{ valid: boolean, reason: string, error: string | null }}
 */
export function resolveSignatureReason(selectedReason, customReason = "") {
  const normalizedSelected =
    typeof selectedReason === "string" ? selectedReason.trim() : "";
  if (!normalizedSelected) {
    return {
      valid: false,
      reason: "",
      error: "Please select a signing reason.",
    };
  }

  if (normalizedSelected === SIGNATURE_OTHER_REASON) {
    const normalizedCustom =
      typeof customReason === "string" ? customReason.trim() : "";
    if (!normalizedCustom) {
      return {
        valid: false,
        reason: "",
        error:
          "Please specify a custom signing reason when 'Other' is selected.",
      };
    }
    return {
      valid: true,
      reason: normalizedCustom,
      error: null,
    };
  }

  return {
    valid: true,
    reason: normalizedSelected,
    error: null,
  };
}

const CLAIM_TO_SIGNER_ROLE_PRIORITY = [
  {
    matches: ["pi", "principal_investigator", "principal investigator"],
    signerRole: "PI",
  },
  {
    matches: ["site_investigator", "site investigator", "investigator"],
    signerRole: "Investigator",
  },
  {
    matches: [
      "crc",
      "site_crc",
      "site crc",
      "clinical_research_coordinator",
      "clinical research coordinator",
    ],
    signerRole: "Site CRC",
  },
  {
    matches: [
      "sponsor_designer",
      "sponsor designer",
      "study_designer",
      "study designer",
      "designer",
      "sponsor_admin",
      "sponsor admin",
    ],
    signerRole: "Sponsor Designer",
  },
  {
    matches: ["data_manager", "data manager"],
    signerRole: "Data Manager",
  },
  {
    matches: ["auditor", "cra", "monitor"],
    signerRole: "Auditor",
  },
  {
    matches: ["subject", "participant"],
    signerRole: "Subject",
  },
];

function extractRoleStrings(claimsOrRoles) {
  if (!claimsOrRoles) {
    return [];
  }
  if (Array.isArray(claimsOrRoles)) {
    return claimsOrRoles
      .map((r) =>
        String(r || "")
          .trim()
          .toLowerCase()
      )
      .filter(Boolean);
  }
  if (typeof claimsOrRoles === "string") {
    return claimsOrRoles
      .split(",")
      .map((r) => r.trim().toLowerCase())
      .filter(Boolean);
  }
  if (typeof claimsOrRoles === "object") {
    const collected = [];
    if (Array.isArray(claimsOrRoles.roles)) {
      collected.push(...claimsOrRoles.roles);
    } else if (typeof claimsOrRoles.roles === "string") {
      collected.push(...claimsOrRoles.roles.split(","));
    }
    if (Array.isArray(claimsOrRoles.realm_access?.roles)) {
      collected.push(...claimsOrRoles.realm_access.roles);
    }
    if (typeof claimsOrRoles.role === "string") {
      collected.push(claimsOrRoles.role);
    }
    return collected
      .map((r) =>
        String(r || "")
          .trim()
          .toLowerCase()
      )
      .filter(Boolean);
  }
  return [];
}

/**
 * Derives the default `SignatureCaptureModal` signer role option from OIDC role claims
 * so authenticated clinicians do not default to `"Subject"`.
 *
 * @param {string[] | string | Record<string, any> | null | undefined} claimsOrRoles
 * @param {string} [fallback="Subject"]
 * @returns {string}
 */
export function deriveSignerRoleFromClaims(
  claimsOrRoles,
  fallback = "Subject"
) {
  const normalizedRoles = extractRoleStrings(claimsOrRoles);
  if (normalizedRoles.length === 0) {
    return fallback;
  }

  const usernameHint =
    claimsOrRoles &&
    typeof claimsOrRoles === "object" &&
    !Array.isArray(claimsOrRoles)
      ? String(claimsOrRoles.username || claimsOrRoles.preferred_username || "")
          .trim()
          .toLowerCase()
      : "";

  if (
    (usernameHint === "pi" || usernameHint.startsWith("pi.")) &&
    normalizedRoles.some((r) =>
      [
        "pi",
        "principal_investigator",
        "principal investigator",
        "site_investigator",
        "site investigator",
        "investigator",
      ].includes(r)
    )
  ) {
    return "PI";
  }

  for (const mapping of CLAIM_TO_SIGNER_ROLE_PRIORITY) {
    if (
      mapping.matches.some((candidate) => normalizedRoles.includes(candidate))
    ) {
      return mapping.signerRole;
    }
  }

  return fallback;
}
