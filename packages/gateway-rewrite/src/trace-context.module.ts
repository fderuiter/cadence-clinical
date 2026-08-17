import { Module } from "@nestjs/common";
import { TraceContextService } from "./trace-context.service.js";
import { PinoLoggerService } from "./pino-logger.service.js";

@Module({
  providers: [TraceContextService, PinoLoggerService],
  exports: [TraceContextService, PinoLoggerService],
})
export class TraceContextModule {}
