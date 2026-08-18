import { Injectable, NestMiddleware } from "@nestjs/common";
import {
  IngressHeaderSanitizerService,
  IngressHeaderSanitizerOptions,
  DEFAULT_ALLOWED_HEADERS,
  DEFAULT_PROHIBITED_HEADERS,
} from "./ingress-header-sanitizer.service.js";

export {
  DEFAULT_ALLOWED_HEADERS,
  DEFAULT_PROHIBITED_HEADERS,
  IngressHeaderSanitizerOptions,
};

@Injectable()
export class IngressHeaderSanitizerMiddleware implements NestMiddleware {
  private readonly sanitizerService: IngressHeaderSanitizerService;

  constructor(optionsOrService?: IngressHeaderSanitizerOptions | IngressHeaderSanitizerService) {
    if (optionsOrService instanceof IngressHeaderSanitizerService) {
      this.sanitizerService = optionsOrService;
    } else {
      this.sanitizerService = new IngressHeaderSanitizerService(optionsOrService);
    }
  }

  use(req: any, res: any, next: () => void): void {
    if (req && req.headers) {
      this.sanitizerService.sanitizeHeaders(req.headers);
    }
    next();
  }

  public sanitizeHeaders(headers: Record<string, any>): Record<string, any> {
    return this.sanitizerService.sanitizeHeaders(headers);
  }
}

// Alias for flexibility in naming conventions
export const IngressHeaderSanitizationMiddleware = IngressHeaderSanitizerMiddleware;
