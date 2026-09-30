import { kernelApi } from "../api/kernelApi";

const inFlight = new Map<string, Promise<string | null>>();

/**
 * The kernel reference of the study with exactly this code, found with kernel search (ADR-2484), or null when the
 * kernel has no such study the reader may see. The Canvas addresses a study by its code in the URL; kernel reads take
 * its NodeRef. Panes that ask for the same code at the same time share one search; nothing is kept once it answers,
 * so a study created or granted later is found on the next read.
 */
export function findKernelStudyRef(code: string): Promise<string | null> {
  const pending = inFlight.get(code);
  if (pending) return pending;
  const lookup = kernelApi
    .search({ text: code, kinds: ["study"], limit: 5 })
    .then((hits) => hits.find((hit) => hit.exact_code)?.ref ?? null)
    .finally(() => inFlight.delete(code));
  inFlight.set(code, lookup);
  return lookup;
}
