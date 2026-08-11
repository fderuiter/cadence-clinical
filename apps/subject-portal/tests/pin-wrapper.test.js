import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, vi } from "vitest";
import { deriveKeyFromPIN } from "ui";
import {
  state,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
  checkPINWrapper,
} from "../index.js";
import {
  clearSessionKey,
  clearInMemoryKey,
  getInMemorySessionKey,
  setInMemorySessionKey,
  getWrappedMasterKeyConfig,
  saveWrappedMasterKeyConfig,
  openDatabase,
} from "../sync-queue.js";

describe("Persistent Local PIN Wrapper Cryptographic & Flow Tests", () => {
  beforeEach(async () => {
    // Reset memory and mock inputs
    clearInMemoryKey();
    clearSessionKey();
    state.pinSetup.isOpen = false;
    state.pinSetup.pin = "";
    state.pinSetup.confirmPin = "";
    state.pinSetup.error = "";

    state.pinUnlock.isOpen = false;
    state.pinUnlock.pin = "";
    state.pinUnlock.error = "";

    state.session.token = "mock-token";
    state.session.isOfflineMode = false;

    // Clear indexedDB config
    const db = await openDatabase();
    await new Promise((resolve) => {
      const tx = db.transaction("config", "readwrite");
      tx.objectStore("config").clear();
      tx.oncomplete = resolve;
    });

    // Mock JSDOM inputs
    document.body.innerHTML = `
      <div id="app">
        <input id="setup-pin" type="password" />
        <input id="confirm-setup-pin" type="password" />
        <input id="unlock-pin" type="password" />
      </div>
    `;
  });

  it("should derive KWK from PIN using PBKDF2 with high iterations", async () => {
    const pin = "1234";
    const salt1 = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8]);
    const salt2 = new Uint8Array([8, 7, 6, 5, 4, 3, 2, 1]);

    const kwk1 = await deriveKeyFromPIN(pin, salt1);
    const kwk2 = await deriveKeyFromPIN(pin, salt2);

    expect(kwk1).toBeInstanceOf(Uint8Array);
    expect(kwk1.length).toBe(32); // 256 bits
    expect(kwk2.length).toBe(32);

    // Different salts must derive different keys
    expect(Array.from(kwk1)).not.toEqual(Array.from(kwk2));
  });

  it("should reject invalid, empty, or mismatched PIN during setup", async () => {
    // 1. Mismatched PINs
    state.pinSetup.pin = "1234";
    state.pinSetup.confirmPin = "5678";
    await handlePINSetupSubmit();
    expect(state.pinSetup.error).toBe("PINs do not match.");
    expect(getInMemorySessionKey()).toBeNull();

    // 2. Empty fields
    state.pinSetup.pin = "";
    state.pinSetup.confirmPin = "";
    await handlePINSetupSubmit();
    expect(state.pinSetup.error).toBe("Please fill in both PIN fields.");

    // 3. Non-numeric PIN
    state.pinSetup.pin = "abcd";
    state.pinSetup.confirmPin = "abcd";
    await handlePINSetupSubmit();
    expect(state.pinSetup.error).toBe("PIN must be numeric-only.");
  });

  it("should securely wrap generated master key during valid setup and clear UI/memory", async () => {
    state.pinSetup.pin = "123456";
    state.pinSetup.confirmPin = "123456";

    await handlePINSetupSubmit();

    expect(state.pinSetup.error).toBe("");
    expect(state.pinSetup.isOpen).toBe(false);

    // Verify key in active memory
    const activeKey = getInMemorySessionKey();
    expect(activeKey).toBeInstanceOf(Uint8Array);
    expect(activeKey.length).toBe(32);

    // Verify UI / Reactivity credential hygiene
    expect(state.pinSetup.pin).toBe("");
    expect(state.pinSetup.confirmPin).toBe("");

    // Verify IndexedDB has wrapped key & salt
    const config = await getWrappedMasterKeyConfig();
    expect(config.wrappedKey).toBeDefined();
    expect(config.wrappedKey).not.toBeNull();
    expect(config.salt).toBeDefined();
    expect(config.salt).not.toBeNull();
  });

  it("should successfully decrypt wrapped key with valid PIN", async () => {
    // Setup PIN
    state.pinSetup.pin = "9999";
    state.pinSetup.confirmPin = "9999";
    await handlePINSetupSubmit();

    const originalKey = getInMemorySessionKey();
    expect(originalKey).not.toBeNull();

    // Reset memory
    clearInMemoryKey();
    expect(getInMemorySessionKey()).toBeNull();

    // Simulate unlock flow
    state.pinUnlock.pin = "9999";
    await handlePINUnlockSubmit();

    expect(state.pinUnlock.error).toBe("");
    expect(state.pinUnlock.isOpen).toBe(false);

    const unlockedKey = getInMemorySessionKey();
    expect(unlockedKey).not.toBeNull();
    expect(Array.from(unlockedKey)).toEqual(Array.from(originalKey));

    // Verify credential hygiene (wiped PIN from state)
    expect(state.pinUnlock.pin).toBe("");
  });

  it("should return explicit decryption failure and block access with invalid PIN", async () => {
    // Setup PIN
    state.pinSetup.pin = "9999";
    state.pinSetup.confirmPin = "9999";
    await handlePINSetupSubmit();

    // Clear memory
    clearInMemoryKey();

    // Simulate unlock with incorrect PIN
    state.pinUnlock.pin = "1111";
    await handlePINUnlockSubmit();

    expect(state.pinUnlock.error).toBe("Incorrect security PIN. Access denied.");
    expect(getInMemorySessionKey()).toBeNull();

    // Field must be cleared
    expect(state.pinUnlock.pin).toBe("");
  });
});
