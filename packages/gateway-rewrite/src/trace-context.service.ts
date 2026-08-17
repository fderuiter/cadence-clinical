import { Injectable } from "@nestjs/common";
import { AsyncLocalStorage } from "node:async_hooks";
import * as crypto from "node:crypto";

export interface TraceContext {
  correlationId: string;
}

@Injectable()
export class TraceContextService {
  private static readonly asyncLocalStorage = new AsyncLocalStorage<TraceContext>();
  private static instance: TraceContextService | null = null;

  constructor() {
    TraceContextService.instance = this;
  }

  static getInstance(): TraceContextService {
    if (!TraceContextService.instance) {
      TraceContextService.instance = new TraceContextService();
    }
    return TraceContextService.instance;
  }

  /**
   * Run a callback within a tracing context.
   */
  run<T>(correlationId: string, callback: () => T): T {
    return TraceContextService.asyncLocalStorage.run({ correlationId }, callback);
  }

  /**
   * Get the current correlation ID.
   */
  getCorrelationId(): string | undefined {
    const store = TraceContextService.asyncLocalStorage.getStore();
    return store?.correlationId;
  }

  /**
   * Generate a unique correlation ID matching downstream format rules.
   */
  generateCorrelationId(targetService: string = "gateway"): string {
    const uuid = crypto.randomUUID();
    return `req-${targetService.toLowerCase()}-${uuid}`;
  }

  /**
   * Format an existing correlation ID for a target downstream service.
   */
  formatForService(correlationId: string, targetService: string): string {
    const parts = correlationId.split("-");
    if (parts[0] === "req" && parts.length > 2) {
      const uuidPart = parts.slice(2).join("-");
      return `req-${targetService.toLowerCase()}-${uuidPart}`;
    }
    return `req-${targetService.toLowerCase()}-${correlationId}`;
  }
}
