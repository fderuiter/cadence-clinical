// Auto-generated from OpenAPI schema definition
// Generated on: 2026-08-20T17:53:52.227Z

export interface ActivityAssignmentRequest {
  visit_id: string;
  procedure_ids?: string[];
  activity_ids?: string[];
}

export interface ActivityReport {
  epoch_id: string | null;
  epoch_internal_id: number;
  scheduled_event_id: string | null;
  scheduled_event_internal_id: number;
  activity_def_id: string | null;
  activity_def_internal_id: number;
  status: string;
  unmapped_items: ItemMappingStatus[];
  mapped_items: ItemMappingStatus[];
}

export interface AllowableUnit {
  ucum_code: string;
  name: string;
}

export interface AmendmentImpactReport {
  base_version?: string | null;
  amended_version?: string | null;
  added_forms_count?: number;
  modified_forms_count?: number;
  deleted_forms_count?: number;
  estimated_cost_usd?: number;
  burden_change?: number;
  explanation: string;
}

export interface AmendmentImpactSummary {
  base_version: string;
  amended_version: string;
  burden_delta?: number;
  affected_visits_count?: number;
  affected_visits?: string[];
  affected_activities_count?: number;
  affected_activities?: string[];
  schema_revisions?: SchemaRevisionSummary;
  is_substantial?: boolean;
  requires_reconsent?: boolean;
  estimated_cost_usd?: number;
  narrative_summary?: string;
}

export interface ApproveProtocolRequest {
  signing_reason: SigningReason;
}

export interface ArmAttributes {
  arm_type: string;
  target_sample_size: number;
  randomization_ratio: string;
}

export interface ArmLibraryObjectDetail {
  id: string;
  version: string;
  status: LibraryStatus;
  sponsor_id: string;
  tenant_id: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
  prior_status?: string | null;
  object_type?: string;
  payload: ArmPayload;
}

export interface ArmPayload {
  attributes: ArmAttributes;
}

export interface ArmReorderItem {
  arm_id: string;
  sequence: number;
}

export interface ArmReorderRequest {
  arms: ArmReorderItem[];
}

export interface AttritionStep {
  criterion_id: string;
  type: string;
  description: string;
  passed_count: number;
  failed_count: number;
  remaining_count: number;
  attrition_rate: number;
}

export interface BlockCreatedResponse {
  status?: string;
  id: string;
}

export interface BlockDetailResponse {
  id: string;
  block_id: string;
  block_type: string;
  order: number;
  version_index: number;
  created_by: string;
  created_at: string;
}

export interface Body_extract_protocol_digitization_api_v1_designer_digitization_extract_post {
  file: string;
}

export interface Body_upload_mapping_csv_api_v1_mappings_upload_post {
  file: string;
}

export interface Body_upload_protocol_ingestion_api_v1_designer_ingestion_upload_post {
  file: string;
}

export interface BranchAmendmentRequest {
  study_id: string;
  base_version_tag?: string;
  amendment_type?: string;
  requires_reconsent?: boolean;
  change_reason: string;
  branch_name?: string | null;
}

export interface BranchAmendmentResponse {
  study_id: string;
  branch_id: string;
  branch_name: string;
  base_version_tag: string;
  new_version_tag: string;
  version_id: string;
  status: string;
  requires_reconsent: boolean;
  created_by: string;
  created_at: string;
}

export interface BurdenTraceItem {
  component: string;
  count: number;
  weight: number;
  subtotal: number;
  explanation: string;
}

export interface BurdenTraceReport {
  visit_burden: number;
  procedure_burden: number;
  activity_burden: number;
  total_burden: number;
  trace?: BurdenTraceItem[];
}

export interface CDASHMapping {
  domain: string;
  variable_name: string;
  data_type: string;
}

export interface CascadeSummaryReport {
  study_id: string;
  amendment_version?: number;
  forms_created: number;
  visits_created: number;
  rules_synced: number;
  forms?: CascadedFormTemplate[];
}

export interface CascadedFormTemplate {
  form_id: string;
  form_name: string;
  domain: string;
  fields?: Record<string, any>[];
  auto_generated?: boolean;
}

export type CodeValidationState = "VALID" | "INVALID" | "DEGRADED";

export interface Comment {
  comment_id: string;
  thread_id: string;
  text: string;
  created_by: string;
  created_at?: string;
  updated_at?: string | null;
  version_index?: number;
}

