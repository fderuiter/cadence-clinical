import { Injectable, NestMiddleware } from "@nestjs/common";
import { RateLimiterService } from "./rate-limiter.service.js";

@Injectable()
export class RateLimitMiddleware implements NestMiddleware {
  constructor(private readonly rateLimiterService: RateLimiterService) {}

  use(req: any, res: any, next: (err?: any) => void): void {
    const path = req.path || req.originalUrl?.split("?")[0] || req.url?.split("?")[0] || "";

    // 1. Exclude health check and empty base paths
    if (this.rateLimiterService.isExcludedPath(path)) {
      next();
      return;
    }

    // 2. Identify client key (user:sub or remote IP)
    const key = this.rateLimiterService.getClientKey(req);

    // 3. Evaluate rate limit threshold
    if (this.rateLimiterService.isRateLimited(key)) {
      const errorPayload = {
        detail: "Too Many Requests. Rate limit exceeded.",
        statusCode: 429,
        error: "Too Many Requests",
        message: "Rate limit exceeded",
      };

      if (typeof res.status === "function") {
        res.status(429).json(errorPayload);
      } else {
        res.statusCode = 429;
        if (typeof res.setHeader === "function") {
          res.setHeader("Content-Type", "application/json");
        }
        if (typeof res.end === "function") {
          res.end(JSON.stringify(errorPayload));
        }
      }
      return;
    }

    next();
  }
}
