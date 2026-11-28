import type { components } from "./generated/kernel";

type Schemas = components["schemas"];

/** A kernel observation's typed value (ADR-2623): a quantity, code, text, boolean, date/time, or a missing value. */
export type KernelObservationValue = Schemas["Kernel_ObservationValue"];

/** One permissible option from an item's governed codelist (Kernel_CodelistOptionView). */
export interface CodelistOption {
  code: string;
  display?: string | null;
  submission_value?: string | null;
}

/** Expected collection item metadata from the protocol's CollectionSpec when correcting a missing/unasked value. */
export interface ExpectedItemSpec {
  valueKind?: string | null;
  unit?: string | null;
  codeSystemVersionId?: string | null;
  codelist?: string | null;
  codelistOptions?: ReadonlyArray<CodelistOption> | null;
}

const NULL_FLAVOR_TEXT: Record<string, string> = {
  NotDone: "Not done",
  Unknown: "Unknown",
  AskedButUnknown: "Asked but unknown",
  NotAsked: "Not asked",
  NotApplicable: "Not applicable",
  Masked: "Masked",
};

const QUANTITY = /^(<=|>=|<|>)?\s*([+-]?\d+(?:\.\d+)?)$/;

/** How a typed value reads on screen: a quantity with its UCUM unit, a code, text, Yes/No, a date, or why it is missing. */
export function formatObservationValue(
  value: KernelObservationValue | null | undefined
): string {
  if (!value) return "";
  switch (value.type) {
    case "quantity":
      return `${value.comparator ?? ""}${value.value} ${value.unit}`;
    case "coded":
      return value.code;
    case "boolean":
      return value.value ? "Yes" : "No";
    case "missing": {
      const reason =
        NULL_FLAVOR_TEXT[String(value.reason)] ?? String(value.reason);
      return value.detail ? `${reason}: ${value.detail}` : reason;
    }
    case "text":
    case "dateTime":
      return value.value;
    default:
      return "";
  }
}

/** The unit a correction of this value is entered in, or null when the value has none. */
export function correctionUnit(
  value: KernelObservationValue | null | undefined,
  expectedSpec?: ExpectedItemSpec | null
): string | null {
  if (value?.type === "quantity") return value.unit;
  if (
    value?.type === "missing" &&
    (expectedSpec?.valueKind ?? "").toLowerCase() === "quantity"
  ) {
    return expectedSpec?.unit ?? null;
  }
  return null;
}

/** The result of reading what the user typed as a correction of the current value. */
export type ParsedCorrection =
  { ok: true; value: KernelObservationValue } | { ok: false; message: string };

/**
 * Reads the text the user typed as a value of the same kind as the current one: a quantity keeps its unit (the number
 * is sent as typed, so 1.50 keeps its precision), a code keeps its code system version. When correcting a missing
 * value that belongs to a CollectionSpec item (`expectedSpec`), the expected kind, unit, and code system are enforced.
 */
export function parseCorrection(
  text: string,
  current: KernelObservationValue,
  expectedSpec?: ExpectedItemSpec | null
): ParsedCorrection {
  const typed = text.trim();
  if (typed.length === 0) return { ok: false, message: "Enter a value." };
  if (current.type === "missing" && expectedSpec?.valueKind) {
    switch (expectedSpec.valueKind.toLowerCase()) {
      case "quantity": {
        if (!expectedSpec.unit) {
          return { ok: false, message: "This item names no unit." };
        }
        return parseCorrection(typed, {
          type: "quantity",
          value: 0,
          unit: expectedSpec.unit,
        });
      }
      case "coded": {
        if (!expectedSpec.codeSystemVersionId) {
          return {
            ok: false,
            message:
              "A coded value needs its code system from the protocol and cannot be entered without one.",
          };
        }
        const codelistProblem = validateCodelistCode(
          typed,
          expectedSpec.codelistOptions,
          expectedSpec.codelist
        );
        if (codelistProblem) {
          return { ok: false, message: codelistProblem };
        }
        return {
          ok: true,
          value: {
            type: "coded",
            code_system_version_id: expectedSpec.codeSystemVersionId,
            code: typed,
          },
        };
      }
      case "boolean":
        return parseCorrection(typed, { type: "boolean", value: false });
      case "datetime":
        return { ok: true, value: { type: "dateTime", value: typed } };
      default:
        return { ok: true, value: { type: "text", value: typed } };
    }
  }
  switch (current.type) {
    case "quantity": {
      const match = QUANTITY.exec(typed);
      if (!match) {
        return {
          ok: false,
          message: `Enter a number in ${current.unit}, optionally with <, <=, > or >=.`,
        };
      }
      const comparator = (match[1] ?? null) as Extract<
        KernelObservationValue,
        { type?: "quantity" }
      >["comparator"];
      return {
        ok: true,
        value: {
          type: "quantity",
          value: match[2],
          unit: current.unit,
          comparator,
        },
      };
    }
    case "coded": {
      const codelistProblem = validateCodelistCode(
        typed,
        expectedSpec?.codelistOptions,
        expectedSpec?.codelist
      );
      if (codelistProblem) {
        return { ok: false, message: codelistProblem };
      }
      return {
        ok: true,
        value: {
          type: "coded",
          code_system_version_id:
            expectedSpec?.codeSystemVersionId ?? current.code_system_version_id,
          code: typed,
        },
      };
    }
    case "boolean": {
      const answer = typed.toLowerCase();
      if (["yes", "true"].includes(answer))
        return { ok: true, value: { type: "boolean", value: true } };
      if (["no", "false"].includes(answer))
        return { ok: true, value: { type: "boolean", value: false } };
      return { ok: false, message: "Enter Yes or No." };
    }
    case "dateTime":
      return { ok: true, value: { type: "dateTime", value: typed } };
    default:
      return { ok: true, value: { type: "text", value: typed } };
  }
}