export interface CommentCreate {
  text: string;
}

export interface CommentCreatePayload {
  field_id: string;
  comment_text: string;
}

export interface CommentThread {
  thread_id: string;
  block_id: string;
  section_id: string;
  study_id: string;
  status?: string;
  created_by: string;
  created_at?: string;
  block_version_index: number;
  comments?: Comment[];
}

export interface CommentThreadCreate {
  block_id: string;
  text: string;
}

export interface CommitUSDMRequest {
  study_id: string;
  data: USDMProtocolExtractionResponse;
  change_reason: string;
}

export interface CommitUSDMResponse {
  study_id: string;
  version_id: string;
  status: string;
  nodes_created: number;
  relationships_created: number;
  synthesized_forms?: SynthesizedECRFForm[];
  message: string;
}

export type ComparisonOperator = "==" | "!=" | "<" | "<=" | ">" | ">=";

export interface ConceptDetail {
  id: string;
  concept_code: string;
  terminology: string;
  display_name: string;
  definition: string;
  cdash_mapping?: CDASHMapping | null;
  allowable_units?: AllowableUnit[] | null;
  version: string;
  status: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
}

export interface ConceptListResponse {
  object: string;
  data: ConceptDetail[];
  has_more: boolean;
  next_cursor?: string | null;
}

export interface ConceptReference {
  element_type: string;
  element_id: string;
  element_name: string;
  attribute: string;
}

export interface ConceptValidationReport {
  concept_code: string;
  state: CodeValidationState;
  decode?: string | null;
  system?: string | null;
  error_message?: string | null;
  references?: ConceptReference[];
}

export interface CreateArmRequest {
  id: string;
  version?: string;
  status?: LibraryStatus;
  sponsor_id: string;
  change_reason: string;
  object_type?: string;
  payload: ArmPayload;
}

export interface CreateBlockRequest {
  id: string;
  block_type: string;
  order: number;
  properties: Record<string, any>;
  change_reason?: string | null;
}

export interface CreateConceptRequest {
  concept_code: string;
  terminology: string;
  display_name: string;
  definition: string;
  cdash_mapping?: CDASHMapping | null;
  allowable_units?: AllowableUnit[] | null;
  change_reason: string;
}

export interface CreateDataElementRequest {
  id: string;
  version?: string;
  status?: LibraryStatus;
  sponsor_id: string;
  change_reason: string;
  object_type?: string;
  payload: DataElementPayload;
}

export interface CreateEligibilityCriterionRequest {
  criterion_id: string;
  criterion_type: "inclusion" | "exclusion";
  description: string;
  dsl_source: string;
  expected_outcome?: boolean;
  change_reason: string;
}

export interface CreateEpochRequest {
  id: string;
  properties: EpochProperties;
  change_reason?: string;
}

export interface CreateFormRequest {
  id: string;
  version?: string;
  status?: LibraryStatus;
  sponsor_id: string;
  change_reason: string;
  object_type?: string;
  payload: FormPayload;
}

export interface CreateProcedureRequest {
  id: string;
  properties: ProcedureProperties;
  change_reason?: string;
}

export interface CreateRuleRequest {
  type: "skip_logic" | "constraint" | "cross_form_check";
  condition: ExpressionNode_Input;
  action?: "show" | "hide" | null;
  target_field?: string | null;
  target_form?: string | null;
  target_group?: string | null;
  query_message?: string | null;
}

export interface CreateStudyArmRequest {
  id: string;
  properties: StudyArmProperties;
  change_reason?: string;
}

export interface CreateStudyVersionRequest {
  id: string;
  version_tag: string;
  status: string;
  version_index: number;
}

export interface CreateTimingWindowRequest {
  id: string;
  properties: TimingWindowProperties;
  change_reason?: string;
}

export interface DataElementLibraryObjectDetail {
  id: string;
  version: string;
  status: LibraryStatus;
  sponsor_id: string;
  tenant_id: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
  prior_status?: string | null;
  object_type?: string;
  payload: DataElementPayload;
}

export interface DataElementPayload {
  data_type: string;
  allowable_units: string[];
  default_unit?: string | null;
}

export interface DifferenceResult {
  field: string;
  old_value: any;
  new_value: any;
}

