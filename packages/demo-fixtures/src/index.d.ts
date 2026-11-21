import type {
  DemoManifestFixture,
  DemoParticipantFixture,
  DemoPersonaFixture,
  DemoRoleFixture,
  DemoSiteFixture,
  DemoSponsorFixture,
  DemoStudyFixture,
} from "./manifest.js";

export type {
  DemoManifestFixture,
  DemoParticipantFixture,
  DemoPersonaFixture,
  DemoRoleFixture,
  DemoSiteFixture,
  DemoSponsorFixture,
  DemoStudyFixture,
};

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
