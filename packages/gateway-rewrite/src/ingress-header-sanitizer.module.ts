import { Module } from "@nestjs/common";
import { IngressHeaderSanitizerService } from "./ingress-header-sanitizer.service.js";
import { IngressHeaderSanitizerMiddleware } from "./ingress-header-sanitizer.middleware.js";

@Module({
  providers: [IngressHeaderSanitizerService, IngressHeaderSanitizerMiddleware],
  exports: [IngressHeaderSanitizerService, IngressHeaderSanitizerMiddleware],
})
export class IngressHeaderSanitizerModule {}
