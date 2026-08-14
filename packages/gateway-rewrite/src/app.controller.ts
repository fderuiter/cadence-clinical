import { Controller, All, Req, Res, HttpStatus } from "@nestjs/common";
import { FastifyRequest, FastifyReply } from "fastify";
import { JwksCoalescerService } from "./jwks-coalescer.service.js";
import * as crypto from "crypto";

const SERVICES: Record<string, string> = {
  designer: process.env.DESIGNER_URL || "http://localhost:8001",
  execution: process.env.EXECUTION_URL || "http://localhost:8002",
  etmf: process.env.ETMF_URL || "http://localhost:8003",
  interop: process.env.INTEROP_URL || "http://localhost:8004",
  ctms: process.env.CTMS_URL || "http://localhost:8007",
  notifications: process.env.NOTIFICATIONS_URL || "http://localhost:8006",
  quality: process.env.QUALITY_URL || "http://localhost:8005",
  safety: process.env.SAFETY_URL || "http://localhost:8008",
  tickets: process.env.TICKETS_URL || "http://localhost:8009",
  org: process.env.ORG_URL || "http://localhost:8012",
  eisf: process.env.EISF_URL || "http://localhost:8010",
  econsent: process.env.ECONSENT_URL || "http://localhost:8011",
};

const GATEWAY_SECRET = process.env.GATEWAY_SECRET || "internal-gateway-secret-12345";

class ReplayPreventionCache {
  private usedTokens = new Map<string, number>();

  isReplayed(jti: string | null, token: string, exp: number): boolean {
    const now = Date.now() / 1000;
    
    // Prune expired
    for (const [key, expireTime] of this.usedTokens.entries()) {
      if (expireTime <= now) {
        this.usedTokens.delete(key);
      }
    }

    const key = jti ? jti : token;
    if (this.usedTokens.has(key)) {
      return true;
    }
    this.usedTokens.set(key, exp);
    return false;
  }
}

const replayCache = new ReplayPreventionCache();

function isPathSignatureGated(pathLower: string): boolean {
  if (pathLower.includes("capture-consent")) {
    return true;
  }
  if (pathLower.includes("econsent")) {
    return false;
  }
  const signatureGatedPatterns = [
    "approve",
    "sign-off",
    "unblind",
    "randomize",
    "queries/sync",
    "close",
    "sign",
    "capture-consent",
  ];
  for (const pattern of signatureGatedPatterns) {
    if (pattern === "sign") {
      const segments = pathLower.split("/");
      if (segments.includes("sign")) {
        return true;
      }
    } else if (pathLower.includes(pattern)) {
      return true;
    }
  }
  return false;
}

function resolveRegulatedAction(method: string, path: string, body: any): string | null {
  const methodUpper = method.toUpperCase();
  const pathLower = path.toLowerCase();

  // Rules:
  // 1. CAPA_CLOSE / CAPA_CANCEL
  if ((methodUpper === "POST" || methodUpper === "PUT") && /quality\/capas\/[^/]+\/transition/.test(pathLower)) {
    const toStatus = body?.to_status?.toUpperCase();
    if (toStatus === "CLOSED") return "quality.capa.close";
    if (toStatus === "CANCELLED") return "quality.capa.cancel";
    return null;
  }

  // 2. GRANT_APPROVE
  if ((methodUpper === "PUT" || methodUpper === "PATCH" || methodUpper === "POST") && /ctms\/grants\/[^/]+$/.test(pathLower)) {
    const status = body?.status?.toUpperCase();
    if (status === "APPROVED") return "ctms.grant.approve";
    return null;
  }

  // Path-only rules
  if (pathLower.includes("sdv/bulk-sign-off")) return "execution.sdv.bulk_signoff";
  if (pathLower.includes("approve")) return "execution.form.approve";
  if (pathLower.includes("sign-off")) return "execution.form.signoff";
  if (pathLower.includes("unblind")) return "execution.subject.unblind";
  if (pathLower.includes("randomize")) return "execution.subject.randomize";
  if (pathLower.includes("queries/sync")) return "execution.queries.sync";
  if (pathLower.includes("close")) return "generic.close";

  return null;
}

