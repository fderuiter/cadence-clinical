import { DEMO_MANIFEST } from "./manifest.js";

export { DEMO_MANIFEST };

export const DEMO_TENANT_CODE = DEMO_MANIFEST.tenantCode;
export const DEMO_TENANT_UUID = DEMO_MANIFEST.tenantUuid;
export const DEMO_EPOCH_ISO = DEMO_MANIFEST.epochIso;
export const DEMO_EPOCH_MS = DEMO_MANIFEST.epochMs;
export const DEMO_SPONSOR = DEMO_MANIFEST.sponsor;
export const DEMO_STUDY = DEMO_MANIFEST.study;
export const DEMO_SITES = DEMO_MANIFEST.sites;
export const DEMO_PARTICIPANTS = DEMO_MANIFEST.participants;
export const DEMO_ROLES = DEMO_MANIFEST.roles;
export const DEMO_PERSONAS = DEMO_MANIFEST.personas;

export const DEFAULT_DEMO_SITE = DEMO_SITES[0] || null;
export const DEFAULT_DEMO_PARTICIPANT = DEMO_PARTICIPANTS[0] || null;

export const DEMO_PERSONA_BY_WORKFLOW = Object.freeze(
  Object.fromEntries(
    DEMO_PERSONAS.map((persona) => [persona.workflow, persona])
  )
);

export const DEMO_PERSONA_BY_WORKSPACE = Object.freeze(
  Object.fromEntries(
    DEMO_PERSONAS.map((persona) => [persona.workspacePersona, persona])
  )
);

export function getDemoSiteByCode(siteCode) {
  if (!siteCode) return null;
  const normalized = String(siteCode).trim().toUpperCase();
  return DEMO_SITES.find((site) => site.code === normalized) || null;
}

export function getDemoParticipantByCode(subjectCode) {
  if (!subjectCode) return null;
  const normalized = String(subjectCode).trim().toUpperCase();
  return (
    DEMO_PARTICIPANTS.find(
      (participant) => participant.subjectCode === normalized
    ) || null
  );
}

export function getDemoPersonaByUsername(username) {
  if (!username) return null;
  const normalized = String(username).trim().toLowerCase();
  return (
    DEMO_PERSONAS.find(
      (persona) => persona.username.toLowerCase() === normalized
    ) || null
  );
}

export function getDemoPersonaByWorkflow(workflow) {
  if (!workflow) return null;
  return (
    DEMO_PERSONA_BY_WORKFLOW[String(workflow).trim().toLowerCase()] || null
  );
}

export function getDemoPersonaForWorkspace(workspacePersona) {
  if (!workspacePersona) {
    return DEMO_PERSONA_BY_WORKSPACE.super_admin || DEMO_PERSONAS[0] || null;
  }
  return (
    DEMO_PERSONA_BY_WORKSPACE[String(workspacePersona).trim()] ||
    getDemoPersonaByWorkflow(workspacePersona) ||
    getDemoPersonaByUsername(workspacePersona) ||
    DEMO_PERSONA_BY_WORKSPACE.super_admin ||
    DEMO_PERSONAS[0] ||
    null
  );
}

export function toDemoAuthUser(persona) {
  if (!persona) return null;
  return Object.freeze({
    username: persona.username,
    email: persona.email,
    firstName: persona.firstName,
    lastName: persona.lastName,
    id: persona.id,
    tenantId: persona.tenantId,
  });
}
