import { NestFactory } from "@nestjs/core";
import { FastifyAdapter, NestFastifyApplication } from "@nestjs/platform-fastify";
import { AppModule } from "./app.module.js";

async function bootstrap() {
  const app = await NestFactory.create<NestFastifyApplication>(
    AppModule,
    new FastifyAdapter()
  );
  
  const port = Number(process.env.PORT) || 8000;
  const host = process.env.HOST || "0.0.0.0";
  
  await app.listen(port, host);
  console.log(`NestJS Fastify Gateway listening on http://${host}:${port}`);
}

bootstrap();
