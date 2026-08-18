import { Module } from "@nestjs/common";
import { RateLimiterService } from "./rate-limiter.service.js";
import { RateLimitMiddleware } from "./rate-limiter.middleware.js";
import { RateLimitGuard } from "./rate-limiter.guard.js";

@Module({
  providers: [RateLimiterService, RateLimitMiddleware, RateLimitGuard],
  exports: [RateLimiterService, RateLimitMiddleware, RateLimitGuard],
})
export class RateLimiterModule {}
