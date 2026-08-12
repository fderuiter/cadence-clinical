import { Injectable, OnModuleInit, Logger } from "@nestjs/common";
import * as crypto from "crypto";

@Injectable()
export class JwksCoalescerService implements OnModuleInit {
  private readonly logger = new Logger(JwksCoalescerService.name);

  // In-memory cache for public keys
  private keyCache = new Map<string, any>();

  // Cached parsed public KeyObjects to eliminate parsing overhead on fast-path
  private publicKeyCache = new Map<string, any>();

  // In-flight fetches map to merge concurrent requests for the same uncached kid
  private inFlightFetches = new Map<string, Promise<void>>();

  // JWKS Endpoint URL
  private jwksUrl: string =
    process.env.JWKS_URL ||
    "http://localhost:8080/realms/master/protocol/openid-connect/certs";

  // Request timeout in milliseconds (default to 5.0 seconds)
  private timeoutMs: number = 5000;

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
      await this.fetchAndCacheKeys(this.timeoutMs);
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
  configure(options: { jwksUrl?: string; timeoutMs?: number }): void {
    if (options.jwksUrl) this.jwksUrl = options.jwksUrl;
    if (options.timeoutMs !== undefined) this.timeoutMs = options.timeoutMs;
  }

  /**
   * Returns the total count of network requests initiated.
   * Useful for testing and validating request coalescing.
   */
  getFetchCount(): number {
    return this.fetchCount;
  }

  /**
   * Reset the fetch count and the in-memory key cache.
   */
  reset(): void {
    this.keyCache.clear();
    this.publicKeyCache.clear();
    this.inFlightFetches.clear();
    this.fetchCount = 0;
  }

  /**
   * Get all currently cached keys
   */
  getCachedKeys(): Map<string, any> {
    return this.keyCache;
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
   * Implements eager cache checks and dynamic promise coalescing for stampede protection.
   */
  async getKey(kid: string): Promise<any> {
    if (!kid) {
      throw new Error("Cannot fetch key: No Key ID (kid) provided.");
    }

    // Requirement 3: Check the local key cache before starting any network request
    if (this.keyCache.has(kid)) {
      return this.keyCache.get(kid);
    }

    // Requirement 4: Merge multiple concurrent verification requests for the same uncached key ID
    let inFlight = this.inFlightFetches.get(kid);
    if (!inFlight) {
      this.logger.log(
        `Cache miss for kid "${kid}". Initiating coalesced JWKS fetch...`
      );
      inFlight = (async () => {
        await this.fetchAndCacheKeys(this.timeoutMs);
      })().finally(() => {
        // Clean up the in-flight map entry after resolution/rejection so future requests can retry if needed
        this.inFlightFetches.delete(kid);
      });
      this.inFlightFetches.set(kid, inFlight);
    } else {
      this.logger.log(
        `Coalescing request: Joining in-flight JWKS fetch for kid "${kid}".`
      );
    }

    try {
      await inFlight;
    } catch (error: any) {
      // Requirement 5: If an on-demand key fetch fails, retain existing cached keys to prevent breaking active user sessions
      this.logger.error(
        `Dynamic JWKS fetch failed for kid "${kid}": ${error.message}`
      );
      throw new Error(
        `Failed to fetch public key for kid "${kid}": ${error.message}`
      );
    }

    // After coalesced fetch resolves, re-check local cache for the key
    if (this.keyCache.has(kid)) {
      return this.keyCache.get(kid);
    }

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

    // 2. Get the key (from cache, or coalesced dynamic fetch)
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
