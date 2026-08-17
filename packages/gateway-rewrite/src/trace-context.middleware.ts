import { Injectable, NestMiddleware } from "@nestjs/common";
import { TraceContextService } from "./trace-context.service.js";

@Injectable()
export class TraceContextMiddleware implements NestMiddleware {
  constructor(private readonly traceContextService: TraceContextService) {}

  use(req: any, res: any, next: () => void): void {
    // 1. Try to find existing correlation ID in request headers
    let correlationId =
      req.headers["x-correlation-id"] ||
      req.headers["x-request-id"] ||
      req.headers["x-trace-id"];

    if (Array.isArray(correlationId)) {
      correlationId = correlationId[0];
    }

    // 2. If not provided, generate a new one based on the target microservice/route
    if (!correlationId) {
      let targetService = "gateway";
      const path = req.originalUrl || req.url || "";
      const match = path.match(/\/api\/v\d+\/([a-zA-Z0-9_-]+)/);
      if (match && match[1]) {
        targetService = match[1];
      }
      correlationId = this.traceContextService.generateCorrelationId(targetService);
    }

    // 3. Set the response headers for client visibility and verification
    res.setHeader("x-correlation-id", correlationId);
    res.setHeader("x-request-id", correlationId);

    // 4. Run the rest of the request within the async trace context
    this.traceContextService.run(correlationId, () => {
      next();
    });
  }
}
