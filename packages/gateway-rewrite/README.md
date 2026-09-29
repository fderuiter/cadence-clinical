# gateway-rewrite

TypeScript library of NestJS gateway building blocks: JWKS fetch coalescing, ingress header sanitisation, rate limiting, W3C trace-context propagation, a PII-scrubbing Pino logger, and a pre-proxy USDM graph validation interceptor.

## Contents

`src/index.ts` exports:

- `JwksCoalescerService`, `JwksCoalescerModule`.
- `IngressHeaderSanitizerMiddleware` (also exported as `IngressHeaderSanitizationMiddleware`), `IngressHeaderSanitizerService`, `IngressHeaderSanitizerModule`, `DEFAULT_ALLOWED_HEADERS`, `DEFAULT_PROHIBITED_HEADERS`.
- `RateLimiterService`, `RateLimiterModule`, `RateLimitMiddleware`, `RateLimitGuard`.
- `TraceContextService`, `TraceContextModule`, `TraceContextMiddleware`, `propagateTraceContext`.
- `PinoLoggerService`, `scrubPii`.
- `PreProxyGraphValidationInterceptor` (uses `validateUsdmGraph` from [usdm-schemas](../usdm-schemas/README.md)).

The package has no application bootstrap: `src/index.ts` only re-exports modules. `docker/docker-compose.yml` defines a `gateway-rewrite` container that runs `packages/gateway-rewrite/dist/index.js`, and the nginx front proxy routes `/designer` to it, but nothing in that file starts an HTTP server. The gateway the platform uses is the .NET service in [apps/gateway](../../apps/gateway/README.md).

## Scripts

| Command                               | Purpose            |
| ------------------------------------- | ------------------ |
| `pnpm --filter gateway-rewrite test`  | Vitest (`tests/`). |
| `pnpm --filter gateway-rewrite build` | `tsc` to `dist/`.  |

## Dependencies

- Runtime: `pino`, `rxjs`, `usdm-schemas` (workspace).
- Development: `@nestjs/common`, `@nestjs/core`, `reflect-metadata`, TypeScript, Vitest.

## Used by

No other workspace package or app imports it.
