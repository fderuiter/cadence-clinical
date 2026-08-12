import { Module } from "@nestjs/common";
import { JwksCoalescerService } from "./jwks-coalescer.service.js";

@Module({
  providers: [JwksCoalescerService],
  exports: [JwksCoalescerService],
})
export class JwksCoalescerModule {}
