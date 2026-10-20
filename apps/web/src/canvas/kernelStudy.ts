import { kernelApi } from "../api/kernelApi";
import { parseNodeRef, type NodeRef } from "./nodeRef";

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

/**
 * The kernel enrollment of a participant, found by walking the kernel's zoom children (study, then its study sites,
 * then their enrollments) and matching codes, or null when the kernel has no single such enrollment the reader may
 * see. The Canvas lists participants from Execution by subject and site code; kernel module reads, such as eCRF
 * queries, take the enrollment's id. A study site is matched by site code when the participant has one; an
 * enrollment by its subject code. Two matching enrollments return null rather than a guess.
 */
export async function findKernelEnrollmentRef(
  studyCode: string,
  siteCode: string | null,
  subjectCode: string
): Promise<NodeRef | null> {
  const studyRef = await findKernelStudyRef(studyCode);
  if (!studyRef) return null;
  const studySites = (await kernelApi.getChildren(studyRef)).filter(
    (child) =>
      child.node.kind === "study_site" &&
      child.node.ref &&
      (!siteCode || child.node.code === siteCode)
  );
  const enrollments = (
    await Promise.all(
      studySites.map((site) => kernelApi.getChildren(site.node.ref as string))
    )
  )
    .flat()
    .filter(
      (child) =>
        child.node.kind === "enrollment" && child.node.code === subjectCode
    );
  if (enrollments.length !== 1) return null;
  const ref = parseNodeRef(enrollments[0].node.ref);
  return ref?.kind === "enrollment" ? ref : null;
}
