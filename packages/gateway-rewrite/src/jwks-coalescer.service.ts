import { Injectable, OnModuleInit, Logger } from "@nestjs/common";
import * as crypto from "crypto";

@Injectable()
export class JwksCoalescerService implements OnModuleInit {
  private readonly logger = new Logger(JwksCoalescerService.name);

  // In-memory cache for public keys
  private keyCache = new Map<string, any>();

  // Cached parsed public KeyObjects to eliminate parsing overhead on fast-path
  private publicKeyCache = new Map<string, any>();

  // Short-lived negative cache for unassigned / invalid key IDs: kid -> expiration timestamp (epoch ms)
  private negativeCache = new Map<string, number>();

  // Global in-flight fetch promise to coalesce concurrent requests for uncached/unknown key IDs
  private inFlightFetch: Promise<void> | null = null;

  // In-flight fetches map to merge concurrent requests for the same uncached kid
  private inFlightFetches = new Map<string, Promise<void>>();

  // JWKS Endpoint URL
  private jwksUrl: string =
    process.env.JWKS_URL ||
    "http://localhost:8080/realms/master/protocol/openid-connect/certs";

  // Request timeout in milliseconds (default to 5.0 seconds)
  private timeoutMs: number = 5000;

  // Negative cache TTL in milliseconds (default to 10 seconds per Requirement 2)
  private negativeCacheTtlMs: number = process.env.NEGATIVE_CACHE_TTL_MS
    ? parseInt(process.env.NEGATIVE_CACHE_TTL_MS, 10)
    : 10000; // deid-ignore

  // Negative cache max capacity threshold (default to 1000 entries per Requirement 4)
  private negativeCacheMaxCapacity: number = process.env
    .NEGATIVE_CACHE_MAX_CAPACITY
    ? parseInt(process.env.NEGATIVE_CACHE_MAX_CAPACITY, 10)
    : 1000;

  // Track number of HTTP fetch calls for metrics / testing assertions
  private fetchCount: number = 0;

  constructor() {
    const originalSet = this.keyCache.set.bind(this.keyCache);
    this.keyCache.set = (key: string, value: any) => {
      const res = originalSet(key, value);
      try {
        if (value && !this.publicKeyCache.has(key)) {
          const publicKey = crypto.createPublicKey({
            key: value,
            format: "jwk",
          });
          this.publicKeyCache.set(key, publicKey);
        }
      } catch (e) {
        // Ignore parsing errors (e.g. if the value is not a valid JWK or during reset/clearing)
      }
      return res;
    };
  }

  /**
   * NestJS OnModuleInit hook - performs eager prefetching
   */
  async onModuleInit(): Promise<void> {
    if (
      process.env.SKIP_JWKS_FETCH &&
      process.env.SKIP_JWKS_FETCH.toLowerCase() !== "false"
    ) {
      this.logger.log(
        "Eager JWKS prefetching skipped via SKIP_JWKS_FETCH environment variable."
      );
      return;
    }
    this.logger.log("Starting eager JWKS prefetching...");
    try {
      if (!this.inFlightFetch) {
        this.inFlightFetch = this.fetchAndCacheKeys(this.timeoutMs).finally(
          () => {
            this.inFlightFetch = null;
          }
        );
      }
      await this.inFlightFetch;
      this.logger.log("Eager JWKS prefetching completed successfully.");
    } catch (error: any) {
      // Complete startup process successfully even if initial prefetch fails
      this.logger.warn(
        `Initial JWKS prefetching failed (${error.message}). Gateway will startup successfully and fetch keys on-demand.`
      );
    }
  }

