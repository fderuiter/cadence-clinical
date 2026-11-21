import { encodeChangeReason } from "./changeReason";
import { getBaseUrl, ApiError } from "./client";
import {
  getBearerToken,
  refreshBearerTokenOnUnauthorized,
} from "./authorization.js";
import type { components } from "./generated/kernel";

type Schemas = components["schemas"];

export type OrgTeamView = Schemas["Kernel_OrgTeamViewResponse"];
export type OrgRole = Schemas["Kernel_OrgRoleDto"];
export type OrgScopeOption = Schemas["Kernel_OrgScopeOptionDto"];
export type OrgDelegableGrant = Schemas["Kernel_OrgDelegableGrantDto"];
export type OrgDutyOption = Schemas["Kernel_OrgDutyOptionDto"];
export type OrgTeamMember = Schemas["Kernel_OrgTeamMemberDto"];
export type OrgMemberGrant = Schemas["Kernel_OrgMemberGrantDto"];

export interface ProblemCondition {
  code: string;
  description: string;
  met?: boolean;
  satisfied?: boolean;
  evidence?: string[];
}

export interface ProblemNextAction {
  code?: string;
  action?: string;
  description?: string;
  capability?: string | null;
  target?: string | null;
}

export interface ProblemExplanation {
  subject?: string | null;
  claim: string;
  producer?: string;
  steps?: Array<{ claim: string; evidence?: string[] }>;
  conditions?: ProblemCondition[];
  next_actions?: ProblemNextAction[];
}

export interface OrgProblemPayload {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  explanation?: ProblemExplanation | null;
}

export interface InviteOrgMemberInput {
  email: string;
  full_name?: string | null;
  given_name?: string | null;
  family_name?: string | null;
  role_code?: string | null;
  role_id?: string | null;
  capability?: string | null;
  scope_level: string;
  scope_target_id?: string | null;
  scope_country?: string | null;
  valid_from?: string | null;
  valid_to?: string | null;
  with_grant_option?: boolean;
  reason_for_change: string;
  idempotencyKey?: string;
}

export type InviteOrgMemberResult = Schemas["Kernel_InviteMemberResponse"] & {
  person_ref?: string;
  grant_ref?: string;
};

export interface DelegateOrgDutiesInput {
  email: string;
  full_name?: string | null;
  given_name?: string | null;
  family_name?: string | null;
  duties?: string[] | null;
  capabilities?: string[] | null;
  role_code?: string | null;
  role_id?: string | null;
  scope_level?: string | null;
  scope_target_id?: string | null;
  scope_country?: string | null;
  study_code?: string | null;
  site_code?: string | null;
  parent_grant_id?: string | null;
  valid_from?: string | null;
  valid_to?: string | null;
  with_grant_option?: boolean;
  reason_for_change: string;
  idempotencyKey?: string;
}

export type DelegateOrgDutiesResult =
  Schemas["Kernel_DelegateDutiesResponse"] & {
    person_ref?: string;
  };

export type RevokeOrgGrantResult = Schemas["Kernel_RevokeOrgGrantResponse"];

export type RevealOrgMemberIdentityResult =
  Schemas["Kernel_RevealMemberIdentityResponse"];

