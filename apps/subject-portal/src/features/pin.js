// Local security PIN that wraps the offline database master key in memory (ADR-2360),
// with 5-attempt consecutive lockout cap and identity-verified OIDC reset.
import { decryptAESGCM, deriveKeyFromPIN, encryptAESGCM } from "ui";
import { nextTick } from "vue";

import { isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import {
  flagLockedEntriesForPinReset,
  getInMemorySessionKey,
  getWrappedMasterKeyConfig,
  saveWrappedMasterKeyConfig,
  setInMemorySessionKey,
} from "../sync-queue.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import { logAuditRecord } from "./audit.js";
import { login } from "./auth.js";
import { maskParticipantIdentifier } from "./site-support.js";
import { syncOfflineQueue } from "./sync.js";
import {
  checkOnline,
  refreshSubmissionsState,
  renderSyncQueueList,
} from "./sync-status.js";

export const MAX_PIN_UNLOCK_ATTEMPTS = 5;
export const PENDING_OIDC_PIN_RESET_KEY = "oidc_pending_pin_reset";

let pendingResetMasterKey = null;

export function hasVerifiedParticipantIdentity() {
  if (isAuthenticatedSession() || isDemoModeActive()) {
    return true;
  }
  if (state.session.isOfflineMode || !state.session.userId) {
    return false;
  }
  try {
    const storedToken =
      (typeof sessionStorage !== "undefined" &&
        sessionStorage.getItem("mock_token")) ||
      (typeof localStorage !== "undefined" &&
        localStorage.getItem("mock_token"));
    return Boolean(storedToken);
  } catch {
    return false;
  }
}

function ensurePinUnlockState() {
  if (typeof state.pinUnlock.failedAttempts !== "number") {
    state.pinUnlock.failedAttempts = 0;
  }
  if (typeof state.pinUnlock.maxAttempts !== "number") {
    state.pinUnlock.maxAttempts = MAX_PIN_UNLOCK_ATTEMPTS;
  }
  if (typeof state.pinUnlock.isLockedOut !== "boolean") {
    state.pinUnlock.isLockedOut = false;
  }
  if (typeof state.pinUnlock.requiresReauth !== "boolean") {
    state.pinUnlock.requiresReauth = false;
  }
}

function consumePendingOidcResetFlag() {
  try {
    if (
      typeof sessionStorage !== "undefined" &&
      sessionStorage.getItem(PENDING_OIDC_PIN_RESET_KEY) === "true"
    ) {
      sessionStorage.removeItem(PENDING_OIDC_PIN_RESET_KEY);
      return true;
    }
  } catch {
    // Ignore sessionStorage restrictions
  }
  return false;
}

export async function checkPINWrapper() {
  ensurePinUnlockState();

  if (hasVerifiedParticipantIdentity() && consumePendingOidcResetFlag()) {
    await confirmPINReset();
    return;
  }

  if (getInMemorySessionKey()) {
    state.pinUnlock.isOpen = false;
    state.pinUnlock.confirmingReset = false;
    state.pinSetup.isOpen = false;
    return;
  }

  const config = await getWrappedMasterKeyConfig();
  if (config.wrappedKey) {
    state.pinSetup.isOpen = false;
    state.pinUnlock.confirmingReset = false;
    state.pinUnlock.isOpen = true;
    nextTick(() => {
      const targetId = state.pinUnlock.isLockedOut
        ? "btn-pin-unlock-reset"
        : "unlock-pin";
      const el = document.getElementById(targetId);
      if (el) el.focus();
    });
  } else if (
    isAuthenticatedSession() ||
    (!isDemoModeActive() && Boolean(state.session.userId))
  ) {
    state.pinUnlock.isOpen = false;
    state.pinSetup.isOpen = true;
    nextTick(() => {
      const el = document.getElementById("setup-pin");
      if (el) el.focus();
    });
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
    const masterKey =
      pendingResetMasterKey ||
      globalThis.crypto.getRandomValues(new Uint8Array(32));
    const salt = globalThis.crypto.getRandomValues(new Uint8Array(16));
    const kwk = await deriveKeyFromPIN(pin, salt);
    const wrappedKeyStr = await encryptAESGCM(
      { masterKey: Array.from(masterKey) },
      kwk
    );
    await saveWrappedMasterKeyConfig(wrappedKeyStr, salt);
    pendingResetMasterKey = null;
    setInMemorySessionKey(masterKey);
    state.pinSetup.isOpen = false;
    state.pinSetup.error = "";
    ensurePinUnlockState();
    state.pinUnlock.failedAttempts = 0;
    state.pinUnlock.isLockedOut = false;
    state.pinUnlock.requiresReauth = false;
    await refreshSubmissionsState();
    await renderSyncQueueList();
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
  ensurePinUnlockState();

  const maxAttempts = state.pinUnlock.maxAttempts || MAX_PIN_UNLOCK_ATTEMPTS;
  if (
    state.pinUnlock.isLockedOut ||
    state.pinUnlock.failedAttempts >= maxAttempts
  ) {
    state.pinUnlock.isLockedOut = true;
    state.pinUnlock.pin = "";
    state.pinUnlock.error =
      "Too many failed PIN attempts (5 of 5). PIN unlock is paused — please sign in to reset your PIN, sign out, or contact your study site.";
    nextTick(() => {
      const resetBtn = document.getElementById("btn-pin-unlock-reset");
      if (resetBtn) resetBtn.focus();
    });
    return;
  }

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
    state.pinUnlock.failedAttempts = 0;
    state.pinUnlock.isLockedOut = false;
    state.pinUnlock.requiresReauth = false;
    state.pinUnlock.isOpen = false;
    state.pinUnlock.confirmingReset = false;
    state.pinUnlock.error = "";

    await refreshSubmissionsState();
    await renderSyncQueueList();
    if (checkOnline()) {
      await syncOfflineQueue(false, {
        autoDisburse: isAuthenticatedSession() || isDemoModeActive(),
      });
    }
  } catch {
    state.pinUnlock.failedAttempts += 1;
    if (state.pinUnlock.failedAttempts >= maxAttempts) {
      state.pinUnlock.isLockedOut = true;
      state.pinUnlock.error =
        "Too many failed PIN attempts (5 of 5). PIN unlock is paused — please sign in to reset your PIN, sign out, or contact your study site.";
      nextTick(() => {
        const resetBtn = document.getElementById("btn-pin-unlock-reset");
        if (resetBtn) resetBtn.focus();
      });
    } else {
      state.pinUnlock.error = "Incorrect security PIN. Access denied.";
    }
  } finally {
    state.pinUnlock.pin = "";
    const el = document.getElementById("unlock-pin");
    if (el) el.value = "";
  }
}

export function requestPINReset() {
  ensurePinUnlockState();
  state.pinUnlock.error = "";
  state.pinUnlock.confirmingReset = true;
  state.pinUnlock.requiresReauth = !hasVerifiedParticipantIdentity();
  nextTick(() => {
    const primaryId = state.pinUnlock.requiresReauth
      ? "btn-pin-reauth-oidc"
      : "btn-pin-reset-confirm";
    const targetBtn =
      document.getElementById(primaryId) ||
      document.getElementById("btn-pin-reset-cancel");
    if (targetBtn) targetBtn.focus();
  });
}

export function cancelPINReset() {
  ensurePinUnlockState();
  state.pinUnlock.confirmingReset = false;
  state.pinUnlock.error = "";
  nextTick(() => {
    const targetId = state.pinUnlock.isLockedOut
      ? "btn-pin-unlock-reset"
      : "unlock-pin";
    const el = document.getElementById(targetId);
    if (el) el.focus();
  });
}

export async function reauthenticateForPINReset() {
  ensurePinUnlockState();
  try {
    if (typeof sessionStorage !== "undefined") {
      sessionStorage.setItem(PENDING_OIDC_PIN_RESET_KEY, "true");
    }
  } catch {
    // Ignore storage errors
  }

  await login();

  if (hasVerifiedParticipantIdentity()) {
    consumePendingOidcResetFlag();
    await confirmPINReset();
  }
}

export async function confirmPINReset() {
  ensurePinUnlockState();

  if (!hasVerifiedParticipantIdentity()) {
    state.pinUnlock.requiresReauth = true;
    state.pinUnlock.error =
      "Identity verification required. Please sign in with your participant account to reset your security PIN.";
    await reauthenticateForPINReset();
    return;
  }

  consumePendingOidcResetFlag();

  const userId = state.session.userId;
  // Generate a fresh 256-bit local master key for the upcoming PIN setup
  pendingResetMasterKey = globalThis.crypto.getRandomValues(new Uint8Array(32));

  const { flaggedCount } = await flagLockedEntriesForPinReset(userId);

  state.pinUnlock.pin = "";
  state.pinUnlock.error = "";
  state.pinUnlock.confirmingReset = false;
  state.pinUnlock.failedAttempts = 0;
  state.pinUnlock.isLockedOut = false;
  state.pinUnlock.requiresReauth = false;
  state.pinUnlock.isOpen = false;
  state.draftPresence = {};
  for (const assign of state.assignments || []) {
    if (assign) assign.has_draft = false;
  }

  await logAuditRecord(
    "PIN_WRAPPER_RESET",
    {
      subject_id: maskParticipantIdentifier(userId) || "authenticated",
      flagged_entries: flaggedCount,
      new_key_generated: true,
    },
    "Participant verified identity, generated a new local master key, and flagged locked offline entries without clinical PII."
  );

  await refreshSubmissionsState();
  await renderSyncQueueList();

  state.pinSetup.error = "";
  state.pinSetup.pin = "";
  state.pinSetup.confirmPin = "";
  state.pinSetup.isOpen = true;
  nextTick(() => {
    const setupInput = document.getElementById("setup-pin");
    if (setupInput) setupInput.focus();
  });
}