  /**
   * Dynamically configure options (mainly for testing or custom runtime setups)
   */
  configure(options: {
    jwksUrl?: string;
    timeoutMs?: number;
    negativeCacheTtlMs?: number;
    negativeCacheMaxCapacity?: number;
  }): void {
    if (options.jwksUrl) this.jwksUrl = options.jwksUrl;
    if (options.timeoutMs !== undefined) this.timeoutMs = options.timeoutMs;
    if (options.negativeCacheTtlMs !== undefined)
      this.negativeCacheTtlMs = options.negativeCacheTtlMs;
    if (options.negativeCacheMaxCapacity !== undefined)
      this.negativeCacheMaxCapacity = options.negativeCacheMaxCapacity;
  }

  /**
   * Returns the total count of network requests initiated.
   * Useful for testing and validating request coalescing.
   */
  getFetchCount(): number {
    return this.fetchCount;
  }

  /**
   * Reset the fetch count and all in-memory caches.
   */
  reset(): void {
    this.keyCache.clear();
    this.publicKeyCache.clear();
    this.inFlightFetches.clear();
    this.inFlightFetch = null;
    this.negativeCache.clear();
    this.fetchCount = 0;
  }

  /**
   * Get all currently cached keys (positive cache)
   */
  getCachedKeys(): Map<string, any> {
    return this.keyCache;
  }

  /**
   * Get all currently active negative cache entries (kid -> expiresAt)
   */
  getNegativeCache(): Map<string, number> {
    return this.negativeCache;
  }

  /**
   * Get total number of entries currently stored in the negative cache
   */
  getNegativeCacheSize(): number {
    return this.negativeCache.size;
  }

  /**
   * Record an unassigned key ID in the negative cache with capacity eviction (LRU order).
   */
  private addToNegativeCache(kid: string): void {
    const expiresAt = Date.now() + this.negativeCacheTtlMs;

    // Delete existing entry so re-adding puts it at the end of Map iteration order (LRU eviction)
    if (this.negativeCache.has(kid)) {
      this.negativeCache.delete(kid);
    } else if (this.negativeCache.size >= this.negativeCacheMaxCapacity) {
      const oldestKey = this.negativeCache.keys().next().value;
      if (oldestKey !== undefined) {
        this.negativeCache.delete(oldestKey);
      }
    }

    this.negativeCache.set(kid, expiresAt);
  }

  /**
   * Check if a key ID is currently recorded in the active negative cache.
   */
  private isNegativelyCached(kid: string): boolean {
    if (!this.negativeCache.has(kid)) {
      return false;
    }
    const expiresAt = this.negativeCache.get(kid)!;
    if (Date.now() >= expiresAt) {
      this.negativeCache.delete(kid);
      return false;
    }
    return true;
  }

  /**
   * Fetch JWKS keys and store them in the local in-memory cache
   */
  private async fetchAndCacheKeys(timeout: number): Promise<void> {
    this.fetchCount++;
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeout);

