import type { components } from "./generated/kernel";

type Schemas = components["schemas"];

/** A kernel observation's typed value (ADR-2623): a quantity, code, text, boolean, date/time, or a missing value. */
export type KernelObservationValue = Schemas["Kernel_ObservationValue"];

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
  value: KernelObservationValue | null | undefined
): string | null {
  return value?.type === "quantity" ? value.unit : null;
}

/** The result of reading what the user typed as a correction of the current value. */
export type ParsedCorrection =
  { ok: true; value: KernelObservationValue } | { ok: false; message: string };

/**
 * Reads the text the user typed as a value of the same kind as the current one: a quantity keeps its unit (the number
 * is sent as typed, so 1.50 keeps its precision), a code keeps its code system version. A value that was missing has
 * no kind to keep, so its correction is recorded as text.
 */
export function parseCorrection(
  text: string,
  current: KernelObservationValue
): ParsedCorrection {
  const typed = text.trim();
  if (typed.length === 0) return { ok: false, message: "Enter a value." };
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
    case "coded":
      return {
        ok: true,
        value: {
          type: "coded",
          code_system_version_id: current.code_system_version_id,
          code: typed,
        },
      };
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
  const fields = Object.entries(value)
    .filter(([, field]) => field !== null && field !== undefined)
    .sort(([x], [y]) => (x < y ? -1 : 1));
  return JSON.stringify(fields);
}

/** True when an item has no user-recorded value yet (either null or the kernel's NotAsked placeholder). */
export function isUnrecordedValue(
  value: KernelObservationValue | null | undefined
): boolean {
  return (
    !value || (value.type === "missing" && String(value.reason) === "NotAsked")
  );
}

/** The reasons a user can record a value as missing (ADR-2623 null flavors; Masked and NotAsked are the kernel's), with how each reads. */
export const MISSING_REASONS = Object.entries(NULL_FLAVOR_TEXT)
  .filter(([reason]) => reason !== "Masked" && reason !== "NotAsked")
  .map(([reason, text]) => ({ reason, text }));

/** What a form item says about the value it takes (Kernel_FormItemResponse). */
export interface FormEntryItem {
  value_kind: string | null;
  unit: string | null;
  value: KernelObservationValue | null;
}

/**
 * Reads what the user typed into a form item as a typed value. An item with a value keeps that value's kind (as a
 * correction does); otherwise the item's value kind decides: a quantity in the item's UCUM unit, Yes/No, a date/time,
 * or text. A coded item names no code-system version, so its first value cannot be entered as a code here.
 */
export function parseFormEntry(
  text: string,
  item: FormEntryItem
): ParsedCorrection {
  if (item.value && item.value.type !== "missing") {
    return parseCorrection(text, item.value);
  }
  switch ((item.value_kind ?? "").toLowerCase()) {
    case "quantity":
      if (!item.unit) {
        return { ok: false, message: "This item names no unit." };
      }
      return parseCorrection(text, {
        type: "quantity",
        value: 0,
        unit: item.unit,
      });
    case "coded":
      return text.trim().length === 0
        ? { ok: false, message: "Enter a value." }
        : {
            ok: false,
            message:
              "A coded value needs its code system from the protocol and cannot be entered here yet; record it as missing instead.",
          };
    case "boolean":
      return parseCorrection(text, { type: "boolean", value: false });
    case "datetime":
      return parseCorrection(text, { type: "dateTime", value: "" });
    default:
      return parseCorrection(text, { type: "text", value: "" });
  }
}
