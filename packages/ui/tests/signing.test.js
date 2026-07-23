import { describe, it, expect } from "vitest";
import {
  canonicalSerialize,
  generateCanonicalSignature,
  verifyCanonicalSignature,
  generateGatewaySignature,
  verifyGatewaySignature,
} from "../index.js";

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

  it("generates correct Version 1 signature matching Python", async () => {
    const expectedV1 =
      "c5e5a9640f9c8aa044267bcc24cf0043938e5a95743755383f95fcec7f5458ad"; // pragma: allowlist secret
    const sig = await generateGatewaySignature(
      userId,
      roles,
      timestamp,
      "1",
      null,
      secret
    );
    expect(sig).toBe(expectedV1);

    const isValid = await verifyGatewaySignature(
      expectedV1,
      userId,
      roles,
      timestamp,
      "1",
      null,
      secret
    );
    expect(isValid).toBe(true);
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

  it("ensures Version 1 and Version 2 signatures differ", async () => {
    const sigV1 = await generateGatewaySignature(
      userId,
      roles,
      timestamp,
      "1",
      null,
      secret
    );
    const sigV2 = await generateGatewaySignature(
      userId,
      roles,
      timestamp,
      "2",
      null,
      secret
    );
    expect(sigV1).not.toBe(sigV2);
  });
});
