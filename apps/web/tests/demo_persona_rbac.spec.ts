import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import { setupServer } from "msw/node";
import { createPinia, setActivePinia } from "pinia";
import { DEMO_PARTICIPANTS, DEMO_SITES } from "demo-fixtures";
import { apiClient } from "../src/api/apiClient.js";
import {
  getDemoIdentityHeaders,
  installAuthorizedFetch,
} from "../src/api/authorization.js";
import { createOpenApiClient } from "../src/api/client";
import {
  canAccessDataCollection,
  resolveSiteReadinessPath,
} from "../src/canvas/palette";
import { canManageParticipants } from "../src/canvas/authority";
import AppShell from "../src/components/AppShell.vue";
import { createWebDemoStore, webDemoStore } from "../src/mocks/demoStore.js";
import { buildWebGatewayHandlers } from "../src/mocks/handlers.js";
import { isRouteAuthorizedForRoles, router } from "../src/router";
import { PERSONA_PRESETS, useAuthStore } from "../src/stores/auth.js";
import { useClinicalStore } from "../src/stores/clinical";
import { useEtmfStore } from "../src/stores/etmf.js";
import { useNotificationsStore } from "../src/stores/notifications.js";
import { useOnboardingStore } from "../src/stores/onboarding.js";
import { useStudyCanvasStore } from "../src/stores/studyCanvas";
import {
  hasUserScopedDrafts,
  scopedKey,
  storageScopeOf,
} from "../src/utils/userScopedStorage.js";
import LoginView from "../src/views/LoginView.vue";

function createFakeKeycloak(roles: string[]) {
  return {
    authenticated: true,
    token: "oidc-real-access-token",
    idToken: "oidc-real-id-token",
    tokenParsed: {
      sub: "oidc-sub-01",
      preferred_username: "oidc.admin",
      email: "oidc.admin@cadence.clinical",
      given_name: "Oidc",
      family_name: "Admin",
      tenant_id: "tenant-alpha",
      exp: Math.floor(Date.now() / 1000) + 3600,
      realm_access: { roles },
    },
    logout: vi.fn(async () => {}),
    login: vi.fn(async () => {}),
    updateToken: vi.fn(async () => false),
  };
}

