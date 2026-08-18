import { describe, it, expect } from "vitest";
import { BadRequestException, ExecutionContext, CallHandler } from "@nestjs/common";
import { PreProxyGraphValidationInterceptor } from "../src/pre-proxy-graph-validation.interceptor.js";

describe("NestJS Gateway PreProxyGraphValidationInterceptor", () => {
  it("rejects mutation payloads referencing non-existent study components with a 400 Bad Request error", () => {
    const interceptor = new PreProxyGraphValidationInterceptor();

    const mockRequest = {
      method: "POST",
      body: {
        id: "ACT-01",
        assignedEncounterIds: ["V-999"], // V-999 does not exist
      },
      headers: {
        "x-study-projection": JSON.stringify({
          encounters: [{ id: "V-101", name: "Baseline Visit" }],
        }),
      },
    };

    const mockContext = {
      switchToHttp: () => ({
        getRequest: () => mockRequest,
      }),
    } as unknown as ExecutionContext;

    const mockHandler: CallHandler = {
      handle: () => ({ subscribe: () => {} }) as any,
    };

    expect(() => interceptor.intercept(mockContext, mockHandler)).toThrow(
      BadRequestException
    );

    try {
      interceptor.intercept(mockContext, mockHandler);
    } catch (err: any) {
      expect(err).toBeInstanceOf(BadRequestException);
      const res = err.getResponse();
      expect(res.statusCode).toBe(400);
      expect(res.message).toContain("USDM Graph Validation Failed");
      expect(res.errors).toBeDefined();
      expect(
        res.errors.some(
          (e: any) => e.code === "MISSING_REFERENCE" && e.referencedId === "V-999"
        )
      ).toBe(true);
    }
  });

  it("allows valid mutation payloads to pass through successfully", async () => {
    const interceptor = new PreProxyGraphValidationInterceptor();

    const mockRequest = {
      method: "POST",
      body: {
        id: "ACT-01",
        assignedEncounterIds: ["V-101"],
      },
      headers: {
        "x-study-projection": JSON.stringify({
          encounters: [{ id: "V-101", name: "Baseline Visit" }],
        }),
      },
    };

    const mockContext = {
      switchToHttp: () => ({
        getRequest: () => mockRequest,
      }),
    } as unknown as ExecutionContext;

    const mockHandler: CallHandler = {
      handle: () =>
        ({
          subscribe: (observer: any) => {
            if (typeof observer === "function") {
              observer("success-response");
            } else if (observer && observer.next) {
              observer.next("success-response");
            }
          },
        }) as any,
    };

    const obs$: any = interceptor.intercept(mockContext, mockHandler);
    const result = await new Promise((resolve, reject) => {
      obs$.subscribe({
        next: (val: any) => resolve(val),
        error: (err: any) => reject(err),
      });
    });

    expect(result).toBe("success-response");
  });

  it("rejects mutation payloads containing cyclic skip-logic loops", () => {
    const interceptor = new PreProxyGraphValidationInterceptor();

    const mockRequest = {
      method: "PUT",
      body: {
        ecrfFields: [
          {
            id: "v1",
            relevant: {
              node_type: "OPERATOR",
              value: "==",
              children: [{ node_type: "XPATH", value: "v2" }],
            },
          },
          {
            id: "v2",
            relevant: {
              node_type: "OPERATOR",
              value: "==",
              children: [{ node_type: "XPATH", value: "v1" }],
            },
          },
        ],
      },
      headers: {},
    };

    const mockContext = {
      switchToHttp: () => ({
        getRequest: () => mockRequest,
      }),
    } as unknown as ExecutionContext;

    const mockHandler: CallHandler = {
      handle: () => ({ subscribe: () => {} }) as any,
    };

    expect(() => interceptor.intercept(mockContext, mockHandler)).toThrow(
      BadRequestException
    );
  });
});