export interface EligibilityCriterion {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id?: string;
  criterion_type: "inclusion" | "exclusion";
  identifier?: string;
  human_readable_text?: string;
  dsl_expression_string?: string;
  structured_expression_tree?: ExpressionNode_Output;
  expected_outcome?: boolean;
  criterion_id?: string;
  description?: string;
  dsl_source?: string;
  condition?: ExpressionNode_Output;
}

export interface EntityDiff {
  entity_id: string;
  entity_type: string;
  name: string;
  change_type: string;
  spec?: string | null;
  schedule?: string | null;
  delta_note?: string | null;
  old_value?: any | null;
  new_value?: any | null;
}

export interface EpochProperties {
  name?: string | null;
  epoch_name?: string | null;
  sequence: number;
}

export interface EpochReorderItem {
  epoch_id: string;
  sequence: number;
}

export interface EpochReorderRequest {
  epochs: EpochReorderItem[];
}

export interface ExpressionNode_Input {
  type: "logical" | "comparison" | "function" | "field_ref" | "constant";
  operator?: string | null;
  operands?: ExpressionNode_Input[] | null;
  value?: any | null;
  field_ref?: FieldReference_Input | null;
}

export interface ExpressionNode_Output {
  type: "logical" | "comparison" | "field_ref" | "constant";
  operator?: ComparisonOperator | LogicalOperator | string | null;
  operands?: ExpressionNode_Output[] | null;
  value?: any | null;
  field_ref?: FieldReference_Output | null;
}

export interface ExtractedActivity {
  activity_name: string;
  cdash_domain: string;
  biomedical_concept_code?: string | null;
  assigned_visit_names?: string[];
}

export interface ExtractedArm {
  name: string;
  arm_type: "EXPERIMENTAL" | "ACTIVE_COMPARATOR" | "PLACEBO_COMPARATOR" | "SHAM_COMPARATOR" | "NO_INTERVENTION";
  description?: string | null;
  target_sample_size?: number | null;
}

export interface ExtractedCriterion {
  criterion_type: "INCLUSION" | "EXCLUSION";
  identifier: string;
  text_expression: string;
  logical_expression?: string | null;
}

export interface ExtractedEpoch {
  name: string;
  epoch_type: "SCREENING" | "TREATMENT" | "WASHOUT" | "FOLLOW_UP" | "RUN_IN";
  sequence_index: number;
}

export interface ExtractedVisit {
  visit_name: string;
  epoch_name: string;
  target_day: number;
  window_lower_days?: number;
  window_upper_days?: number;
  is_mandatory?: boolean;
}

export interface FeasibilityReport {
  starting_cohort_size: number;
  final_eligible_count: number;
  overall_eligibility_rate: number;
  attrition_steps?: AttritionStep[];
}

export interface FieldReference_Input {
  field_id: string;
  form_id?: string | null;
  visit_id?: string | null;
  visit_relative?: string | null;
}

export interface FieldReference_Output {
  raw_reference: string;
  domain: string;
  variable: string;
}

export interface FormItem {
  item_id: string;
  name: string;
  question_text: string;
  data_type: string;
  required?: boolean;
}

export interface FormLibraryObjectDetail {
  id: string;
  version: string;
  status: LibraryStatus;
  sponsor_id: string;
  tenant_id: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
  prior_status?: string | null;
  object_type?: string;
  payload: FormPayload;
}

export interface FormPayload {
  items: FormItem[];
}

export interface FormReviewCommentResponse {
  id: string;
  form_id: string;
  field_id: string;
  author_id: string;
  comment_text: string;
  status: string;
  created_at: string;
  isResolved: boolean;
  authorName: string;
  createdAt: string;
  text: string;
}

export interface HTTPValidationError {
  detail?: ValidationError[];
}

export interface InstantiateLibraryObjectRequest {
  library_object_id: string;
  version?: number | null;
}

export interface InstantiatedFromDetail {
  library_object_id: string;
  version: number;
  sponsor_id: string;
}

export interface InvalidParam {
  field?: string | null;
  reason?: string | null;
  value?: string | null;
}

export interface ItemMappingStatus {
  item_id: string | null;
  internal_id: number | null;
  is_mapped: boolean;
}

export interface LibraryInstanceResponse {
  id: string;
  study_id: string;
  object_type: string;
  payload: Record<string, any>;
  created_at: string;
  created_by: string;
  instantiated_from: InstantiatedFromDetail;
}

