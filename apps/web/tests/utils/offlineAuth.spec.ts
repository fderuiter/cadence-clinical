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
    await manager.clearOfflineSession();
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
});