function validateCodelistCode(
  code: string,
  options?: ReadonlyArray<CodelistOption> | null,
  codelist?: string | null
): string | null {
  if (!options || options.length === 0) return null;
  if (options.some((option) => option.code === code)) return null;
  const allowed = options.map((option) => option.code).join(", ");
  return codelist
    ? `Select a valid code from codelist ${codelist} (${allowed}).`
    : `Select a valid code (${allowed}).`;
}

/** Whether two typed values say the same thing (1.50 and 1.5 mm[Hg] are the same quantity). */
export function sameObservationValue(
  a: KernelObservationValue | null | undefined,
  b: KernelObservationValue | null | undefined
): boolean {
  if (!a || !b) return a === b;
  if (a.type === "quantity" && b.type === "quantity") {
    return (
      Number(a.value) === Number(b.value) &&
      a.unit === b.unit &&
      (a.comparator ?? null) === (b.comparator ?? null)
    );
  }
  return canonical(a) === canonical(b);
}

// Field order and absent optional fields do not change what a value says.
function canonical(value: KernelObservationValue): string {
  if (value.type === "missing") {
    const detail = typeof value.detail === "string" ? value.detail.trim() : "";
    const normalized: Array<[string, unknown]> = detail
      ? [
          ["detail", detail],
          ["reason", value.reason],
          ["type", "missing"],
        ]
      : [
          ["reason", value.reason],
          ["type", "missing"],
        ];
    return JSON.stringify(normalized);
  }
  const fields = Object.entries(value)
    .filter(([, field]) => field !== null && field !== undefined)
    .sort(([x], [y]) => (x < y ? -1 : 1));
  return JSON.stringify(fields);
}

/** True when an item has no user-recorded value yet (either null or the kernel's NotAsked placeholder). */
export function isUnrecordedValue(
  value: KernelObservationValue | null | undefined
): boolean {
  if (!value) return true;
  if (value.type !== "missing") return false;
  const reason = String(value.reason).toLowerCase();
  return reason === "notasked" || reason === "not_asked";
}

/** The reasons a user can record a value as missing (ADR-2623 null flavors; Masked and NotAsked are the kernel's), with how each reads. */
export const MISSING_REASONS = Object.entries(NULL_FLAVOR_TEXT)
  .filter(([reason]) => reason !== "Masked" && reason !== "NotAsked")
  .map(([reason, text]) => ({ reason, text }));

/** What a form item says about the value it takes (Kernel_FormItemResponse). */
export interface FormEntryItem {
  value_kind: string | null;
  unit: string | null;
  code_system_version_id?: string | null;
  codelist?: string | null;
  codelist_options?: ReadonlyArray<CodelistOption> | null;
  value: KernelObservationValue | null;
}

/**
 * Reads what the user typed into a form item as a typed value. When the item declares a `value_kind`, that expected
 * kind (and declared unit / code system) governs parsing even after missing or legacy revisions; otherwise an existing
 * non-missing value's kind decides.
 */
export function parseFormEntry(
  text: string,
  item: FormEntryItem
): ParsedCorrection {
  switch ((item.value_kind ?? "").toLowerCase()) {
    case "quantity": {
      const unit =
        item.unit ?? (item.value?.type === "quantity" ? item.value.unit : null);
      if (!unit) {
        return { ok: false, message: "This item names no unit." };
      }
      return parseCorrection(text, {
        type: "quantity",
        value: 0,
        unit,
      });
    }
    case "coded": {
      const codeSystemVersionId =
        item.code_system_version_id ??
        (item.value?.type === "coded" &&
        item.value.code_system_version_id != null
          ? String(item.value.code_system_version_id)
          : null);
      const itemSpec: ExpectedItemSpec = {
        valueKind: item.value_kind,
        unit: item.unit,
        codeSystemVersionId,
        codelist: item.codelist ?? null,
        codelistOptions: item.codelist_options ?? null,
      };
      if (codeSystemVersionId) {
        return parseCorrection(
          text,
          {
            type: "coded",
            code_system_version_id: codeSystemVersionId,
            code: "",
          },
          itemSpec
        );
      }
      if (item.value && item.value.type !== "missing") {
        return parseCorrection(text, item.value, itemSpec);
      }
      return text.trim().length === 0
        ? { ok: false, message: "Enter a value." }
        : {
            ok: false,
            message:
              "A coded value needs its code system from the protocol and cannot be entered here yet; record it as missing instead.",
          };
    }
    case "boolean":
      return parseCorrection(text, { type: "boolean", value: false });
    case "datetime":
      return parseCorrection(text, { type: "dateTime", value: "" });
    case "text":
      return parseCorrection(text, { type: "text", value: "" });
    default:
      if (item.value && item.value.type !== "missing") {
        if (item.value.type === "quantity" && !item.value.unit && item.unit) {
          return parseCorrection(text, { ...item.value, unit: item.unit });
        }
        return parseCorrection(text, item.value, {
          valueKind: item.value_kind,
          unit: item.unit,
          codeSystemVersionId: item.code_system_version_id ?? null,
          codelist: item.codelist ?? null,
          codelistOptions: item.codelist_options ?? null,
        });
      }
      return parseCorrection(text, { type: "text", value: "" });
  }
}