export interface LibraryObjectAmendRequest {
  reason_for_change: string;
  payload?: Record<string, any> | null;
}

export interface LibraryObjectListResponse {
  object?: string;
  data: FormLibraryObjectDetail | DataElementLibraryObjectDetail | ArmLibraryObjectDetail | VisitLibraryObjectDetail[];
  has_more: boolean;
  next_cursor?: string | null;
}

export interface LibraryObjectTransitionRequest {
  status: LibraryStatus;
  change_reason: string;
}

export type LibraryStatus = "DRAFT" | "IN_REVIEW" | "APPROVED" | "PUBLISHED" | "ARCHIVED" | "REJECTED";

export interface LinkArmApplicabilityRequest {
  arm_id: string;
  target_id: string;
  target_type?: "visit" | "procedure" | "epoch";
}

export interface LinkEpochVisitRequest {
  epoch_id: string;
  visit_id: string;
}

export interface LinkTimingRequest {
  source_id: string;
  timing_id: string;
  source_type?: "visit" | "procedure";
}

export interface LinkVisitProcedureRequest {
  visit_id: string;
  procedure_id: string;
}

export type LogicalOperator = "and" | "or" | "not";

export interface MigrationDirective {
  directive_id: string;
  action: string;
  description: string;
  affected_cohort?: string;
  target_version: string;
}

export type ObjectType = "FORM" | "DATA_ELEMENT" | "ARM" | "VISIT";

export interface ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail: string;
  instance: string;
  code: string;
  invalid_params?: InvalidParam[] | null;
}

export interface ProcedureProperties {
  name?: string | null;
  activity_name?: string | null;
}

export interface ProcedureReorderItem {
  procedure_id: string;
  sequence: number;
}

export interface ProcedureReorderRequest {
  procedures: ProcedureReorderItem[];
}

export interface PromoteRequest {
  change_reason: string;
}

export interface ProtocolAmendRequest {
  amendment_type?: string | null;
  type?: string | null;
}

export interface ProtocolQualityScore {
  study_id: string;
  quality_score: number;
  patient_burden_index: number;
  findings?: QualityRuleFinding[];
  passed: boolean;
  readability?: ReadabilityReport | null;
  burden_details?: BurdenTraceReport | null;
  amendment_impact?: AmendmentImpactReport | null;
  feasibility?: FeasibilityReport | null;
}

export interface PublishStudyVersionRequest {
  description?: string | null;
  baseline_snapshot?: Record<string, any> | null;
  amended_snapshot?: Record<string, any> | null;
}

export interface PublishStudyVersionResponse {
  status: string;
  version_id: string;
  amendment_id?: string | null;
  summary_of_changes?: string | null;
}

export interface QualityRuleFinding {
  rule_id: string;
  severity: string;
  category: string;
  message: string;
  target_node_id?: string | null;
}

export interface ReadabilityReport {
  flesch_reading_ease: number;
  flesch_kincaid_grade_level: number;
  word_count: number;
  sentence_count: number;
  syllable_count: number;
  interpretation: string;
}

export interface RenameConceptRequest {
  display_name: string;
  reason_for_change: string;
}

export interface ReorderBlocksRequest {
  block_ids: string[];
  change_reason?: string | null;
}

export interface RulePreviewResponse {
  xpath: string;
  failures: string[];
  circular_cycles: string[];
}

export interface SchemaRevisionSummary {
  arms?: Record<string, any>;
  epochs?: Record<string, any>;
  encounters?: Record<string, any>;
  activities?: Record<string, any>;
  eligibility_criteria?: Record<string, any>;
  forms?: Record<string, any>;
}

export type SectionReviewStatus = "DRAFT" | "IN_REVIEW" | "LOCKED" | "APPROVED";

export interface SectionReviewTransition {
  transition_id: string;
  section_id: string;
  study_id: string;
  from_status: SectionReviewStatus;
  to_status: SectionReviewStatus;
  actor_id: string;
  actor_role: string;
  reason_for_change: string;
  timestamp?: string;
}

export interface SectionTransitionRequest {
  to_status: SectionReviewStatus;
  reason_for_change: string;
  username?: string | null;
  password?: string | null;
  signing_reason?: SigningReason | null;
}

