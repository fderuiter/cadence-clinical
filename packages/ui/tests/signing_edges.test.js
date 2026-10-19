import crypto from "node:crypto";
import { describe, it, expect } from "vitest";
import {
  generateJwtHS256,
  deriveKeyFromPIN,
  encryptAESGCM,
  decryptAESGCM,
  validateField,
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
