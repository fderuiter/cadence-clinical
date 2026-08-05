import {
  vi,
  describe,
  it,
  expect,
  beforeEach,
  afterEach,
  beforeAll,
} from "vitest";
import "fake-indexeddb/auto";
import {
  OfflineAuthManager,
  OfflineSession,
} from "../../src/utils/offlineAuth";
import { apiClient } from "../../src/api/apiClient";

// Polyfill window.crypto if needed in JSDOM test environment
beforeAll(() => {
  if (typeof window !== "undefined" && !window.crypto) {
    Object.defineProperty(window, "crypto", {
      value: globalThis.crypto,
      writable: true,
      configurable: true,
    });
  }
});

vi.mock("../../src/api/apiClient", () => {
  return {
    apiClient: {
      post: vi.fn().mockResolvedValue({ success: true }),
    },
  };
});

describe("OfflineAuthManager and Crypto Store", () => {
  let manager: OfflineAuthManager;

  const validSession: OfflineSession = {
    userId: "user-123",
    userRoles: ["site_investigator"],
    offlineToken: "jwt-test-token-xyz",
    createdAt: new Date().toISOString(),
    maxOfflineHours: 72,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    manager = new OfflineAuthManager();
  });

  afterEach(async () => {
    await manager.clearAllOfflineSessions();
  });

  it("should successfully store and retrieve/decrypt an offline session with the correct PIN", async () => {
    const pin = "1234";

    // 1. Store the encrypted session
    await manager.storeEncryptedSession(pin, validSession);

    // 2. Unlock the session using the correct PIN
    const decryptedSession = await manager.unlockOfflineSession(pin);

    // 3. Verify exact payload match
    expect(decryptedSession).toBeDefined();
    expect(decryptedSession.userId).toBe(validSession.userId);
    expect(decryptedSession.offlineToken).toBe(validSession.offlineToken);
    expect(decryptedSession.userRoles).toEqual(validSession.userRoles);

    // 4. Assert in-memory active session is updated
    expect(manager.getActiveSession()).toEqual(decryptedSession);
  });

  it("should support saveOfflineSession alias exactly the same way", async () => {
    const pin = "4321";

    await manager.saveOfflineSession(pin, validSession);
    const decryptedSession = await manager.unlockOfflineSession(pin);

    expect(decryptedSession.userId).toBe(validSession.userId);
  });

  it("should throw OperationError (decryption failure) when using an incorrect PIN", async () => {
    const correctPin = "1234";
    const incorrectPin = "9999";

    // Store encrypted session with correct PIN
    await manager.storeEncryptedSession(correctPin, validSession);

    // Attempt to unlock with incorrect PIN should reject with OperationError (decryption failure)
    let thrownError: any = null;
    try {
      await manager.unlockOfflineSession(incorrectPin);
    } catch (err: any) {
      thrownError = err;
    }

    expect(thrownError).toBeDefined();
    // In Web Crypto, mismatched key GCM decryption throws DOMException: OperationError
    expect(thrownError.name).toBe("OperationError");
  });

  it("should throw an error if the offline session is expired (exceeds maxOfflineHours)", async () => {
    const pin = "1234";

    // Session created 73 hours ago, max age is 72 hours
    const oldDate = new Date();
    oldDate.setHours(oldDate.getHours() - 73);

    const expiredSession: OfflineSession = {
      ...validSession,
      createdAt: oldDate.toISOString(),
      maxOfflineHours: 72,
    };

    await manager.storeEncryptedSession(pin, expiredSession);

    // Attempting to unlock should reject because session has expired
    await expect(manager.unlockOfflineSession(pin)).rejects.toThrow(
      "Offline session expired"
    );
  });

  it("should throw an error if the offline session is expired using custom maxOfflineHours", async () => {
    const pin = "1234";

    // Session created 5 hours ago, max age is 4 hours
    const oldDate = new Date();
    oldDate.setHours(oldDate.getHours() - 5);

    const expiredSession: OfflineSession = {
      ...validSession,
      createdAt: oldDate.toISOString(),
      maxOfflineHours: 4,
    };

    await manager.storeEncryptedSession(pin, expiredSession);

    // Attempting to unlock should reject because session has expired
    await expect(manager.unlockOfflineSession(pin)).rejects.toThrow(
      "Offline session expired"
    );
  });

  it("should trigger online session re-synchronization when window online event is dispatched", async () => {
    const mockPost = vi.mocked(apiClient.post);

    // 1. Set active session manually or by unlocking
    manager.setActiveSession(validSession);

    // 2. Simulate browser going online
    const onlineEvent = new Event("online");
    window.dispatchEvent(onlineEvent);

    // Allow any microtasks / async operations in event handler to run
    await new Promise((resolve) => setTimeout(resolve, 50));

    // 3. Verify gateway API verify request is sent with correct parameters
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost).toHaveBeenCalledWith("/api/v1/auth/offline-verify", {
      token: validSession.offlineToken,
    });
  });

  it("should not trigger re-synchronization if there is no active session", async () => {
    const mockPost = vi.mocked(apiClient.post);

    manager.setActiveSession(null);

    const onlineEvent = new Event("online");
    window.dispatchEvent(onlineEvent);

    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(mockPost).not.toHaveBeenCalled();
  });

  it("should validate that PIN must be between 4 and 6 digits during storage setup", async () => {
    const invalidPinShort = "12";
    const invalidPinLong = "1234567";
    const invalidPinLetters = "abcd";
    const validPin4 = "1234";
    const validPin6 = "123456";

    await expect(manager.storeEncryptedSession(invalidPinShort, validSession)).rejects.toThrow(
      "PIN must be between 4 and 6 digits"
    );
    await expect(manager.storeEncryptedSession(invalidPinLong, validSession)).rejects.toThrow(
      "PIN must be between 4 and 6 digits"
    );
    await expect(manager.storeEncryptedSession(invalidPinLetters, validSession)).rejects.toThrow(
      "PIN must be between 4 and 6 digits"
    );

    // Should succeed with valid PINs
    await manager.storeEncryptedSession(validPin4, validSession);
    await manager.storeEncryptedSession(validPin6, { ...validSession, userId: "user-456" });
  });

  it("should map keys directly to user IDs and preserve consecutive users' keys", async () => {
    const user1Session: OfflineSession = {
      userId: "user-abc",
      userRoles: ["site_investigator"],
      offlineToken: "token-abc-111",
      createdAt: new Date().toISOString(),
      maxOfflineHours: 72,
    };

    const user2Session: OfflineSession = {
      userId: "user-xyz",
      userRoles: ["site_investigator"],
      offlineToken: "token-xyz-222",
      createdAt: new Date().toISOString(),
      maxOfflineHours: 72,
    };

    // User 1 logs in and sets PIN "1111"
    await manager.storeEncryptedSession("1111", user1Session);

    // User 2 logs in and sets PIN "2222" (should not overwrite User 1)
    await manager.storeEncryptedSession("2222", user2Session);

    // Unlock User 1's session with their correct PIN
    const unlocked1 = await manager.unlockOfflineSession("1111", "user-abc");
    expect(unlocked1.offlineToken).toBe("token-abc-111");

    // Unlock User 2's session with their correct PIN
    const unlocked2 = await manager.unlockOfflineSession("2222", "user-xyz");
    expect(unlocked2.offlineToken).toBe("token-xyz-222");
  });

  it("should lock key recovery capabilities for a specific user ID after five consecutive incorrect PIN entry attempts", async () => {
    const pin = "1234";
    const incorrectPin = "1111";
    await manager.storeEncryptedSession(pin, validSession);

    // 1st, 2nd, 3rd, 4th failed attempts
    for (let i = 0; i < 4; i++) {
      await expect(manager.unlockOfflineSession(incorrectPin, validSession.userId)).rejects.toThrow();
    }

    // 5th failed attempt - should lock and throw error
    await expect(manager.unlockOfflineSession(incorrectPin, validSession.userId)).rejects.toThrow(
      "Key recovery locked. Too many failed attempts."
    );

    // 6th attempt (even with correct PIN) should immediately throw lock error
    await expect(manager.unlockOfflineSession(pin, validSession.userId)).rejects.toThrow(
      "Key recovery locked. Too many failed attempts."
    );

    // Resetting failed attempts should allow successful unlock
    await manager.resetFailedAttempts(validSession.userId);
    const unlocked = await manager.unlockOfflineSession(pin, validSession.userId);
    expect(unlocked.offlineToken).toBe(validSession.offlineToken);
  });
});
