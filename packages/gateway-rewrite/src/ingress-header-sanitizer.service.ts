import { Injectable } from "@nestjs/common";

export const DEFAULT_PROHIBITED_HEADERS: readonly string[] = [
  // User identity & roles
  "x-user-id",
  "user-id",
  "x-user",
  "x-user-roles",
  "x-roles",
  "user-roles",
  // Gateway HMAC/signatures & timestamps
  "x-gateway-signature",
  "x-signature",
  "x-signature-version",
  "x-gateway-timestamp",
  "x-timestamp",
  // Context/Scope IDs
  "x-site-id",
  "site-id",
  "x-sponsor-id",
  "sponsor-id",
  "x-tenant-id",
  "tenant-id",
  "x-change-reason",
  "change-reason",
  "x-unblinded-access",
  "unblinded-access",
];

export const DEFAULT_ALLOWED_HEADERS: readonly string[] = [
  // Trace correlation headers
  "x-correlation-id",
  "x-request-id",
  "x-trace-id",
  "traceparent",
  "tracestate",
  // Standard HTTP headers & Authorization
  "authorization",
  "content-type",
  "content-length",
  "accept",
  "accept-encoding",
  "accept-language",
  "user-agent",
  "host",
  "connection",
  "cache-control",
  "pragma",
  "transfer-encoding",
  "origin",
  "referer",
  "cookie",
  "if-match",
  "if-none-match",
  "if-modified-since",
  "if-unmodified-since",
  "x-forwarded-for",
  "x-forwarded-proto",
  "x-forwarded-host",
  "x-forwarded-port",
  // HTTP/2 pseudo headers
  ":method",
  ":path",
  ":scheme",
  ":authority",
];

export interface IngressHeaderSanitizerOptions {
  allowedHeaders?: string[];
  prohibitedHeaders?: string[];
  strictMode?: boolean;
}

@Injectable()
export class IngressHeaderSanitizerService {
  private readonly allowedHeadersSet: Set<string>;
  private readonly prohibitedHeadersSet: Set<string>;
  private readonly strictMode: boolean;

  constructor(options?: IngressHeaderSanitizerOptions) {
    const allowed = options?.allowedHeaders ?? DEFAULT_ALLOWED_HEADERS;
    const prohibited = options?.prohibitedHeaders ?? DEFAULT_PROHIBITED_HEADERS;

    this.allowedHeadersSet = new Set(allowed.map((h) => h.toLowerCase()));
    this.prohibitedHeadersSet = new Set(prohibited.map((h) => h.toLowerCase()));
    this.strictMode = options?.strictMode ?? true;
  }

  public sanitizeHeaders(headers: Record<string, any>): Record<string, any> {
    if (!headers) return headers;

    const keys = Object.keys(headers);
    for (let i = 0; i < keys.length; i++) {
      const key = keys[i];
      const lowerKey = key.toLowerCase();

      // Prohibited headers are ALWAYS stripped
      if (this.prohibitedHeadersSet.has(lowerKey)) {
        delete headers[key];
        continue;
      }

      // If strict allowlisting is active, strip headers not on the allowlist
      if (this.strictMode && !this.allowedHeadersSet.has(lowerKey)) {
        delete headers[key];
      }
    }

    return headers;
  }
}
