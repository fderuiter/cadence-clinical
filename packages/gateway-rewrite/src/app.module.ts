import { Module } from "@nestjs/common";
import { AppController } from "./app.controller.js";
import { JwksCoalescerModule } from "./jwks-coalescer.module.js";

@Module({
  imports: [JwksCoalescerModule],
  controllers: [AppController],
})
export class AppModule {}