export interface SemanticDiffRequest {
  study_id: string;
  base_version_tag: string;
  amended_version_tag: string;
  base_payload?: Record<string, any> | null;
  draft_payload?: Record<string, any> | null;
}

export interface SemanticDiffResponse {
  study_id: string;
  base_version_tag: string;
  amended_version_tag: string;
  usdm_graph_diffs?: EntityDiff[];
  soa_matrix_diffs?: EntityDiff[];
  eligibility_diffs?: EntityDiff[];
  ecrf_form_diffs?: EntityDiff[];
  impact_summary: AmendmentImpactSummary;
  migration_directives?: MigrationDirective[];
}

export type SigningReason = "AUTHOR" | "REVIEW" | "APPROVAL" | "SPONSOR_APPROVAL" | "INVESTIGATOR_SIGNATURE" | "TECHNICAL_QC" | "CLINICAL_QC" | "DATA_LOCK" | "SYSTEM_SEAL" | "PROTOCOL_APPROVAL" | "REGULATORY_FORM_SIGNATURE" | "TRAINING_ACKNOWLEDGEMENT" | "SITE_VISIT_SIGN_OFF";

export interface SoACellView {
  activity_id: string;
  encounter_id: string;
  epoch_id: string;
  is_applicable: boolean;
  details?: string | null;
  arm_id?: string | null;
  derived_from_soa?: boolean;
}

export interface SoAEntityCreatedResponse {
  status?: string;
  id: string;
}

export interface SoAEntityDetail {
  id: string;
  version_index: number;
  created_by: string;
  created_at: string;
  is_retired?: boolean;
  is_deleted?: boolean;
}

export interface SoAHeaderArm {
  arm_id: string;
  arm_name: string;
  sequence: number;
}

export interface SoAHeaderEncounter {
  encounter_id: string;
  encounter_name: string;
  epoch_id: string;
  sequence: number;
  arm_id?: string | null;
}

export interface SoAHeaderEpoch {
  epoch_id: string;
  epoch_name: string;
  sequence: number;
  arm_id?: string | null;
}

export interface SoALinkResponse {
  status?: string;
  message?: string;
}

export interface SoAMatrixView {
  epochs?: SoAHeaderEpoch[];
  encounters?: SoAHeaderEncounter[];
  rows?: SoARowView[];
  arms?: SoAHeaderArm[];
}

export interface SoARowView {
  activity_id: string;
  activity_name: string;
  cells?: SoACellView[];
  derived_from_soa?: boolean;
}

export interface StudyAlignmentReport {
  study_id: string;
  complete_activities: ActivityReport[];
  incomplete_activities: ActivityReport[];
  unmapped_activities: ActivityReport[];
  unmapped_odm_items: Record<string, any>[];
  unmapped_crf_item_values: Record<string, any>[];
}

export interface StudyArmProperties {
  name: string;
  type: string;
  sequence?: number | null;
}

export interface StudyTerminologyValidationReport {
  study_id: string;
  is_valid: boolean;
  total_concepts: number;
  valid_count: number;
  invalid_count: number;
  degraded_count: number;
  concepts: ConceptValidationReport[];
}

export interface Suggestion {
  suggestion_id: string;
  block_id: string;
  study_id: string;
  suggested_text: string;
  original_text: string;
  status?: SuggestionStatus;
  created_by: string;
  created_at?: string;
  reason: string;
  decision_reason?: string | null;
  decided_by?: string | null;
  decided_at?: string | null;
  block_version_index: number;
  version_index?: number;
}

export interface SuggestionCreate {
  suggested_text: string;
  reason: string;
}

export interface SuggestionDecisionRequest {
  decision: "accept" | "reject";
  decision_reason: string;
}

export type SuggestionStatus = "pending" | "accepted" | "rejected";

export interface SynopsisExportRequest {
  study_id: string;
  format?: string;
  creator?: string | null;
  change_reason?: string | null;
}

export interface SynopsisExportResponse {
  study_id: string;
  format: string;
  content_base64: string;
  filename: string;
}

export interface SynthesizedECRFForm {
  form_id: string;
  form_name: string;
  cdash_domain: string;
  items?: Record<string, any>[];
  rules?: Record<string, any>[];
}

export interface TerminologyConcept {
  code: string;
  decode: string;
  system: string;
  valid: boolean;
}

