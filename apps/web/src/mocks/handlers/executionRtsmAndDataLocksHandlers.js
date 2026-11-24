import { http, HttpResponse } from "msw";
import { demoNowIso, demoNowMs } from "../../utils/demoMode.js";
import { readJsonBody } from "./shared.js";

function buildRandomizationScheme({
  id,
  studyId = "DEMO-01",
  version = 1,
  method = "permuted_block",
  status = "APPROVED",
  arms = { PLACEBO: 1, ACTIVE: 1 },
  stratificationFactors = {},
  blockSizes = [4],
  slotsPerStratum = 8,
  preferredArmProbability = 0,
  requiresKit = true,
  listSize = 8,
  configurationHash = "demo-hash",
  approvedBy = "stat.approver",
}) {
  const nowIso = demoNowIso();
  return {
    id,
    study_id: studyId,
    version,
    method,
    status,
    arms,
    stratification_factors: stratificationFactors,
    block_sizes: blockSizes,
    slots_per_stratum: slotsPerStratum,
    preferred_arm_probability: preferredArmProbability,
    requires_kit: requiresKit,
    list_size: listSize,
    configuration_hash: configurationHash,
    drafted_by: "stat.author",
    drafted_at: nowIso,
    approved_by: approvedBy,
    approved_at: approvedBy ? nowIso : null,
    retired_at: null,
  };
}

