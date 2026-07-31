/**
 * Web Crypto API Encryption Utility
 * Provides cryptographic key derivation using PBKDF2 and authenticated
 * encryption/decryption using AES-GCM 256-bit.
 * Compliant with 21 CFR Part 11 and PRD-SYS-001.
 */

const getCrypto = (): Crypto => {
  if (typeof window !== "undefined" && window.crypto) {
    return window.crypto;
  }
  return globalThis.crypto;
};

/**
 * Derives an AES-GCM 256-bit CryptoKey from a user PIN and salt using PBKDF2.
 * Key derivation uses 100,000 iterations and SHA-256 hash.
 *
 * @param pin - The user-supplied PIN string.
 * @param salt - A 16-byte cryptographic salt.
 * @returns A promise that resolves to the derived CryptoKey.
 */
export async function deriveKey(
  pin: string,
  salt: Uint8Array
): Promise<CryptoKey> {
  const cryptoObj = getCrypto();
  const encoder = new TextEncoder();
  const keyMaterial = await cryptoObj.subtle.importKey(
    "raw",
    encoder.encode(pin),
    { name: "PBKDF2" },
    false,
    ["deriveKey"]
  );

  return await cryptoObj.subtle.deriveKey(
    {
      name: "PBKDF2",
      salt,
      iterations: 100000,
      hash: "SHA-256",
    },
    keyMaterial,
    { name: "AES-GCM", length: 256 },
    false,
    ["encrypt", "decrypt"]
  );
}

/**
 * Encrypts an object payload with AES-GCM using the derived CryptoKey.
 * Generates a random 16-byte Initialization Vector (IV) if not provided.
 *
 * @param data - The object to encrypt.
 * @param key - The derived AES-GCM CryptoKey.
 * @param iv - Optional 16-byte Initialization Vector.
 * @returns A promise resolving to the ciphertext and the IV used.
 */
export async function encryptData(
  data: object,
  key: CryptoKey,
  iv?: Uint8Array
): Promise<{ ciphertext: ArrayBuffer; iv: Uint8Array }> {
  const cryptoObj = getCrypto();
  const encoder = new TextEncoder();
  // 16-byte random IV as specified in Step 2 requirements
  const finalIv = iv || cryptoObj.getRandomValues(new Uint8Array(16));
  const encodedData = encoder.encode(JSON.stringify(data));

  const ciphertext = await cryptoObj.subtle.encrypt(
    { name: "AES-GCM", iv: finalIv },
    key,
    encodedData
  );

  return {
    ciphertext,
    iv: finalIv,
  };
}

/**
 * Decrypts AES-GCM ciphertext back into the original object payload.
 *
 * @param ciphertext - The encrypted data ArrayBuffer.
 * @param key - The derived AES-GCM CryptoKey.
 * @param iv - The Initialization Vector (IV) used during encryption.
 * @returns A promise resolving to the decrypted object.
 */
export async function decryptData(
  ciphertext: ArrayBuffer,
  key: CryptoKey,
  iv: Uint8Array
): Promise<object> {
  const cryptoObj = getCrypto();
  const decrypted = await cryptoObj.subtle.decrypt(
    { name: "AES-GCM", iv },
    key,
    ciphertext
  );

  const decoder = new TextDecoder();
  return JSON.parse(decoder.decode(decrypted));
}
