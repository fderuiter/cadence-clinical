import { Module } from "@nestjs/common";
import { GxpContextService } from "./gxp-context.service.js";
import { GxpLifecycleService } from "./gxp-lifecycle.service.js";

@Module({
  providers: [GxpContextService, GxpLifecycleService],
  exports: [GxpContextService, GxpLifecycleService],
})
export class GxpLifecycleModule {}
