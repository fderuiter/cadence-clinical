import crypto from "node:crypto";
import { describe, it, expect } from "vitest";
import {
  canonicalSerialize,
  generateCanonicalSignature,
  generateGatewaySignature,
  verifyGatewaySignature,
  sha256,
  validateField,
  buildLedgerBlock,
} from "../index.js";
import { encryptAESGCM, decryptAESGCM, deriveSessionKey } from "../signing.js";

// Verification tests for Version 2 cryptographic signature payload structures.
describe("canonicalSerialize", () => {
  it("serializes primitives identically to Python", () => {
    expect(canonicalSerialize("test")).toBe('"test"');
    expect(canonicalSerialize(123)).toBe("123");
    expect(canonicalSerialize(true)).toBe("true");
    expect(canonicalSerialize(null)).toBe("null");
  });

  it("serializes arrays of elements", () => {
    expect(canonicalSerialize([1, "two", { b: 2, a: 1 }])).toBe(
      '[1,"two",{"a":1,"b":2}]'
    );
  });

  it("serializes nested objects sorting keys alphabetically", () => {
    const obj1 = { b: "hello", c: 42, a: true };
    const obj2 = { a: true, c: 42, b: "hello" };
    const expected = '{"a":true,"b":"hello","c":42}';

    expect(canonicalSerialize(obj1)).toBe(expected);
    expect(canonicalSerialize(obj2)).toBe(expected);
  });

  it("handles deep nesting of objects and arrays", () => {
    const payload = { c: [1, 2, { y: "z", x: "w" }], b: "hello", a: 1 };
    const expected = '{"a":1,"b":"hello","c":[1,2,{"x":"w","y":"z"}]}';
    expect(canonicalSerialize(payload)).toBe(expected);
  });
});

describe("generateCanonicalSignature", () => {
  it("matches Python generate_canonical_signature output exactly", async () => {
    const payload = { a: 1, b: "hello", c: [1, 2, { x: "y" }] };
    const secret = "my-test-secret"; // pragma: allowlist secret
    const expectedSig =
      "a5acdf4504e338fce8ede5b65cb5be3c972692fb0dd797cc0cff8e88d35fa2d2"; // pragma: allowlist secret

    const sig = await generateCanonicalSignature(payload, secret);
    expect(sig).toBe(expectedSig);

    const bytesSig = await generateCanonicalSignature(
      payload,
      new TextEncoder().encode(secret)
    );
    expect(bytesSig).toBe(expectedSig);
  });
});

describe("generateGatewaySignature and verifyGatewaySignature", () => {
  const secret = "internal-gateway-secret-12345"; // pragma: allowlist secret
  const userId = "user1";
  const roles = "admin";
  const timestamp = "123456";

  it("generates correct Version 2 signature matching Python", async () => {
    const expectedV2 =
      "0c66fa2bdfc9792e0c3bb45337d9c1e87be8a72f37f68e8f3998f88f45f5b1f3"; // pragma: allowlist secret
    const changeReason = "Clinical reason for test";

    const sig = await generateGatewaySignature(
      userId,
      roles,
      timestamp,
      "2",
      changeReason,
      secret
    );
    expect(sig).toBe(expectedV2);

    const isValid = await verifyGatewaySignature(
      expectedV2,
      userId,
      roles,
      timestamp,
      "2",
      changeReason,
      secret
    );
    expect(isValid).toBe(true);
  });

  it("treats null or undefined changeReason as empty string in Version 2", async () => {
    const sigWithNull = await generateGatewaySignature(
      userId,
      roles,
      timestamp,
      "2",
      null,
      secret
    );
    const sigWithEmpty = await generateGatewaySignature(
      userId,
      roles,
      timestamp,
      "2",
      "",
      secret
    );
    expect(sigWithNull).toBe(sigWithEmpty);
  });

  it("rejects unsupported versions", async () => {
    await expect(
      generateGatewaySignature(userId, roles, timestamp, "3", null, secret)
    ).rejects.toThrow("Version 2 signature is required.");

    const isValid = await verifyGatewaySignature(
      "some-signature",
      userId,
      roles,
      timestamp,
      "3",
      null,
      secret
    );
    expect(isValid).toBe(false);
  });
});

