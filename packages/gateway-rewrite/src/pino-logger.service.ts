import { Injectable, LoggerService } from "@nestjs/common";
import pino from "pino";
import { TraceContextService } from "./trace-context.service.js";

export function scrubPii(data: any): any {
  if (data === null || data === undefined) {
    return data;
  }
  if (typeof data === "string") {
    let scrubbed = data;
    scrubbed = scrubbed.replace(/\b\d{3}-\d{2}-\d{4}\b/g, "[REDACTED_SSN]");
    scrubbed = scrubbed.replace(/\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b/g, "[REDACTED_EMAIL]");
    return scrubbed;
  }
  if (Array.isArray(data)) {
    return data.map(item => scrubPii(item));
  }
  if (typeof data === "object") {
    if (data instanceof Error) {
      return {
        message: scrubPii(data.message),
        stack: scrubPii(data.stack),
        name: data.name,
      };
    }
    const scrubbedObj: Record<string, any> = {};
    const sensitiveKeys = new Set([
      "email", "ssn", "phone", "birthdate", "birth_date", "dob",
      "password", "patient", "name", "pseudonym", "clinical_identifier", "personal_identifier"
    ]);
    for (const [key, value] of Object.entries(data)) {
      const lowerKey = key.toLowerCase();
      let isSensitive = false;
      for (const sensitive of sensitiveKeys) {
        if (lowerKey.includes(sensitive)) {
          isSensitive = true;
          break;
        }
      }
      if (isSensitive) {
        scrubbedObj[key] = "[REDACTED]";
      } else {
        scrubbedObj[key] = scrubPii(value);
      }
    }
    return scrubbedObj;
  }
  return data;
}

@Injectable()
export class PinoLoggerService implements LoggerService {
  private readonly pinoLogger: pino.Logger;

  constructor(private readonly traceContextService: TraceContextService) {
    this.pinoLogger = pino({
      level: process.env.LOG_LEVEL || "info",
      redact: {
        paths: [
          "req.headers.authorization",
          "req.headers.cookie",
          "email",
          "ssn",
          "phone",
          "birthDate",
          "birthdate",
          "password",
          "patientId",
          "patient_id",
          "pseudonym",
          "subject_pseudonym",
          "clinical_identifier",
          "personal_identifier",
        ],
        censor: "[REDACTED]",
      },
    });
  }

  private buildPayload(message: any, context?: string, extra?: any) {
    const correlationId = this.traceContextService.getCorrelationId();
    const payload: Record<string, any> = {};

    if (correlationId) {
      payload.correlationId = correlationId;
      payload.correlation_id = correlationId;
    }

    if (context) {
      payload.context = context;
    }

    if (extra) {
      payload.extra = scrubPii(extra);
    }

    const scrubbedMessage = scrubPii(message);
    if (typeof scrubbedMessage === "object") {
      return { ...payload, ...scrubbedMessage };
    }

    return { ...payload, msg: scrubbedMessage };
  }

  log(message: any, context?: string, ...optionalParams: any[]) {
    const payload = this.buildPayload(message, context, optionalParams);
    this.pinoLogger.info(payload, typeof message === "string" ? scrubPii(message) : undefined);
  }

  error(message: any, trace?: string, context?: string) {
    const extra = trace ? { trace } : undefined;
    const payload = this.buildPayload(message, context, extra);
    this.pinoLogger.error(payload, typeof message === "string" ? scrubPii(message) : undefined);
  }

  warn(message: any, context?: string, ...optionalParams: any[]) {
    const payload = this.buildPayload(message, context, optionalParams);
    this.pinoLogger.warn(payload, typeof message === "string" ? scrubPii(message) : undefined);
  }

  debug(message: any, context?: string, ...optionalParams: any[]) {
    const payload = this.buildPayload(message, context, optionalParams);
    this.pinoLogger.debug(payload, typeof message === "string" ? scrubPii(message) : undefined);
  }

  verbose(message: any, context?: string, ...optionalParams: any[]) {
    const payload = this.buildPayload(message, context, optionalParams);
    this.pinoLogger.trace(payload, typeof message === "string" ? scrubPii(message) : undefined);
  }
}
