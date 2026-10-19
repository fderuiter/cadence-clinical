// Local security PIN that wraps the offline database master key.
import { decryptAESGCM, deriveKeyFromPIN, encryptAESGCM } from "ui";
import { nextTick } from "vue";

import { isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import {
  getInMemorySessionKey,
  getWrappedMasterKeyConfig,
  saveWrappedMasterKeyConfig,
  setInMemorySessionKey,
} from "../sync-queue.js";

export async function checkPINWrapper() {
  if (getInMemorySessionKey()) {
    state.pinUnlock.isOpen = false;
    state.pinSetup.isOpen = false;
    return;
  }
  const config = await getWrappedMasterKeyConfig();
  if (config.wrappedKey) {
    if (!getInMemorySessionKey()) {
      state.pinUnlock.isOpen = true;
      nextTick(() => {
        const el = document.getElementById("unlock-pin");
        if (el) el.focus();
      });
    }
  } else {
    if (isAuthenticatedSession()) {
      state.pinSetup.isOpen = true;
      nextTick(() => {
        const el = document.getElementById("setup-pin");
        if (el) el.focus();
      });
    }
  }
}

export async function handlePINSetupSubmit() {
  const pin = state.pinSetup.pin;
  const confirmPin = state.pinSetup.confirmPin;

  if (!pin || !confirmPin) {
    state.pinSetup.error = "Please fill in both PIN fields.";
    return;
  }

  if (pin !== confirmPin) {
    state.pinSetup.error = "PINs do not match.";
    return;
  }

  if (!/^\d+$/.test(pin)) {
    state.pinSetup.error = "PIN must be numeric-only.";
    return;
  }

  try {
    const masterKey = globalThis.crypto.getRandomValues(new Uint8Array(32));
    const salt = globalThis.crypto.getRandomValues(new Uint8Array(16));
    const kwk = await deriveKeyFromPIN(pin, salt);
    const wrappedKeyStr = await encryptAESGCM(
      { masterKey: Array.from(masterKey) },
      kwk
    );
    await saveWrappedMasterKeyConfig(wrappedKeyStr, salt);
    setInMemorySessionKey(masterKey);
    state.pinSetup.isOpen = false;
    state.pinSetup.error = "";
  } catch (err) {
    state.pinSetup.error = `Error setting up PIN: ${err.message}`;
  } finally {
    state.pinSetup.pin = "";
    state.pinSetup.confirmPin = "";
    const el1 = document.getElementById("setup-pin");
    if (el1) el1.value = "";
    const el2 = document.getElementById("confirm-setup-pin");
    if (el2) el2.value = "";
  }
}

export async function handlePINUnlockSubmit() {
  const pin = state.pinUnlock.pin;
  if (!pin) {
    state.pinUnlock.error = "Please enter your security PIN.";
    return;
  }

  try {
    const config = await getWrappedMasterKeyConfig();
    if (!config.wrappedKey || !config.salt) {
      state.pinUnlock.error = "No wrapped master key configured.";
      return;
    }
    const kwk = await deriveKeyFromPIN(pin, config.salt);
    const decrypted = await decryptAESGCM(config.wrappedKey, kwk);
    const masterKey = new Uint8Array(decrypted.masterKey);
    setInMemorySessionKey(masterKey);
    state.pinUnlock.isOpen = false;
    state.pinUnlock.error = "";
  } catch {
    state.pinUnlock.error = "Incorrect security PIN. Access denied.";
  } finally {
    state.pinUnlock.pin = "";
    const el = document.getElementById("unlock-pin");
    if (el) el.value = "";
  }
}
