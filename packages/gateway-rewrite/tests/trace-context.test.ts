import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { TraceContextService } from "../src/trace-context.service.js";
import { PinoLoggerService, scrubPii } from "../src/pino-logger.service.js";
import { TraceContextMiddleware } from "../src/trace-context.middleware.js";
import { propagateTraceContext } from "../src/trace-context-http.helper.js";

describe("Pino-Backed TraceContext Module", () => {
  let traceService: TraceContextService;

  beforeEach(() => {
    traceService = new TraceContextService();
  });

  describe("TraceContextService & Naming Rules", () => {
    it("should generate a unique context ID for incoming paths with prefix", () => {
      const id1 = traceService.generateCorrelationId("gateway");
      const id2 = traceService.generateCorrelationId("gateway");

      expect(id1).toContain("req-gateway-");
      expect(id2).toContain("req-gateway-");
      expect(id1).not.toBe(id2);
    });

    it("should structure correlation IDs using target microservice naming rules", () => {
      const etmfId = traceService.generateCorrelationId("etmf");
      const executionId = traceService.generateCorrelationId("execution");
      const qualityId = traceService.generateCorrelationId("quality");

      expect(etmfId).toMatch(/^req-etmf-[a-f0-9-]+$/);
      expect(executionId).toMatch(/^req-execution-[a-f0-9-]+$/);
      expect(qualityId).toMatch(/^req-quality-[a-f0-9-]+$/);
    });

    it("should format existing correlation IDs appropriately when target service changes", () => {
      const gatewayId = "req-gateway-12345-abcde";
      const formattedForEtmf = traceService.formatForService(gatewayId, "etmf");
      expect(formattedForEtmf).toBe("req-etmf-12345-abcde");

      const rawId = "some-random-uuid";
      const formattedRaw = traceService.formatForService(rawId, "execution");
      expect(formattedRaw).toBe("req-execution-some-random-uuid");
    });

    it("should maintain isolation across async contexts using AsyncLocalStorage", () => {
      const idA = "req-service-A";
      const idB = "req-service-B";

      expect(traceService.getCorrelationId()).toBeUndefined();

      traceService.run(idA, () => {
        expect(traceService.getCorrelationId()).toBe(idA);

        traceService.run(idB, () => {
          expect(traceService.getCorrelationId()).toBe(idB);
        });

        expect(traceService.getCorrelationId()).toBe(idA);
      });

      expect(traceService.getCorrelationId()).toBeUndefined();
    });
  });

  describe("PinoLoggerService & PII redaction", () => {
    it("should redact unencrypted clinical and patient personal identifiers (PII/PHI) in strings", () => {
      const patientSSN = "000-12-3456";
      const patientEmail = "patient-jane@example.com";
      const normalText = "This is a normal log message";

      expect(scrubPii(patientSSN)).toBe("[REDACTED_SSN]");
      expect(scrubPii(patientEmail)).toBe("[REDACTED_EMAIL]");
      expect(scrubPii(normalText)).toBe(normalText);
    });

    it("should recursively redact clinical and patient personal identifiers in objects", () => {
      const payload = {
        studyId: "STUDY-101",
        subject: {
          birthDate: "1980-05-15",
          email: "subject@trial.com",
          personal_identifier: "PID-9999",
        },
        findings: {
          clinical_identifier: "CID-123",
          notes: "Patient is recovering well.",
        },
      };

      const scrubbed = scrubPii(payload);
      expect(scrubbed.studyId).toBe("STUDY-101");
      expect(scrubbed.subject.birthDate).toBe("[REDACTED]");
      expect(scrubbed.subject.email).toBe("[REDACTED]");
      expect(scrubbed.subject.personal_identifier).toBe("[REDACTED]");
      expect(scrubbed.findings.clinical_identifier).toBe("[REDACTED]");
      expect(scrubbed.findings.notes).toBe("Patient is recovering well.");
    });

    it("should include correlationId automatically in JSON logs when run in active trace context", () => {
      const logger = new PinoLoggerService(traceService);
      const writeSpy = vi.spyOn((logger as any).pinoLogger, "info");

      traceService.run("req-execution-test-123", () => {
        logger.log("Executing transaction step");
        expect(writeSpy).toHaveBeenCalled();
        const loggedObj = writeSpy.mock.calls[0][0] as any;
        expect(loggedObj.correlationId).toBe("req-execution-test-123");
        expect(loggedObj.correlation_id).toBe("req-execution-test-123");
        expect(loggedObj.msg).toBe("Executing transaction step");
      });
    });
  });

  describe("TraceContextMiddleware", () => {
    it("should extract correlation ID from request headers and set response headers", () => {
      const middleware = new TraceContextMiddleware(traceService);
      const req = {
        headers: {
          "x-correlation-id": "req-custom-header-id",
        },
        url: "/api/v1/etmf/documents",
      };
      const res = {
        headers: {} as Record<string, string>,
        setHeader(name: string, value: string) {
          this.headers[name] = value;
        },
      };
      const next = vi.fn(() => {
        // Confirm inside next() we are running inside the correct context
        expect(traceService.getCorrelationId()).toBe("req-custom-header-id");
      });

      middleware.use(req, res, next);

      expect(next).toHaveBeenCalled();
      expect(res.headers["x-correlation-id"]).toBe("req-custom-header-id");
      expect(res.headers["x-request-id"]).toBe("req-custom-header-id");
    });

    it("should automatically detect target service and generate a correlation ID if missing", () => {
      const middleware = new TraceContextMiddleware(traceService);
      const req = {
        headers: {},
        url: "/api/v1/execution/subject/enroll",
      };
      const res = {
        headers: {} as Record<string, string>,
        setHeader(name: string, value: string) {
          this.headers[name] = value;
        },
      };
      const next = vi.fn(() => {
        const activeId = traceService.getCorrelationId();
        expect(activeId).toContain("req-execution-");
      });

      middleware.use(req, res, next);

      expect(next).toHaveBeenCalled();
      expect(res.headers["x-correlation-id"]).toContain("req-execution-");
    });
  });

  describe("Trace Context Outbound Header Propagation Helper", () => {
    it("should propagate formatted correlation ID to outgoing request headers", () => {
      traceService.run("req-gateway-abc-123", () => {
        const outboundHeaders = propagateTraceContext({}, "etmf");
        expect(outboundHeaders["x-correlation-id"]).toBe("req-etmf-abc-123");
        expect(outboundHeaders["x-request-id"]).toBe("req-etmf-abc-123");
      });
    });

    it("should generate a new formatted correlation ID if no context is active", () => {
      const outboundHeaders = propagateTraceContext({}, "quality");
      expect(outboundHeaders["x-correlation-id"]).toContain("req-quality-");
    });
  });
});
