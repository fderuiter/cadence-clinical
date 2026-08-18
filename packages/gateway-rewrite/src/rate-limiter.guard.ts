import {
  CanActivate,
  ExecutionContext,
  HttpException,
  HttpStatus,
  Injectable,
} from "@nestjs/common";
import { RateLimiterService } from "./rate-limiter.service.js";

@Injectable()
export class RateLimitGuard implements CanActivate {
  constructor(private readonly rateLimiterService: RateLimiterService) {}

  canActivate(context: ExecutionContext): boolean {
    const req = context.switchToHttp().getRequest();
    const path = req.path || req.originalUrl?.split("?")[0] || req.url?.split("?")[0] || "";

    if (this.rateLimiterService.isExcludedPath(path)) {
      return true;
    }

    const key = this.rateLimiterService.getClientKey(req);

    if (this.rateLimiterService.isRateLimited(key)) {
      throw new HttpException(
        {
          detail: "Too Many Requests. Rate limit exceeded.",
          statusCode: HttpStatus.TOO_MANY_REQUESTS,
          error: "Too Many Requests",
          message: "Rate limit exceeded",
        },
        HttpStatus.TOO_MANY_REQUESTS
      );
    }

    return true;
  }
}
