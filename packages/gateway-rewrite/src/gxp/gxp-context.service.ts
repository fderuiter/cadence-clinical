import { Injectable } from "@nestjs/common";
import { AsyncLocalStorage } from "node:async_hooks";

@Injectable()
export class GxpContextService {
  private readonly storage = new AsyncLocalStorage<{ changeReason?: string }>();

  /**
   * Run a callback synchronously or asynchronously under the given change justification context.
   */
  runWithReason<T>(reason: string, callback: () => T): T {
    return this.storage.run({ changeReason: reason }, callback);
  }

  /**
   * Get the current thread-safe change justification reason.
   */
  getChangeReason(): string | undefined {
    return this.storage.getStore()?.changeReason;
  }
}