describe("Demo persona RBAC scoping, blinding, sign-out & sandbox reset", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    pinia = createPinia();
    setActivePinia(pinia);
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  describe("Non-demo OIDC immutability guardrails (ADR-2493)", () => {
    it("never mutates rawRoles or normalizedRoles on switchPersona() and emits zero X-Demo-* headers", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "false");
      const authStore = useAuthStore();
      authStore.setAuth(
        createFakeKeycloak(["Sponsor Admin", "Sponsor Designer", "CRC"])
      );

      const initialRaw = [...authStore.rawRoles];
      const initialNormalized = [...authStore.normalizedRoles];

      expect(authStore.switchPersona("site_crc")).toBe(true);
      expect(authStore.currentPersona).toBe("site_crc");
      expect(authStore.rawRoles).toEqual(initialRaw);
      expect(authStore.normalizedRoles).toEqual(initialNormalized);

      expect(getDemoIdentityHeaders()).toBeNull();

      let capturedHeaders: Headers | null = null;
      const fakeTarget = {
        fetch: vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
          capturedHeaders = new Headers(init?.headers);
          return new Response(JSON.stringify({ ok: true }), {
            status: 200,
            headers: { "Content-Type": "application/json" },
          });
        }),
      };
      installAuthorizedFetch(fakeTarget as never);
      await fakeTarget.fetch(`${window.location.origin}/api/v1/kernel/studies`);

      expect(capturedHeaders).not.toBeNull();
      expect(capturedHeaders!.get("Authorization")).toBe(
        "Bearer oidc-real-access-token"
      );
      expect(capturedHeaders!.get("X-Demo-Persona")).toBeNull();
      expect(capturedHeaders!.get("X-User-Roles")).toBeNull();
      expect(capturedHeaders!.get("X-User-Id")).toBeNull();
      expect(capturedHeaders!.get("X-Site-Id")).toBeNull();
    });
  });

  describe("Demo build role scoping & outbound identity headers", () => {
    it("retains all PERSONA_PRESETS in availablePersonas while scoping normalizedRoles, siteIds, and user metadata", () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      const authStore = useAuthStore();
      authStore.startDemoSession();

      expect(authStore.availablePersonas.map((p) => p.key)).toEqual(
        PERSONA_PRESETS.map((p) => p.key)
      );
      expect(authStore.availablePersonas.length).toBeGreaterThanOrEqual(6);
      expect(authStore.currentPersona).toBe("super_admin");
      expect(authStore.normalizedRoles).toContain("sponsor_admin");

      expect(authStore.switchPersona("site_crc")).toBe(true);
      expect(authStore.availablePersonas.map((p) => p.key)).toEqual(
        PERSONA_PRESETS.map((p) => p.key)
      );
      expect(authStore.normalizedRoles).toEqual(["site_investigator", "crc"]);
      expect(authStore.normalizedRoles).not.toContain("sponsor_admin");
      expect(authStore.assignedSiteIds).toEqual(["SITE-101"]);
      expect(authStore.hasAllStudiesScope).toBe(false);
      expect(authStore.user?.siteId).toBe("SITE-101");
      expect(canManageParticipants(authStore)).toBe(true);
      expect(resolveSiteReadinessPath(authStore.normalizedRoles)).toBe("/ssu");

      expect(authStore.switchPersona("cra_monitor")).toBe(true);
      expect(authStore.normalizedRoles).toEqual(["cra", "monitor"]);
      expect(canManageParticipants(authStore)).toBe(false);
      expect(resolveSiteReadinessPath(authStore.normalizedRoles)).toBe("/ctms");

      expect(authStore.switchPersona("data_manager")).toBe(true);
      expect(authStore.normalizedRoles).toEqual(["data_manager"]);
      expect(canManageParticipants(authStore)).toBe(false);

      expect(authStore.switchPersona("sponsor_designer")).toBe(true);
      expect(authStore.normalizedRoles).toEqual(["sponsor_designer"]);

      expect(authStore.switchPersona("auditor")).toBe(true);
      expect(authStore.normalizedRoles).toEqual(["auditor"]);
      expect(canAccessDataCollection(authStore.normalizedRoles)).toBe(false);
      expect(resolveSiteReadinessPath(authStore.normalizedRoles)).toBeNull();
    });

    it("attaches X-Demo-Persona, X-User-Roles, X-User-Id, and X-Site-Id across fetch, apiClient, and openapi-fetch only while signed in", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      const authStore = useAuthStore();
      authStore.startDemoSession("site_crc");

      const seen: Record<string, string | null>[] = [];
      const originalFetch = globalThis.fetch;
      const mockFetch = vi.fn(
        async (input: RequestInfo | URL, init?: RequestInit) => {
          const req =
            input instanceof Request ? input : new Request(input, init);
          const merged = new Headers(req.headers);
          if (init?.headers) {
            new Headers(init.headers).forEach((v, k) => merged.set(k, v));
          }
          seen.push({
            persona: merged.get("X-Demo-Persona"),
            roles: merged.get("X-User-Roles"),
            userId: merged.get("X-User-Id"),
            siteId: merged.get("X-Site-Id"),
          });
          return new Response(JSON.stringify({ items: [] }), {
            status: 200,
            headers: { "Content-Type": "application/json" },
          });
        }
      );
      globalThis.fetch = mockFetch as typeof fetch;
      installAuthorizedFetch(globalThis);

      try {
        await globalThis.fetch(
          `${window.location.origin}/api/v1/kernel/studies`
        );
        await apiClient.get("/api/v1/kernel/studies");
        const typedClient = createOpenApiClient(window.location.origin);
        await typedClient.GET("/api/v1/kernel/studies" as never);

        expect(seen).toHaveLength(3);
        for (const entry of seen) {
          expect(entry.persona).toBe("site_crc");
          expect(entry.roles).toContain("CRC");
          expect(entry.userId).toBeTruthy();
          expect(entry.siteId).toBe("SITE-101");
        }

        await authStore.logout();
        seen.length = 0;
        await globalThis.fetch(
          `${window.location.origin}/api/v1/kernel/studies`
        );
        expect(seen[0]).toEqual({
          persona: null,
          roles: null,
          userId: null,
          siteId: null,
        });
      } finally {
        globalThis.fetch = originalFetch;
      }
    });
  });

  describe("MSW RBAC policy evaluator & K-AUTH-4 / PX-20 masking", () => {
    let store: ReturnType<typeof createWebDemoStore>;
    let server: ReturnType<typeof setupServer>;
    const origin = window.location.origin;

    beforeEach(() => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      store = createWebDemoStore();
      server = setupServer(
        ...buildWebGatewayHandlers({ forceDemo: true, store })
      );
      server.listen({ onUnhandledFrame: "error" });
      installAuthorizedFetch(globalThis);
    });

    afterEach(() => {
      server.close();
    });

    it("returns HTTP 401 when signed out", async () => {
      const authStore = useAuthStore();
      authStore.clearAuth();

      const res = await fetch(`${origin}/api/v1/execution/subjects`);
      expect(res.status).toBe(401);
    });

    it("enforces Auditor read-only guard (403 on POST/PUT/PATCH/DELETE and signature-verification)", async () => {
      const authStore = useAuthStore();
      authStore.startDemoSession("auditor");

      const listRes = await fetch(`${origin}/api/v1/execution/subjects`);
      expect(listRes.status).toBe(200);
      const page = await listRes.json();
      const firstSubject = page.items[0];

      const sigRes = await fetch(
        `${origin}/api/v1/auth/signature-verification`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            username: "auditor.gxp",
            password: "demo-password", // pragma: allowlist secret
            meaning: "APPROVAL",
          }),
        }
      );
      expect(sigRes.status).toBe(403);

      const postRes = await fetch(`${origin}/api/v1/execution/subjects`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Change-Reason": "Attempted auditor enrollment",
        },
        body: JSON.stringify({
          study_id: "DEMO-01",
          site_id: "SITE-101",
          subject_id: "SUBJ-AUD-01",
          status: "SCREENED",
        }),
      });
      expect(postRes.status).toBe(403);

      const patchRes = await fetch(
        `${origin}/api/v1/execution/subjects/${firstSubject.id}/state`,
        {
          method: "PATCH",
          headers: {
            "Content-Type": "application/json",
            "X-Change-Reason": "Attempted auditor state change",
          },
          body: JSON.stringify({ status: "WITHDRAWN" }),
        }
      );
      expect(patchRes.status).toBe(403);

      const canIRes = await fetch(
        `${origin}/api/v1/kernel/authorization/can-i?action=collection.correct`
      );
      expect(canIRes.status).toBe(200);
      expect(await canIRes.json()).toMatchObject({ permitted: false });
    });

    it("blocks participant enrollment and form value edits for cra_monitor and data_manager while allowing SDV and queries", async () => {
      const authStore = useAuthStore();
      authStore.startDemoSession("site_crc");

      // Open a form first as site_crc so we have a valid form_id and observation_id
      const openFormRes = await fetch(`${origin}/api/v1/execution/forms`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Change-Reason": "Open baseline vitals",
        },
        body: JSON.stringify({
          enrollment_id: DEMO_PARTICIPANTS[0].enrollmentUuid,
          visit_code: "VISIT-BASELINE",
          form_code: "FORM-VITALS",
        }),
      });
      expect(openFormRes.status).toBe(201);
      const openedForm = await openFormRes.json();
      const formDetailRes = await fetch(
        `${origin}/api/v1/execution/forms/${openedForm.form_id}`
      );
      const formDetail = await formDetailRes.json();
      const recordedItems = formDetail.items.filter(
        (item: {
          item_id: string;
          observation_id?: string;
          revision?: number;
        }) => Boolean(item.observation_id)
      );
      expect(recordedItems.length).toBeGreaterThanOrEqual(2);
      const firstObs = recordedItems[0];

      // Site CRC can obtain a signature token and save/sign SITE-101 forms
      const crcSigTokenRes = await fetch(
        `${origin}/api/v1/auth/signature-verification`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            username: "crc.site101",
            password: "demo-password", // pragma: allowlist secret
            action: `/api/v1/execution/forms/${openedForm.form_id}/signatures`,
            semantic_action: "execution.form.sign",
          }),
        }
      );
      expect(crcSigTokenRes.status).toBe(200);

      // Site CRC cannot verify SDV or enroll into out-of-scope SITE-102
      const crcSdvRes = await fetch(
        `${origin}/api/v1/kernel/observations/observation:${firstObs.observation_id}@1/verifications`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Change-Reason": "CRC attempted SDV",
          },
          body: JSON.stringify({ source_reference: "Chart p.4" }),
        }
      );
      expect(crcSdvRes.status).toBe(403);

      const crcWrongSiteRes = await fetch(
        `${origin}/api/v1/execution/subjects`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Change-Reason": "Out-of-scope site enrollment",
          },
          body: JSON.stringify({
            study_id: "DEMO-01",
            site_id: "SITE-102",
            subject_id: "SUBJ-WRONG-SITE",
            status: "SCREENED",
          }),
        }
      );
      expect(crcWrongSiteRes.status).toBe(403);

      for (const [idx, reviewerPersona] of (
        ["cra_monitor", "data_manager"] as const
      ).entries()) {
        const targetObs = recordedItems[idx];
        authStore.switchPersona(reviewerPersona);

        const subjectWriteRes = await fetch(
          `${origin}/api/v1/execution/subjects`,
          {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              "X-Change-Reason": "Monitor attempted enrollment",
            },
            body: JSON.stringify({
              study_id: "DEMO-01",
              site_id: "SITE-101",
              subject_id: `SUBJ-${reviewerPersona}`,
              status: "SCREENED",
            }),
          }
        );
        expect(subjectWriteRes.status).toBe(403);

        const formPutRes = await fetch(
          `${origin}/api/v1/execution/forms/${openedForm.form_id}/values`,
          {
            method: "PUT",
            headers: {
              "Content-Type": "application/json",
              "Idempotency-Key": `idem-${reviewerPersona}`,
              "X-Change-Reason": "Monitor attempted value edit",
            },
            body: JSON.stringify({
              values: [
                {
                  item_id: targetObs.item_id,
                  expected_revision: targetObs.revision,
                  value: { type: "quantity", value: "118", unit: "mmHg" },
                },
              ],
            }),
          }
        );
        expect(formPutRes.status).toBe(403);

        const sdvRes = await fetch(
          `${origin}/api/v1/kernel/observations/observation:${targetObs.observation_id}@1/verifications`,
          {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              "X-Change-Reason": "Source verified against EHR",
            },
            body: JSON.stringify({ source_reference: "Bedside chart" }),
          }
        );
        expect(sdvRes.status).toBe(201);

        const queryRes = await fetch(
          `${origin}/api/v1/execution/forms/${openedForm.form_id}/queries`,
          {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              "X-Change-Reason": "Raise discrepancy query",
            },
            body: JSON.stringify({
              item_id: targetObs.item_id,
              text: `Verify measurement unit (${reviewerPersona})`,
              priority: "normal",
            }),
          }
        );
        expect(queryRes.status).toBe(201);
      }
    });

    it("applies K-AUTH-4 blinding and PX-20 site masking for site_crc while leaving super_admin unmasked", async () => {
      const authStore = useAuthStore();
      const site101Participant = DEMO_PARTICIPANTS[0];
      const site102Ref = DEMO_SITES.find(
        (s) => s.code === "SITE-102"
      )!.studySiteRef;

      authStore.startDemoSession("site_crc");

      const blindedSubjectRes = await fetch(
        `${origin}/api/v1/execution/subjects/${site101Participant.enrollmentUuid}`
      );
      expect(blindedSubjectRes.status).toBe(200);
      const blindedSubject = await blindedSubjectRes.json();
      expect(blindedSubject.treatment_group).toBe("BLINDED");

      const allocNodeRes = await fetch(
        `${origin}/api/v1/kernel/nodes/${encodeURIComponent(`allocation:${site101Participant.enrollmentUuid}`)}`
      );
      expect(allocNodeRes.status).toBe(200);
      const allocNode = await allocNodeRes.json();
      expect(allocNode.mask).toBe("Blinded");

      const otherSiteNodeRes = await fetch(
        `${origin}/api/v1/kernel/nodes/${encodeURIComponent(site102Ref)}`
      );
      expect(otherSiteNodeRes.status).toBe(200);
      const otherSiteNode = await otherSiteNodeRes.json();
      expect(otherSiteNode.mask).toBe("NotPermitted");

      authStore.switchPersona("super_admin");

      const unblindedSubjectRes = await fetch(
        `${origin}/api/v1/execution/subjects/${site101Participant.enrollmentUuid}`
      );
      const unblindedSubject = await unblindedSubjectRes.json();
      expect(unblindedSubject.treatment_group).toBe("ARM-A");

      const unmaskedAllocRes = await fetch(
        `${origin}/api/v1/kernel/nodes/${encodeURIComponent(`allocation:${site101Participant.enrollmentUuid}`)}`
      );
      expect((await unmaskedAllocRes.json()).mask).toBeNull();

      const unmaskedSiteRes = await fetch(
        `${origin}/api/v1/kernel/nodes/${encodeURIComponent(site102Ref)}`
      );
      expect((await unmaskedSiteRes.json()).mask).toBeNull();
    });
  });

  describe("Shell navigation, immediate route guards, sign-out & sandbox reset", () => {
    it("re-evaluates orientation tasks, redirects restricted routes to /forbidden on persona switch, and routes #btn-logout to /login", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      const authStore = useAuthStore();
      const onboardingStore = useOnboardingStore();
      authStore.startDemoSession("cra_monitor");
      onboardingStore.loadPersonaState("cra_monitor");

      const craTaskIds = onboardingStore.visibleTasks.map((t) => t.id);
      expect(
        isRouteAuthorizedForRoles(
          { path: "/ctms", meta: { requiresRole: ["cra", "monitor"] } },
          authStore.normalizedRoles
        )
      ).toBe(true);

      await router.push("/ctms");
      await router.isReady();

      const wrapper = mount(AppShell, {
        global: {
          plugins: [pinia, router],
          stubs: {
            OnboardingTour: true,
            OnboardingChecklistDrawer: true,
            CommandPaletteOverlay: true,
            ContextualHelpDrawer: true,
            TicketCreateModal: true,
          },
        },
      });
      await flushPromises();

      expect(wrapper.find("#nav-site-readiness").exists()).toBe(true);

      // Switch to auditor while standing on /ctms -> redirects to /forbidden and hides restricted nav
      await wrapper.find("#persona-select").setValue("auditor");
      await vi.waitFor(() => {
        expect(router.currentRoute.value.path).toBe("/forbidden");
      });

      expect(authStore.currentPersona).toBe("auditor");
      expect(wrapper.find("#nav-data-collection").exists()).toBe(false);
      expect(wrapper.find("#nav-site-readiness").exists()).toBe(false);
      expect(onboardingStore.visibleTasks.map((t) => t.id)).not.toEqual(
        craTaskIds
      );

      // Clicking #btn-logout clears session and navigates immediately to /login
      await wrapper.find("#btn-logout").trigger("click");
      await vi.waitFor(() => {
        expect(router.currentRoute.value.path).toBe("/login");
      });

      expect(authStore.isAuthenticated).toBe(false);
      wrapper.unmount();
    });

    it("renders #btn-reset-demo on AppShell and LoginView and restores participants, v1 records, notifications, eTMF files, and drafts", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      webDemoStore.reset();

      const authStore = useAuthStore();
      const notificationsStore = useNotificationsStore();
      const etmfStore = useEtmfStore();
      const clinicalStore = useClinicalStore();
      const canvasStore = useStudyCanvasStore();

      authStore.startDemoSession("super_admin");
      const baselineSubjectCount = webDemoStore.listSubjects({}).items.length;

      // Mutate MSW demoStore, notifications, eTMF, drafts, clinicalStore, and studyCanvasStore
      webDemoStore.createSubject({
        study_id: "DEMO-01",
        site_id: "SITE-101",
        subject_id: "SUBJ-MUTATED-99",
        status: "SCREENED",
      });
      const opened = webDemoStore.openForm({
        enrollment_id: DEMO_PARTICIPANTS[0].enrollmentUuid,
        visit_code: "VISIT-BASELINE",
        form_code: "FORM-VITALS",
      });
      webDemoStore.saveFormValues(
        opened.form_id,
        {
          values: [
            {
              item_id: "item-sysbp",
              expected_revision: 1,
              value: { type: "quantity", value: "165", unit: "mmHg" },
            },
          ],
        },
        "Mutated vitals"
      );
      window.localStorage.setItem(
        "demo_notifications_super_admin",
        JSON.stringify([])
      );
      notificationsStore.notifications = [];
      window.localStorage.setItem(
        "demo_documents",
        JSON.stringify([{ id: "mutated-doc" }])
      );
      const draftStorageKey = scopedKey(
        storageScopeOf(authStore),
        "ecrf_form_draft",
        "form-vitals-1"
      );
      expect(draftStorageKey).toBeTruthy();
      window.localStorage.setItem(
        draftStorageKey!,
        JSON.stringify({ dirty: true })
      );
      clinicalStore.activeStudyVersionId = "mutated-version";
      canvasStore.studyCode = "MUTATED-STUDY";
      canvasStore.paletteLoaded = true;
      canvasStore.record.subjectCode = "SUBJ-MUTATED-99";

      expect(webDemoStore.listSubjects({}).items.length).toBe(
        baselineSubjectCount + 1
      );
      expect(hasUserScopedDrafts()).toBe(true);

      await router.push("/login");
      await router.isReady();

      const loginWrapper = mount(LoginView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const resetBtn = loginWrapper.find("#btn-reset-demo");
      expect(resetBtn.exists()).toBe(true);
      await resetBtn.trigger("click");
      await flushPromises();

      expect(webDemoStore.listSubjects({}).items.length).toBe(
        baselineSubjectCount
      );
      const resetForm = webDemoStore.getForm(opened.form_id);
      const resetSbp = resetForm.items.find(
        (it: { item_id: string }) => it.item_id === "item-sysbp"
      );
      expect(resetSbp.revision).toBe(1);
      expect(resetSbp.value.value).toBe("128");
      expect(notificationsStore.notifications.length).toBeGreaterThan(0);
      expect(window.localStorage.getItem("demo_documents")).toBeNull();
      expect(etmfStore.documents.length).toBeGreaterThan(0);
      expect(hasUserScopedDrafts()).toBe(false);
      expect(clinicalStore.activeStudyVersionId).not.toBe("mutated-version");
      expect(canvasStore.studyCode).not.toBe("MUTATED-STUDY");
      expect(canvasStore.paletteLoaded).toBe(false);
      expect(canvasStore.record.subjectCode).toBe("");

      loginWrapper.unmount();
    });
  });
});
