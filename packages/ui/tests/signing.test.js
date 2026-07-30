import { execSync } from "child_process";
import { describe, it, expect } from "vitest";
import {
  canonicalSerialize,
  generateCanonicalSignature,
  verifyCanonicalSignature,
  generateGatewaySignature,
  verifyGatewaySignature,
  sha256,
} from "../index.js";
import { encryptAESGCM, decryptAESGCM, deriveSessionKey } from "../signing.js";

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

describe("generateCanonicalSignature and verifyCanonicalSignature", () => {
  it("matches Python generate_canonical_signature output exactly", async () => {
    const payload = { a: 1, b: "hello", c: [1, 2, { x: "y" }] };
    const secret = "my-test-secret"; // pragma: allowlist secret
    const expectedSig =
      "a5acdf4504e338fce8ede5b65cb5be3c972692fb0dd797cc0cff8e88d35fa2d2"; // pragma: allowlist secret

    const sig = await generateCanonicalSignature(payload, secret);
    expect(sig).toBe(expectedSig);

    const isValid = await verifyCanonicalSignature(
      payload,
      expectedSig,
      secret
    );
    expect(isValid).toBe(true);
  });

  it("returns false for tampered payload or signature", async () => {
    const payload = { a: 1, b: "hello", c: [1, 2, { x: "y" }] };
    const secret = "my-test-secret"; // pragma: allowlist secret
    const sig = await generateCanonicalSignature(payload, secret);

    const tamperedPayload = { a: 2, b: "hello", c: [1, 2, { x: "y" }] };
    const isValidPayload = await verifyCanonicalSignature(
      tamperedPayload,
      sig,
      secret
    );
    expect(isValidPayload).toBe(false);

    const isValidSig = await verifyCanonicalSignature(
      payload,
      sig + "a",
      secret
    );
    expect(isValidSig).toBe(false);
  });
});

describe("generateGatewaySignature and verifyGatewaySignature", () => {
  const secret = "internal-gateway-secret-12345"; // pragma: allowlist secret
  const userId = "user1";
  const roles = "admin";
  const timestamp = "123456";

  it("generates correct Version 1 signature", async () => {
    const sig = await generateGatewaySignature(
      userId,
      roles,
      timestamp,
      "1",
      null,
      secret
    );
    expect(sig).toBeDefined();

    const isValid = await verifyGatewaySignature(
      sig,
      userId,
      roles,
      timestamp,
      "1",
      null,
      secret
    );
    expect(isValid).toBe(true);
  });

  it("returns false for invalid Version 1 signature", async () => {
    const isValid = await verifyGatewaySignature(
      "invalid-signature",
      userId,
      roles,
      timestamp,
      "1",
      null,
      secret
    );
    expect(isValid).toBe(false);
  });

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
    ).rejects.toThrow("Version 1 or Version 2 signature is required.");

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

describe("cross-language parity", () => {
  const getPythonOutput = (script) => {
    const cwd = process.cwd();
    const env = { ...process.env, PYTHONPATH: cwd };
    const pyScript = script.trim().split("\n").map(line => line.trim()).filter(Boolean).join("; ");
    try {
      return execSync(`uv run python -c "${pyScript}"`, { env, cwd }).toString().trim();
    } catch {
      return execSync(`python3 -c "${pyScript}"`, { env, cwd }).toString().trim();
    }
  };

  it("JS can decrypt a Python-produced AES-GCM envelope", async () => {
    const rawKey = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawKey[i] = i;
    const hexKey = Array.from(rawKey)
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");

    const payload = { hello: "world", count: 42 };
    const aad = "my_aad_data";

    const pythonEnvelope = getPythonOutput(`
from packages.security.encryption import encrypt
key = bytes.fromhex('${hexKey}')
payload = {'hello': 'world', 'count': 42}
aad = '${aad}'.encode('utf-8')
print(encrypt(payload, key, 1, aad))
`);

    const decrypted = await decryptAESGCM(
      pythonEnvelope,
      rawKey,
      1,
      new TextEncoder().encode(aad)
    );
    expect(decrypted).toEqual(payload);
  });

  it("Python can decrypt a JS-produced AES-GCM envelope", async () => {
    const rawKey = new Uint8Array(32);
    for (let i = 0; i < 32; i++) rawKey[i] = i + 10;
    const hexKey = Array.from(rawKey)
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");

    const payload = {
      msg: "from javascript to python with love",
      success: true,
    };
    const aad = "another_aad";

    const jsEnvelope = await encryptAESGCM(
      payload,
      rawKey,
      1,
      new TextEncoder().encode(aad)
    );

    const pythonOutput = getPythonOutput(`
import json
from packages.security.encryption import decrypt
key = bytes.fromhex('${hexKey}')
aad = '${aad}'.encode('utf-8')
decrypted = decrypt('${jsEnvelope}', key, 1, aad)
print(json.dumps(decrypted))
`);
    const parsedOutput = JSON.parse(pythonOutput);
    expect(parsedOutput).toEqual(payload);
  });

  it("HKDF key derivation matches Python exactly", async () => {
    const material = "session_material_abc";
    const salt = "salt_123";
    const info = "info_456";

    const jsDerived = await deriveSessionKey(material, salt, info);
    const jsHex = Array.from(jsDerived)
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");

    const pythonHex = getPythonOutput(`
from packages.security.encryption import derive_session_key
derived = derive_session_key(b'${material}', b'${salt}', b'${info}')
print(derived.hex())
`);
    expect(jsHex).toBe(pythonHex);
  });
});
