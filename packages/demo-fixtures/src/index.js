import demoSeed from "../demo-seed.json" with { type: "json" };

const SHA256_K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1,
  0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
  0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786,
  0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
  0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
  0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
  0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a,
  0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
  0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);

const SHA256_H0 = new Uint32Array([
  0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c,
  0x1f83d9ab, 0x5be0cd19,
]);

const utf8Encoder = new TextEncoder();
const DAY_MS = 86_400_000; // deid-ignore

function rotr(value, shift) {
  return (value >>> shift) | (value << (32 - shift));
}

function sha256Bytes(input) {
  const msg = utf8Encoder.encode(input);
  const bitLenHi = Math.floor((msg.length * 8) / 0x100000000); // deid-ignore
  const bitLenLo = (msg.length * 8) >>> 0;
  const paddedLength = (((msg.length + 9 + 63) >> 6) << 6) >>> 0;
  const padded = new Uint8Array(paddedLength);
  padded.set(msg);
  padded[msg.length] = 0x80;
  const view = new DataView(padded.buffer);
  view.setUint32(paddedLength - 8, bitLenHi, false);
  view.setUint32(paddedLength - 4, bitLenLo, false);

  const state = new Uint32Array(SHA256_H0);
  const w = new Uint32Array(64);

  for (let offset = 0; offset < paddedLength; offset += 64) {
    for (let i = 0; i < 16; i += 1) {
      w[i] = view.getUint32(offset + i * 4, false);
    }
    for (let i = 16; i < 64; i += 1) {
      const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >>> 3);
      const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >>> 10);
      w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0;
    }

    let [a, b, c, d, e, f, g, h] = state;
    for (let i = 0; i < 64; i += 1) {
      const s1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
      const ch = (e & f) ^ (~e & g);
      const temp1 = (h + s1 + ch + SHA256_K[i] + w[i]) >>> 0;
      const s0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
      const maj = (a & b) ^ (a & c) ^ (b & c);
      const temp2 = (s0 + maj) >>> 0;

      h = g;
      g = f;
      f = e;
      e = (d + temp1) >>> 0;
      d = c;
      c = b;
      b = a;
      a = (temp1 + temp2) >>> 0;
    }

    state[0] = (state[0] + a) >>> 0;
    state[1] = (state[1] + b) >>> 0;
    state[2] = (state[2] + c) >>> 0;
    state[3] = (state[3] + d) >>> 0;
    state[4] = (state[4] + e) >>> 0;
    state[5] = (state[5] + f) >>> 0;
    state[6] = (state[6] + g) >>> 0;
    state[7] = (state[7] + h) >>> 0;
  }

  const digest = new Uint8Array(32);
  const outView = new DataView(digest.buffer);
  for (let i = 0; i < 8; i += 1) {
    outView.setUint32(i * 4, state[i], false);
  }
  return digest;
}

function formatUuidBytes(bytes) {
  let hex = "";
  for (let i = 0; i < 16; i += 1) {
    hex += bytes[i].toString(16).padStart(2, "0");
  }
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
}

/**
 * Derives an RFC 9562 version 8 UUID over SHA-256 matching C# `DeriveUuidV8`
 * (`cadence-development-demo:{tenantScope}:{kind}:{name}`).
 */
export function deriveDemoUuidV8(tenantScope, kind, name) {
  const hash = sha256Bytes(
    `cadence-development-demo:${tenantScope}:${kind}:${name}`
  );
  const bytes = hash.subarray(0, 16);
  bytes[6] = (bytes[6] & 0x0f) | 0x80;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  return formatUuidBytes(bytes);
}

function formatUtcIso(ms) {
  return new Date(ms).toISOString().replace(".000Z", "Z");
}

function synthesizeDemoManifest(seed) {
  const tenantCode = seed.tenantCode || "cadence-local";
  const tenantUuid = deriveDemoUuidV8(tenantCode, "tenant", tenantCode);
  const epochMs = Date.parse(seed.epoch);
  const epochIso = formatUtcIso(epochMs);

  const sponsor = Object.freeze({
    code: seed.sponsor.code,
    name: seed.sponsor.name,
    organizationUuid: deriveDemoUuidV8(tenantUuid, "organization", "sponsor"),
  });

  const studyUuid = deriveDemoUuidV8(tenantUuid, "study", seed.study.code);
  const study = Object.freeze({
    code: seed.study.code,
    title: seed.study.title,
    phase: seed.study.phase,
    sponsorCode: seed.sponsor.code,
    uuid: studyUuid,
    ref: `study:${studyUuid}`,
  });

  const sites = Object.freeze(
    seed.sites.map((rawSite) => {
      const institutionUuid = deriveDemoUuidV8(
        tenantUuid,
        "organization",
        rawSite.code
      );
      const siteUuid = deriveDemoUuidV8(tenantUuid, "site", rawSite.code);
      const studySiteUuid = deriveDemoUuidV8(
        tenantUuid,
        "study-site",
        rawSite.code
      );
      return Object.freeze({
        code: rawSite.code,
        name: rawSite.name,
        country: rawSite.country,
        institutionCode: rawSite.institutionCode,
        complianceStage: rawSite.complianceStage || "CONDUCT",
        institutionUuid,
        siteUuid,
        studySiteUuid,
        studySiteRef: `study_site:${studySiteUuid}`,
      });
    })
  );

  const participants = Object.freeze(
    seed.participants.map((rawParticipant, idx) => {
      const participantUuid = deriveDemoUuidV8(
        tenantUuid,
        "participant",
        rawParticipant.subjectCode
      );
      const enrollmentUuid = deriveDemoUuidV8(
        tenantUuid,
        "enrollment",
        rawParticipant.subjectCode
      );
      return Object.freeze({
        subjectCode: rawParticipant.subjectCode,
        siteCode: rawParticipant.siteCode,
        enrollmentIndex: idx,
        status: rawParticipant.status || "SCREENED",
        screenedAt: formatUtcIso(epochMs + (7 + idx) * DAY_MS),
        participantUuid,
        enrollmentUuid,
        enrollmentRef: `enrollment:${enrollmentUuid}`,
      });
    })
  );

  const roles = Object.freeze(
    seed.roles.map((rawRole) =>
      Object.freeze({
        code: rawRole.code,
        name: rawRole.name,
        roleUuid: deriveDemoUuidV8(tenantUuid, "role", rawRole.code),
        capabilities: Object.freeze([...rawRole.capabilities]),
      })
    )
  );

  const personas = Object.freeze(
    seed.users.map((rawUser) =>
      Object.freeze({
        username: rawUser.username,
        id: rawUser.subject,
        email: rawUser.email,
        firstName: rawUser.firstName,
        lastName: rawUser.lastName,
        tenantId: rawUser.tenantId || tenantCode,
        kernelRole: rawUser.role,
        workflow: rawUser.workflow,
        workspacePersona: rawUser.workspacePersona,
        siteCode: rawUser.site ?? null,
        grantUuid: deriveDemoUuidV8(tenantUuid, "grant", rawUser.username),
        realmRoles: Object.freeze([...(rawUser.realmRoles || [])]),
      })
    )
  );

  return Object.freeze({
    tenantCode,
    tenantUuid,
    epochIso,
    epochMs,
    sponsor,
    study,
    sites,
    participants,
    roles,
    personas,
  });
}

export const DEMO_MANIFEST = synthesizeDemoManifest(demoSeed);

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
