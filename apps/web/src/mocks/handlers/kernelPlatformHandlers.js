import { http, HttpResponse } from "msw";
import {
  evaluateCapabilityDecision,
  evaluateNodeCommands,
  resolveDemoCaller,
} from "../demoPolicy.js";
import { readJsonBody, requireKernelMutationHeaders } from "./shared.js";

export function createKernelPlatformHandlers(store) {
  const handleCanIGet = ({ request }) => {
    const url = new URL(request.url);
    const capability =
      url.searchParams.get("capability") ||
      url.searchParams.get("action") ||
      "kernel.read";
    const resource =
      url.searchParams.get("resource") ||
      url.searchParams.get("scope") ||
      "study:demo";
    return HttpResponse.json(
      evaluateCapabilityDecision(
        resolveDemoCaller(request),
        capability,
        resource,
        store
      )
    );
  };

  const handleCanIPost = async ({ request }) => {
    const body = await readJsonBody(request);
    const capability = body?.capability || body?.action || "kernel.read";
    const resource = body?.resource || body?.scope || "study:demo";
    return HttpResponse.json(
      evaluateCapabilityDecision(
        resolveDemoCaller(request),
        capability,
        resource,
        store
      )
    );
  };

  return [
    http.get("*/api/v1/kernel/entitlements", () =>
      HttpResponse.json(store.getEntitlements())
    ),
    http.post("*/api/v1/kernel/entitlements/tier", async ({ request }) => {
      const headerErr = requireKernelMutationHeaders(request);
      if (headerErr) return headerErr;
      const body = await readJsonBody(request);
      const outcome = store.upgradeSubscriptionTier(body?.tier);
      if (!outcome || outcome.error === "invalid_tier") {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Commercial.TierInvalid",
            title: "Invalid commercial tier",
            status: 400,
            code: "K.Commercial.TierInvalid",
          },
          { status: 400 }
        );
      }
      if (outcome.error === "expired") {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Subscription.Expired",
            title: "Trial subscription expired",
            status: 403,
            code: "K.Subscription.Expired",
            detail:
              "This tenant's trial subscription has expired. Read and export access remains active; activate a subscription to resume workspace writes.",
          },
          { status: 403 }
        );
      }
      return HttpResponse.json(outcome.data);
    }),
    http.post("*/api/v1/kernel/entitlements/activate", async ({ request }) => {
      const headerErr = requireKernelMutationHeaders(request);
      if (headerErr) return headerErr;
      const body = await readJsonBody(request);
      const outcome = store.activateSubscription(body);
      if (outcome.error === "invalid_tier") {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Commercial.TierInvalid",
            title: "Invalid commercial tier",
            status: 400,
            code: "K.Commercial.TierInvalid",
          },
          { status: 400 }
        );
      }
      if (outcome.error === "scoping_invalid") {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Commercial.ScopingInvalid",
            title: "Study scoping details required for subscription activation",
            status: 422,
            code: "K.Commercial.ScopingInvalid",
            detail:
              "Activating a commercial subscription requires valid study phase, duration, participating countries, site count, participant count, active study count, and a signed commercial quote token.",
            errors: outcome.errors,
          },
          { status: 422 }
        );
      }
      if (outcome.error === "quote_invalid") {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Commercial.QuoteInvalid",
            title: "Commercial quote token could not be verified",
            status: 403,
            code: "K.Commercial.QuoteInvalid",
            detail:
              "Subscription activation requires a verified signed commercial quote token (qtk1.<payload>.<signature>) bound to this tenant, tier, and study scoping.",
          },
          { status: 403 }
        );
      }
      return HttpResponse.json(outcome.data);
    }),
    http.post("*/api/v1/kernel/onboarding/trial", async ({ request }) => {
      const headerErr = requireKernelMutationHeaders(request);
      if (headerErr) return headerErr;
      const body = await readJsonBody(request);
      return HttpResponse.json(store.bootstrapTrial(body));
    }),
    http.get("*/api/v1/kernel/search", ({ request }) => {
      const url = new URL(request.url);
      const q = url.searchParams.get("q") || "";
      const kinds = url.searchParams.getAll("kind");
      const limit = url.searchParams.get("limit") || "25";
      return HttpResponse.json(store.searchKernel({ q, kinds, limit }));
    }),
    http.get("*/api/v1/kernel/enrollments/lookup", ({ request }) => {
      const url = new URL(request.url);
      const result = store.lookupEnrollment({
        studyCode: url.searchParams.get("study_code") || "",
        subjectCode: url.searchParams.get("subject_code") || "",
        siteCode: url.searchParams.get("site_code"),
      });
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
          headers: { "Content-Type": "application/problem+json" },
        });
      }
      return HttpResponse.json(result.data);
    }),
    http.get("*/api/v1/kernel/authorization/can-i", handleCanIGet),
    http.get("*/api/v1/kernel/capabilities/can-i", handleCanIGet),
    http.post("*/api/v1/kernel/authorization/can-i", handleCanIPost),
    http.post("*/api/v1/kernel/capabilities/can-i", handleCanIPost),
    http.get("*/api/v1/kernel/nodes/:nodeRef/path", ({ params, request }) =>
      HttpResponse.json(
        store.getZoomPath(
          decodeURIComponent(String(params.nodeRef)),
          resolveDemoCaller(request)
        )
      )
    ),
    http.get("*/api/v1/kernel/nodes/:nodeRef/children", ({ params, request }) =>
      HttpResponse.json(
        store.getChildren(
          decodeURIComponent(String(params.nodeRef)),
          resolveDemoCaller(request)
        )
      )
    ),
    http.get("*/api/v1/kernel/nodes/:nodeRef/commands", ({ params, request }) =>
      HttpResponse.json(
        evaluateNodeCommands(
          resolveDemoCaller(request),
          decodeURIComponent(String(params.nodeRef)),
          store
        )
      )
    ),
    http.get(
      "*/api/v1/kernel/nodes/:nodeRef/lineage",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.getLineage(
            decodeURIComponent(String(params.nodeRef)),
            url.searchParams.get("direction") || "upstream",
            Number(url.searchParams.get("depth") || 3),
            resolveDemoCaller(request)
          )
        );
      }
    ),
    http.get(
      "*/api/v1/kernel/nodes/:nodeRef/dependents",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.getDependents(
            decodeURIComponent(String(params.nodeRef)),
            Number(url.searchParams.get("depth") || 3),
            resolveDemoCaller(request)
          )
        );
      }
    ),
    http.get("*/api/v1/kernel/nodes/:nodeRef", ({ params, request }) =>
      HttpResponse.json(
        store.getNode(
          decodeURIComponent(String(params.nodeRef)),
          resolveDemoCaller(request)
        )
      )
    ),
  ];
}