export type TerminologyEnum = "SNOMED-CT" | "LOINC" | "MedDRA" | "WHODrug" | "NCI" | "CDISC-CT";

export interface TerminologySearchResponse {
  query: string;
  state: CodeValidationState;
  results: TerminologyConcept[];
  total_results: number;
  error_message?: string | null;
}

export interface TimingWindowProperties {
  name: string;
  anchor_reference?: string | null;
  target_day?: number | null;
  min_offset?: number | null;
  max_offset?: number | null;
  conditional?: boolean | null;
  reason?: string | null;
}

export interface TransitionItemRequest {
  status: string;
  reason: string;
  name?: string | null;
  label?: string | null;
  value?: string | null;
}

export interface USDMProtocolExtractionResponse {
  study_title: string;
  protocol_id: string;
  phase: "PHASE_I" | "PHASE_I_II" | "PHASE_II" | "PHASE_III" | "PHASE_IV";
  therapeutic_area: string;
  arms?: ExtractedArm[];
  epochs?: ExtractedEpoch[];
  visits?: ExtractedVisit[];
  activities?: ExtractedActivity[];
  criteria?: ExtractedCriterion[];
  confidence_score: number;
}

export interface UpdateArmRequest {
  reason_for_change: string;
  object_type?: string;
  payload: ArmPayload;
}

export interface UpdateBlockRequest {
  properties: Record<string, any>;
  change_reason?: string | null;
}

export interface UpdateConceptRequest {
  display_name: string;
  definition: string;
  cdash_mapping?: CDASHMapping | null;
  allowable_units?: AllowableUnit[] | null;
  reason_for_change: string;
}

export interface UpdateDataElementRequest {
  reason_for_change: string;
  object_type?: string;
  payload: DataElementPayload;
}

export interface UpdateEligibilityCriterionRequest {
  criterion_type: "inclusion" | "exclusion";
  description: string;
  dsl_source: string;
  expected_outcome?: boolean;
  change_reason: string;
}

export interface UpdateEpochRequest {
  properties: EpochProperties;
  reason_for_change?: string;
}

export interface UpdateFormRequest {
  reason_for_change: string;
  object_type?: string;
  payload: FormPayload;
}

export interface UpdateLibraryInstanceRequest {
  payload: Record<string, any>;
}

export interface UpdateProcedureRequest {
  properties: ProcedureProperties;
  reason_for_change?: string;
}

export interface UpdateStudyArmRequest {
  properties: StudyArmProperties;
  reason_for_change?: string;
}

export interface UpdateTimingWindowRequest {
  properties: TimingWindowProperties;
  reason_for_change?: string;
}

export interface ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface VersionDiffResponse {
  added_nodes: DifferenceResult[];
  modified_nodes: DifferenceResult[];
  deleted_nodes: DifferenceResult[];
}

export interface VisitAttributes {
  visit_type: string;
  planned_day: number;
  window_days: number;
}

export interface VisitLibraryObjectDetail {
  id: string;
  version: string;
  status: LibraryStatus;
  sponsor_id: string;
  tenant_id: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
  prior_status?: string | null;
  object_type?: string;
  payload: VisitPayload;
}

export interface VisitPayload {
  attributes: VisitAttributes;
}

export interface VisitProperties {
  name?: string | null;
  encounter_name?: string | null;
  sequence: number;
}

export interface VisitReorderItem {
  visit_id: string;
  sequence: number;
}

export interface VisitReorderRequest {
  visits: VisitReorderItem[];
}

export interface VisitToArmAssignmentRequest {
  arm_id: string;
  visit_ids: string[];
}

export interface VisitToEpochAssignmentRequest {
  epoch_id: string;
  visit_ids: string[];
}

export interface apps__designer__domain__protocol_authoring__soa__CreateVisitRequest {
  id: string;
  properties: VisitProperties;
  change_reason?: string;
}

export interface apps__designer__domain__protocol_authoring__soa__UpdateVisitRequest {
  properties: VisitProperties;
  reason_for_change?: string;
}

export interface apps__designer__library__CreateVisitRequest {
  id: string;
  version?: string;
  status?: LibraryStatus;
  sponsor_id: string;
  change_reason: string;
  object_type?: string;
  payload: VisitPayload;
}

export interface apps__designer__library__UpdateVisitRequest {
  reason_for_change: string;
  object_type?: string;
  payload: VisitPayload;
}