function verifySigToken(
  sigToken: string | null,
  userId: string,
  requestPath: string,
  secret: Buffer,
  cache: ReplayPreventionCache,
  expectedSemanticAction: string | null = null
): { success: boolean; payloadOrError: any } {
  if (!sigToken) {
    return { success: false, payloadOrError: "21 CFR Part 11 mandate: Re-authentication is required." };
  }

  let sigPayload: any;
  try {
    const parts = sigToken.split(".");
    if (parts.length !== 3) {
      return { success: false, payloadOrError: "Invalid signature token." };
    }
    const [headerB64, payloadB64, signatureB64] = parts;
    const signInput = `${headerB64}.${payloadB64}`;
    const expectedSig = crypto.createHmac("sha256", secret).update(signInput).digest("base64url");
    if (expectedSig !== signatureB64) {
      return { success: false, payloadOrError: "Invalid signature token." };
    }
    sigPayload = JSON.parse(Buffer.from(payloadB64, "base64url").toString("utf-8"));
  } catch (e) {
    return { success: false, payloadOrError: "Invalid signature token." };
  }

  // Check expiration
  const now = Date.now() / 1000;
  if ((sigPayload.exp || 0) < now) {
    return { success: false, payloadOrError: "Signature token has expired." };
  }

  // Check user binding
  if (sigPayload.sub !== userId) {
    return { success: false, payloadOrError: "Signature token user mismatch." };
  }

  // Validate semantic action binding if expected & present in token
  const tokenSemantic = sigPayload.semantic_action;
  if (expectedSemanticAction && tokenSemantic) {
    if (tokenSemantic !== expectedSemanticAction) {
      return { success: false, payloadOrError: "Signature token semantic action mismatch." };
    }
  }

  // Check loose path binding
  const boundAction = sigPayload.action || "";
  if (
    requestPath !== boundAction &&
    !boundAction.includes(requestPath) &&
    !requestPath.includes(boundAction)
  ) {
    return { success: false, payloadOrError: "Signature token action mismatch." };
  }

  // Check replay
  const jti = sigPayload.jti || null;
  if (cache.isReplayed(jti, sigToken, sigPayload.exp || 0)) {
    return { success: false, payloadOrError: "Signature token has already been used." };
  }

  return { success: true, payloadOrError: sigPayload };
}

function generateGatewaySignature(
  userId: string,
  roles: string,
  timestamp: string,
  secret: Buffer,
  changeReason?: string | null,
  siteId?: string | null,
  sponsorId?: string | null,
  unblindedAccess: boolean = false,
  tenantId?: string | null,
  sigToken?: string | null
): string {
  const payload: any = {
    change_reason: changeReason !== undefined && changeReason !== null ? changeReason : "",
    roles: roles,
    timestamp: timestamp,
    user_id: userId,
    site_id: siteId !== undefined && siteId !== null ? siteId : "",
    sponsor_id: sponsorId !== undefined && sponsorId !== null ? sponsorId : "",
    unblinded_access: unblindedAccess,
    tenant_id: tenantId !== undefined && tenantId !== null ? tenantId : "",
  };
  if (sigToken !== undefined && sigToken !== null) {
    payload.sig_token = sigToken;
  }
  
  // Sort keys alphabetically
  const sortedKeys = Object.keys(payload).sort();
  const sortedObj: any = {};
  for (const key of sortedKeys) {
    sortedObj[key] = payload[key];
  }
  
  const serialized = JSON.stringify(sortedObj);
  return crypto.createHmac("sha256", secret).update(serialized).digest("hex");
}

