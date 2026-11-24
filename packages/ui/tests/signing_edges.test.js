import crypto from "node:crypto";
import { describe, it, expect } from "vitest";
import {
  generateJwtHS256,
  deriveKeyFromPIN,
  encryptAESGCM,
  decryptAESGCM,
  validateField,
  SEMANTIC_ACTIONS,
  isValidSemanticAction,
  buildStepUpVerificationPayload,
} from "../signing.js";

// Edge cases of the Part 11 signing helpers, checked against node:crypto as an independent reference (#5335).

const TEST_KEY = "signing-edges-test-key"; // pragma: allowlist secret

function base64url(buffer) {
  return Buffer.from(buffer)
    .toString("base64")
    .replace(/=/g, "")
    .replace(/\+/g, "-")
    .replace(/\//g, "_");
}

describe("generateJwtHS256", () => {
  it("produces an HS256 token whose signature node:crypto reproduces", async () => {
    const payload = { sub: "usr-001", purpose: "sign", jti: "a1" };
    const token = await generateJwtHS256(payload, TEST_KEY);
    const [header, body, signature] = token.split(".");

    expect(JSON.parse(Buffer.from(header, "base64url").toString())).toEqual({
      alg: "HS256",
      typ: "JWT",
    });
    expect(JSON.parse(Buffer.from(body, "base64url").toString())).toEqual(
      payload
    );
    const expected = base64url(
      crypto.createHmac("sha256", TEST_KEY).update(`${header}.${body}`).digest()
    );
    expect(signature).toBe(expected);
  });

  it("accepts the key as bytes and signs the same as the string form", async () => {
    const payload = { sub: "usr-002" };
    const fromString = await generateJwtHS256(payload, TEST_KEY);
    const fromBytes = await generateJwtHS256(
      payload,
      new TextEncoder().encode(TEST_KEY)
    );
    expect(fromBytes).toBe(fromString);
  });
});

describe("deriveKeyFromPIN", () => {
  it("matches PBKDF2-HMAC-SHA256 from node:crypto", async () => {
    const salt = new Uint8Array(16).fill(7);
    const derived = await deriveKeyFromPIN("135790", salt, 1000);
    const expected = crypto.pbkdf2Sync(
      "135790",
      Buffer.from(salt),
      1000,
      32,
      "sha256"
    );
    expect(Buffer.from(derived).equals(expected)).toBe(true);
  });

  it("derives different keys for different PINs", async () => {
    const salt = new Uint8Array(16).fill(1);
    const a = await deriveKeyFromPIN("111111", salt, 1000);
    const b = await deriveKeyFromPIN("111112", salt, 1000);
    expect(Buffer.from(a).equals(Buffer.from(b))).toBe(false);
  });
});

describe("decryptAESGCM rejects malformed envelopes", () => {
  const key = new Uint8Array(32).fill(3);

  it("rejects text that is not base64", async () => {
    await expect(decryptAESGCM("%%%not-base64%%%", key)).rejects.toThrow(
      "Invalid base64 payload"
    );
  });

  it("rejects an envelope shorter than its header", async () => {
    await expect(
      decryptAESGCM(Buffer.from([0, 0, 0, 1]).toString("base64"), key)
    ).rejects.toThrow("payload too short");
  });

  it("rejects an envelope with another version marker", async () => {
    const envelope = await encryptAESGCM({ a: 1 }, key, 2);
    await expect(decryptAESGCM(envelope, key, 1)).rejects.toThrow(
      "Unrecognized version marker: 2"
    );
  });

  it("rejects an envelope sealed under another key", async () => {
    const envelope = await encryptAESGCM({ a: 1 }, key);
    await expect(
      decryptAESGCM(envelope, new Uint8Array(32).fill(4))
    ).rejects.toThrow("Decryption failed");
  });

  it("rejects a ciphertext that does not hold JSON", async () => {
    const iv = new Uint8Array(12).fill(9);
    const cipher = crypto.createCipheriv("aes-256-gcm", Buffer.from(key), iv);
    const body = Buffer.concat([cipher.update("not json"), cipher.final()]);
    const envelope = Buffer.concat([
      Buffer.from([0, 0, 0, 1]),
      iv,
      body,
      cipher.getAuthTag(),
    ]).toString("base64");
    await expect(decryptAESGCM(envelope, key)).rejects.toThrow(
      "Deserialization failed"
    );
  });

  it("round-trips with a CryptoKey and authenticated data", async () => {
    const cryptoKey = await globalThis.crypto.subtle.importKey(
      "raw",
      key,
      { name: "AES-GCM" },
      false,
      ["encrypt", "decrypt"]
    );
    const aad = new TextEncoder().encode("participant-001");
    const envelope = await encryptAESGCM({ dose: 5 }, cryptoKey, 1, aad);
    await expect(decryptAESGCM(envelope, cryptoKey, 1, aad)).resolves.toEqual({
      dose: 5,
    });
  });
});

describe("validateField", () => {
  const field = (validation, extra = {}) => ({ validation, ...extra });

  it("passes when there is no field metadata", () => {
    expect(validateField(null, "x")).toEqual({ valid: true });
  });

  it("requires a value when the field is required", () => {
    for (const empty of [undefined, null, "", "   "]) {
      expect(validateField(field({ required: true }), empty)).toEqual({
        valid: false,
        message: "This field is required.",
      });
    }
  });

  it("applies the pattern with its own message or a default", () => {
    expect(
      validateField(
        field({ pattern: "^[A-Z]{3}$", message: "Three letters" }),
        "ab"
      )
    ).toEqual({ valid: false, message: "Three letters" });
    expect(validateField(field({ pattern: "^[A-Z]{3}$" }), "ab")).toEqual({
      valid: false,
      message: "Invalid format.",
    });
    expect(validateField(field({ pattern: "^[A-Z]{3}$" }), "ABC")).toEqual({
      valid: true,
    });
  });

  it("checks numeric bounds", () => {
    const bounds = field({ min: 10, max: 20 });
    expect(validateField(bounds, "abc")).toEqual({
      valid: false,
      message: "Value must be a number.",
    });
    expect(validateField(bounds, "9")).toEqual({
      valid: false,
      message: "Minimum value is 10.",
    });
    expect(validateField(bounds, "21")).toEqual({
      valid: false,
      message: "Maximum value is 20.",
    });
    expect(validateField(bounds, "15")).toEqual({ valid: true });
    expect(validateField(field({ max: 5, message: "Too high" }), 6)).toEqual({
      valid: false,
      message: "Too high",
    });
  });

  it("skips format and range checks on an empty optional value", () => {
    expect(validateField(field({ pattern: "^x$", min: 1 }), "")).toEqual({
      valid: true,
    });
  });
});

describe("SEMANTIC_ACTIONS and step-up batch_id binding", () => {
  it("keeps every canonical semantic_action compliant with the v3 grammar", () => {
    expect(SEMANTIC_ACTIONS.CTMS_DOA_EXPORT_SIGNED_PDF).toBe(
      "ctms.doa.export_signed_pdf"
    );
    expect(SEMANTIC_ACTIONS.CTMS_GREENLIGHT_CERTIFY).toBe(
      "ctms.greenlight.certify"
    );
    expect(SEMANTIC_ACTIONS.CTMS_GREENLIGHT_BATCH_CERTIFY).toBe(
      "ctms.greenlight.batch_certify"
    );
    expect(SEMANTIC_ACTIONS.CTMS_MONITORING_VISIT_SIGN_OFF).toBe(
      "ctms.monitoring_visit.sign_off"
    );
    expect(SEMANTIC_ACTIONS.CTMS_LETTER_FINALIZE).toBe("ctms.letter.finalize");
    expect(SEMANTIC_ACTIONS.EXECUTION_SUBJECT_UNBLIND).toBe(
      "execution.subject.unblind"
    );

    for (const value of Object.values(SEMANTIC_ACTIONS)) {
      expect(isValidSemanticAction(value)).toBe(true);
    }
  });

  it("includes trimmed batch_id in step-up verification payloads when batchId is provided", () => {
    const payload = buildStepUpVerificationPayload({
      password: "Password123!", // pragma: allowlist secret
      action: "/api/v1/ctms/greenlight/batch-certify",
      semanticAction: SEMANTIC_ACTIONS.CTMS_GREENLIGHT_BATCH_CERTIFY,
      batchId:
        "  9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08  ",
    });

    expect(payload).toEqual({
      password: "Password123!", // pragma: allowlist secret
      action: "/api/v1/ctms/greenlight/batch-certify",
      semantic_action: "ctms.greenlight.batch_certify",
      batch_id:
        "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
    });
  });

  it("omits batch_id when batchId is null or blank", () => {
    const payload = buildStepUpVerificationPayload({
      password: "Password123!", // pragma: allowlist secret
      action: "/api/v1/execution/subjects/sub-001/unblind",
      semanticAction: SEMANTIC_ACTIONS.EXECUTION_SUBJECT_UNBLIND,
      batchId: "   ",
    });

    expect(payload).toEqual({
      password: "Password123!", // pragma: allowlist secret
      action: "/api/v1/execution/subjects/sub-001/unblind",
      semantic_action: "execution.subject.unblind",
    });
  });
});