describe("sha256", () => {
  it("computes a correct SHA-256 hex digest for empty string", async () => {
    const hash = await sha256("");
    expect(hash).toBe(
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" // pragma: allowlist secret
    );
  });

  it("computes a correct SHA-256 hex digest for a known test message", async () => {
    const hash = await sha256("Cadence Clinical Platform");
    expect(hash).toBe(
      "5789dd8ff2e10b9b13b3365112bf8b66027e43c59ae06110150617571c12f9a2" // pragma: allowlist secret
    );
  });
});

// Envelope format parity with the retired Python reference (packages/security/encryption.py).
// These are frozen known-answer vectors captured from that module, so the tests no longer
// shell out to a Python interpreter. The JS -> other direction is checked against Node's own
// crypto implementation, which is independent of WebCrypto's envelope handling in signing.js.
describe("envelope format parity (known-answer vectors)", () => {
  // Captured from encryption.encrypt({"hello": "world", "count": 42}, bytes(range(32)), 1,
  // b"my_aad_data") with os.urandom patched to return bytes(range(100, 112)) as the nonce.
  const PYTHON_ENVELOPE =
    "AAAAAWRlZmdoaWprbG1ubzM5vQkMhyK8BFZtxPgND5EurSQwqRucAMu1jjW7ZevBCA2O52kx0C05XFR1"; // pragma: allowlist secret
  // Captured from encryption.derive_session_key(b"session_material_abc", b"salt_123", b"info_456").
  const PYTHON_HKDF_HEX =
    "697e50afc31edfdf1951156b913b1f542d73a5b55e00d78e2a0870ba7b1caa1e"; // pragma: allowlist secret

  const keyFrom = (offset) => {
    const key = new Uint8Array(32);
    for (let i = 0; i < 32; i++) key[i] = i + offset;
    return key;
  };

  it("JS can decrypt a Python-produced AES-GCM envelope", async () => {
    const decrypted = await decryptAESGCM(
      PYTHON_ENVELOPE,
      keyFrom(0),
      1,
      new TextEncoder().encode("my_aad_data")
    );
    expect(decrypted).toEqual({ hello: "world", count: 42 });
  });

  it("rejects the Python-produced envelope when the AAD differs", async () => {
    await expect(
      decryptAESGCM(
        PYTHON_ENVELOPE,
        keyFrom(0),
        1,
        new TextEncoder().encode("other_aad")
      )
    ).rejects.toThrow("Decryption failed");
  });

  it("JS envelope is version(4B BE) || nonce(12B) || ciphertext || tag(16B)", async () => {
    const rawKey = keyFrom(10);
    const payload = {
      msg: "from javascript to python with love",
      success: true,
    };
    const aad = Buffer.from("another_aad", "utf8");

    const jsEnvelope = await encryptAESGCM(
      payload,
      rawKey,
      1,
      new Uint8Array(aad)
    );

    const packed = Buffer.from(jsEnvelope, "base64");
    expect(packed.readUInt32BE(0)).toBe(1);
    const nonce = packed.subarray(4, 16);
    const ciphertext = packed.subarray(16, packed.length - 16);
    const tag = packed.subarray(packed.length - 16);

    const decipher = crypto.createDecipheriv("aes-256-gcm", rawKey, nonce);
    decipher.setAAD(aad);
    decipher.setAuthTag(tag);
    const plaintext = Buffer.concat([
      decipher.update(ciphertext),
      decipher.final(),
    ]).toString("utf8");

    expect(plaintext).toBe(canonicalSerialize(payload));
    expect(JSON.parse(plaintext)).toEqual(payload);
  });

  it("HKDF key derivation matches Python exactly", async () => {
    const jsDerived = await deriveSessionKey(
      "session_material_abc",
      "salt_123",
      "info_456"
    );
    expect(Buffer.from(jsDerived).toString("hex")).toBe(PYTHON_HKDF_HEX);
  });

  it("HKDF matches RFC 5869 test case 1", async () => {
    // RFC 5869 A.1 truncated to the 32-byte output deriveSessionKey produces.
    const ikm = new Uint8Array(22).fill(0x0b);
    const salt = Uint8Array.from(
      Buffer.from("000102030405060708090a0b0c", "hex")
    );
    const info = Uint8Array.from(Buffer.from("f0f1f2f3f4f5f6f7f8f9", "hex"));
    const okm = await deriveSessionKey(ikm, salt, info);
    expect(Buffer.from(okm).toString("hex")).toBe(
      "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf" // pragma: allowlist secret
    );
  });
});

