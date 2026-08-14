import { Module } from "@nestjs/common";
import { GatewayGatingService } from "./gating-and-gxp.js";

@Module({
  providers: [GatewayGatingService],
  exports: [GatewayGatingService],
})
export class GxpGatingModule {}
