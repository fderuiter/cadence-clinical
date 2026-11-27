import type { RouteLocationRaw } from "vue-router";
import {
  toFormSummaryCount,
  toSignedFormDayOffset,
  type EcrfFormSummary,
} from "../../api/ecrfFormsApi";
import type {
  StudyScheduleColumn,
  StudyScheduleRow,
} from "../../canvas/kernelStudy";

export interface LifecycleBadge {
  key: string;
  label: string;
  tone:
    | "query-open"
    | "query-answered"
    | "error-finding"
    | "verified"
    | "partial-sdv"
    | "unverified"
    | "locked"
    | "frozen"
    | "signed"
    | "sig-superseded"
    | "timing-in-window"
    | "timing-out-of-window";
  testId: string;
}

export interface FormStatusBadgeView {
  key: string;
  classes: string;
  label: string;
  dataComplete?: "true" | "false";
  testId?: string;
}

export function fallbackHref(loc: RouteLocationRaw | null | undefined): string {
  if (!loc) return "#";
  if (typeof loc === "string") return loc;
  if (typeof loc === "object" && "path" in loc && loc.path) {
    const params = new URLSearchParams();
    if ("query" in loc && loc.query) {
      for (const [k, v] of Object.entries(loc.query)) {
        if (v !== undefined && v !== null) params.set(k, String(v));
      }
    }
    const qs = params.toString();
    return qs ? `${loc.path}?${qs}` : loc.path;
  }
  return "#";
}

export function canAddRepeat(
  row: StudyScheduleRow,
  col: StudyScheduleColumn,
  instances: EcrfFormSummary[]
): boolean {
  if (!row.repeating && !col.repeating) {
    return false;
  }
  return !instances.some((inst) => Boolean(inst.locked || inst.frozen));
}

export function visitWindowBadgeClass(windowStatus: string): string {
  switch (windowStatus) {
    case "Open":
      return "clinical-badge--verified";
    case "Closed":
      return "clinical-badge--warning";
    case "NotYetOpen":
    default:
      return "clinical-badge--draft";
  }
}

export function isFormSummaryComplete(summary: EcrfFormSummary): boolean {
  return (
    Boolean(summary.complete) &&
    toFormSummaryCount(summary.missing_required_count) === 0 &&
    toFormSummaryCount(summary.open_query_count) === 0 &&
    toFormSummaryCount(summary.answered_query_count) === 0 &&
    toFormSummaryCount(summary.open_error_finding_count) === 0
  );
}

export function instanceAriaLabel(
  inst: EcrfFormSummary,
  specName: string,
  visitName: string,
  idx: number,
  total: number
): string {
  const status = isFormSummaryComplete(inst) ? "complete" : "incomplete";
  if (total > 1) {
    return `#${idx + 1} · v${inst.version} — ${specName} at ${visitName}, ${status}`;
  }
  return `${specName} at ${visitName}, ${status}`;
}

export function completenessLabel(summary: EcrfFormSummary): string {
  const recorded = toFormSummaryCount(summary.recorded_count);
  const total = toFormSummaryCount(summary.item_count);
  const missingReq = toFormSummaryCount(summary.missing_required_count);
  const errorFindings = toFormSummaryCount(summary.open_error_finding_count);
  const openQueries = toFormSummaryCount(summary.open_query_count);
  const answeredQueries = toFormSummaryCount(summary.answered_query_count);
  if (isFormSummaryComplete(summary)) {
    return `Complete (${recorded}/${total})`;
  }
  if (missingReq > 0) {
    return `Incomplete (${recorded}/${total} recorded, ${missingReq} req missing)`;
  }
  if (errorFindings > 0) {
    return `Incomplete (${recorded}/${total} recorded, ${errorFindings} ${errorFindings === 1 ? "error finding" : "error findings"})`;
  }
  if (openQueries + answeredQueries > 0) {
    return `Incomplete (${recorded}/${total} recorded, active queries)`;
  }
  return `Incomplete (${recorded}/${total} recorded)`;
}