describe("canonicalSerialize - RFC 8785 Hardening", () => {
  it("serializes Date objects to their ISO string format deterministically", () => {
    const d = new Date("2026-08-03T12:00:00.000Z");
    expect(canonicalSerialize(d)).toBe('"2026-08-03T12:00:00.000Z"');

    const obj = { timestamp: d };
    expect(canonicalSerialize(obj)).toBe(
      '{"timestamp":"2026-08-03T12:00:00.000Z"}'
    );
  });

  it("omits undefined properties entirely inside objects", () => {
    const obj = {
      a: 1,
      b: undefined,
      c: "test",
      d: undefined,
    };
    expect(canonicalSerialize(obj)).toBe('{"a":1,"c":"test"}');
  });

  it("serializes undefined elements inside arrays to null", () => {
    const arr = [1, undefined, "test", undefined, { x: undefined, y: 2 }];
    expect(canonicalSerialize(arr)).toBe('[1,null,"test",null,{"y":2}]');
  });

  it("safely serializes root undefined to undefined", () => {
    expect(canonicalSerialize(undefined)).toBeUndefined();
  });
});

describe("validateField - Falsy Constraint Checking", () => {
  const evaluateAST = (expr, ctx) => {
    return ctx[expr];
  };

  it("blocks falsy evaluation results (false, null, undefined, 0, empty string)", () => {
    const fieldMeta = {
      constraint: {
        condition: "check_rule",
        query_message: "Falsy value not allowed!",
      },
    };

    // Strict false
    expect(
      validateField(fieldMeta, "some_val", { check_rule: false }, evaluateAST)
    ).toEqual({
      valid: false,
      message: "Falsy value not allowed!",
    });

    // null
    expect(
      validateField(fieldMeta, "some_val", { check_rule: null }, evaluateAST)
    ).toEqual({
      valid: false,
      message: "Falsy value not allowed!",
    });

    // undefined
    expect(
      validateField(
        fieldMeta,
        "some_val",
        { check_rule: undefined },
        evaluateAST
      )
    ).toEqual({
      valid: false,
      message: "Falsy value not allowed!",
    });

    // 0
    expect(
      validateField(fieldMeta, "some_val", { check_rule: 0 }, evaluateAST)
    ).toEqual({
      valid: false,
      message: "Falsy value not allowed!",
    });

    // empty string
    expect(
      validateField(fieldMeta, "some_val", { check_rule: "" }, evaluateAST)
    ).toEqual({
      valid: false,
      message: "Falsy value not allowed!",
    });

    // truthy value should pass
    expect(
      validateField(fieldMeta, "some_val", { check_rule: true }, evaluateAST)
    ).toEqual({
      valid: true,
    });
    expect(
      validateField(
        fieldMeta,
        "some_val",
        { check_rule: "is_valid" },
        evaluateAST
      )
    ).toEqual({
      valid: true,
    });
  });
});

describe("buildLedgerBlock - Hardened Signatures Parity", () => {
  it("formats details configurations canonically before hashing", async () => {
    const details1 = { b: 2, a: 1 };
    const details2 = { a: 1, b: 2 };

    const block1 = await buildLedgerBlock(
      1,
      "2026-08-03T12:00:00Z",
      "SUBMIT",
      details1,
      "Reason",
      "prev-hash-123"
    );
    const block2 = await buildLedgerBlock(
      1,
      "2026-08-03T12:00:00Z",
      "SUBMIT",
      details2,
      "Reason",
      "prev-hash-123"
    );

    expect(block1.hash).toBe(block2.hash);
    expect(block1.hash).toBeDefined();
  });
});
