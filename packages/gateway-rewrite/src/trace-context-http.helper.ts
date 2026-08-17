import { TraceContextService } from "./trace-context.service.js";

/**
 * Propagates the active trace context / correlation ID into outgoing HTTP headers
 * formatted using target microservice naming rules.
 */
export function propagateTraceContext(
  existingHeaders: Record<string, any> = {},
  targetService: string
): Record<string, string> {
  const service = TraceContextService.getInstance();
  let correlationId = service.getCorrelationId();

  if (correlationId) {
    // Structure existing correlation ID to match target microservice naming rules
    correlationId = service.formatForService(correlationId, targetService);
  } else {
    // Generate a fresh one if no tracing context is active
    correlationId = service.generateCorrelationId(targetService);
  }

  return {
    ...existingHeaders,
    "x-correlation-id": correlationId,
    "x-request-id": correlationId,
  };
}