export function lifecycleBadges(summary: EcrfFormSummary): LifecycleBadge[] {
  const badges: LifecycleBadge[] = [];
  const openQueries = toFormSummaryCount(summary.open_query_count);
  const answeredQueries = toFormSummaryCount(summary.answered_query_count);
  const errorFindings = toFormSummaryCount(summary.open_error_finding_count);
  const verified = toFormSummaryCount(summary.verified_count);
  const unverified = toFormSummaryCount(summary.unverified_count);

  if (summary.visit_timing) {
    const daysOut = toSignedFormDayOffset(summary.days_outside_window);
    if (summary.visit_timing === "InWindow") {
      badges.push({
        key: "visit-timing",
        label: "InWindow",
        tone: "timing-in-window",
        testId: "badge-visit-timing",
      });
    } else if (summary.visit_timing === "Early") {
      badges.push({
        key: "visit-timing",
        label: daysOut && daysOut > 0 ? `Early (-${daysOut}d)` : "Early",
        tone: "timing-out-of-window",
        testId: "badge-visit-timing",
      });
    } else if (summary.visit_timing === "Late") {
      badges.push({
        key: "visit-timing",
        label: daysOut && daysOut > 0 ? `Late (+${daysOut}d)` : "Late",
        tone: "timing-out-of-window",
        testId: "badge-visit-timing",
      });
    }
  }

  if (errorFindings > 0) {
    badges.push({
      key: "error-findings",
      label: `${errorFindings} ${errorFindings === 1 ? "error finding" : "error findings"}`,
      tone: "error-finding",
      testId: "badge-error-findings",
    });
  }
  if (openQueries > 0) {
    badges.push({
      key: "open-q",
      label: `${openQueries} open ${openQueries === 1 ? "query" : "queries"}`,
      tone: "query-open",
      testId: "badge-open-queries",
    });
  }
  if (answeredQueries > 0) {
    badges.push({
      key: "answered-q",
      label: `${answeredQueries} answered`,
      tone: "query-answered",
      testId: "badge-answered-queries",
    });
  }

  if (verified > 0 && unverified === 0) {
    badges.push({
      key: "sdv-full",
      label: `SDV verified (${verified})`,
      tone: "verified",
      testId: "badge-sdv-verified",
    });
  } else if (verified > 0 && unverified > 0) {
    badges.push({
      key: "sdv-partial",
      label: `SDV ${verified}/${verified + unverified}`,
      tone: "partial-sdv",
      testId: "badge-sdv-partial",
    });
  } else if (
    unverified > 0 &&
    (summary.verified_count !== undefined ||
      summary.unverified_count !== undefined)
  ) {
    badges.push({
      key: "sdv-none",
      label: `${unverified} unverified`,
      tone: "unverified",
      testId: "badge-sdv-unverified",
    });
  }

  if (summary.locked) {
    badges.push({
      key: "locked",
      label: "Locked",
      tone: "locked",
      testId: "badge-locked",
    });
  } else if (summary.frozen) {
    badges.push({
      key: "frozen",
      label: "Frozen",
      tone: "frozen",
      testId: "badge-frozen",
    });
  }

  if (summary.signature_superseded) {
    badges.push({
      key: "sig-superseded",
      label: "Signature superseded",
      tone: "sig-superseded",
      testId: "badge-signature-superseded",
    });
  } else if (summary.signed) {
    badges.push({
      key: "signed",
      label: "Signed",
      tone: "signed",
      testId: "badge-signed",
    });
  }

  return badges;
}

export function clinicalBadgeModifier(tone: LifecycleBadge["tone"]): string {
  switch (tone) {
    case "verified":
    case "timing-in-window":
      return "clinical-badge--verified";
    case "signed":
      return "clinical-badge--signed";
    case "locked":
      return "clinical-badge--locked";
    case "frozen":
    case "query-answered":
      return "clinical-badge--frozen";
    case "query-open":
      return "clinical-badge--queried";
    case "error-finding":
      return "clinical-badge--error";
    case "partial-sdv":
    case "sig-superseded":
    case "timing-out-of-window":
      return "clinical-badge--warning";
    case "unverified":
    default:
      return "clinical-badge--draft";
  }
}

export function instanceStatusBadges(
  summary: EcrfFormSummary,
  flatStatusClass = false
): FormStatusBadgeView[] {
  const completenessClasses = flatStatusClass
    ? `clinical-badge forms-status ${summary.complete ? "clinical-badge--verified" : "clinical-badge--warning"}`
    : `clinical-badge cb-badge ${summary.complete ? "clinical-badge--verified cb-badge--complete" : "clinical-badge--warning cb-badge--incomplete"}`;
  return [
    {
      key: "completeness",
      classes: completenessClasses,
      label: completenessLabel(summary),
      dataComplete: summary.complete ? "true" : "false",
    },
    ...lifecycleBadges(summary).map((badge) => ({
      key: badge.key,
      classes: `clinical-badge cb-badge ${clinicalBadgeModifier(badge.tone)} cb-badge--${badge.tone}`,
      label: badge.label,
      testId: badge.testId,
    })),
  ];
}