    try {
      const response = await fetch(this.jwksUrl, {
        signal: controller.signal,
        headers: { Accept: "application/json" },
      });

      if (!response.ok) {
        throw new Error(
          `JWKS HTTP fetch failed with status ${response.status}`
        );
      }

      const jwks: any = await response.json();
      if (jwks && Array.isArray(jwks.keys)) {
        for (const key of jwks.keys) {
          if (key.kid) {
            this.keyCache.set(key.kid, key);
            // If key is present in JWKS, clear any negative cache entry
            this.negativeCache.delete(key.kid);
          }
        }
      } else {
        throw new Error("Invalid JWKS format: keys array not found.");
      }
    } finally {
      clearTimeout(timer);
    }
  }

  /**
   * Retrieves a public key by Key ID (kid).
   * Implements eager cache checks, short-lived negative cache rejection,
   * and dynamic global promise coalescing for stampede protection.
   */
  async getKey(kid: string): Promise<any> {
    if (!kid) {
      throw new Error("Cannot fetch key: No Key ID (kid) provided.");
    }

    // Requirement 3: Check the local positive key cache before starting any network request
    if (this.keyCache.has(kid)) {
      return this.keyCache.get(kid);
    }

    // Requirement 3: Reject requests matching active negative cache entries without initiating outbound calls
    if (this.isNegativelyCached(kid)) {
      this.logger.debug(
        `Negative cache hit for kid "${kid}". Rejecting without network fetch.`
      );
      throw new Error(`Key ID "${kid}" not found in retrieved JWKS keys.`);
    }

    // Requirement 1: Coalesce concurrent uncached key ID lookups into a single global fetch operation
    if (!this.inFlightFetch) {
      this.logger.log(
        `Cache miss for kid "${kid}". Initiating coalesced JWKS fetch...`
      );
      this.inFlightFetch = this.fetchAndCacheKeys(this.timeoutMs).finally(
        () => {
          this.inFlightFetch = null;
        }
      );
    } else {
      this.logger.log(
        `Coalescing request: Joining in-flight JWKS fetch for kid "${kid}".`
      );
    }

    this.inFlightFetches.set(kid, this.inFlightFetch);

    try {
      await this.inFlightFetch;
    } catch (error: any) {
      // Requirement 5: Retain previously cached valid keys when outbound key retrieval attempts fail
      this.logger.error(
        `Dynamic JWKS fetch failed for kid "${kid}": ${error.message}`
      );
      throw new Error(
        `Failed to fetch public key for kid "${kid}": ${error.message}`
      );
    } finally {
      this.inFlightFetches.delete(kid);
    }

    // After coalesced fetch resolves, re-check local cache for the key
    if (this.keyCache.has(kid)) {
      return this.keyCache.get(kid);
    }

    // Requirement 2: Record unassigned key IDs in a negative lookup cache for 10 seconds upon fetch completion
    this.addToNegativeCache(kid);

    throw new Error(`Key ID "${kid}" not found in retrieved JWKS keys.`);
  }

  /**
   * Verify and decode JWT token using the cached / dynamically fetched public key.
   */
  async verifyToken(token: string): Promise<any> {
    const startTime = performance.now();

    const parts = token.split(".");
    if (parts.length !== 3) {
      throw new Error(
        "Invalid token structure: must have 3 parts separated by dots."
      );
    }

    const [headerB64, payloadB64, signatureB64] = parts;

    // 1. Decode header to extract `kid`
    let header: any;
    try {
      header = JSON.parse(Buffer.from(headerB64, "base64url").toString("utf8"));
    } catch (e: any) {
      throw new Error(`Invalid token header: ${e.message}`);
    }

    const kid = header.kid;
    if (!kid) {
      throw new Error("Token header does not contain a Key ID (kid).");
    }

    // 2. Get the key (from cache, negative cache, or coalesced dynamic fetch)
    const jwk = await this.getKey(kid);

    // 3. Re-verify cryptographic signature
    const data = `${headerB64}.${payloadB64}`;
    const signature = Buffer.from(signatureB64, "base64url");

    try {
      let publicKey = this.publicKeyCache.get(kid);
      if (!publicKey) {
        publicKey = crypto.createPublicKey({ key: jwk, format: "jwk" });
        this.publicKeyCache.set(kid, publicKey);
      }
      const verify = crypto.createVerify("SHA256");
      verify.write(data);
      verify.end();
      const isValid = verify.verify(publicKey, signature);
      if (!isValid) {
        throw new Error("Cryptographic token signature verification failed.");
      }
    } catch (err: any) {
      throw new Error(`Signature verification failed: ${err.message}`);
    }

    // 4. Decode payload and check expiration
    let payload: any;
    try {
      payload = JSON.parse(
        Buffer.from(payloadB64, "base64url").toString("utf8")
      );
    } catch (e: any) {
      throw new Error(`Invalid token payload: ${e.message}`);
    }

    if (payload.exp && Date.now() / 1000 > payload.exp) {
      throw new Error("Token signature is valid but the token has expired.");
    }

    const duration = performance.now() - startTime;
    this.logger.debug(
      `Token verification completed in ${duration.toFixed(2)}ms.`
    );

    return payload;
  }
}