function normalizeScopeValues(
  siteIdInput: any,
  sponsorIdInput: any,
  unblindedAccessInput: any
): [string | null, string | null, boolean] {
  let siteIdVal: string | null = null;
  if (siteIdInput === undefined || siteIdInput === null) {
    siteIdVal = null;
  } else if (Array.isArray(siteIdInput)) {
    siteIdVal = siteIdInput.map(s => String(s).trim()).filter(Boolean).join(",");
    if (!siteIdVal) siteIdVal = null;
  } else {
    const s = String(siteIdInput).trim();
    siteIdVal = s ? s : null;
  }

  let sponsorIdVal: string | null = null;
  if (sponsorIdInput === undefined || sponsorIdInput === null) {
    sponsorIdVal = null;
  } else if (Array.isArray(sponsorIdInput)) {
    sponsorIdVal = sponsorIdInput.map(s => String(s).trim()).filter(Boolean).join(",");
    if (!sponsorIdVal) sponsorIdVal = null;
  } else {
    const s = String(sponsorIdInput).trim();
    sponsorIdVal = s ? s : null;
  }

  let unblindedAccessVal = false;
  if (typeof unblindedAccessInput === "boolean") {
    unblindedAccessVal = unblindedAccessInput;
  } else if (unblindedAccessInput !== undefined && unblindedAccessInput !== null) {
    const valStr = String(unblindedAccessInput).trim().toLowerCase();
    if (["true", "1", "yes"].includes(valStr)) {
      unblindedAccessVal = true;
    }
  }

  return [siteIdVal, sponsorIdVal, unblindedAccessVal];
}

function signHS256(payload: any, secret: string): string {
  const header = { alg: "HS256", typ: "JWT" };
  const headerB64 = Buffer.from(JSON.stringify(header)).toString("base64url");
  const payloadB64 = Buffer.from(JSON.stringify(payload)).toString("base64url");
  const signInput = `${headerB64}.${payloadB64}`;
  const signature = crypto.createHmac("sha256", secret).update(signInput).digest("base64url");
  return `${signInput}.${signature}`;
}

function getTargetUrl(path: string): string | null {
  const cleanPath = path.startsWith("/") ? path.slice(1) : path;

  if (cleanPath.startsWith("designer/")) {
    return `${SERVICES.designer}/${cleanPath.slice("designer/".length)}`;
  } else if (cleanPath.startsWith("execution/")) {
    return `${SERVICES.execution}/${cleanPath.slice("execution/".length)}`;
  } else if (cleanPath.startsWith("etmf/")) {
    return `${SERVICES.etmf}/${cleanPath.slice("etmf/".length)}`;
  } else if (cleanPath.startsWith("interop/")) {
    return `${SERVICES.interop}/${cleanPath.slice("interop/".length)}`;
  } else if (cleanPath.startsWith("ctms/")) {
    return `${SERVICES.ctms}/${cleanPath.slice("ctms/".length)}`;
  } else if (cleanPath.startsWith("notifications/")) {
    return `${SERVICES.notifications}/${cleanPath.slice("notifications/".length)}`;
  } else if (cleanPath.startsWith("quality/")) {
    return `${SERVICES.quality}/${cleanPath.slice("quality/".length)}`;
  } else if (cleanPath.startsWith("safety/")) {
    return `${SERVICES.safety}/${cleanPath.slice("safety/".length)}`;
  } else if (cleanPath.startsWith("tickets/")) {
    return `${SERVICES.tickets}/${cleanPath.slice("tickets/".length)}`;
  } else if (cleanPath.startsWith("eisf/")) {
    return `${SERVICES.eisf}/${cleanPath.slice("eisf/".length)}`;
  } else if (cleanPath.startsWith("api/v1/terminology")) {
    return `${SERVICES.designer}/${cleanPath}`;
  } else if (cleanPath.startsWith("terminology/")) {
    return `${SERVICES.designer}/${cleanPath.slice("terminology/".length)}`;
  } else if (cleanPath.startsWith("api/v1/studies") || cleanPath.startsWith("api/v2/studies")) {
    return `${SERVICES.designer}/${cleanPath}`;
  } else if (cleanPath.startsWith("api/v1/execution") || cleanPath.startsWith("dictionary/")) {
    return `${SERVICES.execution}/${cleanPath}`;
  } else if (cleanPath.startsWith("econsent/")) {
    return `${SERVICES.econsent}/${cleanPath.slice("econsent/".length)}`;
  } else if (
    cleanPath.startsWith("api/v1/cdisc") ||
    cleanPath.startsWith("api/v1/ecoa") ||
    cleanPath.startsWith("api/v1/usdm") ||
    cleanPath === "openapi.json" ||
    cleanPath === "docs" ||
    cleanPath === "redoc" ||
    cleanPath.startsWith("docs/") ||
    cleanPath.startsWith("redoc/")
  ) {
    const pythonGatewayUrl = process.env.PYTHON_GATEWAY_URL || "http://localhost:8014";
    return `${pythonGatewayUrl}/${cleanPath}`;
  }

  return null;
}

