/**
 * eConsent Presentation and Workflow Normalization Utilities.
 */

/**
 * Normalizes patient-facing approved consent content response into an ordered list of sections.
 *
 * @param {Object} content - Approved composed template translation response.
 * @returns {Array<Object>} List of displayable section blocks.
 */
export function normalizeApprovedConsent(content) {
  if (!content) return [];
  const sections = [];

  // 1. Metadata / Header Section
  sections.push({
    id: "metadata",
    type: "metadata",
    title: content.template_name || "Informed Consent Form",
    metadata: {
      template_id: content.template_id,
      study_id: content.study_id,
      protocol_version: content.protocol_version,
      version_index: content.version_index,
      language_code: content.language_code,
      requires_reconsent: content.requires_reconsent,
    },
  });

  // 2. Resolved ordered clauses
  const clauses = content.clauses || [];
  clauses.forEach((clause) => {
    sections.push({
      id: clause.clause_id,
      type: "clause",
      title: clause.title,
      content: clause.text,
      version_index: clause.version_index,
    });
  });

  // 3. Workflow Steps
  const steps = content.workflow_steps || [];
  steps.forEach((step, idx) => {
    sections.push({
      id: `workflow-step-${idx}`,
      type: "workflow_step",
      title: step.type === "comprehension_check" ? "Comprehension Check" : "Signature Requirement",
      step: step,
    });
  });

  return sections;
}

/**
 * Shapes comprehension answers dictionary for submission payload.
 *
 * @param {string} subjectPseudonym - Pseudonym identifier of the subject.
 * @param {Object} answers - Mapping of question_id to answer strings.
 * @param {string} [reasonForChange="Comprehension check submission"] - Part 11 Audit change reason.
 * @returns {Object} Submission request body payload.
 */
export function shapeComprehensionAnswers(
  subjectPseudonym,
  answers,
  reasonForChange = "Comprehension check submission"
) {
  return {
    subject_pseudonym: subjectPseudonym,
    submitted_answers: answers,
    reason_for_change: reasonForChange,
  };
}

/**
 * Interprets backend ComprehensionSubmissionResponse into a UI-agnostic gating decision.
 *
 * @param {Object} response - The backend submission response.
 * @returns {Object} { canSign: boolean, nextStep: string, message: string }
 */
export function interpretComprehensionResult(response) {
  if (!response) {
    return {
      canSign: false,
      nextStep: "retry_checks",
      message: "No submission response received.",
    };
  }

  const passed = response.passed === true;
  const nextStep = response.next_step || "retry_checks";

  return {
    canSign: passed && nextStep === "sign_consent",
    nextStep,
    message: response.message || "",
  };
}
