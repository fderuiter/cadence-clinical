import {
  Injectable,
  NestInterceptor,
  ExecutionContext,
  CallHandler,
  BadRequestException,
} from "@nestjs/common";
import { Observable } from "rxjs";
import { validateUsdmGraph } from "usdm-schemas";

@Injectable()
export class PreProxyGraphValidationInterceptor implements NestInterceptor {
  intercept(context: ExecutionContext, next: CallHandler): Observable<any> {
    const http = context.switchToHttp();
    const req = http.getRequest();

    if (!req) return next.handle();

    const method = (req.method || "").toUpperCase();

    // Intercept study modification requests (POST, PUT, PATCH)
    if (["POST", "PUT", "PATCH"].includes(method)) {
      const body = req.body || {};
      const headers = req.headers || {};

      let projectionContext: any = null;
      if (headers["x-study-projection"]) {
        try {
          projectionContext =
            typeof headers["x-study-projection"] === "string"
              ? JSON.parse(headers["x-study-projection"])
              : headers["x-study-projection"];
        } catch {
          // ignore invalid JSON header
        }
      }

      const result = validateUsdmGraph(body, {
        projectionContext,
      });

      if (!result.valid) {
        const errorMessages = result.errors.map((e) => e.message).join("; ");
        throw new BadRequestException({
          statusCode: 400,
          error: "Bad Request",
          message: `USDM Graph Validation Failed: ${errorMessages}`,
          errors: result.errors,
          cyclePath: result.cyclePath,
        });
      }
    }

    return next.handle();
  }
}
