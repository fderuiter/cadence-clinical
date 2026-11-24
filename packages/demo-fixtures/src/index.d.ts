export interface DemoSponsorFixture {
  readonly code: string;
  readonly name: string;
  readonly organizationUuid: string;
}

export interface DemoStudyFixture {
  readonly code: string;
  readonly title: string;
  readonly phase: string;
  readonly sponsorCode: string;
  readonly uuid: string;
  readonly ref: string;
}

export interface DemoSiteFixture {
  readonly code: string;
  readonly name: string;
  readonly country: string;
  readonly institutionCode: string;
  readonly complianceStage: string;
  readonly institutionUuid: string;
  readonly siteUuid: string;
  readonly studySiteUuid: string;
  readonly studySiteRef: string;
}

export interface DemoParticipantFixture {
  readonly subjectCode: string;
  readonly siteCode: string;
  readonly enrollmentIndex: number;
  readonly status: string;
  readonly screenedAt: string;
  readonly participantUuid: string;
  readonly enrollmentUuid: string;
  readonly enrollmentRef: string;
}

export interface DemoRoleFixture {
  readonly code: string;
  readonly name: string;
  readonly roleUuid: string;
  readonly capabilities: readonly string[];
}

export interface DemoPersonaFixture {
  readonly username: string;
  readonly id: string;
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly tenantId: string;
  readonly kernelRole: string;
  readonly workflow: string;
  readonly workspacePersona: string;
  readonly siteCode: string | null;
  readonly grantUuid: string;
  readonly realmRoles: readonly string[];
}

export interface DemoManifestFixture {
  readonly tenantCode: string;
  readonly tenantUuid: string;
  readonly epochIso: string;
  readonly epochMs: number;
  readonly sponsor: DemoSponsorFixture;
  readonly study: DemoStudyFixture;
  readonly sites: readonly DemoSiteFixture[];
  readonly participants: readonly DemoParticipantFixture[];
  readonly roles: readonly DemoRoleFixture[];
  readonly personas: readonly DemoPersonaFixture[];
}

export interface DemoAuthUser {
  readonly username: string;
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly id: string;
  readonly tenantId: string;
}

export declare const DEMO_MANIFEST: DemoManifestFixture;
export declare const DEMO_TENANT_CODE: string;
export declare const DEMO_TENANT_UUID: string;
export declare const DEMO_EPOCH_ISO: string;
export declare const DEMO_EPOCH_MS: number;
export declare const DEMO_SPONSOR: DemoSponsorFixture;
export declare const DEMO_STUDY: DemoStudyFixture;
export declare const DEMO_SITES: readonly DemoSiteFixture[];
export declare const DEMO_PARTICIPANTS: readonly DemoParticipantFixture[];
export declare const DEMO_ROLES: readonly DemoRoleFixture[];
export declare const DEMO_PERSONAS: readonly DemoPersonaFixture[];
export declare const DEFAULT_DEMO_SITE: DemoSiteFixture | null;
export declare const DEFAULT_DEMO_PARTICIPANT: DemoParticipantFixture | null;
export declare const DEMO_PERSONA_BY_WORKFLOW: Readonly<
  Record<string, DemoPersonaFixture>
>;
export declare const DEMO_PERSONA_BY_WORKSPACE: Readonly<
  Record<string, DemoPersonaFixture>
>;

export declare function getDemoSiteByCode(
  siteCode?: string | null
): DemoSiteFixture | null;
export declare function getDemoParticipantByCode(
  subjectCode?: string | null
): DemoParticipantFixture | null;
export declare function getDemoPersonaByUsername(
  username?: string | null
): DemoPersonaFixture | null;
export declare function getDemoPersonaByWorkflow(
  workflow?: string | null
): DemoPersonaFixture | null;
export declare function getDemoPersonaForWorkspace(
  workspacePersona?: string | null
): DemoPersonaFixture | null;
export declare function toDemoAuthUser(
  persona?: DemoPersonaFixture | null
): DemoAuthUser | null;