export function createExecutionRtsmAndDataLocksHandlers(store) {
  return [
    http.get("*/api/v1/execution/rtsm/sites/:siteId/inventory", ({ params }) =>
      HttpResponse.json(
        store.state.ipKits.filter(
          (k) => k.site_id === params.siteId || params.siteId === "SITE-101"
        )
      )
    ),
    http.get(
      "*/api/v1/execution/rtsm/kits/inventory/:studyId/:siteId",
      ({ params }) =>
        HttpResponse.json(
          store.state.ipKits.filter(
            (k) => k.site_id === params.siteId || params.siteId === "SITE-101"
          )
        )
    ),
    http.post(
      "*/api/v1/execution/rtsm/kits/seed-inventory",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const items = Array.isArray(body?.kits) ? body.kits : [];
        for (const [idx, kit] of items.entries()) {
          store.state.ipKits.push({
            id: `kit-${demoNowMs(idx)}`,
            study_id: body?.study_id || "DEMO-01",
            site_id: body?.site_id || "SITE-101",
            kit_number: kit.kit_number || "KIT-DEMO-99",
            lot_number: kit.lot_number || "LOT-DEMO",
            status:
              kit.status === "AVAILABLE" || !kit.status
                ? "ACTIVE_AT_SITE"
                : kit.status,
            treatment_arm: kit.treatment_arm || null,
            expiration_date: kit.expiration_date || null,
          });
        }
        return HttpResponse.json({ seeded: items.length }, { status: 201 });
      }
    ),
    http.post(
      "*/api/v1/execution/rtsm/kits/validate-scan",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const kit = store.state.ipKits.find(
          (k) => k.kit_number === body?.kit_number
        );
        const scannedLot = body?.scanned_lot_number || null;
        const mismatch = Boolean(
          kit && scannedLot && kit.lot_number !== scannedLot
        );
        return HttpResponse.json({
          kit_number: body?.kit_number || "",
          study_id: body?.study_id || "DEMO-01",
          site_id: body?.site_id || "SITE-101",
          found_in_inventory: Boolean(kit),
          expected_lot_number: kit?.lot_number || null,
          scanned_lot_number: scannedLot,
          lot_mismatch_detected: mismatch,
          current_status: kit?.status || "UNKNOWN",
          recommended_disposition: mismatch
            ? "QUARANTINED"
            : "RETURNED_TO_DEPOT",
          warning: mismatch ? "Lot mismatch detected during scan." : null,
        });
      }
    ),
    http.post(
      "*/api/v1/execution/rtsm/kits/reconcile-batch",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const items = Array.isArray(body?.items) ? body.items : [];
        return HttpResponse.json({
          certificate: {
            certificate_id: `cert-${demoNowMs()}`,
            study_id: body?.study_id || "DEMO-01",
            site_id: body?.site_id || "SITE-101",
            processed_by: body?.processed_by || "usr-pharm-01",
            processed_at: demoNowIso(),
            total_kits_reconciled: items.length,
            items,
          },
        });
      }
    ),
    http.get(
      "*/api/v1/execution/rtsm/kits/certificates/:certificateId",
      ({ params }) =>
        HttpResponse.json({
          certificate_id: String(params.certificateId),
          study_id: "DEMO-01",
          site_id: "SITE-101",
          total_kits_reconciled: 1,
        })
    ),
    http.get("*/api/v1/execution/randomization/schemes", ({ request }) => {
      const url = new URL(request.url);
      const studyId = url.searchParams.get("study_id") || "DEMO-01";
      return HttpResponse.json([
        buildRandomizationScheme({
          id: "a1b2c3d4-e5f6-4a7b-8c9d-e0f1a2b3c4d5",
          studyId,
        }),
      ]);
    }),
    http.post(
      "*/api/v1/execution/randomization/schemes",
      async ({ request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json(
          buildRandomizationScheme({
            id: "b2c3d4e5-f6a7-4b8c-9d0e-f1a2b3c4d5e6",
            studyId: body?.study_id || "DEMO-01",
            version: 2,
            method: body?.method || "permuted_block",
            status: "DRAFT",
            arms: body?.arms || { PLACEBO: 1, ACTIVE: 1 },
            stratificationFactors: body?.stratification_factors || {},
            blockSizes: body?.block_sizes || [4],
            slotsPerStratum: body?.slots_per_stratum || 8,
            preferredArmProbability: body?.preferred_arm_probability || 0,
            requiresKit: Boolean(body?.requires_kit ?? true),
            listSize: 0,
            configurationHash: "demo-draft-hash",
            approvedBy: null,
          }),
          { status: 201 }
        );
      }
    ),
    http.post(
      "*/api/v1/execution/randomization/schemes/:schemeId/approve",
      ({ params }) =>
        HttpResponse.json(
          buildRandomizationScheme({
            id: String(params.schemeId),
            version: 2,
            configurationHash: "demo-approved-hash",
          })
        )
    ),
    http.post(
      "*/api/v1/execution/subjects/:subjectId/consent",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json({
          id: `consent-${demoNowMs()}`,
          subject_id: String(params.subjectId),
          study_id: body?.protocol_version?.study_id || "DEMO-01",
          version_tag: body?.protocol_version?.version_tag || "1.0",
          version_index: body?.protocol_version?.version_index || 1,
          icf_signed: Boolean(body?.icf_signed ?? true),
          icf_signed_date: body?.icf_signed_date || demoNowIso(),
          requires_reconsent: Boolean(body?.requires_reconsent ?? false),
          is_paper_override: Boolean(body?.is_paper_override ?? false),
          version: 1,
        });
      }
    ),
    http.post(
      "*/api/v1/execution/subjects/:subjectId/randomize",
      ({ params }) => {
        const subjectKey = decodeURIComponent(String(params.subjectId));
        const updated = store.updateSubjectState(subjectKey, {
          status: "RANDOMIZED",
        });
        const found = store.state.subjects.find(
          (s) =>
            s.id === updated.id ||
            s.subject_id.toUpperCase() === subjectKey.toUpperCase()
        );
        if (found) {
          found.treatment_group = null;
          found.unblinded_treatment_group = "ACTIVE";
          found.investigational_product_id = null;
        }
        return HttpResponse.json({
          randomization_id: `rand-${demoNowMs()}`,
          subject_id: updated?.subject_id || subjectKey,
          study_id: updated?.study_id || "DEMO-01",
          scheme_version: 1,
          stratum_key: "UNSTRATIFIED",
          kit_number: "KIT-1001",
          status: "RANDOMIZED",
          randomized_at: demoNowIso(),
        });
      }
    ),
    http.get(
      "*/api/v1/execution/rtsm/cgt/chain-of-custody/:studyId/:subjectId",
      ({ params }) =>
        HttpResponse.json({
          study_id: String(params.studyId),
          subject_id: String(params.subjectId),
          din: "DIN-DEMO-ALPHA",
          current_stage: "MANUFACTURING_RELEASED",
          status: "IN_CUSTODY",
          milestones: [
            {
              id: "cgt-ms-01",
              stage: "APHERESIS",
              milestone_type: "COLLECTION_VERIFIED",
              location_name: "Clinical Site Apheresis Unit",
            },
          ],
        })
    ),
    http.post("*/api/v1/execution/rtsm/cgt/apheresis", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(
        {
          id: `cgt-${demoNowMs()}`,
          study_id: body?.study_id || "DEMO-01",
          subject_id: body?.subject_id || "SUBJ-1001",
          din: body?.din || "DIN-DEMO-BETA",
          current_stage: "APHERESIS",
        },
        { status: 201 }
      );
    }),
    http.post("*/api/v1/execution/rtsm/cgt/milestone", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(
        { status: "RECORDED", ...body },
        { status: 201 }
      );
    }),
    http.get("*/api/v1/execution/rtsm/cgt/certificates/:studyId", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/execution/rtsm/cgt/reconciliations/:studyId", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/execution/locks", () =>
      HttpResponse.json(store.state.locks)
    ),
    http.post(
      "*/api/v1/execution/locks/:action",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const nextStatus = params.action === "unlock" ? "UNLOCKED" : "LOCKED";
        if (store.state.locks[0]) {
          store.state.locks[0].status = nextStatus;
          store.state.locks[0].updated_at = new Date().toISOString();
        }
        return HttpResponse.json({
          status: nextStatus,
          study_id: body?.study_id || "DEMO-01",
        });
      }
    ),
  ];
}