@Controller()
export class AppController {
  constructor(private readonly jwksCoalescer: JwksCoalescerService) {}

  @All("*")
  async handleAll(@Req() req: FastifyRequest, @Res() res: FastifyReply) {
    // 1. Strip trailing slash or prefix for clean matching
    let path = req.url;
    if (path.includes("?")) {
      path = path.split("?")[0];
    }
    const cleanPath = path.startsWith("/") ? path.slice(1) : path;

    // 2. Health check route
    if (cleanPath === "health" || cleanPath === "") {
      return res.status(HttpStatus.OK).send({ status: "ok", service: "gateway" });
    }

    // 3. Demo session endpoints
    if (cleanPath === "api/v1/auth/demo" || cleanPath === "api/v1/auth/demo-session") {
      const body = req.body as any;
      const username = body?.username || "demo-user";
      const roles = body?.roles || ["site investigator", "cra", "admin", "auditor"];
      let tenant_id = body?.tenant_id || "sandbox-tenant-default";

      if (!tenant_id.toLowerCase().startsWith("sandbox")) {
        tenant_id = `sandbox-${tenant_id}`;
      }

      const now = Math.floor(Date.now() / 1000);
      const payload = {
        sub: `demo-sub-${crypto.randomUUID()}`,
        preferred_username: username,
        username: username,
        tenant_id: tenant_id,
        roles: roles,
        realm_access: { roles: roles },
        custom_attributes: { tenant_id: tenant_id },
        iat: now,
        exp: now + 86400, // 24 hours
        jti: crypto.randomUUID(),
      };

      const token = signHS256(payload, GATEWAY_SECRET);
      return res.status(HttpStatus.OK).send({
        access_token: token,
        token_type: "Bearer",
        expires_in: 86400,
        tenant_id: tenant_id,
        username: username,
        roles: roles,
      });
    }

    // 4. Inbound email webhook bypasses auth checks
    if (cleanPath === "api/v1/etmf/inbound-email") {
      const targetUrl = `${SERVICES.etmf}/${cleanPath}`;
      return this.executeProxy(targetUrl, req, res, null);
    }

    // 5. Authentication verification
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
      return res.status(HttpStatus.UNAUTHORIZED).send({
        detail: "Missing or invalid Authorization header",
      });
    }

    const token = authHeader.split(" ")[1];
    let payload: any;
    try {
      payload = await this.jwksCoalescer.verifyToken(token);
    } catch (e: any) {
      return res.status(HttpStatus.UNAUTHORIZED).send({
        detail: e.message || "Invalid token",
      });
    }

    const userId = payload.sub || "";

    // 6. Signature gated mutation validation
    const isMutation = ["POST", "PUT", "DELETE", "PATCH"].includes(req.method.toUpperCase());
    const resolvedAction = resolveRegulatedAction(req.method, cleanPath, req.body);
    const isSignatureGated = resolvedAction !== null || isPathSignatureGated(cleanPath.toLowerCase());

    const sigToken = (req.headers["x-sig-token"] || req.headers["X-Sig-Token"]) as string | undefined;

    if (isSignatureGated && isMutation) {
      const { success, payloadOrError } = verifySigToken(
        sigToken || null,
        userId,
        req.url, // python uses request.url.path which might match bound path
        Buffer.from(GATEWAY_SECRET),
        replayCache,
        resolvedAction
      );

      if (!success) {
        return res.status(HttpStatus.UNAUTHORIZED).send({
          detail: "REAUTHENTICATION_REQUIRED",
          error: "REAUTHENTICATION_REQUIRED",
          message: payloadOrError,
        });
      }
    }

