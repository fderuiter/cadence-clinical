import { kernelApi } from "../api/kernelApi";

/**
 * The kernel reference of the study with exactly this code, found with kernel search (ADR-2484), or null when the
 * kernel has no such study the reader may see. The Canvas addresses a study by its code in the URL; kernel reads take
 * its NodeRef.
 */
export async function findKernelStudyRef(code: string): Promise<string | null> {
  const hits = await kernelApi.search({
    text: code,
    kinds: ["study"],
    limit: 5,
  });
  return hits.find((hit) => hit.exact_code)?.ref ?? null;
}
