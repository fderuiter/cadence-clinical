import { Injectable } from "@nestjs/common";

@Injectable()
export class RateLimiterService {
  private requests: Map<string, number[]> = new Map();

  /**
   * Reads the rate limit window in seconds from system environment variables.
   * Defaults to 60.0 seconds if RATE_LIMIT_WINDOW is unset or invalid.
   */
  public getWindowSeconds(): number {
    const envVal = process.env.RATE_LIMIT_WINDOW;
    if (envVal !== undefined && envVal.trim() !== "") {
      const parsed = parseFloat(envVal);
      if (!isNaN(parsed) && parsed > 0) {
        return parsed;
      }
    }
    return 60.0;
  }

  /**
   * Reads the maximum requests per window from system environment variables.
   * Defaults to 100 requests if RATE_LIMIT_MAX_REQUESTS is unset or invalid.
   */
  public getMaxRequests(): number {
    const envVal = process.env.RATE_LIMIT_MAX_REQUESTS;
    if (envVal !== undefined && envVal.trim() !== "") {
      const parsed = parseInt(envVal, 10);
      if (!isNaN(parsed) && parsed > 0) {
        return parsed;
      }
    }
    return 100;
  }

  /**
   * Checks if a request key exceeds the permitted rate within a sliding window.
   *
   * @param key A unique string identifying the requester (e.g., user:sub or IP address).
   * @param customWindowSeconds Optional override for window duration in seconds.
   * @param customMaxRequests Optional override for max request threshold.
   * @param nowTimestamp Optional timestamp override in milliseconds (useful for testing).
   * @returns true if rate limit is exceeded, false otherwise.
   */
  public isRateLimited(
    key: string,
    customWindowSeconds?: number,
    customMaxRequests?: number,
    nowTimestamp?: number
  ): boolean {
    const now = nowTimestamp ?? Date.now();
    const windowSeconds = customWindowSeconds ?? this.getWindowSeconds();
    const maxRequests = customMaxRequests ?? this.getMaxRequests();
    const windowMs = windowSeconds * 1000;

    let timestamps = this.requests.get(key) || [];

    // Prune timestamps older than window boundary
    timestamps = timestamps.filter((t) => now - t < windowMs);

    if (timestamps.length >= maxRequests) {
      this.requests.set(key, timestamps);
      return true;
    }

    timestamps.push(now);
    this.requests.set(key, timestamps);
    return false;
  }

  /**
   * Extracts client identification key from HTTP request.
   * If a Bearer token is present, safely decodes payload (without signature verification)
   * to extract the sub claim.
   * Falls back to client IP address if token or sub claim is missing.
   */
  public getClientKey(req: any): string {
    const authHeader =
      req?.headers?.authorization || req?.headers?.Authorization;

    if (authHeader && typeof authHeader === "string" && authHeader.startsWith("Bearer ")) {
      const token = authHeader.substring(7).trim();
      const sub = this.extractSubFromToken(token);
      if (sub) {
        return `user:${sub}`;
      }
    }

    return this.extractClientIp(req);
  }

  /**
   * Safely decodes JWT payload without cryptographic signature verification.
   */
  private extractSubFromToken(token: string): string | null {
    if (!token) return null;
    const parts = token.split(".");
    if (parts.length < 2) return null;

    try {
      let base64 = parts[1].replace(/-/g, "+").replace(/_/g, "/");
      while (base64.length % 4 !== 0) {
        base64 += "=";
      }
      const jsonStr = Buffer.from(base64, "base64").toString("utf8");
      const payload = JSON.parse(jsonStr);

      if (payload && (typeof payload.sub === "string" || typeof payload.sub === "number")) {
        const subStr = String(payload.sub).trim();
        if (subStr.length > 0) {
          return subStr;
        }
      }
    } catch {
      // Decode failure gracefully falls back to IP address
    }
    return null;
  }

  /**
   * Extracts client remote IP address from HTTP request headers or socket.
   */
  private extractClientIp(req: any): string {
    if (!req) return "unknown";

    const xForwardedFor =
      req.headers?.["x-forwarded-for"] || req.headers?.["X-Forwarded-For"];
    if (xForwardedFor) {
      const raw = Array.isArray(xForwardedFor)
        ? xForwardedFor[0]
        : String(xForwardedFor);
      const firstIp = raw.split(",")[0].trim();
      if (firstIp) return firstIp;
    }

    if (req.ip && typeof req.ip === "string") {
      return req.ip;
    }

    if (req.socket?.remoteAddress && typeof req.socket.remoteAddress === "string") {
      return req.socket.remoteAddress;
    }

    if (req.connection?.remoteAddress && typeof req.connection.remoteAddress === "string") {
      return req.connection.remoteAddress;
    }

    if (req.info?.remoteAddress && typeof req.info.remoteAddress === "string") {
      return req.info.remoteAddress;
    }

    return "unknown";
  }

  /**
   * Checks if a request path should bypass rate limiting.
   * Empty base paths ("" or "/"), health checks ("/health", "/health/"), and docs bypass rate limiting.
   */
  public isExcludedPath(path?: string): boolean {
    if (path === undefined || path === null) return true;
    const cleanPath = path.split("?")[0].trim();
    if (
      cleanPath === "" ||
      cleanPath === "/" ||
      cleanPath === "/health" ||
      cleanPath === "/health/"
    ) {
      return true;
    }
    return false;
  }

  /**
   * Gets current count of active requests in sliding window for key.
   */
  public getRequestCount(
    key: string,
    customWindowSeconds?: number,
    nowTimestamp?: number
  ): number {
    const now = nowTimestamp ?? Date.now();
    const windowSeconds = customWindowSeconds ?? this.getWindowSeconds();
    const windowMs = windowSeconds * 1000;

    let timestamps = this.requests.get(key) || [];
    timestamps = timestamps.filter((t) => now - t < windowMs);
    this.requests.set(key, timestamps);
    return timestamps.length;
  }

  /**
   * Resets in-memory rate limit store. Useful for testing or admin resets.
   */
  public reset(): void {
    this.requests.clear();
  }
}