    // 7. Role extraction
    const rolesSet = new Set<string>();
    const realmAccess = payload.realm_access || {};
    if (typeof realmAccess === "object" && Array.isArray(realmAccess.roles)) {
      for (const r of realmAccess.roles) {
        rolesSet.add(String(r));
      }
    }
    const rolesList = payload.roles;
    if (Array.isArray(rolesList)) {
      for (const r of rolesList) {
        rolesSet.add(String(r));
      }
    } else if (rolesList) {
      rolesSet.add(String(rolesList));
    }

    const resourceAccess = payload.resource_access || {};
    if (typeof resourceAccess === "object") {
      for (const clientData of Object.values(resourceAccess)) {
        if (clientData && typeof clientData === "object" && Array.isArray((clientData as any).roles)) {
          for (const r of (clientData as any).roles) {
            rolesSet.add(String(r));
          }
        }
      }
    }

    const rolesString = Array.from(rolesSet).sort().join(",");

    // 8. Subject / Patient security routing checks
    const userRolesLower = Array.from(rolesSet).map(r => r.trim().toLowerCase());
    if (userRolesLower.includes("subject")) {
      const normalizedPath = cleanPath.startsWith("interop/") ? cleanPath.slice("interop/".length) : cleanPath;
      const parts = normalizedPath.split("/").filter(Boolean);
      let isAllowed = false;
      const method = req.method.toUpperCase();

      if (parts.length === 5) {
        if (
          (parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop" && parts[3] === "epro" && parts[4] === "submit" && method === "POST") ||
          (parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop" && parts[3] === "epro" && parts[4] === "sync" && method === "POST") ||
          (parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop" && parts[3] === "instruments" && method === "GET")
        ) {
          isAllowed = true;
        }
      } else if (parts.length === 6) {
        if (parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop" && parts[3] === "assignments" && parts[4] === "subject" && method === "GET") {
          if (parts[5] === userId) {
            isAllowed = true;
          }
        } else if (
          parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop" && parts[3] === "subjects" && parts[5] === "instruments" && method === "GET" ||
          parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop" && parts[3] === "subjects" && parts[5] === "compliance" && method === "GET" ||
          parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop" && parts[3] === "subjects" && parts[5] === "notifications" && method === "GET"
        ) {
          if (parts[4] === userId) {
            isAllowed = true;
          }
        } else if (parts[0] === "api" && parts[1] === "v1" && parts[2] === "interop" && parts[3] === "notifications" && parts[5] === "acknowledge" && method === "POST") {
          isAllowed = true;
        }
      }

      if (!isAllowed) {
        return res.status(HttpStatus.FORBIDDEN).send({
          detail: "Access denied: Subject principal is not authorized to access this route",
        });
      }
    }

    // 9. Tenant Isolation Gate
    const customAttrs = payload.custom_attributes || {};
    let tenantIdVal = customAttrs.tenant_id || payload.tenant_id || "";
    if (typeof tenantIdVal !== "string") {
      tenantIdVal = String(tenantIdVal);
    }
    tenantIdVal = tenantIdVal.trim();
    if (!tenantIdVal) {
      tenantIdVal = "tenant_default";
    }

    const tokenTenantId = payload.tenant_id || customAttrs.tenant_id || null;
    if (tokenTenantId && String(tokenTenantId).trim().toLowerCase().startsWith("sandbox")) {
      if (!tenantIdVal.toLowerCase().startsWith("sandbox")) {
        return res.status(HttpStatus.FORBIDDEN).send({
          detail: "Access denied: Sandbox token cannot access non-sandbox resources",
        });
      }

      const queryParams: any = req.query || {};
      const reqTenant = queryParams.tenant_id || queryParams.tenant;
      if (reqTenant && !String(reqTenant).trim().toLowerCase().startsWith("sandbox")) {
        return res.status(HttpStatus.FORBIDDEN).send({
          detail: "Access denied: Sandbox token cannot access non-sandbox resources",
        });
      }
    }

    // 10. Extract change reason, site_id, sponsor_id, unblinded_access
    const changeReason = (req.headers["x-change-reason"] || req.headers["X-Change-Reason"]) as string | undefined;
    if (changeReason && changeReason.length > 255) {
      return res.status(HttpStatus.BAD_REQUEST).send({
        detail: "Change reason exceeds 255 characters",
      });
    }

    const rawSiteId = payload.site_id;
    const rawSponsorId = customAttrs.sponsor_id || payload.sponsor_id || "";
    const rawUnblindedAccess = payload.unblinded_access || false;

    const [siteIdVal, sponsorIdVal, unblindedAccessVal] = normalizeScopeValues(
      rawSiteId,
      rawSponsorId,
      rawUnblindedAccess
    );

    // 11. Generate Gateway Signature
    const timestamp = Math.floor(Date.now() / 1000).toString();
    const signature = generateGatewaySignature(
      userId,
      rolesString,
      timestamp,
      Buffer.from(GATEWAY_SECRET),
      changeReason,
      siteIdVal,
      sponsorIdVal,
      unblindedAccessVal,
      tenantIdVal,
      sigToken
    );

    // 12. Determine target URL
    const targetUrl = getTargetUrl(req.url);
    if (!targetUrl) {
      return res.status(HttpStatus.NOT_FOUND).send({
        detail: `Route ${req.url} not found`,
      });
    }

    // 13. Inject Gateway-authenticated identity headers
    const headersToInject: Record<string, string> = {
      "X-User-Id": userId,
      "X-User-Roles": rolesString,
      "X-Gateway-Timestamp": timestamp,
      "X-Gateway-Signature": signature,
      "X-Signature-Version": "2",
      "X-Tenant-Id": tenantIdVal,
    };

    if (changeReason) {
      headersToInject["X-Change-Reason"] = changeReason;
    }
    if (siteIdVal) {
      headersToInject["X-Site-Id"] = siteIdVal;
    }
    if (sponsorIdVal) {
      headersToInject["X-Sponsor-Id"] = sponsorIdVal;
    }
    if (unblindedAccessVal) {
      headersToInject["X-Unblinded-Access"] = "true";
    }
    if (sigToken) {
      headersToInject["X-Sig-Token"] = sigToken;
    }

    return this.executeProxy(targetUrl, req, res, headersToInject);
  }

  private async executeProxy(
    targetUrl: string,
    req: FastifyRequest,
    res: FastifyReply,
    headersToInject: Record<string, string> | null
  ) {
    const proxiedHeaders = { ...req.headers } as Record<string, string>;
    delete proxiedHeaders.host;
    delete proxiedHeaders["content-length"];

    // Clean up incoming headers to prevent client-side spoofing
    const spoofHeaders = [
      "x-user-id",
      "x-user-roles",
      "x-gateway-timestamp",
      "x-gateway-signature",
      "x-signature-version",
      "x-change-reason",
      "x-site-id",
      "x-sponsor-id",
      "x-unblinded-access",
      "x-tenant-id",
    ];
    for (const h of spoofHeaders) {
      delete proxiedHeaders[h];
      delete proxiedHeaders[h.toLowerCase()];
    }

    if (headersToInject) {
      for (const [k, v] of Object.entries(headersToInject)) {
        proxiedHeaders[k] = v;
      }
    }

    let requestBody: any = undefined;
    if (req.method !== "GET" && req.method !== "HEAD") {
      if (req.body !== undefined && req.body !== null) {
        if (typeof req.body === "object") {
          requestBody = JSON.stringify(req.body);
        } else {
          requestBody = req.body;
        }
      }
    }

    try {
      const response = await fetch(targetUrl, {
        method: req.method,
        headers: proxiedHeaders,
        body: requestBody,
      });

      const responseHeaders = {} as Record<string, string>;
      response.headers.forEach((val, key) => {
        const lowerKey = key.toLowerCase();
        if (lowerKey !== "transfer-encoding" && lowerKey !== "content-encoding" && lowerKey !== "content-length") {
          responseHeaders[key] = val;
        }
      });

      const responseBody = await response.arrayBuffer();
      res.status(response.status).headers(responseHeaders);
      return res.send(Buffer.from(responseBody));
    } catch (err: any) {
      return res.status(HttpStatus.BAD_GATEWAY).send({
        detail: `Bad Gateway: ${err.message}`,
      });
    }
  }
}