function makeIdempotencyKey(prefix: string): string {
  const randomPart =
    typeof globalThis.crypto?.randomUUID === "function"
      ? globalThis.crypto.randomUUID()
      : `${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;
  return `${prefix}:${randomPart}`;
}

async function orgFetch<T>(
  path: string,
  init: {
    method?: "GET" | "POST";
    body?: unknown;
    changeReason?: string;
    idempotencyKey?: string;
  } = {}
): Promise<T> {
  const method = init.method ?? "GET";
  const headers: Record<string, string> = {
    Accept: "application/json",
  };

  const token = await getBearerToken();
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  if (init.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  if (init.changeReason) {
    headers["X-Change-Reason"] = encodeChangeReason(init.changeReason);
  }

  if (init.idempotencyKey) {
    headers["Idempotency-Key"] = init.idempotencyKey;
  }

  const base = getBaseUrl().replace(/\/+$/, "");
  const url = `${base}${path}`;
  const serializedBody =
    init.body !== undefined ? JSON.stringify(init.body) : undefined;

  let response = await globalThis.fetch(url, {
    method,
    headers,
    body: serializedBody,
  });

  if (response.status === 401) {
    const errBody = await response
      .clone()
      .json()
      .catch(() => null);
    const freshToken = await refreshBearerTokenOnUnauthorized(url, errBody);
    if (freshToken) {
      response = await globalThis.fetch(url, {
        method,
        headers: {
          ...headers,
          Authorization: `Bearer ${freshToken}`,
        },
        body: serializedBody,
      });
    }
  }

  if (!response.ok) {
    let payload: OrgProblemPayload | null;
    try {
      payload = (await response.json()) as OrgProblemPayload;
    } catch {
      payload = null;
    }
    const message =
      payload?.detail ||
      payload?.title ||
      `Request failed with status ${response.status}`;
    throw new ApiError(message, response.status, response.statusText, payload);
  }

  return (await response.json()) as T;
}

export function extractOrgProblem(error: unknown): OrgProblemPayload | null {
  const data = (error as { data?: unknown } | null)?.data;
  if (!data || typeof data !== "object") {
    return null;
  }
  return data as OrgProblemPayload;
}

export const orgApi = {
  getTeam(): Promise<OrgTeamView> {
    return orgFetch<OrgTeamView>("/api/v1/org/team");
  },

  inviteMember(input: InviteOrgMemberInput): Promise<InviteOrgMemberResult> {
    const { idempotencyKey, ...rest } = input;
    const payload: Schemas["Kernel_InviteMemberRequest"] = {
      ...rest,
      with_grant_option: Boolean(rest.with_grant_option),
    };
    return orgFetch<InviteOrgMemberResult>("/api/v1/org/invitations", {
      method: "POST",
      body: payload,
      changeReason: input.reason_for_change,
      idempotencyKey: idempotencyKey || makeIdempotencyKey("invite"),
    });
  },

  delegateDuties(
    input: DelegateOrgDutiesInput
  ): Promise<DelegateOrgDutiesResult> {
    const { idempotencyKey, ...rest } = input;
    const payload: Schemas["Kernel_DelegateDutiesRequest"] = {
      ...rest,
      with_grant_option: Boolean(rest.with_grant_option),
    };
    return orgFetch<DelegateOrgDutiesResult>("/api/v1/org/delegations", {
      method: "POST",
      body: payload,
      changeReason: input.reason_for_change,
      idempotencyKey: idempotencyKey || makeIdempotencyKey("delegate"),
    });
  },

  revokeGrant(
    grantId: string,
    reasonForChange: string,
    idempotencyKey?: string
  ): Promise<RevokeOrgGrantResult> {
    const payload: Schemas["Kernel_RevokeOrgGrantRequest"] = {
      reason_for_change: reasonForChange,
    };
    return orgFetch<RevokeOrgGrantResult>(
      `/api/v1/org/grants/${encodeURIComponent(grantId)}/revoke`,
      {
        method: "POST",
        body: payload,
        changeReason: reasonForChange,
        idempotencyKey: idempotencyKey || makeIdempotencyKey("revoke"),
      }
    );
  },

  revealMemberIdentity(
    personId: string,
    reasonForChange: string,
    idempotencyKey?: string
  ): Promise<RevealOrgMemberIdentityResult> {
    return orgFetch<RevealOrgMemberIdentityResult>(
      `/api/v1/org/members/${encodeURIComponent(personId)}/reveal`,
      {
        method: "POST",
        body: { reason_for_change: reasonForChange },
        changeReason: reasonForChange,
        idempotencyKey: idempotencyKey || makeIdempotencyKey("reveal"),
      }
    );
  },
};
