import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  DEMO_EPOCH_ISO,
  DEMO_MANIFEST,
  DEMO_PARTICIPANTS,
  DEMO_PERSONAS,
  DEMO_SITES,
  DEMO_SPONSOR,
  DEMO_STUDY,
} from "demo-fixtures";
import { useAuthStore } from "@/stores/auth";
import { useClinicalStore } from "@/stores/clinical";
import {
  demoNowIso,
  demoRandom,
  demoRandomToken,
  resetDemoRandom,
} from "@/utils/demoMode";

describe("Canonical Demo Manifest & Deterministic Demo Ledger (apps/web)", () => {
  beforeEach(() => {
    window.localStorage.clear();
    resetDemoRandom();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
    resetDemoRandom();
  });

  it("exports canonical CADENCE-101 study, sites, participants, and all eight synthetic realm personas", () => {
    expect(DEMO_EPOCH_ISO).toBe("2026-01-05T09:00:00Z");
    expect(DEMO_STUDY.code).toBe("CADENCE-101");
    expect(DEMO_SPONSOR.code).toBe("CADENCE-PHARMA");
    expect(DEMO_SITES.map((s) => s.code)).toEqual(["SITE-101", "SITE-102"]);
    expect(DEMO_PARTICIPANTS.map((p) => p.subjectCode)).toEqual([
      "SUBJ-101-001",
      "SUBJ-101-002",
      "SUBJ-102-001",
    ]);
    expect(DEMO_PERSONAS.map((p) => p.username)).toEqual([
      "crc.site101",
      "pi.site101",
      "cra.monitor",
      "dm.lead",
      "designer_demo",
      "designer.lead",
      "safety.officer",
      "auditor.lead",
    ]);

    const serialized = JSON.stringify(DEMO_MANIFEST);
    expect(serialized).not.toMatch(/frans|de ruiter|fderuiter/i);
  });

  it("switches across all synthetic role-based personas in demo mode while preserving OIDC claims outside demo mode", () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    setActivePinia(createPinia());
    const authStore = useAuthStore();
    authStore.startDemoSession();

    const expectedByPersona: Record<string, string> = {
      super_admin: "designer.lead",
      sponsor_designer: "designer_demo",
      data_manager: "dm.lead",
      site_crc: "crc.site101",
      site_investigator: "pi.site101",
      cra_monitor: "cra.monitor",
      safety_officer: "safety.officer",
      auditor: "auditor.lead",
    };

    for (const [personaKey, expectedUsername] of Object.entries(
      expectedByPersona
    )) {
      authStore.switchPersona(personaKey);
      const manifestPersona = DEMO_PERSONAS.find(
        (p) => p.username === expectedUsername
      );
      expect(authStore.identity?.username).toBe(expectedUsername);
      expect(authStore.identity?.email).toBe(manifestPersona?.email);
      expect(authStore.identity?.id).toBe(manifestPersona?.id);
      expect(JSON.stringify(authStore.identity)).not.toMatch(
        /frans|de ruiter|fderuiter/i
      );
    }

    // Outside demo mode, authenticated OIDC claims must not be overwritten with synthetic personas
    vi.stubEnv("VITE_DEMO_MODE", "false");
    setActivePinia(createPinia());
    const prodAuthStore = useAuthStore();
    prodAuthStore.isDemoMode = false;
    prodAuthStore.isAuthenticated = true;
    prodAuthStore.user = {
      username: "oidc.clinician",
      email: "oidc.clinician@hospital.org", // deid-ignore
      firstName: "Authenticated",
      lastName: "Clinician",
      id: "sub-oidc-999",
      tenantId: "tenant-prod",
    };
    prodAuthStore.rawRoles = [
      "Sponsor Admin",
      "Study Designer",
      "Data Manager",
      "CRC",
    ];
    prodAuthStore.switchPersona("site_crc");
    expect(prodAuthStore.identity.username).toBe("oidc.clinician");
  });

  it("produces identical SHA-256 Genesis hashes and seeded random sequences across repeated demo boots", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");

    const bootGenesisAndSample = async () => {
      window.localStorage.clear();
      resetDemoRandom();
      setActivePinia(createPinia());
      const auth = useAuthStore();
      auth.startDemoSession();
      const clinical = useClinicalStore();
      await clinical.addLedgerBlock(
        "GENESIS",
        {
          platform: "Cadence Clinical",
          compliance: "21 CFR Part 11",
        },
        "System initialized with immutable audit ledger."
      );
      const telemetrySeries = Array.from({ length: 6 }, () =>
        Math.round(92 + demoRandom("sensor-telemetry.glucose-series") * 48)
      );
      const tokenSample = demoRandomToken(8, 36, "audit.export-id");
      return {
        studyId: clinical.activeStudyId,
        siteId: clinical.activeSiteId,
        subjectId: clinical.activeSubjectId,
        genesis: clinical.ledgerBlocks[0],
        telemetrySeries,
        tokenSample,
      };
    };

    const firstBoot = await bootGenesisAndSample();
    const secondBoot = await bootGenesisAndSample();

    expect(firstBoot.studyId).toBe("CADENCE-101");
    expect(firstBoot.siteId).toBe("SITE-101");
    expect(firstBoot.subjectId).toBe("SUBJ-101-001");
    expect(firstBoot.genesis.timestamp).toBe("2026-01-05T09:00:00.000Z");
    expect(firstBoot.genesis.hash).toMatch(/^[0-9a-f]{64}$/);
    expect(secondBoot.genesis).toEqual(firstBoot.genesis);
    expect(secondBoot.telemetrySeries).toEqual(firstBoot.telemetrySeries);
    expect(secondBoot.tokenSample).toBe(firstBoot.tokenSample);
  });

  it("uses real system clock and Web Crypto outside demo builds", () => {
    vi.stubEnv("VITE_DEMO_MODE", "false");
    const cryptoSpy = vi.spyOn(globalThis.crypto, "getRandomValues");

    const liveIso = demoNowIso(0);
    expect(liveIso).not.toBe("2026-01-05T09:00:00.000Z");

    const sample = demoRandom("non-demo-stream");
    expect(sample).toBeGreaterThanOrEqual(0);
    expect(sample).toBeLessThan(1);
    expect(cryptoSpy).toHaveBeenCalled();
  });
});
