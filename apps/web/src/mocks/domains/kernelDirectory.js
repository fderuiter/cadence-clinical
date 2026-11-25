import {
  DEMO_SITES,
  DEMO_SPONSOR,
  DEMO_STUDY,
  getDemoPersonaForWorkspace,
} from "demo-fixtures";
import { demoNowIso } from "../../utils/demoMode.js";
import {
  DEMO_ORGANIZATIONS,
  createInitialDemoStudyDirectoryEntry,
} from "../demoStudyImport.js";
import {
  DEFAULT_QUERY_UUID,
  OBS_1_UUID,
  SITE_101_UUID,
  SITE_102_UUID,
  STUDY_REF,
  VERSION_V2_REF,
  cloneJson,
  makeUuidFromIndex,
  resolveSiteUuid,
} from "./shared.js";

export function createKernelDirectoryDomain(ctx) {
  const { state, nextCounter } = ctx;

  state.studies = new Map();
  state.organizations = [];
  state.sites = [];
  state.studySites = new Map();
  state.idempotentStudyResponses = new Map();
  state.idempotentSiteResponses = new Map();
  state.idempotentStudySiteResponses = new Map();
  state.activityEntries = [];
  state.rules = [];
  state.validationFindings = [];
  state.workQueues = [];
  state.missingNodeRefs = new Set();

  function recordActivity(type, family, subjectKind, subjectRef, actorId) {
    const idx = nextCounter();
    const now = demoNowIso(idx);
    state.activityEntries.unshift({
      event_id: makeUuidFromIndex("00000900", idx),
      type,
      family,
      recorded: now,
      occurred_valid: now,
      actor_id:
        actorId ||
        getDemoPersonaForWorkspace("super_admin")?.username ||
        "designer.lead",
      subject_kind: subjectKind,
      subject: subjectRef,
      mask: null,
      correlation_id: makeUuidFromIndex("00000990", idx),
    });
  }

  function ensureStudyByCode(rawCode) {
    const code = (rawCode || "DEMO-01").trim() || "DEMO-01";
    for (const [existingCode, study] of state.studies.entries()) {
      if (existingCode.toUpperCase() === code.toUpperCase()) {
        return study;
      }
    }
    const idx = nextCounter();
    const created = {
      ref: `study:${makeUuidFromIndex("00000010", idx)}`,
      kind: "study",
      code,
      title: `Synthetic Clinical Study ${code}`,
      status: "ACTIVE",
      mask: null,
    };
    state.studies.set(code, created);
    return created;
  }

  function getStudyByRefOrCode(refOrCode) {
    const raw = String(refOrCode || "").trim();
    const unpinned = raw.split("@")[0];
    for (const study of state.studies.values()) {
      if (
        study.ref === raw ||
        study.ref.split("@")[0] === unpinned ||
        study.code.toUpperCase() === raw.toUpperCase()
      ) {
        return study;
      }
    }
    if (raw.startsWith("study:")) {
      return state.studies.get("DEMO-01");
    }
    return ensureStudyByCode(refOrCode);
  }

  const DEMO_NODE_REF_SYNTAX =
    /^[a-z][a-z0-9_]*:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:@[1-9][0-9]*)?$/i;
  const DEMO_STUDY_CODE_SYNTAX = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;

  function resolveExistingStudy(studyRefOrCode) {
    const clean = String(studyRefOrCode || "").trim();
    if (!clean) {
      return {
        error: {
          status: 400,
          problem: {
            title: "Invalid study reference",
            status: 400,
            code: "K.NodeRef.Invalid",
            detail: "A study reference or study code is required.",
          },
        },
      };
    }
    if (clean.includes(":")) {
      if (
        !DEMO_NODE_REF_SYNTAX.test(clean) ||
        !clean.toLowerCase().startsWith("study:")
      ) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Invalid study reference",
              status: 400,
              code: "K.NodeRef.Invalid",
              detail: `'${clean}' is not a valid study NodeRef.`,
            },
          },
        };
      }
    } else if (!DEMO_STUDY_CODE_SYNTAX.test(clean)) {
      return {
        error: {
          status: 400,
          problem: {
            title: "Invalid study code",
            status: 400,
            code: "K.Study.InvalidCode",
            detail: `'${clean}' is not a valid study code or NodeRef.`,
          },
        },
      };
    }

    const unpinned = clean.split("@")[0].toLowerCase();
    for (const candidate of state.studies.values()) {
      if (
        candidate.code.toUpperCase() === clean.toUpperCase() ||
        String(candidate.ref).split("@")[0].toLowerCase() === unpinned
      ) {
        return { study: candidate };
      }
    }
    if (clean.toUpperCase().startsWith("DEMO")) {
      return { study: ensureStudyByCode(clean.toUpperCase()) };
    }
    return {
      error: {
        status: 404,
        problem: {
          title: "Study not found",
          status: 404,
          code: "K.Study.NotFound",
          detail: `No visible study '${clean}' exists in this tenant.`,
        },
      },
    };
  }

  function validateScopeOrStudyParam(rawScope) {
    const clean = String(rawScope || "").trim();
    if (!clean) return { study: null };
    if (clean.includes(":")) {
      if (!DEMO_NODE_REF_SYNTAX.test(clean)) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Invalid scope reference",
              status: 400,
              code: "K.NodeRef.Invalid",
              detail: `'${clean}' is not a valid NodeRef.`,
            },
          },
        };
      }
      if (clean.toLowerCase().startsWith("study:")) {
        return resolveExistingStudy(clean);
      }
      return { scopeRef: clean };
    }
    return resolveExistingStudy(clean);
  }

  ctx.recordActivity = recordActivity;
  ctx.ensureStudyByCode = ensureStudyByCode;
  ctx.getStudyByRefOrCode = getStudyByRefOrCode;
  ctx.DEMO_NODE_REF_SYNTAX = DEMO_NODE_REF_SYNTAX;
  ctx.resolveExistingStudy = resolveExistingStudy;
  ctx.validateScopeOrStudyParam = validateScopeOrStudyParam;

  function seed() {
    const demoSiteInstitutions = DEMO_SITES.map((site) => ({
      ref: `organization:${site.institutionUuid}@1`,
      code: site.institutionCode,
      name: `${site.name} Institution`,
      kind: "SiteInstitution",
    }));
    state.organizations = [
      ...cloneJson(DEMO_ORGANIZATIONS),
      ...demoSiteInstitutions,
    ];
    if (!state.organizations.some((org) => org.kind === "SiteInstitution")) {
      state.organizations.push({
        ref: "organization:00000000-0000-4000-8000-000000000310@1", // deid-ignore
        code: "US-CLINICAL-INST",
        name: "US Academic Clinical Research Network",
        kind: "SiteInstitution",
      });
    }
    state.idempotentStudyResponses = new Map();
    state.idempotentSiteResponses = new Map();
    state.idempotentStudySiteResponses = new Map();
    state.validationFindings = [];
    state.workQueues = [];
    state.missingNodeRefs = new Set();

    const canonicalStudyCode = DEMO_STUDY.code || "CADENCE-101";
    const baseCadenceStudy = createInitialDemoStudyDirectoryEntry();
    const canonicalStudyEntry = {
      ...baseCadenceStudy,
      ref: DEMO_STUDY.ref || baseCadenceStudy.ref,
      code: canonicalStudyCode,
      title: DEMO_STUDY.title || baseCadenceStudy.title,
      phase: DEMO_STUDY.phase || baseCadenceStudy.phase,
      sponsor: DEMO_SPONSOR.code || baseCadenceStudy.sponsor_code,
      sponsor_code: DEMO_SPONSOR.code || baseCadenceStudy.sponsor_code,
      sponsor_name: DEMO_SPONSOR.name || baseCadenceStudy.sponsor_name,
      site_count: DEMO_SITES.length || baseCadenceStudy.site_count,
    };

    state.studies = new Map([
      [canonicalStudyCode, canonicalStudyEntry],
      [
        "DEMO-01",
        {
          ref: STUDY_REF,
          kind: "study",
          code: "DEMO-01",
          title: "Phase II Synthetic Cardiometabolic Demonstration Protocol",
          phase: "Phase 2",
          sponsor_ref: DEMO_ORGANIZATIONS[0].ref,
          sponsor_code: DEMO_ORGANIZATIONS[0].code,
          sponsor_name: DEMO_ORGANIZATIONS[0].name,
          site_count: 2,
          status: "ACTIVE",
          mask: null,
        },
      ],
      [
        "STUDY-USDM-001",
        {
          ref: "study:00000000-0000-0000-0000-000000000002", // deid-ignore
          kind: "study",
          code: "STUDY-USDM-001",
          title: "USDM v3.0 Synthetic Oncology Reference Trial",
          phase: "Phase 3",
          sponsor_ref: DEMO_ORGANIZATIONS[0].ref,
          sponsor_code: DEMO_ORGANIZATIONS[0].code,
          sponsor_name: DEMO_ORGANIZATIONS[0].name,
          site_count: 2,
          status: "ACTIVE",
          mask: null,
        },
      ],
    ]);

    const initialSiteInst =
      state.organizations.find((o) => o.kind === "SiteInstitution") ||
      state.organizations[0];
    state.sites =
      DEMO_SITES.length > 0
        ? DEMO_SITES.map((demoSite, idx) => ({
            ref: demoSite.siteUuid
              ? `site:${demoSite.siteUuid}@1`
              : `site:${makeUuidFromIndex("00000210", idx + 1)}@1`,
            code: demoSite.code,
            name: demoSite.name || demoSite.code,
            country: demoSite.country || "US",
            is_active: true,
            institution_ref: demoSite.institutionUuid
              ? `organization:${demoSite.institutionUuid}@1`
              : initialSiteInst.ref,
            institution_code: demoSite.institutionCode || initialSiteInst.code,
            institution_name: demoSite.name
              ? `${demoSite.name} Institution`
              : initialSiteInst.name,
            linked_study_count: state.studies.size,
          }))
        : [
            {
              ref: `site:${SITE_101_UUID}@1`,
              code: "SITE-101",
              name: "US Academic Clinical Research Center (SITE-101)",
              country: "US",
              is_active: true,
              institution_ref: initialSiteInst.ref,
              institution_code: initialSiteInst.code,
              institution_name: initialSiteInst.name,
              linked_study_count: state.studies.size,
            },
            {
              ref: `site:${SITE_102_UUID}@1`,
              code: "SITE-102",
              name: "US Regional Trial Unit 2",
              country: "US",
              is_active: true,
              institution_ref: initialSiteInst.ref,
              institution_code: initialSiteInst.code,
              institution_name: initialSiteInst.name,
              linked_study_count: state.studies.size,
            },
          ];

    const buildStudySitesForStudy = (
      studyRef,
      studyCode,
      { allEffective = false } = {}
    ) =>
      DEMO_SITES.length > 0
        ? DEMO_SITES.map((site, idx) => {
            const unversionedStudySiteRef =
              studyRef === canonicalStudyEntry.ref && site.studySiteRef
                ? String(site.studySiteRef).split("@")[0]
                : `study_site:${makeUuidFromIndex("00000400", idx + 1)}`;
            const isEffective =
              allEffective || site.complianceStage === "CONDUCT";
            return {
              ref: `${unversionedStudySiteRef}@1`,
              study_site_id: unversionedStudySiteRef.replace(
                /^study_site:/,
                ""
              ),
              study_site_ref: unversionedStudySiteRef,
              study_ref: String(studyRef).split("@")[0],
              study_code: studyCode,
              site_ref: `site:${site.siteUuid}@1`,
              site_code: site.code,
              site_name: site.name,
              country: site.country,
              institution_ref: `organization:${site.institutionUuid}@1`,
              institution_code: site.institutionCode,
              institution_name: `${site.name} Institution`,
              status: isEffective ? "Active" : "Selected",
              is_active: true,
              effective_versions: isEffective
                ? [
                    {
                      protocol_version_ref: `${VERSION_V2_REF}@1`,
                      number: 2,
                      effectivity_ref: `effectivity:${makeUuidFromIndex("00000450", idx + 1)}@1`,
                      level: "StudySite",
                    },
                  ]
                : [],
              mask: null,
            };
          })
        : state.sites.map((site, idx) => {
            const siteUuid = resolveSiteUuid(site.code);
            return {
              ref: `study_site:${siteUuid}@1`,
              study_site_id: siteUuid,
              study_site_ref: `study_site:${siteUuid}`,
              study_ref: String(studyRef).split("@")[0],
              study_code: studyCode,
              site_ref: site.ref,
              site_code: site.code,
              site_name: site.name,
              country: site.country,
              institution_ref: site.institution_ref,
              institution_code: site.institution_code,
              institution_name: site.institution_name,
              status: "Active",
              is_active: true,
              effective_versions: allEffective
                ? [
                    {
                      protocol_version_ref: `${VERSION_V2_REF}@1`,
                      number: 2,
                      effectivity_ref: `effectivity:${makeUuidFromIndex("00000450", idx + 1)}@1`,
                      level: "StudySite",
                    },
                  ]
                : [],
              mask: null,
            };
          });

    state.studySites = new Map([
      [
        canonicalStudyCode.toUpperCase(),
        buildStudySitesForStudy(canonicalStudyEntry.ref, canonicalStudyCode),
      ],
      [
        "DEMO-01",
        buildStudySitesForStudy(STUDY_REF, "DEMO-01", { allEffective: true }),
      ],
      [
        "STUDY-USDM-001",
        buildStudySitesForStudy(
          "study:00000000-0000-0000-0000-000000000002", // deid-ignore
          "STUDY-USDM-001",
          { allEffective: true }
        ),
      ],
    ]);

    state.activityEntries = [
      {
        event_id: "00000000-0000-4000-8000-000000000902", // deid-ignore
        type: "execution.query.raised",
        family: "execution",
        recorded: "2026-08-02T11:00:00Z",
        occurred_valid: "2026-08-02T11:00:00Z",
        actor_id: "usr-cra-01",
        subject_kind: "query",
        subject: `query:${DEFAULT_QUERY_UUID}`,
        mask: null,
        correlation_id: "00000000-0000-4000-8000-000000000992", // deid-ignore
      },
      {
        event_id: "00000000-0000-4000-8000-000000000901", // deid-ignore
        type: "observation.recorded",
        family: "collection",
        recorded: "2026-08-01T09:15:00Z",
        occurred_valid: "2026-08-01T09:15:00Z",
        actor_id: "usr-crc-01",
        subject_kind: "observation",
        subject: `observation:${OBS_1_UUID}`,
        mask: null,
        correlation_id: "00000000-0000-4000-8000-000000000991", // deid-ignore
      },
    ];

    state.rules = [
      {
        id: "rule-sysbp-range",
        study_id: "study_1",
        name: "Systolic Blood Pressure Physiological Range",
        target_field: "SYSBP",
        expression: "SYSBP >= 70 && SYSBP <= 220",
        severity: "WARNING",
        message: "Systolic blood pressure outside expected 70-220 mmHg window.",
        is_active: true,
      },
    ];
  }

  function linkSiteToStudy(
    studyRefOrCode = "",
    body = {},
    { reason = "", idempotencyKey = "" } = {}
  ) {
    if (!String(reason || "").trim()) {
      return {
        error: {
          status: 422,
          problem: {
            title: "Missing X-Change-Reason",
            status: 422,
            code: "K.Audit.ReasonRequired",
            detail:
              "Header 'X-Change-Reason' is required when linking a site to a study.",
          },
        },
      };
    }
    const trimmedKey = String(idempotencyKey || "").trim();
    if (trimmedKey && state.idempotentStudySiteResponses?.has(trimmedKey)) {
      return {
        data: cloneJson(state.idempotentStudySiteResponses.get(trimmedKey)),
        replayed: true,
      };
    }

    const clean = String(studyRefOrCode || "").trim();
    const unversioned = clean.split("@")[0];
    let matchedStudy = null;
    for (const candidate of state.studies.values()) {
      if (
        candidate.code.toUpperCase() === clean.toUpperCase() ||
        String(candidate.ref).split("@")[0] === unversioned
      ) {
        matchedStudy = candidate;
        break;
      }
    }
    if (!matchedStudy) {
      return {
        error: {
          status: 404,
          problem: {
            title: "Study not found",
            status: 404,
            code: "K.Study.NotFound",
            detail: `No visible study '${clean}' exists in this tenant.`,
          },
        },
      };
    }

    const reqRef = String(body.site_ref || "")
      .trim()
      .split("@")[0];
    const reqCode = String(body.site_code || "").trim();
    let site = state.sites.find(
      (candidate) =>
        (reqRef && candidate.ref.split("@")[0] === reqRef) ||
        (reqCode && candidate.code.toUpperCase() === reqCode.toUpperCase())
    );
    if (
      !site &&
      !body.site_ref &&
      body.site_code &&
      (body.site_name || body.country || body.institution_ref)
    ) {
      const regIdx = nextCounter();
      const code = String(body.site_code).trim();
      site = {
        ref: `site:${makeUuidFromIndex("00000310", regIdx)}@1`,
        code,
        name: String(body.site_name || code).trim(),
        country: String(body.country || "US")
          .trim()
          .toUpperCase(),
        is_active: true,
        institution_ref:
          body.institution_ref ||
          matchedStudy.sponsor_ref ||
          DEMO_ORGANIZATIONS[0].ref,
        institution_code:
          matchedStudy.sponsor_code || DEMO_ORGANIZATIONS[0].code,
        institution_name:
          matchedStudy.sponsor_name || DEMO_ORGANIZATIONS[0].name,
        linked_study_count: 1,
      };
      state.sites.push(site);
    }
    if (!site) {
      return {
        error: {
          status: 404,
          problem: {
            title: "Site not found",
            status: 404,
            code: "K.Study.SiteUnknown",
            detail: "The specified clinical site was not found in this tenant.",
          },
        },
      };
    }

    const key = matchedStudy.code.toUpperCase();
    const existingList =
      state.studySites.get(key) ||
      state.studySites.get(matchedStudy.code) ||
      [];
    if (
      existingList.some(
        (entry) => entry.site_code?.toUpperCase() === site.code.toUpperCase()
      )
    ) {
      return {
        error: {
          status: 409,
          problem: {
            title: "Site already linked",
            status: 409,
            code: "K.Study.AlreadyLinked",
            detail: `Site '${site.code}' is already linked to '${matchedStudy.code}'.`,
          },
        },
      };
    }

    const idx = nextCounter();
    const studySiteUuid = makeUuidFromIndex("00000410", idx);
    const studySite = {
      ref: `study_site:${studySiteUuid}@1`,
      study_site_id: studySiteUuid,
      study_site_ref: `study_site:${studySiteUuid}`,
      study_ref: String(matchedStudy.ref).split("@")[0],
      study_code: matchedStudy.code,
      site_ref: site.ref,
      site_code: site.code,
      site_name: site.name,
      country: site.country,
      institution_ref: site.institution_ref,
      institution_code: site.institution_code,
      institution_name: site.institution_name,
      status: "Selected",
      is_active: true,
      effective_versions: [],
      mask: null,
    };
    const nextLinks = [...existingList, studySite].sort((a, b) =>
      String(a.site_code || "").localeCompare(String(b.site_code || ""))
    );
    state.studySites.set(key, nextLinks);
    matchedStudy.site_count = nextLinks.length;
    if (trimmedKey && state.idempotentStudySiteResponses) {
      state.idempotentStudySiteResponses.set(trimmedKey, cloneJson(studySite));
    }
    return { data: cloneJson(studySite) };
  }

  const methods = {
    listStudies() {
      return [...state.studies.values()].map((study) => ({
        ref: study.ref,
        code: study.code,
        title: study.title,
        phase: study.phase || "Phase 2",
        sponsor_ref: study.sponsor_ref || DEMO_ORGANIZATIONS[0].ref,
        sponsor_code: study.sponsor_code || DEMO_ORGANIZATIONS[0].code,
        sponsor_name: study.sponsor_name || DEMO_ORGANIZATIONS[0].name,
        site_count: study.site_count ?? 0,
      }));
    },

    listOrganizations(kind = "") {
      const filterKind = String(kind || "").trim();
      const orgs = filterKind
        ? state.organizations.filter(
            (org) =>
              String(org.kind || "").toLowerCase() === filterKind.toLowerCase()
          )
        : state.organizations;
      return cloneJson(orgs);
    },

    createOrganization(body = {}, { reason = "" } = {}) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 422,
            problem: {
              title: "Missing X-Change-Reason",
              status: 422,
              code: "K.Audit.ReasonRequired",
              detail:
                "Header 'X-Change-Reason' is required when registering an organization.",
            },
          },
        };
      }
      const name = String(body.name || "").trim();
      if (!name) {
        return {
          error: {
            status: 422,
            problem: {
              title: "Invalid organization request",
              status: 422,
              code: "K.Identity.NameRequired",
              detail: "Organization name is required.",
            },
          },
        };
      }
      const kind =
        String(body.kind || "SiteInstitution").trim() || "SiteInstitution";
      const derivedCode =
        String(body.code || "")
          .trim()
          .toUpperCase() ||
        `INST-${name
          .toUpperCase()
          .replace(/[^A-Z0-9]+/g, "-")
          .replace(/^-+|-+$/g, "")
          .slice(0, 16)}`;
      if (
        state.organizations.some(
          (o) => o.code.toUpperCase() === derivedCode.toUpperCase()
        )
      ) {
        return {
          error: {
            status: 409,
            problem: {
              title: "Duplicate organization code",
              status: 409,
              code: "K.Node.AlreadyExists",
              detail: `Organization '${derivedCode}' already exists.`,
            },
          },
        };
      }
      const idx = nextCounter();
      const created = {
        ref: `organization:${makeUuidFromIndex("00000310", idx)}@1`,
        code: derivedCode,
        name,
        kind,
      };
      state.organizations.push(created);
      return { data: cloneJson(created) };
    },

    listSites() {
      return cloneJson(
        [...state.sites].sort((a, b) => a.code.localeCompare(b.code))
      );
    },

    createSite(body = {}, { reason = "", idempotencyKey = "" } = {}) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 422,
            problem: {
              title: "Missing X-Change-Reason",
              status: 422,
              code: "K.Audit.ReasonRequired",
              detail:
                "Header 'X-Change-Reason' is required when registering a site.",
            },
          },
        };
      }
      const trimmedKey = String(idempotencyKey || "").trim();
      if (trimmedKey && state.idempotentSiteResponses?.has(trimmedKey)) {
        return {
          data: cloneJson(state.idempotentSiteResponses.get(trimmedKey)),
          replayed: true,
        };
      }

      const siteCode = String(body.site_code || "")
        .trim()
        .toUpperCase();
      const name = String(body.name || "").trim();
      const country = String(body.country || "")
        .trim()
        .toUpperCase();
      if (!siteCode || !name || !/^[A-Z]{2}$/.test(country)) {
        return {
          error: {
            status: 422,
            problem: {
              title: "Invalid site registration request",
              status: 422,
              code: "K.Study.CountryInvalid",
              detail:
                "Site code, name, and a two-letter ISO country code are required.",
            },
          },
        };
      }

      if (
        state.sites.some((s) => s.code.toUpperCase() === siteCode.toUpperCase())
      ) {
        return {
          error: {
            status: 409,
            problem: {
              title: "Duplicate site code",
              status: 409,
              code: "K.Study.SiteCodeTaken",
              detail: `A site with code '${siteCode}' already exists in this tenant.`,
            },
          },
        };
      }

      let org = state.organizations.find(
        (o) =>
          (body.institution_ref &&
            o.ref.split("@")[0] ===
              String(body.institution_ref).split("@")[0]) ||
          (body.institution_code &&
            o.code.toUpperCase() ===
              String(body.institution_code).trim().toUpperCase())
      );
      if (org && org.kind !== "SiteInstitution") {
        if (body.institution_ref || body.institution_code) {
          return {
            error: {
              status: 422,
              problem: {
                title: "Invalid site institution kind",
                status: 422,
                code: "K.Study.OrganizationKindMismatch",
                detail: `Organization '${org.code}' has kind '${org.kind}', expected 'SiteInstitution'.`,
              },
            },
          };
        }
        org = null;
      }

      if (!org) {
        const instName = String(body.institution_name || "").trim();
        if (!instName && !String(body.institution_code || "").trim()) {
          return {
            error: {
              status: 422,
              problem: {
                title: "Missing site institution",
                status: 422,
                code: "K.Study.MissingInstitution",
                detail:
                  "Select an existing SiteInstitution or provide institution_name.",
              },
            },
          };
        }
        const derivedBase = (
          String(body.institution_code || "").trim() ||
          instName.replace(/[^A-Za-z0-9]+/g, "-").replace(/^-+|-+$/g, "")
        ).toUpperCase();
        const codeTaken = state.organizations.some(
          (o) => o.code.toUpperCase() === derivedBase
        );
        const finalOrgCode = codeTaken ? `${derivedBase}-SITE` : derivedBase;
        const orgIdx = nextCounter();
        org = {
          ref: `organization:${makeUuidFromIndex("00000310", orgIdx)}@1`,
          code: finalOrgCode,
          name: instName || finalOrgCode,
          kind: "SiteInstitution",
        };
        state.organizations.push(org);
      }

      const idx = nextCounter();
      const created = {
        ref: `site:${makeUuidFromIndex("00000220", idx)}@1`,
        code: siteCode,
        name,
        country,
        is_active: true,
        institution_ref: org.ref,
        institution_code: org.code,
        institution_name: org.name,
        linked_study_count: 0,
      };
      state.sites.push(created);
      if (trimmedKey && state.idempotentSiteResponses) {
        state.idempotentSiteResponses.set(trimmedKey, cloneJson(created));
      }
      return { data: cloneJson(created) };
    },

    listStudySites(studyRefOrCode = "") {
      const clean = String(studyRefOrCode || "").trim();
      const unversioned = clean.split("@")[0];
      let matchedStudy = null;
      for (const candidate of state.studies.values()) {
        if (
          candidate.code.toUpperCase() === clean.toUpperCase() ||
          String(candidate.ref).split("@")[0] === unversioned
        ) {
          matchedStudy = candidate;
          break;
        }
      }
      if (!matchedStudy) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Study not found",
              status: 404,
              code: "K.Study.NotFound",
              detail: `No visible study '${clean}' exists in this tenant.`,
            },
          },
        };
      }
      const list =
        state.studySites.get(matchedStudy.code.toUpperCase()) ||
        state.studySites.get(matchedStudy.code) ||
        [];
      return {
        data: cloneJson(
          [...list].sort((a, b) =>
            String(a.site_code || "").localeCompare(String(b.site_code || ""))
          )
        ),
      };
    },

    linkSiteToStudy,

    createStudy(body = {}, { reason = "", idempotencyKey = "" } = {}) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Missing X-Change-Reason",
              status: 400,
              code: "Kernel.Audit.MissingReasonForChange",
              detail:
                "Header 'X-Change-Reason' is required when creating a study.",
            },
          },
        };
      }
      if (
        idempotencyKey &&
        state.idempotentStudyResponses.has(idempotencyKey)
      ) {
        return {
          data: cloneJson(state.idempotentStudyResponses.get(idempotencyKey)),
          replayed: true,
        };
      }

      const code = String(body.study_code || "").trim();
      const title = String(body.title || "").trim();
      const phase = String(body.phase || "").trim();
      if (!code || !title || !phase) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Invalid study creation request",
              status: 400,
              code: "Kernel.Study.InvalidRequest",
              detail: "Study code, title, and clinical phase are required.",
            },
          },
        };
      }

      for (const existing of state.studies.values()) {
        if (existing.code.toUpperCase() === code.toUpperCase()) {
          return {
            error: {
              status: 409,
              problem: {
                title: "Duplicate study code",
                status: 409,
                code: "Kernel.Study.DuplicateCode",
                detail: `A study with code '${code}' already exists in this tenant.`,
              },
            },
          };
        }
      }

      let org = state.organizations.find(
        (o) =>
          (body.sponsor_ref && o.ref === body.sponsor_ref) ||
          (body.sponsor_code &&
            o.code.toUpperCase() === String(body.sponsor_code).toUpperCase())
      );
      if (!org) {
        if (!String(body.sponsor_code || "").trim()) {
          return {
            error: {
              status: 400,
              problem: {
                title: "Missing sponsor",
                status: 400,
                code: "Kernel.Study.MissingSponsor",
                detail: "Select an existing sponsor or provide a sponsor code.",
              },
            },
          };
        }
        const orgIdx = nextCounter();
        org = {
          ref: `organization:${makeUuidFromIndex("00000300", orgIdx)}@1`,
          code: String(body.sponsor_code).trim().toUpperCase(),
          name:
            String(body.sponsor_name || "").trim() ||
            String(body.sponsor_code).trim(),
          kind: "Sponsor",
        };
        state.organizations.push(org);
      }

      const idx = nextCounter();
      const created = {
        ref: `study:${makeUuidFromIndex("00000020", idx)}@1`,
        kind: "study",
        code,
        title,
        phase,
        sponsor_ref: org.ref,
        sponsor_code: org.code,
        sponsor_name: org.name,
        site_count: 0,
        status: "ACTIVE",
        mask: null,
        isCustomCreated: true,
      };
      state.studies.set(code, created);
      state.studySites.set(code.toUpperCase(), []);
      let linkedSite = null;
      if (
        body.link_sandbox_site ||
        body.initial_site_ref ||
        body.initial_site_code
      ) {
        const candidateSite =
          state.sites.find(
            (s) =>
              (body.initial_site_ref &&
                s.ref.split("@")[0] ===
                  String(body.initial_site_ref).split("@")[0]) ||
              (body.initial_site_code &&
                s.code.toUpperCase() ===
                  String(body.initial_site_code).trim().toUpperCase())
          ) || state.sites[0];
        if (candidateSite) {
          const linkRes = linkSiteToStudy(
            code,
            { site_ref: candidateSite.ref },
            { reason }
          );
          if (linkRes.data) {
            linkedSite = linkRes.data;
          }
        }
      }
      recordActivity(
        "kernel.study.create",
        "protocol",
        "study",
        created.ref,
        "usr-demo-operator"
      );

      const response = {
        ref: created.ref,
        code: created.code,
        title: created.title,
        phase: created.phase,
        sponsor_ref: created.sponsor_ref,
        sponsor_code: created.sponsor_code,
        sponsor_name: created.sponsor_name,
        site_count: created.site_count,
        linked_site: linkedSite,
      };
      if (idempotencyKey) {
        state.idempotentStudyResponses.set(idempotencyKey, cloneJson(response));
      }
      return { data: response };
    },

    resolveStudy(studyRefOrCode = "") {
      return resolveExistingStudy(studyRefOrCode);
    },

    getActivityPage({
      scope,
      families = [],
      limit = 25,
      includeNodes = false,
      caller = null,
    } = {}) {
      if (scope) {
        const check = validateScopeOrStudyParam(scope);
        if (check.error) return check;
      }
      const normalizedFamilies = (
        Array.isArray(families) ? families : [families]
      )
        .filter(Boolean)
        .map((f) => String(f).toLowerCase());
      let filtered = state.activityEntries;
      if (normalizedFamilies.length > 0) {
        filtered = filtered.filter((entry) =>
          normalizedFamilies.includes(String(entry.family || "").toLowerCase())
        );
      }
      const entries = cloneJson(filtered.slice(0, Number(limit) || 25));
      const page = {
        entries,
        next: null,
      };
      if (includeNodes) {
        const subjectRefs = [
          ...new Set(
            entries
              .filter((e) => !e.mask && e.subject)
              .map((e) => String(e.subject))
          ),
        ];
        const batch = ctx.getNodes(subjectRefs, caller);
        page.nodes = batch.data?.nodes || {};
      }
      return scope ? { data: page } : page;
    },
  };

  return { seed, methods };
}
