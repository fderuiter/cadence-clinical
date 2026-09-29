// Auto-generated from the OpenAPI schema definitions in docs/openapi/legacy by src/types/generate-types.js. Do not edit.

export interface ChatMessageDTO {
  role?: MessageRole | string;
  content?: string | null;
  tool_calls?: ToolCallDTO[] | null;
  tool_call_id?: string | null;
  name?: string | null;
}

export interface ClinicalReasoningRequest {
  query: string;
  tenant_id: string;
  study_id?: string | null;
  protocol_chunks?: ProtocolRAGContextChunk[];
  patient_records?: Record<string, any>[] | Record<string, any> | string | null;
  execution_state?: ExecutionStateContext | null;
  min_citation_confidence?: number;
  compliance_profile?: string;
  esignature_context?: Record<string, any> | null;
}

export interface ClinicalReasoningResponse {
  status: string;
  draft_record?: DraftClinicalRecord | null;
  reasoning_chain?: ReasoningStepState[];
  citations?: ParsedCitation[];
  overall_faithfulness_score: number;
  is_citation_verified: boolean;
  phi_masking_applied: boolean;
  phi_tokens_masked: number;
  execution_state: ExecutionStateContext;
  model_used?: string;
  latency_ms?: number;
}

export interface DraftClinicalRecord {
  record_id: string;
  title: string;
  study_id?: string | null;
  status?: string;
  synthesized_guidelines: string;
  eligibility_criteria?: string[];
  citations?: ParsedCitation[];
  requires_esignature?: boolean;
  created_by_agent?: string;
  created_at: string;
}

export interface EmbeddingRequestDTO {
  input_texts: string[];
  tier?: ModelTier;
  model_override?: string | null;
  tenant_id?: string | null;
  custom_terms?: string[] | null;
  enable_deid?: boolean;
  compliance_profile?: string;
}

export interface EmbeddingResponseDTO {
  embeddings: number[][];
  model: string;
  tier: ModelTier;
  usage?: TokenUsageDTO;
  latency_ms?: number;
  deid_applied?: boolean;
  deid_tokens_count?: number;
}

export interface ExecutionStateContext {
  session_id: string;
  current_step_index?: number;
  total_steps?: number;
  completed_steps?: ReasoningStepState[];
  patient_context?: Record<string, any>[] | Record<string, any> | string | null;
  protocol_chunks?: ProtocolRAGContextChunk[];
  metadata?: Record<string, any>;
}

export interface FunctionDefinitionDTO {
  name: string;
  description?: string | null;
  parameters?: Record<string, any> | null;
}

export interface GenerationRequestDTO {
  prompt?: string | null;
  messages?: ChatMessageDTO[] | null;
  tools?: ToolDefinitionDTO[] | null;
  tool_choice?: string | Record<string, any> | null;
  tool_responses?: ToolResponseDTO[] | null;
  tier?: ModelTier;
  model_override?: string | null;
  temperature?: number;
  max_tokens?: number | null;
  response_schema?: Record<string, any> | null;
  tenant_id?: string | null;
  study_id?: string | null;
  custom_terms?: string[] | null;
  enable_deid?: boolean;
  compliance_profile?: string;
}

export interface GenerationResponseDTO {
  content?: string | null;
  structured_data?: Record<string, any> | null;
  tool_calls?: ToolCallDTO[] | null;
  model: string;
  tier: ModelTier;
  usage?: TokenUsageDTO;
  latency_ms?: number;
  deid_applied?: boolean;
  deid_tokens_count?: number;
}

export interface HTTPValidationError {
  detail?: ValidationError[];
}

export type MessageRole = "system" | "user" | "assistant" | "tool";

export type ModelTier = "tier_1_local" | "tier_2_fast" | "tier_3_frontier";

export interface ParsedCitation {
  protocol_version: string;
  section_number?: string | null;
  section_title?: string | null;
  page_number: number;
  citation_marker: string;
  is_approved: boolean;
  is_verified: boolean;
}

export interface ProtocolRAGContextChunk {
  chunk_id: string;
  protocol_version: string;
  section_number?: string | null;
  section_title?: string | null;
  page_number: number;
  chunk_text: string;
  is_approved?: boolean;
}

export interface ProtocolRAGRequest {
  query: string;
  study_id: string;
  protocol_version?: string | null;
  context_chunks?: ProtocolRAGContextChunk[];
  top_k?: number;
  tenant_id?: string;
}

export interface ProtocolRAGResponse {
  answer: string;
  citations?: ParsedCitation[];
  faithfulness_score: number;
  is_grounded: boolean;
  confidence_tier: string;
  model?: string;
  latency_ms?: number;
}

export interface ReasoningStepState {
  step_index: number;
  step_type: ReasoningStepType | string;
  title: string;
  input_summary: string;
  output_text: string;
  status?: string;
  has_phi_masked?: boolean;
  masked_tokens_count?: number;
  step_citations?: ParsedCitation[];
}

export type ReasoningStepType =
  "planning" | "extraction" | "synthesis" | "verification" | "draft_staging";

export interface TierInfoDTO {
  tier: ModelTier;
  default_model: string;
  description: string;
  capabilities?: string[];
}

export interface TokenUsageDTO {
  prompt_tokens?: number;
  completion_tokens?: number;
  total_tokens?: number;
}

export interface ToolCallDTO {
  id: string;
  type?: string;
  function: ToolCallFunctionDTO;
}

export interface ToolCallFunctionDTO {
  name: string;
  arguments: string | Record<string, any>;
}

export interface ToolDefinitionDTO {
  type?: string;
  function: FunctionDefinitionDTO;
}

export interface ToolResponseDTO {
  tool_call_id: string;
  content: string | Record<string, any>;
  name?: string | null;
}

export interface ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Ai_gateway_ChatMessageDTO {
  role?: Ai_gateway_MessageRole | string;
  content?: string | null;
  tool_calls?: Ai_gateway_ToolCallDTO[] | null;
  tool_call_id?: string | null;
  name?: string | null;
}

export interface Ai_gateway_ClinicalReasoningRequest {
  query: string;
  tenant_id: string;
  study_id?: string | null;
  protocol_chunks?: Ai_gateway_ProtocolRAGContextChunk[];
  patient_records?: Record<string, any>[] | Record<string, any> | string | null;
  execution_state?: Ai_gateway_ExecutionStateContext | null;
  min_citation_confidence?: number;
  compliance_profile?: string;
  esignature_context?: Record<string, any> | null;
}

export interface Ai_gateway_ClinicalReasoningResponse {
  status: string;
  draft_record?: Ai_gateway_DraftClinicalRecord | null;
  reasoning_chain?: Ai_gateway_ReasoningStepState[];
  citations?: Ai_gateway_ParsedCitation[];
  overall_faithfulness_score: number;
  is_citation_verified: boolean;
  phi_masking_applied: boolean;
  phi_tokens_masked: number;
  execution_state: Ai_gateway_ExecutionStateContext;
  model_used?: string;
  latency_ms?: number;
}

export interface Ai_gateway_DraftClinicalRecord {
  record_id: string;
  title: string;
  study_id?: string | null;
  status?: string;
  synthesized_guidelines: string;
  eligibility_criteria?: string[];
  citations?: Ai_gateway_ParsedCitation[];
  requires_esignature?: boolean;
  created_by_agent?: string;
  created_at: string;
}

export interface Ai_gateway_EmbeddingRequestDTO {
  input_texts: string[];
  tier?: Ai_gateway_ModelTier;
  model_override?: string | null;
  tenant_id?: string | null;
  custom_terms?: string[] | null;
  enable_deid?: boolean;
  compliance_profile?: string;
}

export interface Ai_gateway_EmbeddingResponseDTO {
  embeddings: number[][];
  model: string;
  tier: Ai_gateway_ModelTier;
  usage?: Ai_gateway_TokenUsageDTO;
  latency_ms?: number;
  deid_applied?: boolean;
  deid_tokens_count?: number;
}

export interface Ai_gateway_ExecutionStateContext {
  session_id: string;
  current_step_index?: number;
  total_steps?: number;
  completed_steps?: Ai_gateway_ReasoningStepState[];
  patient_context?: Record<string, any>[] | Record<string, any> | string | null;
  protocol_chunks?: Ai_gateway_ProtocolRAGContextChunk[];
  metadata?: Record<string, any>;
}

export interface Ai_gateway_FunctionDefinitionDTO {
  name: string;
  description?: string | null;
  parameters?: Record<string, any> | null;
}

export interface Ai_gateway_GenerationRequestDTO {
  prompt?: string | null;
  messages?: Ai_gateway_ChatMessageDTO[] | null;
  tools?: Ai_gateway_ToolDefinitionDTO[] | null;
  tool_choice?: string | Record<string, any> | null;
  tool_responses?: Ai_gateway_ToolResponseDTO[] | null;
  tier?: Ai_gateway_ModelTier;
  model_override?: string | null;
  temperature?: number;
  max_tokens?: number | null;
  response_schema?: Record<string, any> | null;
  tenant_id?: string | null;
  study_id?: string | null;
  custom_terms?: string[] | null;
  enable_deid?: boolean;
  compliance_profile?: string;
}

export interface Ai_gateway_GenerationResponseDTO {
  content?: string | null;
  structured_data?: Record<string, any> | null;
  tool_calls?: Ai_gateway_ToolCallDTO[] | null;
  model: string;
  tier: Ai_gateway_ModelTier;
  usage?: Ai_gateway_TokenUsageDTO;
  latency_ms?: number;
  deid_applied?: boolean;
  deid_tokens_count?: number;
}

export interface Ai_gateway_HTTPValidationError {
  detail?: Ai_gateway_ValidationError[];
}

export type Ai_gateway_MessageRole = "system" | "user" | "assistant" | "tool";

export type Ai_gateway_ModelTier =
  "tier_1_local" | "tier_2_fast" | "tier_3_frontier";

export interface Ai_gateway_ParsedCitation {
  protocol_version: string;
  section_number?: string | null;
  section_title?: string | null;
  page_number: number;
  citation_marker: string;
  is_approved: boolean;
  is_verified: boolean;
}

export interface Ai_gateway_ProtocolRAGContextChunk {
  chunk_id: string;
  protocol_version: string;
  section_number?: string | null;
  section_title?: string | null;
  page_number: number;
  chunk_text: string;
  is_approved?: boolean;
}

export interface Ai_gateway_ProtocolRAGRequest {
  query: string;
  study_id: string;
  protocol_version?: string | null;
  context_chunks?: Ai_gateway_ProtocolRAGContextChunk[];
  top_k?: number;
  tenant_id?: string;
}

export interface Ai_gateway_ProtocolRAGResponse {
  answer: string;
  citations?: Ai_gateway_ParsedCitation[];
  faithfulness_score: number;
  is_grounded: boolean;
  confidence_tier: string;
  model?: string;
  latency_ms?: number;
}

export interface Ai_gateway_ReasoningStepState {
  step_index: number;
  step_type: Ai_gateway_ReasoningStepType | string;
  title: string;
  input_summary: string;
  output_text: string;
  status?: string;
  has_phi_masked?: boolean;
  masked_tokens_count?: number;
  step_citations?: Ai_gateway_ParsedCitation[];
}

export type Ai_gateway_ReasoningStepType =
  "planning" | "extraction" | "synthesis" | "verification" | "draft_staging";

export interface Ai_gateway_TierInfoDTO {
  tier: Ai_gateway_ModelTier;
  default_model: string;
  description: string;
  capabilities?: string[];
}

export interface Ai_gateway_TokenUsageDTO {
  prompt_tokens?: number;
  completion_tokens?: number;
  total_tokens?: number;
}

export interface Ai_gateway_ToolCallDTO {
  id: string;
  type?: string;
  function: Ai_gateway_ToolCallFunctionDTO;
}

export interface Ai_gateway_ToolCallFunctionDTO {
  name: string;
  arguments: string | Record<string, any>;
}

export interface Ai_gateway_ToolDefinitionDTO {
  type?: string;
  function: Ai_gateway_FunctionDefinitionDTO;
}

export interface Ai_gateway_ToolResponseDTO {
  tool_call_id: string;
  content: string | Record<string, any>;
  name?: string | null;
}

export interface Ai_gateway_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Ctms_ApplyTemplateRequest {
  template_id: string;
}

export interface Ctms_BudgetAdjustmentCreate {
  grant_id: string;
  site_id: string;
  study_id: string;
  adjustment_type: string;
  description: string;
  amount: number;
  currency?: string;
}

export interface Ctms_BudgetAdjustmentResponse {
  id: string;
  grant_id: string;
  site_id: string;
  study_id: string;
  adjustment_type: string;
  description: string;
  amount: number;
  currency: string;
  status: string;
  approved_by: string | null;
  approved_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_BudgetLineItemCreate {
  category: string;
  description: string;
  amount: number;
}

export interface Ctms_BudgetLineItemResponse {
  id: string;
  grant_id: string;
  category: string;
  description: string;
  amount: number;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_CRAAllocationCreate {
  cra_id: string;
  site_id: string;
  study_id: string;
  status?: string | null;
  effective_start_date?: string | null;
  effective_end_date?: string | null;
}

export interface Ctms_CRAAllocationResponse {
  id: string;
  cra_id: string;
  site_id: string;
  study_id: string;
  status: string;
  effective_start_date: string;
  effective_end_date: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_CRAAllocationUpdate {
  cra_id?: string | null;
  status?: string | null;
  effective_start_date?: string | null;
  effective_end_date?: string | null;
}

export interface Ctms_CRAWorkloadItem {
  cra_id: string;
  active_allocations_count: number;
  allocated_sites: string[];
  allocated_studies: string[];
}

export interface Ctms_CTMSAuditLogResponse {
  id: string;
  timestamp: string;
  user_id: string | null;
  user_role: string | null;
  action: string;
  details: string;
}

export interface Ctms_CTMSResupplyApprovalRequest {
  change_justification: string;
}

export interface Ctms_CTMSResupplyEventResponse {
  id: string;
  study_id: string;
  site_id: string;
  kit_id: string;
  requested_qty: number;
  status: string;
  triggered_at: string;
}

export interface Ctms_CTMSStudyCreate {
  study_id: string;
  name: string;
  status?: string | null;
}

export interface Ctms_CTMSStudyResponse {
  id: string;
  study_id: string;
  name: string;
  status: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type Ctms_ConflictStrategy = "CLIENT_WINS" | "SERVER_WINS" | "MERGE";

export interface Ctms_ContractAmendmentCreate {
  summary_of_changes: string;
  revised_budget: number;
  updated_procedures?: Record<string, any>[] | null;
  signature_manifestation?: Record<string, any> | null;
}

export interface Ctms_ContractAmendmentResponse {
  id: string;
  grant_id: string;
  amendment_number: number;
  effective_date: string;
  previous_budget: number;
  revised_budget: number;
  previous_version_index: number;
  new_version_index: number;
  summary_of_changes: string;
  status: string;
  approved_by: string | null;
  approved_at: string | null;
  signature_manifestation: Record<string, any> | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_CountryMilestoneCreate {
  study_id: string;
  country_code: string;
  milestone_type: string;
  status?: string;
  planned_date?: string | null;
  actual_date?: string | null;
  approval_number?: string | null;
  authority_name?: string | null;
}

export interface Ctms_CountryMilestoneResponse {
  id: string;
  study_id: string;
  country_code: string;
  milestone_type: string;
  status: string;
  planned_date: string | null;
  actual_date: string | null;
  approval_number: string | null;
  authority_name: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_CountryPrerequisiteRuleCreate {
  study_id: string;
  country_code: string;
  facet: string;
  criterion_code: string;
  criterion_name: string;
  is_mandatory?: boolean;
  description?: string | null;
}

export interface Ctms_CountryPrerequisiteRuleResponse {
  id: string;
  study_id: string;
  country_code: string;
  facet: string;
  criterion_code: string;
  criterion_name: string;
  is_mandatory: boolean;
  description: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_CurrencyConvertRequest {
  amount: number;
  from_currency?: string;
  to_currency: string;
}

export interface Ctms_CurrencyConvertResponse {
  original_amount: number;
  from_currency: string;
  to_currency: string;
  exchange_rate: number;
  converted_amount: number;
}

export interface Ctms_CurrencyExchangeRateCreate {
  base_currency?: string;
  target_currency: string;
  rate: number;
  expiry_date?: string | null;
}

export interface Ctms_CurrencyExchangeRateResponse {
  id: string;
  base_currency: string;
  target_currency: string;
  rate: number;
  effective_date: string;
  expiry_date: string | null;
  is_active: boolean;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_DOALogResponse {
  site_id: string;
  pi_name: string;
  delegated_staff: Record<string, any>[];
  audit_history: Record<string, any>[];
}

export interface Ctms_DOASignOffRequest {
  record_id: string;
  reason_for_change: string;
}

export interface Ctms_DelegationTaskRequest {
  site_id: string;
  staff_user_id: string;
  task_codes: string[];
  start_date: string;
  reason_for_change: string;
}

export interface Ctms_DeviationActionItemComplete {
  resolution_notes: string;
}

export interface Ctms_DeviationActionItemCreate {
  deviation_id: string;
  site_id: string;
  description: string;
  assignee_user_id: string;
  assignee_role: string;
  due_date: string;
}

export interface Ctms_DeviationActionItemResponse {
  id: string;
  deviation_id: string;
  site_id: string;
  description: string;
  assignee_user_id: string;
  assignee_role: string;
  due_date: string;
  status: string;
  resolution_notes: string | null;
  completed_by: string | null;
  completed_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_ETMFSyncRecordResponse {
  id: string;
  study_id: string;
  site_id: string;
  artifact_type: string;
  source_record_id: string;
  etmf_document_id: string;
  dia_zone: string;
  dia_section: string;
  dia_artifact: string;
  sync_status: string;
  error_message: string | null;
  synced_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_ETMFSyncRequest {
  study_id: string;
  site_id: string;
  artifact_type: string;
  source_record_id: string;
  title: string;
  content_text: string;
  dia_zone?: string;
  dia_section?: string;
  dia_artifact?: string;
}

export interface Ctms_EssentialDocumentCreate {
  study_id: string;
  site_id: string;
  document_type: string;
  file_name: string;
  file_hash: string;
  expiration_date?: string | null;
}

export interface Ctms_EssentialDocumentResponse {
  id: string;
  study_id: string;
  site_id: string;
  document_type: string;
  file_name: string;
  file_hash: string;
  status: string;
  expiration_date: string | null;
  review_notes: string | null;
  reviewed_by: string | null;
  reviewed_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_EssentialDocumentReview {
  status: string;
  review_notes?: string | null;
}

export interface Ctms_FinancialInvoiceCreate {
  study_id: string;
  site_id: string;
  grant_id: string;
  invoice_type?: string;
  gross_amount: number;
  withholding_amount?: number;
  currency?: string;
  payable_ids?: string[];
}

export interface Ctms_FinancialInvoiceResponse {
  id: string;
  study_id: string;
  site_id: string;
  grant_id: string;
  invoice_number: string;
  invoice_type: string;
  gross_amount: number;
  withholding_amount: number;
  net_amount: number;
  currency: string;
  status: string;
  payable_ids: string[];
  approved_by: string | null;
  approved_at: string | null;
  disbursed_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_FindingCreate {
  text: string;
  severity: string;
  resolution_status?: string | null;
}

export interface Ctms_GeneratedLetterResponse {
  id: string;
  visit_id: string;
  letter_type: string;
  rendered_content: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_GreenlightTokenVerifyRequest {
  token: string;
  site_id?: string | null;
  study_id?: string | null;
}

export interface Ctms_GreenlightTokenVerifyResponse {
  is_valid: boolean;
  payload?: Record<string, any> | null;
  error?: string | null;
}

export interface Ctms_HTTPValidationError {
  detail?: Ctms_ValidationError[];
}

export interface Ctms_IPDestructionCertificateCreate {
  study_id: string;
  site_id: string;
  kit_ids: string[];
  destruction_method?: string;
  witness_user_id: string;
  witness_role: string;
  pi_signature_hash: string;
  reason_for_destruction: string;
}

export interface Ctms_IPDestructionCertificateResponse {
  id: string;
  study_id: string;
  site_id: string;
  certificate_number: string;
  kit_ids: string[];
  destruction_method: string;
  destruction_date: string;
  witness_user_id: string;
  witness_role: string;
  pi_signature_hash: string;
  pi_signed_at: string;
  reason_for_destruction: string;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_IPKitDispenseRequest {
  subject_id: string;
  visit_id: string;
}

export interface Ctms_IPKitReconcileRequest {
  returned_units_count: number;
  expected_units_count: number;
  notes?: string | null;
}

export interface Ctms_IPKitRecordResponse {
  id: string;
  study_id: string;
  site_id: string;
  kit_number: string;
  lot_number: string;
  kit_type: string;
  shipment_tracking_number: string;
  expiration_date: string;
  status: string;
  received_date: string | null;
  dispensed_subject_id: string | null;
  dispensed_visit_id: string | null;
  dispensed_date: string | null;
  returned_units_count: number | null;
  expected_units_count: number | null;
  compliance_percentage: number | null;
  notes: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_IPShipmentReceiveRequest {
  study_id: string;
  site_id: string;
  kit_numbers: string[];
  lot_number: string;
  kit_type?: string;
  shipment_tracking_number: string;
  expiration_date: string;
}

export interface Ctms_IPTemperatureExcursionCreate {
  study_id: string;
  site_id: string;
  kit_ids: string[];
  excursion_type?: string;
  min_temp_celsius: number;
  max_temp_celsius: number;
  duration_hours: number;
  occurred_at: string;
}

export interface Ctms_IPTemperatureExcursionDisposition {
  disposition_status: string;
  qa_rationale: string;
}

export interface Ctms_IPTemperatureExcursionResponse {
  id: string;
  study_id: string;
  site_id: string;
  kit_ids: string[];
  excursion_type: string;
  min_temp_celsius: number;
  max_temp_celsius: number;
  duration_hours: number;
  occurred_at: string;
  disposition_status: string;
  qa_reviewed_by: string | null;
  qa_reviewed_at: string | null;
  qa_rationale: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_InvestigatorGrantCreate {
  study_id: string;
  site_id: string;
  total_budget?: number;
  currency?: string | null;
}

export interface Ctms_InvestigatorGrantResponse {
  id: string;
  study_id: string;
  site_id: string;
  total_budget: number;
  currency: string;
  status: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_InvestigatorGrantUpdate {
  total_budget?: number | null;
  currency?: string | null;
  status?: string | null;
}

export interface Ctms_InvestigatorPayableResponse {
  id: string;
  grant_id: string;
  milestone_id: string | null;
  amount: number;
  payment_status: string;
  due_date: string | null;
  paid_at: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_MonitoringVisitComplete {
  actual_date: string;
  findings?: Ctms_FindingCreate[];
}

export interface Ctms_MonitoringVisitCreate {
  study_id: string;
  site_id: string;
  cra_id: string;
  visit_type: string;
  scheduled_date: string;
}

export interface Ctms_MonitoringVisitOfflineSync {
  visit_id: string;
  study_id?: string | null;
  site_id?: string | null;
  actual_date: string;
  device_timestamp: string;
  findings?: Ctms_FindingCreate[];
  offline_sync_markers: Ctms_OfflineSyncMarkers;
}

export interface Ctms_MonitoringVisitResponse {
  id: string;
  study_id: string;
  site_id: string;
  cra_id: string;
  visit_type: string;
  scheduled_date: string;
  actual_date: string | null;
  status: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_OfflineSyncMarkers {
  client_id: string;
  sequence_number: number;
  conflict_strategy?: Ctms_ConflictStrategy;
  timestamps?: Record<string, any> | null;
  signature?: string | null;
}

export interface Ctms_PaymentMilestoneCreate {
  milestone_name: string;
  trigger_condition: string;
  amount: number;
}

export interface Ctms_PaymentMilestoneResponse {
  id: string;
  grant_id: string;
  milestone_name: string;
  trigger_condition: string;
  amount: number;
  is_triggered: boolean;
  triggered_at: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_ProcedurePaymentGridCreate {
  grant_id: string;
  visit_name: string;
  procedure_code: string;
  procedure_name: string;
  base_amount: number;
  overhead_percentage?: number;
  withholding_percentage?: number;
  activity_id?: string | null;
  form_id?: string | null;
  patient_stipend?: number;
  travel_reimbursement?: number;
}

export interface Ctms_ProcedurePaymentGridResponse {
  id: string;
  grant_id: string;
  visit_name: string;
  procedure_code: string;
  procedure_name: string;
  base_amount: number;
  overhead_percentage: number;
  withholding_percentage: number;
  activity_id?: string | null;
  form_id?: string | null;
  patient_stipend?: number;
  travel_reimbursement?: number;
  is_active: boolean;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_ProtocolDeviationCreate {
  study_id: string;
  site_id: string;
  subject_id?: string | null;
  visit_id?: string | null;
  deviation_category: string;
  severity: string;
  title: string;
  description: string;
  date_occurred: string;
}

export interface Ctms_ProtocolDeviationRCA {
  root_cause_5whys?: string[];
  root_cause_summary: string;
  corrective_action_plan: string;
  preventive_action_plan: string;
}

export interface Ctms_ProtocolDeviationResponse {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string | null;
  visit_id: string | null;
  deviation_category: string;
  severity: string;
  title: string;
  description: string;
  date_occurred: string;
  date_identified: string;
  status: string;
  root_cause_5whys: string[];
  root_cause_summary: string | null;
  corrective_action_plan: string | null;
  preventive_action_plan: string | null;
  quality_capa_id: string | null;
  reported_by: string;
  resolved_by: string | null;
  resolved_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_RBQMKRIMetricCreate {
  study_id: string;
  site_id: string;
  metric_type: string;
  metric_value: number;
  threshold_low: number;
  threshold_high: number;
  notes?: string | null;
}

export interface Ctms_RBQMKRIMetricResponse {
  id: string;
  study_id: string;
  site_id: string;
  metric_type: string;
  metric_value: number;
  threshold_low: number;
  threshold_high: number;
  breach_status: string;
  calculation_date: string;
  notes: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_RecruitmentRecordCreate {
  site_id: string;
  study_id: string;
  screened_count?: number;
  enrolled_count?: number;
  target_count?: number;
  as_of_date?: string | null;
}

export interface Ctms_RecruitmentRecordResponse {
  id: string;
  site_id: string;
  study_id: string;
  screened_count: number;
  enrolled_count: number;
  target_count: number;
  as_of_date: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_RevokeDelegationRequest {
  record_id: string;
  reason_for_change: string;
}

export interface Ctms_SiteBudgetTemplateCreate {
  template_name: string;
  study_id?: string | null;
  tier_level?: string;
  currency?: string;
  overhead_percentage?: number;
  withholding_percentage?: number;
  fixed_startup_fee?: number;
  fixed_closeout_fee?: number;
  irb_fee?: number;
  patient_stipend_amount?: number;
  travel_reimbursement_rate?: number;
  fixed_fees?: Record<string, any>[];
  procedure_items?: Record<string, any>[];
}

export interface Ctms_SiteBudgetTemplateResponse {
  id: string;
  template_name: string;
  study_id: string | null;
  tier_level: string;
  currency: string;
  overhead_percentage: number;
  withholding_percentage: number;
  fixed_startup_fee: number;
  fixed_closeout_fee: number;
  irb_fee: number;
  patient_stipend_amount: number;
  travel_reimbursement_rate: number;
  fixed_fees: Record<string, any>[];
  procedure_items: Record<string, any>[];
  is_active: boolean;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_SiteGreenlightGateResponse {
  id: string | null;
  study_id: string;
  site_id: string;
  country_code?: string;
  overall_status: string;
  facet_regulatory_irb?: boolean;
  facet_cta_budget?: boolean;
  facet_eisf_baseline?: boolean;
  facet_doa_log?: boolean;
  facet_staff_training?: boolean;
  facet_ip_release?: boolean;
  contract_approved?: boolean;
  irb_approved?: boolean;
  form_1572_approved?: boolean;
  doa_signed_off?: boolean;
  ip_ready?: boolean;
  greenlight_token?: string | null;
  greenlight_certified_by?: string | null;
  greenlight_certified_at?: string | null;
  is_overridden?: boolean;
  override_sponsor_user_id?: string | null;
  override_medical_user_id?: string | null;
  override_justification?: string | null;
  override_authorized_at?: string | null;
  evaluation_details?: Record<string, any> | null;
  rejection_reason?: string | null;
  created_at?: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_SiteGreenlightOverrideRequest {
  study_id: string;
  sponsor_user_id: string;
  medical_user_id: string;
  sponsor_signature: string;
  medical_signature: string;
  justification: string;
  country_code?: string;
}

export interface Ctms_SiteMilestoneCreate {
  site_id: string;
  study_id: string;
  milestone_type: string;
  planned_date?: string | null;
  actual_date?: string | null;
  status?: string | null;
}

export interface Ctms_SiteMilestoneResponse {
  id: string;
  site_id: string;
  study_id: string;
  milestone_type: string;
  planned_date: string | null;
  actual_date: string | null;
  status: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_SiteMilestoneUpdate {
  planned_date?: string | null;
  actual_date?: string | null;
  status?: string | null;
}

export interface Ctms_SiteRiskScoreResponse {
  id: string | null;
  study_id: string;
  site_id: string;
  composite_score: number;
  risk_level: string;
  assessment_date: string;
  recommended_monitoring_type: string;
  monitoring_interval_days: number;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Ctms_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Ctms_VisitPayableCalculationResponse {
  gross_amount: number;
  withholding_amount: number;
  net_amount: number;
  visit_name?: string | null;
  procedures_count?: number;
  base_amount?: number;
  overhead_amount?: number;
  patient_stipend_total?: number;
  travel_reimbursement_total?: number;
  currency?: string;
  target_currency?: string | null;
  exchange_rate?: number;
  converted_net_amount?: number | null;
  version_index?: number | null;
}

export interface Designer_ActivityAssignmentRequest {
  visit_id: string;
  procedure_ids?: string[];
  activity_ids?: string[];
}

export interface Designer_ActivityReport {
  epoch_id: string | null;
  epoch_internal_id: number;
  scheduled_event_id: string | null;
  scheduled_event_internal_id: number;
  activity_def_id: string | null;
  activity_def_internal_id: number;
  status: string;
  unmapped_items: Designer_ItemMappingStatus[];
  mapped_items: Designer_ItemMappingStatus[];
}

export interface Designer_AllowableUnit {
  ucum_code: string;
  name: string;
}

export interface Designer_AmendmentImpactReport {
  base_version?: string | null;
  amended_version?: string | null;
  added_forms_count?: number;
  modified_forms_count?: number;
  deleted_forms_count?: number;
  estimated_cost_usd?: number;
  burden_change?: number;
  explanation: string;
}

export interface Designer_AmendmentImpactSummary {
  base_version: string;
  amended_version: string;
  burden_delta?: number;
  affected_visits_count?: number;
  affected_visits?: string[];
  affected_activities_count?: number;
  affected_activities?: string[];
  schema_revisions?: Designer_SchemaRevisionSummary;
  is_substantial?: boolean;
  requires_reconsent?: boolean;
  estimated_cost_usd?: number;
  narrative_summary?: string;
}

export interface Designer_ApproveProtocolRequest {
  signing_reason: Designer_SigningReason;
}

export interface Designer_ArmAttributes {
  arm_type: string;
  target_sample_size: number;
  randomization_ratio: string;
}

export interface Designer_ArmLibraryObjectDetail {
  id: string;
  version: string;
  status: Designer_LibraryStatus;
  sponsor_id: string;
  tenant_id: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
  prior_status?: string | null;
  object_type?: string;
  payload: Designer_ArmPayload;
}

export interface Designer_ArmPayload {
  attributes: Designer_ArmAttributes;
}

export interface Designer_ArmReorderItem {
  arm_id: string;
  sequence: number;
}

export interface Designer_ArmReorderRequest {
  arms: Designer_ArmReorderItem[];
}

export interface Designer_AstEditCheck {
  id: string;
  name: string;
  ast_expression: Record<string, any>;
  severity?: string;
  query_message: string;
  target_variable_ids?: string[];
}

export interface Designer_AttritionStep {
  criterion_id: string;
  type: string;
  description: string;
  passed_count: number;
  failed_count: number;
  remaining_count: number;
  attrition_rate: number;
}

export interface Designer_BlockCreatedResponse {
  status?: string;
  id: string;
}

export interface Designer_BlockDetailResponse {
  id: string;
  block_id: string;
  block_type: string;
  order: number;
  version_index: number;
  created_by: string;
  created_at: string;
}

export interface Designer_Body_extract_protocol_digitization_api_v1_designer_digitization_extract_post {
  file: string;
}

export interface Designer_Body_start_digitization_dag_job_api_v1_designer_digitization_dag_jobs_post {
  file: string;
  study_id?: string | null;
}

export interface Designer_Body_upload_mapping_csv_api_v1_mappings_upload_post {
  file: string;
}

export interface Designer_Body_upload_protocol_ingestion_api_v1_designer_ingestion_upload_post {
  file: string;
}

export interface Designer_BranchAmendmentRequest {
  study_id: string;
  base_version_tag?: string;
  amendment_type?: string;
  requires_reconsent?: boolean;
  change_reason: string;
  branch_name?: string | null;
}

export interface Designer_BranchAmendmentResponse {
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

export interface Designer_BurdenTraceItem {
  component: string;
  count: number;
  weight: number;
  subtotal: number;
  explanation: string;
}

export interface Designer_BurdenTraceReport {
  visit_burden: number;
  procedure_burden: number;
  activity_burden: number;
  total_burden: number;
  trace?: Designer_BurdenTraceItem[];
}

export interface Designer_CDASHMapping {
  domain: string;
  variable_name: string;
  data_type: string;
}

export interface Designer_CascadeSummaryReport {
  study_id: string;
  amendment_version?: number;
  forms_created: number;
  visits_created: number;
  rules_synced: number;
  forms?: Designer_CascadedFormTemplate[];
}

export interface Designer_CascadedFormTemplate {
  form_id: string;
  form_name: string;
  domain: string;
  fields?: Record<string, any>[];
  auto_generated?: boolean;
}

export interface Designer_CdiscMetadata {
  domain?: string;
  variable_name?: string;
  oid: string;
  codelist_oid?: string | null;
  ucum_unit?: string | null;
  core_designation?: string;
  sdtm_target?: string | null;
}

export type Designer_ClinicalDataType =
  | "string"
  | "integer"
  | "float"
  | "date"
  | "datetime"
  | "time"
  | "boolean"
  | "codelist"
  | "text"
  | "repeat_log";

export type Designer_CodeValidationState = "VALID" | "INVALID" | "DEGRADED";

export interface Designer_CohortFeasibilityJob {
  job_id: string;
  study_id: string;
  cohort_id: string | null;
  status: "PENDING" | "RUNNING" | "COMPLETED" | "FAILED" | "DUPLICATE_LOCKED";
  created_at: string;
  report?: Designer_CohortFeasibilityReport | null;
}

export interface Designer_CohortFeasibilityReport {
  job_id: string;
  study_id: string;
  cohort_id?: string | null;
  status: "PENDING" | "RUNNING" | "COMPLETED" | "FAILED" | "DUPLICATE_LOCKED";
  starting_cohort_size: number;
  eligible_count: number;
  ineligible_count: number;
  indeterminate_count: number;
  overall_eligibility_rate: number;
  estimated_fit_percentage: number;
  sub_cohort_recommendations?: Designer_SubCohortRecommendation[];
  node_explanations?: Designer_NodeFitExplanation[];
  synthetic_simulation_summary?: Designer_SyntheticSimulationSummary | null;
  deid_applied?: boolean;
  deid_tokens_count?: number;
  advisory_lock_acquired?: boolean;
  execution_time_seconds?: number;
  error_message?: string | null;
}

export interface Designer_CohortFeasibilityRequest {
  analysis_id?: string | null;
  study_id: string;
  cohort_id?: string | null;
  unstructured_protocol_text?:
    string | Designer_ProtocolRAGContextChunk[] | null;
  structured_criteria?: Designer_EligibilityCriterion_Input[];
  patient_profiles?: Record<string, any>[] | null;
  synthetic_simulation_count?: number;
  missing_data_scenarios?: string[] | null;
  is_async?: boolean;
  compliance_profile?: string;
}

export interface Designer_Comment {
  comment_id: string;
  thread_id: string;
  text: string;
  created_by: string;
  created_at?: string;
  updated_at?: string | null;
  version_index?: number;
}

export interface Designer_CommentCreate {
  text: string;
}

export interface Designer_CommentCreatePayload {
  field_id: string;
  comment_text: string;
}

export interface Designer_CommentThread {
  thread_id: string;
  block_id: string;
  section_id: string;
  study_id: string;
  status?: string;
  created_by: string;
  created_at?: string;
  block_version_index: number;
  comments?: Designer_Comment[];
}

export interface Designer_CommentThreadCreate {
  block_id: string;
  text: string;
}

export interface Designer_CommitUSDMRequest {
  study_id: string;
  data: Designer_USDMProtocolExtractionResponse;
  change_reason: string;
}

export interface Designer_CommitUSDMResponse {
  study_id: string;
  version_id: string;
  status: string;
  nodes_created: number;
  relationships_created: number;
  synthesized_forms?: Designer_SynthesizedECRFForm[];
  message: string;
  faithfulness_metadata?: Designer_FaithfulnessVerificationMetadata | null;
}

export type Designer_ComparisonOperator = "==" | "!=" | "<" | "<=" | ">" | ">=";

export interface Designer_CompileUSDMFromJobRequest {
  study_id: string;
  change_reason: string;
}

export interface Designer_ConceptDetail {
  id: string;
  concept_code: string;
  terminology: string;
  display_name: string;
  definition: string;
  cdash_mapping?: Designer_CDASHMapping | null;
  allowable_units?: Designer_AllowableUnit[] | null;
  version: string;
  status: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
}

export interface Designer_ConceptListResponse {
  object: string;
  data: Designer_ConceptDetail[];
  has_more: boolean;
  next_cursor?: string | null;
}

export interface Designer_ConceptReference {
  element_type: string;
  element_id: string;
  element_name: string;
  attribute: string;
}

export interface Designer_ConceptValidationReport {
  concept_code: string;
  state: Designer_CodeValidationState;
  decode?: string | null;
  system?: string | null;
  error_message?: string | null;
  references?: Designer_ConceptReference[];
}

export interface Designer_ConstraintDefinition {
  required?: boolean;
  min_value?: number | null;
  max_value?: number | null;
  min_length?: number | null;
  max_length?: number | null;
  regex_pattern?: string | null;
  allowed_values?: string[] | null;
}

export interface Designer_CreateArmRequest {
  id: string;
  version?: string;
  status?: Designer_LibraryStatus;
  sponsor_id: string;
  change_reason: string;
  object_type?: string;
  payload: Designer_ArmPayload;
}

export interface Designer_CreateBlockRequest {
  id: string;
  block_type: string;
  order: number;
  properties: Record<string, any>;
  change_reason?: string | null;
}

export interface Designer_CreateConceptRequest {
  concept_code: string;
  terminology: string;
  display_name: string;
  definition: string;
  cdash_mapping?: Designer_CDASHMapping | null;
  allowable_units?: Designer_AllowableUnit[] | null;
  change_reason: string;
}

export interface Designer_CreateDataElementRequest {
  id: string;
  version?: string;
  status?: Designer_LibraryStatus;
  sponsor_id: string;
  change_reason: string;
  object_type?: string;
  payload: Designer_DataElementPayload;
}

export interface Designer_CreateEligibilityCriterionRequest {
  criterion_id: string;
  criterion_type: "inclusion" | "exclusion";
  description: string;
  dsl_source: string;
  expected_outcome?: boolean;
  change_reason: string;
}

export interface Designer_CreateEpochRequest {
  id: string;
  properties: Designer_EpochProperties;
  change_reason?: string;
}

export interface Designer_CreateFormRequest {
  id: string;
  version?: string;
  status?: Designer_LibraryStatus;
  sponsor_id: string;
  change_reason: string;
  object_type?: string;
  payload: Designer_FormPayload;
}

export interface Designer_CreateProcedureRequest {
  id: string;
  properties: Designer_ProcedureProperties;
  change_reason?: string;
}

export interface Designer_CreateRuleRequest {
  type: "skip_logic" | "constraint" | "cross_form_check";
  condition: Designer_packages__ddf__rules__ast__ExpressionNode;
  action?: "show" | "hide" | null;
  target_field?: string | null;
  target_form?: string | null;
  target_group?: string | null;
  query_message?: string | null;
}

export interface Designer_CreateStudyArmRequest {
  id: string;
  properties: Designer_StudyArmProperties;
  change_reason?: string;
}

export interface Designer_CreateStudyVersionRequest {
  id: string;
  version_tag: string;
  status: string;
  version_index: number;
}

export interface Designer_CreateTimingWindowRequest {
  id: string;
  properties: Designer_TimingWindowProperties;
  change_reason?: string;
}

export interface Designer_CriteriaBreakdownSummary {
  criterion_id: string;
  identifier: string;
  human_readable_text: string;
  criterion_type: "inclusion" | "exclusion";
  passed_count?: number;
  failed_count?: number;
  indeterminate_count?: number;
  pass_rate_percentage?: number;
}

export interface Designer_CrossVisitDependency {
  id: string;
  trigger_visit_id: string;
  trigger_variable_id: string;
  trigger_condition: Record<string, any>;
  target_visit_ids_to_spawn?: string[];
  target_visit_ids_to_suppress?: string[];
}

export interface Designer_DAGJobStatusResponse {
  job_id: string;
  study_id?: string | null;
  status: Designer_DigitizationJobStatus;
  current_stage?: Designer_DigitizationStage | null;
  progress_pct: number;
  created_at: string;
  updated_at: string;
  checkpoints?: Record<string, any>;
  error_message?: string | null;
  is_terminal: boolean;
  final_usdm_payload?: Designer_USDMProtocolExtractionResponse | null;
  synthesized_forms?: Designer_SynthesizedECRFForm[];
  faithfulness_metadata?: Designer_FaithfulnessVerificationMetadata | null;
}

export interface Designer_DataCaptureEcrfImpact {
  affected_forms_count?: number;
  added_forms?: string[];
  modified_forms?: string[];
  removed_forms?: string[];
  affected_visits?: string[];
  new_cdash_fields?: string[];
  rule_modifications_count?: number;
  estimated_build_hours?: number;
  action_items?: string[];
}

export interface Designer_DataElementLibraryObjectDetail {
  id: string;
  version: string;
  status: Designer_LibraryStatus;
  sponsor_id: string;
  tenant_id: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
  prior_status?: string | null;
  object_type?: string;
  payload: Designer_DataElementPayload;
}

export interface Designer_DataElementPayload {
  data_type: string;
  allowable_units: string[];
  default_unit?: string | null;
}

export interface Designer_DataSchema {
  schema_version?: string;
  form_id: string;
  name: string;
  cdisc_form_oid: string;
  variables?: Designer_VariableDefinition[];
}

export interface Designer_DerivedMathRule {
  id: string;
  target_variable_id: string;
  formula_expression: string;
  input_variable_ids?: string[];
  unit_conversion?: Record<string, any>;
}

export interface Designer_DiffRequest {
  base_form: Designer_TripartiteFormDefinition;
  amended_form: Designer_TripartiteFormDefinition;
}

export interface Designer_DifferenceResult {
  field: string;
  old_value: any;
  new_value: any;
}

export type Designer_DigitizationJobStatus =
  | "PENDING"
  | "RUNNING"
  | "COMPLETED"
  | "PENDING_REVIEW"
  | "FAILED"
  | "CANCELLED";

export type Designer_DigitizationStage =
  | "LAYOUT_PARSING"
  | "SOA_EXTRACTION"
  | "BIOMEDICAL_CONCEPT_MAPPING"
  | "ECRF_SYNTHESIS"
  | "USDM_COMPILATION";

export interface Designer_DispatchedTicketInfo {
  ticket_id: string;
  reference: string;
  domain_queue: Designer_DomainQueue;
  title: string;
  priority: string;
  status: string;
  assignee_role: string;
}

export type Designer_DomainQueue =
  | "DATA_CAPTURE_ECRF"
  | "SUBJECT_MANAGEMENT_RTSM"
  | "REGULATORY_COMPLIANCE"
  | "SITE_OPERATIONS"
  | "CLINICAL_OPERATIONS";

export interface Designer_EligibilityCriterion_Input {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id?: string;
  criterion_type: "inclusion" | "exclusion";
  identifier?: string;
  human_readable_text?: string;
  dsl_expression_string?: string;
  structured_expression_tree?: Designer_apps__designer__domain__eligibility__models__ExpressionNode_Input;
  expected_outcome?: boolean;
  criterion_id?: string;
  description?: string;
  dsl_source?: string;
  condition?: Designer_apps__designer__domain__eligibility__models__ExpressionNode_Input;
}

export interface Designer_EligibilityCriterion_Output {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id?: string;
  criterion_type: "inclusion" | "exclusion";
  identifier?: string;
  human_readable_text?: string;
  dsl_expression_string?: string;
  structured_expression_tree?: Designer_ExpressionNode_Output;
  expected_outcome?: boolean;
  criterion_id?: string;
  description?: string;
  dsl_source?: string;
  condition?: Designer_ExpressionNode_Output;
}

export interface Designer_EntityDiff {
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

export interface Designer_EpochProperties {
  name?: string | null;
  epoch_name?: string | null;
  sequence: number;
}

export interface Designer_EpochReorderItem {
  epoch_id: string;
  sequence: number;
}

export interface Designer_EpochReorderRequest {
  epochs: Designer_EpochReorderItem[];
}

export interface Designer_ExpressionNode_Output {
  type: "logical" | "comparison" | "field_ref" | "constant";
  operator?:
    Designer_ComparisonOperator | Designer_LogicalOperator | string | null;
  operands?: Designer_ExpressionNode_Output[] | null;
  value?: any | null;
  field_ref?: Designer_FieldReference_Output | null;
}

export interface Designer_ExtractedActivity {
  activity_name: string;
  cdash_domain: string;
  biomedical_concept_code?: string | null;
  assigned_visit_names?: string[];
  section_number?: string | null;
  page_number?: number | null;
  citation_marker?: string | null;
}

export interface Designer_ExtractedArm {
  name: string;
  arm_type:
    | "EXPERIMENTAL"
    | "ACTIVE_COMPARATOR"
    | "PLACEBO_COMPARATOR"
    | "SHAM_COMPARATOR"
    | "NO_INTERVENTION";
  description?: string | null;
  target_sample_size?: number | null;
}

export interface Designer_ExtractedCriterion {
  criterion_type: "INCLUSION" | "EXCLUSION";
  identifier: string;
  text_expression: string;
  logical_expression?: string | null;
}

export interface Designer_ExtractedEpoch {
  name: string;
  epoch_type: "SCREENING" | "TREATMENT" | "WASHOUT" | "FOLLOW_UP" | "RUN_IN";
  sequence_index: number;
}

export interface Designer_ExtractedVisit {
  visit_name: string;
  epoch_name: string;
  target_day: number;
  window_lower_days?: number;
  window_upper_days?: number;
  is_mandatory?: boolean;
}

export interface Designer_FaithfulnessVerificationMetadata {
  faithfulness_score: number;
  is_faithful: boolean;
  verification_status?: "VERIFIED" | "FLAGGED_LOW_FAITHFULNESS";
  citation_coverage?: number;
  protocol_version?: string;
  warnings?: string[];
}

export interface Designer_FeasibilityReport {
  starting_cohort_size: number;
  final_eligible_count: number;
  overall_eligibility_rate: number;
  attrition_steps?: Designer_AttritionStep[];
}

export interface Designer_FieldReference_Output {
  raw_reference: string;
  domain: string;
  variable: string;
}

export interface Designer_FieldWidgetConfig {
  variable_id: string;
  widget?: Designer_WidgetType;
  grid_column_span?: number;
  placeholder?: string | null;
  help_text?: string | null;
  css_class?: string | null;
  readonly?: boolean;
  hidden?: boolean;
}

export interface Designer_FormItem {
  item_id: string;
  name: string;
  question_text: string;
  data_type: string;
  required?: boolean;
}

export interface Designer_FormLibraryObjectDetail {
  id: string;
  version: string;
  status: Designer_LibraryStatus;
  sponsor_id: string;
  tenant_id: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
  prior_status?: string | null;
  object_type?: string;
  payload: Designer_FormPayload;
}

export interface Designer_FormPayload {
  items: Designer_FormItem[];
}

export interface Designer_FormReviewCommentResponse {
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

export interface Designer_GenerateStartupTemplateRequest {
  study_id: string;
  country_codes: string[];
}

export interface Designer_GenerateStartupTemplateResponse {
  study_id: string;
  generated_rules: Designer_PrerequisiteCriterionTemplate[];
  summary_by_country: Record<string, any>;
}

export interface Designer_GridSection {
  id: string;
  title: string;
  columns?: number;
  widgets?: Designer_FieldWidgetConfig[];
}

export interface Designer_HTTPValidationError {
  detail?: Designer_ValidationError[];
}

export interface Designer_InstantiateLibraryObjectRequest {
  library_object_id: string;
  version?: number | null;
}

export interface Designer_InstantiatedFromDetail {
  library_object_id: string;
  version: number;
  sponsor_id: string;
}

export interface Designer_InvalidParam {
  field?: string | null;
  reason?: string | null;
  value?: string | null;
}

export interface Designer_ItemMappingStatus {
  item_id: string | null;
  internal_id: number | null;
  is_mapped: boolean;
}

export interface Designer_JurisdictionInfo {
  code: string;
  name: string;
  regulatory_framework: string;
  ethics_framework: string;
  mandatory_criteria_count: number;
}

export interface Designer_LibraryInstanceResponse {
  id: string;
  study_id: string;
  object_type: string;
  payload: Record<string, any>;
  created_at: string;
  created_by: string;
  instantiated_from: Designer_InstantiatedFromDetail;
}

export interface Designer_LibraryObjectAmendRequest {
  reason_for_change: string;
  payload?: Record<string, any> | null;
}

export interface Designer_LibraryObjectListResponse {
  object?: string;
  data:
    | Designer_FormLibraryObjectDetail
    | Designer_DataElementLibraryObjectDetail
    | Designer_ArmLibraryObjectDetail
    | Designer_VisitLibraryObjectDetail[];
  has_more: boolean;
  next_cursor?: string | null;
}

export interface Designer_LibraryObjectTransitionRequest {
  status: Designer_LibraryStatus;
  change_reason: string;
}

export type Designer_LibraryStatus =
  "DRAFT" | "IN_REVIEW" | "APPROVED" | "PUBLISHED" | "ARCHIVED" | "REJECTED";

export interface Designer_LinkArmApplicabilityRequest {
  arm_id: string;
  target_id: string;
  target_type?: "visit" | "procedure" | "epoch";
}

export interface Designer_LinkEpochVisitRequest {
  epoch_id: string;
  visit_id: string;
}

export interface Designer_LinkTimingRequest {
  source_id: string;
  timing_id: string;
  source_type?: "visit" | "procedure";
}

export interface Designer_LinkVisitProcedureRequest {
  visit_id: string;
  procedure_id: string;
}

export interface Designer_LogicSchema {
  schema_version?: string;
  form_id: string;
  edit_checks?: Designer_AstEditCheck[];
  derived_rules?: Designer_DerivedMathRule[];
  cross_visit_dependencies?: Designer_CrossVisitDependency[];
}

export type Designer_LogicalOperator = "and" | "or" | "not";

export interface Designer_MigrationDirective_Input {
  directive_id: string;
  action: string;
  description: string;
  affected_cohort?: string;
  target_version: string;
}

export type Designer_MigrationMode = "prospective" | "retrospective";

export interface Designer_NarrativeDelta {
  section_id: string;
  section_title: string;
  change_type: string;
  old_text?: string | null;
  new_text?: string | null;
  delta_summary: string;
  safety_risk_impact?: boolean;
}

export interface Designer_NodeEvaluation {
  node_type: string;
  operator?: string | null;
  value?: any | null;
  is_indeterminate?: boolean;
  explanation: string;
  children?: Designer_NodeEvaluation[];
}

export interface Designer_NodeFitExplanation {
  subject_id: string;
  criterion_id: string;
  criterion_name: string;
  result_status: "TRUE" | "FALSE" | "INDETERMINATE";
  node_trace: Designer_NodeEvaluation;
  missing_fields?: string[];
  explanation: string;
}

export type Designer_ObjectType = "FORM" | "DATA_ELEMENT" | "ARM" | "VISIT";

export interface Designer_OperationalTicketBlueprint {
  domain_queue: Designer_DomainQueue;
  title: string;
  description: string;
  category?: string;
  priority?: string;
  gxp_severity?: string;
  assignee_role: string;
  action_plan?: string[];
  due_date_offset_days?: number | null;
  context_payload?: Record<string, any>;
}

export interface Designer_PatientMigrationMatrix {
  protocol_id: string;
  from_version_tag: string;
  to_version_tag: string;
  directives?: Designer_packages__ddf__tripartite__MigrationDirective[];
  effective_date?: string;
}

export interface Designer_PrerequisiteCriterionTemplate {
  country_code: string;
  facet: string;
  criterion_code: string;
  criterion_name: string;
  is_mandatory: boolean;
  description?: string | null;
}

export interface Designer_ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail: string;
  instance: string;
  code: string;
  invalid_params?: Designer_InvalidParam[] | null;
}

export interface Designer_ProcedureProperties {
  name?: string | null;
  activity_name?: string | null;
}

export interface Designer_ProcedureReorderItem {
  procedure_id: string;
  sequence: number;
}

export interface Designer_ProcedureReorderRequest {
  procedures: Designer_ProcedureReorderItem[];
}

export interface Designer_PromoteRequest {
  change_reason: string;
}

export interface Designer_ProtocolAmendRequest {
  amendment_type?: string | null;
  type?: string | null;
}

export interface Designer_ProtocolImpactAssessment_Input {
  assessment_id: string;
  study_id: string;
  base_version: string;
  amended_version: string;
  amendment_type?: string;
  is_substantial?: boolean;
  requires_reconsent?: boolean;
  patient_burden_delta?: number;
  estimated_cost_usd?: number;
  executive_summary: string;
  graph_deltas?: Designer_EntityDiff[];
  soa_deltas?: Designer_EntityDiff[];
  narrative_deltas?: Designer_NarrativeDelta[];
  data_capture_ecrf: Designer_DataCaptureEcrfImpact;
  subject_management_rtsm: Designer_SubjectManagementRtsmImpact;
  regulatory_compliance: Designer_RegulatoryComplianceImpact;
  reconsent_gating_plan: Designer_ReConsentGatingPlan;
  operational_tickets?: Designer_OperationalTicketBlueprint[];
  migration_directives?: Designer_MigrationDirective_Input[];
  created_at: string;
}

export interface Designer_ProtocolImpactAssessment_Output {
  assessment_id: string;
  study_id: string;
  base_version: string;
  amended_version: string;
  amendment_type?: string;
  is_substantial?: boolean;
  requires_reconsent?: boolean;
  patient_burden_delta?: number;
  estimated_cost_usd?: number;
  executive_summary: string;
  graph_deltas?: Designer_EntityDiff[];
  soa_deltas?: Designer_EntityDiff[];
  narrative_deltas?: Designer_NarrativeDelta[];
  data_capture_ecrf: Designer_DataCaptureEcrfImpact;
  subject_management_rtsm: Designer_SubjectManagementRtsmImpact;
  regulatory_compliance: Designer_RegulatoryComplianceImpact;
  reconsent_gating_plan: Designer_ReConsentGatingPlan;
  operational_tickets?: Designer_OperationalTicketBlueprint[];
  migration_directives?: Designer_apps__designer__domain__cdisc__branch_models__MigrationDirective[];
  created_at: string;
}

export interface Designer_ProtocolQualityScore {
  study_id: string;
  quality_score: number;
  patient_burden_index: number;
  findings?: Designer_QualityRuleFinding[];
  passed: boolean;
  readability?: Designer_ReadabilityReport | null;
  burden_details?: Designer_BurdenTraceReport | null;
  amendment_impact?: Designer_AmendmentImpactReport | null;
  feasibility?: Designer_FeasibilityReport | null;
}

export interface Designer_ProtocolRAGContextChunk {
  chunk_id: string;
  protocol_version: string;
  section_number?: string | null;
  section_title?: string | null;
  page_number: number;
  chunk_text: string;
  is_approved?: boolean;
}

export interface Designer_PublicationRequest {
  form_definition: Designer_TripartiteFormDefinition;
  reason_for_change: string;
  step_up_signature_token: string;
}

export interface Designer_PublishStudyVersionRequest {
  description?: string | null;
  baseline_snapshot?: Record<string, any> | null;
  amended_snapshot?: Record<string, any> | null;
}

export interface Designer_PublishStudyVersionResponse {
  status: string;
  version_id: string;
  amendment_id?: string | null;
  summary_of_changes?: string | null;
}

export interface Designer_QualityRuleFinding {
  rule_id: string;
  severity: string;
  category: string;
  message: string;
  target_node_id?: string | null;
}

export interface Designer_ReConsentGatingPlan {
  gating_mandated?: boolean;
  affected_cohort?: string;
  flagged_subject_count?: number;
  flagged_subject_ids?: string[];
  justification?: string;
}

export interface Designer_ReadabilityReport {
  flesch_reading_ease: number;
  flesch_kincaid_grade_level: number;
  word_count: number;
  sentence_count: number;
  syllable_count: number;
  interpretation: string;
}

export interface Designer_RegulatoryComplianceImpact {
  safety_risk_level?: string;
  requires_reconsent?: boolean;
  is_substantial_amendment?: boolean;
  icf_version_upgrade?: string;
  irb_iec_submission_type?: string;
  affected_subject_cohorts?: string[];
  flagged_active_subjects?: string[];
  action_items?: string[];
}

export interface Designer_RenameConceptRequest {
  display_name: string;
  reason_for_change: string;
}

export interface Designer_ReorderBlocksRequest {
  block_ids: string[];
  change_reason?: string | null;
}

export interface Designer_ResumeDAGJobRequest {
  from_stage?: Designer_DigitizationStage | null;
  change_reason?: string | null;
}

export interface Designer_RippleAnalysisRequest {
  study_id: string;
  base_version_tag?: string;
  amended_version_tag?: string;
  amendment_type?: string;
  requires_reconsent?: boolean | null;
  base_payload?: Record<string, any> | null;
  draft_payload?: Record<string, any> | null;
  active_subject_ids?: string[] | null;
}

export interface Designer_RulePreviewResponse {
  xpath: string;
  failures: string[];
  circular_cycles: string[];
}

export interface Designer_SchemaRevisionSummary {
  arms?: Record<string, any>;
  epochs?: Record<string, any>;
  encounters?: Record<string, any>;
  activities?: Record<string, any>;
  eligibility_criteria?: Record<string, any>;
  forms?: Record<string, any>;
}

export type Designer_SectionReviewStatus =
  "DRAFT" | "IN_REVIEW" | "LOCKED" | "APPROVED";

export interface Designer_SectionReviewTransition {
  transition_id: string;
  section_id: string;
  study_id: string;
  from_status: Designer_SectionReviewStatus;
  to_status: Designer_SectionReviewStatus;
  actor_id: string;
  actor_role: string;
  reason_for_change: string;
  timestamp?: string;
}

export interface Designer_SectionTransitionRequest {
  to_status: Designer_SectionReviewStatus;
  reason_for_change: string;
  username?: string | null;
  password?: string | null;
  signing_reason?: Designer_SigningReason | null;
}

export interface Designer_SemanticDiffRequest {
  study_id: string;
  base_version_tag: string;
  amended_version_tag: string;
  base_payload?: Record<string, any> | null;
  draft_payload?: Record<string, any> | null;
}

export interface Designer_SemanticDiffResponse {
  study_id: string;
  base_version_tag: string;
  amended_version_tag: string;
  usdm_graph_diffs?: Designer_EntityDiff[];
  soa_matrix_diffs?: Designer_EntityDiff[];
  eligibility_diffs?: Designer_EntityDiff[];
  ecrf_form_diffs?: Designer_EntityDiff[];
  impact_summary: Designer_AmendmentImpactSummary;
  migration_directives?: Designer_apps__designer__domain__cdisc__branch_models__MigrationDirective[];
}

export type Designer_SigningReason =
  | "AUTHOR"
  | "REVIEW"
  | "APPROVAL"
  | "SPONSOR_APPROVAL"
  | "INVESTIGATOR_SIGNATURE"
  | "TECHNICAL_QC"
  | "CLINICAL_QC"
  | "DATA_LOCK"
  | "SYSTEM_SEAL"
  | "PROTOCOL_APPROVAL"
  | "REGULATORY_FORM_SIGNATURE"
  | "TRAINING_ACKNOWLEDGEMENT"
  | "SITE_VISIT_SIGN_OFF"
  | "ADJUDICATION_SIGN_OFF"
  | "ENDPOINT_ADJUDICATION";

export interface Designer_SoACellView {
  activity_id: string;
  encounter_id: string;
  epoch_id: string;
  is_applicable: boolean;
  details?: string | null;
  arm_id?: string | null;
  derived_from_soa?: boolean;
  section_number?: string | null;
  page_number?: number | null;
  citation_marker?: string | null;
}

export interface Designer_SoAEntityCreatedResponse {
  status?: string;
  id: string;
}

export interface Designer_SoAEntityDetail {
  id: string;
  version_index: number;
  created_by: string;
  created_at: string;
  is_retired?: boolean;
  is_deleted?: boolean;
}

export interface Designer_SoAHeaderArm {
  arm_id: string;
  arm_name: string;
  sequence: number;
}

export interface Designer_SoAHeaderEncounter {
  encounter_id: string;
  encounter_name: string;
  epoch_id: string;
  sequence: number;
  arm_id?: string | null;
}

export interface Designer_SoAHeaderEpoch {
  epoch_id: string;
  epoch_name: string;
  sequence: number;
  arm_id?: string | null;
}

export interface Designer_SoALinkResponse {
  status?: string;
  message?: string;
}

export interface Designer_SoAMatrixView {
  epochs?: Designer_SoAHeaderEpoch[];
  encounters?: Designer_SoAHeaderEncounter[];
  rows?: Designer_SoARowView[];
  arms?: Designer_SoAHeaderArm[];
}

export interface Designer_SoARowView {
  activity_id: string;
  activity_name: string;
  cells?: Designer_SoACellView[];
  derived_from_soa?: boolean;
}

export interface Designer_StageCheckpoint {
  stage: Designer_DigitizationStage;
  status: Designer_StageGateStatus;
  started_at: string;
  completed_at?: string | null;
  duration_ms?: number;
  gate_status?: Designer_StageGateStatus;
  gate_errors?: string[];
  recovery_attempts?: number;
  recovered_by_llm?: boolean;
  corrected_by?: string | null;
  correction_reason?: string | null;
  signature_manifestation?: Record<string, any> | null;
  confidence_score?: number;
  data?: Record<string, any>;
}

export type Designer_StageGateStatus = "PASSED" | "FAILED" | "SKIPPED";

export interface Designer_StartDAGJobResponse {
  job_id: string;
  status: Designer_DigitizationJobStatus;
  current_stage?: Designer_DigitizationStage | null;
  message: string;
}

export interface Designer_StudyAlignmentReport {
  study_id: string;
  complete_activities: Designer_ActivityReport[];
  incomplete_activities: Designer_ActivityReport[];
  unmapped_activities: Designer_ActivityReport[];
  unmapped_odm_items: Record<string, any>[];
  unmapped_crf_item_values: Record<string, any>[];
}

export interface Designer_StudyArmProperties {
  name: string;
  type: string;
  sequence?: number | null;
}

export interface Designer_StudyTerminologyValidationReport {
  study_id: string;
  is_valid: boolean;
  total_concepts: number;
  valid_count: number;
  invalid_count: number;
  degraded_count: number;
  concepts: Designer_ConceptValidationReport[];
}

export interface Designer_SubCohortRecommendation {
  sub_cohort_id: string;
  name: string;
  description: string;
  estimated_fit_percentage: number;
  criteria_breakdown?: Designer_CriteriaBreakdownSummary[];
  sample_patient_ids?: string[];
}

export interface Designer_SubjectManagementRtsmImpact {
  cohort_adjustments_count?: number;
  affected_arms?: string[];
  dosing_changes?: Record<string, any>[];
  visit_window_adjustments?: Record<string, any>[];
  requires_kit_reallocation?: boolean;
  randomization_ratio_changed?: boolean;
  action_items?: string[];
}

export interface Designer_SubmitStageCorrectionRequest {
  stage: Designer_DigitizationStage;
  corrected_data: Record<string, any>;
  signature_username: string;
  change_reason: string;
  signature_token?: string | null;
  resume_pipeline?: boolean;
}

export interface Designer_Suggestion {
  suggestion_id: string;
  block_id: string;
  study_id: string;
  suggested_text: string;
  original_text: string;
  status?: Designer_SuggestionStatus;
  created_by: string;
  created_at?: string;
  reason: string;
  decision_reason?: string | null;
  decided_by?: string | null;
  decided_at?: string | null;
  block_version_index: number;
  version_index?: number;
}

export interface Designer_SuggestionCreate {
  suggested_text: string;
  reason: string;
}

export interface Designer_SuggestionDecisionRequest {
  decision: "accept" | "reject";
  decision_reason: string;
}

export type Designer_SuggestionStatus = "pending" | "accepted" | "rejected";

export interface Designer_SynopsisExportRequest {
  study_id: string;
  format?: string;
  creator?: string | null;
  change_reason?: string | null;
}

export interface Designer_SynopsisExportResponse {
  study_id: string;
  format: string;
  content_base64: string;
  filename: string;
}

export interface Designer_SynthesizedECRFForm {
  form_id: string;
  form_name: string;
  cdash_domain: string;
  items?: Record<string, any>[];
  rules?: Record<string, any>[];
  section_number?: string | null;
  page_number?: number | null;
  citation_marker?: string | null;
}

export interface Designer_SyntheticPayload {
  payload_id: string;
  test_scenario: string;
  data: Record<string, any>;
  expected_rule_outcomes: Record<string, any>;
}

export interface Designer_SyntheticSimulationSummary {
  simulated_subject_count: number;
  complete_data_fit_rate: number;
  partial_data_fit_rate: number;
  missing_data_fit_rate: number;
  indeterminate_rate: number;
  high_risk_missing_fields?: string[];
}

export interface Designer_TerminologyConcept {
  code: string;
  decode: string;
  system: string;
  valid: boolean;
}

export type Designer_TerminologyEnum =
  "SNOMED-CT" | "LOINC" | "MedDRA" | "WHODrug" | "NCI" | "CDISC-CT";

export interface Designer_TerminologySearchResponse {
  query: string;
  state: Designer_CodeValidationState;
  results: Designer_TerminologyConcept[];
  total_results: number;
  error_message?: string | null;
}

export interface Designer_TicketDispatchRequest {
  study_id: string;
  impact_assessment?: Designer_ProtocolImpactAssessment_Input | null;
  base_version_tag?: string | null;
  amended_version_tag?: string | null;
  base_payload?: Record<string, any> | null;
  draft_payload?: Record<string, any> | null;
  selected_domain_queues?: Designer_DomainQueue[] | null;
}

export interface Designer_TicketDispatchResponse {
  study_id: string;
  assessment_id: string;
  total_dispatched?: number;
  dispatched_tickets?: Designer_DispatchedTicketInfo[];
  message: string;
}

export interface Designer_TimingWindowProperties {
  name: string;
  anchor_reference?: string | null;
  target_day?: number | null;
  min_offset?: number | null;
  max_offset?: number | null;
  conditional?: boolean | null;
  reason?: string | null;
}

export interface Designer_TransitionItemRequest {
  status: string;
  reason: string;
  name?: string | null;
  label?: string | null;
  value?: string | null;
}

export interface Designer_TripartiteFormDefinition {
  form_id: string;
  protocol_id: string;
  version_index?: number;
  version_tag?: string;
  data_schema: Designer_DataSchema;
  ui_schema: Designer_UiSchema;
  logic_schema: Designer_LogicSchema;
  created_at?: string;
  created_by?: string;
  reason_for_change?: string;
}

export interface Designer_USDMProtocolExtractionResponse {
  study_title: string;
  protocol_id: string;
  phase: "PHASE_I" | "PHASE_I_II" | "PHASE_II" | "PHASE_III" | "PHASE_IV";
  therapeutic_area: string;
  arms?: Designer_ExtractedArm[];
  epochs?: Designer_ExtractedEpoch[];
  visits?: Designer_ExtractedVisit[];
  activities?: Designer_ExtractedActivity[];
  criteria?: Designer_ExtractedCriterion[];
  confidence_score: number;
  faithfulness_metadata?: Designer_FaithfulnessVerificationMetadata | null;
}

export interface Designer_UatEvidenceReport {
  report_id: string;
  form_id: string;
  protocol_id: string;
  executed_at?: string;
  total_scenarios_evaluated: number;
  scenarios_passed: number;
  scenarios_failed: number;
  rule_coverage_percentage: number;
  execution_duration_ms: number;
  gxp_compliance_verified?: boolean;
  sha256_evidence_digest: string;
}

export interface Designer_UatExecutionRequest {
  form_definition: Designer_TripartiteFormDefinition;
  payloads?: Designer_SyntheticPayload[];
}

export interface Designer_UiSchema {
  schema_version?: string;
  form_id: string;
  sections?: Designer_GridSection[];
  default_viewport?: Designer_ViewportMode;
  theme_tokens?: Record<string, any>;
}

export interface Designer_UpdateArmRequest {
  reason_for_change: string;
  object_type?: string;
  payload: Designer_ArmPayload;
}

export interface Designer_UpdateBlockRequest {
  properties: Record<string, any>;
  change_reason?: string | null;
}

export interface Designer_UpdateConceptRequest {
  display_name: string;
  definition: string;
  cdash_mapping?: Designer_CDASHMapping | null;
  allowable_units?: Designer_AllowableUnit[] | null;
  reason_for_change: string;
}

export interface Designer_UpdateDataElementRequest {
  reason_for_change: string;
  object_type?: string;
  payload: Designer_DataElementPayload;
}

export interface Designer_UpdateEligibilityCriterionRequest {
  criterion_type: "inclusion" | "exclusion";
  description: string;
  dsl_source: string;
  expected_outcome?: boolean;
  change_reason: string;
}

export interface Designer_UpdateEpochRequest {
  properties: Designer_EpochProperties;
  reason_for_change?: string;
}

export interface Designer_UpdateFormRequest {
  reason_for_change: string;
  object_type?: string;
  payload: Designer_FormPayload;
}

export interface Designer_UpdateLibraryInstanceRequest {
  payload: Record<string, any>;
}

export interface Designer_UpdateProcedureRequest {
  properties: Designer_ProcedureProperties;
  reason_for_change?: string;
}

export interface Designer_UpdateStudyArmRequest {
  properties: Designer_StudyArmProperties;
  reason_for_change?: string;
}

export interface Designer_UpdateTimingWindowRequest {
  properties: Designer_TimingWindowProperties;
  reason_for_change?: string;
}

export interface Designer_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Designer_VariableDefinition {
  id: string;
  name: string;
  data_type?: Designer_ClinicalDataType;
  cdisc: Designer_CdiscMetadata;
  constraints?: Designer_ConstraintDefinition;
  phi_pii_masked?: boolean;
  sdv_tier?: string;
  part11_reason_required?: boolean;
}

export interface Designer_VersionDiffResponse {
  added_nodes: Designer_DifferenceResult[];
  modified_nodes: Designer_DifferenceResult[];
  deleted_nodes: Designer_DifferenceResult[];
}

export type Designer_ViewportMode =
  "desktop_edc" | "mobile_epro" | "tablet_esource";

export interface Designer_VisitAttributes {
  visit_type: string;
  planned_day: number;
  window_days: number;
}

export interface Designer_VisitLibraryObjectDetail {
  id: string;
  version: string;
  status: Designer_LibraryStatus;
  sponsor_id: string;
  tenant_id: string;
  created_at: string;
  created_by: string;
  updated_at?: string | null;
  updated_by?: string | null;
  reason_for_change?: string | null;
  prior_status?: string | null;
  object_type?: string;
  payload: Designer_VisitPayload;
}

export interface Designer_VisitPayload {
  attributes: Designer_VisitAttributes;
}

export interface Designer_VisitProperties {
  name?: string | null;
  encounter_name?: string | null;
  sequence: number;
}

export interface Designer_VisitReorderItem {
  visit_id: string;
  sequence: number;
}

export interface Designer_VisitReorderRequest {
  visits: Designer_VisitReorderItem[];
}

export interface Designer_VisitToArmAssignmentRequest {
  arm_id: string;
  visit_ids: string[];
}

export interface Designer_VisitToEpochAssignmentRequest {
  epoch_id: string;
  visit_ids: string[];
}

export type Designer_WidgetType =
  | "text_input"
  | "textarea"
  | "number_input"
  | "radio_group"
  | "select_dropdown"
  | "multi_select"
  | "date_picker"
  | "time_picker"
  | "slider_vas"
  | "body_map_coordinate"
  | "repeating_log_grid"
  | "medical_autocode"
  | "sensor_stream_card";

export interface Designer_apps__designer__domain__cdisc__branch_models__MigrationDirective {
  directive_id: string;
  action: string;
  description: string;
  affected_cohort?: string;
  target_version: string;
}

export interface Designer_apps__designer__domain__eligibility__models__ExpressionNode_Input {
  type: "logical" | "comparison" | "field_ref" | "constant";
  operator?:
    Designer_ComparisonOperator | Designer_LogicalOperator | string | null;
  operands?:
    | Designer_apps__designer__domain__eligibility__models__ExpressionNode_Input[]
    | null;
  value?: any | null;
  field_ref?: Designer_apps__designer__domain__eligibility__models__FieldReference | null;
}

export interface Designer_apps__designer__domain__eligibility__models__FieldReference {
  raw_reference: string;
  domain: string;
  variable: string;
}

export interface Designer_apps__designer__domain__protocol_authoring__soa__CreateVisitRequest {
  id: string;
  properties: Designer_VisitProperties;
  change_reason?: string;
}

export interface Designer_apps__designer__domain__protocol_authoring__soa__UpdateVisitRequest {
  properties: Designer_VisitProperties;
  reason_for_change?: string;
}

export interface Designer_apps__designer__library__CreateVisitRequest {
  id: string;
  version?: string;
  status?: Designer_LibraryStatus;
  sponsor_id: string;
  change_reason: string;
  object_type?: string;
  payload: Designer_VisitPayload;
}

export interface Designer_apps__designer__library__UpdateVisitRequest {
  reason_for_change: string;
  object_type?: string;
  payload: Designer_VisitPayload;
}

export interface Designer_packages__ddf__rules__ast__ExpressionNode {
  type: "logical" | "comparison" | "function" | "field_ref" | "constant";
  operator?: string | null;
  operands?: Designer_packages__ddf__rules__ast__ExpressionNode[] | null;
  value?: any | null;
  field_ref?: Designer_packages__ddf__rules__ast__FieldReference | null;
}

export interface Designer_packages__ddf__rules__ast__FieldReference {
  field_id: string;
  form_id?: string | null;
  visit_id?: string | null;
  visit_relative?: string | null;
}

export interface Designer_packages__ddf__tripartite__MigrationDirective {
  variable_id: string;
  action: string;
  migration_mode?: Designer_MigrationMode;
  backfill_query_message?: string | null;
}

export interface Econsent_ArchivalDeliveryResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  id: string;
  status: string;
  attempts: number;
  last_error?: string | null;
  next_retry_at?: string | null;
  completed_at?: string | null;
  retry_eligible: boolean;
  correlation_id: string;
  template_id: string;
  subject_pseudonym: string;
  study_id: string;
  site_id?: string | null;
  etmf_document_id?: string | null;
}

export interface Econsent_CdiscOdmExportResponse {
  study_id: string;
  subject_pseudonym: string;
  template_id: string;
  version_index: number;
  odm_version: string;
  xml_content: string;
}

export interface Econsent_ClauseDiffDTO {
  clause_id: string;
  change_type: string;
  old_title: string | null;
  new_title: string | null;
  old_text: string | null;
  new_text: string | null;
  text_diff: string | null;
  is_substantive: boolean;
}

export interface Econsent_ClauseHarmonizationApplyRequest {
  harmonized_text: string;
  reason_for_change: string;
  protocol_version?: string | null;
}

export interface Econsent_ClauseHarmonizationApplyResponse {
  clause_id: string;
  version_index: number;
  title: string;
  text: string;
  metrics: Econsent_ReadabilityMetricsDTO;
  protocol_version: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface Econsent_ComposedClauseResponse {
  clause_id: string;
  title: string;
  text: string;
  version_index: number;
}

export interface Econsent_ComposedTemplateResponse {
  id: string;
  template_id: string;
  study_id: string;
  template_name: string;
  protocol_version: string;
  is_published: boolean;
  requires_reconsent: boolean;
  version_index: number;
  clauses: Econsent_ComposedClauseResponse[];
  workflow_steps: Record<string, any>[];
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface Econsent_ComprehensionCheckCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  questions: Record<string, any>[];
  expected_answers: Record<string, any>;
  threshold_policy: Record<string, any>;
}

export interface Econsent_ComprehensionCheckResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  id: string;
  template_id: string;
  questions: Record<string, any>[];
  expected_answers: Record<string, any>;
  threshold_policy: Record<string, any>;
}

export interface Econsent_ComprehensionSubmissionRequest {
  subject_pseudonym: string;
  submitted_answers: Record<string, any>;
  reason_for_change: string;
}

export interface Econsent_ComprehensionSubmissionResponse {
  passed: boolean;
  score: number;
  total_questions: number;
  correct_count: number;
  min_required: number;
  next_step: string;
  message: string;
  details?: Record<string, any>[] | null;
}

export interface Econsent_ConsentAuditLogResponse {
  id: string;
  timestamp: string;
  actor_id: string;
  actor_role: string;
  action: string;
  document_id: string | null;
  details: string;
  reason_for_change: string;
  tenant_id?: string;
  previous_digest?: string | null;
  sha256_digest?: string | null;
}

export interface Econsent_ConsentClauseCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  clause_id?: string | null;
  study_id: string;
  title: string;
  text: string;
}

export interface Econsent_ConsentClauseResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  clause_id: string;
  study_id: string;
  title: string;
  text: string;
}

export interface Econsent_ConsentClauseUpdate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  study_id: string;
  title: string;
  text: string;
}

export interface Econsent_ConsentDocumentCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  study_id: string;
  site_id: string;
  document_name: string;
  content: string;
}

export interface Econsent_ConsentDocumentResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  study_id: string;
  site_id: string;
  document_name: string;
  content: string;
}

export interface Econsent_ConsentSignatureRequest {
  subject_pseudonym: string;
  signature_data?: string | null;
  reason_for_change: string;
  site_id?: string | null;
  role?: string;
  signer_name?: string | null;
  signer_email?: string | null;
  meaning?: string | null;
  lar_relationship?: string | null;
  lar_authority_basis?: string | null;
}

export interface Econsent_ConsentSignatureResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  id: string;
  template_id: string;
  subject_pseudonym: string;
  role?: string;
  signer_name?: string | null;
  signer_email?: string | null;
  meaning?: string | null;
  signature_data: string | null;
  signed_at: string;
  digest_sha256?: string | null;
  lar_relationship?: string | null;
  lar_authority_basis?: string | null;
}

export interface Econsent_ConsentTemplateCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  template_id?: string | null;
  study_id: string;
  template_name: string;
  protocol_version: string;
  requires_reconsent?: boolean;
  clauses?: string[];
  workflow_steps?: Record<string, any>[];
}

export interface Econsent_ConsentTemplateResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  template_id: string;
  study_id: string;
  template_name: string;
  protocol_version: string;
  is_published: boolean;
  requires_reconsent: boolean;
  clauses?: string[];
  workflow_steps?: Record<string, any>[];
}

export interface Econsent_ConsentTemplateUpdate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  study_id: string;
  template_name: string;
  protocol_version: string;
  requires_reconsent?: boolean;
  clauses?: string[];
  workflow_steps?: Record<string, any>[];
}

export interface Econsent_ConsentTranslationCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  translation_id?: string | null;
  source_id: string;
  source_type: string;
  source_version_index: number;
  language_code: string;
  translated_title: string;
  translated_text: string;
}

export interface Econsent_ConsentTranslationResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  translation_id: string;
  source_id: string;
  source_type: string;
  source_version_index: number;
  language_code: string;
  translated_title: string;
  translated_text: string;
  status: string;
}

export interface Econsent_ConsentTranslationUpdate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  source_id: string;
  source_type: string;
  source_version_index: number;
  language_code: string;
  translated_title: string;
  translated_text: string;
}

export interface Econsent_ConsentWithdrawalRequest {
  study_id: string;
  site_id: string;
  subject_pseudonym: string;
  template_id: string;
  withdrawal_date?: string | null;
  reason_category: string;
  reason_detail: string;
  scope?: string;
  investigator_id?: string | null;
  reason_for_change: string;
}

export interface Econsent_ConsentWithdrawalResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  study_id: string;
  site_id: string;
  subject_pseudonym: string;
  template_id: string;
  withdrawal_date: string;
  reason_category: string;
  reason_detail: string;
  scope: string;
  acknowledged_by_investigator: boolean;
  investigator_id: string | null;
}

export interface Econsent_GranularOptionCreate {
  option_code: string;
  title: string;
  description: string;
  category?: string;
  is_mandatory?: boolean;
  default_selected?: boolean;
  reason_for_change: string;
}

export interface Econsent_GranularOptionResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  id: string;
  template_id: string;
  option_code: string;
  title: string;
  description: string;
  category: string;
  is_mandatory: boolean;
  default_selected: boolean;
}

export interface Econsent_HTTPValidationError {
  detail?: Econsent_ValidationError[];
}

export interface Econsent_JargonSubstitutionDTO {
  original_term: string;
  suggested_term: string;
  rationale: string;
  category?: string;
  confidence_score?: number;
  start_offset?: number | null;
  end_offset?: number | null;
}

export interface Econsent_MinorMilestoneEvaluationRequest {
  study_id: string;
  subjects: Econsent_SubjectMilestoneInput[];
  as_of_date?: string | null;
  lead_time_days?: number;
}

export interface Econsent_MinorMilestoneEvaluationResponse {
  study_id: string;
  evaluation_date: string;
  total_evaluated: number;
  majority_reached_count: number;
  approaching_count: number;
  results: Econsent_MinorMilestoneResultDTO[];
}

export interface Econsent_MinorMilestoneResultDTO {
  subject_pseudonym: string;
  date_of_birth: string;
  jurisdiction: string;
  current_age_years: number;
  exact_age: number;
  legal_age_threshold: number;
  majority_reached_date: string;
  has_reached_majority: boolean;
  is_transition_due: boolean;
  days_until_majority: number;
  requires_adult_reconsent: boolean;
  milestone_status: string;
}

export interface Econsent_ReadabilityAnalysisRequest {
  text: string;
  study_id?: string | null;
}

export interface Econsent_ReadabilityAnalysisResponse {
  metrics: Econsent_ReadabilityMetricsDTO;
}

export interface Econsent_ReadabilityHarmonizationRequest {
  text: string;
  study_id?: string | null;
  target_grade_level?: number;
  protocol_version?: string | null;
}

export interface Econsent_ReadabilityHarmonizationResponse {
  original_metrics: Econsent_ReadabilityMetricsDTO;
  harmonized_metrics: Econsent_ReadabilityMetricsDTO;
  substitutions: Econsent_JargonSubstitutionDTO[];
  harmonized_text: string;
  grade_level_delta: number;
  model_identifier: string;
}

export interface Econsent_ReadabilityMetricsDTO {
  word_count: number;
  sentence_count: number;
  syllable_count: number;
  difficult_word_count: number;
  difficult_words: string[];
  flesch_reading_ease: number;
  flesch_kincaid_grade_level: number;
  dale_chall_score: number;
  dale_chall_grade_level: string;
  is_target_grade_level: boolean;
  interpretation: string;
}

export interface Econsent_ReconsentRequirementResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  study_id: string;
  site_id: string | null;
  template_id: string;
  prior_version_index: number;
  new_version_index: number;
  subject_pseudonym: string;
  status: string;
  change_summary: string;
  substantive_changes: Record<string, any>[];
  deadline_at: string | null;
  completed_consent_id: string | null;
}

export interface Econsent_ReconsentTriggerRequest {
  study_id: string;
  site_id?: string | null;
  prior_version_index: number;
  new_version_index: number;
  change_summary: string;
  substantive_changes?: Record<string, any>[];
  reason_for_change: string;
}

export interface Econsent_SubjectConsentCaptureRequest {
  subject_pseudonym: string;
  site_id: string;
  device_timestamp?: string | null;
  source_content_identity: string;
  reason_for_change: string;
  signatures?: Record<string, any>[] | null;
  granular_selections?: Record<string, any>[] | null;
}

export interface Econsent_SubjectConsentResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  subject_pseudonym: string;
  study_id: string;
  site_id: string;
  template_id: string;
  protocol_version: string;
  source_content_identity: string;
  status?: string;
  server_timestamp: string;
  device_timestamp?: string | null;
  signature_manifest: Record<string, any>;
}

export interface Econsent_SubjectConsentStatusResponse {
  subject_pseudonym: string;
  study_id: string;
  site_id: string;
  template_id: string;
  version_index: number;
  protocol_version: string;
  signed: boolean;
  comprehension_passed: boolean;
  status?: string;
  requires_reconsent?: boolean;
}

export interface Econsent_SubjectMilestoneInput {
  subject_pseudonym: string;
  date_of_birth: string;
  jurisdiction?: string | null;
  site_id?: string | null;
  current_consent_role?: string | null;
}

export interface Econsent_TemplateDiffResponse {
  template_id: string;
  base_version_index: number;
  target_version_index: number;
  total_added: number;
  total_removed: number;
  total_modified: number;
  total_unchanged: number;
  requires_reconsent: boolean;
  substantive_summary: string[];
  clause_diffs: Econsent_ClauseDiffDTO[];
}

export interface Econsent_TranslationTransitionRequest {
  status: string;
  reason_for_change: string;
}

export interface Econsent_TriggerMajorityReconsentRequest {
  study_id: string;
  site_id?: string | null;
  template_id: string;
  adult_version_index?: number;
  subjects: Econsent_SubjectMilestoneInput[];
  lead_time_days?: number;
  as_of_date?: string | null;
  reason_for_change?: string;
}

export interface Econsent_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Econsent_VerifiableCertificateResponse {
  study_id: string;
  subject_pseudonym: string;
  template_id: string;
  version_index: number;
  html_content: string;
  digest_sha256: string;
}

export interface Eisf_BinderCompletenessResponse {
  site_id: string;
  is_complete: boolean;
  sections: Eisf_BinderSectionStatus[];
}

export interface Eisf_BinderSectionStatus {
  section_name: string;
  required_artifacts: string[];
  present: string[];
  missing: string[];
}

export interface Eisf_DocumentCreate {
  study_id: string;
  site_id: string;
  binder_classification: string;
  filename: string;
  content: string;
  mime_type: string;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string;
  reason_for_change: string;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface Eisf_DocumentResponse {
  id: string;
  study_id: string;
  site_id: string;
  binder_classification: string;
  filename: string;
  content: string;
  mime_type: string;
  version_index: number;
  created_at: string;
  created_by: string;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  sync_status: string;
  source_system: string;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface Eisf_DocumentUpdate {
  study_id: string;
  site_id: string;
  binder_classification: string;
  filename: string;
  content: string;
  mime_type: string;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string;
  reason_for_change: string;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface Eisf_EISFDocumentDetail {
  id: string;
  site_id: string;
  section_code: string;
  filename: string;
  version: string;
  expiration_date?: string | null;
  created_at: string;
  created_by: string;
  download_url?: string | null;
}

export interface Eisf_EISFDocumentIntelligenceRequest {
  content: string;
  filename: string;
  site_id?: string | null;
  study_id?: string | null;
  mime_type?: string;
}

export interface Eisf_EISFDocumentIntelligenceResponse {
  filename: string;
  recommended_binder_section: string;
  recommended_eisf_folder: string;
  recommended_etmf_artifact_code: string;
  recommended_etmf_artifact_name: string;
  confidence: number;
  extracted_metadata: Record<string, any>;
  signature_completeness: Record<string, any>;
  intelligence_report: Record<string, any>;
}

export interface Eisf_EISFDocumentUploadRequest {
  study_id: string;
  section_code: string;
  filename: string;
  content: string;
  mime_type: string;
  reason_for_change: string;
  expiration_date?: string | null;
}

export interface Eisf_EISFFolderNode {
  section_code: string;
  title: string;
  document_count: number;
  subfolders?: Eisf_EISFFolderNode[];
}

export interface Eisf_EISFIngestionRequest {
  study_id: string;
  site_id: string;
  binder_classification?: string | null;
  artifact_type?: string | null;
  filename: string;
  content: string;
  mime_type: string;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string;
  reason_for_change?: string | null;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface Eisf_EISFSyncItem {
  id?: string | null;
  study_id: string;
  site_id: string;
  binder_classification: string;
  filename: string;
  content: string;
  mime_type: string;
  version_index?: number | null;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string;
  sync_status?: string;
  conflict_policy?: string;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface Eisf_EISFSyncRequest {
  submissions: Eisf_EISFSyncItem[];
}

export interface Eisf_EISFSyncResponse {
  status?: string;
  processed_count: number;
  created_count: number;
  updated_count: number;
  ignored_count: number;
}

export interface Eisf_HTTPValidationError {
  detail?: Eisf_ValidationError[];
}

export interface Eisf_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface ETMF_ArchiveJobResponse {
  job_id: string;
  study_id: string;
  status: "PENDING" | "PROCESSING" | "COMPLETED" | "FAILED";
  download_url?: string | null;
}

export interface ETMF_ArtifactDetail {
  artifact_type: string;
  scope: string;
  status: string;
  document_id?: string | null;
  version_index?: number | null;
}

export interface ETMF_AuditChainVerificationResponse {
  is_valid: boolean;
  total_sealed_blocks: number;
  total_sealed_records: number;
  latest_block_hash: string | null;
  genesis_block_hash: string | null;
  unsealed_records_count: number;
  tamper_detected: boolean;
  details: string;
}

export interface ETMF_AuditLogResponse {
  id: string;
  timestamp: string;
  user_id: string;
  user_role: string;
  action: string;
  document_id: string | null;
  details: string;
  reason_for_change?: string | null;
  cryptographic_seal?: string | null;
  old_value?: any | null;
  new_value?: any | null;
  entity_type?: string | null;
  sha256_hash?: string | null;
}

export interface ETMF_AutoFileRequest {
  filename: string;
  artifact_type?: string | null;
  free_text?: string | null;
  study_id?: string | null;
}

export interface ETMF_AutoFileResponse {
  resolved_zone: number;
  resolved_section: string;
  artifact_code: string;
  artifact_type: string;
  match_basis: string;
}

export interface ETMF_AutomatedRedactRequest {
  profile?: ETMF_ComplianceProfile;
  custom_terms?: string[] | null;
  strategies?: Record<string, any> | null;
  redacted_filename?: string | null;
}

export interface ETMF_AutomatedRedactResponse {
  status?: string;
  document_id: string;
  version_index: number;
  filename: string;
  categories_counts: Record<string, any>;
  manifest: Record<string, any>;
}

export interface ETMF_BinderArtifactNode {
  artifact_code: string;
  artifact_name: string;
  status: string;
  document_id?: string | null;
  version_index?: number | null;
}

export interface ETMF_BinderSectionNode {
  section_code: string;
  section_name: string;
  artifacts: ETMF_BinderArtifactNode[];
}

export interface ETMF_BinderStructureResponse {
  study_id: string;
  milestone?: string | null;
  site_id?: string | null;
  zones: ETMF_BinderZoneNode[];
  present_artifacts: string[];
  missing_artifacts: string[];
}

export interface ETMF_BinderZoneNode {
  zone_code: number;
  zone_name: string;
  sections: ETMF_BinderSectionNode[];
}

export interface ETMF_CRAQCReviewRequest {
  decision: string;
  reason_for_change: string;
  override_artifact_code?: string | null;
  discrepancy_comment?: string | null;
}

export interface ETMF_CRAQCStagingItem {
  document_id: string;
  study_id: string;
  site_id?: string | null;
  filename: string;
  status: string;
  intelligence_report: ETMF_DocumentIntelligenceReport;
  staged_at: string;
  staged_by: string;
  qc_assigned_to?: string | null;
  discrepancy_notes?: string[];
}

export type ETMF_ClassificationConfidenceTier = "HIGH" | "MEDIUM" | "LOW";

export interface ETMF_CompletenessResponse {
  study_id: string;
  site_id?: string | null;
  milestone: string;
  is_complete: boolean;
  scope: string;
  present_artifacts: string[];
  missing_artifacts: string[];
  per_artifact_detail: ETMF_ArtifactDetail[];
}

export type ETMF_ComplianceProfile = "HIPAA" | "GDPR" | "EU_CTR";

export interface ETMF_DocumentExpirationUpdate {
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface ETMF_DocumentIntelligenceAnalyzeRequest {
  content: string;
  filename: string;
  mime_type?: string;
  study_id?: string | null;
  site_id?: string | null;
  artifact_hint?: string | null;
  free_text?: string | null;
  taxonomy_version?: string | null;
  document_id?: string | null;
}

export interface ETMF_DocumentIntelligenceClassifyRequest {
  content?: string | null;
  filename: string;
  artifact_hint?: string | null;
  free_text?: string | null;
  taxonomy_version?: string | null;
}

export interface ETMF_DocumentIntelligenceMetadataRequest {
  content: string;
  filename?: string;
  study_id?: string | null;
  site_id?: string | null;
}

export interface ETMF_DocumentIntelligenceReport {
  document_id?: string | null;
  filename: string;
  mime_type: string;
  sha256_hash: string;
  modality?: ETMF_DocumentModality;
  primary_classification: ETMF_TaxonomyMatchCandidate;
  alternative_candidates?: ETMF_TaxonomyMatchCandidate[];
  confidence_tier?: ETMF_ClassificationConfidenceTier;
  qc_recommendation?: ETMF_QCRecommendation;
  extracted_metadata?: ETMF_ExtractedRegulatoryMetadata;
  signature_analysis?: ETMF_SignatureAnalysisResult;
  ai_generation_manifest?: Record<string, any>;
  eisf_target_mapping?: Record<string, any> | null;
}

export interface ETMF_DocumentIntelligenceSignatureRequest {
  content: string;
  artifact_code: string;
  filename?: string;
}

export type ETMF_DocumentModality =
  "TEXT" | "PDF_BINARY" | "SCANNED_IMAGE" | "STRUCTURED_FORM";

export interface ETMF_DocumentResponse {
  id: string;
  study_id: string;
  site_id?: string | null;
  zone: number;
  section: string;
  artifact_type: string;
  filename: string;
  mime_type: string;
  created_at: string;
  created_by: string;
  version_index: number;
  status: string;
  taxonomy_version: string;
  artifact_code: string;
  metadata_json?: Record<string, any> | null;
  document_type?: string | null;
  approval_status?: string;
  signature_manifestation?: Record<string, any> | null;
  signer?: string | null;
  signing_timestamp?: string | null;
  is_redacted?: boolean;
  redaction_source_id?: string | null;
  redaction_manifest_json?: Record<string, any> | null;
  reason_for_change?: string | null;
  protocol_version?: ETMF_ProtocolVersionRefDTO | null;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string | null;
  sync_status?: string | null;
}

export interface ETMF_DocumentVersionEntry {
  id: string;
  version_index: number;
  status: string;
  approval_status: string;
  created_at: string;
  created_by: string;
  filename: string;
  artifact_code: string;
  signer?: string | null;
  signing_timestamp?: string | null;
  transitions: ETMF_TransitionResponse[];
}

export interface ETMF_DocumentVersionsResponse {
  study_id: string;
  artifact_code: string;
  versions: ETMF_DocumentVersionEntry[];
}

export interface ETMF_ExpectedDocumentCreate {
  study_id: string;
  site_id?: string | null;
  milestone: string;
  artifact_type: string;
  zone?: number | null;
  section?: string | null;
  metadata_json?: Record<string, any> | null;
  reason_for_change: string;
}

export interface ETMF_ExpectedDocumentResponse {
  id: string;
  study_id: string;
  site_id?: string | null;
  milestone: string;
  artifact_type: string;
  zone?: number | null;
  section?: string | null;
  metadata_json?: Record<string, any> | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface ETMF_ExtractedRegulatoryMetadata {
  protocol_number?: string | null;
  study_id?: string | null;
  site_id?: string | null;
  investigator_name?: string | null;
  issue_date?: string | null;
  expiration_date?: string | null;
  form_identifier?: string | null;
  version_tag?: string | null;
  raw_extracted_fields?: Record<string, any>;
  phi_pii_detected?: boolean;
}

export interface ETMF_ExtractedSignature {
  signer_name?: string | null;
  signer_role?: string | null;
  signature_date?: string | string | null;
  confidence?: number;
  location_hint?: string | null;
  is_digital_signature?: boolean;
  signature_type?: string;
}

export interface ETMF_HTTPValidationError {
  detail?: ETMF_ValidationError[];
}

export interface ETMF_IngestionRequest {
  study_id: string;
  site_id?: string | null;
  idempotency_key?: string | null;
  artifact_type: string;
  filename: string;
  content: string;
  mime_type: string;
  zone?: number | null;
  section?: string | null;
  artifact_code?: string | null;
  taxonomy_version?: string | null;
  metadata_json?: Record<string, any> | null;
  protocol_version?: ETMF_ProtocolVersionRefDTO | null;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string | null;
}

export interface ETMF_InspectionReadinessResponse {
  study_id: string;
  generated_at: string;
  overall_readiness_score: number;
  readiness_rating: string;
  total_documents: number;
  total_expected: number;
  approved_documents_count: number;
  pending_qc_count: number;
  unsigned_documents_count: number;
  expired_documents_count: number;
  expiring_soon_count: number;
  milestones: ETMF_MilestoneReadinessDetail[];
  zones: ETMF_ZoneReadinessDetail[];
  action_items: string[];
}

export interface ETMF_ManualRedactRequest {
  spans?: ETMF_SpanItem[] | null;
  terms?: string[] | null;
  redacted_filename?: string | null;
}

export interface ETMF_ManualRedactResponse {
  status?: string;
  document_id: string;
  version_index: number;
  filename: string;
  categories_counts: Record<string, any>;
  manifest: Record<string, any>;
}

export interface ETMF_MilestoneReadinessDetail {
  milestone: string;
  is_complete: boolean;
  expected_count: number;
  present_count: number;
  approved_count: number;
  missing_artifacts: string[];
  completeness_percentage: number;
}

export interface ETMF_PaginatedAuditLogResponse {
  items: ETMF_AuditLogResponse[];
  total_count: number;
  limit: number;
  offset: number;
  next_page?: string | null;
  next_cursor?: string | null;
  has_more: boolean;
}

export interface ETMF_ProtocolVersionRefDTO {
  study_id: string;
  version_tag: string;
  version_index: number;
  status: ETMF_ProtocolVersionStatusDTO;
}

export type ETMF_ProtocolVersionStatusDTO =
  "DRAFT" | "ACTIVE" | "LOCKED" | "PUBLISHED" | "ARCHIVED" | "FROZEN";

export type ETMF_QCRecommendation =
  "AUTO_CLASSIFY" | "FLAG_FOR_QC_REVIEW" | "MANUAL_RECLASSIFICATION_REQUIRED";

export interface ETMF_RedactRequest {
  redacted_content: string;
  redacted_filename?: string | null;
  manifest: Record<string, any>;
}

export interface ETMF_SeedEDLRequest {
  milestones?: string[] | null;
  reason_for_change?: string;
}

export interface ETMF_SignDocumentRequest {
  signing_reason: ETMF_SigningReason;
}

export interface ETMF_SignatureAnalysisResult {
  status?: ETMF_SignaturePresenceStatus;
  extracted_signatures?: ETMF_ExtractedSignature[];
  missing_required_signatures?: string[];
  signature_blocks_detected?: number;
  details?: string;
}

export type ETMF_SignaturePresenceStatus =
  "FULLY_SIGNED" | "PARTIALLY_SIGNED" | "UNSIGNED" | "SIGNATURE_NOT_REQUIRED";

export interface ETMF_SignatureVerificationResponse {
  document_id: string;
  version_index: number;
  is_valid: boolean;
  signer: string | null;
  signing_timestamp: string | null;
  signing_reason: string | null;
  certificate_fingerprint: string | null;
  content_hash_matched: boolean;
  details: string;
}

export type ETMF_SigningReason =
  | "AUTHOR"
  | "REVIEW"
  | "APPROVAL"
  | "SPONSOR_APPROVAL"
  | "INVESTIGATOR_SIGNATURE"
  | "TECHNICAL_QC"
  | "CLINICAL_QC"
  | "DATA_LOCK"
  | "SYSTEM_SEAL"
  | "PROTOCOL_APPROVAL"
  | "REGULATORY_FORM_SIGNATURE"
  | "TRAINING_ACKNOWLEDGEMENT"
  | "SITE_VISIT_SIGN_OFF"
  | "ADJUDICATION_SIGN_OFF"
  | "ENDPOINT_ADJUDICATION";

export interface ETMF_SpanItem {
  start: number;
  end: number;
  label?: string | null;
}

export interface ETMF_StageDocumentQCRequest {
  content: string;
  filename: string;
  mime_type?: string;
  study_id: string;
  site_id?: string | null;
  artifact_hint?: string | null;
  taxonomy_version?: string | null;
  reason_for_change: string;
  assigned_cra?: string | null;
}

export interface ETMF_StudyArchiveItemResult {
  document_id: string;
  filename: string;
  from_status: string;
  to_status: string;
  status: string;
  error_message?: string | null;
}

export interface ETMF_StudyArchiveRequest {
  reason_for_change: string;
  all_or_nothing?: boolean;
}

export interface ETMF_StudyArchiveResponse {
  status: string;
  study_id: string;
  total_processed: number;
  successful_count: number;
  failed_count: number;
  skipped_count: number;
  results: ETMF_StudyArchiveItemResult[];
}

export interface ETMF_TaxonomyArtifactNode {
  artifact_code: string;
  artifact_name: string;
}

export interface ETMF_TaxonomyCatalogResponse {
  version: string;
  zones: ETMF_TaxonomyZoneNode[];
}

export interface ETMF_TaxonomyMatchCandidate {
  zone_code: number;
  zone_name: string;
  section_code: string;
  section_name: string;
  artifact_code: string;
  artifact_name: string;
  confidence: number;
  matched_signals?: string[];
  is_extension?: boolean;
}

export interface ETMF_TaxonomySectionNode {
  section_code: string;
  section_name: string;
  artifacts: ETMF_TaxonomyArtifactNode[];
}

export interface ETMF_TaxonomyZoneNode {
  zone_code: number;
  zone_name: string;
  sections: ETMF_TaxonomySectionNode[];
}

export interface ETMF_TransitionRequest {
  to_status: string;
  reason_for_change: string;
}

export interface ETMF_TransitionResponse {
  id: string;
  document_id: string;
  from_status: string;
  to_status: string;
  actor_id: string;
  actor_role: string;
  reason_for_change: string;
  timestamp: string;
}

export interface ETMF_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface ETMF_ZoneReadinessDetail {
  zone_code: number;
  zone_name: string;
  expected_count: number;
  present_count: number;
  approved_count: number;
  pending_qc_count: number;
  rejected_count: number;
  missing_count: number;
  completeness_percentage: number;
}

export type Execution_ActionTakenDevice =
  | "QUARANTINED_AT_SITE"
  | "RETURNED_TO_SPONSOR"
  | "REPLACED"
  | "DISPOSED"
  | "REPAIRED"
  | "NONE";

export interface Execution_AdaptiveTrialConfigDTO {
  study_id: string;
  design_type: string;
  dose_levels?: Execution_DoseLevelDef[];
  boin_config?: Execution_BoinConfig | null;
  crm_config?: Execution_CrmConfig | null;
  three_plus_three_config?: Execution_ThreePlusThreeConfig | null;
  rar_config?: Execution_RarConfig | null;
  cohort_size?: number;
  max_sample_size?: number | null;
  is_active?: boolean;
}

export interface Execution_AddDOAAssignmentRequest {
  study_id: string;
  site_id: string;
  personnel_name: string;
  personnel_email: string;
  role: Execution_DOATaskRoleEnum;
  delegated_tasks: Execution_DOATaskDelegationEnum[];
  start_date: string;
}

export type Execution_AdjudicationActionEnum = "APPROVE" | "REJECT";

export type Execution_AdjudicationClassification =
  | "CONFIRMED_MI"
  | "CONFIRMED_ISCHEMIC_STROKE"
  | "CONFIRMED_HEMORRHAGIC_STROKE"
  | "CONFIRMED_CV_DEATH"
  | "CONFIRMED_NON_CV_DEATH"
  | "CONFIRMED_HEART_FAILURE_HOSPITALIZATION"
  | "CONFIRMED_STENT_THROMBOSIS"
  | "CONFIRMED_VTE"
  | "CONFIRMED_DISEASE_PROGRESSION"
  | "NOT_AN_ENDPOINT"
  | "INDETERMINATE"
  | "INSUFFICIENT_DATA";

export interface Execution_AdjudicationRequest {
  action: Execution_AdjudicationActionEnum;
  reason: string;
  updated_message?: string | null;
}

export interface Execution_AdjudicationResponse {
  query_id: string;
  new_status: string;
  action: string;
  message: string;
  adjudicated_by: string;
  adjudicated_at: string;
}

export type Execution_AdjudicationWorkflowStatus =
  | "PENDING_PRIMARY_REVIEWS"
  | "READER_1_COMPLETED"
  | "READER_2_COMPLETED"
  | "CONSENSUS_REACHED"
  | "DISCORDANCE_DETECTED"
  | "ESCALATED_TO_CHAIR"
  | "FINALIZED_CONCORDANT"
  | "FINALIZED_BY_CHAIR"
  | "FINALIZED_BY_COMMITTEE";

export interface Execution_AnomalyCandidateItem {
  query_id: string;
  study_id: string;
  subject_id: string;
  site_id?: string | null;
  visit_id?: string | null;
  domain?: string | null;
  test_code: string;
  observation_id?: string | null;
  field_link?: string | null;
  rule_id?: string | null;
  message?: string | null;
  explanation?: string | null;
  origin?: string | null;
  priority?: string | null;
  status?: string;
  created_at: string;
  created_by?: string | null;
}

export interface Execution_AnomalyEvaluateRequest {
  subject_id: string;
  study_id: string;
  enable_ai?: boolean;
  auto_stage_queries?: boolean;
}

export interface Execution_AnomalyEvaluateResponse {
  subject_id: string;
  study_id: string;
  anomalies: Execution_CrossDomainAnomaly[];
  evaluated_at: string;
  queries_staged_count: number;
}

export type Execution_AnomalySeverity = "HIGH" | "MEDIUM" | "LOW";

export interface Execution_ApproveDelegationRequest {
  delegation_id: string;
  pi_user_id: string;
  password: string;
  totp_code?: string | null;
}

export interface Execution_ApproveTaskDelegationRequest {
  delegation_id: string;
  pi_user_id: string;
  signature_hash: string;
  reason_for_change: string;
}

export interface Execution_BatchAssignItem {
  assignment_id: string;
  action?: string;
  code?: string | null;
  term?: string | null;
  suggestion_index?: number | null;
  reason_for_change?: string | null;
}

export interface Execution_BatchAssignRequest {
  assignment_ids?: string[] | null;
  items?: Execution_BatchAssignItem[] | null;
  code?: string | null;
  term?: string | null;
  dictionary_type?: string | null;
  dictionary_version?: string | null;
  reason?: string | null;
  reason_for_change?: string | null;
  action?: string;
}

export interface Execution_BatchAssignResponse {
  success_count: number;
  failed_count: number;
  results?: Record<string, any>[];
}

export interface Execution_BatchSignOffRequest {
  study_id: string;
  target_type: string;
  target_ids: string[];
  signing_reason: string;
}

export interface Execution_BatchSignOffResponse {
  status: string;
  approved_submission_ids: string[];
  skipped_submission_ids: string[];
  skipped_targets: string[];
}

export interface Execution_BatchSignatureRequest {
  study_id: string;
  subject_id: string;
  target_type?: string;
  target_ids?: string[];
  target_form_ids?: string[];
  signing_reason: string;
  password: string;
  printed_name: string;
}

export interface Execution_BatchSignatureResponse {
  signature_id: string;
  study_id: string;
  subject_id: string;
  signed_forms_count: number;
  content_digest: string;
  timestamp_utc: string;
  audit_tx: string;
}

export type Execution_BindingDeterminationSource =
  | "CONCORDANT_CONSENSUS"
  | "CHAIR_UPHELD_READER_1"
  | "CHAIR_UPHELD_READER_2"
  | "CHAIR_CUSTOM_DETERMINATION"
  | "COMMITTEE_QUORUM_VOTE";

export interface Execution_Body_import_dictionary_api_v1_dictionaries_import_post {
  dictionary_type: Execution_DictTypeEnum;
  version: string;
  files: string;
  parse_multilingual?: boolean;
}

export interface Execution_Body_upload_document_api_v1_documents_upload_post {
  file: string;
  dia_tmf_code: string;
  reason_for_change: string;
}

export interface Execution_BoinBoundariesCalculationRequest {
  target_toxicity_rate?: number;
  factor_e?: number;
  factor_d?: number;
}

export interface Execution_BoinBoundariesCalculationResponse {
  target_toxicity_rate: number;
  phi_1: number;
  phi_2: number;
  lambda_e: number;
  lambda_d: number;
  description: string;
}

export interface Execution_BoinConfig {
  target_toxicity_rate?: number;
  factor_e?: number;
  factor_d?: number;
  safety_cutoff?: number;
  prior_alpha?: number;
  prior_beta?: number;
  min_subjects_for_elimination?: number;
}

export interface Execution_BulkQueryGenerationRequest {
  study_id?: string | null;
  site_id?: string | null;
  subject_id?: string | null;
  targets: Execution_QueryTargetDescriptor[];
  reason_for_change: string;
}

export interface Execution_BulkQueryGenerationResponse {
  batch_id?: string | null;
  audit_tx?: string | null;
  generated_count?: number | null;
  generated_query_ids: string[];
  skipped_targets?: Execution_QueryTargetDescriptor[];
  timestamp_utc?: string | null;
}

export interface Execution_BulkSdvSignOffRequest {
  study_id: string;
  subject_id: string;
  scope: string;
  target_ids: string[];
  reason_for_change: string;
  site_id?: string | null;
  signing_reason?: string;
}

export interface Execution_BulkSdvSignOffResponse {
  bulk_id?: string | null;
  content_digest: string;
  timestamp_utc: string;
  audit_tx: string;
  verified_count?: number | null;
  verified_target_ids?: string[] | null;
  skipped_targets?: Record<string, any>[] | null;
  signed_count: number;
  signed_target_ids: string[];
  skipped_target_ids: string[];
}

export interface Execution_BulkSubjectReconsentRequest {
  subject_ids: string[];
  study_id: string;
  protocol_version: string;
  version_index?: number;
  icf_signed?: boolean;
  signature_type?: string;
  signer_name?: string;
  reason_for_change?: string;
}

export interface Execution_BundledEcrfForm {
  form_id: string;
  form_name: string;
  visit_name: string;
  submission_timestamp: string;
  form_data?: Record<string, any>;
}

export interface Execution_CGTApheresisCreateRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  collector_id: string;
  collector_name: string;
  collector_role?: string;
  initial_temp_celsius?: number;
  din?: string | null;
  dry_shipper_id?: string | null;
  seal_id?: string | null;
  notes?: string | null;
}

export interface Execution_CGTBarcodeVerifyRequest {
  barcode_payload: string;
  expected_subject_id?: string | null;
  expected_din?: string | null;
  expected_batch_id?: string | null;
}

export interface Execution_CGTBarcodeVerifyResponse {
  is_valid: boolean;
  failure_reason?: string | null;
  barcode_id?: string | null;
  study_id?: string | null;
  site_id?: string | null;
  subject_id?: string | null;
  din?: string | null;
  batch_id?: string | null;
  crypto_hash?: string | null;
}

export interface Execution_CGTBedsideVerificationRequest {
  coc_id: string;
  scanned_wristband_subject_id: string;
  scanned_bag_barcode: string;
  primary_verifier_id: string;
  primary_verifier_name: string;
  primary_verifier_role: string;
  primary_signature_token: string;
  secondary_verifier_id: string;
  secondary_verifier_name: string;
  secondary_verifier_role: string;
  secondary_signature_token: string;
  notes?: string | null;
}

export interface Execution_CGTBedsideVerificationResponse {
  id: string;
  study_id: string;
  site_id: string;
  chain_of_custody_id: string;
  subject_id: string;
  din: string;
  batch_id: string;
  wristband_matched: boolean;
  bag_matched: boolean;
  temperature_chain_verified: boolean;
  primary_verifier_id: string;
  primary_verifier_name: string;
  primary_verifier_role: string;
  primary_signed_at: string;
  secondary_verifier_id: string;
  secondary_verifier_name: string;
  secondary_verifier_role: string;
  secondary_signed_at: string;
  status: string;
  unlock_token: string;
  verification_notes?: string | null;
}

export interface Execution_CGTChainOfCustodyResponse {
  id: string;
  study_id: string;
  subject_id: string;
  din: string;
  batch_id?: string | null;
  current_stage: string;
  status: string;
  tamper_evident_barcode?: string | null;
  barcode_signature?: string | null;
  collection_site_id?: string | null;
  collection_timestamp?: string | null;
  manufacturing_facility_id?: string | null;
  cryo_temp_excursion_count?: number;
  lowest_temp_celsius?: number | null;
  highest_temp_celsius?: number | null;
  infusion_unlocked?: boolean;
  infused_at?: string | null;
  reconciled_at?: string | null;
}

export interface Execution_CGTDestructionCertificateCreateRequest {
  study_id: string;
  site_id: string;
  lot_number: string;
  kit_numbers: string[];
  vial_identifiers: string[];
  destruction_reason: string;
  destruction_method?: string;
  destruction_facility: string;
  witness_1_id: string;
  witness_1_name: string;
  witness_1_role: string;
  witness_1_signature_token: string;
  witness_2_id: string;
  witness_2_name: string;
  witness_2_role: string;
  witness_2_signature_token: string;
  quantity_destroyed?: number;
  residual_volume_ml?: number | null;
  subject_id?: string | null;
  din?: string | null;
  coc_id?: string | null;
  notes?: string | null;
}

export interface Execution_CGTDestructionCertificateResponse {
  id: string;
  study_id: string;
  site_id: string;
  certificate_number: string;
  chain_of_custody_id?: string | null;
  subject_id?: string | null;
  din?: string | null;
  lot_number: string;
  kit_numbers: string[];
  vial_identifiers: string[];
  quantity_destroyed: number;
  unit_of_measure: string;
  residual_volume_ml?: number | null;
  destruction_reason: string;
  destruction_method: string;
  destruction_facility: string;
  destruction_timestamp: string;
  witness_1_id: string;
  witness_1_name: string;
  witness_1_role: string;
  witness_1_signed_at: string;
  witness_2_id: string;
  witness_2_name: string;
  witness_2_role: string;
  witness_2_signed_at: string;
  cryptographic_seal: string;
  status: string;
  reconciliation_notes?: string | null;
}

export interface Execution_CGTFullTraceabilityResponse {
  chain_of_custody: Execution_CGTChainOfCustodyResponse;
  milestones: Execution_CGTMilestoneResponse[];
  bedside_verification?: Execution_CGTBedsideVerificationResponse | null;
  destruction_certificate?: Execution_CGTDestructionCertificateResponse | null;
}

export interface Execution_CGTInfusionExecuteRequest {
  coc_id: string;
  unlock_token: string;
  administrator_id: string;
  administrator_name: string;
  administrator_role?: string;
  volume_infused_ml?: number;
  vital_signs_pre_temp_celsius?: number;
  vital_signs_post_temp_celsius?: number;
  notes?: string | null;
}

export interface Execution_CGTManufacturingBindRequest {
  coc_id: string;
  batch_id: string;
  manufacturing_facility_id: string;
  operator_id: string;
  operator_name: string;
  operator_role?: string;
  temperature_celsius?: number;
  notes?: string | null;
}

export interface Execution_CGTMilestoneCreateRequest {
  coc_id: string;
  stage: string;
  location_name: string;
  temperature_celsius: number;
  custodian_id: string;
  custodian_name: string;
  custodian_role: string;
  facility_id?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  carrier?: string | null;
  tracking_number?: string | null;
  seal_intact?: boolean;
  seal_id?: string | null;
  dry_shipper_id?: string | null;
  signature_token?: string | null;
  notes?: string | null;
}

export interface Execution_CGTMilestoneResponse {
  id: string;
  study_id: string;
  chain_of_custody_id: string;
  stage: string;
  milestone_type: string;
  timestamp: string;
  location_name: string;
  facility_id?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  temperature_celsius: number;
  temperature_status: string;
  custodian_id: string;
  custodian_name: string;
  custodian_role: string;
  carrier?: string | null;
  tracking_number?: string | null;
  seal_intact: boolean;
  seal_id?: string | null;
  dry_shipper_id?: string | null;
  electronic_signature?: Record<string, any> | null;
  notes?: string | null;
}

export interface Execution_CGTVialReturnReconciliationRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  kit_number: string;
  vial_identifier: string;
  lot_number: string;
  expected_quantity?: number;
  returned_quantity?: number;
  intact_vials?: number;
  used_vials?: number;
  residual_volume_ml?: number;
  din?: string | null;
  logged_by: string;
  disposition?: string;
}

export interface Execution_CGTVialReturnReconciliationResponse {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  kit_number: string;
  vial_identifier: string;
  lot_number: string;
  din?: string | null;
  expected_quantity: number;
  returned_quantity: number;
  intact_vials: number;
  used_vials: number;
  residual_volume_ml: number;
  adherence_percentage: number;
  disposition: string;
  logged_by: string;
  logged_at: string;
  certificate_id?: string | null;
}

export interface Execution_CecCharterConfig {
  charter_id: string;
  study_id: string;
  committee_name: string;
  version?: string;
  voting_rule?: Execution_VotingRule;
  minimum_quorum_percentage?: number;
  minimum_members_count?: number;
  eligible_members?: Record<string, any>[];
  endpoint_definitions?: string[];
  diagnostic_criteria_templates?: Record<string, any>;
  is_active?: boolean;
  created_at?: string;
  created_by?: string;
  version_index?: number;
}

export interface Execution_CecDossierSummaryItem {
  dossier_id: string;
  event_id: string;
  study_id: string;
  masked_subject_id: string;
  candidate_event_type: Execution_EndpointCategory;
  reported_term: string;
  reported_onset_date: string;
  workflow_status: Execution_AdjudicationWorkflowStatus;
  is_discordant: boolean;
  final_consensus_classification?: Execution_AdjudicationClassification | null;
  reader_1_status: string;
  reader_2_status: string;
  created_at: string;
}

export type Execution_CertificationStatusEnum =
  "ACTIVE" | "EXPIRED" | "SUSPENDED" | "PENDING_RENEWAL";

export interface Execution_ChairAdjudicateDTO {
  final_classification: Execution_AdjudicationClassification;
  final_subtype?: string | null;
  final_adjudicated_onset_date: string;
  binding_reader?: Execution_BindingDeterminationSource;
  criteria_confirmed?: string[];
  adjudication_rationale: string;
  signature_token?: string | null;
  signer_name?: string;
  password?: string;
  signature_meaning?: string;
}

export interface Execution_ChairAdjudicationDetermination {
  determination_id: string;
  dossier_id: string;
  chair_id: string;
  chair_name: string;
  final_classification: Execution_AdjudicationClassification;
  final_subtype?: string | null;
  final_adjudicated_onset_date: string;
  binding_reader?: Execution_BindingDeterminationSource;
  criteria_confirmed?: string[];
  adjudication_rationale: string;
  electronic_signature: Record<string, any>;
  finalized_at?: string;
}

export interface Execution_CharterCreateDTO {
  study_id: string;
  committee_name: string;
  version?: string;
  voting_rule?: Execution_VotingRule;
  minimum_quorum_percentage?: number;
  minimum_members_count?: number;
  eligible_members?: Record<string, any>[];
  endpoint_definitions?: string[];
  diagnostic_criteria_templates?: Record<string, any>;
}

export interface Execution_ClinicalQueryResponse {
  id: string;
  study_id: string;
  subject_id: string;
  visit_id?: string | null;
  domain?: string | null;
  test_code: string;
  status: string;
  explanation?: string | null;
  response?: string | null;
  created_at: string;
  updated_at: string;
  history?: Execution_QueryHistoryItem[];
  observation_id?: string | null;
  field_link?: string | null;
  message?: string | null;
  origin?: string | null;
  priority?: string | null;
  rule_id?: string | null;
  created_by?: string | null;
  responder?: string | null;
  resolver?: string | null;
  resolved_at?: string | null;
  cancellation_reason?: string | null;
  escalated_at?: string | null;
  form_id?: string | null;
  field_id?: string | null;
  query_type?: string | null;
  action_required?: string | null;
}

export interface Execution_CoderActionRequest {
  action: string;
  code?: string | null;
  term?: string | null;
  suggestion_index?: number | null;
  reason_for_change?: string | null;
}

export interface Execution_CodingAssignmentResponse {
  id: string;
  verbatim_text: string;
  source_field?: string | null;
  observation_id?: string | null;
  dictionary_type: string;
  dictionary_version: string;
  coded_code?: string | null;
  coded_term?: string | null;
  status: string;
  recoding_status: string;
  assigned_by?: string | null;
  assigned_at: string;
  score?: number | null;
  hierarchy?: Record<string, any> | any[] | null;
  suggestions?: any[] | Record<string, any> | null;
  domain?: string | null;
  version: number;
  is_deleted: boolean;
}

export interface Execution_CohortEvaluationInput {
  study_id: string;
  cohort_number: number;
  dose_level_index: number;
  subjects_evaluated: number;
  dlts_observed: number;
  subject_ids?: string[];
  dlt_subject_ids?: string[];
}

export interface Execution_CohortEvaluationResult {
  study_id: string;
  cohort_number: number;
  evaluated_dose_level_index: number;
  evaluated_dose_name: string;
  subjects_in_cohort: number;
  dlts_in_cohort: number;
  recommendation_action: string;
  recommended_dose_level_index?: number | null;
  recommended_dose_name?: string | null;
  lambda_e?: number | null;
  lambda_d?: number | null;
  target_toxicity_rate?: number | null;
  overdose_probability?: number | null;
  is_safety_stop_triggered?: boolean;
  is_mtd_declared?: boolean;
  mtd_dose_level_index?: number | null;
  mtd_dose_name?: string | null;
  dose_summaries?: Execution_DoseLevelEvaluationSummary[];
  rationale: string;
}

export type Execution_CohortRecommendationAction =
  | "ESCALATE"
  | "DE_ESCALATE"
  | "STAY"
  | "EXPAND_COHORT"
  | "STOP_SAFETY"
  | "STOP_SUCCESS"
  | "MTD_FOUND";

export interface Execution_CohortTransitionLogResponse {
  id: string;
  study_id: string;
  cohort_number: number;
  from_dose_level: number;
  to_dose_level: number;
  action_taken: string;
  algorithm_recommendation: string;
  override_occurred: boolean;
  override_reason?: string | null;
  evaluated_dlt_rate?: number | null;
  posterior_toxicity_estimates?: Record<string, any> | null;
  authorized_by: string;
  transitioned_at: string;
  signature_token?: string | null;
  cryptographic_checksum?: string | null;
}

export interface Execution_CohortTransitionRequest {
  study_id: string;
  cohort_number: number;
  decision_action: string;
  target_dose_level_index: number;
  override_reason?: string | null;
  electronic_signature?: string | null;
}

export interface Execution_ComplianceStatusResponse {
  study_id: string;
  site_id: string | null;
  milestone: string;
  is_complete: boolean;
  missing_documents: string[];
}

export interface Execution_CriterionLevelResult {
  criterion_id: string;
  criterion_type: string;
  description: string;
  dsl_source: string;
  is_met: boolean;
  is_indeterminate: boolean;
}

export interface Execution_CrmConfig {
  target_toxicity_rate?: number;
  skeleton_probs: number[];
  model_type?: string;
  prior_std?: number;
  safety_cutoff?: number;
  allow_dose_skipping?: boolean;
}

export interface Execution_CrmPosteriorCalculationRequest {
  skeleton_probs: number[];
  target_toxicity_rate?: number;
  prior_std?: number;
  dose_evaluations: Record<string, any>[];
  current_dose_index?: number;
}

export interface Execution_CrmPosteriorCalculationResponse {
  target_toxicity_rate: number;
  current_dose_index: number;
  recommended_dose_index: number;
  recommendation_action: Execution_CohortRecommendationAction;
  posterior_mean_probabilities: number[];
  overdose_probability_dose_1: number;
  is_safety_stop_triggered: boolean;
  rationale: string;
}

export interface Execution_CrossDomainAnomaly {
  anomaly_id?: string;
  anomaly_type: Execution_CrossDomainAnomalyType;
  study_id: string;
  subject_id: string;
  site_id?: string | null;
  visit_id?: string | null;
  primary_domain: string;
  primary_test_code: string;
  correlated_domain: string;
  correlated_test_code?: string | null;
  severity?: Execution_AnomalySeverity;
  message: string;
  explanation: string;
  confidence_score?: number;
  model_identifier?: string | null;
  prompt_hash?: string | null;
  observation_ids?: string[];
  form_id?: string | null;
  field_id?: string | null;
}

export type Execution_CrossDomainAnomalyType =
  | "AE_WITHOUT_CONCOMITANT_MED"
  | "CONCOMITANT_MED_WITHOUT_AE"
  | "MARKED_LAB_ABNORMALITY_WITHOUT_AE"
  | "CRITICAL_VITALS_WITHOUT_AE"
  | "VITALS_INCONSISTENT_WITH_HISTORY"
  | "DRUG_DISCONTINUATION_WITHOUT_AE"
  | "TEMPORAL_SEQUENCE_MISMATCH"
  | "AI_CONTEXTUAL_INCONSISTENCY";

export type Execution_CustodianEnum = "Lead Unblinded Statistician" | "IDMC";

export interface Execution_CustodianShare {
  custodian: Execution_CustodianEnum;
  version: number;
  x: number;
  y: number;
}

export interface Execution_DOAAssignmentRecord {
  record_id: string;
  study_id: string;
  site_id: string;
  personnel_name: string;
  personnel_email: string;
  role: Execution_DOATaskRoleEnum;
  delegated_tasks: Execution_DOATaskDelegationEnum[];
  start_date: string;
  end_date?: string | null;
  is_active?: boolean;
  signed_off?: boolean;
}

export interface Execution_DOAAuditLogResponse {
  id: string;
  user_id: string;
  action: string;
  details: string;
  timestamp: string;
}

export interface Execution_DOADelegationRecordResponse {
  id: string;
  site_id: string;
  staff_user_id: string;
  task_code: string;
  pi_user_id?: string | null;
  status: string;
  pi_signature_hash?: string | null;
  pi_approved_at?: string | null;
  end_date?: string | null;
  reason_for_change?: string | null;
  is_active: boolean;
}

export interface Execution_DOASignOffRequest {
  record_id: string;
  reason_for_change: string;
}

export type Execution_DOATaskDelegationEnum =
  | "SUBJECT_INFORMED_CONSENT"
  | "PHYSICAL_EXAMINATION"
  | "AE_SAE_REPORTING"
  | "CRF_DATA_ENTRY"
  | "PI_CASEBOOK_SIGNOFF";

export type Execution_DOATaskRoleEnum =
  | "PRINCIPAL_INVESTIGATOR"
  | "SUB_INVESTIGATOR"
  | "CLINICAL_RESEARCH_COORDINATOR"
  | "STUDY_NURSE"
  | "DATA_MANAGER";

export interface Execution_DailyDemandProjection {
  day_index: number;
  date: string;
  scheduled_dispensations: number;
  projected_new_subject_dispensations: number;
  total_daily_demand: number;
  cumulative_demand: number;
  projected_closing_balance: number;
}

export interface Execution_DataLockRecord {
  lock_id: string;
  study_id?: string | null;
  site_id?: string | null;
  subject_id?: string | null;
  visit_id?: string | null;
  form_id?: string | null;
  item_group_id?: string | null;
  field_name?: string | null;
  scope?: Execution_LockScopeEnum | string;
  scope_type?: string | null;
  scope_id?: string | null;
  status?: Execution_LockStatusEnum | string;
  lock_type?: string | null;
  is_active?: boolean;
  locked_by: string;
  created_by?: string | null;
  reason_for_change: string;
  locked_at: string;
  created_at?: string | null;
  unlocked_by?: string | null;
  unlocked_at?: string | null;
  unlock_justification?: string | null;
  signature_token?: string | null;
}

export interface Execution_DataLockRequest {
  study_id?: string | null;
  site_id?: string | null;
  subject_id?: string | null;
  visit_id?: string | null;
  form_id?: string | null;
  item_group_id?: string | null;
  field_name?: string | null;
  scope?: Execution_LockScopeEnum | string | null;
  scope_type?: string | null;
  scope_id?: string | null;
  action?: string;
  lock_type?: string | null;
  reason_for_change?: string | null;
  reason?: string | null;
  justification?: string | null;
  lock_id?: string | null;
}

export interface Execution_DataLockResponse {
  lock_id: string;
  status: string;
  message: string;
  record?: Execution_DataLockRecord | Record<string, any> | null;
  scope_type?: string | null;
  scope_id?: string | null;
  lock_type?: string | null;
  is_active?: boolean;
  locked_at?: string | null;
  unlocked_at?: string | null;
}

export interface Execution_DelegateTaskRequest {
  site_id: string;
  staff_user_id: string;
  task_code: string;
  pi_user_id: string;
  reason_for_change: string;
}

export interface Execution_Demographics {
  name?: string | null;
  birthdate?: string | null;
  gender?: string | null;
  race?: string | null;
}

export type Execution_DeviceClassification =
  | "CLASS_I"
  | "CLASS_IIA"
  | "CLASS_IIB"
  | "CLASS_III"
  | "IVD_CLASS_A"
  | "IVD_CLASS_B"
  | "IVD_CLASS_C"
  | "IVD_CLASS_D";

export interface Execution_DeviceDeficiencyCreateRequest {
  study_id: string;
  site_id: string;
  subject_id?: string | null;
  device_inventory_id?: string | null;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  device_name?: string;
  deficiency_type?: Execution_DeviceDeficiencyType;
  deficiency_description: string;
  incident_date: string;
  awareness_date?: string;
  could_lead_to_sade?: boolean;
  patient_involved?: boolean;
  health_impact?: Execution_HealthImpact;
  action_taken_device?: Execution_ActionTakenDevice;
  device_returned_to_sponsor?: boolean;
  return_tracking_number?: string | null;
  quarantine_scope?: Execution_QuarantineScope;
  reason_for_change?: string;
  signature_token?: string | null;
}

export interface Execution_DeviceDeficiencyResponse {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string | null;
  device_inventory_id: string | null;
  udi_di: string;
  lot_number: string;
  serial_number: string | null;
  device_name: string;
  deficiency_type: string;
  deficiency_description: string;
  incident_date: string;
  awareness_date: string;
  could_lead_to_sade: boolean;
  patient_involved: boolean;
  health_impact: string;
  action_taken_device: string;
  device_returned_to_sponsor: boolean;
  return_tracking_number: string | null;
  root_cause_analysis: string | null;
  sponsor_evaluation: string | null;
  quarantine_triggered: boolean;
  quarantine_scope: string;
  safety_case_id: string | null;
  safety_classification: string | null;
  investigation_status: string;
  quarantine_record_ids?: string[];
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type Execution_DeviceDeficiencyType =
  | "MALFUNCTION"
  | "USABILITY_ERROR"
  | "INADEQUATE_LABELING"
  | "DESIGN_DEFECT"
  | "PACKAGING_BREACH"
  | "SOFTWARE_FAULT";

export interface Execution_DeviceDispensationRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  device_inventory_id?: string | null;
  serial_number?: string | null;
  scanned_barcode?: string | null;
  reason_for_change?: string;
}

export interface Execution_DeviceDispensationResponse {
  status?: string;
  message: string;
  device: Execution_DeviceInventoryResponse;
  dispensed_at: string;
}

export interface Execution_DeviceInventoryCreateRequest {
  study_id: string;
  site_id: string;
  device_identifier?: string | null;
  brand_name: string;
  device_model?: string | null;
  device_type?: Execution_DeviceType;
  device_classification?: Execution_DeviceClassification;
  raw_barcode?: string | null;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  expiration_date?: string | null;
  manufacturing_date?: string | null;
  software_version?: string | null;
  quantity?: number;
  reason_for_change?: string;
}

export interface Execution_DeviceInventoryResponse {
  id: string;
  study_id: string;
  site_id: string;
  device_identifier: string;
  brand_name: string;
  device_model: string | null;
  device_type: string;
  device_classification: string;
  udi_di: string;
  udi_pi: string | null;
  udi_carrier_format: string;
  lot_number: string;
  serial_number: string | null;
  expiration_date: string | null;
  manufacturing_date: string | null;
  software_version: string | null;
  status: string;
  is_dispensation_blocked: boolean;
  allocated_subject_id: string | null;
  dispensed_at: string | null;
  dispensed_by: string | null;
  quarantined_at: string | null;
  quarantined_by: string | null;
  quarantine_reason: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Execution_DeviceInvestigationSummary {
  study_id: string;
  total_devices?: number;
  available_devices?: number;
  dispensed_devices?: number;
  quarantined_devices?: number;
  total_deficiencies?: number;
  open_safety_cases?: number;
  sade_count?: number;
  active_quarantines?: number;
}

export interface Execution_DeviceQuarantineCreateRequest {
  study_id: string;
  site_id: string;
  scope?: Execution_QuarantineScope;
  device_inventory_id?: string | null;
  lot_number?: string | null;
  serial_number?: string | null;
  reason: string;
  reason_for_change?: string;
}

export interface Execution_DeviceQuarantineReleaseRequest {
  quarantine_id: string;
  release_justification: string;
  signature_token: string;
  reason_for_change?: string;
}

export interface Execution_DeviceQuarantineResponse {
  id: string;
  study_id: string;
  site_id: string;
  deficiency_id: string | null;
  device_inventory_id: string | null;
  lot_number: string;
  serial_number: string | null;
  scope: string;
  status: string;
  reason: string;
  quarantined_at: string;
  quarantined_by: string;
  released_at: string | null;
  released_by: string | null;
  release_justification: string | null;
  impacted_device_count?: number;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type Execution_DeviceType =
  "THERAPEUTIC" | "DIAGNOSTIC_IVD" | "IMPLANTABLE" | "SAMD" | "COMBINATION";

export interface Execution_DiagnosticAttachment {
  attachment_id: string;
  document_type: string;
  title: string;
  summary: string;
  key_findings?: string[];
  file_url?: string | null;
  content_hash?: string | null;
}

export type Execution_DictTypeEnum = "MEDDRA" | "WHODRUG" | "LOINC" | "SNOMED";

export interface Execution_DiscreteExcursionResponse {
  start_time: string;
  end_time: string;
  duration_hours: number;
  excursion_type: string;
  peak_or_nadir_temp_celsius: number;
  threshold_celsius: number;
}

export interface Execution_DispenseRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_id: string;
  kit_id: string;
  quantity?: number;
}

export interface Execution_DispenseResponse {
  status: string;
  message: string;
  resupply_triggered: boolean;
}

export interface Execution_DocumentMetadataResponse {
  document_id: string;
  filename: string;
  version_index: string;
  sha256_hash: string;
  dia_tmf_code: string;
  status: string;
  created_by: string;
  created_at: string;
}

export interface Execution_DocumentUploadResponse {
  document_id: string;
  filename: string;
  version_index: string;
  sha256_hash: string;
}

export interface Execution_DoseLevelDef {
  index: number;
  name: string;
  dose_value: number;
}

export interface Execution_DoseLevelEvaluationSummary {
  dose_level_index: number;
  dose_name: string;
  dose_value: number;
  total_enrolled: number;
  total_dlts: number;
  observed_dlt_rate: number;
  posterior_dlt_rate?: number | null;
  is_eliminated_for_safety?: boolean;
  overdose_probability?: number | null;
}

export interface Execution_DossierBlindedView {
  dossier_id: string;
  event_id: string;
  study_id: string;
  masked_site_id: string;
  masked_subject_id: string;
  candidate_event_type: Execution_EndpointCategory;
  reported_term: string;
  verbatim_description: string;
  reported_onset_date: string;
  medical_narrative: string;
  is_deidentified: boolean;
  bundled_forms: Execution_BundledEcrfForm[];
  lab_panels: Execution_LabPanelItem[];
  ecg_records: Execution_ECGRecord[];
  diagnostic_attachments: Execution_DiagnosticAttachment[];
  reader_role: Execution_ReaderRole;
  workflow_status: Execution_AdjudicationWorkflowStatus;
  my_assessment?: Execution_ReaderAssessment | null;
  peer_assessment_completed?: boolean;
  peer_assessment?: Execution_ReaderAssessment | null;
  chair_determination?: Execution_ChairAdjudicationDetermination | null;
  is_discordant?: boolean;
  discordance_reasons?: string[];
  final_consensus_classification?: Execution_AdjudicationClassification | null;
  final_adjudicated_onset_date?: string | null;
  dossier_digest?: string;
}

export interface Execution_DossierDetailResponseDTO {
  dossier_id: string;
  event_id: string;
  study_id: string;
  site_id: string;
  masked_site_id: string;
  subject_id: string;
  masked_subject_id: string;
  candidate_event_type: string;
  reported_term: string;
  verbatim_description: string;
  reported_onset_date: string;
  medical_narrative: string;
  is_deidentified: boolean;
  bundled_forms: Record<string, any>[];
  lab_panels: Record<string, any>[];
  ecg_records: Record<string, any>[];
  diagnostic_attachments: Record<string, any>[];
  redaction_summary: Record<string, any>;
  reader_1_id: string;
  reader_1_name: string;
  reader_1_status: string;
  reader_1_assessment: Record<string, any> | null;
  reader_2_id: string;
  reader_2_name: string;
  reader_2_status: string;
  reader_2_assessment: Record<string, any> | null;
  chair_id: string | null;
  chair_name: string | null;
  chair_determination: Record<string, any> | null;
  workflow_status: string;
  is_discordant: boolean;
  discordance_reasons: string[];
  final_consensus_classification: string | null;
  final_consensus_subtype: string | null;
  final_adjudicated_onset_date: string | null;
  binding_determination_source: string | null;
  adjudication_rationale: string | null;
  electronic_signature: Record<string, any> | null;
  edc_writeback_status: string;
  dossier_digest: string;
  created_at: string;
  version_index: number;
}

export interface Execution_DossierPackageCreateDTO {
  study_id: string;
  site_id: string;
  subject_id: string;
  event_id: string;
  candidate_event_type: Execution_EndpointCategory;
  reported_term: string;
  verbatim_description?: string;
  reported_onset_date: string;
  medical_narrative?: string;
  bundled_forms?: Execution_BundledEcrfForm[];
  lab_panels?: Execution_LabPanelItem[];
  ecg_records?: Execution_ECGRecord[];
  diagnostic_attachments?: Execution_DiagnosticAttachment[];
  reader_1_id?: string;
  reader_1_name?: string;
  reader_2_id?: string;
  reader_2_name?: string;
  chair_id?: string;
  chair_name?: string;
}

export interface Execution_ECGRecord {
  ecg_id: string;
  timestamp: string;
  rhythm?: string;
  st_elevation_leads?: string[];
  st_depression_leads?: string[];
  t_wave_inversion_leads?: string[];
  q_wave_leads?: string[];
  lbbb_present?: boolean;
  qtcf_ms?: number | null;
  interpretation?: string | null;
}

export interface Execution_EISFDocumentRecord {
  document_id: string;
  study_id: string;
  site_id: string;
  category: Execution_EISFTaxonomyCategoryEnum;
  title: string;
  version?: string;
  file_name: string;
  file_size_bytes: number;
  sha256_hash: string;
  uploaded_by: string;
  uploaded_at: string;
  expiration_date?: string | null;
  is_redacted?: boolean;
}

export type Execution_EISFTaxonomyCategoryEnum =
  | "1_INVESTIGATOR_CV"
  | "2_MEDICAL_LICENSE"
  | "3_PROTOCOL_APPROVAL"
  | "4_IRB_IEC_APPROVAL"
  | "5_INFORMED_CONSENT"
  | "6_FINANCIAL_DISCLOSURE"
  | "7_DELEGATION_OF_AUTHORITY"
  | "8_SAFETY_REPORT";

export interface Execution_ETMFCompletenessWebhookPayload {
  study_id: string;
  site_id?: string | null;
  milestone: string;
  is_complete: boolean;
  missing_artifacts?: string[];
}

export interface Execution_EdcCasebookWritebackResult {
  dossier_id: string;
  subject_id: string;
  study_id: string;
  event_id: string;
  observation_ids_updated?: string[];
  adjudication_status?: string;
  adjudicated_term: string;
  adjudication_date: string;
  signature_id?: string | null;
  is_locked?: boolean;
  written_at?: string;
  status?: string;
}

export type Execution_EndpointCategory =
  | "MACE"
  | "MYOCARDIAL_INFARCTION"
  | "STROKE"
  | "CARDIOVASCULAR_MORTALITY"
  | "ALL_CAUSE_MORTALITY"
  | "HEART_FAILURE_HOSPITALIZATION"
  | "STENT_THROMBOSIS"
  | "VENOUS_THROMBOEMBOLISM"
  | "DISEASE_PROGRESSION"
  | "OTHER";

export interface Execution_ExcursionEvaluationResponse {
  id?: string | null;
  study_id: string;
  logger_id: string;
  entity_type: string;
  entity_id: string;
  evaluation_status: string;
  mean_kinetic_temperature_celsius: number;
  arithmetic_mean_celsius: number;
  min_temperature_celsius: number;
  max_temperature_celsius: number;
  cumulative_hours_above_upper: number;
  cumulative_hours_below_lower: number;
  total_duration_hours: number;
  readings_count: number;
  hard_boundary_breached: boolean;
  mkt_breached: boolean;
  cumulative_time_breached: boolean;
  quarantine_triggered: boolean;
  quarantined_kit_count: number;
  excursions?: Execution_DiscreteExcursionResponse[];
  evaluated_at: string;
  evaluated_by: string;
  summary_notes?: string | null;
}

export interface Execution_ExportBundleRequest {
  study_id: string;
  format?: string;
  domains?: string[];
  datasets?: string[];
  site_ids?: string[];
  cohorts?: string[];
  privacy_profile?: string;
  xpt_version?: string;
  include_audit_trail?: boolean;
  metadata_version_oid?: string;
  salt?: string | null;
}

export interface Execution_FormSubmissionApprove {
  signing_reason: string;
  signature_manifest: Record<string, any>;
}

export interface Execution_FormSubmissionCreate {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_id?: string | null;
  form_id: string;
  protocol_version?: string | null;
  payload?: Record<string, any> | null;
}

export interface Execution_FormSubmissionResponse {
  id: string;
  study_id: string;
  site_id: string | null;
  subject_id: string;
  visit_id?: string | null;
  form_id: string;
  status: Execution_FormSubmissionStatusEnum;
  version: number;
  is_deleted: boolean;
  signature_manifest?: Record<string, any> | null;
  protocol_version?: string | null;
  payload?: Record<string, any> | null;
  is_active: boolean;
  is_readonly: boolean;
  cloned_from_id?: string | null;
}

export type Execution_FormSubmissionStatusEnum =
  "DRAFT" | "COMPLETED" | "APPROVED";

export interface Execution_GUDIDDeviceRecord {
  primary_di: string;
  brand_name: string;
  version_model_number: string;
  device_description: string;
  commercial_distribution_status?: string;
  device_count?: number;
  fda_listing_number?: string | null;
  premarket_submission_number?: string | null;
  gmdn_code?: string | null;
  gmdn_name?: string | null;
  device_class?: string;
  is_single_use?: boolean;
  is_prescription_only?: boolean;
  is_otc?: boolean;
  is_sterile?: boolean;
  sterilization_methods?: string[];
  mri_safety_status?: string;
  has_lot_number?: boolean;
  has_serial_number?: boolean;
  has_expiration_date?: boolean;
  has_donation_id?: boolean;
}

export interface Execution_GUDIDLookupRequest {
  udi_di: string;
}

export interface Execution_GUDIDVerificationResult {
  udi_di: string;
  is_in_gudid: boolean;
  is_checksum_valid: boolean;
  is_verified: boolean;
  gudid_record?: Execution_GUDIDDeviceRecord | null;
  pi_validation?: Record<string, any>;
  discrepancies?: string[];
  verification_timestamp?: string;
}

export interface Execution_GenerateAuditorTokenRequest {
  auditor_email: string;
  study_id: string;
  duration_hours?: number;
  reason_for_access: string;
}

export type Execution_GradingScaleEnum = "CTCAE_V5" | "DAIDS_V2_1";

export interface Execution_HTTPValidationError {
  detail?: Execution_ValidationError[];
}

export type Execution_HealthImpact =
  | "NONE"
  | "MILD_TRANSIENT"
  | "SERIOUS_DETERIORATION"
  | "PERMANENT_IMPAIRMENT"
  | "LIFE_THREATENING"
  | "DEATH";

export interface Execution_IPKitCreateRequest {
  study_id: string;
  kit_number: string;
  kit_type: string;
  description?: string | null;
  lot_number?: string | null;
  expiration_date?: string | null;
  node_id?: string | null;
}

export interface Execution_IPKitResponse {
  id: string;
  study_id: string;
  kit_number: string;
  kit_type: string;
  description?: string | null;
  status: string;
  lot_number?: string | null;
  expiration_date?: string | null;
  node_id?: string | null;
  quarantine_reason?: string | null;
  quarantined_at?: string | null;
  quarantined_by?: string | null;
}

export interface Execution_IVDAssayPerformanceRequest {
  study_id: string;
  assay_name: string;
  assay_lot_number: string;
  device_udi_di?: string | null;
  true_positives: number;
  false_positives: number;
  true_negatives: number;
  false_negatives: number;
  replicate_measurements?: number[];
  inter_assay_runs?: number[][];
  blank_measurements?: number[];
  low_standard_measurements?: number[];
  linearity_concentrations?: number[];
  linearity_signals?: number[];
  reason_for_change?: string;
}

export interface Execution_IVDAssayPerformanceResponse {
  id: string;
  study_id: string;
  assay_name: string;
  assay_lot_number: string;
  device_udi_di: string | null;
  true_positives: number;
  false_positives: number;
  true_negatives: number;
  false_negatives: number;
  sensitivity_percent: number;
  specificity_percent: number;
  ppv_percent: number;
  npv_percent: number;
  accuracy_percent: number;
  cohens_kappa: number;
  intra_assay_cv_percent: number;
  inter_assay_cv_percent: number;
  limit_of_detection: number | null;
  limit_of_quantitation: number | null;
  linearity_r_squared: number | null;
  status: string;
  passes_acceptance_criteria: boolean;
  created_at: string;
  created_by: string;
}

export type Execution_IVDContainerType =
  | "EDTA_TUBE"
  | "SST_TUBE"
  | "CRYOBIO_VIAL"
  | "HEPARIN_TUBE"
  | "STERILE_CUP"
  | "SWAB_TRANSPORT_MEDIA";

export interface Execution_IVDSampleIntegrityCheckRequest {
  hemolysis_index_mg_dl?: number;
  lipemia_index_mg_dl?: number;
  icterus_index_mg_dl?: number;
  clot_detected?: boolean;
  temperature_excursion?: boolean;
  volume_adequate?: boolean;
  integrity_notes?: string | null;
  reason_for_change?: string;
}

export interface Execution_IVDSampleIntegrityCheckResponse {
  specimen_identifier: string;
  integrity_status: string;
  hemolysis_index_mg_dl: number;
  lipemia_index_mg_dl: number;
  icterus_index_mg_dl: number;
  clot_detected: boolean;
  temperature_excursion: boolean;
  is_quarantined: boolean;
  quarantine_reason: string | null;
  validated_at: string;
  validated_by: string;
}

export interface Execution_IVDSpecimenCreateRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  specimen_identifier?: string | null;
  specimen_type?: Execution_IVDSpecimenType;
  collection_date?: string;
  collector_user_id?: string | null;
  container_type?: Execution_IVDContainerType;
  initial_volume_ul?: number;
  storage_temperature_celsius?: number;
  storage_location?: string;
  max_allowed_freeze_thaw?: number;
  reason_for_change?: string;
}

export interface Execution_IVDSpecimenCustodyEventRequest {
  from_user_id: string;
  to_user_id: string;
  from_location: string;
  to_location: string;
  transport_condition?: string;
  temperature_at_transfer_celsius?: number | null;
  seal_intact?: boolean;
  signature_token?: string | null;
  reason_for_change?: string;
}

export interface Execution_IVDSpecimenCustodyEventResponse {
  id: string;
  specimen_identifier: string;
  transfer_timestamp: string;
  from_user_id: string;
  to_user_id: string;
  from_location: string;
  to_location: string;
  transport_condition: string;
  temperature_at_transfer_celsius: number | null;
  seal_intact: boolean;
  signature_token: string | null;
}

export interface Execution_IVDSpecimenFreezeThawEventRequest {
  thaw_start_timestamp?: string;
  thaw_end_timestamp?: string | null;
  thaw_duration_minutes?: number;
  thaw_temperature_celsius?: number;
  operator_user_id: string;
  purpose: string;
  is_excursion?: boolean;
  reason_for_change?: string;
}

export interface Execution_IVDSpecimenFreezeThawEventResponse {
  id: string;
  specimen_identifier: string;
  cycle_number: number;
  thaw_start_timestamp: string;
  thaw_end_timestamp: string | null;
  thaw_duration_minutes: number;
  thaw_temperature_celsius: number;
  operator_user_id: string;
  purpose: string;
  is_excursion: boolean;
}

export interface Execution_IVDSpecimenResponse {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  specimen_identifier: string;
  specimen_type: string;
  collection_date: string;
  collector_user_id: string;
  container_type: string;
  initial_volume_ul: number;
  current_volume_ul: number;
  storage_temperature_celsius: number;
  storage_location: string;
  status: string;
  is_quarantined: boolean;
  quarantine_reason: string | null;
  freeze_thaw_count: number;
  max_allowed_freeze_thaw: number;
  hemolysis_index_mg_dl: number;
  lipemia_index_mg_dl: number;
  icterus_index_mg_dl: number;
  clot_detected: boolean;
  temperature_excursion: boolean;
  integrity_status: string;
  integrity_validated_at: string | null;
  integrity_validated_by: string | null;
  custody_events?: Execution_IVDSpecimenCustodyEventResponse[];
  freeze_thaw_events?: Execution_IVDSpecimenFreezeThawEventResponse[];
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type Execution_IVDSpecimenType =
  | "WHOLE_BLOOD"
  | "SERUM"
  | "PLASMA"
  | "URINE"
  | "SALIVA"
  | "CSF"
  | "TISSUE_BIOPSY"
  | "NASOPHARYNGEAL_SWAB"
  | "CELL_CULTURE";

export interface Execution_ImpactAnalysisRequest {
  dictionary_type: Execution_DictTypeEnum;
  new_version: string;
}

export interface Execution_ImpactAnalysisResponse {
  status: string;
  dictionary_type: Execution_DictTypeEnum;
  new_version: string;
  metrics: Execution_ImpactMetrics;
}

export interface Execution_ImpactMetrics {
  unchanged?: number;
  reclassified?: number;
  deprecated?: number;
  skipped?: number;
  verbatim_terms_affected?: number | null;
  coded_terms_affected?: number | null;
  uncodable_terms?: number | null;
}

export interface Execution_InvalidParam {
  field?: string | null;
  reason?: string | null;
  value?: string | null;
}

export type Execution_JobStatusEnum =
  "PENDING" | "PROCESSING" | "COMPLETED" | "FAILED";

export interface Execution_JobStatusResponse {
  job_id: string;
  dictionary_type: string;
  version: string;
  status: Execution_JobStatusEnum;
  started_at: string;
  completed_at?: string | null;
  progress_percentage?: number | null;
  records_imported?: number | null;
  errors_encountered?: number | null;
}

export interface Execution_KitQuarantineRequest {
  study_id: string;
  kit_numbers: string[];
  quarantine_reason: string;
}

export interface Execution_KitReleaseQuarantineRequest {
  study_id: string;
  kit_numbers: string[];
  qa_release_justification: string;
}

export interface Execution_LabBatchIngestResult {
  batch_id: string;
  study_id?: string | null;
  format: string;
  status: string;
  total_processed?: number;
  ingested_count?: number;
  out_of_range_count?: number;
  critical_alerts?: number;
  queries_raised?: number;
  staged_aes_count?: number;
  uncalibrated_safeguards?: number;
  toxicity_grades?: Record<string, any>;
  errors?: Record<string, any>[];
  created_at?: string;
}

export interface Execution_LabPanelItem {
  test_name: string;
  loinc_code?: string | null;
  result_value: number | string;
  result_unit: string;
  reference_low?: number | null;
  reference_high?: number | null;
  is_out_of_range?: boolean;
  toxicity_grade?: number | null;
  sample_timestamp?: string | null;
}

export interface Execution_LabRangeRecalculateRequest {
  study_id: string;
  test_code: string;
}

export interface Execution_LabRangeRecalculateResponse {
  status: string;
  study_id: string;
  test_code: string;
  updated_count: number;
}

export interface Execution_LabReferenceRangeCreate {
  study_id: string;
  test_code: string;
  test_name: string;
  source: Execution_LabSourceEnum;
  site_id?: string | null;
  unit: string;
  normalized_unit: string;
  sex_applicability: string;
  ethnicity_applicability?: string | null;
  age_low?: number | null;
  age_high?: number | null;
  low_bound?: number | null;
  high_bound?: number | null;
  critical_low?: number | null;
  critical_high?: number | null;
  reason_for_change?: string | null;
}

export interface Execution_LabReferenceRangeResponse {
  id: string;
  study_id: string;
  test_code: string;
  test_name: string;
  source: Execution_LabSourceEnum;
  site_id?: string | null;
  unit: string;
  normalized_unit: string;
  sex_applicability: string;
  ethnicity_applicability?: string | null;
  age_low?: number | null;
  age_high?: number | null;
  low_bound?: number | null;
  high_bound?: number | null;
  critical_low?: number | null;
  critical_high?: number | null;
  version: number;
  is_deleted: boolean;
}

export interface Execution_LabReferenceRangeUpdate {
  study_id?: string | null;
  test_code?: string | null;
  test_name?: string | null;
  source?: string | null;
  site_id?: string | null;
  unit?: string | null;
  normalized_unit?: string | null;
  sex_applicability?: string | null;
  ethnicity_applicability?: string | null;
  age_low?: number | null;
  age_high?: number | null;
  low_bound?: number | null;
  high_bound?: number | null;
  critical_low?: number | null;
  critical_high?: number | null;
  reason_for_change?: string | null;
}

export type Execution_LabSourceEnum = "CENTRAL" | "LOCAL";

export interface Execution_LabToxicityEvaluateRequest {
  test_code: string;
  test_name?: string | null;
  value: number;
  unit: string;
  ref_low?: number | null;
  ref_high?: number | null;
  baseline_value?: number | null;
  previous_value?: number | null;
  grading_scale?: Execution_GradingScaleEnum;
  alt_value?: number | null;
  ast_value?: number | null;
  bili_value?: number | null;
  alkp_value?: number | null;
}

export interface Execution_LabToxicityEvaluateResponse {
  test_code: string;
  test_name: string;
  observed_value: number;
  unit: string;
  ctcae_grade: number;
  grading_scale: string;
  toxicity_description: string;
  is_critical_ae_trigger: boolean;
  is_hys_law_warning: boolean;
  is_delta_check_alert: boolean;
  delta_percentage?: number | null;
}

export interface Execution_LocalLabCertificationCreate {
  study_id: string;
  site_id: string;
  lab_name: string;
  lab_identifier?: string | null;
  certification_number: string;
  accreditation_body: string;
  effective_date: string;
  expiration_date: string;
  director_name?: string | null;
  is_accredited?: boolean;
  notes?: string | null;
  reason_for_change: string;
}

export interface Execution_LocalLabCertificationResponse {
  id: string;
  study_id: string;
  site_id: string;
  lab_name: string;
  lab_identifier?: string | null;
  certification_number: string;
  accreditation_body: string;
  effective_date: string;
  expiration_date: string;
  status: Execution_CertificationStatusEnum;
  director_name?: string | null;
  is_accredited: boolean;
  notes?: string | null;
  version?: number;
  is_deleted?: boolean;
}

export interface Execution_LocalLabCertificationUpdate {
  lab_name?: string | null;
  lab_identifier?: string | null;
  certification_number?: string | null;
  accreditation_body?: string | null;
  effective_date?: string | null;
  expiration_date?: string | null;
  status?: Execution_CertificationStatusEnum | null;
  director_name?: string | null;
  is_accredited?: boolean | null;
  notes?: string | null;
  reason_for_change: string;
}

export interface Execution_LocalLabCertificationVerifyRequest {
  study_id: string;
  site_id: string;
  observation_date?: string | null;
}

export interface Execution_LocalLabCertificationVerifyResponse {
  is_valid: boolean;
  status: string;
  site_id: string;
  study_id: string;
  certification_number?: string | null;
  accreditation_body?: string | null;
  expiration_date?: string | null;
  days_until_expiration?: number | null;
  uncalibrated_safeguard_triggered?: boolean;
  message: string;
}

export interface Execution_LocalLedgerBlock {
  index: number;
  timestamp: string;
  action: string;
  details: Execution_SyncBlockDetails;
  reason: string;
  prevHash: string;
  hash: string;
}

export type Execution_LockScopeEnum =
  | "STUDY"
  | "TRIAL"
  | "SITE"
  | "SUBJECT"
  | "VISIT"
  | "FORM"
  | "ITEM_GROUP"
  | "FIELD";

export type Execution_LockStatusEnum =
  "UNLOCKED" | "FROZEN" | "LOCKED" | "HARD_LOCK" | "SOFT_LOCK";

export interface Execution_LockStatusResponse {
  locked_sites: string[];
  locked_visits: string[];
  locked_forms: string[];
  locked_subjects: string[];
  trial_locked: boolean;
}

export interface Execution_LoggerIngestionRequest {
  study_id: string;
  logger_id: string;
  file_format?: string;
  raw_payload: string;
  filename: string;
  entity_type?: string;
  entity_id: string;
  stability_budget_override?: Execution_StabilityBudgetCreateRequest | null;
}

export interface Execution_LoincCatalogResponse {
  total_count: number;
  items: Execution_LoincMappingItem[];
}

export interface Execution_LoincConvertRequest {
  test_code: string;
  from_unit: string;
  to_unit: string;
  value: number;
}

export interface Execution_LoincConvertResponse {
  test_code: string;
  from_unit: string;
  to_unit: string;
  original_value: number;
  converted_value: number;
  factor: number;
  offset?: number | null;
  loinc_code?: string | null;
}

export interface Execution_LoincMappingItem {
  test_code: string;
  test_name: string;
  loinc_code: string;
  standard_unit: string;
  category: string;
  molecular_weight?: number | null;
}

export interface Execution_LotExpiryExtensionRequest {
  study_id: string;
  lot_number: string;
  kit_type: string;
  extended_expiry_date: string;
  extension_reason: string;
  stability_protocol_reference: string;
}

export interface Execution_LotExpiryExtensionResponse {
  id: string;
  study_id: string;
  lot_number: string;
  kit_type: string;
  initial_expiry_date: string;
  extended_expiry_date: string;
  extension_reason: string;
  stability_protocol_reference: string;
  authorized_by: string;
  approval_status: string;
  affected_kits_count: number;
  relabeling_tasks_generated: number;
  created_at: string;
}

export interface Execution_MedDRACodeLookupResponse {
  status: "AUTO-CODED" | "SUGGESTIONS" | "UNCODABLE";
  matches: Execution_MedDRAMatch[];
}

export interface Execution_MedDRAMatch {
  llt_code: string;
  llt_name: string;
  pt_code: string;
  pt_name: string;
  hlt_code: string;
  hlt_name: string;
  hlgt_code: string;
  hlgt_name: string;
  soc_code: string;
  soc_name: string;
  primary_soc_flag?: string | null;
  score: number;
}

export type Execution_MedDRATargetLevelEnum = "LLT" | "PT";

export interface Execution_MeetingQuorumRecordDTO {
  study_id: string;
  charter_id: string;
  meeting_date: string;
  attendees: Record<string, any>[];
  total_eligible_members: number;
  dossiers_reviewed?: string[];
  voting_results?: Record<string, any>;
  meeting_minutes?: string;
  chair_signature?: Record<string, any> | null;
}

export interface Execution_MigrationRuleCreate {
  study_id: string;
  source_version: string;
  target_version: string;
  rule_type: string;
  source_field?: string | null;
  target_field?: string | null;
  default_value_string?: string | null;
  default_value_float?: number | null;
}

export interface Execution_MigrationRuleResponse {
  id: string;
  study_id: string;
  source_version: string;
  target_version: string;
  rule_type: string;
  source_field?: string | null;
  target_field?: string | null;
  default_value_string?: string | null;
  default_value_float?: number | null;
}

export interface Execution_MktCalculationDirectRequest {
  temperatures_celsius: number[];
  activation_energy_over_r?: number;
}

export interface Execution_MktCalculationDirectResponse {
  mean_kinetic_temperature_celsius: number;
  arithmetic_mean_celsius: number;
  min_temperature_celsius: number;
  max_temperature_celsius: number;
  readings_count: number;
  formula_reference?: string;
}

export interface Execution_MultiEchelonRollup {
  study_id: string;
  total_nodes_count: number;
  manufacturers_count: number;
  central_depots_count: number;
  regional_depots_count: number;
  clinical_sites_count: number;
  total_global_on_hand_stock: number;
  total_global_in_transit_stock: number;
  total_global_reserved_stock: number;
  total_global_storage_capacity: number;
  overall_capacity_utilization_pct: number;
  hierarchy_tree: Execution_SupplyHierarchyNode[];
}

export interface Execution_ObservationCreate {
  subject_id: string;
  study_id?: string | null;
  site_id?: string | null;
  visit_id?: string | null;
  domain: string;
  test_code: string;
  test_name: string;
  value?: number | null;
  value_string?: string | null;
  unit?: string | null;
  observation_date?: string | null;
  lab_source?: string | null;
  lab_site_id?: string | null;
}

export interface Execution_ObservationResponse {
  id: string;
  subject_id: string;
  study_id: string;
  visit_id?: string | null;
  domain: string;
  observation_date: string;
  test_code: string;
  test_name: string;
  value?: number | null;
  value_string?: string | null;
  unit?: string | null;
  normalized_value?: number | null;
  normalized_unit?: string | null;
  is_outlier: boolean;
  lab_source?: string | null;
  lab_site_id?: string | null;
  lab_indicator?: string | null;
  lab_out_of_range?: boolean | null;
  matched_normal_bounds?: string | null;
  range_indicator?: string | null;
  is_out_of_range?: boolean | null;
  reference_range_low?: number | null;
  reference_range_high?: number | null;
  protocol_version_tag?: string | null;
  protocol_version_index?: number | null;
}

export interface Execution_OfflineBatchSyncRequest {
  client_batch_id: string;
  device_id: string;
  deltas: Execution_OfflineDeltaItem[];
}

export interface Execution_OfflineBatchSyncResponse {
  client_batch_id: string;
  status: "SUCCESS" | "PARTIAL_SUCCESS" | "ALREADY_PROCESSED";
  processed_count: number;
  conflicts?: Record<string, any>[];
}

export interface Execution_OfflineDeltaItem {
  delta_id: string;
  entity_type: string;
  entity_id: string;
  action: "CREATE" | "UPDATE" | "SUBMIT";
  payload: Record<string, any>;
  client_timestamp_utc: string;
  reason_for_change: string;
}

export interface Execution_OutlierRecalculateRequest {
  study_id: string;
  test_code: string;
}

export interface Execution_OutlierRecalculateResponse {
  status: string;
  study_id: string;
  test_code: string;
  outliers_found: number;
}

export interface Execution_PHIScanRequest {
  text: string;
}

export interface Execution_PackingManifestCreateRequest {
  manifest_number: string;
  serialized_kit_numbers: string[];
  lot_number: string;
  expiration_date?: string | null;
  container_serial_number?: string | null;
  data_logger_id?: string | null;
  tamper_seal_id?: string | null;
}

export interface Execution_PackingManifestResponse {
  id: string;
  study_id: string;
  transfer_order_id: string;
  manifest_number: string;
  serialized_kit_numbers: string[];
  lot_number: string;
  expiration_date?: string | null;
  container_serial_number?: string | null;
  data_logger_id?: string | null;
  tamper_seal_id?: string | null;
  packed_by: string;
  packed_at: string;
  verified_by?: string | null;
  verified_at?: string | null;
}

export interface Execution_ParsedUDI {
  raw_barcode: string;
  udi_carrier_format?: Execution_UDICarrierFormat;
  udi_di: string;
  lot_number?: string | null;
  serial_number?: string | null;
  expiration_date?: string | null;
  manufacturing_date?: string | null;
  software_version?: string | null;
  is_valid?: boolean;
  validation_errors?: string[];
}

export interface Execution_PredictiveForecastRequest {
  study_id: string;
  site_id: string;
  kit_type?: string;
  horizon_days?: number;
  enrollment_velocity_weekly?: number;
  screening_pipeline_count?: number;
  screening_conversion_rate?: number;
  lead_time_days?: number;
  buffer_days?: number;
  safety_stock_threshold?: number;
  storage_capacity?: number;
}

export interface Execution_PredictiveForecastResponse {
  study_id: string;
  site_id: string;
  kit_type: string;
  forecast_generated_at: string;
  horizon_days: number;
  current_on_hand_qty: number;
  current_reserved_qty: number;
  current_in_transit_qty: number;
  effective_available_stock: number;
  active_subjects_count: number;
  screening_pipeline_count: number;
  enrollment_velocity_weekly: number;
  total_projected_demand: number;
  average_daily_burn_rate: number;
  days_of_supply_remaining: number;
  projected_runout_date: string | null;
  stockout_risk_level: string;
  recommended_order_qty: number;
  reorder_recommended: boolean;
  reorder_trigger_day: number | null;
  timeline: Execution_DailyDemandProjection[];
}

export interface Execution_ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail: string;
  instance: string;
  code: string;
  invalid_params?: Execution_InvalidParam[] | null;
}

export interface Execution_ProtocolVersionRefDTO {
  study_id: string;
  version_tag: string;
  version_index: number;
  status: Execution_ProtocolVersionStatusEnum;
}

export type Execution_ProtocolVersionStatusEnum =
  "DRAFT" | "ACTIVE" | "LOCKED" | "PUBLISHED" | "ARCHIVED" | "FROZEN";

export interface Execution_PublishAmendmentRequest {
  study_id: string;
  version_number: string;
  description: string;
  baseline_snapshot: Record<string, any>;
  amended_snapshot: Record<string, any>;
}

export interface Execution_PublishAmendmentResponse {
  amendment_id: string;
  study_id: string;
  version_number: string;
  published_at: string;
  summary_of_changes: string;
  added_activities_count: number;
  removed_activities_count: number;
}

export type Execution_QuarantineScope =
  "DEVICE_SERIAL" | "SITE_LOT" | "STUDY_GLOBAL_LOT";

export interface Execution_QueryCancel {
  reason: string;
}

export interface Execution_QueryCreate {
  study_id: string;
  subject_id: string;
  visit_id?: string | null;
  domain?: string | null;
  test_code: string;
  explanation: string;
  status?: string | null;
  observation_id?: string | null;
  field_link?: string | null;
  message?: string | null;
  origin?: string | null;
  priority?: string | null;
  rule_id?: string | null;
  created_by?: string | null;
  form_id?: string | null;
  field_id?: string | null;
  query_type?: string | null;
  action_required?: string | null;
}

export interface Execution_QueryHistoryItem {
  action: string;
  user_id?: string | null;
  timestamp: string;
  old_values?: Record<string, any> | null;
  new_values?: Record<string, any> | null;
  change_reason?: string | null;
  version_index: number;
}

export interface Execution_QueryReopen {
  reason?: string | null;
}

export interface Execution_QueryRespond {
  response: string;
  responder?: string | null;
}

export interface Execution_QueryTargetDescriptor {
  study_id?: string | null;
  subject_id: string;
  visit_id: string;
  domain: string;
  test_code: string;
  observation_id?: string | null;
  form_id?: string | null;
  field_id?: string | null;
  explanation?: string | null;
}

export interface Execution_QueryUpdate {
  status: string;
  explanation?: string | null;
  response?: string | null;
  observation_id?: string | null;
  field_link?: string | null;
  message?: string | null;
  origin?: string | null;
  priority?: string | null;
  rule_id?: string | null;
  created_by?: string | null;
  responder?: string | null;
  resolver?: string | null;
  resolved_at?: string | null;
  cancellation_reason?: string | null;
  escalated_at?: string | null;
  form_id?: string | null;
  field_id?: string | null;
  query_type?: string | null;
  action_required?: string | null;
}

export interface Execution_RARArmEvaluationDTO {
  arm_id: string;
  arm_name: string;
  is_control: boolean;
  status: string;
  enrolled_count: number;
  response_count: number;
  posterior_alpha: number;
  posterior_beta: number;
  posterior_mean: number;
  allocation_probability: number;
  superiority_vs_control_prob?: number | null;
}

export interface Execution_RAREvaluationResult {
  study_id: string;
  total_enrolled: number;
  arms: Execution_RARArmEvaluationDTO[];
  allocation_probabilities: Record<string, any>;
  dropped_arms?: string[];
  graduated_arms?: string[];
  rationale: string;
  updated_at: string;
}

export interface Execution_RARExpandOrTerminateArmRequest {
  study_id: string;
  arm_id: string;
  arm_name: string;
  is_control?: boolean;
  action: string;
  prior_alpha?: number;
  prior_beta?: number;
  reason: string;
}

export interface Execution_RaiseQueryRequest {
  query_text?: string | null;
  message?: string | null;
  explanation?: string | null;
  reason?: string | null;
  reason_for_change?: string | null;
}

export interface Execution_RaiseQueryResponse {
  query_id: string;
  status: string;
  assignment_id: string;
  explanation?: string | null;
}

export interface Execution_RarConfig {
  burn_in_sample_per_arm?: number;
  power_tuning_param?: number;
  min_allocation_prob?: number;
  max_allocation_prob?: number;
  futility_threshold?: number;
  efficacy_threshold?: number;
  prior_alpha?: number;
  prior_beta?: number;
}

export interface Execution_ReLabelingTaskCompleteRequest {
  notes?: string | null;
}

export interface Execution_ReLabelingTaskResponse {
  id: string;
  study_id: string;
  task_number: string;
  lot_number: string;
  node_id: string;
  affected_kit_count: number;
  old_expiry_date: string;
  new_expiry_date: string;
  status: string;
  assigned_to?: string | null;
  completed_at?: string | null;
  completed_by?: string | null;
  verified_at?: string | null;
  verified_by?: string | null;
  label_barcode_hash?: string | null;
  relabeling_notes?: string | null;
}

export interface Execution_ReLabelingTaskVerifyRequest {
  label_barcode_hash?: string | null;
  notes?: string | null;
}

export interface Execution_ReaderAssessment {
  assessment_id: string;
  dossier_id: string;
  reader_id: string;
  reader_name: string;
  reader_role: Execution_ReaderRole;
  classification: Execution_AdjudicationClassification;
  subtype?: string | null;
  adjudicated_onset_date?: string | null;
  criteria_met?: string[];
  confidence_score?: number;
  clinical_rationale?: string;
  submitted_at?: string | null;
  assessment_digest?: string;
}

export interface Execution_ReaderAssessmentSubmitDTO {
  reader_role: Execution_ReaderRole;
  classification: Execution_AdjudicationClassification;
  subtype?: string | null;
  adjudicated_onset_date?: string | null;
  criteria_met?: string[];
  confidence_score?: number;
  clinical_rationale?: string;
}

export type Execution_ReaderRole =
  "READER_1" | "READER_2" | "CHAIR" | "COMMITTEE_MEMBER" | "COORDINATOR";

export interface Execution_RedactPDFRequest {
  pdf_base64: string;
  target_snippets?: string[];
}

export interface Execution_ResupplyEventApprovalRequest {
  change_justification: string;
}

export interface Execution_ResupplyEventResponse {
  id: string;
  study_id: string;
  site_id: string;
  kit_id: string;
  requested_qty: number;
  status: string;
  triggered_at: any;
}

export interface Execution_RevokeDelegationRequest {
  delegation_id: string;
  end_date: string;
  reason_for_change: string;
}

export interface Execution_SAEReconcileRequest {
  study_id: string;
  edc_ae_events: Record<string, any>[];
  safety_cases_xml?: string[] | null;
}

export type Execution_SDVScopeEnum = "FIELD" | "PAGE" | "VISIT";

export interface Execution_SDVSignOffRequest {
  scope: Execution_SDVScopeEnum;
  target_id: string;
  subject_id: string;
  study_id: string;
  site_id?: string | null;
}

export interface Execution_SDVSignOffResponse {
  id: string;
  scope: string;
  target_id: string;
  subject_id: string;
  study_id: string;
  site_id?: string | null;
  is_verified: boolean;
  verified_by?: string | null;
  verified_at?: string | null;
  dropped_reason?: string | null;
  dropped_at?: string | null;
}

export interface Execution_SafetyDispatchRequest {
  study_id: string;
  subject_id: string;
  safety_report_id: string;
  destination_gateway?: string;
  expedited?: boolean;
  reason_for_change: string;
}

export interface Execution_SafetyDispatchResponse {
  dispatch_id: string;
  safety_report_id: string;
  status: string;
  dispatched_at: string;
  ack_status: string;
}

export type Execution_SamplingModelEnum =
  "SUBJECT_BASED" | "FIELD_BASED" | "COMBINED";

export interface Execution_SdtmCeCompilationResult {
  study_id: string;
  ce_records?: Record<string, any>[];
  suppce_records?: Record<string, any>[];
  record_count?: number;
  compiled_at?: string;
  dataset_digest?: string;
}

export interface Execution_SdtmCeCompileRequestDTO {
  study_id: string;
}

export interface Execution_SignRequest {
  data: string;
}

export interface Execution_SignResponse {
  signed_data: string;
}

export interface Execution_SiteActivationRequest {
  study_id: string;
}

export interface Execution_SiteStaffMemberRequest {
  site_id: string;
  staff_user_id: string;
  name: string;
  email: string;
  has_gcp_training?: boolean;
}

export interface Execution_SiteStaffMemberResponse {
  id: string;
  site_id: string;
  staff_user_id: string;
  name: string;
  email: string;
  has_gcp_training: boolean;
}

export interface Execution_StabilityBudgetCreateRequest {
  study_id: string;
  kit_type: string;
  lot_number?: string | null;
  storage_condition?: string;
  min_temp_celsius?: number;
  max_temp_celsius?: number;
  max_mkt_celsius?: number;
  max_cumulative_hours_above_upper?: number;
  max_cumulative_hours_below_lower?: number;
  activation_energy_over_r?: number;
  hard_min_temp_celsius?: number | null;
  hard_max_temp_celsius?: number | null;
}

export interface Execution_StabilityBudgetResponse {
  id: string;
  study_id: string;
  kit_type: string;
  lot_number?: string | null;
  storage_condition: string;
  min_temp_celsius: number;
  max_temp_celsius: number;
  max_mkt_celsius: number;
  max_cumulative_hours_above_upper: number;
  max_cumulative_hours_below_lower: number;
  activation_energy_over_r: number;
  hard_min_temp_celsius?: number | null;
  hard_max_temp_celsius?: number | null;
}

export interface Execution_StagedAdverseEventActionRequest {
  action: string;
  ae_term?: string | null;
  causality_assessment?: string | null;
  investigator_notes?: string | null;
  electronic_signature_password?: string | null;
  reason_for_change: string;
}

export interface Execution_StagedAdverseEventActionResponse {
  id: string;
  review_status: string;
  action_taken: string;
  form_submission_id?: string | null;
  query_id?: string | null;
  message: string;
  timestamp: string;
}

export interface Execution_StagedAdverseEventReviewResponse {
  id: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  visit_id?: string | null;
  observation_id?: string | null;
  form_submission_id?: string | null;
  query_id?: string | null;
  test_code: string;
  test_name: string;
  lab_value: number;
  unit: string;
  ctcae_grade: number;
  grading_scale: string;
  toxicity_term: string;
  clinical_description: string;
  is_hys_law_warning: boolean;
  is_delta_check_alert: boolean;
  delta_percentage?: number | null;
  review_status: string;
  reviewed_by?: string | null;
  reviewed_at?: string | null;
  investigator_notes?: string | null;
  version: number;
  is_deleted: boolean;
  created_at?: string | null;
}

export interface Execution_StudyEvent {
  study_id: string;
  payload?: Record<string, any>;
}

export interface Execution_SubjectConsentRequest {
  protocol_version: Execution_ProtocolVersionRefDTO;
  icf_signed: boolean;
  icf_signed_date?: string | null;
  requires_reconsent?: boolean;
  is_paper_override?: boolean;
}

export interface Execution_SubjectConsentResponse {
  id: string;
  subject_id: string;
  study_id: string;
  version_tag: string;
  version_index: number;
  icf_signed: boolean;
  icf_signed_date?: string | null;
  requires_reconsent: boolean;
  is_paper_override?: boolean;
  version: number;
}

export interface Execution_SubjectCreate {
  subject_id: string;
  study_id: string;
  site_id?: string | null;
  arm_id?: string | null;
  demographics?: Execution_Demographics | null;
}

export interface Execution_SubjectDemographicsUpdateRequest {
  demographics?: Execution_Demographics | null;
  strat_factors?: Record<string, any> | null;
}

export interface Execution_SubjectDetailResponse {
  subject_id: string;
  study_id: string;
  status: string;
  site_id?: string | null;
  treatment_group?: string | null;
  randomization_seed?: string | null;
  investigational_product_id?: string | null;
}

export interface Execution_SubjectRandomizationResponse {
  subject_id: string;
  status: string;
  stratum_key?: string | null;
  randomized_at: string;
  kit_reference?: string | null;
  treatment_arm?: string | null;
}

export interface Execution_SubjectReconsentRequest {
  subject_id: string;
  study_id: string;
  protocol_version: string;
  version_index?: number;
  icf_signed?: boolean;
  signature_type?: string;
}

export interface Execution_SubjectResponse {
  id: string;
  subject_id: string;
  study_id: string;
  site_id?: string | null;
  treatment_group?: string | null;
  status?: string | null;
  active_protocol_version?: string | null;
  encrypted_demographics?: string | null;
}

export interface Execution_SubjectScreeningRequest {
  study_id?: string | null;
}

export interface Execution_SubjectScreeningResponse {
  eligible?: boolean | null;
  failed_criteria?: string[];
  indeterminate_criteria?: string[];
  criterion_evaluations?: Execution_CriterionLevelResult[];
}

export interface Execution_SubjectStateUpdateRequest {
  status?: string | null;
  state?: string | null;
}

export interface Execution_SubjectUnblindResponse {
  subject_id: string;
  status: string;
  is_unblinded: boolean;
  treatment_arm?: string | null;
  drug_code?: string | null;
  unblinded_at?: string | null;
  unblinded_by?: string | null;
  unblinded_reason?: string | null;
}

export interface Execution_SupplyHierarchyNode {
  node_id: string;
  study_id: string;
  node_type: Execution_SupplyNodeType;
  name: string;
  country: string;
  address?: string | null;
  parent_node_id?: string | null;
  storage_capacity: number;
  current_utilization: number;
  utilization_percentage: number;
  lead_time_days: number;
  temperature_conditions?: string[];
  is_active: boolean;
  total_on_hand?: number;
  total_reserved?: number;
  total_in_transit?: number;
  children?: Execution_SupplyHierarchyNode[];
}

export interface Execution_SupplyNodeCreateRequest {
  study_id: string;
  node_id: string;
  node_type?: string;
  name: string;
  country: string;
  address?: string | null;
  parent_node_id?: string | null;
  storage_capacity?: number;
  lead_time_days?: number;
  temperature_conditions?: string[];
}

export interface Execution_SupplyNodeInventoryAdjustRequest {
  study_id: string;
  node_id: string;
  kit_type: string;
  lot_number?: string | null;
  on_hand_qty: number;
  safety_stock_threshold?: number;
  reorder_point?: number;
  max_capacity?: number;
}

export interface Execution_SupplyNodeInventoryResponse {
  id: string;
  study_id: string;
  node_id: string;
  kit_type: string;
  lot_number?: string | null;
  on_hand_qty: number;
  reserved_qty: number;
  in_transit_qty: number;
  safety_stock_threshold: number;
  reorder_point: number;
  max_capacity: number;
  resupply_signal: boolean;
}

export interface Execution_SupplyNodeResponse {
  id: string;
  study_id: string;
  node_id: string;
  node_type: string;
  name: string;
  country: string;
  address?: string | null;
  parent_node_id?: string | null;
  storage_capacity: number;
  current_utilization: number;
  lead_time_days: number;
  temperature_conditions?: string[];
  is_active: boolean;
}

export type Execution_SupplyNodeType =
  "PRIMARY_MANUFACTURER" | "CENTRAL_DEPOT" | "REGIONAL_DEPOT" | "CLINICAL_SITE";

export interface Execution_SyncBlockDetails {
  fieldId: string;
  studyId?: string | null;
  subjectId?: string | null;
  visitId?: string | null;
  domain?: string | null;
  testCode?: string | null;
  query?: Execution_SyncBlockQuery | null;
  label?: string | null;
  cdash?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
}

export interface Execution_SyncBlockQuery {
  status: string;
  message?: string | null;
  createdBy?: string | null;
  createdAt?: string | null;
  response?: string | null;
  respondedBy?: string | null;
  respondedAt?: string | null;
  closedBy?: string | null;
  closedAt?: string | null;
}

export interface Execution_SyncRequest {
  blocks: Execution_LocalLedgerBlock[];
}

export interface Execution_TSDVConfigCreate {
  study_id: string;
  sampling_model: Execution_SamplingModelEnum;
  initial_full_sdv_subject_count?: number;
  random_sample_percentage?: number;
  full_sdv_domains?: string[] | null;
  safety_endpoints?: string[] | null;
  zero_sdv_domains?: string[] | null;
  trial_random_seed?: number | null;
}

export interface Execution_TSDVConfigResponse {
  id: string;
  study_id: string;
  sampling_model: string;
  initial_full_sdv_subject_count: number;
  random_sample_percentage: number;
  full_sdv_domains?: string[] | null;
  safety_endpoints?: string[] | null;
  zero_sdv_domains?: string[] | null;
  trial_random_seed?: number | null;
  version: number;
}

export interface Execution_TSDVEvaluationResponse {
  required: boolean;
  subject_selected: boolean;
  field_decision?: boolean | null;
  sampling_model: string;
  config_id: string;
  enrollment_index: number;
  explanation: string;
}

export interface Execution_TemperatureLoggerRegisterRequest {
  study_id: string;
  logger_id: string;
  device_model?: string;
  serial_number?: string | null;
  logger_type?: string;
  assigned_transfer_order_id?: string | null;
  assigned_node_id?: string | null;
  assigned_container_id?: string | null;
  calibration_expiry_date?: string | null;
}

export interface Execution_TemperatureLoggerResponse {
  id: string;
  study_id: string;
  logger_id: string;
  device_model: string;
  serial_number?: string | null;
  logger_type: string;
  assigned_transfer_order_id?: string | null;
  assigned_node_id?: string | null;
  assigned_container_id?: string | null;
  calibration_expiry_date?: string | null;
  status: string;
}

export interface Execution_ThreePlusThreeConfig {
  initial_cohort_size?: number;
  expansion_cohort_size?: number;
  max_dlt_escalate?: number;
  max_dlt_expand?: number;
}

export interface Execution_TransferOrderApprovalRequest {
  change_justification: string;
}

export interface Execution_TransferOrderCreateRequest {
  study_id: string;
  source_node_id: string;
  destination_node_id: string;
  kit_type: string;
  quantity: number;
  priority?: string;
  temperature_condition?: string;
  carrier?: string | null;
  transfer_order_number?: string | null;
}

export interface Execution_TransferOrderReceiveRequest {
  change_justification: string;
}

export interface Execution_TransferOrderResponse {
  id: string;
  study_id: string;
  transfer_order_number: string;
  source_node_id: string;
  destination_node_id: string;
  kit_type: string;
  quantity: number;
  priority: string;
  status: string;
  temperature_condition: string;
  carrier?: string | null;
  tracking_number?: string | null;
  estimated_delivery_date?: string | null;
  requested_at: string;
  requested_by?: string | null;
  approved_at?: string | null;
  approved_by?: string | null;
  shipped_at?: string | null;
  shipped_by?: string | null;
  delivered_at?: string | null;
  delivered_by?: string | null;
  rejection_reason?: string | null;
}

export interface Execution_TransferOrderShipRequest {
  tracking_number: string;
  carrier?: string | null;
  estimated_delivery_date?: string | null;
  change_justification: string;
}

export interface Execution_TranslationJobResponse {
  id: string;
  study_id: string;
  status: string;
  odm_payload?: string | null;
  openrosa_payload?: string | null;
  error_message?: string | null;
  warnings?: Record<string, any>[] | null;
}

export interface Execution_UCUMConvertRequest {
  value: number;
  source_unit: string;
  target_unit: string;
}

export interface Execution_UCUMConvertResponse {
  source: Execution_UCUMUnitValue;
  target: Execution_UCUMUnitValue;
  is_compatible: boolean;
  scale_factor: number;
  offset?: number | null;
}

export interface Execution_UCUMUnitValue {
  value: number;
  unit: string;
}

export type Execution_UDICarrierFormat =
  "GS1_128" | "GS1_DATAMATRIX" | "HIBCC_128" | "HIBCC_DATAMATRIX" | "RAW_UDI";

export interface Execution_UDIParseRequest {
  raw_barcode: string;
}

export interface Execution_UnblindRequest {
  reason_code: Execution_UnblindingReasonCode;
  justification: string;
  shares: Execution_CustodianShare[];
}

export type Execution_UnblindingReasonCode =
  | "SAE-Life-Threatening-Event"
  | "Accidental-Overdose"
  | "Required-by-Regulatory-Authority";

export interface Execution_UnitConversionRequest {
  value: number;
  from_unit: string;
  to_unit: string;
}

export interface Execution_UnitConversionResponse {
  value: number;
  from_unit: string;
  to_unit: string;
  converted_value: number;
}

export interface Execution_UploadEISFDocumentRequest {
  study_id: string;
  site_id: string;
  category: Execution_EISFTaxonomyCategoryEnum;
  title: string;
  file_name: string;
  content_base64: string;
}

export interface Execution_ValidateGatingRequest {
  subject_id: string;
  target_visit_id: string;
  active_protocol_version: string;
  requires_reconsent?: boolean;
}

export interface Execution_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Execution_VerifyRequest {
  signed_data: string;
}

export interface Execution_VerifyResponse {
  is_valid: boolean;
  status: string;
  failure_reason?: string;
}

export interface Execution_VerifyTrainingGatingRequest {
  study_id: string;
  protocol_version: string;
  user_id: string;
  site_id?: string | null;
  user_role?: string | null;
}

export interface Execution_VisitCreate {
  subject_id: string;
  visit_name: string;
  study_id: string;
  visit_date?: string | null;
  planned_date?: string | null;
  window_start?: string | null;
  window_end?: string | null;
  window_status?: string | null;
}

export interface Execution_VisitDetailResponse {
  id: string;
  subject_id: string;
  visit_name: string;
  visit_date: string;
  study_id: string;
  treatment_group?: string | null;
  randomization_seed?: string | null;
  investigational_product_id?: string | null;
  planned_date?: string | null;
  window_start?: string | null;
  window_end?: string | null;
  window_status?: string | null;
}

export interface Execution_VisitResponse {
  id: string;
  subject_id: string;
  visit_name: string;
  visit_date: string;
  study_id: string;
  protocol_version_tag?: string | null;
  protocol_version_index?: number | null;
  planned_date?: string | null;
  window_start?: string | null;
  window_end?: string | null;
  window_status?: string | null;
}

export type Execution_VotingRule =
  "UNANIMOUS" | "MAJORITY_2_OF_3" | "CHAIR_TIE_BREAKER" | "SUPERMAJORITY";

export interface Execution_WHODrugATCContext {
  atc_code: string;
  description: string;
  code?: string | null;
  text?: string | null;
}

export interface Execution_WHODrugCodeLookupResponse {
  status: "AUTO-CODED" | "SUGGESTIONS" | "UNCODABLE";
  matches: Execution_WHODrugMatch[];
}

export interface Execution_WHODrugIngredientItem {
  ingredient_code: string;
  ingredient_name: string;
  code?: string | null;
  name?: string | null;
}

export interface Execution_WHODrugMatch {
  drug_code: string;
  preferred_name: string;
  drug_name?: string | null;
  score: number;
  ingredients?: Execution_WHODrugIngredientItem[];
  atc_context?: Execution_WHODrugATCContext[];
  code?: string | null;
  name?: string | null;
  atc?: Execution_WHODrugATCContext[];
}

export interface Fileshare_ActivityFeedEntryResponse {
  event_type: string;
  actor_user_id: string;
  actor_display_name: string;
  timestamp: string;
  detail?: Record<string, any> | null;
}

export interface Fileshare_ActivityFeedResponse {
  entries: Fileshare_ActivityFeedEntryResponse[];
  total: number;
  page: number;
  page_size: number;
}

export type Fileshare_ApprovalStatus =
  "PENDING_APPROVAL" | "APPROVED" | "REJECTED" | "APPROVAL_RESCINDED";

export type Fileshare_ConversionStatus =
  "NOT_REQUIRED" | "PENDING" | "READY" | "FAILED";

export interface Fileshare_FileApproveRequest {
  signing_reason: string;
  credential: string;
}

export interface Fileshare_FileAuditLogResponse {
  id: string;
  file_record_id: string;
  action: string;
  user_id: string;
  timestamp: string;
  reason_for_change: string;
  detail?: Record<string, any> | null;
}

export interface Fileshare_FileDeleteRequest {
  reason_for_change?: string;
}

export interface Fileshare_FileDownloadUrlResponse {
  file_id: string;
  filename: string;
  mime_type: string;
  download_url: string;
  expires_in?: number;
  is_watermarked?: boolean;
  access_count?: number | null;
  is_rendition?: boolean;
  rendition_key?: string | null;
}

export interface Fileshare_FileHoldClearRequest {
  reason_for_change: string;
}

export interface Fileshare_FileHoldRequest {
  reason_for_change: string;
  hold_reason?: string | null;
}

export interface Fileshare_FileRecordResponse {
  id: string;
  study_id: string;
  site_id: string | null;
  filename: string;
  mime_type: string;
  size_bytes: number;
  object_key: string;
  checksum_sha256: string | null;
  version_index: number;
  uploaded_by: string;
  uploaded_at: string;
  is_on_hold: boolean;
  hold_reason?: string | null;
  status?: Fileshare_FileStatus;
  scan_status?: Fileshare_ScanStatus;
  scan_engine?: string | null;
  scan_completed_at?: string | null;
  scan_threat_name?: string | null;
  transcode_status?: Fileshare_TranscodeStatus;
  hls_manifest_key?: string | null;
  transcode_error?: string | null;
  pdf_rendition_key?: string | null;
  conversion_status?: Fileshare_ConversionStatus;
  conversion_error?: string | null;
  approval_status?: Fileshare_ApprovalStatus;
  signature_manifestation?: Record<string, any> | null;
  approved_by?: string | null;
  approved_at?: string | null;
  approval_reason?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface Fileshare_FileRejectRequest {
  reason_for_change: string;
}

export interface Fileshare_FileRescindApprovalRequest {
  reason_for_change: string;
}

export type Fileshare_FileStatus =
  "PENDING_SCAN" | "READY" | "PENDING_TRANSCODE" | "QUARANTINED";

export interface Fileshare_FileUploadUrlRequest {
  study_id: string;
  site_id?: string | null;
  filename: string;
  mime_type: string;
  size_bytes: number;
  is_multipart?: boolean;
  parts_count?: number;
  checksum_sha256?: string | null;
  reason_for_change?: string;
}

export interface Fileshare_FileUploadUrlResponse {
  file_id: string;
  object_key: string;
  upload_id?: string | null;
  upload_url?: string | null;
  upload_urls?: Record<string, any> | null;
  expires_in?: number;
  checksum_sha256?: string | null;
  required_headers?: Record<string, any>;
}

export interface Fileshare_GuestLinkCreateRequest {
  expires_in_hours?: number;
  reason_for_change: string;
}

export interface Fileshare_GuestLinkResponse {
  id: string;
  file_record_id: string;
  guest_url: string;
  expires_at: string;
  created_by: string;
  access_count: number;
  is_valid: boolean;
}

export interface Fileshare_HTTPValidationError {
  detail?: Fileshare_ValidationError[];
}

export type Fileshare_PermissionLevel =
  | "view"
  | "comment"
  | "download"
  | "edit"
  | "upload_revision"
  | "reshare"
  | "approve"
  | "admin"
  | "expire_revoke";

export interface Fileshare_PlaybackAuditCreateRequest {
  file_record_id: string;
  event: Fileshare_PlaybackEventType;
  source?: Fileshare_PlaybackEventSource;
  session_id: string;
  guest_token?: string | null;
  reason_for_change?: string;
}

export interface Fileshare_PlaybackAuditResponse {
  id: string;
  file_record_id: string;
  event: Fileshare_PlaybackEventType;
  source: Fileshare_PlaybackEventSource;
  session_id: string;
  user_id: string;
  timestamp: string;
  created_at: string;
  reason_for_change: string;
}

export type Fileshare_PlaybackEventSource = "internal" | "guest_link";

export type Fileshare_PlaybackEventType =
  "PLAY_START" | "PLAY_25" | "PLAY_50" | "PLAY_75" | "PLAY_END";

export type Fileshare_ScanStatus =
  "PENDING" | "CLEAN" | "INFECTED" | "SCAN_ERROR";

export interface Fileshare_ShareGrantCreateRequest {
  granted_to_user_id?: string | null;
  scope?: Fileshare_ShareScope;
  permission_level?: Fileshare_PermissionLevel;
  expires_at?: string | null;
  reason_for_change: string;
}

export interface Fileshare_ShareGrantResponse {
  id: string;
  file_record_id: string;
  granted_to_user_id: string | null;
  granted_by_user_id: string;
  scope: Fileshare_ShareScope;
  permission_level: Fileshare_PermissionLevel;
  expires_at: string | null;
  revoked_at: string | null;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export type Fileshare_ShareScope =
  "study" | "site" | "individual" | "role" | "folder";

export interface Fileshare_StreamUrlResponse {
  file_id: string;
  stream_url?: string | null;
  expires_in?: number;
  transcode_status: Fileshare_TranscodeStatus;
  message?: string | null;
}

export type Fileshare_TranscodeStatus =
  "NOT_REQUIRED" | "PENDING" | "IN_PROGRESS" | "READY" | "FAILED";

export interface Fileshare_UploadCompleteRequest {
  upload_id?: string | null;
  parts?: Record<string, any>[] | null;
  checksum_sha256?: string | null;
  reason_for_change?: string;
}

export interface Fileshare_UploadRevisionRequest {
  filename: string;
  mime_type: string;
  size_bytes: number;
  checksum_sha256?: string | null;
  reason_for_change: string;
  is_multipart?: boolean;
  parts_count?: number;
}

export interface Fileshare_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Imaging_AdjudicationSessionResponseDto {
  session_id: string;
  study_envelope_id?: string | null;
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  assessment_criteria: string;
  workflow_status: string;
  reader_1_status: string;
  reader_2_status: string;
  reader_1_assessment?: Imaging_BlindedReaderAssessmentDto | null;
  reader_2_assessment?: Imaging_BlindedReaderAssessmentDto | null;
  adjudicator_assessment?: Imaging_BlindedReaderAssessmentDto | null;
  sld_discordance_delta?: number;
  is_response_discordant?: boolean;
  discordance_reasons?: string[];
  final_consensus_response?: string | null;
  final_consensus_sld_mm?: number | null;
  binding_reader?: string | null;
  adjudication_rationale?: string | null;
  electronic_signature?: Record<string, any> | null;
  locked_at?: string | string | null;
  locked_by?: string | null;
  session_digest?: string;
  version_index?: number;
}

export interface Imaging_BlindedReaderAssessmentDto {
  reader_id: string;
  reader_name: string;
  reader_role: string;
  timepoint_name: string;
  target_lesions?: Imaging_LesionMeasurementDto[];
  non_target_status: string;
  new_lesions_present: boolean;
  new_lesions_count: number;
  sum_longest_diameters_mm: number;
  overall_response: string;
  assessment_notes?: string | null;
  submitted_at: string;
  digital_signature: string;
}

export interface Imaging_Body_deidentify_in_flight_api_v1_imaging_deidentify_post {
  file: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  date_shift_days?: number;
  apply_pixel_mask?: boolean;
}

export interface Imaging_Body_upload_dicom_api_v1_imaging_upload_post {
  file: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  date_shift_days?: number;
  apply_pixel_mask?: boolean;
  custom_pixel_masks_json?: string | null;
}

export interface Imaging_CaliperAnnotationCreateDto {
  study_envelope_id?: string | null;
  series_envelope_id?: string | null;
  sop_instance_uid?: string | null;
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  lesion_id: string;
  lesion_type?: string;
  organ_site: string;
  measurement_criteria?: string;
  measurement_type?: string;
  longest_diameter_mm: number;
  perpendicular_diameter_mm?: number | null;
  volume_mm3?: number | null;
  slice_index?: number | null;
  slice_location?: number | null;
  deauville_score?: number | null;
  points?: Imaging_PointCoordinateDto[];
  hounsfield_stats?: Record<string, any> | null;
  notes?: string | null;
}

export interface Imaging_CaliperAnnotationResponseDto {
  id: string;
  study_envelope_id?: string | null;
  series_envelope_id?: string | null;
  sop_instance_uid?: string | null;
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  lesion_id: string;
  lesion_type: string;
  organ_site: string;
  measurement_criteria: string;
  measurement_type: string;
  longest_diameter_mm: number;
  perpendicular_diameter_mm?: number | null;
  product_perpendicular_mm2?: number | null;
  volume_mm3?: number | null;
  slice_index?: number | null;
  slice_location?: number | null;
  deauville_score?: number | null;
  points?: Imaging_PointCoordinateDto[];
  hounsfield_stats?: Record<string, any> | null;
  reader_id: string;
  notes?: string | null;
  created_at?: string | null;
  created_by: string;
  version_index: number;
}

export interface Imaging_CertificateVerificationResponse {
  certificate_id: string;
  is_valid: boolean;
  source_sha256: string;
  target_sha256: string;
  subject_id: string;
  site_id: string;
  study_id: string;
  scrubbed_tags_count: number;
  pixel_mask_applied: boolean;
  timestamp: string;
  operator_identity: string;
  message: string;
}

export interface Imaging_CreateAdjudicationSessionDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  study_envelope_id?: string | null;
  assessment_criteria?: string;
  reader_1_id: string;
  reader_1_name?: string;
  reader_2_id: string;
  reader_2_name?: string;
  adjudicator_id?: string | null;
  adjudicator_name?: string | null;
}

export interface Imaging_DicomDeidCertificate {
  certificate_id?: string;
  source_sha256: string;
  target_sha256: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  original_study_instance_uid_hash: string;
  original_sop_instance_uid_hash: string;
  anonymized_study_instance_uid: string;
  anonymized_sop_instance_uid: string;
  scrubbed_tags_count: number;
  scrubbed_tags?: string[];
  pixel_mask_applied?: boolean;
  pixel_masks?: Imaging_PixelMaskBoundingBox[];
  operator_identity?: string;
  timestamp?: string;
  signature?: string | null;
}

export interface Imaging_DicomUploadResponse {
  study_envelope_id: string;
  study_instance_uid: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  processed_instances_count: number;
  processed_series_count: number;
  modalities: string[];
  certificates: Imaging_DicomDeidCertificate[];
}

export interface Imaging_EDCTumorBindingResponseDto {
  form_id?: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  assessment_criteria: string;
  target_lesions?: Record<string, any>[];
  sum_longest_diameters_mm?: number | null;
  baseline_sld_mm?: number | null;
  nadir_sld_mm?: number | null;
  pct_change_baseline?: number | null;
  pct_change_nadir?: number | null;
  non_target_status: string;
  new_lesions_present: boolean;
  overall_response: string;
  adjudication_status: string;
  binding_source: string;
  manifest_sha256: string;
  signed_by?: string | null;
  signed_at?: string | null;
}

export interface Imaging_HTTPValidationError {
  detail?: Imaging_ValidationError[];
}

export interface Imaging_InstanceEnvelopeSummaryDto {
  id: string;
  sop_instance_uid: string;
  sop_class_uid: string;
  instance_number?: number | null;
  rows: number;
  columns: number;
  bits_allocated: number;
  pixel_spacing?: number[] | null;
  slice_thickness?: number | null;
  slice_location?: number | null;
  object_key: string;
  checksum_sha256: string;
  size_bytes: number;
  burned_in_phi_masked: boolean;
  certificate_id?: string | null;
}

export interface Imaging_LesionMeasurementDto {
  lesion_id: string;
  organ_site: string;
  longest_diameter_mm: number;
  short_axis_mm?: number | null;
  slice_location?: number | null;
  slice_index?: number | null;
  sop_instance_uid?: string;
  deauville_score?: number | null;
}

export interface Imaging_LuganoEvaluationRequestDto {
  current_lesions: Imaging_LuganoLesionInputDto[];
  baseline_spd_mm2?: number;
  prior_timepoints_spd?: number[];
  new_lesions_present?: boolean;
  single_lesion_progression?: boolean;
  non_measured_progression?: boolean;
  is_baseline?: boolean;
}

export interface Imaging_LuganoEvaluationResponseDto {
  criteria?: string;
  baseline_spd_mm2: number;
  nadir_spd_mm2: number;
  current_spd_mm2: number;
  pct_change_spd_baseline: number;
  pct_change_spd_nadir: number;
  ct_response: string;
  pet_metabolic_response?: string | null;
  max_deauville_score?: number | null;
  new_lesions_present: boolean;
  overall_response: string;
}

export interface Imaging_LuganoLesionInputDto {
  lesion_id: string;
  organ_site: string;
  longest_diameter_mm: number;
  perpendicular_diameter_mm: number;
  is_extranodal?: boolean;
  deauville_score?: number | null;
}

export interface Imaging_MprMetadataResponseDto {
  series_instance_uid: string;
  modality: string;
  rows: number;
  columns: number;
  slice_count: number;
  pixel_spacing: number[];
  slice_thickness: number;
  slice_spacing: number;
  volume_dimensions_mm: number[];
  window_center?: number | null;
  window_width?: number | null;
  available_planes: string[];
  window_presets: Record<string, any>;
}

export interface Imaging_PixelMaskBoundingBox {
  x_min: number;
  y_min: number;
  x_max: number;
  y_max: number;
}

export interface Imaging_PointCoordinateDto {
  x: number;
  y: number;
  z?: number | null;
}

export interface Imaging_PresignedDownloadUrlResponse {
  sop_instance_uid: string;
  download_url: string;
  expires_in_seconds?: number;
}

export interface Imaging_RECISTEvaluationRequestDto {
  current_lesions: Imaging_RECISTLesionInputDto[];
  baseline_sld_mm?: number;
  prior_timepoints_sld?: number[];
  non_target_status?: string;
  new_lesions_present?: boolean;
  new_lesions_count?: number;
  is_baseline?: boolean;
}

export interface Imaging_RECISTEvaluationResponseDto {
  criteria?: string;
  baseline_sld_mm: number;
  nadir_sld_mm: number;
  current_sld_mm: number;
  pct_change_baseline: number;
  pct_change_nadir: number;
  absolute_change_nadir_mm: number;
  target_response: string;
  non_target_status: string;
  new_lesions_present: boolean;
  new_lesions_count: number;
  overall_response: string;
  is_nadir: boolean;
}

export interface Imaging_RECISTLesionInputDto {
  lesion_id: string;
  organ_site: string;
  lesion_type?: string;
  longest_diameter_mm: number;
  is_lymph_node?: boolean;
  short_axis_mm?: number | null;
  sop_instance_uid?: string | null;
}

export interface Imaging_SDTMDatasetExportResponseDto {
  study_id: string;
  subject_id: string;
  timepoint_name: string;
  tu_records?: Record<string, any>[];
  tr_records?: Record<string, any>[];
  rs_records?: Record<string, any>[];
  total_records?: number;
}

export interface Imaging_SeriesEnvelopeSummaryDto {
  id: string;
  series_instance_uid: string;
  series_number?: number | null;
  modality: string;
  body_part_examined?: string | null;
  series_description?: string | null;
  instance_count: number;
  protocol_name?: string | null;
  instances?: Imaging_InstanceEnvelopeSummaryDto[];
}

export interface Imaging_StepUpAdjudicationSignOffDto {
  signer_id: string;
  signer_name: string;
  password?: string | null;
  step_up_token?: string | null;
  signing_reason?: string;
  binding_reader?: string;
  rationale: string;
  adjudicator_assessment?: Imaging_SubmitReaderAssessmentDto | null;
}

export interface Imaging_StudyEnvelopeDetailDto {
  id: string;
  study_instance_uid: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  modalities: string[];
  study_date?: string | null;
  study_description?: string | null;
  series_count: number;
  instance_count: number;
  status: string;
  adjudication_status: string;
  tenant_id: string;
  created_at?: string | null;
  created_by: string;
  version_index: number;
  series?: Imaging_SeriesEnvelopeSummaryDto[];
  certificate_ids?: string[];
}

export interface Imaging_StudyEnvelopeSummaryDto {
  id: string;
  study_instance_uid: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  modalities: string[];
  study_date?: string | null;
  study_description?: string | null;
  series_count: number;
  instance_count: number;
  status: string;
  adjudication_status: string;
  tenant_id: string;
  created_at?: string | null;
  created_by: string;
  version_index: number;
}

export interface Imaging_SubjectTimepointEvaluateRequestDto {
  study_id: string;
  site_id: string;
  timepoint_name: string;
  assessment_criteria?: string;
  non_target_status?: string;
  new_lesions_present?: boolean;
  new_lesions_count?: number;
  is_baseline?: boolean;
  digital_signature?: string | null;
}

export interface Imaging_SubjectTimepointsTrajectoryResponseDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  trajectory?: Imaging_TimepointTrajectorySummaryDto[];
  active_annotations_count?: number;
}

export interface Imaging_SubmitReaderAssessmentDto {
  reader_id: string;
  reader_role: string;
  reader_name?: string;
  target_lesions?: Imaging_LesionMeasurementDto[];
  non_target_status?: string;
  new_lesions_present?: boolean;
  new_lesions_count?: number;
  sum_longest_diameters_mm?: number;
  baseline_sld_mm?: number | null;
  nadir_sld_mm?: number | null;
  overall_response: string;
  assessment_notes?: string | null;
  digital_signature?: string;
}

export interface Imaging_TimepointTrajectorySummaryDto {
  id: string;
  timepoint_name: string;
  assessment_criteria: string;
  baseline_sld_mm?: number | null;
  nadir_sld_mm?: number | null;
  current_sld_mm?: number | null;
  pct_change_baseline?: number | null;
  pct_change_nadir?: number | null;
  baseline_spd_mm2?: number | null;
  nadir_spd_mm2?: number | null;
  current_spd_mm2?: number | null;
  pct_change_spd_baseline?: number | null;
  pct_change_spd_nadir?: number | null;
  target_response: string;
  non_target_status: string;
  new_lesions_present: boolean;
  new_lesions_count: number;
  max_deauville_score?: number | null;
  overall_response: string;
  is_locked: boolean;
  locked_at?: string | null;
  reader_id: string;
  created_at?: string | null;
}

export interface Imaging_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Interop_ADaMPipelineResponse {
  study_id: string;
  snapshot_id: string | null;
  dataset_counts: Record<string, any>;
  total_records: number;
  datasets: Record<string, any>;
}

export interface Interop_AcknowledgeAlertRequest {
  notes: string;
  audit_signature?: string | null;
}

export interface Interop_AcknowledgeAlertResponse {
  success?: boolean;
  alert: Interop_SensorSafetyAlert;
  message?: string;
}

export interface Interop_AcknowledgeNotificationRequest {
  reason_for_change: string;
}

export interface Interop_ActigraphyDailySummary {
  total_steps: number;
  mean_cadence_spm: number;
  peak_cadence_spm: number;
  active_minutes: number;
  sedentary_minutes: number;
  sleep_duration_minutes: number;
  sleep_efficiency_percent: number;
  resting_heart_rate_bpm: number;
  readings_count: number;
}

export type Interop_AlertSeverity =
  "INFO" | "WARNING" | "CRITICAL" | "LIFE_THREATENING";

export interface Interop_ArchiveRecordingRequest {
  media_payload_or_hash: string;
  duration_seconds: number;
  resolution?: string;
  recorded_by: string;
  participant_manifest?: Record<string, any>[];
}

export interface Interop_ArchiveRecordingResponse {
  success: boolean;
  recording_id: string;
  media_sha256_hash: string;
  digital_seal_signature: string;
  sealed_at_utc: string;
  part11_certification_statement: string;
}

export interface Interop_AssignmentComplianceDetailDTO {
  assignment_id: string;
  instrument_id: string;
  instrument_name: string;
  status: string;
  due_at: string | null;
  end_date: string;
  submitted_at?: string | null;
}

export interface Interop_BloodPressureDailySummary {
  mean_systolic_mmhg: number;
  mean_diastolic_mmhg: number;
  mean_arterial_pressure_mmhg: number;
  morning_systolic_mmhg?: number | null;
  evening_systolic_mmhg?: number | null;
  readings_count: number;
}

export interface Interop_BulkSyncPayload {
  submissions: Interop_EPROSubmissionPayload[];
}

export interface Interop_CGMDailySummary {
  tir_percentage: number;
  tbr_level1_percentage: number;
  tbr_level2_percentage: number;
  tar_level1_percentage: number;
  tar_level2_percentage: number;
  mean_glucose_mg_dl: number;
  glucose_sd: number;
  glucose_cv_percent: number;
  glucose_management_indicator: number;
  readings_count: number;
  active_wear_percentage: number;
}

export type Interop_CalibrationState =
  "CALIBRATED" | "PENDING_CALIBRATION" | "EXPIRED" | "FACTORY_CALIBRATED";

export interface Interop_CanonicalClinicalRecord {
  record_id?: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  stream_type: Interop_DataStreamType;
  domain: string;
  item_code: string;
  item_name: string;
  observation_datetime: string;
  numeric_value?: number | null;
  text_value?: string | null;
  unit?: string | null;
  reference_low?: number | null;
  reference_high?: number | null;
  abnormal_flag?: string | null;
  is_unblinded?: boolean;
  raw_payload?: Record<string, any>;
  normalized_attributes?: Record<string, any>;
  data_hash: string;
  created_at?: string;
}

export interface Interop_CentralLabIngestItem {
  subject_id: string;
  site_id?: string | null;
  specimen_id: string;
  test_code: string;
  test_name: string;
  numeric_value?: number | null;
  string_value?: string | null;
  unit?: string | null;
  reference_low?: number | null;
  reference_high?: number | null;
  abnormal_flag?: string | null;
  collection_timestamp: string;
  accession_number?: string | null;
  is_unblinded?: boolean;
  metadata?: Record<string, any>;
}

export interface Interop_ClinicalSnapshot {
  snapshot_id?: string;
  study_id: string;
  version_tag: string;
  milestone: Interop_LockMilestoneType;
  lock_id?: string | null;
  record_count?: number;
  stream_counts?: Record<string, any>;
  domain_counts?: Record<string, any>;
  sha256_digest: string;
  created_at?: string;
  created_by: string;
  reason_for_snapshot: string;
  is_locked?: boolean;
  blinded_record_count?: number;
  unblinded_record_count?: number;
  snapshot_manifest?: Record<string, any>;
}

export interface Interop_CoViewStateRequest {
  document_id: string;
  page_index?: number;
  clause_id?: string | null;
  presenter_id: string;
  presenter_role?: Interop_ParticipantRole;
  scroll_percent?: number;
  active_highlights?: Record<string, any>[];
  lock_presenter_control?: boolean;
}

export interface Interop_ComprehensionAnswerDTO {
  question_id: string;
  selected_option: string;
  correct_option: string;
}

export interface Interop_ConceptMapListResponse {
  total_concepts: number;
  domains_supported: string[];
  concept_maps: Interop_ConceptMapSummaryDTO[];
}

export interface Interop_ConceptMapSummaryDTO {
  source_system: string;
  source_code: string;
  source_display: string;
  target_domain: string;
  target_variable: string;
  cdash_testcd: string;
  cdash_test: string;
  standard_unit?: string | null;
  category?: string | null;
  description?: string | null;
}

export type Interop_ConflictStrategy = "CLIENT_WINS" | "SERVER_WINS" | "MERGE";

export interface Interop_CreateProgramRequest {
  study_id: string;
  name: string;
  title: string;
  description?: string;
  language: Interop_ExecutionLanguage;
  version?: string;
  source_code: string;
  lockfile_content?: string | null;
  lock_type?: Interop_PackageLockType;
  target_domain?: string | null;
  reason_for_change?: string;
}

export interface Interop_CreateSnapshotRequest {
  study_id: string;
  version_tag: string;
  milestone: Interop_LockMilestoneType;
  lock_id?: string | null;
  reason_for_snapshot: string;
  filter_criteria?: Interop_SnapshotFilterCriteria | null;
}

export interface Interop_CriterionExplanation {
  criterion_id: string;
  criterion_type: string;
  description: string;
  is_met: boolean;
  is_indeterminate: boolean;
}

export interface Interop_CustodyHandoffRequest {
  stage: string;
  released_by: string;
  received_by: string;
  tamper_seal_intact?: boolean;
  current_temperature_celsius: number;
  notes?: string;
}

export interface Interop_DCTMetricsResponse {
  success: boolean;
  study_id: string;
  total_telehealth_sessions: number;
  active_video_rooms: number;
  total_dtp_shipments: number;
  active_cold_chain_shipments: number;
  temperature_excursion_alerts: number;
  scheduled_nurse_visits: number;
  completed_home_visits: number;
  pending_offline_syncs: number;
  pediatric_assents_completed: number;
}

export interface Interop_DTPShipmentCreateRequest {
  study_id: string;
  site_id: string;
  subject_pseudonym_id: string;
  destination_address_pseudonym: string;
  courier_name?: string;
  temperature_zone?: Interop_TemperatureZone;
  kit_serials: string[];
  tamper_seal_id: string;
}

export interface Interop_DTPShipmentDTO {
  shipment_id: string;
  tracking_number: string;
  courier_name: string;
  study_id: string;
  site_id: string;
  subject_pseudonym_id: string;
  destination_address_pseudonym: string;
  temperature_zone: Interop_TemperatureZone;
  kit_serials: string[];
  tamper_seal_id: string;
  status: Interop_DTPShipmentStatus;
  current_temp_celsius: number;
  min_temp_celsius: number;
  max_temp_celsius: number;
  mkt_celsius: number;
  excursion_severity: Interop_ExcursionSeverity;
  custody_handoffs: Record<string, any>[];
  temperature_readings: Record<string, any>[];
  delivery_signature: Record<string, any> | null;
  created_at: string;
}

export interface Interop_DTPShipmentListResponse {
  success: boolean;
  shipments: Interop_DTPShipmentDTO[];
  total: number;
}

export interface Interop_DTPShipmentResponse {
  success: boolean;
  shipment: Interop_DTPShipmentDTO;
  message?: string;
}

export type Interop_DTPShipmentStatus =
  | "ORDER_CREATED"
  | "DEPOT_PICKED"
  | "PACKED"
  | "COURIER_DISPATCHED"
  | "IN_TRANSIT"
  | "OUT_FOR_DELIVERY"
  | "DELIVERED"
  | "TEMP_EXCURSION_HOLD"
  | "RETURNED";

export interface Interop_DailySummariesResponse {
  study_id: string;
  total_count: number;
  summaries?: Interop_DailySummaryEndpoints[];
}

export interface Interop_DailySummaryEndpoints {
  summary_id?: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  summary_date: string;
  device_type: Interop_SensorDeviceType;
  device_token: string;
  cgm?: Interop_CGMDailySummary | null;
  actigraphy?: Interop_ActigraphyDailySummary | null;
  spirometry?: Interop_SpirometryDailySummary | null;
  blood_pressure?: Interop_BloodPressureDailySummary | null;
  lakehouse_record_id?: string | null;
  data_hash: string;
  created_at?: string;
}

export type Interop_DataStreamType =
  | "EDC_ECRF"
  | "EPRO_ECOA"
  | "CENTRAL_LAB"
  | "LOCAL_LAB"
  | "IOT_WEARABLE"
  | "EHR_FHIR"
  | "MEDICAL_IMAGING";

export interface Interop_DatabaseLockAuthToken {
  token_id: string;
  study_id: string;
  lock_id: string;
  milestone: string;
  issued_to: string;
  authorized_roles: string[];
  issued_at: string;
  expires_at: string;
  signature_hash: string;
  is_revoked?: boolean;
  reason: string;
}

export interface Interop_DatasetJSONClinicalData {
  studyOID: string;
  metaDataVersionOID: string;
  metaDataRef?: string | null;
  itemGroupData: Record<string, any>;
}

export interface Interop_DatasetJSONDocument {
  datasetJSONCreationDateTime?: string;
  datasetJSONVersion?: string;
  fileOID: string;
  dbLastModifiedDateTime?: string | null;
  originator?: string;
  sourceSystem?: string;
  sourceSystemVersion?: string;
  clinicalData: Interop_DatasetJSONClinicalData;
}

export interface Interop_DatasetJSONItem {
  OID?: string | null;
  name: string;
  label: string;
  type: string;
  length?: number | null;
  displayFormat?: string | null;
  keySequence?: number | null;
  mandatory?: boolean | string | null;
}

export interface Interop_DatasetJSONItemGroup {
  itemGroupOID: string;
  name: string;
  label: string;
  records: number;
  items: Interop_DatasetJSONItem[];
  itemData: any[][];
}

export interface Interop_DefineXMLValidationResult {
  is_valid: boolean;
  errors?: string[];
  warnings?: string[];
  item_group_count?: number;
  item_def_count?: number;
  value_level_metadata_count?: number;
  where_clause_count?: number;
  codelist_count?: number;
  method_count?: number;
}

export interface Interop_DeliveryConfirmRequest {
  recipient_name: string;
  signature_manifestation: string;
  tamper_seal_verified?: boolean;
  logger_greenlight?: boolean;
}

export interface Interop_DeviceInvestigationExportPackageRequest {
  study_id: string;
  devices?: Record<string, any>[];
  deficiencies?: Record<string, any>[];
}

export interface Interop_DeviceInvestigationExportPackageResponse {
  study_id: string;
  generated_at: string;
  sdtm_dd_count: number;
  sdtm_do_count: number;
  sdtm_dd_dataset: Record<string, any>[];
  sdtm_do_dataset: Record<string, any>[];
  incident_reports: Record<string, any>[];
  audit_manifest: Record<string, any>;
}

export interface Interop_DeviceListResponse {
  study_id: string;
  total_count: number;
  devices?: Interop_DeviceRegistration[];
}

export interface Interop_DeviceRegistration {
  device_id?: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  device_type: Interop_SensorDeviceType;
  vendor: string;
  model_name: string;
  udi_di: string;
  udi_pi_serial?: string | null;
  udi_pi_lot?: string | null;
  mac_address_masked?: string | null;
  device_token: string;
  firmware_version?: string | null;
  calibration_status: Interop_CalibrationState;
  calibration_date?: string | null;
  is_active?: boolean;
  created_at?: string;
  created_by?: string;
  reason_for_change: string;
  version_index?: number;
}

export interface Interop_DeviceRegistrationRequest {
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  device_type: Interop_SensorDeviceType;
  vendor: string;
  model_name: string;
  device_udi?: string | null;
  mac_address?: string | null;
  serial_number?: string | null;
  firmware_version?: string | null;
  calibration_status?: Interop_CalibrationState;
  calibration_date?: string | null;
  reason_for_change?: string;
}

export interface Interop_DeviceRegistrationResponse {
  success?: boolean;
  device: Interop_DeviceRegistration;
  message?: string;
}

export interface Interop_DomainCatalogResponse {
  sdtm_domains: string[];
  adam_datasets: string[];
  domain_metadata: Record<string, any>;
  vlm_catalog: Record<string, any>;
  codelist_catalog: string[];
}

export interface Interop_EDCEcrfIngestItem {
  subject_id: string;
  visit_id: string;
  form_oid: string;
  field_name: string;
  value: any;
  unit?: string | null;
  recorded_at: string;
  status?: string;
  investigator_id?: string | null;
  site_id?: string | null;
  domain?: string | null;
  is_unblinded?: boolean;
  metadata?: Record<string, any>;
}

export interface Interop_EHRFHIRIngestItem {
  subject_id: string;
  resource_type: string;
  fhir_id: string;
  resource_payload: Record<string, any>;
  coding_code?: string | null;
  coding_display?: string | null;
  effective_datetime: string;
  site_id?: string | null;
  metadata?: Record<string, any>;
}

export interface Interop_EPROEcoaIngestItem {
  subject_id: string;
  diary_id: string;
  questionnaire_name: string;
  answers: Record<string, any>;
  total_score?: number | null;
  device_timestamp: string;
  completed_at?: string | null;
  compliance_flag?: boolean;
  site_id?: string | null;
  metadata?: Record<string, any>;
}

export interface Interop_EPROSubmissionPayload {
  subject_id: string;
  diary_id: string;
  version_index?: number;
  device_timestamp: string;
  answers: Record<string, any>;
  offline_sync_markers: Interop_OfflineSyncMarkers;
}

export interface Interop_EditQuarantinedSubmissionRequest {
  answers: Record<string, any>;
  password: string;
  change_reason: string;
}

export interface Interop_EhrCandidateItemDTO {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  subject_pseudonym: string;
  ehr_system_id: string;
  fhir_resource_type: string;
  fhir_resource_id: string;
  source_timestamp: string | string;
  target_domain: string;
  target_variable: string;
  cdash_testcd?: string | null;
  cdash_test?: string | null;
  candidate_value: any;
  candidate_unit?: string | null;
  current_ecrf_value?: any | null;
  current_ecrf_updated_at?: string | string | null;
  discrepancy_type?: string;
  confidence_score?: number;
  mapping_provenance: string;
  raw_fhir_resource?: Record<string, any>;
  status?: string;
  resolution_action?: string | null;
  resolution_reason?: string | null;
  resolved_by?: string | null;
  resolved_at?: string | string | null;
  electronic_signature?: Record<string, any> | null;
  clinical_query_id?: string | null;
  created_at?: string | string | null;
  version_index?: number;
}

export interface Interop_EhrCandidateListResponse {
  total_count: number;
  candidates: Interop_EhrCandidateItemDTO[];
}

export interface Interop_EhrEndpointCreateRequest {
  name: string;
  system_type?: Interop_EhrSystemType;
  fhir_base_url: string;
  token_url: string;
  client_id: string;
  public_key_jwks_url?: string | null;
  scopes?: string[];
  polling_interval_sec?: number;
  is_active?: boolean;
}

export interface Interop_EhrEndpointResponse {
  endpoint_id: string;
  name: string;
  system_type: string;
  fhir_base_url: string;
  token_url: string;
  client_id: string;
  public_key_jwks_url?: string | null;
  scopes: string[];
  polling_interval_sec: number;
  is_active: boolean;
  last_polled_at?: string | string | null;
  created_at?: string | string | null;
  version_index?: number;
}

export interface Interop_EhrIngestBundleRequest {
  study_id: string;
  site_id?: string;
  ehr_system_id?: string;
  bundle: Record<string, any>;
  current_ecrf_context?: Record<string, any> | null;
}

export interface Interop_EhrIngestBundleResponse {
  study_id: string;
  total_ingested: number;
  candidates: Interop_EhrCandidateItemDTO[];
}

export interface Interop_EhrPollRequest {
  endpoint_id: string;
  study_id: string;
  site_id?: string;
  subject_id: string;
  private_key_pem: string;
  current_ecrf_context?: Record<string, any> | null;
}

export interface Interop_EhrPollResponse {
  study_id: string;
  site_id: string;
  subject_id: string;
  total_ingested: number;
  candidates: Interop_EhrCandidateItemDTO[];
}

export interface Interop_EhrProvenanceResponse {
  candidate_id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  subject_pseudonym: string;
  ehr_system_id: string;
  fhir_resource_type: string;
  fhir_resource_id: string;
  source_timestamp: string | null;
  target_domain: string;
  target_variable: string;
  cdash_testcd: string | null;
  cdash_test: string | null;
  candidate_value: any;
  candidate_unit: string | null;
  current_ecrf_value: any | null;
  discrepancy_type: string;
  confidence_score: number;
  mapping_provenance: string;
  raw_fhir_resource: Record<string, any>;
  status: string;
  resolution_action: string | null;
  resolution_reason: string | null;
  resolved_by: string | null;
  resolved_at: string | null;
  electronic_signature: Record<string, any> | null;
  clinical_query_id: string | null;
  audit_trail?: Record<string, any>[];
}

export interface Interop_EhrReconcileAcceptRequest {
  candidate_id: string;
  signature_token?: string | null;
  commit_reason?: string | null;
  change_reason?: string;
}

export interface Interop_EhrReconcileActionResponse {
  success?: boolean;
  candidate: Interop_EhrCandidateItemDTO;
  clinical_query_id?: string | null;
  message: string;
}

export interface Interop_EhrReconcileQueryRequest {
  candidate_id: string;
  query_explanation: string;
  discrepancy_type?: string;
  change_reason?: string;
}

export interface Interop_EhrReconcileRejectRequest {
  candidate_id: string;
  rejection_reason: string;
  change_reason?: string;
}

export type Interop_EhrSystemType =
  "EPIC" | "CERNER" | "ALLSCRIPTS" | "SMART_FHIR_GENERIC";

export interface Interop_ElectronicSignatureBlock {
  signer_id: string;
  signer_role: string;
  signed_at?: string;
  signature_meaning?: string;
  hmac_signature: string;
}

export type Interop_ExcursionSeverity =
  "NONE" | "MINOR_WARNING" | "CRITICAL_EXCURSION_QUARANTINE";

export interface Interop_ExecuteProgramRequest {
  study_id: string;
  program_id: string;
  snapshot_id: string;
  is_unblinded?: boolean;
  database_lock_token_id?: string | null;
  sandbox_config?: Interop_ExecutionSandboxConfig | null;
  reason_for_execution?: string;
}

export interface Interop_ExecuteProgramResponse {
  manifest: Interop_ExecutionRunManifest;
}

export type Interop_ExecutionLanguage = "PYTHON" | "R";

export interface Interop_ExecutionRunDetailResponse {
  run: Interop_ExecutionRunManifest;
}

export interface Interop_ExecutionRunListResponse {
  study_id: string;
  total: number;
  runs?: Interop_ExecutionRunManifest[];
}

export interface Interop_ExecutionRunManifest {
  manifest_id?: string;
  run_id: string;
  study_id: string;
  program_id: string;
  program_name: string;
  program_version: string;
  snapshot_id: string;
  snapshot_version_tag: string;
  snapshot_digest: string;
  language: Interop_ExecutionLanguage;
  script_code_digest: string;
  lockfile_digest: string;
  container_image_digest: string;
  execution_start: string;
  execution_end: string;
  duration_ms: number;
  exit_code: number;
  status: Interop_ExecutionStatus;
  stdout_capture?: string;
  stderr_capture?: string;
  output_artifacts?: Interop_OutputArtifact[];
  output_artifacts_digest: string;
  is_unblinded_run?: boolean;
  database_lock_token_id?: string | null;
  executed_by: string;
  executed_by_role: string;
  signature_block: Interop_ElectronicSignatureBlock;
  manifest_sha256_seal: string;
}

export interface Interop_ExecutionSandboxConfig {
  max_memory_mb?: number;
  timeout_seconds?: number;
  network_isolated?: boolean;
  filesystem_readonly_root?: boolean;
  max_output_bytes?: number;
  docker_image_digest?: string;
  python_version?: string;
  r_version?: string;
  environment_variables?: Record<string, any>;
}

export type Interop_ExecutionStatus =
  "PENDING" | "RUNNING" | "COMPLETED" | "FAILED" | "TIMEOUT" | "REJECTED";

export interface Interop_ExportDatasetJSONRequest {
  study_id: string;
  snapshot_id?: string | null;
  datasets?: string[] | null;
  dataset_json_version?: string;
  reason_for_change?: string;
}

export interface Interop_ExportDefineXMLRequest {
  study_id: string;
  study_name?: string | null;
  protocol_name?: string | null;
  metadata_version_oid?: string;
  standard_name?: string;
  standard_version?: string;
  domains?: string[];
  adam_datasets?: string[];
  include_vlm?: boolean;
  include_where_clauses?: boolean;
  include_origins?: boolean;
  reason_for_change?: string;
}

export interface Interop_FHIRPreScreenRequest {
  study_id: string;
  bundle: Record<string, any>;
}

export interface Interop_FHIRPreScreenResponse {
  eligible?: boolean | null;
  failed_criteria?: string[];
  indeterminate_criteria?: string[];
  criteria_explanations?: Interop_CriterionExplanation[];
}

export interface Interop_FHIRPrefillRequest {
  study_id: string;
  bundle: Record<string, any>;
}

export interface Interop_FHIRSemanticMapRequest {
  study_id: string;
  bundle: Record<string, any>;
  enable_deterministic?: boolean;
  enable_embedding?: boolean;
  enable_llm_fallback?: boolean;
  embedding_confidence_threshold?: number;
  llm_confidence_threshold?: number;
  human_review_confidence_threshold?: number;
}

export interface Interop_FHIRSemanticMapResponse {
  study_id: string;
  subject_pseudonym: string;
  mapped_fields?: Record<string, any>;
  mapped_items?: Interop_SemanticMappedItemDTO[];
  clinical_records?: Record<string, any>;
  statistics?: Interop_MappingTierStatisticsDTO;
}

export interface Interop_HTTPValidationError {
  detail?: Interop_ValidationError[];
}

export interface Interop_IOTWearableIngestItem {
  subject_id: string;
  device_id: string;
  device_type: string;
  observation_type: string;
  telemetry_readings: Interop_IoTTelemetryReading[];
  duration_seconds?: number | null;
  sample_rate_hz?: number | null;
  calibration_status?: string;
  site_id?: string | null;
  metadata?: Record<string, any>;
}

export interface Interop_InHomeVisitCreateRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_number: string;
  visit_name: string;
  nurse_id: string;
  nurse_name: string;
  nurse_license_number: string;
  scheduled_start_utc: string;
  procedures?: Interop_ProtocolProcedureDTO[];
}

export interface Interop_InHomeVisitDTO {
  visit_id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_number: string;
  visit_name: string;
  nurse_id: string;
  nurse_name: string;
  nurse_license_number: string;
  scheduled_start_utc: string;
  status: Interop_InHomeVisitStatus;
  procedures: Record<string, any>[];
  mobile_ecrf_data: Record<string, any>;
  offline_sync_id: string | null;
  sync_status: string;
  created_at: string;
}

export interface Interop_InHomeVisitListResponse {
  success: boolean;
  visits: Interop_InHomeVisitDTO[];
  total: number;
}

export interface Interop_InHomeVisitResponse {
  success: boolean;
  visit: Interop_InHomeVisitDTO;
  message?: string;
}

export type Interop_InHomeVisitStatus =
  | "SCHEDULED"
  | "CONFIRMED"
  | "EN_ROUTE"
  | "ARRIVED_ON_SITE"
  | "PROCEDURES_IN_PROGRESS"
  | "VISIT_COMPLETED"
  | "CANCELLED"
  | "RESCHEDULED";

export interface Interop_InHomeVisitStatusUpdateRequest {
  status: Interop_InHomeVisitStatus;
  procedures?: Interop_ProtocolProcedureDTO[] | null;
}

export interface Interop_IngestResponse {
  study_id: string;
  stream_type: string;
  records_ingested: number;
  unblinded_records?: number;
  record_ids?: string[];
}

export interface Interop_InstrumentCreateDTO {
  study_id: string;
  name: string;
  description?: string | null;
  items: Record<string, any>;
  response_types: Record<string, any>;
  scoring_metadata: Record<string, any>;
  reason_for_change: string;
}

export interface Interop_InstrumentResponseDTO {
  id: string;
  name: string;
  description: string | null;
  items: Record<string, any>;
  response_types: Record<string, any>;
  scoring_metadata: Record<string, any>;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Interop_IoTTelemetryReading {
  timestamp: string;
  metric_name: string;
  value: number;
  unit?: string | null;
  flag?: string | null;
}

export interface Interop_IssueLockTokenRequest {
  study_id: string;
  lock_id: string;
  milestone: string;
  issued_to: string;
  reason: string;
  expires_in_hours?: number;
  authorized_roles?: string[] | null;
}

export type Interop_LARRelationship =
  | "PARENT"
  | "LEGAL_GUARDIAN"
  | "POWER_OF_ATTORNEY"
  | "COURT_APPOINTED"
  | "NEXT_OF_KIN";

export interface Interop_LakehouseMetrics {
  study_id: string;
  total_records?: number;
  total_snapshots?: number;
  stream_breakdown?: Record<string, any>;
  domain_breakdown?: Record<string, any>;
  subject_count?: number;
  site_count?: number;
  blinded_record_count?: number;
  unblinded_record_count?: number;
  latest_snapshot_digest?: string | null;
  last_ingested_at?: string | null;
}

export interface Interop_LakehouseMetricsResponse {
  metrics: Interop_LakehouseMetrics;
}

export interface Interop_LocalLabIngestItem {
  subject_id: string;
  site_id: string;
  test_code: string;
  test_name: string;
  value: number | string;
  unit?: string | null;
  reference_low?: number | null;
  reference_high?: number | null;
  abnormal_flag?: string | null;
  collection_timestamp: string;
  is_unblinded?: boolean;
  metadata?: Record<string, any>;
}

export type Interop_LockMilestoneType =
  | "INTERIM_ANALYSIS_1"
  | "INTERIM_ANALYSIS_2"
  | "DATABASE_LOCK"
  | "SAFETY_REVIEW"
  | "REGULATORY_SUBMISSION"
  | "PROTOCOL_FREEZE"
  | "ADJUDICATION_MILESTONE"
  | "CUSTOM";

export interface Interop_LockTokenListResponse {
  study_id: string;
  total: number;
  tokens?: Interop_DatabaseLockAuthToken[];
}

export interface Interop_LockTokenResponse {
  token: Interop_DatabaseLockAuthToken;
}

export type Interop_LockValidationStatus =
  | "VALID"
  | "TAMPERED"
  | "INVALID_SYNTAX"
  | "UNQUALIFIED_PACKAGE"
  | "UNVERIFIED";

export interface Interop_MDRIncidentReportRequest {
  study_id: string;
  site_id: string;
  deficiency_id: string;
  brand_name: string;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  incident_date: string;
  awareness_date?: string | null;
  deficiency_type?: string;
  deficiency_description: string;
  safety_classification?: string;
  causality?: string;
  patient_involved?: boolean;
  health_impact?: string;
  root_cause_analysis?: string | null;
  action_taken?: string;
  capa_required?: boolean;
  fsca_required?: boolean;
}

export interface Interop_MDRIncidentReportResponse {
  report_reference_number: string;
  mir_version?: string;
  regulatory_framework?: string;
  reporting_date: string;
  payload_json: Record<string, any>;
  payload_xml: string;
}

export interface Interop_MappingTierStatisticsDTO {
  total_extracted?: number;
  deterministic_count?: number;
  embedding_count?: number;
  llm_fallback_count?: number;
  unmapped_count?: number;
  flagged_for_review_count?: number;
  execution_latency_ms?: number;
}

export interface Interop_MedicalImagingIngestItem {
  subject_id: string;
  site_id?: string | null;
  study_instance_uid: string;
  series_instance_uid?: string | null;
  modality: string;
  timepoint: string;
  lesions?: Interop_MedicalImagingLesion[];
  overall_response?: string | null;
  sum_of_diameters_mm?: number | null;
  reader_id: string;
  adjudication_tier?: string;
  scan_datetime: string;
  is_unblinded?: boolean;
  metadata?: Record<string, any>;
}

export interface Interop_MedicalImagingLesion {
  lesion_id: string;
  lesion_type?: string;
  site_location: string;
  longest_diameter_mm?: number | null;
  short_axis_mm?: number | null;
  response_category?: string | null;
}

export interface Interop_MobileECRFSyncRequest {
  client_sync_id: string;
  offline_collected_at_utc: string;
  data: Record<string, any>;
}

export interface Interop_MultiStreamIngestPayload {
  study_id: string;
  stream_type: Interop_DataStreamType;
  edc_items?: Interop_EDCEcrfIngestItem[] | null;
  epro_items?: Interop_EPROEcoaIngestItem[] | null;
  central_lab_items?: Interop_CentralLabIngestItem[] | null;
  local_lab_items?: Interop_LocalLabIngestItem[] | null;
  wearable_items?: Interop_IOTWearableIngestItem[] | null;
  fhir_items?: Interop_EHRFHIRIngestItem[] | null;
  imaging_items?: Interop_MedicalImagingIngestItem[] | null;
  reason_for_ingest?: string;
}

export interface Interop_OfflineSyncMarkers {
  sequence_number: number;
  client_id: string;
  conflict_strategy?: Interop_ConflictStrategy;
  signature?: string | null;
  timestamps?: Record<string, any> | null;
}

export interface Interop_OutputArtifact {
  artifact_id?: string;
  name: string;
  artifact_type: Interop_OutputArtifactType;
  file_path: string;
  mime_type: string;
  size_bytes: number;
  sha256_digest: string;
  content_preview?: string | null;
}

export type Interop_OutputArtifactType =
  "TABLE" | "LISTING" | "FIGURE" | "DATASET" | "LOG" | "MANIFEST";

export interface Interop_PackageEntry {
  name: string;
  version: string;
  checksum?: string | null;
  repository?: string | null;
  is_qualified?: boolean;
}

export type Interop_PackageLockType =
  "UV_LOCK" | "RENV_LOCK" | "REQUIREMENTS_TXT";

export interface Interop_PackageLockValidationResult {
  lock_type: Interop_PackageLockType;
  status: Interop_LockValidationStatus;
  lock_digest: string;
  package_count: number;
  packages?: Interop_PackageEntry[];
  is_qualified: boolean;
  unqualified_packages?: string[];
  details: string;
}

export type Interop_ParticipantRole =
  | "INVESTIGATOR"
  | "SUBJECT"
  | "LAR"
  | "NURSE_COORDINATOR"
  | "TRANSLATOR"
  | "WITNESS";

export interface Interop_PediatricAssentDTO {
  assent_id: string;
  study_id: string;
  subject_id: string;
  tier: Interop_PediatricAssentTier;
  minor_age: number;
  comprehension_score_percent: number;
  comprehension_passed: boolean;
  assent_signature: string | null;
  lar_name: string | null;
  lar_relationship: string | null;
  lar_legal_basis: string | null;
  lar_signature: string | null;
  investigator_name: string | null;
  investigator_signature: string | null;
  cryptographic_bundle_hash: string;
  created_at: string;
}

export interface Interop_PediatricAssentListResponse {
  success: boolean;
  assents: Interop_PediatricAssentDTO[];
  total: number;
}

export interface Interop_PediatricAssentResponse {
  success: boolean;
  assent: Interop_PediatricAssentDTO;
  message?: string;
}

export interface Interop_PediatricAssentSubmitRequest {
  study_id: string;
  subject_id: string;
  minor_age: number;
  answers?: Interop_ComprehensionAnswerDTO[];
  assent_signature?: string | null;
  lar_name?: string | null;
  lar_relationship?: Interop_LARRelationship | null;
  lar_legal_basis?: string | null;
  lar_signature?: string | null;
  investigator_name?: string | null;
  investigator_signature?: string | null;
}

export type Interop_PediatricAssentTier =
  "CHILD_7_11" | "ADOLESCENT_12_17" | "LAR_PARENT_GUARDIAN" | "ADULT_18_PLUS";

export interface Interop_PipelineExecutionResult {
  execution_id: string;
  study_id: string;
  snapshot_id?: string | null;
  status: Interop_PipelineExecutionStatus;
  started_at: string;
  completed_at?: string | null;
  sdtm_counts?: Record<string, any>;
  adam_counts?: Record<string, any>;
  total_sdtm_records?: number;
  total_adam_records?: number;
  dataset_json_manifest?: Record<string, any>;
  define_xml_summary?: Record<string, any>;
  validation_summary?: Record<string, any>;
  errors?: string[];
}

export type Interop_PipelineExecutionStatus =
  "PENDING" | "RUNNING" | "COMPLETED" | "FAILED";

export type Interop_ProcedureType =
  | "VITAL_SIGNS"
  | "PK_BLOOD_DRAW"
  | "IP_ADMINISTRATION"
  | "12_LEAD_ECG"
  | "SAFETY_LABS"
  | "PHYSICAL_EXAM"
  | "BIOMARKER_SAMPLING"
  | "PATIENT_DIARY_REVIEW";

export interface Interop_ProgramListResponse {
  study_id: string;
  total: number;
  programs?: Interop_StatisticalProgram[];
}

export interface Interop_ProgramResponse {
  program: Interop_StatisticalProgram;
}

export interface Interop_ProtocolProcedureDTO {
  procedure_type: Interop_ProcedureType;
  description: string;
  completed?: boolean;
  timestamp_utc?: string | null;
  sample_barcodes?: string[];
  performed_by?: string;
  notes?: string;
}

export interface Interop_QuarantinedSubmissionResponse {
  id: string;
  subject_id: string;
  diary_id: string;
  device_timestamp: string;
  answers: Record<string, any>;
  original_answers: Record<string, any>;
  offline_sync_markers: Record<string, any>;
  validation_errors: string[];
  status: string;
  triage_history: Record<string, any>[];
  created_at: string;
  updated_at: string;
}

export interface Interop_RawSensorReading {
  timestamp: string;
  metric_name: string;
  value: number;
  unit: string;
  flag?: string | null;
  extra_attributes?: Record<string, any>;
}

export interface Interop_ReplayQuarantinedSubmissionRequest {
  password: string;
  change_reason: string;
}

export interface Interop_RoomTokenRequest {
  participant_id: string;
  participant_role: Interop_ParticipantRole;
  display_name: string;
  duration_minutes?: number;
}

export interface Interop_RoomTokenResponse {
  success: boolean;
  room_id: string;
  token: string;
  expires_at_utc: string;
  participant_role: Interop_ParticipantRole;
  capabilities: Record<string, any>;
}

export interface Interop_RunADaMPipelineRequest {
  study_id: string;
  snapshot_id?: string | null;
  datasets?: string[] | null;
  reason_for_change?: string;
}

export interface Interop_RunContinuousPipelineRequest {
  study_id: string;
  snapshot_id?: string | null;
  dataset_json_version?: string;
  reason_for_change?: string;
}

export interface Interop_RunSDTMPipelineRequest {
  study_id: string;
  snapshot_id?: string | null;
  domains?: string[] | null;
  reason_for_change?: string;
}

export interface Interop_SDTMDDGenerateRequest {
  study_id: string;
  deficiencies?: Record<string, any>[];
}

export interface Interop_SDTMDDResponse {
  study_id: string;
  domain?: string;
  record_count: number;
  records: Record<string, any>[];
}

export interface Interop_SDTMDOGenerateRequest {
  study_id: string;
  devices?: Record<string, any>[];
}

export interface Interop_SDTMDOResponse {
  study_id: string;
  domain?: string;
  record_count: number;
  records: Record<string, any>[];
}

export interface Interop_SDTMPipelineResponse {
  study_id: string;
  snapshot_id: string | null;
  domain_counts: Record<string, any>;
  total_records: number;
  domains: Record<string, any>;
}

export interface Interop_SafetyAlertsResponse {
  study_id: string;
  total_count: number;
  unacknowledged_count: number;
  alerts?: Interop_SensorSafetyAlert[];
}

export interface Interop_SemanticMappedItemDTO {
  source_resource_type: string;
  source_id?: string | null;
  source_code?: string | null;
  source_system?: string | null;
  source_display?: string | null;
  target_domain: string;
  target_variable: string;
  cdash_testcd?: string | null;
  cdash_test?: string | null;
  extracted_value?: any;
  extracted_unit?: string | null;
  observation_date?: string | string | null;
  mapping_tier: string;
  confidence_score: number;
  provenance: string;
  needs_human_review?: boolean;
  status?: string;
}

export type Interop_SensorAlertType =
  | "SEVERE_HYPOGLYCEMIA"
  | "HYPOGLYCEMIA"
  | "SEVERE_HYPERGLYCEMIA"
  | "HYPERGLYCEMIA"
  | "EXTREME_TACHYCARDIA"
  | "TACHYCARDIA"
  | "SEVERE_BRADYCARDIA"
  | "BRADYCARDIA"
  | "HYPERTENSIVE_CRISIS"
  | "STAGE2_HYPERTENSION"
  | "SEVERE_HYPOTENSION"
  | "ACUTE_BRONCHOCONSTRICTION"
  | "DEVICE_CLOCK_DRIFT_EXCESSIVE"
  | "BATTERY_CRITICAL";

export interface Interop_SensorDashboardMetrics {
  study_id: string;
  total_streams_ingested?: number;
  total_readings_ingested?: number;
  active_devices_count?: number;
  device_breakdown?: Record<string, any>;
  total_safety_alerts?: number;
  unacknowledged_alerts?: number;
  mean_time_in_range_percent?: number | null;
  mean_daily_steps?: number | null;
  mean_resting_hr_bpm?: number | null;
  mean_fev1_liters?: number | null;
  last_telemetry_received_at?: string | null;
}

export type Interop_SensorDeviceType =
  | "CGM"
  | "ACTIGRAPHY"
  | "SPIROMETRY"
  | "BLOOD_PRESSURE"
  | "PULSE_OXIMETER"
  | "ECG";

export interface Interop_SensorIngestResponse {
  success?: boolean;
  result: Interop_SensorIngestResult;
  message?: string;
}

export interface Interop_SensorIngestResult {
  stream_id?: string;
  study_id: string;
  subject_id_pseudonymized: string;
  device_token: string;
  device_type: Interop_SensorDeviceType;
  readings_ingested: number;
  clock_drift_offset_ms: number;
  drift_corrected: boolean;
  omh_data_points_count: number;
  fhir_observations_count: number;
  safety_alerts_triggered?: Interop_SensorSafetyAlert[];
  daily_summary?: Interop_DailySummaryEndpoints | null;
  lakehouse_record_ids?: string[];
  data_hash: string;
  ingested_at?: string;
}

export interface Interop_SensorIngestStreamPayload {
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  device_type: Interop_SensorDeviceType;
  vendor: string;
  model_name: string;
  device_udi?: string | null;
  mac_address?: string | null;
  serial_number?: string | null;
  firmware_version?: string | null;
  device_sync_timestamp: string;
  gateway_receive_timestamp: string;
  readings: Interop_RawSensorReading[];
  feed_to_lakehouse?: boolean;
  reason_for_change?: string;
}

export interface Interop_SensorMetricsResponse {
  metrics: Interop_SensorDashboardMetrics;
}

export interface Interop_SensorSafetyAlert {
  alert_id?: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  device_type: Interop_SensorDeviceType;
  device_token: string;
  alert_type: Interop_SensorAlertType;
  severity: Interop_AlertSeverity;
  parameter_name: string;
  observed_value: number;
  threshold_value: number;
  unit: string;
  triggered_at: string;
  is_acknowledged?: boolean;
  acknowledged_by?: string | null;
  acknowledged_at?: string | null;
  acknowledgment_notes?: string | null;
  audit_signature?: string | null;
  notified_roles?: string[];
}

export type Interop_SignalingMessageType =
  | "OFFER"
  | "ANSWER"
  | "ICE_CANDIDATE"
  | "JOIN_ROOM"
  | "LEAVE_ROOM"
  | "CO_VIEW_STATE"
  | "ANNOTATION"
  | "CHAT";

export interface Interop_SmartAssertionVerifyRequest {
  assertion_jwt: string;
  public_key_pem: string;
  expected_audience?: string | null;
  expected_client_id?: string | null;
}

export interface Interop_SmartAssertionVerifyResponse {
  is_valid: boolean;
  claims?: Record<string, any>;
  error_message?: string | null;
}

export interface Interop_SmartClientAssertionRequest {
  client_id: string;
  token_endpoint: string;
  private_key_pem: string;
  algorithm?: string;
  key_id?: string | null;
  expiration_seconds?: number;
}

export interface Interop_SmartClientAssertionResponse {
  client_assertion_jwt: string;
  token_endpoint: string;
  algorithm: string;
  expires_in_seconds: number;
}

export interface Interop_SnapshotAuditTrailResponse {
  snapshot_id: string;
  audit_entries?: Record<string, any>[];
  total_entries?: number;
}

export interface Interop_SnapshotDataQueryRequest {
  filter_criteria?: Interop_SnapshotFilterCriteria | null;
  request_unblinded?: boolean;
}

export interface Interop_SnapshotDataQueryResponse {
  snapshot_id: string;
  sha256_digest: string;
  total_records?: number;
  is_masked?: boolean;
  masked_count?: number;
  records?: Interop_CanonicalClinicalRecord[];
}

export interface Interop_SnapshotFilterCriteria {
  subject_ids?: string[] | null;
  site_ids?: string[] | null;
  stream_types?: Interop_DataStreamType[] | null;
  domains?: string[] | null;
  cutoff_datetime?: string | null;
  include_unblinded?: boolean;
}

export interface Interop_SnapshotListResponse {
  study_id: string;
  snapshots?: Interop_ClinicalSnapshot[];
  total?: number;
}

export interface Interop_SnapshotResponse {
  snapshot: Interop_ClinicalSnapshot;
  message?: string;
}

export interface Interop_SnapshotVerificationResult {
  snapshot_id: string;
  version_tag: string;
  stored_digest: string;
  computed_digest: string;
  is_valid: boolean;
  record_count: number;
  verified_at?: string;
  verified_by: string;
  verification_message: string;
}

export interface Interop_SpirometryDailySummary {
  best_fev1_liters: number;
  best_fvc_liters: number;
  fev1_fvc_ratio: number;
  fev1_percent_predicted: number;
  peak_expiratory_flow_l_min: number;
  maneuver_count: number;
  session_quality_grade?: string;
}

export interface Interop_StatisticalProgram {
  program_id?: string;
  study_id: string;
  name: string;
  title: string;
  description?: string;
  language: Interop_ExecutionLanguage;
  version?: string;
  source_code: string;
  code_hash: string;
  lockfile_content?: string | null;
  lock_type?: Interop_PackageLockType;
  lock_digest: string;
  target_domain?: string | null;
  created_by: string;
  created_at?: string;
  reason_for_change?: string;
}

export interface Interop_SubjectAssignmentCreateDTO {
  study_id: string;
  subject_id: string;
  instrument_id: string;
  start_date: string;
  end_date: string;
  recurrence_pattern?: string | null;
  due_at?: string | null;
  reason_for_change: string;
}

export interface Interop_SubjectAssignmentResponseDTO {
  id: string;
  subject_id: string;
  instrument_id: string;
  start_date: string;
  end_date: string;
  recurrence_pattern: string | null;
  due_at: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Interop_SubjectComplianceResponseDTO {
  subject_id: string;
  compliance_rate: number;
  completed_count: number;
  pending_count: number;
  overdue_count: number;
  assignments: Interop_AssignmentComplianceDetailDTO[];
}

export interface Interop_SubjectNotificationResponse {
  id: string;
  subject_id: string;
  assignment_id: string | null;
  due_at: string;
  channel: string;
  delivery_status: string;
  is_read: boolean;
  read_at: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Interop_TelehealthSessionCreateRequest {
  study_id: string;
  subject_id: string;
  title?: string;
  scheduled_start_utc: string;
  site_id?: string | null;
  scheduled_end_utc?: string | null;
}

export interface Interop_TelehealthSessionDTO {
  id: string;
  room_id: string;
  study_id: string;
  site_id: string | null;
  subject_id: string;
  title: string;
  scheduled_start_utc: string;
  scheduled_end_utc: string | null;
  status: Interop_TelehealthSessionStatus;
  co_view_state: Record<string, any>;
  signaling_messages: Record<string, any>[];
  participant_records: Record<string, any>[];
  recording_seal_metadata: Record<string, any> | null;
  created_at: string;
  created_by: string;
}

export interface Interop_TelehealthSessionListResponse {
  success: boolean;
  sessions: Interop_TelehealthSessionDTO[];
  total: number;
}

export interface Interop_TelehealthSessionResponse {
  success: boolean;
  session: Interop_TelehealthSessionDTO;
  message?: string;
}

export type Interop_TelehealthSessionStatus =
  | "SCHEDULED"
  | "WAITING_ROOM"
  | "IN_PROGRESS"
  | "COMPLETED"
  | "CANCELLED"
  | "RECORDING_PROCESSING";

export interface Interop_TemperatureIngestRequest {
  readings: Interop_TemperatureReadingDTO[];
}

export interface Interop_TemperatureReadingDTO {
  timestamp_utc: string;
  temperature_celsius: number;
  logger_id?: string;
  battery_percent?: number;
}

export type Interop_TemperatureZone =
  "CRYOGENIC" | "ULTRA_COLD" | "FROZEN" | "REFRIGERATED" | "CONTROLLED_AMBIENT";

export interface Interop_ValidateDatasetJSONRequest {
  payload: Record<string, any> | string;
}

export interface Interop_ValidateDatasetJSONResponse {
  is_valid: boolean;
  errors?: string[];
  warnings?: string[];
  dataset_count?: number;
  record_count?: number;
}

export interface Interop_ValidateDefineXMLRequest {
  xml_content: string;
}

export interface Interop_ValidateLockfileRequest {
  lock_type: Interop_PackageLockType;
  content: string;
  expected_digest?: string | null;
  strict_qualification?: boolean;
}

export interface Interop_ValidateLockfileResponse {
  result: Interop_PackageLockValidationResult;
}

export interface Interop_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Interop_VerifyAssentResponse {
  assent_id: string;
  is_valid: boolean;
  bundle_hash: string;
}

export interface Interop_VerifyManifestResponse {
  result: Record<string, any>;
}

export interface Interop_VerifySnapshotResponse {
  result: Interop_SnapshotVerificationResult;
}

export interface Interop_WebRTCSignalingRequest {
  sender_id: string;
  sender_role: Interop_ParticipantRole;
  message_type: Interop_SignalingMessageType;
  sdp?: string | null;
  candidate?: string | null;
  sdp_mid?: string | null;
  sdp_m_line_index?: number | null;
  payload?: Record<string, any>;
}

export interface Knowledge_ArticleApproveRequest {
  reason_for_change: string;
}

export interface Knowledge_ArticleCreate {
  title: string;
  slug: string;
  category_id: string;
  body_markdown: string;
  version_label?: string;
  tags?: string[] | string | null;
  reason_for_change: string;
}

export interface Knowledge_ArticleDraftSave {
  body_markdown: string;
  title?: string | null;
  slug?: string | null;
  category_id?: string | null;
  tags?: string[] | string | null;
  reason_for_change?: string | null;
}

export interface Knowledge_ArticlePublishRequest {
  reason_for_change: string;
  version_label?: string | null;
}

export interface Knowledge_ArticleRejectRequest {
  reason_for_change?: string | null;
}

export interface Knowledge_ArticleResponse {
  id: string;
  slug: string;
  title: string;
  category_id: string;
  status: Knowledge_ArticleStatus;
  version_index: number;
  version_label: string;
  current_published_version_id?: string | null;
  tags?: string[] | null;
  author_user_id: string;
  last_edited_by: string | null;
  approved_by: string | null;
  body_markdown?: string | null;
  body_html?: string | null;
  is_deleted: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface Knowledge_ArticleSearchResult {
  id: string;
  slug: string;
  title: string;
  category_id: string;
  category_name?: string | null;
  tags?: string[] | null;
  version_label: string;
  version_index: number;
  snippet: string;
  highlighted_snippet?: string;
  relevance_score: number;
  created_at: string;
}

export type Knowledge_ArticleStatus =
  | "DRAFT"
  | "IN_REVIEW"
  | "APPROVED"
  | "PUBLISHED"
  | "SUPERSEDED"
  | "ARCHIVED"
  | "REJECTED";

export interface Knowledge_ArticleSubmitReviewRequest {
  reason_for_change?: string | null;
}

export interface Knowledge_ArticleTransitionRequest {
  target_status: Knowledge_ArticleStatus;
  reason_for_change?: string | null;
  version_label?: string | null;
}

export interface Knowledge_ArticleUpdate {
  title?: string | null;
  slug?: string | null;
  category_id?: string | null;
  body_markdown: string;
  tags?: string[] | string | null;
  reason_for_change?: string | null;
}

export interface Knowledge_ArticleVersionResponse {
  id: string;
  article_id: string;
  version_index: number;
  version_label: string;
  status_at_snapshot: string;
  body_markdown: string;
  body_html: string | null;
  is_locked?: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface Knowledge_AssignCurriculumRequest {
  study_id: string;
  site_id: string;
  user_id: string;
  user_name: string;
  user_role?: string;
  reason_for_change?: string;
}

export interface Knowledge_AuditChainVerificationResponse {
  article_id?: string | null;
  is_valid: boolean;
  total_records: number;
  genesis_digest: string;
  latest_digest: string;
  tamper_detected?: boolean;
  details: string;
}

export interface Knowledge_AuditLogResponse {
  id: string;
  article_id: string | null;
  action: string;
  previous_status: string | null;
  new_status: string | null;
  details: string | null;
  previous_digest?: string | null;
  sha256_digest?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
}

export interface Knowledge_Body_upload_protocol_file_api_v1_knowledge_protocols_upload_post {
  study_id: string;
  protocol_version: string;
  document_type?: string;
  is_approved?: boolean;
  reason_for_change?: string;
  file: string;
}

export interface Knowledge_CategoryCreate {
  name: string;
  slug: string;
  description?: string | null;
  persona_visibility?: string | null;
  parent_id?: string | null;
  reason_for_change: string;
}

export interface Knowledge_CategoryResponse {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  persona_visibility: string | null;
  parent_id: string | null;
  is_deleted: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Knowledge_CertificateResponse {
  id: string;
  assignment_id: string;
  curriculum_id: string;
  study_id: string;
  site_id: string;
  user_id: string;
  user_name: string;
  user_role: string;
  protocol_version: string;
  score_achieved: number;
  time_spent_seconds: number;
  signature_manifestation: Record<string, any>;
  sha256_seal: string;
  pdf_base64?: string | null;
  etmf_doc_id?: string | null;
  eisf_doc_id?: string | null;
  routed_to_etmf: boolean;
  routed_to_eisf: boolean;
  created_at: string;
}

export interface Knowledge_ContextualHelpMappingCreate {
  route_pattern: string;
  persona?: string | null;
  article_id: string;
  section_anchor?: string | null;
  priority?: number;
  is_active?: boolean;
  reason_for_change: string;
}

export interface Knowledge_ContextualHelpMappingResponse {
  id: string;
  route_pattern: string;
  persona: string | null;
  article_id: string;
  section_anchor?: string | null;
  priority: number;
  is_active?: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Knowledge_ContextualHelpMappingUpdate {
  route_pattern?: string | null;
  persona?: string | null;
  article_id?: string | null;
  section_anchor?: string | null;
  priority?: number | null;
  is_active?: boolean | null;
  reason_for_change: string;
}

export interface Knowledge_ContextualHelpResolutionResponse {
  matched_mapping?: Knowledge_ContextualHelpMappingResponse | null;
  primary_article?: Knowledge_ArticleResponse | null;
  primary_version?: Knowledge_ArticleVersionResponse | null;
  section_anchor?: string | null;
  related_articles?: Knowledge_ArticleResponse[];
  article?: Knowledge_ArticleResponse | null;
  version?: Knowledge_ArticleVersionResponse | null;
}

export interface Knowledge_GenerateAmendmentCurriculumRequest {
  study_id: string;
  protocol_version: string;
  baseline_snapshot: Record<string, any>;
  amended_snapshot: Record<string, any>;
  target_roles?: string[] | null;
  site_assignments?: Record<string, any>[] | null;
  reason_for_change?: string;
}

export interface Knowledge_HTTPValidationError {
  detail?: Knowledge_ValidationError[];
}

export interface Knowledge_ProtocolChunkSummary {
  chunk_id: string;
  document_id: string;
  study_id: string;
  protocol_version: string;
  section_number: string | null;
  section_title: string;
  page_number: number;
  token_count: number;
  is_approved: boolean;
  citation_marker: string;
}

export interface Knowledge_ProtocolIngestTextRequest {
  study_id: string;
  protocol_version: string;
  raw_text: string;
  filename?: string;
  document_id?: string | null;
  document_type?: string;
  is_approved?: boolean;
  reason_for_change?: string;
}

export interface Knowledge_ProtocolSearchRequest {
  query: string;
  study_id: string;
  protocol_version?: string | null;
  only_approved?: boolean;
  top_k?: number;
}

export interface Knowledge_ProtocolSearchResult {
  chunk_id: string;
  document_id: string;
  study_id: string;
  protocol_version: string;
  section_number: string | null;
  section_title: string;
  page_number: number;
  chunk_text: string;
  is_approved: boolean;
  similarity_score: number;
  citation_marker: string;
}

export interface Knowledge_QualificationGateResult {
  is_qualified: boolean;
  user_id: string;
  study_id: string;
  protocol_version: string;
  site_id?: string | null;
  blocked_reason?: string | null;
  pending_curricula?: Record<string, any>[];
}

export interface Knowledge_QuizEvaluationResponse {
  score_percentage: number;
  passed: boolean;
  passing_threshold: number;
  total_questions: number;
  correct_count: number;
  feedback?: Record<string, any>[];
}

export interface Knowledge_RecordProgressRequest {
  time_spent_delta_seconds: number;
  reason_for_change?: string;
}

export interface Knowledge_SignTrainingRequest {
  signer_name: string;
  signer_role: string;
  meaning?: string;
  reason_for_change?: string;
}

export interface Knowledge_SubmitQuizRequest {
  answers: Record<string, any>;
  reason_for_change?: string;
}

export interface Knowledge_TrainingAssignmentDetailResponse {
  id: string;
  curriculum_id: string;
  study_id: string;
  site_id: string;
  user_id: string;
  user_name: string;
  user_role: string;
  status: string;
  time_spent_seconds: number;
  quiz_score?: number | null;
  attempts_count: number;
  completed_at?: string | null;
  certificate_id?: string | null;
  created_at: string;
  curriculum?: Knowledge_TrainingCurriculumResponse | null;
}

export interface Knowledge_TrainingAssignmentResponse {
  id: string;
  curriculum_id: string;
  study_id: string;
  site_id: string;
  user_id: string;
  user_name: string;
  user_role: string;
  status: string;
  time_spent_seconds: number;
  quiz_score?: number | null;
  attempts_count: number;
  completed_at?: string | null;
  certificate_id?: string | null;
  created_at: string;
}

export interface Knowledge_TrainingCurriculumResponse {
  id: string;
  study_id: string;
  protocol_version: string;
  title: string;
  description: string;
  target_roles: string[];
  is_mandatory: boolean;
  passing_score: number;
  min_reading_time_seconds: number;
  is_active: boolean;
  modules?: Knowledge_TrainingModuleSummary[];
  created_at: string;
  created_by: string;
}

export interface Knowledge_TrainingModuleSummary {
  id: string;
  curriculum_id: string;
  title: string;
  module_type: string;
  content_text: string;
  video_url?: string | null;
  document_reference?: string | null;
  order_index: number;
  min_reading_time_seconds: number;
  quiz_questions?: Record<string, any>[];
}

export interface Knowledge_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Knowledge_VerifyGateRequest {
  user_id: string;
  study_id: string;
  protocol_version: string;
  site_id?: string | null;
  user_role?: string | null;
}

export interface Notifications_HTTPValidationError {
  detail?: Notifications_ValidationError[];
}

export type Notifications_NotificationCategory =
  "ALERTS" | "SYSTEM" | "ACTION_ITEMS";

export interface Notifications_NotificationCreate {
  recipient_user_id?: string | null;
  recipient_role?: string | null;
  category: Notifications_NotificationCategory;
  priority: Notifications_NotificationPriority;
  channels?: string;
  message_content: string;
  related_entity_id?: string | null;
  related_entity_type?: string | null;
}

export type Notifications_NotificationPriority =
  "LOW" | "MEDIUM" | "HIGH" | "CRITICAL";

export interface Notifications_NotificationResponse {
  id: string;
  recipient_user_id?: string | null;
  recipient_role?: string | null;
  category: Notifications_NotificationCategory;
  priority: Notifications_NotificationPriority;
  channels: string;
  message_content: string;
  related_entity_id?: string | null;
  related_entity_type?: string | null;
  status: Notifications_NotificationStatus;
  delivery_state: string;
  retries: number;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type Notifications_NotificationStatus =
  "OPEN" | "ACKNOWLEDGED" | "RESOLVED";

export interface Notifications_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Org_AssignmentResolutionResponse {
  personnel_id: string;
  roles: string[];
  assigned_sites: string[];
  assigned_studies: string[];
}

export type Org_ClinicalStaffRole =
  | "Principal Investigator"
  | "Sub-Investigator"
  | "CRC"
  | "CRA/Monitor"
  | "External Monitor";

export interface Org_DelegationCreate {
  delegator_id: string;
  delegatee_id: string;
  site_id: string;
  study_id: string;
  duties: string[];
  start_date: string;
  end_date?: string | null;
  reason_for_change: string;
}

export interface Org_DelegationResponse {
  id: string;
  delegator_id: string;
  delegatee_id: string;
  site_id: string;
  study_id: string;
  duties: string[];
  start_date: string;
  end_date?: string | null;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  signature?: string | null;
  signed_payload?: Record<string, any> | null;
  signed_at?: string | null;
  signed_by?: string | null;
  revoked_at?: string | null;
  revoked_by?: string | null;
  revocation_reason?: string | null;
}

export interface Org_DelegationRevoke {
  reason_for_change: string;
}

export interface Org_DelegationSign {
  payload: Record<string, any>;
  signature: string;
  reason_for_change: string;
}

export interface Org_HTTPValidationError {
  detail?: Org_ValidationError[];
}

export interface Org_OrgAuditLogResponse {
  id: string;
  timestamp: string;
  actor_id: string;
  actor_role: string;
  action: string;
  record_id?: string | null;
  details: string;
  reason_for_change: string;
}

export interface Org_OrganizationCreate {
  name: string;
  org_type: Org_OrganizationType;
  reason_for_change: string;
}

export interface Org_OrganizationResponse {
  id: string;
  name: string;
  org_type: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type Org_OrganizationType =
  "sponsor" | "CRO" | "IRB/IEC" | "central laboratory" | "site";

export interface Org_OrganizationUpdate {
  name?: string | null;
  org_type?: Org_OrganizationType | null;
  reason_for_change: string;
}

export interface Org_PersonnelAssignmentCreate {
  site_id: string;
  study_id: string;
  is_active?: boolean;
  reason_for_change: string;
}

export interface Org_PersonnelAssignmentResponse {
  id: string;
  personnel_id: string;
  site_id: string;
  study_id: string;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Org_PersonnelAssignmentUpdate {
  site_id?: string | null;
  study_id?: string | null;
  is_active?: boolean | null;
  reason_for_change: string;
}

export interface Org_PersonnelCreate {
  keycloak_user_id?: string | null;
  first_name: string;
  last_name: string;
  email: string;
  role: Org_ClinicalStaffRole;
  organization_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  reason_for_change: string;
}

export interface Org_PersonnelResponse {
  id: string;
  keycloak_user_id?: string | null;
  first_name: string;
  last_name: string;
  email: string;
  role: string;
  organization_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Org_PersonnelUpdate {
  keycloak_user_id?: string | null;
  first_name?: string | null;
  last_name?: string | null;
  email?: string | null;
  role?: Org_ClinicalStaffRole | null;
  organization_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  reason_for_change: string;
}

export interface Org_SiteCreate {
  site_id: string;
  name: string;
  organization_id: string;
  study_id?: string | null;
  reason_for_change: string;
}

export interface Org_SiteResponse {
  id: string;
  site_id: string;
  name: string;
  organization_id: string;
  study_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Org_SiteUpdate {
  site_id?: string | null;
  name?: string | null;
  organization_id?: string | null;
  study_id?: string | null;
  reason_for_change: string;
}

export interface Org_TrainingLogCreate {
  personnel_id: string;
  site_id: string;
  study_id: string;
  training_topic: string;
  completion_date: string;
  reason_for_change: string;
}

export interface Org_TrainingLogResponse {
  id: string;
  personnel_id: string;
  site_id: string;
  study_id: string;
  training_topic: string;
  completion_date: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  signature_manifestation?: Record<string, any> | null;
  signer?: string | null;
  signing_timestamp?: string | null;
}

export interface Org_TrainingLogSignRequest {
  payload: Record<string, any>;
  signature: string;
  reason_for_change: string;
}

export interface Org_TrainingLogUpdate {
  personnel_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  training_topic?: string | null;
  completion_date?: string | null;
  reason_for_change?: string | null;
}

export interface Org_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Payments_BalanceInquiryResponse {
  wallet_id: string;
  subject_id: string;
  study_id: string;
  site_id: string;
  current_balance: number;
  available_balance: number;
  pending_claims_total: number;
  lifetime_stipends: number;
  lifetime_reimbursements: number;
  lifetime_disbursements: number;
  taxable_ytd: number;
  currency: string;
  virtual_card_mask: string | null;
  card_status: string;
  card_type: string;
  ach_account_mask: string | null;
  is_1099_threshold_exceeded: boolean;
  w9_status: string;
}

export interface Payments_BatchEventIngestDto {
  ecrf_submissions?: Payments_EcrfSubmissionEventDto[];
  sdv_verifications?: Payments_CraSdvVerificationEventDto[];
  milestones?: Payments_MilestoneCompletionEventDto[];
}

export interface Payments_BillingCycleCompilationRequest {
  study_id: string;
  site_id: string;
  billing_cycle?: string;
  period_start: string;
  period_end: string;
  auto_approve?: boolean;
}

export interface Payments_BudgetContractCreate {
  study_id: string;
  site_id: string;
  contract_name: string;
  tier_level?: string;
  currency?: string;
  total_budget?: number;
  overhead_percentage?: number;
  withholding_percentage?: number;
  effective_date?: string | null;
}

export interface Payments_BudgetContractResponse {
  id: string;
  study_id: string;
  site_id: string;
  contract_name: string;
  tier_level: string;
  currency: string;
  total_budget: number;
  overhead_percentage: number;
  withholding_percentage: number;
  effective_date: string | null;
  status: string;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
  fee_schedules_count?: number;
  split_payees_count?: number;
}

export interface Payments_CraSdvVerificationEventDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_code: string;
  form_id?: string | null;
  verified_by: string;
  event_id: string;
  timestamp?: string | null;
}

export interface Payments_DisbursementRequest {
  disbursement_type?: string;
  amount?: number | null;
  destination_mask?: string | null;
}

export interface Payments_DisbursementResponse {
  id: string;
  wallet_id: string;
  subject_id: string;
  study_id: string;
  disbursement_type: string;
  amount: number;
  currency: string;
  status: string;
  reference_number: string;
  destination_mask: string;
  dispatched_at: string;
  settled_at: string | null;
  error_message: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface Payments_EcrfSubmissionEventDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_code: string;
  form_id: string;
  event_id: string;
  timestamp?: string | null;
  actor_id?: string;
}

export interface Payments_ErpExportResponse {
  erp_system: string;
  invoice_number: string;
  document_type?: string;
  payload: Record<string, any>;
  export_timestamp: string;
}

export interface Payments_ErpSyncLogResponse {
  id: string;
  invoice_id: string;
  erp_system: string;
  sync_direction: string;
  status: string;
  payload: Record<string, any> | null;
  response_data: Record<string, any> | null;
  external_reference: string | null;
  reconciled: boolean;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface Payments_ErpWebhookPayload {
  erp_system?: string;
  invoice_number?: string | null;
  invoice_id?: string | null;
  erp_reference_id: string;
  status: string;
  payment_date?: string | null;
  amount_paid?: number | null;
  currency?: string;
  bank_reference?: string | null;
  notes?: string | null;
}

export interface Payments_ErpWebhookResponse {
  status: string;
  invoice_id: string;
  invoice_number: string;
  erp_system: string;
  erp_reference_id: string;
  previous_status: string;
  new_status: string;
  reconciled: boolean;
  message: string;
}

export interface Payments_ExpenseClaimResponse {
  id: string;
  wallet_id: string;
  subject_id: string;
  study_id: string;
  site_id: string;
  expense_date: string;
  category: string;
  merchant_name: string;
  gross_amount: number;
  tax_deduction: number;
  net_amount: number;
  currency: string;
  is_meal_allowance?: boolean;
  per_diem_cap?: number | null;
  receipt_image_name: string | null;
  receipt_ocr_data: Record<string, any> | null;
  status: string;
  reviewed_by: string | null;
  reviewed_at: string | null;
  review_notes: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface Payments_ExpenseClaimReviewRequest {
  status: string;
  review_notes?: string | null;
  adjusted_amount?: number | null;
  adjusted_category?: string | null;
  adjusted_merchant?: string | null;
}

export interface Payments_ExpenseClaimSubmitRequest {
  wallet_id?: string | null;
  subject_id: string;
  study_id: string;
  site_id: string;
  expense_date: string;
  category?: string;
  merchant_name: string;
  gross_amount: number;
  tax_deduction?: number;
  net_amount?: number | null;
  currency?: string;
  is_meal_allowance?: boolean;
  per_diem_cap?: number | null;
  receipt_image_name?: string | null;
  receipt_image_base64?: string | null;
  receipt_ocr_data?: Record<string, any> | null;
}

export interface Payments_FeeScheduleCreate {
  visit_code: string;
  activity_id?: string | null;
  form_id?: string | null;
  procedure_code: string;
  procedure_name: string;
  base_amount: number;
  overhead_percentage?: number | null;
  withholding_percentage?: number | null;
  patient_stipend?: number;
  travel_reimbursement?: number;
  requires_sdv?: boolean;
}

export interface Payments_FeeScheduleResponse {
  id: string;
  contract_id: string;
  visit_code: string;
  activity_id: string | null;
  form_id: string | null;
  procedure_code: string;
  procedure_name: string;
  base_amount: number;
  overhead_percentage: number;
  withholding_percentage: number;
  patient_stipend: number;
  travel_reimbursement: number;
  requires_sdv: boolean;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
}

export interface Payments_HTTPValidationError {
  detail?: Payments_ValidationError[];
}

export interface Payments_HoldbackReleaseRequest {
  study_id: string;
  site_id: string;
  release_reason: string;
  dm_signoff_by: string;
  quality_signoff_by: string;
  holdback_ids?: string[] | null;
}

export interface Payments_HoldbackReleaseResponse {
  study_id: string;
  site_id: string;
  total_released_amount: number;
  currency: string;
  released_count: number;
  dm_signoff_by: string;
  quality_signoff_by: string;
  status: string;
  released_reserves?: Payments_HoldbackReserveResponse[];
}

export interface Payments_HoldbackReserveResponse {
  id: string;
  study_id: string;
  site_id: string;
  contract_id: string;
  payable_id: string;
  holdback_amount: number;
  currency: string;
  status: string;
  release_authorized_by: string | null;
  release_authorized_at: string | null;
  dm_signoff_by: string | null;
  dm_signoff_at: string | null;
  quality_signoff_by: string | null;
  quality_signoff_at: string | null;
  release_notes: string | null;
  created_at: string;
  version_index: number;
}

export interface Payments_InvoiceLineItemResponse {
  id: string;
  invoice_id: string;
  payable_id: string | null;
  subject_id: string;
  visit_code: string;
  procedure_code: string | null;
  procedure_name: string | null;
  base_amount: number;
  overhead_amount: number;
  patient_stipend: number;
  travel_reimbursement: number;
  gross_amount: number;
  withholding_rate: number;
  withholding_amount: number;
  net_amount: number;
  currency: string;
  created_at: string;
  version_index: number;
}

export interface Payments_IssueReloadableCardRequest {
  cardholder_name?: string | null;
  card_type?: string;
  initial_load?: number;
}

export interface Payments_IssueReloadableCardResponse {
  wallet_id: string;
  subject_id: string;
  card_token: string;
  card_mask: string;
  card_expiry: string;
  card_type: string;
  card_status: string;
  cardholder_name: string;
  current_balance: number;
  issued_at: string;
}

export interface Payments_LaySummaryCreate {
  study_id: string;
  protocol_id?: string | null;
  title: string;
  trial_phase?: string;
  therapeutic_area: string;
  sponsor_name: string;
  summary_text: string;
  plain_language_sections?: Record<string, any> | null;
  irb_approval_number: string;
  irb_approval_date: string;
  language?: string;
  status?: string;
}

export interface Payments_LaySummaryPublishRequest {
  irb_approval_number?: string | null;
  irb_approval_date?: string | null;
}

export interface Payments_LaySummaryReceiptConfirmRequest {
  subject_id: string;
  study_id: string;
  comprehension_score?: number;
  comprehension_feedback?: string | null;
  confirmation_statement?: string;
}

export interface Payments_LaySummaryReceiptResponse {
  id: string;
  summary_id: string;
  subject_id: string;
  study_id: string;
  comprehension_score: number;
  comprehension_feedback: string | null;
  confirmation_statement: string;
  receipt_hash: string;
  signer_identity: string;
  confirmed_at: string;
  status?: string;
}

export interface Payments_LaySummaryResponse {
  id: string;
  study_id: string;
  protocol_id: string | null;
  title: string;
  trial_phase: string;
  therapeutic_area: string;
  sponsor_name: string;
  summary_text: string;
  plain_language_sections: Record<string, any> | null;
  irb_approval_number: string;
  irb_approval_date: string;
  language: string;
  status: string;
  published_at: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  receipts_count?: number;
}

export interface Payments_MilestoneCompletionEventDto {
  study_id: string;
  site_id: string;
  milestone_code: string;
  milestone_name: string;
  amount: number;
  verified_by: string;
  event_id: string;
  timestamp?: string | null;
}

export interface Payments_ParticipantStipendRuleCreate {
  study_id: string;
  visit_code: string;
  visit_name: string;
  procedure_code?: string | null;
  amount: number;
  currency?: string;
  requires_verification?: boolean;
  taxable?: boolean;
  anti_coercion_cap?: number | null;
  is_milestone?: boolean;
  milestone_code?: string | null;
}

export interface Payments_ParticipantStipendRuleResponse {
  id: string;
  study_id: string;
  visit_code: string;
  visit_name: string;
  procedure_code: string | null;
  amount: number;
  currency: string;
  requires_verification: boolean;
  taxable: boolean;
  anti_coercion_cap?: number | null;
  is_milestone?: boolean;
  milestone_code?: string | null;
  is_active: boolean;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface Payments_ParticipantTransactionResponse {
  id: string;
  wallet_id: string;
  subject_id: string;
  study_id: string;
  transaction_type: string;
  amount: number;
  balance_after: number;
  reference_id: string;
  description: string;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface Payments_ParticipantWalletCreate {
  subject_id: string;
  study_id: string;
  site_id: string;
  currency?: string;
  card_type?: string;
  virtual_card_token?: string | null;
  virtual_card_mask?: string | null;
  virtual_card_expiry?: string | null;
  anti_coercion_cap?: number | null;
  ach_routing_number?: string | null;
  ach_account_mask?: string | null;
  ach_account_holder?: string | null;
}

export interface Payments_ParticipantWalletPayoutMethodsUpdate {
  virtual_card_token?: string | null;
  virtual_card_mask?: string | null;
  virtual_card_expiry?: string | null;
  card_status?: string | null;
  ach_routing_number?: string | null;
  ach_account_mask?: string | null;
  ach_account_holder?: string | null;
}

export interface Payments_ParticipantWalletResponse {
  id: string;
  subject_id: string;
  study_id: string;
  site_id: string;
  current_balance: number;
  lifetime_stipends: number;
  lifetime_reimbursements: number;
  lifetime_disbursements: number;
  currency: string;
  status: string;
  taxable_ytd?: number;
  is_1099_threshold_exceeded?: boolean;
  w9_status?: string;
  w9_submitted_at?: string | null;
  w9_verified_at?: string | null;
  tax_id_last_four?: string | null;
  card_type?: string;
  card_status?: string;
  virtual_card_token?: string | null;
  virtual_card_mask?: string | null;
  virtual_card_expiry?: string | null;
  anti_coercion_cap?: number | null;
  ach_routing_number?: string | null;
  ach_account_mask?: string | null;
  ach_account_holder?: string | null;
  created_at: string;
  created_by: string;
  updated_at: string;
  version_index: number;
  pending_claims_count?: number;
  total_claims_count?: number;
  disbursements_count?: number;
}

export interface Payments_PayableAllocationResponse {
  id: string;
  payable_id: string;
  payee_id: string;
  payee_name: string;
  payee_role: string;
  allocation_percentage: number;
  gross_share: number;
  withholding_share: number;
  net_share: number;
  currency: string;
  status: string;
  created_at: string;
  version_index: number;
}

export interface Payments_PayableResponse {
  id: string;
  study_id: string;
  site_id: string;
  contract_id: string;
  subject_id: string;
  visit_code: string;
  form_id: string | null;
  procedure_code: string | null;
  procedure_name: string | null;
  trigger_type: string;
  trigger_event_id: string;
  trigger_source: string;
  base_amount: number;
  overhead_amount: number;
  patient_stipend_amount: number;
  travel_reimbursement_amount: number;
  gross_amount: number;
  withholding_rate: number;
  withholding_amount: number;
  net_amount: number;
  currency: string;
  status: string;
  is_sdv_gated: boolean;
  sdv_verified_at: string | null;
  sdv_verified_by: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
  allocations?: Payments_PayableAllocationResponse[];
  holdback_reserve?: Payments_HoldbackReserveResponse | null;
}

export interface Payments_PaymentAuditLogResponse {
  id: string;
  entity_type: string;
  entity_id: string;
  action: string;
  previous_state: Record<string, any> | null;
  new_state: Record<string, any> | null;
  details: string | null;
  user_id: string;
  user_roles: string;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
}

export interface Payments_PaymentReconciliationRequest {
  payment_reference: string;
  erp_system?: string;
  payment_date?: string | null;
  amount_paid: number;
  notes?: string | null;
}

export interface Payments_PaymentReconciliationResponse {
  invoice_id: string;
  invoice_number: string;
  status: string;
  erp_sync_status: string;
  reconciled_by: string;
  reconciled_at: string;
  reconciliation_reference: string;
  total_net_amount: number;
  amount_paid: number;
  reconciled_payables_count: number;
}

export interface Payments_ReceiptOcrExtractRequest {
  image_base64?: string | null;
  raw_text?: string | null;
  category_hint?: string | null;
  filename?: string | null;
}

export interface Payments_ReceiptOcrExtractResponse {
  merchant_name: string;
  expense_date: string;
  gross_amount: number;
  tax_deduction: number;
  net_amount: number;
  category: string;
  confidence_score: number;
  detected_keywords: string[];
  raw_text: string;
}

export interface Payments_RemittanceAdviceDispatchRequest {
  recipient_email: string;
}

export interface Payments_RemittanceAdviceResponse {
  id: string;
  remittance_number: string;
  invoice_id: string;
  study_id: string;
  site_id: string;
  currency: string;
  gross_amount: number;
  overhead_amount: number;
  holdback_amount: number;
  net_amount: number;
  pdf_sha256_hash: string;
  is_sealed: boolean;
  is_signed: boolean;
  signed_by: string | null;
  signed_at: string | null;
  signer_role: string | null;
  signature_manifest: Record<string, any> | null;
  dispatched_to: string | null;
  dispatched_at: string | null;
  dispatch_status: string;
  created_at: string;
  version_index: number;
}

export interface Payments_RemittanceAdviceSignRequest {
  signer_name: string;
  signer_role?: string;
  signature_reason?: string;
  cert_pem?: string | null;
  private_key_pem?: string | null;
}

export interface Payments_RemittanceSignatureVerificationResponse {
  is_valid: boolean;
  status: string;
  failure_reason?: string | null;
  pdf_sha256_hash: string;
  signed_by?: string | null;
  signed_at?: string | null;
  signer_role?: string | null;
}

export interface Payments_SelfBillingInvoiceResponse {
  id: string;
  invoice_number: string;
  study_id: string;
  site_id: string;
  contract_id: string;
  billing_cycle: string;
  period_start: string;
  period_end: string;
  currency: string;
  total_base_amount: number;
  total_overhead_amount: number;
  total_patient_stipend: number;
  total_travel_reimbursement: number;
  total_gross_amount: number;
  total_holdback_amount: number;
  total_net_amount: number;
  payable_count: number;
  status: string;
  approved_by: string | null;
  approved_at: string | null;
  erp_system: string | null;
  erp_reference_id: string | null;
  erp_sync_status: string;
  erp_synced_at: string | null;
  reconciled_by: string | null;
  reconciled_at: string | null;
  reconciliation_reference: string | null;
  reconciliation_notes: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
  line_items?: Payments_InvoiceLineItemResponse[];
  remittance_advice?: Payments_RemittanceAdviceResponse | null;
}

export interface Payments_SplitPayeeConfigBulkUpdate {
  payees: Payments_SplitPayeeConfigItem[];
}

export interface Payments_SplitPayeeConfigItem {
  payee_id: string;
  payee_name: string;
  payee_role: string;
  allocation_percentage: number;
  bank_account_reference?: string | null;
  tax_identifier?: string | null;
}

export interface Payments_SplitPayeeConfigResponse {
  id: string;
  contract_id: string;
  payee_id: string;
  payee_name: string;
  payee_role: string;
  allocation_percentage: number;
  bank_account_reference: string | null;
  tax_identifier: string | null;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
}

export interface Payments_Tax1099ThresholdResponse {
  wallet_id: string;
  subject_id: string;
  study_id: string;
  taxable_ytd: number;
  threshold_limit?: number;
  threshold_percentage: number;
  is_threshold_exceeded: boolean;
  w9_status: string;
  w9_required: boolean;
  warning_level: string;
  currency?: string;
}

export interface Payments_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Payments_VisitVerificationEventDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_code: string;
  event_id: string;
  verified_by?: string;
  verification_timestamp?: string | null;
}

export interface Payments_W9SubmitRequest {
  tax_id_last_four: string;
  full_legal_name: string;
  certify_accurate?: boolean;
  address?: string | null;
}

export interface Payments_W9SubmitResponse {
  wallet_id: string;
  subject_id: string;
  w9_status: string;
  tax_id_last_four: string;
  submitted_at: string;
  message: string;
}

export interface Payments_W9VerifyRequest {
  notes?: string | null;
}

export type Quality_ActionItemStatus =
  "CANCELLED" | "COMPLETED" | "IN_PROGRESS" | "OPEN";

export interface Quality_AuditCreate {
  audit_number: string;
  study_id: string;
  site_id?: string | null;
  vendor_name?: string | null;
  audit_type?: string;
  lead_auditor: string;
  planned_start_date: string;
  planned_end_date: string;
  scope_summary: string;
}

export interface Quality_AuditEventPayload {
  entry_id: string;
  subject_id: string;
  site_id: string;
  user_id: string;
  timestamp: string;
  assessment_type: string;
  procedure_pair?: string | null;
  elapsed_time_seconds?: number | null;
}

export interface Quality_AuditFindingCreate {
  finding_number: string;
  severity?: string;
  category: string;
  condition: string;
  criteria: string;
  cause: string;
  effect: string;
}

export interface Quality_AuditFindingResponse {
  id: string;
  audit_id: string;
  finding_number: string;
  severity: string;
  category: string;
  condition: string;
  criteria: string;
  cause: string;
  effect: string;
  capa_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_AuditLogResponse {
  id: string;
  timestamp: string;
  user_id: string;
  user_role: string;
  action: string;
  details: string;
  entity_type?: string | null;
  record_id?: string | null;
  old_value?: any | null;
  new_value?: any | null;
  change_reason?: string | null;
  merkle_hash?: string | null;
  sha256_hash?: string | null;
  signature_hash?: string | null;
}

export interface Quality_AuditResponse {
  id: string;
  audit_number: string;
  study_id: string;
  site_id?: string | null;
  vendor_name?: string | null;
  audit_type: string;
  lead_auditor: string;
  planned_start_date: string;
  planned_end_date: string;
  actual_start_date?: string | null;
  actual_end_date?: string | null;
  status: string;
  scope_summary: string;
  findings?: Quality_AuditFindingResponse[];
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type Quality_AuditStatus =
  | "CANCELLED"
  | "CAPA_PENDING"
  | "COMPLETED"
  | "FINDINGS_REPORTED"
  | "IN_PROGRESS"
  | "PLANNED";

export interface Quality_AuditStatusUpdate {
  status: Quality_AuditStatus;
  actual_start_date?: string | null;
  actual_end_date?: string | null;
}

export type Quality_BenfordConformity =
  | "ACCEPTABLE_CONFORMITY"
  | "CLOSE_CONFORMITY"
  | "MARGINAL_CONFORMITY"
  | "NON_CONFORMING";

export interface Quality_BenfordDigitDistributionResponse {
  sample_size: number;
  chi_square: number;
  p_value: number;
  mad: number;
  conformity: Quality_BenfordConformity;
  observed_frequencies: Record<string, any>;
  expected_frequencies: Record<string, any>;
}

export type Quality_BreachStatus =
  | "AUTHORITY_NOTIFIED"
  | "CONFIRMED_BREACH"
  | "DISMISSED"
  | "RESOLVED"
  | "UNDER_EVALUATION";

export interface Quality_CAPAActionItemCreate {
  title: string;
  description: string;
  action_type?: string;
  assigned_to: string;
  due_date?: string | null;
}

export interface Quality_CAPAActionItemResponse {
  id: string;
  capa_id: string;
  title: string;
  description: string;
  action_type: string;
  assigned_to: string;
  due_date?: string | null;
  status: string;
  completed_at?: string | null;
  evidence_url?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_CAPAActionItemUpdate {
  status: Quality_ActionItemStatus;
  evidence_url?: string | null;
}

export interface Quality_CAPACreate {
  deviation_id: string;
  rca_id?: string | null;
  capa_type?: string;
  action_plan: string;
  preventive_measures?: string | null;
  risk_level?: string;
  lead_investigator_id?: string | null;
  target_completion_date?: string | null;
  effectiveness_interval_days?: number;
  audit_finding_id?: string | null;
}

export interface Quality_CAPAEffectivenessCheckCreate {
  planned_date: string;
  metric_evaluated: string;
  baseline_value: string;
  target_value: string;
  actual_value: string;
  outcome: Quality_EffectivenessOutcome;
  comments?: string | null;
}

export interface Quality_CAPAEffectivenessCheckResponse {
  id: string;
  capa_id: string;
  planned_date: string;
  executed_date?: string | null;
  metric_evaluated: string;
  baseline_value: string;
  target_value: string;
  actual_value?: string | null;
  outcome: string;
  evaluator_id?: string | null;
  comments?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_CAPAResponse {
  id: string;
  deviation_id: string;
  rca_id?: string | null;
  capa_type: string;
  action_plan: string;
  status: Quality_CAPAStatus;
  preventive_measures?: string | null;
  risk_level?: string;
  lead_investigator_id?: string | null;
  qa_approver_id?: string | null;
  target_completion_date?: string | null;
  actual_completion_date?: string | null;
  effectiveness_interval_days?: number;
  effectiveness_review_date?: string | null;
  effectiveness_outcome?: string;
  recurrence_detected?: boolean;
  audit_finding_id?: string | null;
  study_id: string;
  site_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
  action_items?: Quality_CAPAActionItemResponse[];
  effectiveness_checks?: Quality_CAPAEffectivenessCheckResponse[];
}

export type Quality_CAPAStatus =
  | "INITIATED"
  | "UNDER_REVIEW"
  | "APPROVED"
  | "IMPLEMENTATION"
  | "IMPLEMENTATION_VERIFIED"
  | "EFFECTIVENESS_CHECK"
  | "CLOSED"
  | "INEFFECTIVE"
  | "CANCELLED";

export interface Quality_CAPATransitionRequest {
  to_status: Quality_CAPAStatus;
  version_index?: number | null;
}

export interface Quality_CAPAUpdate {
  action_plan?: string | null;
  preventive_measures?: string | null;
  risk_level?: string | null;
  target_completion_date?: string | null;
  effectiveness_interval_days?: number | null;
  version_index?: number | null;
}

export interface Quality_CSMDossierResponse {
  id: string;
  study_id: string;
  dossier_name: string;
  generated_at: string;
  generated_by: string;
  status: string;
  ract_summary: Record<string, any>;
  kri_surveillance_summary: Record<string, any>;
  site_risk_rankings: Record<string, any>[];
  anomaly_forensics_summary: Record<string, any>;
  electronic_signature: Record<string, any>;
  audit_trail_snapshot: Record<string, any>[];
  merkle_seal?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_CompileCSMDossierRequest {
  study_id: string;
  dossier_name?: string | null;
  signer_name: string;
  signer_role: string;
  signature_meaning?: string;
}

export interface Quality_CtQFactorCreate {
  study_id: string;
  category?: string;
  critical_aspect: string;
  risk_description: string;
  impact_area: string;
  mitigation_strategy: string;
}

export interface Quality_CtQFactorResponse {
  id: string;
  study_id: string;
  category: string;
  critical_aspect: string;
  risk_description: string;
  impact_area: string;
  mitigation_strategy: string;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_DataIntegrityComprehensiveScanRequest {
  study_id: string;
  subjects?: Quality_SubjectClinicalVectorPayload[] | null;
  audit_events?: Quality_AuditEventPayload[] | null;
  image_attachments?: Quality_ImageAttachmentPayload[] | null;
  auto_dispatch_investigation?: boolean;
}

export interface Quality_DataIntegrityFindingResponse {
  id: string;
  run_id: string;
  study_id: string;
  sentinel_type: Quality_DataIntegritySentinelType;
  severity: Quality_DeviationSeverity;
  anomaly_flag: Quality_ForensicsAnomalyFlag;
  primary_subject_id: string;
  primary_site_id: string;
  matched_subject_id?: string | null;
  matched_site_id?: string | null;
  confidence_score: number;
  similarity_score: number;
  hamming_distance?: number | null;
  transformation_type?: Quality_ImageTransformationType | null;
  evidence_summary: string;
  forensic_metadata: Record<string, any>;
  investigation_deviation_id?: string | null;
  kri_evaluation_id?: string | null;
  status: Quality_DataIntegrityFindingStatus;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type Quality_DataIntegrityFindingStatus =
  "CONFIRMED" | "DISMISSED" | "INVESTIGATING" | "OPEN";

export interface Quality_DataIntegrityInvestigationDispatchRequest {
  finding_id: string;
  rationale?: string;
}

export interface Quality_DataIntegritySentinelRunResponse {
  id: string;
  study_id: string;
  run_name: string;
  scan_timestamp: string;
  sentinel_types: string[];
  total_subjects_scanned: number;
  total_images_scanned: number;
  total_events_scanned: number;
  anomalies_detected_count: number;
  risk_tier: Quality_RiskTier;
  status: string;
  summary_findings: Record<string, any>;
  findings: Quality_DataIntegrityFindingResponse[];
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type Quality_DataIntegritySentinelType =
  | "COMPREHENSIVE_INTEGRITY_SCAN"
  | "DUPLICATE_PATIENT_RECORD"
  | "PERCEPTUAL_IMAGE_HASH"
  | "TEMPORAL_BEHAVIORAL_ANOMALY";

export interface Quality_DataIntegritySimulationRequest {
  study_id?: string;
  num_subjects_per_site?: number;
  include_duplicate_patient?: boolean;
  include_temporal_burst?: boolean;
  include_identical_intervals?: boolean;
  include_image_duplication?: boolean;
}

export interface Quality_DataIntegritySimulationResponse {
  study_id: string;
  subjects_count: number;
  audit_events_count: number;
  image_attachments_count: number;
  simulated_scenarios: string[];
  sample_subjects: Record<string, any>[];
  sample_audit_events: Record<string, any>[];
  sample_image_attachments: Record<string, any>[];
}

export interface Quality_DeviationCreate {
  study_id: string;
  site_id?: string | null;
  title: string;
  description: string;
  severity: Quality_DeviationSeverity;
  type: Quality_DeviationType;
  category?: string | null;
  is_protocol_violation?: boolean;
  impact_safety?: boolean;
  impact_data?: boolean;
  impact_compliance?: boolean;
  source_system?: string;
  source_reference_id?: string | null;
}

export interface Quality_DeviationIngestRequest {
  study_id: string;
  site_id?: string | null;
  title: string;
  description: string;
  severity?: Quality_DeviationSeverity;
  type?: Quality_DeviationType;
  category?: string | null;
  is_protocol_violation?: boolean;
  impact_safety?: boolean;
  impact_data?: boolean;
  impact_compliance?: boolean;
  source_system: string;
  source_reference_id?: string | null;
}

export interface Quality_DeviationResponse {
  id: string;
  study_id: string;
  site_id?: string | null;
  title: string;
  description: string;
  severity: Quality_DeviationSeverity;
  status: Quality_DeviationStatus;
  type: Quality_DeviationType;
  category?: string | null;
  is_protocol_violation: boolean;
  impact_safety?: boolean;
  impact_data?: boolean;
  impact_compliance?: boolean;
  source_system?: string;
  source_reference_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type Quality_DeviationSeverity =
  "MINOR" | "MAJOR" | "CRITICAL" | "SYSTEMATIC_TREND";

export type Quality_DeviationStatus =
  | "REPORTED"
  | "UNDER_INVESTIGATION"
  | "RCA_COMPLETE"
  | "CAPA_INITIATED"
  | "RESOLVED"
  | "CLOSED";

export type Quality_DeviationType =
  | "CONCOMITANT_MED"
  | "ELIGIBILITY"
  | "GCP_COMPLIANCE"
  | "INFORMED_CONSENT"
  | "INVESTIGATIONAL_PRODUCT"
  | "IP_MANAGEMENT"
  | "LAB_SPECIMEN"
  | "OTHER"
  | "PROTOCOL_PROCEDURE"
  | "SAFETY_REPORTING"
  | "VISIT_WINDOW";

export interface Quality_DuplicatePatientScanRequest {
  study_id: string;
  subjects?: Quality_SubjectClinicalVectorPayload[] | null;
  raw_subjects?: Record<string, any>[] | null;
  similarity_threshold?: number;
  auto_dispatch_investigation?: boolean;
}

export type Quality_EffectivenessOutcome =
  "EFFECTIVE" | "INEFFECTIVE" | "PARTIALLY_EFFECTIVE" | "PENDING";

export type Quality_ForensicsAnomalyFlag =
  | "ANOMALOUS_VARIANCE_SHRINKAGE"
  | "BENFORD_NON_CONFORMANCE"
  | "CROSS_SITE_CLONING_SUSPICION"
  | "DATA_FABRICATION_SUSPICION"
  | "DUPLICATE_PATIENT_RECORD"
  | "IDENTICAL_PROCEDURE_INTERVALS"
  | "MULTIVARIATE_MAHALANOBIS_OUTLIER"
  | "OFF_HOURS_BURST_ANOMALY"
  | "PERCEPTUAL_IMAGE_HASH_MATCH"
  | "RAPID_BURST_SUBMISSION"
  | "SUSPICIOUS_TIMESTAMP_CLUSTER"
  | "TERMINAL_DIGIT_ROUNDING_BIAS";

export interface Quality_ForensicsSimulationRequest {
  study_id?: string;
  num_normal_sites?: number;
  include_benford_anomaly?: boolean;
  include_rounding_bias?: boolean;
  include_variance_shrinkage?: boolean;
  include_multivariate_outlier?: boolean;
  samples_per_site?: number;
}

export interface Quality_ForensicsSimulationResponse {
  study_id: string;
  target_variables: string[];
  site_data: Record<string, any>;
  total_sites: number;
  total_observations: number;
  simulated_anomalies: string[];
}

export interface Quality_HTTPValidationError {
  detail?: Quality_ValidationError[];
}

export interface Quality_ImageAttachmentPayload {
  image_id: string;
  subject_id: string;
  site_id: string;
  study_id: string;
  visit_name?: string;
  modality?: string;
  pixel_matrix?: number[][] | null;
  base64_data?: string | null;
  phash?: string | null;
}

export interface Quality_ImageHashScanRequest {
  study_id: string;
  image_attachments?: Quality_ImageAttachmentPayload[] | null;
  raw_images?: Record<string, any>[] | null;
  hamming_threshold?: number;
  auto_dispatch_investigation?: boolean;
}

export type Quality_ImageTransformationType =
  | "BRIGHTNESS_ADJUSTED"
  | "CROPPED_OR_RESCALED"
  | "EXACT"
  | "FLIPPED_HORIZONTAL"
  | "FLIPPED_VERTICAL"
  | "ROTATED_180"
  | "ROTATED_270"
  | "ROTATED_90";

export interface Quality_KRIBatchEvaluationRequest {
  study_id: string;
  kri_code: string;
  site_raw_values: Record<string, any>;
}

export interface Quality_KRIDefinitionCreate {
  code: string;
  name: string;
  category?: string;
  description: string;
  calculation_formula: string;
  green_threshold?: number;
  amber_threshold?: number;
  red_threshold?: number;
  weight?: number;
}

export interface Quality_KRIDefinitionResponse {
  id: string;
  code: string;
  name: string;
  category: string;
  description: string;
  calculation_formula: string;
  green_threshold: number;
  amber_threshold: number;
  red_threshold: number;
  weight: number;
  is_active: boolean;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_KRIMetricEvaluationResponse {
  id: string;
  study_id: string;
  site_id: string;
  kri_code: string;
  evaluation_date: string;
  raw_value: number;
  standardized_z_score: number;
  risk_tier: string;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_MahalanobisMetricsResponse {
  mahalanobis_distance: number;
  mahalanobis_p_value: number;
  correlation_matrix_distance: number;
  site_mean_vector: Record<string, any>;
  site_covariance_matrix: Record<string, any>;
}

export interface Quality_PromoteFindingToCAPARequest {
  action_plan: string;
  preventive_measures?: string | null;
  target_completion_date?: string | null;
}

export interface Quality_QTLBreachEventResponse {
  id: string;
  qtl_id: string;
  study_id: string;
  breach_date: string;
  observed_value: number;
  threshold_value: number;
  root_cause: string;
  corrective_action_summary: string;
  csr_narrative: string;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_QTLCreate {
  study_id: string;
  parameter_name: string;
  target_value: number;
  tolerance_limit: number;
  unit?: string;
}

export interface Quality_QTLEvaluateBreachRequest {
  observed_value: number;
  root_cause: string;
  corrective_action_summary: string;
}

export interface Quality_QTLResponse {
  id: string;
  study_id: string;
  parameter_name: string;
  target_value: number;
  tolerance_limit: number;
  unit: string;
  is_breached: boolean;
  breach_count: number;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_QualitySurveillanceRunRequest {
  study_id: string;
  run_name?: string | null;
  auto_dispatch_deviations?: boolean;
  target_variables?: string[] | null;
  min_subjects_power?: number;
  min_observations_power?: number;
  similarity_threshold?: number;
  hamming_threshold?: number;
}

export interface Quality_QualitySurveillanceRunResponse {
  run_id: string;
  study_id: string;
  run_name: string;
  timestamp: string;
  total_sites: number;
  gated_sites_count: number;
  analyzed_sites_count: number;
  total_subjects: number;
  total_observations: number;
  total_images_scanned: number;
  total_events_scanned: number;
  anomalies_count: number;
  deviations_dispatched_count: number;
  status: string;
  site_summaries: Quality_SiteSurveillanceSummaryResponse[];
  findings: Record<string, any>[];
  deviations: Record<string, any>[];
  forensics_run_id?: string | null;
  sentinel_run_id?: string | null;
}

export type Quality_RACTCategory =
  | "SAFETY"
  | "CONSENT"
  | "STUDY_CONDUCT"
  | "DATA_INTEGRITY"
  | "IP_MANAGEMENT"
  | "TECHNOLOGY";

export interface Quality_RACTCategorySummary {
  category: string;
  total_factors: number;
  high_count: number;
  medium_count: number;
  low_count: number;
  average_score: number;
  linked_kri_count: number;
}

export interface Quality_RACTMatrixSummaryResponse {
  study_id: string;
  total_risk_factors: number;
  high_risk_count: number;
  medium_risk_count: number;
  low_risk_count: number;
  overall_average_score: number;
  linked_kri_coverage_percentage: number;
  category_summaries: Quality_RACTCategorySummary[];
  risk_factors: Quality_RACTRiskFactorResponse[];
}

export interface Quality_RACTRiskFactorCreate {
  study_id: string;
  category: Quality_RACTCategory;
  risk_topic: string;
  risk_description: string;
  probability_score: number;
  impact_score: number;
  detectability_score: number;
  mitigation_strategy: string;
  linked_kri_code?: string | null;
}

export interface Quality_RACTRiskFactorResponse {
  id: string;
  study_id: string;
  category: string;
  risk_topic: string;
  risk_description: string;
  probability_score: number;
  impact_score: number;
  detectability_score: number;
  total_risk_score: number;
  risk_tier: string;
  mitigation_strategy: string;
  linked_kri_code?: string | null;
  is_active?: boolean;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_RACTRiskFactorUpdate {
  risk_topic?: string | null;
  risk_description?: string | null;
  probability_score?: number | null;
  impact_score?: number | null;
  detectability_score?: number | null;
  mitigation_strategy?: string | null;
  linked_kri_code?: string | null;
  is_active?: boolean | null;
}

export interface Quality_RCACreateOrUpdate {
  methodology?: string;
  investigation_details: string;
  root_cause_summary: string;
  five_whys_chain?: Record<string, any> | any[] | null;
  fishbone_categories?: Record<string, any> | null;
  contributing_factors?: any[] | null;
  version_index?: number | null;
}

export interface Quality_RCAResponse {
  id: string;
  deviation_id: string;
  methodology: string;
  investigation_details: string;
  root_cause_summary: string;
  five_whys_chain?: Record<string, any> | any[] | null;
  fishbone_categories?: Record<string, any> | null;
  contributing_factors?: any[] | null;
  study_id: string;
  site_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_RegulatoryClockStatusResponse {
  breach_id: string;
  study_id: string;
  status: string;
  reporting_deadline?: string | null;
  regulatory_clock_hours_remaining: number;
  is_approaching_deadline: boolean;
  is_overdue: boolean;
  affected_authorities?: any[] | null;
}

export type Quality_RiskTier = "CRITICAL" | "HIGH" | "LOW" | "MEDIUM";

export type Quality_RoundingBiasTier =
  "MODERATE_ROUNDING" | "NORMAL" | "SEVERE_ROUNDING";

export interface Quality_SeriousBreachConfirmRequest {
  affected_authorities: string[];
}

export interface Quality_SeriousBreachReportRequest {
  study_id: string;
  site_id?: string | null;
  title: string;
  summary: string;
  event_date: string;
  discovery_date: string;
  affected_authorities?: string[];
}

export interface Quality_SeriousBreachResponse {
  id: string;
  study_id: string;
  site_id?: string | null;
  title: string;
  summary: string;
  event_date: string;
  discovery_date: string;
  confirmation_date?: string | null;
  reporting_deadline?: string | null;
  affected_authorities?: any[] | null;
  status: string;
  regulatory_clock_hours_remaining?: number;
  lead_qa_id: string;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_SeriousBreachStatusUpdate {
  status: Quality_BreachStatus;
}

export interface Quality_SiteForensicsResponse {
  id: string;
  run_id: string;
  study_id: string;
  site_id: string;
  sample_size: number;
  benford: Quality_BenfordDigitDistributionResponse;
  terminal_digit: Quality_TerminalDigitDistributionResponse;
  multivariate: Quality_MahalanobisMetricsResponse;
  variance_shrinkage: Quality_VarianceShrinkageMetricsResponse;
  composite_risk_score: number;
  risk_tier: Quality_RiskTier;
  is_anomalous_outlier: boolean;
  flagged_anomaly_reasons: string[];
  kri_evaluation_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_SiteObservationPayload {
  site_id: string;
  observations: Record<string, any>[];
}

export interface Quality_SiteRiskProfileResponse {
  id: string;
  study_id: string;
  site_id: string;
  evaluation_date: string;
  composite_risk_score: number;
  risk_rank: number;
  percentile_rank?: number;
  risk_tier?: string;
  high_risk_kri_count: number;
  active_deviations_count: number;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_SiteSurveillanceSummaryResponse {
  site_id: string;
  sample_size: number;
  observation_count: number;
  power_status: string;
  is_sample_gated: boolean;
  composite_risk_score: number;
  risk_tier: string;
  anomalies_detected: string[];
  deviations_created: string[];
  kri_alerts_triggered: string[];
  details: Record<string, any>;
}

export interface Quality_StatisticalForensicsAnalyzeRequest {
  study_id: string;
  target_variables?: string[];
  site_observations?: Record<string, any> | null;
  site_data?: Quality_SiteObservationPayload[] | null;
  run_name?: string | null;
  auto_trigger_kri?: boolean;
}

export interface Quality_StatisticalForensicsRunResponse {
  id: string;
  study_id: string;
  run_name: string;
  analysis_timestamp: string;
  target_variables: string[];
  total_sites: number;
  total_observations: number;
  anomalous_sites_count: number;
  status: string;
  site_results: Quality_SiteForensicsResponse[];
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface Quality_SubjectClinicalVectorPayload {
  subject_id: string;
  site_id: string;
  study_id: string;
  demographics?: Record<string, any>;
  vital_signs?: Record<string, any>;
  ecg_intervals?: Record<string, any>;
  lab_panel?: Record<string, any>;
  medical_history?: string[];
}

export interface Quality_TemporalAnomalyScanRequest {
  study_id: string;
  audit_events?: Quality_AuditEventPayload[] | null;
  raw_events?: Record<string, any>[] | null;
  work_hour_start?: number;
  work_hour_end?: number;
  auto_dispatch_investigation?: boolean;
}

export interface Quality_TerminalDigitDistributionResponse {
  sample_size: number;
  chi_square: number;
  p_value: number;
  zero_five_index: number;
  rounding_bias_tier: Quality_RoundingBiasTier;
  observed_frequencies: Record<string, any>;
  expected_frequencies: Record<string, any>;
}

export interface Quality_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Quality_VarianceShrinkageMetricsResponse {
  variance_ratio: number;
  variance_shrinkage_p_value: number;
  kl_divergence: number;
  is_variance_shrunk: boolean;
}

export type Safety_AIReviewStatus =
  "DRAFT_AI" | "PENDING_REVIEW" | "APPROVED" | "REJECTED";

export type Safety_AS2Authority =
  "FDA_ESG" | "EMA_GATEWAY" | "PMDA" | "MHRA" | "HEALTH_CANADA";

export interface Safety_AS2OutboundTransmissionRequestDTO {
  case_id?: string | null;
  study_id?: string | null;
  icsr_xml_payload: string;
  target_authority: Safety_AS2Authority;
  sender_as2_id?: string;
  receiver_as2_id?: string | null;
  environment?: string;
  custom_headers?: Record<string, any>;
}

export interface Safety_AS2TransmissionAuditLogDTO {
  id: string;
  transmission_id: string;
  action: string;
  from_status?: string | null;
  to_status?: string | null;
  details: string;
  created_at: string;
  created_by: string;
  reason_for_change?: string | null;
  version_index: number;
}

export interface Safety_AS2TransmissionResponseDTO {
  id: string;
  transmission_id: string;
  case_id?: string | null;
  study_id?: string | null;
  worldwide_unique_case_id?: string | null;
  message_id: string;
  as2_from: string;
  as2_to: string;
  target_authority: string;
  environment: string;
  status: string;
  payload_digest_sha256: string;
  mic: string;
  encryption_algorithm: string;
  signature_algorithm: string;
  outbound_payload_size: number;
  outbound_xml?: string | null;
  mdn_message_id?: string | null;
  mdn_disposition?: string | null;
  mdn_mic?: string | null;
  mdn_status?: string | null;
  mdn_received_at?: string | null;
  ack_a_status?: string | null;
  ack_a_received_at?: string | null;
  ack_b_status?: string | null;
  ack_b_received_at?: string | null;
  ack_b_errors?: Record<string, any>[];
  ack_c_status?: string | null;
  ack_c_received_at?: string | null;
  ack_c_details?: Record<string, any>;
  error_message?: string | null;
  retry_count?: number;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  audit_events?: Safety_AS2TransmissionAuditLogDTO[];
}

export type Safety_AckType = "ACK_A" | "ACK_B" | "ACK_C";

export interface Safety_AuthorityRuleEvaluationResult {
  authority: Safety_ReportingAuthority;
  country_code: string;
  is_expedited: boolean;
  timeframe_days?: number | null;
  due_date?: string | null;
  rule_name: string;
  statutory_citation: string;
  classification: Safety_ExpeditedClassification;
  rationale: string;
  countdown?: Safety_CountdownClockStatus | null;
}

export type Safety_BenchmarkReferenceDataset =
  "INTERNAL_TRIAL_POOL" | "FAERS" | "EUDRAVIGILANCE" | "CUSTOM";

export type Safety_CausalityCategory =
  | "RELATED"
  | "SUSPECTED"
  | "POSSIBLY_RELATED"
  | "PROBABLY_RELATED"
  | "DEFINITELY_RELATED"
  | "NOT_RELATED"
  | "UNLIKELY";

export interface Safety_ClinicalTimelineEvent {
  event_id: string;
  event_type: Safety_TimelineEventType;
  event_date?: string | null;
  title: string;
  description: string;
  domain?: string | null;
  sequence?: number | null;
  source_record_id?: string | null;
  details?: Record<string, any>;
}

export type Safety_ClockAlertLevel =
  "NORMAL" | "WARNING" | "CRITICAL" | "OVERDUE";

export interface Safety_CountdownClockStatus {
  clock_start_date: string;
  clock_due_date: string;
  total_hours: number;
  elapsed_hours: number;
  elapsed_percentage: number;
  remaining_hours: number;
  is_overdue?: boolean;
  alert_level?: Safety_ClockAlertLevel;
}

export interface Safety_DSURLineListingItem {
  case_id: string;
  subject_id: string;
  study_id: string;
  treatment_arm?: string;
  dosage_cohort?: string | null;
  age?: number | null;
  sex?: string | null;
  country?: string;
  meddra_pt: string;
  meddra_soc: string;
  ae_term: string;
  onset_date: string;
  resolution_date?: string | null;
  outcome?: string;
  seriousness_death?: boolean;
  seriousness_life_threatening?: boolean;
  seriousness_hospitalization?: boolean;
  seriousness_disability?: boolean;
  seriousness_congenital_anomaly?: boolean;
  seriousness_other?: boolean;
  causality?: string;
  expectedness?: string;
  susar?: boolean;
  is_aesi?: boolean;
  aesi_category?: string | null;
  action_taken?: string;
}

export interface Safety_DSURSummaryTabulationRow {
  meddra_soc: string;
  meddra_pt: string;
  is_aesi?: boolean;
  aesi_category?: string | null;
  is_fatal?: boolean;
  is_susar?: boolean;
  active_arm_count?: number;
  active_arm_subjects?: number;
  comparator_arm_count?: number;
  comparator_arm_subjects?: number;
  placebo_arm_count?: number;
  placebo_arm_subjects?: number;
  total_event_count?: number;
  total_subjects_count?: number;
  cumulative_event_count?: number;
  cumulative_subjects_count?: number;
  rsi_listed?: boolean;
  rsi_expected_rate?: number | null;
  observed_rate_pct?: number;
  observed_incidence_rate_pyr?: number;
  rsi_rate_ratio?: number | null;
  rsi_threshold_breached?: boolean;
}

export interface Safety_DeviceMedWatchReportResponseDTO {
  case_number: string;
  report_type?: string;
  reporting_authority?: string;
  device_name: string;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  classification: string;
  defect_type: string;
  is_5_day_expedited: boolean;
  incident_summary: string;
  vigilance_deadline_days: number;
  vigilance_due_date: string;
  payload_json: Record<string, any>;
  payload_xml: string;
}

export interface Safety_DeviceSafetyInvestigationAdjudicateDTO {
  classification: string;
  causality: string;
  is_usade?: boolean;
  investigation_notes?: string | null;
  root_cause_summary?: string | null;
  capa_required?: boolean;
  capa_reference?: string | null;
  fsca_required?: boolean;
  signature_token: string;
  reason_for_change?: string;
}

export interface Safety_DeviceSafetyInvestigationCreateDTO {
  case_number?: string | null;
  study_id: string;
  site_id: string;
  subject_id?: string | null;
  deficiency_id: string;
  device_name?: string;
  device_udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  classification?: string;
  causality?: string;
  is_usade?: boolean;
  investigation_notes?: string | null;
  reason_for_change?: string;
}

export interface Safety_DeviceSafetyInvestigationResponseDTO {
  id: string;
  case_number: string;
  study_id: string;
  site_id: string;
  subject_id?: string | null;
  deficiency_id: string;
  device_name: string;
  device_udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  classification: string;
  causality: string;
  is_usade: boolean;
  vigilance_reporting_required: boolean;
  vigilance_deadline_days: number;
  vigilance_due_date: string;
  days_remaining: number;
  is_overdue: boolean;
  notified_body_notified: boolean;
  competent_authority_notified: boolean;
  status: string;
  investigation_notes?: string | null;
  root_cause_summary?: string | null;
  capa_required: boolean;
  capa_reference?: string | null;
  fsca_required: boolean;
  safety_physician_user_id?: string | null;
  adjudicated_at?: string | null;
  esignature_manifest_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Safety_DeviceVigilanceReportResponseDTO {
  case_number: string;
  report_type?: string;
  reporting_authority?: string;
  device_name: string;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  classification: string;
  incident_summary: string;
  vigilance_deadline_days: number;
  vigilance_due_date: string;
  payload_json: Record<string, any>;
  payload_xml: string;
}

export interface Safety_DisproportionalityMetricResult {
  drug_name: string;
  meddra_pt: string;
  meddra_soc?: string | null;
  term_level?: Safety_StratificationLevel;
  case_count_a: number;
  case_count_b?: number;
  case_count_c?: number;
  case_count_d?: number;
  total_n?: number;
  expected_count_e: number;
  prr: number;
  prr_ci_lower: number;
  prr_ci_upper: number;
  chi_square_yates: number;
  ror: number;
  ror_ci_lower: number;
  ror_ci_upper: number;
  ebgm: number;
  eb05: number;
  eb95: number;
  is_prr_signal?: boolean;
  is_ror_signal?: boolean;
  is_ebgm_signal?: boolean;
  overall_signal_flag?: boolean;
  signal_strength?: Safety_SignalStrength;
  stratum?: string | null;
  benchmark_dataset?: string | null;
  benchmark_relative_risk?: number | null;
}

export type Safety_ExpectednessCategory = "EXPECTED" | "UNEXPECTED";

export type Safety_ExpeditedClassification =
  | "SUSAR_FATAL_LIFE_THREATENING_7_DAY"
  | "SUSAR_SERIOUS_UNEXPECTED_15_DAY"
  | "NON_EXPEDITED_PERIODIC"
  | "EXEMPT";

export type Safety_ExposureStratificationCategory =
  "TREATMENT_ARM" | "DOSAGE_COHORT" | "AGE_GROUP" | "SEX" | "OVERALL";

export interface Safety_ExposureStratificationRow {
  category: Safety_ExposureStratificationCategory;
  group_name: string;
  subject_count?: number;
  interval_patient_years?: number;
  cumulative_patient_years?: number;
  mean_duration_days?: number;
  median_duration_days?: number;
}

export interface Safety_GroundedClaim {
  claim_id: string;
  sentence_text: string;
  section_type: Safety_NarrativeSectionType;
  grounded_event_ids?: string[];
  confidence_score?: number;
}

export interface Safety_HTTPValidationError {
  detail?: Safety_ValidationError[];
}

export type Safety_ICSRApprovalStatus =
  "DRAFT" | "PHYSICIAN_APPROVED" | "TRANSMITTED" | "REJECTED";

export interface Safety_ICSRDataExportRequest {
  job_name: string;
  icsr: Safety_IndividualCaseSafetyReport;
}

export interface Safety_ICSRHeader {
  sender_organization: string;
  receiver_organization: string;
  message_type?: string;
  transmission_date: string;
  message_id: string;
}

export interface Safety_ICSRPatient {
  patient_id: string;
  sex: string;
  age?: number | null;
  age_unit?: string | null;
  birth_date?: string | null;
}

export interface Safety_ICSRReactionEvent {
  reaction_term: string;
  meddra_coding?: Safety_MedDRACoding | null;
  start_date?: string | null;
  end_date?: string | null;
  outcome?: string | null;
  seriousness_death?: string;
  seriousness_life_threatening?: string;
  seriousness_hospitalization?: string;
  seriousness_disability?: string;
  seriousness_congenital_anomaly?: string;
  seriousness_other_medically_important?: string;
}

export interface Safety_ICSRReportIdentifiers {
  worldwide_unique_case_id: string;
  local_report_id?: string | null;
  first_sender_type?: string | null;
}

export interface Safety_ICSRRuleEvaluationInput {
  case_id: string;
  study_id: string;
  subject_id: string;
  receipt_date: string;
  seriousness_death?: boolean;
  seriousness_life_threatening?: boolean;
  seriousness_hospitalization?: boolean;
  seriousness_disability?: boolean;
  seriousness_congenital_anomaly?: boolean;
  seriousness_other?: boolean;
  causality?: Safety_CausalityCategory;
  expectedness?: Safety_ExpectednessCategory;
  target_authorities?: Safety_ReportingAuthority[] | null;
  reason_for_change?: string | null;
}

export interface Safety_ICSRRuleEvaluationSummary {
  id: string;
  case_id: string;
  study_id: string;
  subject_id: string;
  receipt_date: string;
  is_serious: boolean;
  is_unexpected: boolean;
  is_causal: boolean;
  is_susar: boolean;
  any_expedited: boolean;
  shortest_timeframe_days?: number | null;
  overall_alert_level?: Safety_ClockAlertLevel;
  evaluations?: Safety_AuthorityRuleEvaluationResult[];
  approval_status?: Safety_ICSRApprovalStatus;
  approved_by_user_id?: string | null;
  approved_at?: string | null;
  esignature_manifest_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
}

export interface Safety_ICSRSuspectDrug {
  drug_name: string;
  active_substance_name?: string | null;
  dosage_text?: string | null;
  route_of_administration?: string | null;
  action_taken_with_drug?: string | null;
  drug_role?: string;
}

export interface Safety_IndividualCaseSafetyReport {
  version_index?: number;
  reason_for_change?: string | null;
  header: Safety_ICSRHeader;
  report_identifiers: Safety_ICSRReportIdentifiers;
  patient: Safety_ICSRPatient;
  reactions?: Safety_ICSRReactionEvent[];
  suspect_drugs?: Safety_ICSRSuspectDrug[];
}

export interface Safety_MDNProcessRequestDTO {
  mdn_payload: string;
  authority_cert_pem?: string | null;
}

export interface Safety_MDNProcessResponseDTO {
  is_valid_mdn: boolean;
  original_message_id?: string | null;
  disposition_mode?: string | null;
  disposition_type: string;
  disposition_raw?: string | null;
  received_content_mic?: string | null;
  mic_matches?: boolean | null;
  is_signed?: boolean;
  signature_valid?: boolean | null;
  human_readable_text?: string | null;
  error_details?: string | null;
}

export interface Safety_MGPSMultiItemResult {
  item_set: string[];
  observed_n: number;
  expected_e: number;
  ebgm: number;
  eb05: number;
  eb95: number;
  interaction_score: number;
  is_interaction_signal?: boolean;
}

export interface Safety_MedDRACoding {
  llt_code: string;
  llt_name: string;
  pt_code: string;
  pt_name: string;
  hlt_code: string;
  hlt_name: string;
  hlgt_code: string;
  hlgt_name: string;
  soc_code: string;
  soc_name: string;
  primary_soc_flag?: string | null;
  score?: number;
}

export interface Safety_NarrativeGenerateRequest {
  study_id: string;
  subject_id: string;
  sae_event_key: string;
  worldwide_unique_case_id?: string | null;
  reason_for_change: string;
  additional_context?: string | null;
}

export type Safety_NarrativeSectionType =
  | "DEMOGRAPHICS_BASELINE"
  | "MEDICAL_TREATMENT_HISTORY"
  | "INDEX_AE_CHRONOLOGY"
  | "DIAGNOSTIC_LABS"
  | "CLINICAL_MANAGEMENT"
  | "OUTCOME_CAUSALITY";

export interface Safety_NarrativeSignRequest {
  narrative_id: string;
  reason_for_change: string;
  signature_secret?: string | null;
}

export interface Safety_NarrativeSignResponse {
  narrative_id: string;
  review_status?: Safety_AIReviewStatus;
  approved_by_user_id: string;
  approved_at: string;
  esignature_manifest_id: string;
  message: string;
}

export interface Safety_PeriodicReportCompilationRequest {
  report_type?: Safety_PeriodicSafetyReportType;
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  sponsor_name?: string;
  include_line_listings?: boolean;
  include_summary_tabulations?: boolean;
  include_signal_analyses?: boolean;
  benefit_risk_narrative?: string | null;
  cumulative_exposure_subjects?: number;
  rsi_config?: Safety_ReferenceSafetyInformationConfig | null;
  aesi_terms?: string[] | null;
  cases?: Record<string, any>[] | null;
  subjects_exposure_data?: Record<string, any>[] | null;
  auto_lock_snapshot?: boolean;
}

export interface Safety_PeriodicReportPayload {
  report_id: string;
  report_type: Safety_PeriodicSafetyReportType;
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  sponsor_name: string;
  snapshot?: Safety_RegulatorySnapshot | null;
  exposure_metrics?: Safety_SubjectExposureMetrics;
  total_subject_exposure: number;
  total_saes: number;
  total_fatal_cases?: number;
  total_susars?: number;
  total_aesi?: number;
  line_listings?: Safety_DSURLineListingItem[];
  summary_tabulations?: Safety_DSURSummaryTabulationRow[];
  rsi_comparison_summary?: Record<string, any>[];
  signal_summary?: Record<string, any>[];
  benefit_risk_assessment: string;
  conclusions: string;
  regulatory_guidelines?: string[];
  qppv_signoff?: Safety_QPPVSignoffManifest | null;
  xml_submission_package?: string | null;
}

export interface Safety_PeriodicReportResponseDTO {
  id: string;
  report_type: string;
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  status: string;
  total_subject_exposure: number;
  total_saes: number;
  total_fatal_cases?: number;
  total_susars?: number;
  total_aesi?: number;
  snapshot_hash?: string | null;
  line_listings_count: number;
  summary_tabulations_count: number;
  signals_included_count: number;
  payload: Safety_PeriodicReportPayload;
  approved_by_user_id?: string | null;
  approved_at?: string | null;
  esignature_manifest_id?: string | null;
  qppv_signoff?: Safety_QPPVSignoffManifest | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Safety_PeriodicReportSignRequest {
  user_id: string;
  meaning_of_signature?: string;
  signature_secret: string;
  role?: string | null;
  comments?: string | null;
}

export interface Safety_PeriodicReportSignResponse {
  report_id: string;
  status: Safety_PeriodicReportStatus;
  approved_by_user_id: string;
  approved_at: string;
  esignature_manifest_id: string;
  message: string;
  qppv_manifest?: Safety_QPPVSignoffManifest | null;
}

export type Safety_PeriodicReportStatus =
  "DRAFT" | "IN_REVIEW" | "APPROVED" | "ARCHIVED";

export type Safety_PeriodicSafetyReportType = "DSUR" | "PSUR" | "PBRER";

export interface Safety_QPPVSignoffManifest {
  qppv_user_id: string;
  qppv_name: string;
  qppv_role?: string;
  meaning_of_signature?: string;
  signed_at: string;
  manifest_id: string;
  snapshot_hash: string;
  report_hash: string;
  signature_token: string;
  comments?: string | null;
  verification_status?: string;
}

export interface Safety_ReferenceSafetyInformationConfig {
  rsi_document_id?: string;
  rsi_version?: string;
  effective_date?: string;
  listed_terms?: Safety_ReferenceSafetyInformationTerm[];
}

export interface Safety_ReferenceSafetyInformationTerm {
  meddra_pt: string;
  meddra_soc: string;
  is_listed?: boolean;
  expected_frequency_rate?: number;
  severity_grade_threshold?: number;
  rsi_version?: string;
}

export interface Safety_RegulatoryAckProcessRequestDTO {
  ack_payload: string;
  ack_type: Safety_AckType;
  transmission_id?: string | null;
}

export interface Safety_RegulatoryAckProcessResponseDTO {
  ack_type: string;
  ack_status: string;
  is_accepted: boolean;
  authority_receipt_id?: string | null;
  worldwide_unique_case_id?: string | null;
  message_id?: string | null;
  authority_status_code?: string | null;
  errors?: string[];
  warnings?: string[];
  received_at: string;
}

export interface Safety_RegulatorySnapshot {
  snapshot_id: string;
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  snapshot_hash: string;
  total_cases_frozen?: number;
  total_subjects_frozen?: number;
  is_locked?: boolean;
  locked_at?: string | null;
  locked_by?: string | null;
  lock_reason?: string | null;
  dataset_manifest?: Record<string, any>;
}

export interface Safety_RegulatorySnapshotLockRequest {
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  lock_reason?: string;
  cases?: Record<string, any>[] | null;
  subjects_data?: Record<string, any>[] | null;
}

export interface Safety_RegulatorySnapshotLockResponse {
  snapshot: Safety_RegulatorySnapshot;
}

export interface Safety_RegulatorySnapshotVerifyRequest {
  snapshot_id: string;
  snapshot_hash: string;
  dataset_manifest: Record<string, any>;
}

export interface Safety_RegulatorySnapshotVerifyResponse {
  snapshot_id: string;
  is_valid: boolean;
  computed_hash: string;
  expected_hash: string;
  match_status: string;
  verified_at: string;
}

export type Safety_ReportingAuthority =
  "US_FDA" | "EMA" | "PMDA" | "MHRA" | "HEALTH_CANADA";

export interface Safety_SAEDiscrepancyResponse {
  id: string;
  run_id: string;
  source: string;
  case_event_key: string;
  field_name: string;
  expected_value?: string | null;
  actual_value?: string | null;
  meddra_version?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Safety_SAEReconciliationJobRequest {
  study_id: string;
}

export interface Safety_SAEReconciliationJobResponse {
  id: string;
  study_id: string;
  status: string;
  error_message?: string | null;
  run_id?: string | null;
  result_summary?: Record<string, any> | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Safety_SAEReconciliationRunRequest {
  study_id: string;
}

export interface Safety_SAEReconciliationRunResponse {
  id: string;
  study_id: string;
  run_date: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  discrepancies?: Safety_SAEDiscrepancyResponse[];
}

export interface Safety_SafetyAuditLogResponse {
  id: string;
  created_at: string;
  created_by: string;
  reason_for_change?: string | null;
  version_index: number;
  action: string;
  details: string;
  record_id?: string | null;
}

export interface Safety_SafetyCaseICSRCreate {
  worldwide_unique_case_id: string;
  patient_id: string;
  case_data: Record<string, any>;
}

export interface Safety_SafetyCaseICSRResponse {
  id: string;
  worldwide_unique_case_id: string;
  patient_id: string;
  case_data: Record<string, any>;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Safety_SafetyExportJobCreate {
  job_name: string;
}

export interface Safety_SafetyExportJobResponse {
  id: string;
  job_name: string;
  status: string;
  output?: string | null;
  error?: string | null;
  error_message?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Safety_SafetyNarrativeDTO {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  model_identifier: string;
  prompt_hash: string;
  confidence_score: number;
  review_status?: Safety_AIReviewStatus;
  approved_by_user_id?: string | null;
  approved_at?: string | null;
  esignature_manifest_id?: string | null;
  id: string;
  study_id: string;
  subject_id: string;
  case_id: string;
  sae_event_key: string;
  title: string;
  sections?: Safety_SafetyNarrativeSection[];
  raw_narrative_text: string;
  timeline_events?: Safety_ClinicalTimelineEvent[];
  grounded_claims?: Safety_GroundedClaim[];
}

export interface Safety_SafetyNarrativeSection {
  section_type: Safety_NarrativeSectionType;
  section_title: string;
  content: string;
  grounded_claims?: Safety_GroundedClaim[];
  order_index?: number;
}

export interface Safety_SafetySignalAlertDTO {
  id: string;
  detection_run_id?: string | null;
  study_id?: string | null;
  target_drug: string;
  meddra_term: string;
  term_level: string;
  meddra_soc?: string | null;
  case_count_a: number;
  expected_count_e: number;
  prr: number;
  prr_ci_lower: number;
  prr_ci_upper: number;
  chi_square_yates: number;
  ror: number;
  ror_ci_lower: number;
  ror_ci_upper: number;
  ebgm: number;
  eb05: number;
  eb95: number;
  severity: string;
  status: string;
  triage_summary: string;
  assigned_safety_officer?: string | null;
  review_decision?: string | null;
  review_justification?: string | null;
  clinical_summary?: string | null;
  reviewed_by?: string | null;
  reviewed_at?: string | null;
  esignature_manifest_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Safety_SafetySignalAlertReviewRequest {
  decision: Safety_SafetySignalReviewDecision;
  justification: string;
  clinical_summary?: string | null;
  assigned_safety_officer?: string | null;
  recommended_action?: string | null;
}

export interface Safety_SafetySignalAlertSignRequest {
  user_id: string;
  meaning_of_signature?: string;
  signature_secret: string;
  decision: Safety_SafetySignalReviewDecision;
  justification: string;
  clinical_summary?: string | null;
}

export interface Safety_SafetySignalAlertSignResponse {
  alert_id: string;
  status: Safety_SafetySignalAlertStatus;
  reviewed_by: string;
  reviewed_at: string;
  esignature_manifest_id: string;
  message: string;
}

export type Safety_SafetySignalAlertStatus =
  "TRIGGERED" | "UNDER_REVIEW" | "CONFIRMED_SIGNAL" | "REFUTED" | "CLOSED";

export type Safety_SafetySignalReviewDecision =
  "CONFIRM_SIGNAL" | "REFUTE_SIGNAL" | "REQUEST_SAFETY_INQUIRY" | "CLOSE_ALERT";

export interface Safety_SignICSRRuleEvaluationRequest {
  reason_for_change: string;
  signature_secret?: string | null;
  meaning_of_signature?: string;
}

export interface Safety_SignICSRRuleEvaluationResponse {
  evaluation_id: string;
  approval_status: Safety_ICSRApprovalStatus;
  approved_by_user_id: string;
  approved_at: string;
  esignature_manifest_id: string;
  message: string;
}

export interface Safety_SignalDetectionScanRequest {
  target_drug: string;
  study_id?: string | null;
  comparator_drugs?: string[];
  benchmark_dataset?: Safety_BenchmarkReferenceDataset;
  stratification_levels?: Safety_StratificationLevel[];
  min_case_count?: number;
  prr_threshold?: number;
  chi_square_threshold?: number;
  ror_ci_lower_threshold?: number;
  eb05_threshold?: number;
  auto_trigger_alerts?: boolean;
  cases?: Record<string, any>[] | null;
  benchmark_cases?: Record<string, any>[] | null;
}

export interface Safety_SignalDetectionScanResponse {
  run_id: string;
  study_id?: string | null;
  target_drug: string;
  benchmark_dataset: string;
  total_cases_analyzed: number;
  total_terms_evaluated: number;
  signals_detected_count: number;
  alerts_triggered_count: number;
  results: Safety_DisproportionalityMetricResult[];
  multi_item_results?: Safety_MGPSMultiItemResult[];
  created_at: string;
}

export type Safety_SignalStrength =
  "CRITICAL" | "STRONG" | "MODERATE" | "WEAK" | "NONE";

export type Safety_StratificationLevel = "PT" | "SOC";

export interface Safety_SubjectExposureCalculationRequest {
  study_id: string;
  reporting_period_start: string;
  reporting_period_end: string;
  subjects_exposure_data?: Record<string, any>[] | null;
  cases?: Record<string, any>[] | null;
}

export interface Safety_SubjectExposureCalculationResponse {
  study_id: string;
  reporting_period_start: string;
  reporting_period_end: string;
  metrics: Safety_SubjectExposureMetrics;
}

export interface Safety_SubjectExposureMetrics {
  total_cumulative_subjects?: number;
  total_interval_subjects?: number;
  total_cumulative_patient_years?: number;
  total_interval_patient_years?: number;
  by_treatment_arm?: Safety_ExposureStratificationRow[];
  by_dosage_cohort?: Safety_ExposureStratificationRow[];
  by_age_group?: Safety_ExposureStratificationRow[];
  by_sex?: Safety_ExposureStratificationRow[];
}

export type Safety_TimelineEventType =
  | "DEMOGRAPHICS"
  | "MEDICAL_HISTORY"
  | "DRUG_ADMINISTRATION"
  | "CONCOMITANT_MEDICATION"
  | "ADVERSE_EVENT"
  | "DIAGNOSTIC_LAB"
  | "HOSPITALIZATION"
  | "DECHALLENGE_RECHALLENGE";

export interface Safety_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface Tickets_AttachmentResponse {
  id: string;
  ticket_id: string;
  filename: string;
  file_size_bytes: number;
  mime_type: string;
  storage_uri: string;
  sha256_hash: string;
  uploaded_by: string;
  uploaded_at: string;
  deid_scrubbed: boolean;
  version_index: number;
}

export interface Tickets_Body_upload_ticket_attachment_api_v1_tickets__id__attachments_post {
  file: string;
  reason_for_change?: string;
}

export interface Tickets_CommentCreate {
  body: string;
  visibility?: Tickets_CommentVisibility;
}

export interface Tickets_CommentResponse {
  id: string;
  ticket_id: string;
  body: string;
  visibility?: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type Tickets_CommentVisibility = "PUBLIC" | "INTERNAL_SPONSOR";

export interface Tickets_CrossAppEventCreate {
  event_type: string;
  title: string;
  description: string;
  category?: Tickets_TicketCategory;
  priority?: Tickets_TicketPriority;
  gxp_severity?: Tickets_GxPSeverity;
  source_service: string;
  study_id?: string | null;
  site_id?: string | null;
  related_entity_type: string;
  related_entity_id: string;
  context_payload?: Record<string, any> | null;
}

export type Tickets_GxPSeverity =
  "NOT_APPLICABLE" | "MINOR" | "MAJOR" | "CRITICAL";

export interface Tickets_HTTPValidationError {
  detail?: Tickets_ValidationError[];
}

export interface Tickets_PaginatedTicketAuditLogResponse {
  items: Tickets_TicketAuditLogResponse[];
  total_count: number;
  limit: number;
  offset: number;
  has_more: boolean;
  next_page?: string | null;
  next_cursor?: string | null;
}

export interface Tickets_RAGPreviewRequest {
  query: string;
  study_id: string;
  protocol_version?: string | null;
  top_k?: number;
}

export interface Tickets_RAGTriageResponse {
  ticket_id: string;
  rag_status: string;
  faithfulness_score: number;
  is_grounded: boolean;
  draft_answer?: string | null;
  citations?: Record<string, any>[];
  routed_to_role?: string | null;
  routing_reason: string;
  latency_ms?: number;
}

export interface Tickets_RegulatoryRiskAssessment {
  risk_level: "HIGH_RISK" | "MEDIUM_RISK" | "LOW_RISK";
  affected_gxp_clauses: string[];
  requires_qa_signoff: boolean;
  summary: string;
  risk_summary: string;
}

export type Tickets_ResolutionCode =
  | "RESOLVED_AS_EXPECTED"
  | "PROTOCOL_CLARIFICATION_ISSUED"
  | "DATA_AMENDED"
  | "CAPA_INITIATED"
  | "FALSE_POSITIVE_VOIDED"
  | "WORKAROUND_APPLIED"
  | "CLOSED_UNRESOLVED";

export type Tickets_RootCauseCategory =
  | "HUMAN_ERROR"
  | "PROTOCOL_DESIGN"
  | "PROCESS_WORKFLOW"
  | "SYSTEM_TECHNICAL"
  | "INVESTIGATIONAL_PRODUCT"
  | "VENDOR_LAB"
  | "TRAINING_GAP"
  | "OTHER";

export interface Tickets_SettingDiffEntry {
  setting_key: string;
  old_value: string;
  new_value: string;
  data_type?: string;
}

export interface Tickets_TicketAssignPayload {
  assignee_user?: string | null;
  assignee_role?: string | null;
  version_index: number;
}

export interface Tickets_TicketAuditLogResponse {
  id: string;
  ticket_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change?: string | null;
  version_index: number;
  action: string;
  details: string;
  record_id?: string | null;
}

export type Tickets_TicketCategory =
  | "TECHNICAL"
  | "CLINICAL"
  | "HARDWARE"
  | "ACCESS"
  | "PROTOCOL_DEVIATION"
  | "DATA_QUERY"
  | "SAFETY_ADVERSE_EVENT"
  | "SUPPLY_EXCURSION"
  | "SITE_OPERATIONS"
  | "REGULATORY_ETMF"
  | "CHANGE_REQUEST"
  | "OTHER";

export interface Tickets_TicketCreate {
  title: string;
  description: string;
  category?: Tickets_TicketCategory;
  priority?: Tickets_TicketPriority;
  gxp_severity?: Tickets_GxPSeverity;
  reporter?: string | null;
  assignee_user?: string | null;
  assignee_role?: string | null;
  org_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  related_entity_type?: string | null;
  related_entity_id?: string | null;
  context_payload?: string | null;
  due_date?: string | null;
  custom_sla_hours?: number | null;
  workflow_type?: string | null;
  action_type?: string | null;
  signature_action?: string | null;
}

export interface Tickets_TicketKPISummaryResponse {
  total_tickets: number;
  active_tickets: number;
  open_tickets: number;
  in_progress_tickets: number;
  waiting_tickets: number;
  resolved_tickets: number;
  closed_tickets: number;
  critical_deviations: number;
  sla_breaches: number;
  sla_amber_warnings: number;
  sla_compliance_rate: number;
  mean_time_to_resolution_hours: number;
  category_distribution: Record<string, any>;
  severity_distribution: Record<string, any>;
  rca_distribution: Record<string, any>;
  site_distribution: Record<string, any>;
}

export type Tickets_TicketPriority = "LOW" | "MEDIUM" | "HIGH" | "CRITICAL";

export interface Tickets_TicketResponse {
  id: string;
  reference: string;
  title: string;
  description: string;
  category: string;
  priority: string;
  status: string;
  gxp_severity?: string;
  root_cause_category?: string | null;
  root_cause_summary?: string | null;
  resolution_code?: string | null;
  reporter: string;
  assignee_user?: string | null;
  assignee_role?: string | null;
  org_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  related_entity_type?: string | null;
  related_entity_id?: string | null;
  context_payload?: string | null;
  due_date?: string | null;
  sla_target_at?: string | null;
  sla_paused_at?: string | null;
  sla_total_paused_seconds?: number;
  sla_breached?: boolean;
  sla_amber_warned?: boolean;
  signature_token?: string | null;
  signature_meaning?: string | null;
  signature_timestamp?: string | null;
  signature_user?: string | null;
  is_deleted: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface Tickets_TicketSignaturePayload {
  signature_token: string;
  meaning: string;
  version_index: number;
}

export type Tickets_TicketStatus =
  | "OPEN"
  | "IN_PROGRESS"
  | "WAITING_ON_SITE"
  | "WAITING_ON_SPONSOR"
  | "PENDING_REGULATORY_REVIEW"
  | "RESOLVED"
  | "CLOSED"
  | "REOPENED"
  | "CANCELLED";

export interface Tickets_TicketTransitionPayload {
  status: Tickets_TicketStatus;
  version_index: number;
  root_cause_category?: Tickets_RootCauseCategory | null;
  root_cause_summary?: string | null;
  resolution_code?: Tickets_ResolutionCode | null;
  signature_token?: string | null;
  signature_meaning?: string | null;
}

export interface Tickets_TicketUpdate {
  title?: string | null;
  description?: string | null;
  category?: Tickets_TicketCategory | null;
  priority?: Tickets_TicketPriority | null;
  gxp_severity?: Tickets_GxPSeverity | null;
  status?: Tickets_TicketStatus | null;
  assignee_user?: string | null;
  assignee_role?: string | null;
  org_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  related_entity_type?: string | null;
  related_entity_id?: string | null;
  context_payload?: string | null;
  due_date?: string | null;
  is_deleted?: boolean | null;
  version_index?: number | null;
}

export interface Tickets_ValidationError {
  loc: string | number[];
  msg: string;
  type: string;
  input?: any;
  ctx?: Record<string, any>;
}

export interface ApplyTemplateRequest {
  template_id: string;
}

export interface BudgetAdjustmentCreate {
  grant_id: string;
  site_id: string;
  study_id: string;
  adjustment_type: string;
  description: string;
  amount: number;
  currency?: string;
}

export interface BudgetAdjustmentResponse {
  id: string;
  grant_id: string;
  site_id: string;
  study_id: string;
  adjustment_type: string;
  description: string;
  amount: number;
  currency: string;
  status: string;
  approved_by: string | null;
  approved_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface BudgetLineItemCreate {
  category: string;
  description: string;
  amount: number;
}

export interface BudgetLineItemResponse {
  id: string;
  grant_id: string;
  category: string;
  description: string;
  amount: number;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface CRAAllocationCreate {
  cra_id: string;
  site_id: string;
  study_id: string;
  status?: string | null;
  effective_start_date?: string | null;
  effective_end_date?: string | null;
}

export interface CRAAllocationResponse {
  id: string;
  cra_id: string;
  site_id: string;
  study_id: string;
  status: string;
  effective_start_date: string;
  effective_end_date: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface CRAAllocationUpdate {
  cra_id?: string | null;
  status?: string | null;
  effective_start_date?: string | null;
  effective_end_date?: string | null;
}

export interface CRAWorkloadItem {
  cra_id: string;
  active_allocations_count: number;
  allocated_sites: string[];
  allocated_studies: string[];
}

export interface CTMSAuditLogResponse {
  id: string;
  timestamp: string;
  user_id: string | null;
  user_role: string | null;
  action: string;
  details: string;
}

export interface CTMSResupplyApprovalRequest {
  change_justification: string;
}

export interface CTMSResupplyEventResponse {
  id: string;
  study_id: string;
  site_id: string;
  kit_id: string;
  requested_qty: number;
  status: string;
  triggered_at: string;
}

export interface CTMSStudyCreate {
  study_id: string;
  name: string;
  status?: string | null;
}

export interface CTMSStudyResponse {
  id: string;
  study_id: string;
  name: string;
  status: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type ConflictStrategy = "CLIENT_WINS" | "SERVER_WINS" | "MERGE";

export interface ContractAmendmentCreate {
  summary_of_changes: string;
  revised_budget: number;
  updated_procedures?: Record<string, any>[] | null;
  signature_manifestation?: Record<string, any> | null;
}

export interface ContractAmendmentResponse {
  id: string;
  grant_id: string;
  amendment_number: number;
  effective_date: string;
  previous_budget: number;
  revised_budget: number;
  previous_version_index: number;
  new_version_index: number;
  summary_of_changes: string;
  status: string;
  approved_by: string | null;
  approved_at: string | null;
  signature_manifestation: Record<string, any> | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface CountryMilestoneCreate {
  study_id: string;
  country_code: string;
  milestone_type: string;
  status?: string;
  planned_date?: string | null;
  actual_date?: string | null;
  approval_number?: string | null;
  authority_name?: string | null;
}

export interface CountryMilestoneResponse {
  id: string;
  study_id: string;
  country_code: string;
  milestone_type: string;
  status: string;
  planned_date: string | null;
  actual_date: string | null;
  approval_number: string | null;
  authority_name: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface CountryPrerequisiteRuleCreate {
  study_id: string;
  country_code: string;
  facet: string;
  criterion_code: string;
  criterion_name: string;
  is_mandatory?: boolean;
  description?: string | null;
}

export interface CountryPrerequisiteRuleResponse {
  id: string;
  study_id: string;
  country_code: string;
  facet: string;
  criterion_code: string;
  criterion_name: string;
  is_mandatory: boolean;
  description: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface CurrencyConvertRequest {
  amount: number;
  from_currency?: string;
  to_currency: string;
}

export interface CurrencyConvertResponse {
  original_amount: number;
  from_currency: string;
  to_currency: string;
  exchange_rate: number;
  converted_amount: number;
}

export interface CurrencyExchangeRateCreate {
  base_currency?: string;
  target_currency: string;
  rate: number;
  expiry_date?: string | null;
}

export interface CurrencyExchangeRateResponse {
  id: string;
  base_currency: string;
  target_currency: string;
  rate: number;
  effective_date: string;
  expiry_date: string | null;
  is_active: boolean;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface DOALogResponse {
  site_id: string;
  pi_name: string;
  delegated_staff: Record<string, any>[];
  audit_history: Record<string, any>[];
}

export interface DOASignOffRequest {
  record_id: string;
  reason_for_change: string;
}

export interface DelegationTaskRequest {
  site_id: string;
  staff_user_id: string;
  task_codes: string[];
  start_date: string;
  reason_for_change: string;
}

export interface DeviationActionItemComplete {
  resolution_notes: string;
}

export interface DeviationActionItemCreate {
  deviation_id: string;
  site_id: string;
  description: string;
  assignee_user_id: string;
  assignee_role: string;
  due_date: string;
}

export interface DeviationActionItemResponse {
  id: string;
  deviation_id: string;
  site_id: string;
  description: string;
  assignee_user_id: string;
  assignee_role: string;
  due_date: string;
  status: string;
  resolution_notes: string | null;
  completed_by: string | null;
  completed_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface ETMFSyncRecordResponse {
  id: string;
  study_id: string;
  site_id: string;
  artifact_type: string;
  source_record_id: string;
  etmf_document_id: string;
  dia_zone: string;
  dia_section: string;
  dia_artifact: string;
  sync_status: string;
  error_message: string | null;
  synced_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface ETMFSyncRequest {
  study_id: string;
  site_id: string;
  artifact_type: string;
  source_record_id: string;
  title: string;
  content_text: string;
  dia_zone?: string;
  dia_section?: string;
  dia_artifact?: string;
}

export interface EssentialDocumentCreate {
  study_id: string;
  site_id: string;
  document_type: string;
  file_name: string;
  file_hash: string;
  expiration_date?: string | null;
}

export interface EssentialDocumentResponse {
  id: string;
  study_id: string;
  site_id: string;
  document_type: string;
  file_name: string;
  file_hash: string;
  status: string;
  expiration_date: string | null;
  review_notes: string | null;
  reviewed_by: string | null;
  reviewed_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface EssentialDocumentReview {
  status: string;
  review_notes?: string | null;
}

export interface FinancialInvoiceCreate {
  study_id: string;
  site_id: string;
  grant_id: string;
  invoice_type?: string;
  gross_amount: number;
  withholding_amount?: number;
  currency?: string;
  payable_ids?: string[];
}

export interface FinancialInvoiceResponse {
  id: string;
  study_id: string;
  site_id: string;
  grant_id: string;
  invoice_number: string;
  invoice_type: string;
  gross_amount: number;
  withholding_amount: number;
  net_amount: number;
  currency: string;
  status: string;
  payable_ids: string[];
  approved_by: string | null;
  approved_at: string | null;
  disbursed_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface FindingCreate {
  text: string;
  severity: string;
  resolution_status?: string | null;
}

export interface GeneratedLetterResponse {
  id: string;
  visit_id: string;
  letter_type: string;
  rendered_content: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface GreenlightTokenVerifyRequest {
  token: string;
  site_id?: string | null;
  study_id?: string | null;
}

export interface GreenlightTokenVerifyResponse {
  is_valid: boolean;
  payload?: Record<string, any> | null;
  error?: string | null;
}

export interface IPDestructionCertificateCreate {
  study_id: string;
  site_id: string;
  kit_ids: string[];
  destruction_method?: string;
  witness_user_id: string;
  witness_role: string;
  pi_signature_hash: string;
  reason_for_destruction: string;
}

export interface IPDestructionCertificateResponse {
  id: string;
  study_id: string;
  site_id: string;
  certificate_number: string;
  kit_ids: string[];
  destruction_method: string;
  destruction_date: string;
  witness_user_id: string;
  witness_role: string;
  pi_signature_hash: string;
  pi_signed_at: string;
  reason_for_destruction: string;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface IPKitDispenseRequest {
  subject_id: string;
  visit_id: string;
}

export interface IPKitReconcileRequest {
  returned_units_count: number;
  expected_units_count: number;
  notes?: string | null;
}

export interface IPKitRecordResponse {
  id: string;
  study_id: string;
  site_id: string;
  kit_number: string;
  lot_number: string;
  kit_type: string;
  shipment_tracking_number: string;
  expiration_date: string;
  status: string;
  received_date: string | null;
  dispensed_subject_id: string | null;
  dispensed_visit_id: string | null;
  dispensed_date: string | null;
  returned_units_count: number | null;
  expected_units_count: number | null;
  compliance_percentage: number | null;
  notes: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface IPShipmentReceiveRequest {
  study_id: string;
  site_id: string;
  kit_numbers: string[];
  lot_number: string;
  kit_type?: string;
  shipment_tracking_number: string;
  expiration_date: string;
}

export interface IPTemperatureExcursionCreate {
  study_id: string;
  site_id: string;
  kit_ids: string[];
  excursion_type?: string;
  min_temp_celsius: number;
  max_temp_celsius: number;
  duration_hours: number;
  occurred_at: string;
}

export interface IPTemperatureExcursionDisposition {
  disposition_status: string;
  qa_rationale: string;
}

export interface IPTemperatureExcursionResponse {
  id: string;
  study_id: string;
  site_id: string;
  kit_ids: string[];
  excursion_type: string;
  min_temp_celsius: number;
  max_temp_celsius: number;
  duration_hours: number;
  occurred_at: string;
  disposition_status: string;
  qa_reviewed_by: string | null;
  qa_reviewed_at: string | null;
  qa_rationale: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface InvestigatorGrantCreate {
  study_id: string;
  site_id: string;
  total_budget?: number;
  currency?: string | null;
}

export interface InvestigatorGrantResponse {
  id: string;
  study_id: string;
  site_id: string;
  total_budget: number;
  currency: string;
  status: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface InvestigatorGrantUpdate {
  total_budget?: number | null;
  currency?: string | null;
  status?: string | null;
}

export interface InvestigatorPayableResponse {
  id: string;
  grant_id: string;
  milestone_id: string | null;
  amount: number;
  payment_status: string;
  due_date: string | null;
  paid_at: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface MonitoringVisitComplete {
  actual_date: string;
  findings?: FindingCreate[];
}

export interface MonitoringVisitCreate {
  study_id: string;
  site_id: string;
  cra_id: string;
  visit_type: string;
  scheduled_date: string;
}

export interface MonitoringVisitOfflineSync {
  visit_id: string;
  study_id?: string | null;
  site_id?: string | null;
  actual_date: string;
  device_timestamp: string;
  findings?: FindingCreate[];
  offline_sync_markers: OfflineSyncMarkers;
}

export interface MonitoringVisitResponse {
  id: string;
  study_id: string;
  site_id: string;
  cra_id: string;
  visit_type: string;
  scheduled_date: string;
  actual_date: string | null;
  status: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface OfflineSyncMarkers {
  client_id: string;
  sequence_number: number;
  conflict_strategy?: ConflictStrategy;
  timestamps?: Record<string, any> | null;
  signature?: string | null;
}

export interface PaymentMilestoneCreate {
  milestone_name: string;
  trigger_condition: string;
  amount: number;
}

export interface PaymentMilestoneResponse {
  id: string;
  grant_id: string;
  milestone_name: string;
  trigger_condition: string;
  amount: number;
  is_triggered: boolean;
  triggered_at: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface ProcedurePaymentGridCreate {
  grant_id: string;
  visit_name: string;
  procedure_code: string;
  procedure_name: string;
  base_amount: number;
  overhead_percentage?: number;
  withholding_percentage?: number;
  activity_id?: string | null;
  form_id?: string | null;
  patient_stipend?: number;
  travel_reimbursement?: number;
}

export interface ProcedurePaymentGridResponse {
  id: string;
  grant_id: string;
  visit_name: string;
  procedure_code: string;
  procedure_name: string;
  base_amount: number;
  overhead_percentage: number;
  withholding_percentage: number;
  activity_id?: string | null;
  form_id?: string | null;
  patient_stipend?: number;
  travel_reimbursement?: number;
  is_active: boolean;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface ProtocolDeviationCreate {
  study_id: string;
  site_id: string;
  subject_id?: string | null;
  visit_id?: string | null;
  deviation_category: string;
  severity: string;
  title: string;
  description: string;
  date_occurred: string;
}

export interface ProtocolDeviationRCA {
  root_cause_5whys?: string[];
  root_cause_summary: string;
  corrective_action_plan: string;
  preventive_action_plan: string;
}

export interface ProtocolDeviationResponse {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string | null;
  visit_id: string | null;
  deviation_category: string;
  severity: string;
  title: string;
  description: string;
  date_occurred: string;
  date_identified: string;
  status: string;
  root_cause_5whys: string[];
  root_cause_summary: string | null;
  corrective_action_plan: string | null;
  preventive_action_plan: string | null;
  quality_capa_id: string | null;
  reported_by: string;
  resolved_by: string | null;
  resolved_at: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface RBQMKRIMetricCreate {
  study_id: string;
  site_id: string;
  metric_type: string;
  metric_value: number;
  threshold_low: number;
  threshold_high: number;
  notes?: string | null;
}

export interface RBQMKRIMetricResponse {
  id: string;
  study_id: string;
  site_id: string;
  metric_type: string;
  metric_value: number;
  threshold_low: number;
  threshold_high: number;
  breach_status: string;
  calculation_date: string;
  notes: string | null;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface RecruitmentRecordCreate {
  site_id: string;
  study_id: string;
  screened_count?: number;
  enrolled_count?: number;
  target_count?: number;
  as_of_date?: string | null;
}

export interface RecruitmentRecordResponse {
  id: string;
  site_id: string;
  study_id: string;
  screened_count: number;
  enrolled_count: number;
  target_count: number;
  as_of_date: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface RevokeDelegationRequest {
  record_id: string;
  reason_for_change: string;
}

export interface SiteBudgetTemplateCreate {
  template_name: string;
  study_id?: string | null;
  tier_level?: string;
  currency?: string;
  overhead_percentage?: number;
  withholding_percentage?: number;
  fixed_startup_fee?: number;
  fixed_closeout_fee?: number;
  irb_fee?: number;
  patient_stipend_amount?: number;
  travel_reimbursement_rate?: number;
  fixed_fees?: Record<string, any>[];
  procedure_items?: Record<string, any>[];
}

export interface SiteBudgetTemplateResponse {
  id: string;
  template_name: string;
  study_id: string | null;
  tier_level: string;
  currency: string;
  overhead_percentage: number;
  withholding_percentage: number;
  fixed_startup_fee: number;
  fixed_closeout_fee: number;
  irb_fee: number;
  patient_stipend_amount: number;
  travel_reimbursement_rate: number;
  fixed_fees: Record<string, any>[];
  procedure_items: Record<string, any>[];
  is_active: boolean;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SiteGreenlightGateResponse {
  id: string | null;
  study_id: string;
  site_id: string;
  country_code?: string;
  overall_status: string;
  facet_regulatory_irb?: boolean;
  facet_cta_budget?: boolean;
  facet_eisf_baseline?: boolean;
  facet_doa_log?: boolean;
  facet_staff_training?: boolean;
  facet_ip_release?: boolean;
  contract_approved?: boolean;
  irb_approved?: boolean;
  form_1572_approved?: boolean;
  doa_signed_off?: boolean;
  ip_ready?: boolean;
  greenlight_token?: string | null;
  greenlight_certified_by?: string | null;
  greenlight_certified_at?: string | null;
  is_overridden?: boolean;
  override_sponsor_user_id?: string | null;
  override_medical_user_id?: string | null;
  override_justification?: string | null;
  override_authorized_at?: string | null;
  evaluation_details?: Record<string, any> | null;
  rejection_reason?: string | null;
  created_at?: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SiteGreenlightOverrideRequest {
  study_id: string;
  sponsor_user_id: string;
  medical_user_id: string;
  sponsor_signature: string;
  medical_signature: string;
  justification: string;
  country_code?: string;
}

export interface SiteMilestoneCreate {
  site_id: string;
  study_id: string;
  milestone_type: string;
  planned_date?: string | null;
  actual_date?: string | null;
  status?: string | null;
}

export interface SiteMilestoneResponse {
  id: string;
  site_id: string;
  study_id: string;
  milestone_type: string;
  planned_date: string | null;
  actual_date: string | null;
  status: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SiteMilestoneUpdate {
  planned_date?: string | null;
  actual_date?: string | null;
  status?: string | null;
}

export interface SiteRiskScoreResponse {
  id: string | null;
  study_id: string;
  site_id: string;
  composite_score: number;
  risk_level: string;
  assessment_date: string;
  recommended_monitoring_type: string;
  monitoring_interval_days: number;
  created_at: string | null;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface VisitPayableCalculationResponse {
  gross_amount: number;
  withholding_amount: number;
  net_amount: number;
  visit_name?: string | null;
  procedures_count?: number;
  base_amount?: number;
  overhead_amount?: number;
  patient_stipend_total?: number;
  travel_reimbursement_total?: number;
  currency?: string;
  target_currency?: string | null;
  exchange_rate?: number;
  converted_net_amount?: number | null;
  version_index?: number | null;
}

export interface ArchivalDeliveryResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  id: string;
  status: string;
  attempts: number;
  last_error?: string | null;
  next_retry_at?: string | null;
  completed_at?: string | null;
  retry_eligible: boolean;
  correlation_id: string;
  template_id: string;
  subject_pseudonym: string;
  study_id: string;
  site_id?: string | null;
  etmf_document_id?: string | null;
}

export interface CdiscOdmExportResponse {
  study_id: string;
  subject_pseudonym: string;
  template_id: string;
  version_index: number;
  odm_version: string;
  xml_content: string;
}

export interface ClauseDiffDTO {
  clause_id: string;
  change_type: string;
  old_title: string | null;
  new_title: string | null;
  old_text: string | null;
  new_text: string | null;
  text_diff: string | null;
  is_substantive: boolean;
}

export interface ClauseHarmonizationApplyRequest {
  harmonized_text: string;
  reason_for_change: string;
  protocol_version?: string | null;
}

export interface ClauseHarmonizationApplyResponse {
  clause_id: string;
  version_index: number;
  title: string;
  text: string;
  metrics: ReadabilityMetricsDTO;
  protocol_version: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface ComposedClauseResponse {
  clause_id: string;
  title: string;
  text: string;
  version_index: number;
}

export interface ComposedTemplateResponse {
  id: string;
  template_id: string;
  study_id: string;
  template_name: string;
  protocol_version: string;
  is_published: boolean;
  requires_reconsent: boolean;
  version_index: number;
  clauses: ComposedClauseResponse[];
  workflow_steps: Record<string, any>[];
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface ComprehensionCheckCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  questions: Record<string, any>[];
  expected_answers: Record<string, any>;
  threshold_policy: Record<string, any>;
}

export interface ComprehensionCheckResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  id: string;
  template_id: string;
  questions: Record<string, any>[];
  expected_answers: Record<string, any>;
  threshold_policy: Record<string, any>;
}

export interface ComprehensionSubmissionRequest {
  subject_pseudonym: string;
  submitted_answers: Record<string, any>;
  reason_for_change: string;
}

export interface ComprehensionSubmissionResponse {
  passed: boolean;
  score: number;
  total_questions: number;
  correct_count: number;
  min_required: number;
  next_step: string;
  message: string;
  details?: Record<string, any>[] | null;
}

export interface ConsentAuditLogResponse {
  id: string;
  timestamp: string;
  actor_id: string;
  actor_role: string;
  action: string;
  document_id: string | null;
  details: string;
  reason_for_change: string;
  tenant_id?: string;
  previous_digest?: string | null;
  sha256_digest?: string | null;
}

export interface ConsentClauseCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  clause_id?: string | null;
  study_id: string;
  title: string;
  text: string;
}

export interface ConsentClauseResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  clause_id: string;
  study_id: string;
  title: string;
  text: string;
}

export interface ConsentClauseUpdate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  study_id: string;
  title: string;
  text: string;
}

export interface ConsentDocumentCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  study_id: string;
  site_id: string;
  document_name: string;
  content: string;
}

export interface ConsentDocumentResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  study_id: string;
  site_id: string;
  document_name: string;
  content: string;
}

export interface ConsentSignatureRequest {
  subject_pseudonym: string;
  signature_data?: string | null;
  reason_for_change: string;
  site_id?: string | null;
  role?: string;
  signer_name?: string | null;
  signer_email?: string | null;
  meaning?: string | null;
  lar_relationship?: string | null;
  lar_authority_basis?: string | null;
}

export interface ConsentSignatureResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  id: string;
  template_id: string;
  subject_pseudonym: string;
  role?: string;
  signer_name?: string | null;
  signer_email?: string | null;
  meaning?: string | null;
  signature_data: string | null;
  signed_at: string;
  digest_sha256?: string | null;
  lar_relationship?: string | null;
  lar_authority_basis?: string | null;
}

export interface ConsentTemplateCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  template_id?: string | null;
  study_id: string;
  template_name: string;
  protocol_version: string;
  requires_reconsent?: boolean;
  clauses?: string[];
  workflow_steps?: Record<string, any>[];
}

export interface ConsentTemplateResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  template_id: string;
  study_id: string;
  template_name: string;
  protocol_version: string;
  is_published: boolean;
  requires_reconsent: boolean;
  clauses?: string[];
  workflow_steps?: Record<string, any>[];
}

export interface ConsentTemplateUpdate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  study_id: string;
  template_name: string;
  protocol_version: string;
  requires_reconsent?: boolean;
  clauses?: string[];
  workflow_steps?: Record<string, any>[];
}

export interface ConsentTranslationCreate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  translation_id?: string | null;
  source_id: string;
  source_type: string;
  source_version_index: number;
  language_code: string;
  translated_title: string;
  translated_text: string;
}

export interface ConsentTranslationResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  translation_id: string;
  source_id: string;
  source_type: string;
  source_version_index: number;
  language_code: string;
  translated_title: string;
  translated_text: string;
  status: string;
}

export interface ConsentTranslationUpdate {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  source_id: string;
  source_type: string;
  source_version_index: number;
  language_code: string;
  translated_title: string;
  translated_text: string;
}

export interface ConsentWithdrawalRequest {
  study_id: string;
  site_id: string;
  subject_pseudonym: string;
  template_id: string;
  withdrawal_date?: string | null;
  reason_category: string;
  reason_detail: string;
  scope?: string;
  investigator_id?: string | null;
  reason_for_change: string;
}

export interface ConsentWithdrawalResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  study_id: string;
  site_id: string;
  subject_pseudonym: string;
  template_id: string;
  withdrawal_date: string;
  reason_category: string;
  reason_detail: string;
  scope: string;
  acknowledged_by_investigator: boolean;
  investigator_id: string | null;
}

export interface GranularOptionCreate {
  option_code: string;
  title: string;
  description: string;
  category?: string;
  is_mandatory?: boolean;
  default_selected?: boolean;
  reason_for_change: string;
}

export interface GranularOptionResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  id: string;
  template_id: string;
  option_code: string;
  title: string;
  description: string;
  category: string;
  is_mandatory: boolean;
  default_selected: boolean;
}

export interface JargonSubstitutionDTO {
  original_term: string;
  suggested_term: string;
  rationale: string;
  category?: string;
  confidence_score?: number;
  start_offset?: number | null;
  end_offset?: number | null;
}

export interface MinorMilestoneEvaluationRequest {
  study_id: string;
  subjects: SubjectMilestoneInput[];
  as_of_date?: string | null;
  lead_time_days?: number;
}

export interface MinorMilestoneEvaluationResponse {
  study_id: string;
  evaluation_date: string;
  total_evaluated: number;
  majority_reached_count: number;
  approaching_count: number;
  results: MinorMilestoneResultDTO[];
}

export interface MinorMilestoneResultDTO {
  subject_pseudonym: string;
  date_of_birth: string;
  jurisdiction: string;
  current_age_years: number;
  exact_age: number;
  legal_age_threshold: number;
  majority_reached_date: string;
  has_reached_majority: boolean;
  is_transition_due: boolean;
  days_until_majority: number;
  requires_adult_reconsent: boolean;
  milestone_status: string;
}

export interface ReadabilityAnalysisRequest {
  text: string;
  study_id?: string | null;
}

export interface ReadabilityAnalysisResponse {
  metrics: ReadabilityMetricsDTO;
}

export interface ReadabilityHarmonizationRequest {
  text: string;
  study_id?: string | null;
  target_grade_level?: number;
  protocol_version?: string | null;
}

export interface ReadabilityHarmonizationResponse {
  original_metrics: ReadabilityMetricsDTO;
  harmonized_metrics: ReadabilityMetricsDTO;
  substitutions: JargonSubstitutionDTO[];
  harmonized_text: string;
  grade_level_delta: number;
  model_identifier: string;
}

export interface ReadabilityMetricsDTO {
  word_count: number;
  sentence_count: number;
  syllable_count: number;
  difficult_word_count: number;
  difficult_words: string[];
  flesch_reading_ease: number;
  flesch_kincaid_grade_level: number;
  dale_chall_score: number;
  dale_chall_grade_level: string;
  is_target_grade_level: boolean;
  interpretation: string;
}

export interface ReconsentRequirementResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  study_id: string;
  site_id: string | null;
  template_id: string;
  prior_version_index: number;
  new_version_index: number;
  subject_pseudonym: string;
  status: string;
  change_summary: string;
  substantive_changes: Record<string, any>[];
  deadline_at: string | null;
  completed_consent_id: string | null;
}

export interface ReconsentTriggerRequest {
  study_id: string;
  site_id?: string | null;
  prior_version_index: number;
  new_version_index: number;
  change_summary: string;
  substantive_changes?: Record<string, any>[];
  reason_for_change: string;
}

export interface SubjectConsentCaptureRequest {
  subject_pseudonym: string;
  site_id: string;
  device_timestamp?: string | null;
  source_content_identity: string;
  reason_for_change: string;
  signatures?: Record<string, any>[] | null;
  granular_selections?: Record<string, any>[] | null;
}

export interface SubjectConsentResponse {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  id: string;
  subject_pseudonym: string;
  study_id: string;
  site_id: string;
  template_id: string;
  protocol_version: string;
  source_content_identity: string;
  status?: string;
  server_timestamp: string;
  device_timestamp?: string | null;
  signature_manifest: Record<string, any>;
}

export interface SubjectConsentStatusResponse {
  subject_pseudonym: string;
  study_id: string;
  site_id: string;
  template_id: string;
  version_index: number;
  protocol_version: string;
  signed: boolean;
  comprehension_passed: boolean;
  status?: string;
  requires_reconsent?: boolean;
}

export interface SubjectMilestoneInput {
  subject_pseudonym: string;
  date_of_birth: string;
  jurisdiction?: string | null;
  site_id?: string | null;
  current_consent_role?: string | null;
}

export interface TemplateDiffResponse {
  template_id: string;
  base_version_index: number;
  target_version_index: number;
  total_added: number;
  total_removed: number;
  total_modified: number;
  total_unchanged: number;
  requires_reconsent: boolean;
  substantive_summary: string[];
  clause_diffs: ClauseDiffDTO[];
}

export interface TranslationTransitionRequest {
  status: string;
  reason_for_change: string;
}

export interface TriggerMajorityReconsentRequest {
  study_id: string;
  site_id?: string | null;
  template_id: string;
  adult_version_index?: number;
  subjects: SubjectMilestoneInput[];
  lead_time_days?: number;
  as_of_date?: string | null;
  reason_for_change?: string;
}

export interface VerifiableCertificateResponse {
  study_id: string;
  subject_pseudonym: string;
  template_id: string;
  version_index: number;
  html_content: string;
  digest_sha256: string;
}

export interface BinderCompletenessResponse {
  site_id: string;
  is_complete: boolean;
  sections: BinderSectionStatus[];
}

export interface BinderSectionStatus {
  section_name: string;
  required_artifacts: string[];
  present: string[];
  missing: string[];
}

export interface DocumentCreate {
  study_id: string;
  site_id: string;
  binder_classification: string;
  filename: string;
  content: string;
  mime_type: string;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string;
  reason_for_change: string;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface DocumentResponse {
  id: string;
  study_id: string;
  site_id: string;
  binder_classification: string;
  filename: string;
  content: string;
  mime_type: string;
  version_index: number;
  created_at: string;
  created_by: string;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  sync_status: string;
  source_system: string;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface DocumentUpdate {
  study_id: string;
  site_id: string;
  binder_classification: string;
  filename: string;
  content: string;
  mime_type: string;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string;
  reason_for_change: string;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface EISFDocumentDetail {
  id: string;
  site_id: string;
  section_code: string;
  filename: string;
  version: string;
  expiration_date?: string | null;
  created_at: string;
  created_by: string;
  download_url?: string | null;
}

export interface EISFDocumentIntelligenceRequest {
  content: string;
  filename: string;
  site_id?: string | null;
  study_id?: string | null;
  mime_type?: string;
}

export interface EISFDocumentIntelligenceResponse {
  filename: string;
  recommended_binder_section: string;
  recommended_eisf_folder: string;
  recommended_etmf_artifact_code: string;
  recommended_etmf_artifact_name: string;
  confidence: number;
  extracted_metadata: Record<string, any>;
  signature_completeness: Record<string, any>;
  intelligence_report: Record<string, any>;
}

export interface EISFDocumentUploadRequest {
  study_id: string;
  section_code: string;
  filename: string;
  content: string;
  mime_type: string;
  reason_for_change: string;
  expiration_date?: string | null;
}

export interface EISFFolderNode {
  section_code: string;
  title: string;
  document_count: number;
  subfolders?: EISFFolderNode[];
}

export interface EISFIngestionRequest {
  study_id: string;
  site_id: string;
  binder_classification?: string | null;
  artifact_type?: string | null;
  filename: string;
  content: string;
  mime_type: string;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string;
  reason_for_change?: string | null;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface EISFSyncItem {
  id?: string | null;
  study_id: string;
  site_id: string;
  binder_classification: string;
  filename: string;
  content: string;
  mime_type: string;
  version_index?: number | null;
  metadata_json?: Record<string, any> | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string;
  sync_status?: string;
  conflict_policy?: string;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface EISFSyncRequest {
  submissions: EISFSyncItem[];
}

export interface EISFSyncResponse {
  status?: string;
  processed_count: number;
  created_count: number;
  updated_count: number;
  ignored_count: number;
}

export interface ArchiveJobResponse {
  job_id: string;
  study_id: string;
  status: "PENDING" | "PROCESSING" | "COMPLETED" | "FAILED";
  download_url?: string | null;
}

export interface ArtifactDetail {
  artifact_type: string;
  scope: string;
  status: string;
  document_id?: string | null;
  version_index?: number | null;
}

export interface AuditChainVerificationResponse {
  is_valid: boolean;
  total_sealed_blocks: number;
  total_sealed_records: number;
  latest_block_hash: string | null;
  genesis_block_hash: string | null;
  unsealed_records_count: number;
  tamper_detected: boolean;
  details: string;
}

export interface AuditLogResponse {
  id: string;
  timestamp: string;
  user_id: string;
  user_role: string;
  action: string;
  document_id: string | null;
  details: string;
  reason_for_change?: string | null;
  cryptographic_seal?: string | null;
  old_value?: any | null;
  new_value?: any | null;
  entity_type?: string | null;
  sha256_hash?: string | null;
}

export interface AutoFileRequest {
  filename: string;
  artifact_type?: string | null;
  free_text?: string | null;
  study_id?: string | null;
}

export interface AutoFileResponse {
  resolved_zone: number;
  resolved_section: string;
  artifact_code: string;
  artifact_type: string;
  match_basis: string;
}

export interface AutomatedRedactRequest {
  profile?: ComplianceProfile;
  custom_terms?: string[] | null;
  strategies?: Record<string, any> | null;
  redacted_filename?: string | null;
}

export interface AutomatedRedactResponse {
  status?: string;
  document_id: string;
  version_index: number;
  filename: string;
  categories_counts: Record<string, any>;
  manifest: Record<string, any>;
}

export interface BinderArtifactNode {
  artifact_code: string;
  artifact_name: string;
  status: string;
  document_id?: string | null;
  version_index?: number | null;
}

export interface BinderSectionNode {
  section_code: string;
  section_name: string;
  artifacts: BinderArtifactNode[];
}

export interface BinderStructureResponse {
  study_id: string;
  milestone?: string | null;
  site_id?: string | null;
  zones: BinderZoneNode[];
  present_artifacts: string[];
  missing_artifacts: string[];
}

export interface BinderZoneNode {
  zone_code: number;
  zone_name: string;
  sections: BinderSectionNode[];
}

export interface CRAQCReviewRequest {
  decision: string;
  reason_for_change: string;
  override_artifact_code?: string | null;
  discrepancy_comment?: string | null;
}

export interface CRAQCStagingItem {
  document_id: string;
  study_id: string;
  site_id?: string | null;
  filename: string;
  status: string;
  intelligence_report: DocumentIntelligenceReport;
  staged_at: string;
  staged_by: string;
  qc_assigned_to?: string | null;
  discrepancy_notes?: string[];
}

export type ClassificationConfidenceTier = "HIGH" | "MEDIUM" | "LOW";

export interface CompletenessResponse {
  study_id: string;
  site_id?: string | null;
  milestone: string;
  is_complete: boolean;
  scope: string;
  present_artifacts: string[];
  missing_artifacts: string[];
  per_artifact_detail: ArtifactDetail[];
}

export type ComplianceProfile = "HIPAA" | "GDPR" | "EU_CTR";

export interface DocumentExpirationUpdate {
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
}

export interface DocumentIntelligenceAnalyzeRequest {
  content: string;
  filename: string;
  mime_type?: string;
  study_id?: string | null;
  site_id?: string | null;
  artifact_hint?: string | null;
  free_text?: string | null;
  taxonomy_version?: string | null;
  document_id?: string | null;
}

export interface DocumentIntelligenceClassifyRequest {
  content?: string | null;
  filename: string;
  artifact_hint?: string | null;
  free_text?: string | null;
  taxonomy_version?: string | null;
}

export interface DocumentIntelligenceMetadataRequest {
  content: string;
  filename?: string;
  study_id?: string | null;
  site_id?: string | null;
}

export interface DocumentIntelligenceReport {
  document_id?: string | null;
  filename: string;
  mime_type: string;
  sha256_hash: string;
  modality?: DocumentModality;
  primary_classification: TaxonomyMatchCandidate;
  alternative_candidates?: TaxonomyMatchCandidate[];
  confidence_tier?: ClassificationConfidenceTier;
  qc_recommendation?: QCRecommendation;
  extracted_metadata?: ExtractedRegulatoryMetadata;
  signature_analysis?: SignatureAnalysisResult;
  ai_generation_manifest?: Record<string, any>;
  eisf_target_mapping?: Record<string, any> | null;
}

export interface DocumentIntelligenceSignatureRequest {
  content: string;
  artifact_code: string;
  filename?: string;
}

export type DocumentModality =
  "TEXT" | "PDF_BINARY" | "SCANNED_IMAGE" | "STRUCTURED_FORM";

export interface DocumentVersionEntry {
  id: string;
  version_index: number;
  status: string;
  approval_status: string;
  created_at: string;
  created_by: string;
  filename: string;
  artifact_code: string;
  signer?: string | null;
  signing_timestamp?: string | null;
  transitions: TransitionResponse[];
}

export interface DocumentVersionsResponse {
  study_id: string;
  artifact_code: string;
  versions: DocumentVersionEntry[];
}

export interface ExpectedDocumentCreate {
  study_id: string;
  site_id?: string | null;
  milestone: string;
  artifact_type: string;
  zone?: number | null;
  section?: string | null;
  metadata_json?: Record<string, any> | null;
  reason_for_change: string;
}

export interface ExpectedDocumentResponse {
  id: string;
  study_id: string;
  site_id?: string | null;
  milestone: string;
  artifact_type: string;
  zone?: number | null;
  section?: string | null;
  metadata_json?: Record<string, any> | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface ExtractedRegulatoryMetadata {
  protocol_number?: string | null;
  study_id?: string | null;
  site_id?: string | null;
  investigator_name?: string | null;
  issue_date?: string | null;
  expiration_date?: string | null;
  form_identifier?: string | null;
  version_tag?: string | null;
  raw_extracted_fields?: Record<string, any>;
  phi_pii_detected?: boolean;
}

export interface ExtractedSignature {
  signer_name?: string | null;
  signer_role?: string | null;
  signature_date?: string | string | null;
  confidence?: number;
  location_hint?: string | null;
  is_digital_signature?: boolean;
  signature_type?: string;
}

export interface IngestionRequest {
  study_id: string;
  site_id?: string | null;
  idempotency_key?: string | null;
  artifact_type: string;
  filename: string;
  content: string;
  mime_type: string;
  zone?: number | null;
  section?: string | null;
  artifact_code?: string | null;
  taxonomy_version?: string | null;
  metadata_json?: Record<string, any> | null;
  protocol_version?: ProtocolVersionRefDTO | null;
  issue_date?: string | null;
  expiration_date?: string | null;
  document_owner_id?: string | null;
  correlation_key?: string | null;
  content_checksum?: string | null;
  source_system?: string | null;
}

export interface InspectionReadinessResponse {
  study_id: string;
  generated_at: string;
  overall_readiness_score: number;
  readiness_rating: string;
  total_documents: number;
  total_expected: number;
  approved_documents_count: number;
  pending_qc_count: number;
  unsigned_documents_count: number;
  expired_documents_count: number;
  expiring_soon_count: number;
  milestones: MilestoneReadinessDetail[];
  zones: ZoneReadinessDetail[];
  action_items: string[];
}

export interface ManualRedactRequest {
  spans?: SpanItem[] | null;
  terms?: string[] | null;
  redacted_filename?: string | null;
}

export interface ManualRedactResponse {
  status?: string;
  document_id: string;
  version_index: number;
  filename: string;
  categories_counts: Record<string, any>;
  manifest: Record<string, any>;
}

export interface MilestoneReadinessDetail {
  milestone: string;
  is_complete: boolean;
  expected_count: number;
  present_count: number;
  approved_count: number;
  missing_artifacts: string[];
  completeness_percentage: number;
}

export interface PaginatedAuditLogResponse {
  items: AuditLogResponse[];
  total_count: number;
  limit: number;
  offset: number;
  next_page?: string | null;
  next_cursor?: string | null;
  has_more: boolean;
}

export interface ProtocolVersionRefDTO {
  study_id: string;
  version_tag: string;
  version_index: number;
  status: ProtocolVersionStatusDTO;
}

export type ProtocolVersionStatusDTO =
  "DRAFT" | "ACTIVE" | "LOCKED" | "PUBLISHED" | "ARCHIVED" | "FROZEN";

export type QCRecommendation =
  "AUTO_CLASSIFY" | "FLAG_FOR_QC_REVIEW" | "MANUAL_RECLASSIFICATION_REQUIRED";

export interface RedactRequest {
  redacted_content: string;
  redacted_filename?: string | null;
  manifest: Record<string, any>;
}

export interface SeedEDLRequest {
  milestones?: string[] | null;
  reason_for_change?: string;
}

export interface SignDocumentRequest {
  signing_reason: SigningReason;
}

export interface SignatureAnalysisResult {
  status?: SignaturePresenceStatus;
  extracted_signatures?: ExtractedSignature[];
  missing_required_signatures?: string[];
  signature_blocks_detected?: number;
  details?: string;
}

export type SignaturePresenceStatus =
  "FULLY_SIGNED" | "PARTIALLY_SIGNED" | "UNSIGNED" | "SIGNATURE_NOT_REQUIRED";

export interface SignatureVerificationResponse {
  document_id: string;
  version_index: number;
  is_valid: boolean;
  signer: string | null;
  signing_timestamp: string | null;
  signing_reason: string | null;
  certificate_fingerprint: string | null;
  content_hash_matched: boolean;
  details: string;
}

export type SigningReason =
  | "AUTHOR"
  | "REVIEW"
  | "APPROVAL"
  | "SPONSOR_APPROVAL"
  | "INVESTIGATOR_SIGNATURE"
  | "TECHNICAL_QC"
  | "CLINICAL_QC"
  | "DATA_LOCK"
  | "SYSTEM_SEAL"
  | "PROTOCOL_APPROVAL"
  | "REGULATORY_FORM_SIGNATURE"
  | "TRAINING_ACKNOWLEDGEMENT"
  | "SITE_VISIT_SIGN_OFF"
  | "ADJUDICATION_SIGN_OFF"
  | "ENDPOINT_ADJUDICATION";

export interface SpanItem {
  start: number;
  end: number;
  label?: string | null;
}

export interface StageDocumentQCRequest {
  content: string;
  filename: string;
  mime_type?: string;
  study_id: string;
  site_id?: string | null;
  artifact_hint?: string | null;
  taxonomy_version?: string | null;
  reason_for_change: string;
  assigned_cra?: string | null;
}

export interface StudyArchiveItemResult {
  document_id: string;
  filename: string;
  from_status: string;
  to_status: string;
  status: string;
  error_message?: string | null;
}

export interface StudyArchiveRequest {
  reason_for_change: string;
  all_or_nothing?: boolean;
}

export interface StudyArchiveResponse {
  status: string;
  study_id: string;
  total_processed: number;
  successful_count: number;
  failed_count: number;
  skipped_count: number;
  results: StudyArchiveItemResult[];
}

export interface TaxonomyArtifactNode {
  artifact_code: string;
  artifact_name: string;
}

export interface TaxonomyCatalogResponse {
  version: string;
  zones: TaxonomyZoneNode[];
}

export interface TaxonomyMatchCandidate {
  zone_code: number;
  zone_name: string;
  section_code: string;
  section_name: string;
  artifact_code: string;
  artifact_name: string;
  confidence: number;
  matched_signals?: string[];
  is_extension?: boolean;
}

export interface TaxonomySectionNode {
  section_code: string;
  section_name: string;
  artifacts: TaxonomyArtifactNode[];
}

export interface TaxonomyZoneNode {
  zone_code: number;
  zone_name: string;
  sections: TaxonomySectionNode[];
}

export interface TransitionRequest {
  to_status: string;
  reason_for_change: string;
}

export interface TransitionResponse {
  id: string;
  document_id: string;
  from_status: string;
  to_status: string;
  actor_id: string;
  actor_role: string;
  reason_for_change: string;
  timestamp: string;
}

export interface ZoneReadinessDetail {
  zone_code: number;
  zone_name: string;
  expected_count: number;
  present_count: number;
  approved_count: number;
  pending_qc_count: number;
  rejected_count: number;
  missing_count: number;
  completeness_percentage: number;
}

export type ActionTakenDevice =
  | "QUARANTINED_AT_SITE"
  | "RETURNED_TO_SPONSOR"
  | "REPLACED"
  | "DISPOSED"
  | "REPAIRED"
  | "NONE";

export interface AdaptiveTrialConfigDTO {
  study_id: string;
  design_type: string;
  dose_levels?: DoseLevelDef[];
  boin_config?: BoinConfig | null;
  crm_config?: CrmConfig | null;
  three_plus_three_config?: ThreePlusThreeConfig | null;
  rar_config?: RarConfig | null;
  cohort_size?: number;
  max_sample_size?: number | null;
  is_active?: boolean;
}

export interface AddDOAAssignmentRequest {
  study_id: string;
  site_id: string;
  personnel_name: string;
  personnel_email: string;
  role: DOATaskRoleEnum;
  delegated_tasks: DOATaskDelegationEnum[];
  start_date: string;
}

export type AdjudicationActionEnum = "APPROVE" | "REJECT";

export type AdjudicationClassification =
  | "CONFIRMED_MI"
  | "CONFIRMED_ISCHEMIC_STROKE"
  | "CONFIRMED_HEMORRHAGIC_STROKE"
  | "CONFIRMED_CV_DEATH"
  | "CONFIRMED_NON_CV_DEATH"
  | "CONFIRMED_HEART_FAILURE_HOSPITALIZATION"
  | "CONFIRMED_STENT_THROMBOSIS"
  | "CONFIRMED_VTE"
  | "CONFIRMED_DISEASE_PROGRESSION"
  | "NOT_AN_ENDPOINT"
  | "INDETERMINATE"
  | "INSUFFICIENT_DATA";

export interface AdjudicationRequest {
  action: AdjudicationActionEnum;
  reason: string;
  updated_message?: string | null;
}

export interface AdjudicationResponse {
  query_id: string;
  new_status: string;
  action: string;
  message: string;
  adjudicated_by: string;
  adjudicated_at: string;
}

export type AdjudicationWorkflowStatus =
  | "PENDING_PRIMARY_REVIEWS"
  | "READER_1_COMPLETED"
  | "READER_2_COMPLETED"
  | "CONSENSUS_REACHED"
  | "DISCORDANCE_DETECTED"
  | "ESCALATED_TO_CHAIR"
  | "FINALIZED_CONCORDANT"
  | "FINALIZED_BY_CHAIR"
  | "FINALIZED_BY_COMMITTEE";

export interface AnomalyCandidateItem {
  query_id: string;
  study_id: string;
  subject_id: string;
  site_id?: string | null;
  visit_id?: string | null;
  domain?: string | null;
  test_code: string;
  observation_id?: string | null;
  field_link?: string | null;
  rule_id?: string | null;
  message?: string | null;
  explanation?: string | null;
  origin?: string | null;
  priority?: string | null;
  status?: string;
  created_at: string;
  created_by?: string | null;
}

export interface AnomalyEvaluateRequest {
  subject_id: string;
  study_id: string;
  enable_ai?: boolean;
  auto_stage_queries?: boolean;
}

export interface AnomalyEvaluateResponse {
  subject_id: string;
  study_id: string;
  anomalies: CrossDomainAnomaly[];
  evaluated_at: string;
  queries_staged_count: number;
}

export type AnomalySeverity = "HIGH" | "MEDIUM" | "LOW";

export interface ApproveDelegationRequest {
  delegation_id: string;
  pi_user_id: string;
  password: string;
  totp_code?: string | null;
}

export interface ApproveTaskDelegationRequest {
  delegation_id: string;
  pi_user_id: string;
  signature_hash: string;
  reason_for_change: string;
}

export interface BatchAssignItem {
  assignment_id: string;
  action?: string;
  code?: string | null;
  term?: string | null;
  suggestion_index?: number | null;
  reason_for_change?: string | null;
}

export interface BatchAssignRequest {
  assignment_ids?: string[] | null;
  items?: BatchAssignItem[] | null;
  code?: string | null;
  term?: string | null;
  dictionary_type?: string | null;
  dictionary_version?: string | null;
  reason?: string | null;
  reason_for_change?: string | null;
  action?: string;
}

export interface BatchAssignResponse {
  success_count: number;
  failed_count: number;
  results?: Record<string, any>[];
}

export interface BatchSignOffRequest {
  study_id: string;
  target_type: string;
  target_ids: string[];
  signing_reason: string;
}

export interface BatchSignOffResponse {
  status: string;
  approved_submission_ids: string[];
  skipped_submission_ids: string[];
  skipped_targets: string[];
}

export interface BatchSignatureRequest {
  study_id: string;
  subject_id: string;
  target_type?: string;
  target_ids?: string[];
  target_form_ids?: string[];
  signing_reason: string;
  password: string;
  printed_name: string;
}

export interface BatchSignatureResponse {
  signature_id: string;
  study_id: string;
  subject_id: string;
  signed_forms_count: number;
  content_digest: string;
  timestamp_utc: string;
  audit_tx: string;
}

export type BindingDeterminationSource =
  | "CONCORDANT_CONSENSUS"
  | "CHAIR_UPHELD_READER_1"
  | "CHAIR_UPHELD_READER_2"
  | "CHAIR_CUSTOM_DETERMINATION"
  | "COMMITTEE_QUORUM_VOTE";

export interface Body_import_dictionary_api_v1_dictionaries_import_post {
  dictionary_type: DictTypeEnum;
  version: string;
  files: string;
  parse_multilingual?: boolean;
}

export interface Body_upload_document_api_v1_documents_upload_post {
  file: string;
  dia_tmf_code: string;
  reason_for_change: string;
}

export interface BoinBoundariesCalculationRequest {
  target_toxicity_rate?: number;
  factor_e?: number;
  factor_d?: number;
}

export interface BoinBoundariesCalculationResponse {
  target_toxicity_rate: number;
  phi_1: number;
  phi_2: number;
  lambda_e: number;
  lambda_d: number;
  description: string;
}

export interface BoinConfig {
  target_toxicity_rate?: number;
  factor_e?: number;
  factor_d?: number;
  safety_cutoff?: number;
  prior_alpha?: number;
  prior_beta?: number;
  min_subjects_for_elimination?: number;
}

export interface BulkQueryGenerationRequest {
  study_id?: string | null;
  site_id?: string | null;
  subject_id?: string | null;
  targets: QueryTargetDescriptor[];
  reason_for_change: string;
}

export interface BulkQueryGenerationResponse {
  batch_id?: string | null;
  audit_tx?: string | null;
  generated_count?: number | null;
  generated_query_ids: string[];
  skipped_targets?: QueryTargetDescriptor[];
  timestamp_utc?: string | null;
}

export interface BulkSdvSignOffRequest {
  study_id: string;
  subject_id: string;
  scope: string;
  target_ids: string[];
  reason_for_change: string;
  site_id?: string | null;
  signing_reason?: string;
}

export interface BulkSdvSignOffResponse {
  bulk_id?: string | null;
  content_digest: string;
  timestamp_utc: string;
  audit_tx: string;
  verified_count?: number | null;
  verified_target_ids?: string[] | null;
  skipped_targets?: Record<string, any>[] | null;
  signed_count: number;
  signed_target_ids: string[];
  skipped_target_ids: string[];
}

export interface BulkSubjectReconsentRequest {
  subject_ids: string[];
  study_id: string;
  protocol_version: string;
  version_index?: number;
  icf_signed?: boolean;
  signature_type?: string;
  signer_name?: string;
  reason_for_change?: string;
}

export interface BundledEcrfForm {
  form_id: string;
  form_name: string;
  visit_name: string;
  submission_timestamp: string;
  form_data?: Record<string, any>;
}

export interface CGTApheresisCreateRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  collector_id: string;
  collector_name: string;
  collector_role?: string;
  initial_temp_celsius?: number;
  din?: string | null;
  dry_shipper_id?: string | null;
  seal_id?: string | null;
  notes?: string | null;
}

export interface CGTBarcodeVerifyRequest {
  barcode_payload: string;
  expected_subject_id?: string | null;
  expected_din?: string | null;
  expected_batch_id?: string | null;
}

export interface CGTBarcodeVerifyResponse {
  is_valid: boolean;
  failure_reason?: string | null;
  barcode_id?: string | null;
  study_id?: string | null;
  site_id?: string | null;
  subject_id?: string | null;
  din?: string | null;
  batch_id?: string | null;
  crypto_hash?: string | null;
}

export interface CGTBedsideVerificationRequest {
  coc_id: string;
  scanned_wristband_subject_id: string;
  scanned_bag_barcode: string;
  primary_verifier_id: string;
  primary_verifier_name: string;
  primary_verifier_role: string;
  primary_signature_token: string;
  secondary_verifier_id: string;
  secondary_verifier_name: string;
  secondary_verifier_role: string;
  secondary_signature_token: string;
  notes?: string | null;
}

export interface CGTBedsideVerificationResponse {
  id: string;
  study_id: string;
  site_id: string;
  chain_of_custody_id: string;
  subject_id: string;
  din: string;
  batch_id: string;
  wristband_matched: boolean;
  bag_matched: boolean;
  temperature_chain_verified: boolean;
  primary_verifier_id: string;
  primary_verifier_name: string;
  primary_verifier_role: string;
  primary_signed_at: string;
  secondary_verifier_id: string;
  secondary_verifier_name: string;
  secondary_verifier_role: string;
  secondary_signed_at: string;
  status: string;
  unlock_token: string;
  verification_notes?: string | null;
}

export interface CGTChainOfCustodyResponse {
  id: string;
  study_id: string;
  subject_id: string;
  din: string;
  batch_id?: string | null;
  current_stage: string;
  status: string;
  tamper_evident_barcode?: string | null;
  barcode_signature?: string | null;
  collection_site_id?: string | null;
  collection_timestamp?: string | null;
  manufacturing_facility_id?: string | null;
  cryo_temp_excursion_count?: number;
  lowest_temp_celsius?: number | null;
  highest_temp_celsius?: number | null;
  infusion_unlocked?: boolean;
  infused_at?: string | null;
  reconciled_at?: string | null;
}

export interface CGTDestructionCertificateCreateRequest {
  study_id: string;
  site_id: string;
  lot_number: string;
  kit_numbers: string[];
  vial_identifiers: string[];
  destruction_reason: string;
  destruction_method?: string;
  destruction_facility: string;
  witness_1_id: string;
  witness_1_name: string;
  witness_1_role: string;
  witness_1_signature_token: string;
  witness_2_id: string;
  witness_2_name: string;
  witness_2_role: string;
  witness_2_signature_token: string;
  quantity_destroyed?: number;
  residual_volume_ml?: number | null;
  subject_id?: string | null;
  din?: string | null;
  coc_id?: string | null;
  notes?: string | null;
}

export interface CGTDestructionCertificateResponse {
  id: string;
  study_id: string;
  site_id: string;
  certificate_number: string;
  chain_of_custody_id?: string | null;
  subject_id?: string | null;
  din?: string | null;
  lot_number: string;
  kit_numbers: string[];
  vial_identifiers: string[];
  quantity_destroyed: number;
  unit_of_measure: string;
  residual_volume_ml?: number | null;
  destruction_reason: string;
  destruction_method: string;
  destruction_facility: string;
  destruction_timestamp: string;
  witness_1_id: string;
  witness_1_name: string;
  witness_1_role: string;
  witness_1_signed_at: string;
  witness_2_id: string;
  witness_2_name: string;
  witness_2_role: string;
  witness_2_signed_at: string;
  cryptographic_seal: string;
  status: string;
  reconciliation_notes?: string | null;
}

export interface CGTFullTraceabilityResponse {
  chain_of_custody: CGTChainOfCustodyResponse;
  milestones: CGTMilestoneResponse[];
  bedside_verification?: CGTBedsideVerificationResponse | null;
  destruction_certificate?: CGTDestructionCertificateResponse | null;
}

export interface CGTInfusionExecuteRequest {
  coc_id: string;
  unlock_token: string;
  administrator_id: string;
  administrator_name: string;
  administrator_role?: string;
  volume_infused_ml?: number;
  vital_signs_pre_temp_celsius?: number;
  vital_signs_post_temp_celsius?: number;
  notes?: string | null;
}

export interface CGTManufacturingBindRequest {
  coc_id: string;
  batch_id: string;
  manufacturing_facility_id: string;
  operator_id: string;
  operator_name: string;
  operator_role?: string;
  temperature_celsius?: number;
  notes?: string | null;
}

export interface CGTMilestoneCreateRequest {
  coc_id: string;
  stage: string;
  location_name: string;
  temperature_celsius: number;
  custodian_id: string;
  custodian_name: string;
  custodian_role: string;
  facility_id?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  carrier?: string | null;
  tracking_number?: string | null;
  seal_intact?: boolean;
  seal_id?: string | null;
  dry_shipper_id?: string | null;
  signature_token?: string | null;
  notes?: string | null;
}

export interface CGTMilestoneResponse {
  id: string;
  study_id: string;
  chain_of_custody_id: string;
  stage: string;
  milestone_type: string;
  timestamp: string;
  location_name: string;
  facility_id?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  temperature_celsius: number;
  temperature_status: string;
  custodian_id: string;
  custodian_name: string;
  custodian_role: string;
  carrier?: string | null;
  tracking_number?: string | null;
  seal_intact: boolean;
  seal_id?: string | null;
  dry_shipper_id?: string | null;
  electronic_signature?: Record<string, any> | null;
  notes?: string | null;
}

export interface CGTVialReturnReconciliationRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  kit_number: string;
  vial_identifier: string;
  lot_number: string;
  expected_quantity?: number;
  returned_quantity?: number;
  intact_vials?: number;
  used_vials?: number;
  residual_volume_ml?: number;
  din?: string | null;
  logged_by: string;
  disposition?: string;
}

export interface CGTVialReturnReconciliationResponse {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  kit_number: string;
  vial_identifier: string;
  lot_number: string;
  din?: string | null;
  expected_quantity: number;
  returned_quantity: number;
  intact_vials: number;
  used_vials: number;
  residual_volume_ml: number;
  adherence_percentage: number;
  disposition: string;
  logged_by: string;
  logged_at: string;
  certificate_id?: string | null;
}

export interface CecCharterConfig {
  charter_id: string;
  study_id: string;
  committee_name: string;
  version?: string;
  voting_rule?: VotingRule;
  minimum_quorum_percentage?: number;
  minimum_members_count?: number;
  eligible_members?: Record<string, any>[];
  endpoint_definitions?: string[];
  diagnostic_criteria_templates?: Record<string, any>;
  is_active?: boolean;
  created_at?: string;
  created_by?: string;
  version_index?: number;
}

export interface CecDossierSummaryItem {
  dossier_id: string;
  event_id: string;
  study_id: string;
  masked_subject_id: string;
  candidate_event_type: EndpointCategory;
  reported_term: string;
  reported_onset_date: string;
  workflow_status: AdjudicationWorkflowStatus;
  is_discordant: boolean;
  final_consensus_classification?: AdjudicationClassification | null;
  reader_1_status: string;
  reader_2_status: string;
  created_at: string;
}

export type CertificationStatusEnum =
  "ACTIVE" | "EXPIRED" | "SUSPENDED" | "PENDING_RENEWAL";

export interface ChairAdjudicateDTO {
  final_classification: AdjudicationClassification;
  final_subtype?: string | null;
  final_adjudicated_onset_date: string;
  binding_reader?: BindingDeterminationSource;
  criteria_confirmed?: string[];
  adjudication_rationale: string;
  signature_token?: string | null;
  signer_name?: string;
  password?: string;
  signature_meaning?: string;
}

export interface ChairAdjudicationDetermination {
  determination_id: string;
  dossier_id: string;
  chair_id: string;
  chair_name: string;
  final_classification: AdjudicationClassification;
  final_subtype?: string | null;
  final_adjudicated_onset_date: string;
  binding_reader?: BindingDeterminationSource;
  criteria_confirmed?: string[];
  adjudication_rationale: string;
  electronic_signature: Record<string, any>;
  finalized_at?: string;
}

export interface CharterCreateDTO {
  study_id: string;
  committee_name: string;
  version?: string;
  voting_rule?: VotingRule;
  minimum_quorum_percentage?: number;
  minimum_members_count?: number;
  eligible_members?: Record<string, any>[];
  endpoint_definitions?: string[];
  diagnostic_criteria_templates?: Record<string, any>;
}

export interface ClinicalQueryResponse {
  id: string;
  study_id: string;
  subject_id: string;
  visit_id?: string | null;
  domain?: string | null;
  test_code: string;
  status: string;
  explanation?: string | null;
  response?: string | null;
  created_at: string;
  updated_at: string;
  history?: QueryHistoryItem[];
  observation_id?: string | null;
  field_link?: string | null;
  message?: string | null;
  origin?: string | null;
  priority?: string | null;
  rule_id?: string | null;
  created_by?: string | null;
  responder?: string | null;
  resolver?: string | null;
  resolved_at?: string | null;
  cancellation_reason?: string | null;
  escalated_at?: string | null;
  form_id?: string | null;
  field_id?: string | null;
  query_type?: string | null;
  action_required?: string | null;
}

export interface CoderActionRequest {
  action: string;
  code?: string | null;
  term?: string | null;
  suggestion_index?: number | null;
  reason_for_change?: string | null;
}

export interface CodingAssignmentResponse {
  id: string;
  verbatim_text: string;
  source_field?: string | null;
  observation_id?: string | null;
  dictionary_type: string;
  dictionary_version: string;
  coded_code?: string | null;
  coded_term?: string | null;
  status: string;
  recoding_status: string;
  assigned_by?: string | null;
  assigned_at: string;
  score?: number | null;
  hierarchy?: Record<string, any> | any[] | null;
  suggestions?: any[] | Record<string, any> | null;
  domain?: string | null;
  version: number;
  is_deleted: boolean;
}

export interface CohortEvaluationInput {
  study_id: string;
  cohort_number: number;
  dose_level_index: number;
  subjects_evaluated: number;
  dlts_observed: number;
  subject_ids?: string[];
  dlt_subject_ids?: string[];
}

export interface CohortEvaluationResult {
  study_id: string;
  cohort_number: number;
  evaluated_dose_level_index: number;
  evaluated_dose_name: string;
  subjects_in_cohort: number;
  dlts_in_cohort: number;
  recommendation_action: string;
  recommended_dose_level_index?: number | null;
  recommended_dose_name?: string | null;
  lambda_e?: number | null;
  lambda_d?: number | null;
  target_toxicity_rate?: number | null;
  overdose_probability?: number | null;
  is_safety_stop_triggered?: boolean;
  is_mtd_declared?: boolean;
  mtd_dose_level_index?: number | null;
  mtd_dose_name?: string | null;
  dose_summaries?: DoseLevelEvaluationSummary[];
  rationale: string;
}

export type CohortRecommendationAction =
  | "ESCALATE"
  | "DE_ESCALATE"
  | "STAY"
  | "EXPAND_COHORT"
  | "STOP_SAFETY"
  | "STOP_SUCCESS"
  | "MTD_FOUND";

export interface CohortTransitionLogResponse {
  id: string;
  study_id: string;
  cohort_number: number;
  from_dose_level: number;
  to_dose_level: number;
  action_taken: string;
  algorithm_recommendation: string;
  override_occurred: boolean;
  override_reason?: string | null;
  evaluated_dlt_rate?: number | null;
  posterior_toxicity_estimates?: Record<string, any> | null;
  authorized_by: string;
  transitioned_at: string;
  signature_token?: string | null;
  cryptographic_checksum?: string | null;
}

export interface CohortTransitionRequest {
  study_id: string;
  cohort_number: number;
  decision_action: string;
  target_dose_level_index: number;
  override_reason?: string | null;
  electronic_signature?: string | null;
}

export interface ComplianceStatusResponse {
  study_id: string;
  site_id: string | null;
  milestone: string;
  is_complete: boolean;
  missing_documents: string[];
}

export interface CriterionLevelResult {
  criterion_id: string;
  criterion_type: string;
  description: string;
  dsl_source: string;
  is_met: boolean;
  is_indeterminate: boolean;
}

export interface CrmConfig {
  target_toxicity_rate?: number;
  skeleton_probs: number[];
  model_type?: string;
  prior_std?: number;
  safety_cutoff?: number;
  allow_dose_skipping?: boolean;
}

export interface CrmPosteriorCalculationRequest {
  skeleton_probs: number[];
  target_toxicity_rate?: number;
  prior_std?: number;
  dose_evaluations: Record<string, any>[];
  current_dose_index?: number;
}

export interface CrmPosteriorCalculationResponse {
  target_toxicity_rate: number;
  current_dose_index: number;
  recommended_dose_index: number;
  recommendation_action: CohortRecommendationAction;
  posterior_mean_probabilities: number[];
  overdose_probability_dose_1: number;
  is_safety_stop_triggered: boolean;
  rationale: string;
}

export interface CrossDomainAnomaly {
  anomaly_id?: string;
  anomaly_type: CrossDomainAnomalyType;
  study_id: string;
  subject_id: string;
  site_id?: string | null;
  visit_id?: string | null;
  primary_domain: string;
  primary_test_code: string;
  correlated_domain: string;
  correlated_test_code?: string | null;
  severity?: AnomalySeverity;
  message: string;
  explanation: string;
  confidence_score?: number;
  model_identifier?: string | null;
  prompt_hash?: string | null;
  observation_ids?: string[];
  form_id?: string | null;
  field_id?: string | null;
}

export type CrossDomainAnomalyType =
  | "AE_WITHOUT_CONCOMITANT_MED"
  | "CONCOMITANT_MED_WITHOUT_AE"
  | "MARKED_LAB_ABNORMALITY_WITHOUT_AE"
  | "CRITICAL_VITALS_WITHOUT_AE"
  | "VITALS_INCONSISTENT_WITH_HISTORY"
  | "DRUG_DISCONTINUATION_WITHOUT_AE"
  | "TEMPORAL_SEQUENCE_MISMATCH"
  | "AI_CONTEXTUAL_INCONSISTENCY";

export type CustodianEnum = "Lead Unblinded Statistician" | "IDMC";

export interface CustodianShare {
  custodian: CustodianEnum;
  version: number;
  x: number;
  y: number;
}

export interface DOAAssignmentRecord {
  record_id: string;
  study_id: string;
  site_id: string;
  personnel_name: string;
  personnel_email: string;
  role: DOATaskRoleEnum;
  delegated_tasks: DOATaskDelegationEnum[];
  start_date: string;
  end_date?: string | null;
  is_active?: boolean;
  signed_off?: boolean;
}

export interface DOAAuditLogResponse {
  id: string;
  user_id: string;
  action: string;
  details: string;
  timestamp: string;
}

export interface DOADelegationRecordResponse {
  id: string;
  site_id: string;
  staff_user_id: string;
  task_code: string;
  pi_user_id?: string | null;
  status: string;
  pi_signature_hash?: string | null;
  pi_approved_at?: string | null;
  end_date?: string | null;
  reason_for_change?: string | null;
  is_active: boolean;
}

export type DOATaskDelegationEnum =
  | "SUBJECT_INFORMED_CONSENT"
  | "PHYSICAL_EXAMINATION"
  | "AE_SAE_REPORTING"
  | "CRF_DATA_ENTRY"
  | "PI_CASEBOOK_SIGNOFF";

export type DOATaskRoleEnum =
  | "PRINCIPAL_INVESTIGATOR"
  | "SUB_INVESTIGATOR"
  | "CLINICAL_RESEARCH_COORDINATOR"
  | "STUDY_NURSE"
  | "DATA_MANAGER";

export interface DailyDemandProjection {
  day_index: number;
  date: string;
  scheduled_dispensations: number;
  projected_new_subject_dispensations: number;
  total_daily_demand: number;
  cumulative_demand: number;
  projected_closing_balance: number;
}

export interface DataLockRecord {
  lock_id: string;
  study_id?: string | null;
  site_id?: string | null;
  subject_id?: string | null;
  visit_id?: string | null;
  form_id?: string | null;
  item_group_id?: string | null;
  field_name?: string | null;
  scope?: LockScopeEnum | string;
  scope_type?: string | null;
  scope_id?: string | null;
  status?: LockStatusEnum | string;
  lock_type?: string | null;
  is_active?: boolean;
  locked_by: string;
  created_by?: string | null;
  reason_for_change: string;
  locked_at: string;
  created_at?: string | null;
  unlocked_by?: string | null;
  unlocked_at?: string | null;
  unlock_justification?: string | null;
  signature_token?: string | null;
}

export interface DataLockRequest {
  study_id?: string | null;
  site_id?: string | null;
  subject_id?: string | null;
  visit_id?: string | null;
  form_id?: string | null;
  item_group_id?: string | null;
  field_name?: string | null;
  scope?: LockScopeEnum | string | null;
  scope_type?: string | null;
  scope_id?: string | null;
  action?: string;
  lock_type?: string | null;
  reason_for_change?: string | null;
  reason?: string | null;
  justification?: string | null;
  lock_id?: string | null;
}

export interface DataLockResponse {
  lock_id: string;
  status: string;
  message: string;
  record?: DataLockRecord | Record<string, any> | null;
  scope_type?: string | null;
  scope_id?: string | null;
  lock_type?: string | null;
  is_active?: boolean;
  locked_at?: string | null;
  unlocked_at?: string | null;
}

export interface DelegateTaskRequest {
  site_id: string;
  staff_user_id: string;
  task_code: string;
  pi_user_id: string;
  reason_for_change: string;
}

export interface Demographics {
  name?: string | null;
  birthdate?: string | null;
  gender?: string | null;
  race?: string | null;
}

export type DeviceClassification =
  | "CLASS_I"
  | "CLASS_IIA"
  | "CLASS_IIB"
  | "CLASS_III"
  | "IVD_CLASS_A"
  | "IVD_CLASS_B"
  | "IVD_CLASS_C"
  | "IVD_CLASS_D";

export interface DeviceDeficiencyCreateRequest {
  study_id: string;
  site_id: string;
  subject_id?: string | null;
  device_inventory_id?: string | null;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  device_name?: string;
  deficiency_type?: DeviceDeficiencyType;
  deficiency_description: string;
  incident_date: string;
  awareness_date?: string;
  could_lead_to_sade?: boolean;
  patient_involved?: boolean;
  health_impact?: HealthImpact;
  action_taken_device?: ActionTakenDevice;
  device_returned_to_sponsor?: boolean;
  return_tracking_number?: string | null;
  quarantine_scope?: QuarantineScope;
  reason_for_change?: string;
  signature_token?: string | null;
}

export interface DeviceDeficiencyResponse {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string | null;
  device_inventory_id: string | null;
  udi_di: string;
  lot_number: string;
  serial_number: string | null;
  device_name: string;
  deficiency_type: string;
  deficiency_description: string;
  incident_date: string;
  awareness_date: string;
  could_lead_to_sade: boolean;
  patient_involved: boolean;
  health_impact: string;
  action_taken_device: string;
  device_returned_to_sponsor: boolean;
  return_tracking_number: string | null;
  root_cause_analysis: string | null;
  sponsor_evaluation: string | null;
  quarantine_triggered: boolean;
  quarantine_scope: string;
  safety_case_id: string | null;
  safety_classification: string | null;
  investigation_status: string;
  quarantine_record_ids?: string[];
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type DeviceDeficiencyType =
  | "MALFUNCTION"
  | "USABILITY_ERROR"
  | "INADEQUATE_LABELING"
  | "DESIGN_DEFECT"
  | "PACKAGING_BREACH"
  | "SOFTWARE_FAULT";

export interface DeviceDispensationRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  device_inventory_id?: string | null;
  serial_number?: string | null;
  scanned_barcode?: string | null;
  reason_for_change?: string;
}

export interface DeviceDispensationResponse {
  status?: string;
  message: string;
  device: DeviceInventoryResponse;
  dispensed_at: string;
}

export interface DeviceInventoryCreateRequest {
  study_id: string;
  site_id: string;
  device_identifier?: string | null;
  brand_name: string;
  device_model?: string | null;
  device_type?: DeviceType;
  device_classification?: DeviceClassification;
  raw_barcode?: string | null;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  expiration_date?: string | null;
  manufacturing_date?: string | null;
  software_version?: string | null;
  quantity?: number;
  reason_for_change?: string;
}

export interface DeviceInventoryResponse {
  id: string;
  study_id: string;
  site_id: string;
  device_identifier: string;
  brand_name: string;
  device_model: string | null;
  device_type: string;
  device_classification: string;
  udi_di: string;
  udi_pi: string | null;
  udi_carrier_format: string;
  lot_number: string;
  serial_number: string | null;
  expiration_date: string | null;
  manufacturing_date: string | null;
  software_version: string | null;
  status: string;
  is_dispensation_blocked: boolean;
  allocated_subject_id: string | null;
  dispensed_at: string | null;
  dispensed_by: string | null;
  quarantined_at: string | null;
  quarantined_by: string | null;
  quarantine_reason: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface DeviceInvestigationSummary {
  study_id: string;
  total_devices?: number;
  available_devices?: number;
  dispensed_devices?: number;
  quarantined_devices?: number;
  total_deficiencies?: number;
  open_safety_cases?: number;
  sade_count?: number;
  active_quarantines?: number;
}

export interface DeviceQuarantineCreateRequest {
  study_id: string;
  site_id: string;
  scope?: QuarantineScope;
  device_inventory_id?: string | null;
  lot_number?: string | null;
  serial_number?: string | null;
  reason: string;
  reason_for_change?: string;
}

export interface DeviceQuarantineReleaseRequest {
  quarantine_id: string;
  release_justification: string;
  signature_token: string;
  reason_for_change?: string;
}

export interface DeviceQuarantineResponse {
  id: string;
  study_id: string;
  site_id: string;
  deficiency_id: string | null;
  device_inventory_id: string | null;
  lot_number: string;
  serial_number: string | null;
  scope: string;
  status: string;
  reason: string;
  quarantined_at: string;
  quarantined_by: string;
  released_at: string | null;
  released_by: string | null;
  release_justification: string | null;
  impacted_device_count?: number;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type DeviceType =
  "THERAPEUTIC" | "DIAGNOSTIC_IVD" | "IMPLANTABLE" | "SAMD" | "COMBINATION";

export interface DiagnosticAttachment {
  attachment_id: string;
  document_type: string;
  title: string;
  summary: string;
  key_findings?: string[];
  file_url?: string | null;
  content_hash?: string | null;
}

export type DictTypeEnum = "MEDDRA" | "WHODRUG" | "LOINC" | "SNOMED";

export interface DiscreteExcursionResponse {
  start_time: string;
  end_time: string;
  duration_hours: number;
  excursion_type: string;
  peak_or_nadir_temp_celsius: number;
  threshold_celsius: number;
}

export interface DispenseRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_id: string;
  kit_id: string;
  quantity?: number;
}

export interface DispenseResponse {
  status: string;
  message: string;
  resupply_triggered: boolean;
}

export interface DocumentMetadataResponse {
  document_id: string;
  filename: string;
  version_index: string;
  sha256_hash: string;
  dia_tmf_code: string;
  status: string;
  created_by: string;
  created_at: string;
}

export interface DocumentUploadResponse {
  document_id: string;
  filename: string;
  version_index: string;
  sha256_hash: string;
}

export interface DoseLevelDef {
  index: number;
  name: string;
  dose_value: number;
}

export interface DoseLevelEvaluationSummary {
  dose_level_index: number;
  dose_name: string;
  dose_value: number;
  total_enrolled: number;
  total_dlts: number;
  observed_dlt_rate: number;
  posterior_dlt_rate?: number | null;
  is_eliminated_for_safety?: boolean;
  overdose_probability?: number | null;
}

export interface DossierBlindedView {
  dossier_id: string;
  event_id: string;
  study_id: string;
  masked_site_id: string;
  masked_subject_id: string;
  candidate_event_type: EndpointCategory;
  reported_term: string;
  verbatim_description: string;
  reported_onset_date: string;
  medical_narrative: string;
  is_deidentified: boolean;
  bundled_forms: BundledEcrfForm[];
  lab_panels: LabPanelItem[];
  ecg_records: ECGRecord[];
  diagnostic_attachments: DiagnosticAttachment[];
  reader_role: ReaderRole;
  workflow_status: AdjudicationWorkflowStatus;
  my_assessment?: ReaderAssessment | null;
  peer_assessment_completed?: boolean;
  peer_assessment?: ReaderAssessment | null;
  chair_determination?: ChairAdjudicationDetermination | null;
  is_discordant?: boolean;
  discordance_reasons?: string[];
  final_consensus_classification?: AdjudicationClassification | null;
  final_adjudicated_onset_date?: string | null;
  dossier_digest?: string;
}

export interface DossierDetailResponseDTO {
  dossier_id: string;
  event_id: string;
  study_id: string;
  site_id: string;
  masked_site_id: string;
  subject_id: string;
  masked_subject_id: string;
  candidate_event_type: string;
  reported_term: string;
  verbatim_description: string;
  reported_onset_date: string;
  medical_narrative: string;
  is_deidentified: boolean;
  bundled_forms: Record<string, any>[];
  lab_panels: Record<string, any>[];
  ecg_records: Record<string, any>[];
  diagnostic_attachments: Record<string, any>[];
  redaction_summary: Record<string, any>;
  reader_1_id: string;
  reader_1_name: string;
  reader_1_status: string;
  reader_1_assessment: Record<string, any> | null;
  reader_2_id: string;
  reader_2_name: string;
  reader_2_status: string;
  reader_2_assessment: Record<string, any> | null;
  chair_id: string | null;
  chair_name: string | null;
  chair_determination: Record<string, any> | null;
  workflow_status: string;
  is_discordant: boolean;
  discordance_reasons: string[];
  final_consensus_classification: string | null;
  final_consensus_subtype: string | null;
  final_adjudicated_onset_date: string | null;
  binding_determination_source: string | null;
  adjudication_rationale: string | null;
  electronic_signature: Record<string, any> | null;
  edc_writeback_status: string;
  dossier_digest: string;
  created_at: string;
  version_index: number;
}

export interface DossierPackageCreateDTO {
  study_id: string;
  site_id: string;
  subject_id: string;
  event_id: string;
  candidate_event_type: EndpointCategory;
  reported_term: string;
  verbatim_description?: string;
  reported_onset_date: string;
  medical_narrative?: string;
  bundled_forms?: BundledEcrfForm[];
  lab_panels?: LabPanelItem[];
  ecg_records?: ECGRecord[];
  diagnostic_attachments?: DiagnosticAttachment[];
  reader_1_id?: string;
  reader_1_name?: string;
  reader_2_id?: string;
  reader_2_name?: string;
  chair_id?: string;
  chair_name?: string;
}

export interface ECGRecord {
  ecg_id: string;
  timestamp: string;
  rhythm?: string;
  st_elevation_leads?: string[];
  st_depression_leads?: string[];
  t_wave_inversion_leads?: string[];
  q_wave_leads?: string[];
  lbbb_present?: boolean;
  qtcf_ms?: number | null;
  interpretation?: string | null;
}

export interface EISFDocumentRecord {
  document_id: string;
  study_id: string;
  site_id: string;
  category: EISFTaxonomyCategoryEnum;
  title: string;
  version?: string;
  file_name: string;
  file_size_bytes: number;
  sha256_hash: string;
  uploaded_by: string;
  uploaded_at: string;
  expiration_date?: string | null;
  is_redacted?: boolean;
}

export type EISFTaxonomyCategoryEnum =
  | "1_INVESTIGATOR_CV"
  | "2_MEDICAL_LICENSE"
  | "3_PROTOCOL_APPROVAL"
  | "4_IRB_IEC_APPROVAL"
  | "5_INFORMED_CONSENT"
  | "6_FINANCIAL_DISCLOSURE"
  | "7_DELEGATION_OF_AUTHORITY"
  | "8_SAFETY_REPORT";

export interface ETMFCompletenessWebhookPayload {
  study_id: string;
  site_id?: string | null;
  milestone: string;
  is_complete: boolean;
  missing_artifacts?: string[];
}

export interface EdcCasebookWritebackResult {
  dossier_id: string;
  subject_id: string;
  study_id: string;
  event_id: string;
  observation_ids_updated?: string[];
  adjudication_status?: string;
  adjudicated_term: string;
  adjudication_date: string;
  signature_id?: string | null;
  is_locked?: boolean;
  written_at?: string;
  status?: string;
}

export type EndpointCategory =
  | "MACE"
  | "MYOCARDIAL_INFARCTION"
  | "STROKE"
  | "CARDIOVASCULAR_MORTALITY"
  | "ALL_CAUSE_MORTALITY"
  | "HEART_FAILURE_HOSPITALIZATION"
  | "STENT_THROMBOSIS"
  | "VENOUS_THROMBOEMBOLISM"
  | "DISEASE_PROGRESSION"
  | "OTHER";

export interface ExcursionEvaluationResponse {
  id?: string | null;
  study_id: string;
  logger_id: string;
  entity_type: string;
  entity_id: string;
  evaluation_status: string;
  mean_kinetic_temperature_celsius: number;
  arithmetic_mean_celsius: number;
  min_temperature_celsius: number;
  max_temperature_celsius: number;
  cumulative_hours_above_upper: number;
  cumulative_hours_below_lower: number;
  total_duration_hours: number;
  readings_count: number;
  hard_boundary_breached: boolean;
  mkt_breached: boolean;
  cumulative_time_breached: boolean;
  quarantine_triggered: boolean;
  quarantined_kit_count: number;
  excursions?: DiscreteExcursionResponse[];
  evaluated_at: string;
  evaluated_by: string;
  summary_notes?: string | null;
}

export interface ExportBundleRequest {
  study_id: string;
  format?: string;
  domains?: string[];
  datasets?: string[];
  site_ids?: string[];
  cohorts?: string[];
  privacy_profile?: string;
  xpt_version?: string;
  include_audit_trail?: boolean;
  metadata_version_oid?: string;
  salt?: string | null;
}

export interface FormSubmissionApprove {
  signing_reason: string;
  signature_manifest: Record<string, any>;
}

export interface FormSubmissionCreate {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_id?: string | null;
  form_id: string;
  protocol_version?: string | null;
  payload?: Record<string, any> | null;
}

export interface FormSubmissionResponse {
  id: string;
  study_id: string;
  site_id: string | null;
  subject_id: string;
  visit_id?: string | null;
  form_id: string;
  status: FormSubmissionStatusEnum;
  version: number;
  is_deleted: boolean;
  signature_manifest?: Record<string, any> | null;
  protocol_version?: string | null;
  payload?: Record<string, any> | null;
  is_active: boolean;
  is_readonly: boolean;
  cloned_from_id?: string | null;
}

export type FormSubmissionStatusEnum = "DRAFT" | "COMPLETED" | "APPROVED";

export interface GUDIDDeviceRecord {
  primary_di: string;
  brand_name: string;
  version_model_number: string;
  device_description: string;
  commercial_distribution_status?: string;
  device_count?: number;
  fda_listing_number?: string | null;
  premarket_submission_number?: string | null;
  gmdn_code?: string | null;
  gmdn_name?: string | null;
  device_class?: string;
  is_single_use?: boolean;
  is_prescription_only?: boolean;
  is_otc?: boolean;
  is_sterile?: boolean;
  sterilization_methods?: string[];
  mri_safety_status?: string;
  has_lot_number?: boolean;
  has_serial_number?: boolean;
  has_expiration_date?: boolean;
  has_donation_id?: boolean;
}

export interface GUDIDLookupRequest {
  udi_di: string;
}

export interface GUDIDVerificationResult {
  udi_di: string;
  is_in_gudid: boolean;
  is_checksum_valid: boolean;
  is_verified: boolean;
  gudid_record?: GUDIDDeviceRecord | null;
  pi_validation?: Record<string, any>;
  discrepancies?: string[];
  verification_timestamp?: string;
}

export interface GenerateAuditorTokenRequest {
  auditor_email: string;
  study_id: string;
  duration_hours?: number;
  reason_for_access: string;
}

export type GradingScaleEnum = "CTCAE_V5" | "DAIDS_V2_1";

export type HealthImpact =
  | "NONE"
  | "MILD_TRANSIENT"
  | "SERIOUS_DETERIORATION"
  | "PERMANENT_IMPAIRMENT"
  | "LIFE_THREATENING"
  | "DEATH";

export interface IPKitCreateRequest {
  study_id: string;
  kit_number: string;
  kit_type: string;
  description?: string | null;
  lot_number?: string | null;
  expiration_date?: string | null;
  node_id?: string | null;
}

export interface IPKitResponse {
  id: string;
  study_id: string;
  kit_number: string;
  kit_type: string;
  description?: string | null;
  status: string;
  lot_number?: string | null;
  expiration_date?: string | null;
  node_id?: string | null;
  quarantine_reason?: string | null;
  quarantined_at?: string | null;
  quarantined_by?: string | null;
}

export interface IVDAssayPerformanceRequest {
  study_id: string;
  assay_name: string;
  assay_lot_number: string;
  device_udi_di?: string | null;
  true_positives: number;
  false_positives: number;
  true_negatives: number;
  false_negatives: number;
  replicate_measurements?: number[];
  inter_assay_runs?: number[][];
  blank_measurements?: number[];
  low_standard_measurements?: number[];
  linearity_concentrations?: number[];
  linearity_signals?: number[];
  reason_for_change?: string;
}

export interface IVDAssayPerformanceResponse {
  id: string;
  study_id: string;
  assay_name: string;
  assay_lot_number: string;
  device_udi_di: string | null;
  true_positives: number;
  false_positives: number;
  true_negatives: number;
  false_negatives: number;
  sensitivity_percent: number;
  specificity_percent: number;
  ppv_percent: number;
  npv_percent: number;
  accuracy_percent: number;
  cohens_kappa: number;
  intra_assay_cv_percent: number;
  inter_assay_cv_percent: number;
  limit_of_detection: number | null;
  limit_of_quantitation: number | null;
  linearity_r_squared: number | null;
  status: string;
  passes_acceptance_criteria: boolean;
  created_at: string;
  created_by: string;
}

export type IVDContainerType =
  | "EDTA_TUBE"
  | "SST_TUBE"
  | "CRYOBIO_VIAL"
  | "HEPARIN_TUBE"
  | "STERILE_CUP"
  | "SWAB_TRANSPORT_MEDIA";

export interface IVDSampleIntegrityCheckRequest {
  hemolysis_index_mg_dl?: number;
  lipemia_index_mg_dl?: number;
  icterus_index_mg_dl?: number;
  clot_detected?: boolean;
  temperature_excursion?: boolean;
  volume_adequate?: boolean;
  integrity_notes?: string | null;
  reason_for_change?: string;
}

export interface IVDSampleIntegrityCheckResponse {
  specimen_identifier: string;
  integrity_status: string;
  hemolysis_index_mg_dl: number;
  lipemia_index_mg_dl: number;
  icterus_index_mg_dl: number;
  clot_detected: boolean;
  temperature_excursion: boolean;
  is_quarantined: boolean;
  quarantine_reason: string | null;
  validated_at: string;
  validated_by: string;
}

export interface IVDSpecimenCreateRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  specimen_identifier?: string | null;
  specimen_type?: IVDSpecimenType;
  collection_date?: string;
  collector_user_id?: string | null;
  container_type?: IVDContainerType;
  initial_volume_ul?: number;
  storage_temperature_celsius?: number;
  storage_location?: string;
  max_allowed_freeze_thaw?: number;
  reason_for_change?: string;
}

export interface IVDSpecimenCustodyEventRequest {
  from_user_id: string;
  to_user_id: string;
  from_location: string;
  to_location: string;
  transport_condition?: string;
  temperature_at_transfer_celsius?: number | null;
  seal_intact?: boolean;
  signature_token?: string | null;
  reason_for_change?: string;
}

export interface IVDSpecimenCustodyEventResponse {
  id: string;
  specimen_identifier: string;
  transfer_timestamp: string;
  from_user_id: string;
  to_user_id: string;
  from_location: string;
  to_location: string;
  transport_condition: string;
  temperature_at_transfer_celsius: number | null;
  seal_intact: boolean;
  signature_token: string | null;
}

export interface IVDSpecimenFreezeThawEventRequest {
  thaw_start_timestamp?: string;
  thaw_end_timestamp?: string | null;
  thaw_duration_minutes?: number;
  thaw_temperature_celsius?: number;
  operator_user_id: string;
  purpose: string;
  is_excursion?: boolean;
  reason_for_change?: string;
}

export interface IVDSpecimenFreezeThawEventResponse {
  id: string;
  specimen_identifier: string;
  cycle_number: number;
  thaw_start_timestamp: string;
  thaw_end_timestamp: string | null;
  thaw_duration_minutes: number;
  thaw_temperature_celsius: number;
  operator_user_id: string;
  purpose: string;
  is_excursion: boolean;
}

export interface IVDSpecimenResponse {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  specimen_identifier: string;
  specimen_type: string;
  collection_date: string;
  collector_user_id: string;
  container_type: string;
  initial_volume_ul: number;
  current_volume_ul: number;
  storage_temperature_celsius: number;
  storage_location: string;
  status: string;
  is_quarantined: boolean;
  quarantine_reason: string | null;
  freeze_thaw_count: number;
  max_allowed_freeze_thaw: number;
  hemolysis_index_mg_dl: number;
  lipemia_index_mg_dl: number;
  icterus_index_mg_dl: number;
  clot_detected: boolean;
  temperature_excursion: boolean;
  integrity_status: string;
  integrity_validated_at: string | null;
  integrity_validated_by: string | null;
  custody_events?: IVDSpecimenCustodyEventResponse[];
  freeze_thaw_events?: IVDSpecimenFreezeThawEventResponse[];
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type IVDSpecimenType =
  | "WHOLE_BLOOD"
  | "SERUM"
  | "PLASMA"
  | "URINE"
  | "SALIVA"
  | "CSF"
  | "TISSUE_BIOPSY"
  | "NASOPHARYNGEAL_SWAB"
  | "CELL_CULTURE";

export interface ImpactAnalysisRequest {
  dictionary_type: DictTypeEnum;
  new_version: string;
}

export interface ImpactAnalysisResponse {
  status: string;
  dictionary_type: DictTypeEnum;
  new_version: string;
  metrics: ImpactMetrics;
}

export interface ImpactMetrics {
  unchanged?: number;
  reclassified?: number;
  deprecated?: number;
  skipped?: number;
  verbatim_terms_affected?: number | null;
  coded_terms_affected?: number | null;
  uncodable_terms?: number | null;
}

export interface InvalidParam {
  field?: string | null;
  reason?: string | null;
  value?: string | null;
}

export type JobStatusEnum = "PENDING" | "PROCESSING" | "COMPLETED" | "FAILED";

export interface JobStatusResponse {
  job_id: string;
  dictionary_type: string;
  version: string;
  status: JobStatusEnum;
  started_at: string;
  completed_at?: string | null;
  progress_percentage?: number | null;
  records_imported?: number | null;
  errors_encountered?: number | null;
}

export interface KitQuarantineRequest {
  study_id: string;
  kit_numbers: string[];
  quarantine_reason: string;
}

export interface KitReleaseQuarantineRequest {
  study_id: string;
  kit_numbers: string[];
  qa_release_justification: string;
}

export interface LabBatchIngestResult {
  batch_id: string;
  study_id?: string | null;
  format: string;
  status: string;
  total_processed?: number;
  ingested_count?: number;
  out_of_range_count?: number;
  critical_alerts?: number;
  queries_raised?: number;
  staged_aes_count?: number;
  uncalibrated_safeguards?: number;
  toxicity_grades?: Record<string, any>;
  errors?: Record<string, any>[];
  created_at?: string;
}

export interface LabPanelItem {
  test_name: string;
  loinc_code?: string | null;
  result_value: number | string;
  result_unit: string;
  reference_low?: number | null;
  reference_high?: number | null;
  is_out_of_range?: boolean;
  toxicity_grade?: number | null;
  sample_timestamp?: string | null;
}

export interface LabRangeRecalculateRequest {
  study_id: string;
  test_code: string;
}

export interface LabRangeRecalculateResponse {
  status: string;
  study_id: string;
  test_code: string;
  updated_count: number;
}

export interface LabReferenceRangeCreate {
  study_id: string;
  test_code: string;
  test_name: string;
  source: LabSourceEnum;
  site_id?: string | null;
  unit: string;
  normalized_unit: string;
  sex_applicability: string;
  ethnicity_applicability?: string | null;
  age_low?: number | null;
  age_high?: number | null;
  low_bound?: number | null;
  high_bound?: number | null;
  critical_low?: number | null;
  critical_high?: number | null;
  reason_for_change?: string | null;
}

export interface LabReferenceRangeResponse {
  id: string;
  study_id: string;
  test_code: string;
  test_name: string;
  source: LabSourceEnum;
  site_id?: string | null;
  unit: string;
  normalized_unit: string;
  sex_applicability: string;
  ethnicity_applicability?: string | null;
  age_low?: number | null;
  age_high?: number | null;
  low_bound?: number | null;
  high_bound?: number | null;
  critical_low?: number | null;
  critical_high?: number | null;
  version: number;
  is_deleted: boolean;
}

export interface LabReferenceRangeUpdate {
  study_id?: string | null;
  test_code?: string | null;
  test_name?: string | null;
  source?: string | null;
  site_id?: string | null;
  unit?: string | null;
  normalized_unit?: string | null;
  sex_applicability?: string | null;
  ethnicity_applicability?: string | null;
  age_low?: number | null;
  age_high?: number | null;
  low_bound?: number | null;
  high_bound?: number | null;
  critical_low?: number | null;
  critical_high?: number | null;
  reason_for_change?: string | null;
}

export type LabSourceEnum = "CENTRAL" | "LOCAL";

export interface LabToxicityEvaluateRequest {
  test_code: string;
  test_name?: string | null;
  value: number;
  unit: string;
  ref_low?: number | null;
  ref_high?: number | null;
  baseline_value?: number | null;
  previous_value?: number | null;
  grading_scale?: GradingScaleEnum;
  alt_value?: number | null;
  ast_value?: number | null;
  bili_value?: number | null;
  alkp_value?: number | null;
}

export interface LabToxicityEvaluateResponse {
  test_code: string;
  test_name: string;
  observed_value: number;
  unit: string;
  ctcae_grade: number;
  grading_scale: string;
  toxicity_description: string;
  is_critical_ae_trigger: boolean;
  is_hys_law_warning: boolean;
  is_delta_check_alert: boolean;
  delta_percentage?: number | null;
}

export interface LocalLabCertificationCreate {
  study_id: string;
  site_id: string;
  lab_name: string;
  lab_identifier?: string | null;
  certification_number: string;
  accreditation_body: string;
  effective_date: string;
  expiration_date: string;
  director_name?: string | null;
  is_accredited?: boolean;
  notes?: string | null;
  reason_for_change: string;
}

export interface LocalLabCertificationResponse {
  id: string;
  study_id: string;
  site_id: string;
  lab_name: string;
  lab_identifier?: string | null;
  certification_number: string;
  accreditation_body: string;
  effective_date: string;
  expiration_date: string;
  status: CertificationStatusEnum;
  director_name?: string | null;
  is_accredited: boolean;
  notes?: string | null;
  version?: number;
  is_deleted?: boolean;
}

export interface LocalLabCertificationUpdate {
  lab_name?: string | null;
  lab_identifier?: string | null;
  certification_number?: string | null;
  accreditation_body?: string | null;
  effective_date?: string | null;
  expiration_date?: string | null;
  status?: CertificationStatusEnum | null;
  director_name?: string | null;
  is_accredited?: boolean | null;
  notes?: string | null;
  reason_for_change: string;
}

export interface LocalLabCertificationVerifyRequest {
  study_id: string;
  site_id: string;
  observation_date?: string | null;
}

export interface LocalLabCertificationVerifyResponse {
  is_valid: boolean;
  status: string;
  site_id: string;
  study_id: string;
  certification_number?: string | null;
  accreditation_body?: string | null;
  expiration_date?: string | null;
  days_until_expiration?: number | null;
  uncalibrated_safeguard_triggered?: boolean;
  message: string;
}

export interface LocalLedgerBlock {
  index: number;
  timestamp: string;
  action: string;
  details: SyncBlockDetails;
  reason: string;
  prevHash: string;
  hash: string;
}

export type LockScopeEnum =
  | "STUDY"
  | "TRIAL"
  | "SITE"
  | "SUBJECT"
  | "VISIT"
  | "FORM"
  | "ITEM_GROUP"
  | "FIELD";

export type LockStatusEnum =
  "UNLOCKED" | "FROZEN" | "LOCKED" | "HARD_LOCK" | "SOFT_LOCK";

export interface LockStatusResponse {
  locked_sites: string[];
  locked_visits: string[];
  locked_forms: string[];
  locked_subjects: string[];
  trial_locked: boolean;
}

export interface LoggerIngestionRequest {
  study_id: string;
  logger_id: string;
  file_format?: string;
  raw_payload: string;
  filename: string;
  entity_type?: string;
  entity_id: string;
  stability_budget_override?: StabilityBudgetCreateRequest | null;
}

export interface LoincCatalogResponse {
  total_count: number;
  items: LoincMappingItem[];
}

export interface LoincConvertRequest {
  test_code: string;
  from_unit: string;
  to_unit: string;
  value: number;
}

export interface LoincConvertResponse {
  test_code: string;
  from_unit: string;
  to_unit: string;
  original_value: number;
  converted_value: number;
  factor: number;
  offset?: number | null;
  loinc_code?: string | null;
}

export interface LoincMappingItem {
  test_code: string;
  test_name: string;
  loinc_code: string;
  standard_unit: string;
  category: string;
  molecular_weight?: number | null;
}

export interface LotExpiryExtensionRequest {
  study_id: string;
  lot_number: string;
  kit_type: string;
  extended_expiry_date: string;
  extension_reason: string;
  stability_protocol_reference: string;
}

export interface LotExpiryExtensionResponse {
  id: string;
  study_id: string;
  lot_number: string;
  kit_type: string;
  initial_expiry_date: string;
  extended_expiry_date: string;
  extension_reason: string;
  stability_protocol_reference: string;
  authorized_by: string;
  approval_status: string;
  affected_kits_count: number;
  relabeling_tasks_generated: number;
  created_at: string;
}

export interface MedDRACodeLookupResponse {
  status: "AUTO-CODED" | "SUGGESTIONS" | "UNCODABLE";
  matches: MedDRAMatch[];
}

export interface MedDRAMatch {
  llt_code: string;
  llt_name: string;
  pt_code: string;
  pt_name: string;
  hlt_code: string;
  hlt_name: string;
  hlgt_code: string;
  hlgt_name: string;
  soc_code: string;
  soc_name: string;
  primary_soc_flag?: string | null;
  score: number;
}

export type MedDRATargetLevelEnum = "LLT" | "PT";

export interface MeetingQuorumRecordDTO {
  study_id: string;
  charter_id: string;
  meeting_date: string;
  attendees: Record<string, any>[];
  total_eligible_members: number;
  dossiers_reviewed?: string[];
  voting_results?: Record<string, any>;
  meeting_minutes?: string;
  chair_signature?: Record<string, any> | null;
}

export interface MigrationRuleCreate {
  study_id: string;
  source_version: string;
  target_version: string;
  rule_type: string;
  source_field?: string | null;
  target_field?: string | null;
  default_value_string?: string | null;
  default_value_float?: number | null;
}

export interface MigrationRuleResponse {
  id: string;
  study_id: string;
  source_version: string;
  target_version: string;
  rule_type: string;
  source_field?: string | null;
  target_field?: string | null;
  default_value_string?: string | null;
  default_value_float?: number | null;
}

export interface MktCalculationDirectRequest {
  temperatures_celsius: number[];
  activation_energy_over_r?: number;
}

export interface MktCalculationDirectResponse {
  mean_kinetic_temperature_celsius: number;
  arithmetic_mean_celsius: number;
  min_temperature_celsius: number;
  max_temperature_celsius: number;
  readings_count: number;
  formula_reference?: string;
}

export interface MultiEchelonRollup {
  study_id: string;
  total_nodes_count: number;
  manufacturers_count: number;
  central_depots_count: number;
  regional_depots_count: number;
  clinical_sites_count: number;
  total_global_on_hand_stock: number;
  total_global_in_transit_stock: number;
  total_global_reserved_stock: number;
  total_global_storage_capacity: number;
  overall_capacity_utilization_pct: number;
  hierarchy_tree: SupplyHierarchyNode[];
}

export interface ObservationCreate {
  subject_id: string;
  study_id?: string | null;
  site_id?: string | null;
  visit_id?: string | null;
  domain: string;
  test_code: string;
  test_name: string;
  value?: number | null;
  value_string?: string | null;
  unit?: string | null;
  observation_date?: string | null;
  lab_source?: string | null;
  lab_site_id?: string | null;
}

export interface ObservationResponse {
  id: string;
  subject_id: string;
  study_id: string;
  visit_id?: string | null;
  domain: string;
  observation_date: string;
  test_code: string;
  test_name: string;
  value?: number | null;
  value_string?: string | null;
  unit?: string | null;
  normalized_value?: number | null;
  normalized_unit?: string | null;
  is_outlier: boolean;
  lab_source?: string | null;
  lab_site_id?: string | null;
  lab_indicator?: string | null;
  lab_out_of_range?: boolean | null;
  matched_normal_bounds?: string | null;
  range_indicator?: string | null;
  is_out_of_range?: boolean | null;
  reference_range_low?: number | null;
  reference_range_high?: number | null;
  protocol_version_tag?: string | null;
  protocol_version_index?: number | null;
}

export interface OfflineBatchSyncRequest {
  client_batch_id: string;
  device_id: string;
  deltas: OfflineDeltaItem[];
}

export interface OfflineBatchSyncResponse {
  client_batch_id: string;
  status: "SUCCESS" | "PARTIAL_SUCCESS" | "ALREADY_PROCESSED";
  processed_count: number;
  conflicts?: Record<string, any>[];
}

export interface OfflineDeltaItem {
  delta_id: string;
  entity_type: string;
  entity_id: string;
  action: "CREATE" | "UPDATE" | "SUBMIT";
  payload: Record<string, any>;
  client_timestamp_utc: string;
  reason_for_change: string;
}

export interface OutlierRecalculateRequest {
  study_id: string;
  test_code: string;
}

export interface OutlierRecalculateResponse {
  status: string;
  study_id: string;
  test_code: string;
  outliers_found: number;
}

export interface PHIScanRequest {
  text: string;
}

export interface PackingManifestCreateRequest {
  manifest_number: string;
  serialized_kit_numbers: string[];
  lot_number: string;
  expiration_date?: string | null;
  container_serial_number?: string | null;
  data_logger_id?: string | null;
  tamper_seal_id?: string | null;
}

export interface PackingManifestResponse {
  id: string;
  study_id: string;
  transfer_order_id: string;
  manifest_number: string;
  serialized_kit_numbers: string[];
  lot_number: string;
  expiration_date?: string | null;
  container_serial_number?: string | null;
  data_logger_id?: string | null;
  tamper_seal_id?: string | null;
  packed_by: string;
  packed_at: string;
  verified_by?: string | null;
  verified_at?: string | null;
}

export interface ParsedUDI {
  raw_barcode: string;
  udi_carrier_format?: UDICarrierFormat;
  udi_di: string;
  lot_number?: string | null;
  serial_number?: string | null;
  expiration_date?: string | null;
  manufacturing_date?: string | null;
  software_version?: string | null;
  is_valid?: boolean;
  validation_errors?: string[];
}

export interface PredictiveForecastRequest {
  study_id: string;
  site_id: string;
  kit_type?: string;
  horizon_days?: number;
  enrollment_velocity_weekly?: number;
  screening_pipeline_count?: number;
  screening_conversion_rate?: number;
  lead_time_days?: number;
  buffer_days?: number;
  safety_stock_threshold?: number;
  storage_capacity?: number;
}

export interface PredictiveForecastResponse {
  study_id: string;
  site_id: string;
  kit_type: string;
  forecast_generated_at: string;
  horizon_days: number;
  current_on_hand_qty: number;
  current_reserved_qty: number;
  current_in_transit_qty: number;
  effective_available_stock: number;
  active_subjects_count: number;
  screening_pipeline_count: number;
  enrollment_velocity_weekly: number;
  total_projected_demand: number;
  average_daily_burn_rate: number;
  days_of_supply_remaining: number;
  projected_runout_date: string | null;
  stockout_risk_level: string;
  recommended_order_qty: number;
  reorder_recommended: boolean;
  reorder_trigger_day: number | null;
  timeline: DailyDemandProjection[];
}

export interface ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail: string;
  instance: string;
  code: string;
  invalid_params?: InvalidParam[] | null;
}

export type ProtocolVersionStatusEnum =
  "DRAFT" | "ACTIVE" | "LOCKED" | "PUBLISHED" | "ARCHIVED" | "FROZEN";

export interface PublishAmendmentRequest {
  study_id: string;
  version_number: string;
  description: string;
  baseline_snapshot: Record<string, any>;
  amended_snapshot: Record<string, any>;
}

export interface PublishAmendmentResponse {
  amendment_id: string;
  study_id: string;
  version_number: string;
  published_at: string;
  summary_of_changes: string;
  added_activities_count: number;
  removed_activities_count: number;
}

export type QuarantineScope = "DEVICE_SERIAL" | "SITE_LOT" | "STUDY_GLOBAL_LOT";

export interface QueryCancel {
  reason: string;
}

export interface QueryCreate {
  study_id: string;
  subject_id: string;
  visit_id?: string | null;
  domain?: string | null;
  test_code: string;
  explanation: string;
  status?: string | null;
  observation_id?: string | null;
  field_link?: string | null;
  message?: string | null;
  origin?: string | null;
  priority?: string | null;
  rule_id?: string | null;
  created_by?: string | null;
  form_id?: string | null;
  field_id?: string | null;
  query_type?: string | null;
  action_required?: string | null;
}

export interface QueryHistoryItem {
  action: string;
  user_id?: string | null;
  timestamp: string;
  old_values?: Record<string, any> | null;
  new_values?: Record<string, any> | null;
  change_reason?: string | null;
  version_index: number;
}

export interface QueryReopen {
  reason?: string | null;
}

export interface QueryRespond {
  response: string;
  responder?: string | null;
}

export interface QueryTargetDescriptor {
  study_id?: string | null;
  subject_id: string;
  visit_id: string;
  domain: string;
  test_code: string;
  observation_id?: string | null;
  form_id?: string | null;
  field_id?: string | null;
  explanation?: string | null;
}

export interface QueryUpdate {
  status: string;
  explanation?: string | null;
  response?: string | null;
  observation_id?: string | null;
  field_link?: string | null;
  message?: string | null;
  origin?: string | null;
  priority?: string | null;
  rule_id?: string | null;
  created_by?: string | null;
  responder?: string | null;
  resolver?: string | null;
  resolved_at?: string | null;
  cancellation_reason?: string | null;
  escalated_at?: string | null;
  form_id?: string | null;
  field_id?: string | null;
  query_type?: string | null;
  action_required?: string | null;
}

export interface RARArmEvaluationDTO {
  arm_id: string;
  arm_name: string;
  is_control: boolean;
  status: string;
  enrolled_count: number;
  response_count: number;
  posterior_alpha: number;
  posterior_beta: number;
  posterior_mean: number;
  allocation_probability: number;
  superiority_vs_control_prob?: number | null;
}

export interface RAREvaluationResult {
  study_id: string;
  total_enrolled: number;
  arms: RARArmEvaluationDTO[];
  allocation_probabilities: Record<string, any>;
  dropped_arms?: string[];
  graduated_arms?: string[];
  rationale: string;
  updated_at: string;
}

export interface RARExpandOrTerminateArmRequest {
  study_id: string;
  arm_id: string;
  arm_name: string;
  is_control?: boolean;
  action: string;
  prior_alpha?: number;
  prior_beta?: number;
  reason: string;
}

export interface RaiseQueryRequest {
  query_text?: string | null;
  message?: string | null;
  explanation?: string | null;
  reason?: string | null;
  reason_for_change?: string | null;
}

export interface RaiseQueryResponse {
  query_id: string;
  status: string;
  assignment_id: string;
  explanation?: string | null;
}

export interface RarConfig {
  burn_in_sample_per_arm?: number;
  power_tuning_param?: number;
  min_allocation_prob?: number;
  max_allocation_prob?: number;
  futility_threshold?: number;
  efficacy_threshold?: number;
  prior_alpha?: number;
  prior_beta?: number;
}

export interface ReLabelingTaskCompleteRequest {
  notes?: string | null;
}

export interface ReLabelingTaskResponse {
  id: string;
  study_id: string;
  task_number: string;
  lot_number: string;
  node_id: string;
  affected_kit_count: number;
  old_expiry_date: string;
  new_expiry_date: string;
  status: string;
  assigned_to?: string | null;
  completed_at?: string | null;
  completed_by?: string | null;
  verified_at?: string | null;
  verified_by?: string | null;
  label_barcode_hash?: string | null;
  relabeling_notes?: string | null;
}

export interface ReLabelingTaskVerifyRequest {
  label_barcode_hash?: string | null;
  notes?: string | null;
}

export interface ReaderAssessment {
  assessment_id: string;
  dossier_id: string;
  reader_id: string;
  reader_name: string;
  reader_role: ReaderRole;
  classification: AdjudicationClassification;
  subtype?: string | null;
  adjudicated_onset_date?: string | null;
  criteria_met?: string[];
  confidence_score?: number;
  clinical_rationale?: string;
  submitted_at?: string | null;
  assessment_digest?: string;
}

export interface ReaderAssessmentSubmitDTO {
  reader_role: ReaderRole;
  classification: AdjudicationClassification;
  subtype?: string | null;
  adjudicated_onset_date?: string | null;
  criteria_met?: string[];
  confidence_score?: number;
  clinical_rationale?: string;
}

export type ReaderRole =
  "READER_1" | "READER_2" | "CHAIR" | "COMMITTEE_MEMBER" | "COORDINATOR";

export interface RedactPDFRequest {
  pdf_base64: string;
  target_snippets?: string[];
}

export interface ResupplyEventApprovalRequest {
  change_justification: string;
}

export interface ResupplyEventResponse {
  id: string;
  study_id: string;
  site_id: string;
  kit_id: string;
  requested_qty: number;
  status: string;
  triggered_at: any;
}

export interface SAEReconcileRequest {
  study_id: string;
  edc_ae_events: Record<string, any>[];
  safety_cases_xml?: string[] | null;
}

export type SDVScopeEnum = "FIELD" | "PAGE" | "VISIT";

export interface SDVSignOffRequest {
  scope: SDVScopeEnum;
  target_id: string;
  subject_id: string;
  study_id: string;
  site_id?: string | null;
}

export interface SDVSignOffResponse {
  id: string;
  scope: string;
  target_id: string;
  subject_id: string;
  study_id: string;
  site_id?: string | null;
  is_verified: boolean;
  verified_by?: string | null;
  verified_at?: string | null;
  dropped_reason?: string | null;
  dropped_at?: string | null;
}

export interface SafetyDispatchRequest {
  study_id: string;
  subject_id: string;
  safety_report_id: string;
  destination_gateway?: string;
  expedited?: boolean;
  reason_for_change: string;
}

export interface SafetyDispatchResponse {
  dispatch_id: string;
  safety_report_id: string;
  status: string;
  dispatched_at: string;
  ack_status: string;
}

export type SamplingModelEnum = "SUBJECT_BASED" | "FIELD_BASED" | "COMBINED";

export interface SdtmCeCompilationResult {
  study_id: string;
  ce_records?: Record<string, any>[];
  suppce_records?: Record<string, any>[];
  record_count?: number;
  compiled_at?: string;
  dataset_digest?: string;
}

export interface SdtmCeCompileRequestDTO {
  study_id: string;
}

export interface SignRequest {
  data: string;
}

export interface SignResponse {
  signed_data: string;
}

export interface SiteActivationRequest {
  study_id: string;
}

export interface SiteStaffMemberRequest {
  site_id: string;
  staff_user_id: string;
  name: string;
  email: string;
  has_gcp_training?: boolean;
}

export interface SiteStaffMemberResponse {
  id: string;
  site_id: string;
  staff_user_id: string;
  name: string;
  email: string;
  has_gcp_training: boolean;
}

export interface StabilityBudgetCreateRequest {
  study_id: string;
  kit_type: string;
  lot_number?: string | null;
  storage_condition?: string;
  min_temp_celsius?: number;
  max_temp_celsius?: number;
  max_mkt_celsius?: number;
  max_cumulative_hours_above_upper?: number;
  max_cumulative_hours_below_lower?: number;
  activation_energy_over_r?: number;
  hard_min_temp_celsius?: number | null;
  hard_max_temp_celsius?: number | null;
}

export interface StabilityBudgetResponse {
  id: string;
  study_id: string;
  kit_type: string;
  lot_number?: string | null;
  storage_condition: string;
  min_temp_celsius: number;
  max_temp_celsius: number;
  max_mkt_celsius: number;
  max_cumulative_hours_above_upper: number;
  max_cumulative_hours_below_lower: number;
  activation_energy_over_r: number;
  hard_min_temp_celsius?: number | null;
  hard_max_temp_celsius?: number | null;
}

export interface StagedAdverseEventActionRequest {
  action: string;
  ae_term?: string | null;
  causality_assessment?: string | null;
  investigator_notes?: string | null;
  electronic_signature_password?: string | null;
  reason_for_change: string;
}

export interface StagedAdverseEventActionResponse {
  id: string;
  review_status: string;
  action_taken: string;
  form_submission_id?: string | null;
  query_id?: string | null;
  message: string;
  timestamp: string;
}

export interface StagedAdverseEventReviewResponse {
  id: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  visit_id?: string | null;
  observation_id?: string | null;
  form_submission_id?: string | null;
  query_id?: string | null;
  test_code: string;
  test_name: string;
  lab_value: number;
  unit: string;
  ctcae_grade: number;
  grading_scale: string;
  toxicity_term: string;
  clinical_description: string;
  is_hys_law_warning: boolean;
  is_delta_check_alert: boolean;
  delta_percentage?: number | null;
  review_status: string;
  reviewed_by?: string | null;
  reviewed_at?: string | null;
  investigator_notes?: string | null;
  version: number;
  is_deleted: boolean;
  created_at?: string | null;
}

export interface StudyEvent {
  study_id: string;
  payload?: Record<string, any>;
}

export interface SubjectConsentRequest {
  protocol_version: ProtocolVersionRefDTO;
  icf_signed: boolean;
  icf_signed_date?: string | null;
  requires_reconsent?: boolean;
  is_paper_override?: boolean;
}

export interface SubjectCreate {
  subject_id: string;
  study_id: string;
  site_id?: string | null;
  arm_id?: string | null;
  demographics?: Demographics | null;
}

export interface SubjectDemographicsUpdateRequest {
  demographics?: Demographics | null;
  strat_factors?: Record<string, any> | null;
}

export interface SubjectDetailResponse {
  subject_id: string;
  study_id: string;
  status: string;
  site_id?: string | null;
  treatment_group?: string | null;
  randomization_seed?: string | null;
  investigational_product_id?: string | null;
}

export interface SubjectRandomizationResponse {
  subject_id: string;
  status: string;
  stratum_key?: string | null;
  randomized_at: string;
  kit_reference?: string | null;
  treatment_arm?: string | null;
}

export interface SubjectReconsentRequest {
  subject_id: string;
  study_id: string;
  protocol_version: string;
  version_index?: number;
  icf_signed?: boolean;
  signature_type?: string;
}

export interface SubjectResponse {
  id: string;
  subject_id: string;
  study_id: string;
  site_id?: string | null;
  treatment_group?: string | null;
  status?: string | null;
  active_protocol_version?: string | null;
  encrypted_demographics?: string | null;
}

export interface SubjectScreeningRequest {
  study_id?: string | null;
}

export interface SubjectScreeningResponse {
  eligible?: boolean | null;
  failed_criteria?: string[];
  indeterminate_criteria?: string[];
  criterion_evaluations?: CriterionLevelResult[];
}

export interface SubjectStateUpdateRequest {
  status?: string | null;
  state?: string | null;
}

export interface SubjectUnblindResponse {
  subject_id: string;
  status: string;
  is_unblinded: boolean;
  treatment_arm?: string | null;
  drug_code?: string | null;
  unblinded_at?: string | null;
  unblinded_by?: string | null;
  unblinded_reason?: string | null;
}

export interface SupplyHierarchyNode {
  node_id: string;
  study_id: string;
  node_type: SupplyNodeType;
  name: string;
  country: string;
  address?: string | null;
  parent_node_id?: string | null;
  storage_capacity: number;
  current_utilization: number;
  utilization_percentage: number;
  lead_time_days: number;
  temperature_conditions?: string[];
  is_active: boolean;
  total_on_hand?: number;
  total_reserved?: number;
  total_in_transit?: number;
  children?: SupplyHierarchyNode[];
}

export interface SupplyNodeCreateRequest {
  study_id: string;
  node_id: string;
  node_type?: string;
  name: string;
  country: string;
  address?: string | null;
  parent_node_id?: string | null;
  storage_capacity?: number;
  lead_time_days?: number;
  temperature_conditions?: string[];
}

export interface SupplyNodeInventoryAdjustRequest {
  study_id: string;
  node_id: string;
  kit_type: string;
  lot_number?: string | null;
  on_hand_qty: number;
  safety_stock_threshold?: number;
  reorder_point?: number;
  max_capacity?: number;
}

export interface SupplyNodeInventoryResponse {
  id: string;
  study_id: string;
  node_id: string;
  kit_type: string;
  lot_number?: string | null;
  on_hand_qty: number;
  reserved_qty: number;
  in_transit_qty: number;
  safety_stock_threshold: number;
  reorder_point: number;
  max_capacity: number;
  resupply_signal: boolean;
}

export interface SupplyNodeResponse {
  id: string;
  study_id: string;
  node_id: string;
  node_type: string;
  name: string;
  country: string;
  address?: string | null;
  parent_node_id?: string | null;
  storage_capacity: number;
  current_utilization: number;
  lead_time_days: number;
  temperature_conditions?: string[];
  is_active: boolean;
}

export type SupplyNodeType =
  "PRIMARY_MANUFACTURER" | "CENTRAL_DEPOT" | "REGIONAL_DEPOT" | "CLINICAL_SITE";

export interface SyncBlockDetails {
  fieldId: string;
  studyId?: string | null;
  subjectId?: string | null;
  visitId?: string | null;
  domain?: string | null;
  testCode?: string | null;
  query?: SyncBlockQuery | null;
  label?: string | null;
  cdash?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
}

export interface SyncBlockQuery {
  status: string;
  message?: string | null;
  createdBy?: string | null;
  createdAt?: string | null;
  response?: string | null;
  respondedBy?: string | null;
  respondedAt?: string | null;
  closedBy?: string | null;
  closedAt?: string | null;
}

export interface SyncRequest {
  blocks: LocalLedgerBlock[];
}

export interface TSDVConfigCreate {
  study_id: string;
  sampling_model: SamplingModelEnum;
  initial_full_sdv_subject_count?: number;
  random_sample_percentage?: number;
  full_sdv_domains?: string[] | null;
  safety_endpoints?: string[] | null;
  zero_sdv_domains?: string[] | null;
  trial_random_seed?: number | null;
}

export interface TSDVConfigResponse {
  id: string;
  study_id: string;
  sampling_model: string;
  initial_full_sdv_subject_count: number;
  random_sample_percentage: number;
  full_sdv_domains?: string[] | null;
  safety_endpoints?: string[] | null;
  zero_sdv_domains?: string[] | null;
  trial_random_seed?: number | null;
  version: number;
}

export interface TSDVEvaluationResponse {
  required: boolean;
  subject_selected: boolean;
  field_decision?: boolean | null;
  sampling_model: string;
  config_id: string;
  enrollment_index: number;
  explanation: string;
}

export interface TemperatureLoggerRegisterRequest {
  study_id: string;
  logger_id: string;
  device_model?: string;
  serial_number?: string | null;
  logger_type?: string;
  assigned_transfer_order_id?: string | null;
  assigned_node_id?: string | null;
  assigned_container_id?: string | null;
  calibration_expiry_date?: string | null;
}

export interface TemperatureLoggerResponse {
  id: string;
  study_id: string;
  logger_id: string;
  device_model: string;
  serial_number?: string | null;
  logger_type: string;
  assigned_transfer_order_id?: string | null;
  assigned_node_id?: string | null;
  assigned_container_id?: string | null;
  calibration_expiry_date?: string | null;
  status: string;
}

export interface ThreePlusThreeConfig {
  initial_cohort_size?: number;
  expansion_cohort_size?: number;
  max_dlt_escalate?: number;
  max_dlt_expand?: number;
}

export interface TransferOrderApprovalRequest {
  change_justification: string;
}

export interface TransferOrderCreateRequest {
  study_id: string;
  source_node_id: string;
  destination_node_id: string;
  kit_type: string;
  quantity: number;
  priority?: string;
  temperature_condition?: string;
  carrier?: string | null;
  transfer_order_number?: string | null;
}

export interface TransferOrderReceiveRequest {
  change_justification: string;
}

export interface TransferOrderResponse {
  id: string;
  study_id: string;
  transfer_order_number: string;
  source_node_id: string;
  destination_node_id: string;
  kit_type: string;
  quantity: number;
  priority: string;
  status: string;
  temperature_condition: string;
  carrier?: string | null;
  tracking_number?: string | null;
  estimated_delivery_date?: string | null;
  requested_at: string;
  requested_by?: string | null;
  approved_at?: string | null;
  approved_by?: string | null;
  shipped_at?: string | null;
  shipped_by?: string | null;
  delivered_at?: string | null;
  delivered_by?: string | null;
  rejection_reason?: string | null;
}

export interface TransferOrderShipRequest {
  tracking_number: string;
  carrier?: string | null;
  estimated_delivery_date?: string | null;
  change_justification: string;
}

export interface TranslationJobResponse {
  id: string;
  study_id: string;
  status: string;
  odm_payload?: string | null;
  openrosa_payload?: string | null;
  error_message?: string | null;
  warnings?: Record<string, any>[] | null;
}

export interface UCUMConvertRequest {
  value: number;
  source_unit: string;
  target_unit: string;
}

export interface UCUMConvertResponse {
  source: UCUMUnitValue;
  target: UCUMUnitValue;
  is_compatible: boolean;
  scale_factor: number;
  offset?: number | null;
}

export interface UCUMUnitValue {
  value: number;
  unit: string;
}

export type UDICarrierFormat =
  "GS1_128" | "GS1_DATAMATRIX" | "HIBCC_128" | "HIBCC_DATAMATRIX" | "RAW_UDI";

export interface UDIParseRequest {
  raw_barcode: string;
}

export interface UnblindRequest {
  reason_code: UnblindingReasonCode;
  justification: string;
  shares: CustodianShare[];
}

export type UnblindingReasonCode =
  | "SAE-Life-Threatening-Event"
  | "Accidental-Overdose"
  | "Required-by-Regulatory-Authority";

export interface UnitConversionRequest {
  value: number;
  from_unit: string;
  to_unit: string;
}

export interface UnitConversionResponse {
  value: number;
  from_unit: string;
  to_unit: string;
  converted_value: number;
}

export interface UploadEISFDocumentRequest {
  study_id: string;
  site_id: string;
  category: EISFTaxonomyCategoryEnum;
  title: string;
  file_name: string;
  content_base64: string;
}

export interface ValidateGatingRequest {
  subject_id: string;
  target_visit_id: string;
  active_protocol_version: string;
  requires_reconsent?: boolean;
}

export interface VerifyRequest {
  signed_data: string;
}

export interface VerifyResponse {
  is_valid: boolean;
  status: string;
  failure_reason?: string;
}

export interface VerifyTrainingGatingRequest {
  study_id: string;
  protocol_version: string;
  user_id: string;
  site_id?: string | null;
  user_role?: string | null;
}

export interface VisitCreate {
  subject_id: string;
  visit_name: string;
  study_id: string;
  visit_date?: string | null;
  planned_date?: string | null;
  window_start?: string | null;
  window_end?: string | null;
  window_status?: string | null;
}

export interface VisitDetailResponse {
  id: string;
  subject_id: string;
  visit_name: string;
  visit_date: string;
  study_id: string;
  treatment_group?: string | null;
  randomization_seed?: string | null;
  investigational_product_id?: string | null;
  planned_date?: string | null;
  window_start?: string | null;
  window_end?: string | null;
  window_status?: string | null;
}

export interface VisitResponse {
  id: string;
  subject_id: string;
  visit_name: string;
  visit_date: string;
  study_id: string;
  protocol_version_tag?: string | null;
  protocol_version_index?: number | null;
  planned_date?: string | null;
  window_start?: string | null;
  window_end?: string | null;
  window_status?: string | null;
}

export type VotingRule =
  "UNANIMOUS" | "MAJORITY_2_OF_3" | "CHAIR_TIE_BREAKER" | "SUPERMAJORITY";

export interface WHODrugATCContext {
  atc_code: string;
  description: string;
  code?: string | null;
  text?: string | null;
}

export interface WHODrugCodeLookupResponse {
  status: "AUTO-CODED" | "SUGGESTIONS" | "UNCODABLE";
  matches: WHODrugMatch[];
}

export interface WHODrugIngredientItem {
  ingredient_code: string;
  ingredient_name: string;
  code?: string | null;
  name?: string | null;
}

export interface WHODrugMatch {
  drug_code: string;
  preferred_name: string;
  drug_name?: string | null;
  score: number;
  ingredients?: WHODrugIngredientItem[];
  atc_context?: WHODrugATCContext[];
  code?: string | null;
  name?: string | null;
  atc?: WHODrugATCContext[];
}

export interface ActivityFeedEntryResponse {
  event_type: string;
  actor_user_id: string;
  actor_display_name: string;
  timestamp: string;
  detail?: Record<string, any> | null;
}

export interface ActivityFeedResponse {
  entries: ActivityFeedEntryResponse[];
  total: number;
  page: number;
  page_size: number;
}

export type ApprovalStatus =
  "PENDING_APPROVAL" | "APPROVED" | "REJECTED" | "APPROVAL_RESCINDED";

export type ConversionStatus = "NOT_REQUIRED" | "PENDING" | "READY" | "FAILED";

export interface FileApproveRequest {
  signing_reason: string;
  credential: string;
}

export interface FileAuditLogResponse {
  id: string;
  file_record_id: string;
  action: string;
  user_id: string;
  timestamp: string;
  reason_for_change: string;
  detail?: Record<string, any> | null;
}

export interface FileDeleteRequest {
  reason_for_change?: string;
}

export interface FileDownloadUrlResponse {
  file_id: string;
  filename: string;
  mime_type: string;
  download_url: string;
  expires_in?: number;
  is_watermarked?: boolean;
  access_count?: number | null;
  is_rendition?: boolean;
  rendition_key?: string | null;
}

export interface FileHoldClearRequest {
  reason_for_change: string;
}

export interface FileHoldRequest {
  reason_for_change: string;
  hold_reason?: string | null;
}

export interface FileRecordResponse {
  id: string;
  study_id: string;
  site_id: string | null;
  filename: string;
  mime_type: string;
  size_bytes: number;
  object_key: string;
  checksum_sha256: string | null;
  version_index: number;
  uploaded_by: string;
  uploaded_at: string;
  is_on_hold: boolean;
  hold_reason?: string | null;
  status?: FileStatus;
  scan_status?: ScanStatus;
  scan_engine?: string | null;
  scan_completed_at?: string | null;
  scan_threat_name?: string | null;
  transcode_status?: TranscodeStatus;
  hls_manifest_key?: string | null;
  transcode_error?: string | null;
  pdf_rendition_key?: string | null;
  conversion_status?: ConversionStatus;
  conversion_error?: string | null;
  approval_status?: ApprovalStatus;
  signature_manifestation?: Record<string, any> | null;
  approved_by?: string | null;
  approved_at?: string | null;
  approval_reason?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface FileRejectRequest {
  reason_for_change: string;
}

export interface FileRescindApprovalRequest {
  reason_for_change: string;
}

export type FileStatus =
  "PENDING_SCAN" | "READY" | "PENDING_TRANSCODE" | "QUARANTINED";

export interface FileUploadUrlRequest {
  study_id: string;
  site_id?: string | null;
  filename: string;
  mime_type: string;
  size_bytes: number;
  is_multipart?: boolean;
  parts_count?: number;
  checksum_sha256?: string | null;
  reason_for_change?: string;
}

export interface FileUploadUrlResponse {
  file_id: string;
  object_key: string;
  upload_id?: string | null;
  upload_url?: string | null;
  upload_urls?: Record<string, any> | null;
  expires_in?: number;
  checksum_sha256?: string | null;
  required_headers?: Record<string, any>;
}

export interface GuestLinkCreateRequest {
  expires_in_hours?: number;
  reason_for_change: string;
}

export interface GuestLinkResponse {
  id: string;
  file_record_id: string;
  guest_url: string;
  expires_at: string;
  created_by: string;
  access_count: number;
  is_valid: boolean;
}

export type PermissionLevel =
  | "view"
  | "comment"
  | "download"
  | "edit"
  | "upload_revision"
  | "reshare"
  | "approve"
  | "admin"
  | "expire_revoke";

export interface PlaybackAuditCreateRequest {
  file_record_id: string;
  event: PlaybackEventType;
  source?: PlaybackEventSource;
  session_id: string;
  guest_token?: string | null;
  reason_for_change?: string;
}

export interface PlaybackAuditResponse {
  id: string;
  file_record_id: string;
  event: PlaybackEventType;
  source: PlaybackEventSource;
  session_id: string;
  user_id: string;
  timestamp: string;
  created_at: string;
  reason_for_change: string;
}

export type PlaybackEventSource = "internal" | "guest_link";

export type PlaybackEventType =
  "PLAY_START" | "PLAY_25" | "PLAY_50" | "PLAY_75" | "PLAY_END";

export type ScanStatus = "PENDING" | "CLEAN" | "INFECTED" | "SCAN_ERROR";

export interface ShareGrantCreateRequest {
  granted_to_user_id?: string | null;
  scope?: ShareScope;
  permission_level?: PermissionLevel;
  expires_at?: string | null;
  reason_for_change: string;
}

export interface ShareGrantResponse {
  id: string;
  file_record_id: string;
  granted_to_user_id: string | null;
  granted_by_user_id: string;
  scope: ShareScope;
  permission_level: PermissionLevel;
  expires_at: string | null;
  revoked_at: string | null;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export type ShareScope = "study" | "site" | "individual" | "role" | "folder";

export interface StreamUrlResponse {
  file_id: string;
  stream_url?: string | null;
  expires_in?: number;
  transcode_status: TranscodeStatus;
  message?: string | null;
}

export type TranscodeStatus =
  "NOT_REQUIRED" | "PENDING" | "IN_PROGRESS" | "READY" | "FAILED";

export interface UploadCompleteRequest {
  upload_id?: string | null;
  parts?: Record<string, any>[] | null;
  checksum_sha256?: string | null;
  reason_for_change?: string;
}

export interface UploadRevisionRequest {
  filename: string;
  mime_type: string;
  size_bytes: number;
  checksum_sha256?: string | null;
  reason_for_change: string;
  is_multipart?: boolean;
  parts_count?: number;
}

export interface AdjudicationSessionResponseDto {
  session_id: string;
  study_envelope_id?: string | null;
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  assessment_criteria: string;
  workflow_status: string;
  reader_1_status: string;
  reader_2_status: string;
  reader_1_assessment?: BlindedReaderAssessmentDto | null;
  reader_2_assessment?: BlindedReaderAssessmentDto | null;
  adjudicator_assessment?: BlindedReaderAssessmentDto | null;
  sld_discordance_delta?: number;
  is_response_discordant?: boolean;
  discordance_reasons?: string[];
  final_consensus_response?: string | null;
  final_consensus_sld_mm?: number | null;
  binding_reader?: string | null;
  adjudication_rationale?: string | null;
  electronic_signature?: Record<string, any> | null;
  locked_at?: string | string | null;
  locked_by?: string | null;
  session_digest?: string;
  version_index?: number;
}

export interface BlindedReaderAssessmentDto {
  reader_id: string;
  reader_name: string;
  reader_role: string;
  timepoint_name: string;
  target_lesions?: LesionMeasurementDto[];
  non_target_status: string;
  new_lesions_present: boolean;
  new_lesions_count: number;
  sum_longest_diameters_mm: number;
  overall_response: string;
  assessment_notes?: string | null;
  submitted_at: string;
  digital_signature: string;
}

export interface Body_deidentify_in_flight_api_v1_imaging_deidentify_post {
  file: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  date_shift_days?: number;
  apply_pixel_mask?: boolean;
}

export interface Body_upload_dicom_api_v1_imaging_upload_post {
  file: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  date_shift_days?: number;
  apply_pixel_mask?: boolean;
  custom_pixel_masks_json?: string | null;
}

export interface CaliperAnnotationCreateDto {
  study_envelope_id?: string | null;
  series_envelope_id?: string | null;
  sop_instance_uid?: string | null;
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  lesion_id: string;
  lesion_type?: string;
  organ_site: string;
  measurement_criteria?: string;
  measurement_type?: string;
  longest_diameter_mm: number;
  perpendicular_diameter_mm?: number | null;
  volume_mm3?: number | null;
  slice_index?: number | null;
  slice_location?: number | null;
  deauville_score?: number | null;
  points?: PointCoordinateDto[];
  hounsfield_stats?: Record<string, any> | null;
  notes?: string | null;
}

export interface CaliperAnnotationResponseDto {
  id: string;
  study_envelope_id?: string | null;
  series_envelope_id?: string | null;
  sop_instance_uid?: string | null;
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  lesion_id: string;
  lesion_type: string;
  organ_site: string;
  measurement_criteria: string;
  measurement_type: string;
  longest_diameter_mm: number;
  perpendicular_diameter_mm?: number | null;
  product_perpendicular_mm2?: number | null;
  volume_mm3?: number | null;
  slice_index?: number | null;
  slice_location?: number | null;
  deauville_score?: number | null;
  points?: PointCoordinateDto[];
  hounsfield_stats?: Record<string, any> | null;
  reader_id: string;
  notes?: string | null;
  created_at?: string | null;
  created_by: string;
  version_index: number;
}

export interface CertificateVerificationResponse {
  certificate_id: string;
  is_valid: boolean;
  source_sha256: string;
  target_sha256: string;
  subject_id: string;
  site_id: string;
  study_id: string;
  scrubbed_tags_count: number;
  pixel_mask_applied: boolean;
  timestamp: string;
  operator_identity: string;
  message: string;
}

export interface CreateAdjudicationSessionDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  study_envelope_id?: string | null;
  assessment_criteria?: string;
  reader_1_id: string;
  reader_1_name?: string;
  reader_2_id: string;
  reader_2_name?: string;
  adjudicator_id?: string | null;
  adjudicator_name?: string | null;
}

export interface DicomDeidCertificate {
  certificate_id?: string;
  source_sha256: string;
  target_sha256: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  original_study_instance_uid_hash: string;
  original_sop_instance_uid_hash: string;
  anonymized_study_instance_uid: string;
  anonymized_sop_instance_uid: string;
  scrubbed_tags_count: number;
  scrubbed_tags?: string[];
  pixel_mask_applied?: boolean;
  pixel_masks?: PixelMaskBoundingBox[];
  operator_identity?: string;
  timestamp?: string;
  signature?: string | null;
}

export interface DicomUploadResponse {
  study_envelope_id: string;
  study_instance_uid: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  processed_instances_count: number;
  processed_series_count: number;
  modalities: string[];
  certificates: DicomDeidCertificate[];
}

export interface EDCTumorBindingResponseDto {
  form_id?: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  timepoint_name: string;
  assessment_criteria: string;
  target_lesions?: Record<string, any>[];
  sum_longest_diameters_mm?: number | null;
  baseline_sld_mm?: number | null;
  nadir_sld_mm?: number | null;
  pct_change_baseline?: number | null;
  pct_change_nadir?: number | null;
  non_target_status: string;
  new_lesions_present: boolean;
  overall_response: string;
  adjudication_status: string;
  binding_source: string;
  manifest_sha256: string;
  signed_by?: string | null;
  signed_at?: string | null;
}

export interface InstanceEnvelopeSummaryDto {
  id: string;
  sop_instance_uid: string;
  sop_class_uid: string;
  instance_number?: number | null;
  rows: number;
  columns: number;
  bits_allocated: number;
  pixel_spacing?: number[] | null;
  slice_thickness?: number | null;
  slice_location?: number | null;
  object_key: string;
  checksum_sha256: string;
  size_bytes: number;
  burned_in_phi_masked: boolean;
  certificate_id?: string | null;
}

export interface LesionMeasurementDto {
  lesion_id: string;
  organ_site: string;
  longest_diameter_mm: number;
  short_axis_mm?: number | null;
  slice_location?: number | null;
  slice_index?: number | null;
  sop_instance_uid?: string;
  deauville_score?: number | null;
}

export interface LuganoEvaluationRequestDto {
  current_lesions: LuganoLesionInputDto[];
  baseline_spd_mm2?: number;
  prior_timepoints_spd?: number[];
  new_lesions_present?: boolean;
  single_lesion_progression?: boolean;
  non_measured_progression?: boolean;
  is_baseline?: boolean;
}

export interface LuganoEvaluationResponseDto {
  criteria?: string;
  baseline_spd_mm2: number;
  nadir_spd_mm2: number;
  current_spd_mm2: number;
  pct_change_spd_baseline: number;
  pct_change_spd_nadir: number;
  ct_response: string;
  pet_metabolic_response?: string | null;
  max_deauville_score?: number | null;
  new_lesions_present: boolean;
  overall_response: string;
}

export interface LuganoLesionInputDto {
  lesion_id: string;
  organ_site: string;
  longest_diameter_mm: number;
  perpendicular_diameter_mm: number;
  is_extranodal?: boolean;
  deauville_score?: number | null;
}

export interface MprMetadataResponseDto {
  series_instance_uid: string;
  modality: string;
  rows: number;
  columns: number;
  slice_count: number;
  pixel_spacing: number[];
  slice_thickness: number;
  slice_spacing: number;
  volume_dimensions_mm: number[];
  window_center?: number | null;
  window_width?: number | null;
  available_planes: string[];
  window_presets: Record<string, any>;
}

export interface PixelMaskBoundingBox {
  x_min: number;
  y_min: number;
  x_max: number;
  y_max: number;
}

export interface PointCoordinateDto {
  x: number;
  y: number;
  z?: number | null;
}

export interface PresignedDownloadUrlResponse {
  sop_instance_uid: string;
  download_url: string;
  expires_in_seconds?: number;
}

export interface RECISTEvaluationRequestDto {
  current_lesions: RECISTLesionInputDto[];
  baseline_sld_mm?: number;
  prior_timepoints_sld?: number[];
  non_target_status?: string;
  new_lesions_present?: boolean;
  new_lesions_count?: number;
  is_baseline?: boolean;
}

export interface RECISTEvaluationResponseDto {
  criteria?: string;
  baseline_sld_mm: number;
  nadir_sld_mm: number;
  current_sld_mm: number;
  pct_change_baseline: number;
  pct_change_nadir: number;
  absolute_change_nadir_mm: number;
  target_response: string;
  non_target_status: string;
  new_lesions_present: boolean;
  new_lesions_count: number;
  overall_response: string;
  is_nadir: boolean;
}

export interface RECISTLesionInputDto {
  lesion_id: string;
  organ_site: string;
  lesion_type?: string;
  longest_diameter_mm: number;
  is_lymph_node?: boolean;
  short_axis_mm?: number | null;
  sop_instance_uid?: string | null;
}

export interface SDTMDatasetExportResponseDto {
  study_id: string;
  subject_id: string;
  timepoint_name: string;
  tu_records?: Record<string, any>[];
  tr_records?: Record<string, any>[];
  rs_records?: Record<string, any>[];
  total_records?: number;
}

export interface SeriesEnvelopeSummaryDto {
  id: string;
  series_instance_uid: string;
  series_number?: number | null;
  modality: string;
  body_part_examined?: string | null;
  series_description?: string | null;
  instance_count: number;
  protocol_name?: string | null;
  instances?: InstanceEnvelopeSummaryDto[];
}

export interface StepUpAdjudicationSignOffDto {
  signer_id: string;
  signer_name: string;
  password?: string | null;
  step_up_token?: string | null;
  signing_reason?: string;
  binding_reader?: string;
  rationale: string;
  adjudicator_assessment?: SubmitReaderAssessmentDto | null;
}

export interface StudyEnvelopeDetailDto {
  id: string;
  study_instance_uid: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  modalities: string[];
  study_date?: string | null;
  study_description?: string | null;
  series_count: number;
  instance_count: number;
  status: string;
  adjudication_status: string;
  tenant_id: string;
  created_at?: string | null;
  created_by: string;
  version_index: number;
  series?: SeriesEnvelopeSummaryDto[];
  certificate_ids?: string[];
}

export interface StudyEnvelopeSummaryDto {
  id: string;
  study_instance_uid: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  modalities: string[];
  study_date?: string | null;
  study_description?: string | null;
  series_count: number;
  instance_count: number;
  status: string;
  adjudication_status: string;
  tenant_id: string;
  created_at?: string | null;
  created_by: string;
  version_index: number;
}

export interface SubjectTimepointEvaluateRequestDto {
  study_id: string;
  site_id: string;
  timepoint_name: string;
  assessment_criteria?: string;
  non_target_status?: string;
  new_lesions_present?: boolean;
  new_lesions_count?: number;
  is_baseline?: boolean;
  digital_signature?: string | null;
}

export interface SubjectTimepointsTrajectoryResponseDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  trajectory?: TimepointTrajectorySummaryDto[];
  active_annotations_count?: number;
}

export interface SubmitReaderAssessmentDto {
  reader_id: string;
  reader_role: string;
  reader_name?: string;
  target_lesions?: LesionMeasurementDto[];
  non_target_status?: string;
  new_lesions_present?: boolean;
  new_lesions_count?: number;
  sum_longest_diameters_mm?: number;
  baseline_sld_mm?: number | null;
  nadir_sld_mm?: number | null;
  overall_response: string;
  assessment_notes?: string | null;
  digital_signature?: string;
}

export interface TimepointTrajectorySummaryDto {
  id: string;
  timepoint_name: string;
  assessment_criteria: string;
  baseline_sld_mm?: number | null;
  nadir_sld_mm?: number | null;
  current_sld_mm?: number | null;
  pct_change_baseline?: number | null;
  pct_change_nadir?: number | null;
  baseline_spd_mm2?: number | null;
  nadir_spd_mm2?: number | null;
  current_spd_mm2?: number | null;
  pct_change_spd_baseline?: number | null;
  pct_change_spd_nadir?: number | null;
  target_response: string;
  non_target_status: string;
  new_lesions_present: boolean;
  new_lesions_count: number;
  max_deauville_score?: number | null;
  overall_response: string;
  is_locked: boolean;
  locked_at?: string | null;
  reader_id: string;
  created_at?: string | null;
}

export interface ADaMPipelineResponse {
  study_id: string;
  snapshot_id: string | null;
  dataset_counts: Record<string, any>;
  total_records: number;
  datasets: Record<string, any>;
}

export interface AcknowledgeAlertRequest {
  notes: string;
  audit_signature?: string | null;
}

export interface AcknowledgeAlertResponse {
  success?: boolean;
  alert: SensorSafetyAlert;
  message?: string;
}

export interface AcknowledgeNotificationRequest {
  reason_for_change: string;
}

export interface ActigraphyDailySummary {
  total_steps: number;
  mean_cadence_spm: number;
  peak_cadence_spm: number;
  active_minutes: number;
  sedentary_minutes: number;
  sleep_duration_minutes: number;
  sleep_efficiency_percent: number;
  resting_heart_rate_bpm: number;
  readings_count: number;
}

export type AlertSeverity =
  "INFO" | "WARNING" | "CRITICAL" | "LIFE_THREATENING";

export interface ArchiveRecordingRequest {
  media_payload_or_hash: string;
  duration_seconds: number;
  resolution?: string;
  recorded_by: string;
  participant_manifest?: Record<string, any>[];
}

export interface ArchiveRecordingResponse {
  success: boolean;
  recording_id: string;
  media_sha256_hash: string;
  digital_seal_signature: string;
  sealed_at_utc: string;
  part11_certification_statement: string;
}

export interface AssignmentComplianceDetailDTO {
  assignment_id: string;
  instrument_id: string;
  instrument_name: string;
  status: string;
  due_at: string | null;
  end_date: string;
  submitted_at?: string | null;
}

export interface BloodPressureDailySummary {
  mean_systolic_mmhg: number;
  mean_diastolic_mmhg: number;
  mean_arterial_pressure_mmhg: number;
  morning_systolic_mmhg?: number | null;
  evening_systolic_mmhg?: number | null;
  readings_count: number;
}

export interface BulkSyncPayload {
  submissions: EPROSubmissionPayload[];
}

export interface CGMDailySummary {
  tir_percentage: number;
  tbr_level1_percentage: number;
  tbr_level2_percentage: number;
  tar_level1_percentage: number;
  tar_level2_percentage: number;
  mean_glucose_mg_dl: number;
  glucose_sd: number;
  glucose_cv_percent: number;
  glucose_management_indicator: number;
  readings_count: number;
  active_wear_percentage: number;
}

export type CalibrationState =
  "CALIBRATED" | "PENDING_CALIBRATION" | "EXPIRED" | "FACTORY_CALIBRATED";

export interface CanonicalClinicalRecord {
  record_id?: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  stream_type: DataStreamType;
  domain: string;
  item_code: string;
  item_name: string;
  observation_datetime: string;
  numeric_value?: number | null;
  text_value?: string | null;
  unit?: string | null;
  reference_low?: number | null;
  reference_high?: number | null;
  abnormal_flag?: string | null;
  is_unblinded?: boolean;
  raw_payload?: Record<string, any>;
  normalized_attributes?: Record<string, any>;
  data_hash: string;
  created_at?: string;
}

export interface CentralLabIngestItem {
  subject_id: string;
  site_id?: string | null;
  specimen_id: string;
  test_code: string;
  test_name: string;
  numeric_value?: number | null;
  string_value?: string | null;
  unit?: string | null;
  reference_low?: number | null;
  reference_high?: number | null;
  abnormal_flag?: string | null;
  collection_timestamp: string;
  accession_number?: string | null;
  is_unblinded?: boolean;
  metadata?: Record<string, any>;
}

export interface ClinicalSnapshot {
  snapshot_id?: string;
  study_id: string;
  version_tag: string;
  milestone: LockMilestoneType;
  lock_id?: string | null;
  record_count?: number;
  stream_counts?: Record<string, any>;
  domain_counts?: Record<string, any>;
  sha256_digest: string;
  created_at?: string;
  created_by: string;
  reason_for_snapshot: string;
  is_locked?: boolean;
  blinded_record_count?: number;
  unblinded_record_count?: number;
  snapshot_manifest?: Record<string, any>;
}

export interface CoViewStateRequest {
  document_id: string;
  page_index?: number;
  clause_id?: string | null;
  presenter_id: string;
  presenter_role?: ParticipantRole;
  scroll_percent?: number;
  active_highlights?: Record<string, any>[];
  lock_presenter_control?: boolean;
}

export interface ComprehensionAnswerDTO {
  question_id: string;
  selected_option: string;
  correct_option: string;
}

export interface ConceptMapListResponse {
  total_concepts: number;
  domains_supported: string[];
  concept_maps: ConceptMapSummaryDTO[];
}

export interface ConceptMapSummaryDTO {
  source_system: string;
  source_code: string;
  source_display: string;
  target_domain: string;
  target_variable: string;
  cdash_testcd: string;
  cdash_test: string;
  standard_unit?: string | null;
  category?: string | null;
  description?: string | null;
}

export interface CreateProgramRequest {
  study_id: string;
  name: string;
  title: string;
  description?: string;
  language: ExecutionLanguage;
  version?: string;
  source_code: string;
  lockfile_content?: string | null;
  lock_type?: PackageLockType;
  target_domain?: string | null;
  reason_for_change?: string;
}

export interface CreateSnapshotRequest {
  study_id: string;
  version_tag: string;
  milestone: LockMilestoneType;
  lock_id?: string | null;
  reason_for_snapshot: string;
  filter_criteria?: SnapshotFilterCriteria | null;
}

export interface CriterionExplanation {
  criterion_id: string;
  criterion_type: string;
  description: string;
  is_met: boolean;
  is_indeterminate: boolean;
}

export interface CustodyHandoffRequest {
  stage: string;
  released_by: string;
  received_by: string;
  tamper_seal_intact?: boolean;
  current_temperature_celsius: number;
  notes?: string;
}

export interface DCTMetricsResponse {
  success: boolean;
  study_id: string;
  total_telehealth_sessions: number;
  active_video_rooms: number;
  total_dtp_shipments: number;
  active_cold_chain_shipments: number;
  temperature_excursion_alerts: number;
  scheduled_nurse_visits: number;
  completed_home_visits: number;
  pending_offline_syncs: number;
  pediatric_assents_completed: number;
}

export interface DTPShipmentCreateRequest {
  study_id: string;
  site_id: string;
  subject_pseudonym_id: string;
  destination_address_pseudonym: string;
  courier_name?: string;
  temperature_zone?: TemperatureZone;
  kit_serials: string[];
  tamper_seal_id: string;
}

export interface DTPShipmentDTO {
  shipment_id: string;
  tracking_number: string;
  courier_name: string;
  study_id: string;
  site_id: string;
  subject_pseudonym_id: string;
  destination_address_pseudonym: string;
  temperature_zone: TemperatureZone;
  kit_serials: string[];
  tamper_seal_id: string;
  status: DTPShipmentStatus;
  current_temp_celsius: number;
  min_temp_celsius: number;
  max_temp_celsius: number;
  mkt_celsius: number;
  excursion_severity: ExcursionSeverity;
  custody_handoffs: Record<string, any>[];
  temperature_readings: Record<string, any>[];
  delivery_signature: Record<string, any> | null;
  created_at: string;
}

export interface DTPShipmentListResponse {
  success: boolean;
  shipments: DTPShipmentDTO[];
  total: number;
}

export interface DTPShipmentResponse {
  success: boolean;
  shipment: DTPShipmentDTO;
  message?: string;
}

export type DTPShipmentStatus =
  | "ORDER_CREATED"
  | "DEPOT_PICKED"
  | "PACKED"
  | "COURIER_DISPATCHED"
  | "IN_TRANSIT"
  | "OUT_FOR_DELIVERY"
  | "DELIVERED"
  | "TEMP_EXCURSION_HOLD"
  | "RETURNED";

export interface DailySummariesResponse {
  study_id: string;
  total_count: number;
  summaries?: DailySummaryEndpoints[];
}

export interface DailySummaryEndpoints {
  summary_id?: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  summary_date: string;
  device_type: SensorDeviceType;
  device_token: string;
  cgm?: CGMDailySummary | null;
  actigraphy?: ActigraphyDailySummary | null;
  spirometry?: SpirometryDailySummary | null;
  blood_pressure?: BloodPressureDailySummary | null;
  lakehouse_record_id?: string | null;
  data_hash: string;
  created_at?: string;
}

export type DataStreamType =
  | "EDC_ECRF"
  | "EPRO_ECOA"
  | "CENTRAL_LAB"
  | "LOCAL_LAB"
  | "IOT_WEARABLE"
  | "EHR_FHIR"
  | "MEDICAL_IMAGING";

export interface DatabaseLockAuthToken {
  token_id: string;
  study_id: string;
  lock_id: string;
  milestone: string;
  issued_to: string;
  authorized_roles: string[];
  issued_at: string;
  expires_at: string;
  signature_hash: string;
  is_revoked?: boolean;
  reason: string;
}

export interface DatasetJSONClinicalData {
  studyOID: string;
  metaDataVersionOID: string;
  metaDataRef?: string | null;
  itemGroupData: Record<string, any>;
}

export interface DatasetJSONDocument {
  datasetJSONCreationDateTime?: string;
  datasetJSONVersion?: string;
  fileOID: string;
  dbLastModifiedDateTime?: string | null;
  originator?: string;
  sourceSystem?: string;
  sourceSystemVersion?: string;
  clinicalData: DatasetJSONClinicalData;
}

export interface DatasetJSONItem {
  OID?: string | null;
  name: string;
  label: string;
  type: string;
  length?: number | null;
  displayFormat?: string | null;
  keySequence?: number | null;
  mandatory?: boolean | string | null;
}

export interface DatasetJSONItemGroup {
  itemGroupOID: string;
  name: string;
  label: string;
  records: number;
  items: DatasetJSONItem[];
  itemData: any[][];
}

export interface DefineXMLValidationResult {
  is_valid: boolean;
  errors?: string[];
  warnings?: string[];
  item_group_count?: number;
  item_def_count?: number;
  value_level_metadata_count?: number;
  where_clause_count?: number;
  codelist_count?: number;
  method_count?: number;
}

export interface DeliveryConfirmRequest {
  recipient_name: string;
  signature_manifestation: string;
  tamper_seal_verified?: boolean;
  logger_greenlight?: boolean;
}

export interface DeviceInvestigationExportPackageRequest {
  study_id: string;
  devices?: Record<string, any>[];
  deficiencies?: Record<string, any>[];
}

export interface DeviceInvestigationExportPackageResponse {
  study_id: string;
  generated_at: string;
  sdtm_dd_count: number;
  sdtm_do_count: number;
  sdtm_dd_dataset: Record<string, any>[];
  sdtm_do_dataset: Record<string, any>[];
  incident_reports: Record<string, any>[];
  audit_manifest: Record<string, any>;
}

export interface DeviceListResponse {
  study_id: string;
  total_count: number;
  devices?: DeviceRegistration[];
}

export interface DeviceRegistration {
  device_id?: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  device_type: SensorDeviceType;
  vendor: string;
  model_name: string;
  udi_di: string;
  udi_pi_serial?: string | null;
  udi_pi_lot?: string | null;
  mac_address_masked?: string | null;
  device_token: string;
  firmware_version?: string | null;
  calibration_status: CalibrationState;
  calibration_date?: string | null;
  is_active?: boolean;
  created_at?: string;
  created_by?: string;
  reason_for_change: string;
  version_index?: number;
}

export interface DeviceRegistrationRequest {
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  device_type: SensorDeviceType;
  vendor: string;
  model_name: string;
  device_udi?: string | null;
  mac_address?: string | null;
  serial_number?: string | null;
  firmware_version?: string | null;
  calibration_status?: CalibrationState;
  calibration_date?: string | null;
  reason_for_change?: string;
}

export interface DeviceRegistrationResponse {
  success?: boolean;
  device: DeviceRegistration;
  message?: string;
}

export interface DomainCatalogResponse {
  sdtm_domains: string[];
  adam_datasets: string[];
  domain_metadata: Record<string, any>;
  vlm_catalog: Record<string, any>;
  codelist_catalog: string[];
}

export interface EDCEcrfIngestItem {
  subject_id: string;
  visit_id: string;
  form_oid: string;
  field_name: string;
  value: any;
  unit?: string | null;
  recorded_at: string;
  status?: string;
  investigator_id?: string | null;
  site_id?: string | null;
  domain?: string | null;
  is_unblinded?: boolean;
  metadata?: Record<string, any>;
}

export interface EHRFHIRIngestItem {
  subject_id: string;
  resource_type: string;
  fhir_id: string;
  resource_payload: Record<string, any>;
  coding_code?: string | null;
  coding_display?: string | null;
  effective_datetime: string;
  site_id?: string | null;
  metadata?: Record<string, any>;
}

export interface EPROEcoaIngestItem {
  subject_id: string;
  diary_id: string;
  questionnaire_name: string;
  answers: Record<string, any>;
  total_score?: number | null;
  device_timestamp: string;
  completed_at?: string | null;
  compliance_flag?: boolean;
  site_id?: string | null;
  metadata?: Record<string, any>;
}

export interface EPROSubmissionPayload {
  subject_id: string;
  diary_id: string;
  version_index?: number;
  device_timestamp: string;
  answers: Record<string, any>;
  offline_sync_markers: OfflineSyncMarkers;
}

export interface EditQuarantinedSubmissionRequest {
  answers: Record<string, any>;
  password: string;
  change_reason: string;
}

export interface EhrCandidateItemDTO {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  subject_pseudonym: string;
  ehr_system_id: string;
  fhir_resource_type: string;
  fhir_resource_id: string;
  source_timestamp: string | string;
  target_domain: string;
  target_variable: string;
  cdash_testcd?: string | null;
  cdash_test?: string | null;
  candidate_value: any;
  candidate_unit?: string | null;
  current_ecrf_value?: any | null;
  current_ecrf_updated_at?: string | string | null;
  discrepancy_type?: string;
  confidence_score?: number;
  mapping_provenance: string;
  raw_fhir_resource?: Record<string, any>;
  status?: string;
  resolution_action?: string | null;
  resolution_reason?: string | null;
  resolved_by?: string | null;
  resolved_at?: string | string | null;
  electronic_signature?: Record<string, any> | null;
  clinical_query_id?: string | null;
  created_at?: string | string | null;
  version_index?: number;
}

export interface EhrCandidateListResponse {
  total_count: number;
  candidates: EhrCandidateItemDTO[];
}

export interface EhrEndpointCreateRequest {
  name: string;
  system_type?: EhrSystemType;
  fhir_base_url: string;
  token_url: string;
  client_id: string;
  public_key_jwks_url?: string | null;
  scopes?: string[];
  polling_interval_sec?: number;
  is_active?: boolean;
}

export interface EhrEndpointResponse {
  endpoint_id: string;
  name: string;
  system_type: string;
  fhir_base_url: string;
  token_url: string;
  client_id: string;
  public_key_jwks_url?: string | null;
  scopes: string[];
  polling_interval_sec: number;
  is_active: boolean;
  last_polled_at?: string | string | null;
  created_at?: string | string | null;
  version_index?: number;
}

export interface EhrIngestBundleRequest {
  study_id: string;
  site_id?: string;
  ehr_system_id?: string;
  bundle: Record<string, any>;
  current_ecrf_context?: Record<string, any> | null;
}

export interface EhrIngestBundleResponse {
  study_id: string;
  total_ingested: number;
  candidates: EhrCandidateItemDTO[];
}

export interface EhrPollRequest {
  endpoint_id: string;
  study_id: string;
  site_id?: string;
  subject_id: string;
  private_key_pem: string;
  current_ecrf_context?: Record<string, any> | null;
}

export interface EhrPollResponse {
  study_id: string;
  site_id: string;
  subject_id: string;
  total_ingested: number;
  candidates: EhrCandidateItemDTO[];
}

export interface EhrProvenanceResponse {
  candidate_id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  subject_pseudonym: string;
  ehr_system_id: string;
  fhir_resource_type: string;
  fhir_resource_id: string;
  source_timestamp: string | null;
  target_domain: string;
  target_variable: string;
  cdash_testcd: string | null;
  cdash_test: string | null;
  candidate_value: any;
  candidate_unit: string | null;
  current_ecrf_value: any | null;
  discrepancy_type: string;
  confidence_score: number;
  mapping_provenance: string;
  raw_fhir_resource: Record<string, any>;
  status: string;
  resolution_action: string | null;
  resolution_reason: string | null;
  resolved_by: string | null;
  resolved_at: string | null;
  electronic_signature: Record<string, any> | null;
  clinical_query_id: string | null;
  audit_trail?: Record<string, any>[];
}

export interface EhrReconcileAcceptRequest {
  candidate_id: string;
  signature_token?: string | null;
  commit_reason?: string | null;
  change_reason?: string;
}

export interface EhrReconcileActionResponse {
  success?: boolean;
  candidate: EhrCandidateItemDTO;
  clinical_query_id?: string | null;
  message: string;
}

export interface EhrReconcileQueryRequest {
  candidate_id: string;
  query_explanation: string;
  discrepancy_type?: string;
  change_reason?: string;
}

export interface EhrReconcileRejectRequest {
  candidate_id: string;
  rejection_reason: string;
  change_reason?: string;
}

export type EhrSystemType =
  "EPIC" | "CERNER" | "ALLSCRIPTS" | "SMART_FHIR_GENERIC";

export interface ElectronicSignatureBlock {
  signer_id: string;
  signer_role: string;
  signed_at?: string;
  signature_meaning?: string;
  hmac_signature: string;
}

export type ExcursionSeverity =
  "NONE" | "MINOR_WARNING" | "CRITICAL_EXCURSION_QUARANTINE";

export interface ExecuteProgramRequest {
  study_id: string;
  program_id: string;
  snapshot_id: string;
  is_unblinded?: boolean;
  database_lock_token_id?: string | null;
  sandbox_config?: ExecutionSandboxConfig | null;
  reason_for_execution?: string;
}

export interface ExecuteProgramResponse {
  manifest: ExecutionRunManifest;
}

export type ExecutionLanguage = "PYTHON" | "R";

export interface ExecutionRunDetailResponse {
  run: ExecutionRunManifest;
}

export interface ExecutionRunListResponse {
  study_id: string;
  total: number;
  runs?: ExecutionRunManifest[];
}

export interface ExecutionRunManifest {
  manifest_id?: string;
  run_id: string;
  study_id: string;
  program_id: string;
  program_name: string;
  program_version: string;
  snapshot_id: string;
  snapshot_version_tag: string;
  snapshot_digest: string;
  language: ExecutionLanguage;
  script_code_digest: string;
  lockfile_digest: string;
  container_image_digest: string;
  execution_start: string;
  execution_end: string;
  duration_ms: number;
  exit_code: number;
  status: ExecutionStatus;
  stdout_capture?: string;
  stderr_capture?: string;
  output_artifacts?: OutputArtifact[];
  output_artifacts_digest: string;
  is_unblinded_run?: boolean;
  database_lock_token_id?: string | null;
  executed_by: string;
  executed_by_role: string;
  signature_block: ElectronicSignatureBlock;
  manifest_sha256_seal: string;
}

export interface ExecutionSandboxConfig {
  max_memory_mb?: number;
  timeout_seconds?: number;
  network_isolated?: boolean;
  filesystem_readonly_root?: boolean;
  max_output_bytes?: number;
  docker_image_digest?: string;
  python_version?: string;
  r_version?: string;
  environment_variables?: Record<string, any>;
}

export type ExecutionStatus =
  "PENDING" | "RUNNING" | "COMPLETED" | "FAILED" | "TIMEOUT" | "REJECTED";

export interface ExportDatasetJSONRequest {
  study_id: string;
  snapshot_id?: string | null;
  datasets?: string[] | null;
  dataset_json_version?: string;
  reason_for_change?: string;
}

export interface ExportDefineXMLRequest {
  study_id: string;
  study_name?: string | null;
  protocol_name?: string | null;
  metadata_version_oid?: string;
  standard_name?: string;
  standard_version?: string;
  domains?: string[];
  adam_datasets?: string[];
  include_vlm?: boolean;
  include_where_clauses?: boolean;
  include_origins?: boolean;
  reason_for_change?: string;
}

export interface FHIRPreScreenRequest {
  study_id: string;
  bundle: Record<string, any>;
}

export interface FHIRPreScreenResponse {
  eligible?: boolean | null;
  failed_criteria?: string[];
  indeterminate_criteria?: string[];
  criteria_explanations?: CriterionExplanation[];
}

export interface FHIRPrefillRequest {
  study_id: string;
  bundle: Record<string, any>;
}

export interface FHIRSemanticMapRequest {
  study_id: string;
  bundle: Record<string, any>;
  enable_deterministic?: boolean;
  enable_embedding?: boolean;
  enable_llm_fallback?: boolean;
  embedding_confidence_threshold?: number;
  llm_confidence_threshold?: number;
  human_review_confidence_threshold?: number;
}

export interface FHIRSemanticMapResponse {
  study_id: string;
  subject_pseudonym: string;
  mapped_fields?: Record<string, any>;
  mapped_items?: SemanticMappedItemDTO[];
  clinical_records?: Record<string, any>;
  statistics?: MappingTierStatisticsDTO;
}

export interface IOTWearableIngestItem {
  subject_id: string;
  device_id: string;
  device_type: string;
  observation_type: string;
  telemetry_readings: IoTTelemetryReading[];
  duration_seconds?: number | null;
  sample_rate_hz?: number | null;
  calibration_status?: string;
  site_id?: string | null;
  metadata?: Record<string, any>;
}

export interface InHomeVisitCreateRequest {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_number: string;
  visit_name: string;
  nurse_id: string;
  nurse_name: string;
  nurse_license_number: string;
  scheduled_start_utc: string;
  procedures?: ProtocolProcedureDTO[];
}

export interface InHomeVisitDTO {
  visit_id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_number: string;
  visit_name: string;
  nurse_id: string;
  nurse_name: string;
  nurse_license_number: string;
  scheduled_start_utc: string;
  status: InHomeVisitStatus;
  procedures: Record<string, any>[];
  mobile_ecrf_data: Record<string, any>;
  offline_sync_id: string | null;
  sync_status: string;
  created_at: string;
}

export interface InHomeVisitListResponse {
  success: boolean;
  visits: InHomeVisitDTO[];
  total: number;
}

export interface InHomeVisitResponse {
  success: boolean;
  visit: InHomeVisitDTO;
  message?: string;
}

export type InHomeVisitStatus =
  | "SCHEDULED"
  | "CONFIRMED"
  | "EN_ROUTE"
  | "ARRIVED_ON_SITE"
  | "PROCEDURES_IN_PROGRESS"
  | "VISIT_COMPLETED"
  | "CANCELLED"
  | "RESCHEDULED";

export interface InHomeVisitStatusUpdateRequest {
  status: InHomeVisitStatus;
  procedures?: ProtocolProcedureDTO[] | null;
}

export interface IngestResponse {
  study_id: string;
  stream_type: string;
  records_ingested: number;
  unblinded_records?: number;
  record_ids?: string[];
}

export interface InstrumentCreateDTO {
  study_id: string;
  name: string;
  description?: string | null;
  items: Record<string, any>;
  response_types: Record<string, any>;
  scoring_metadata: Record<string, any>;
  reason_for_change: string;
}

export interface InstrumentResponseDTO {
  id: string;
  name: string;
  description: string | null;
  items: Record<string, any>;
  response_types: Record<string, any>;
  scoring_metadata: Record<string, any>;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface IoTTelemetryReading {
  timestamp: string;
  metric_name: string;
  value: number;
  unit?: string | null;
  flag?: string | null;
}

export interface IssueLockTokenRequest {
  study_id: string;
  lock_id: string;
  milestone: string;
  issued_to: string;
  reason: string;
  expires_in_hours?: number;
  authorized_roles?: string[] | null;
}

export type LARRelationship =
  | "PARENT"
  | "LEGAL_GUARDIAN"
  | "POWER_OF_ATTORNEY"
  | "COURT_APPOINTED"
  | "NEXT_OF_KIN";

export interface LakehouseMetrics {
  study_id: string;
  total_records?: number;
  total_snapshots?: number;
  stream_breakdown?: Record<string, any>;
  domain_breakdown?: Record<string, any>;
  subject_count?: number;
  site_count?: number;
  blinded_record_count?: number;
  unblinded_record_count?: number;
  latest_snapshot_digest?: string | null;
  last_ingested_at?: string | null;
}

export interface LakehouseMetricsResponse {
  metrics: LakehouseMetrics;
}

export interface LocalLabIngestItem {
  subject_id: string;
  site_id: string;
  test_code: string;
  test_name: string;
  value: number | string;
  unit?: string | null;
  reference_low?: number | null;
  reference_high?: number | null;
  abnormal_flag?: string | null;
  collection_timestamp: string;
  is_unblinded?: boolean;
  metadata?: Record<string, any>;
}

export type LockMilestoneType =
  | "INTERIM_ANALYSIS_1"
  | "INTERIM_ANALYSIS_2"
  | "DATABASE_LOCK"
  | "SAFETY_REVIEW"
  | "REGULATORY_SUBMISSION"
  | "PROTOCOL_FREEZE"
  | "ADJUDICATION_MILESTONE"
  | "CUSTOM";

export interface LockTokenListResponse {
  study_id: string;
  total: number;
  tokens?: DatabaseLockAuthToken[];
}

export interface LockTokenResponse {
  token: DatabaseLockAuthToken;
}

export type LockValidationStatus =
  | "VALID"
  | "TAMPERED"
  | "INVALID_SYNTAX"
  | "UNQUALIFIED_PACKAGE"
  | "UNVERIFIED";

export interface MDRIncidentReportRequest {
  study_id: string;
  site_id: string;
  deficiency_id: string;
  brand_name: string;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  incident_date: string;
  awareness_date?: string | null;
  deficiency_type?: string;
  deficiency_description: string;
  safety_classification?: string;
  causality?: string;
  patient_involved?: boolean;
  health_impact?: string;
  root_cause_analysis?: string | null;
  action_taken?: string;
  capa_required?: boolean;
  fsca_required?: boolean;
}

export interface MDRIncidentReportResponse {
  report_reference_number: string;
  mir_version?: string;
  regulatory_framework?: string;
  reporting_date: string;
  payload_json: Record<string, any>;
  payload_xml: string;
}

export interface MappingTierStatisticsDTO {
  total_extracted?: number;
  deterministic_count?: number;
  embedding_count?: number;
  llm_fallback_count?: number;
  unmapped_count?: number;
  flagged_for_review_count?: number;
  execution_latency_ms?: number;
}

export interface MedicalImagingIngestItem {
  subject_id: string;
  site_id?: string | null;
  study_instance_uid: string;
  series_instance_uid?: string | null;
  modality: string;
  timepoint: string;
  lesions?: MedicalImagingLesion[];
  overall_response?: string | null;
  sum_of_diameters_mm?: number | null;
  reader_id: string;
  adjudication_tier?: string;
  scan_datetime: string;
  is_unblinded?: boolean;
  metadata?: Record<string, any>;
}

export interface MedicalImagingLesion {
  lesion_id: string;
  lesion_type?: string;
  site_location: string;
  longest_diameter_mm?: number | null;
  short_axis_mm?: number | null;
  response_category?: string | null;
}

export interface MobileECRFSyncRequest {
  client_sync_id: string;
  offline_collected_at_utc: string;
  data: Record<string, any>;
}

export interface MultiStreamIngestPayload {
  study_id: string;
  stream_type: DataStreamType;
  edc_items?: EDCEcrfIngestItem[] | null;
  epro_items?: EPROEcoaIngestItem[] | null;
  central_lab_items?: CentralLabIngestItem[] | null;
  local_lab_items?: LocalLabIngestItem[] | null;
  wearable_items?: IOTWearableIngestItem[] | null;
  fhir_items?: EHRFHIRIngestItem[] | null;
  imaging_items?: MedicalImagingIngestItem[] | null;
  reason_for_ingest?: string;
}

export interface OutputArtifact {
  artifact_id?: string;
  name: string;
  artifact_type: OutputArtifactType;
  file_path: string;
  mime_type: string;
  size_bytes: number;
  sha256_digest: string;
  content_preview?: string | null;
}

export type OutputArtifactType =
  "TABLE" | "LISTING" | "FIGURE" | "DATASET" | "LOG" | "MANIFEST";

export interface PackageEntry {
  name: string;
  version: string;
  checksum?: string | null;
  repository?: string | null;
  is_qualified?: boolean;
}

export type PackageLockType = "UV_LOCK" | "RENV_LOCK" | "REQUIREMENTS_TXT";

export interface PackageLockValidationResult {
  lock_type: PackageLockType;
  status: LockValidationStatus;
  lock_digest: string;
  package_count: number;
  packages?: PackageEntry[];
  is_qualified: boolean;
  unqualified_packages?: string[];
  details: string;
}

export type ParticipantRole =
  | "INVESTIGATOR"
  | "SUBJECT"
  | "LAR"
  | "NURSE_COORDINATOR"
  | "TRANSLATOR"
  | "WITNESS";

export interface PediatricAssentDTO {
  assent_id: string;
  study_id: string;
  subject_id: string;
  tier: PediatricAssentTier;
  minor_age: number;
  comprehension_score_percent: number;
  comprehension_passed: boolean;
  assent_signature: string | null;
  lar_name: string | null;
  lar_relationship: string | null;
  lar_legal_basis: string | null;
  lar_signature: string | null;
  investigator_name: string | null;
  investigator_signature: string | null;
  cryptographic_bundle_hash: string;
  created_at: string;
}

export interface PediatricAssentListResponse {
  success: boolean;
  assents: PediatricAssentDTO[];
  total: number;
}

export interface PediatricAssentResponse {
  success: boolean;
  assent: PediatricAssentDTO;
  message?: string;
}

export interface PediatricAssentSubmitRequest {
  study_id: string;
  subject_id: string;
  minor_age: number;
  answers?: ComprehensionAnswerDTO[];
  assent_signature?: string | null;
  lar_name?: string | null;
  lar_relationship?: LARRelationship | null;
  lar_legal_basis?: string | null;
  lar_signature?: string | null;
  investigator_name?: string | null;
  investigator_signature?: string | null;
}

export type PediatricAssentTier =
  "CHILD_7_11" | "ADOLESCENT_12_17" | "LAR_PARENT_GUARDIAN" | "ADULT_18_PLUS";

export interface PipelineExecutionResult {
  execution_id: string;
  study_id: string;
  snapshot_id?: string | null;
  status: PipelineExecutionStatus;
  started_at: string;
  completed_at?: string | null;
  sdtm_counts?: Record<string, any>;
  adam_counts?: Record<string, any>;
  total_sdtm_records?: number;
  total_adam_records?: number;
  dataset_json_manifest?: Record<string, any>;
  define_xml_summary?: Record<string, any>;
  validation_summary?: Record<string, any>;
  errors?: string[];
}

export type PipelineExecutionStatus =
  "PENDING" | "RUNNING" | "COMPLETED" | "FAILED";

export type ProcedureType =
  | "VITAL_SIGNS"
  | "PK_BLOOD_DRAW"
  | "IP_ADMINISTRATION"
  | "12_LEAD_ECG"
  | "SAFETY_LABS"
  | "PHYSICAL_EXAM"
  | "BIOMARKER_SAMPLING"
  | "PATIENT_DIARY_REVIEW";

export interface ProgramListResponse {
  study_id: string;
  total: number;
  programs?: StatisticalProgram[];
}

export interface ProgramResponse {
  program: StatisticalProgram;
}

export interface ProtocolProcedureDTO {
  procedure_type: ProcedureType;
  description: string;
  completed?: boolean;
  timestamp_utc?: string | null;
  sample_barcodes?: string[];
  performed_by?: string;
  notes?: string;
}

export interface QuarantinedSubmissionResponse {
  id: string;
  subject_id: string;
  diary_id: string;
  device_timestamp: string;
  answers: Record<string, any>;
  original_answers: Record<string, any>;
  offline_sync_markers: Record<string, any>;
  validation_errors: string[];
  status: string;
  triage_history: Record<string, any>[];
  created_at: string;
  updated_at: string;
}

export interface RawSensorReading {
  timestamp: string;
  metric_name: string;
  value: number;
  unit: string;
  flag?: string | null;
  extra_attributes?: Record<string, any>;
}

export interface ReplayQuarantinedSubmissionRequest {
  password: string;
  change_reason: string;
}

export interface RoomTokenRequest {
  participant_id: string;
  participant_role: ParticipantRole;
  display_name: string;
  duration_minutes?: number;
}

export interface RoomTokenResponse {
  success: boolean;
  room_id: string;
  token: string;
  expires_at_utc: string;
  participant_role: ParticipantRole;
  capabilities: Record<string, any>;
}

export interface RunADaMPipelineRequest {
  study_id: string;
  snapshot_id?: string | null;
  datasets?: string[] | null;
  reason_for_change?: string;
}

export interface RunContinuousPipelineRequest {
  study_id: string;
  snapshot_id?: string | null;
  dataset_json_version?: string;
  reason_for_change?: string;
}

export interface RunSDTMPipelineRequest {
  study_id: string;
  snapshot_id?: string | null;
  domains?: string[] | null;
  reason_for_change?: string;
}

export interface SDTMDDGenerateRequest {
  study_id: string;
  deficiencies?: Record<string, any>[];
}

export interface SDTMDDResponse {
  study_id: string;
  domain?: string;
  record_count: number;
  records: Record<string, any>[];
}

export interface SDTMDOGenerateRequest {
  study_id: string;
  devices?: Record<string, any>[];
}

export interface SDTMDOResponse {
  study_id: string;
  domain?: string;
  record_count: number;
  records: Record<string, any>[];
}

export interface SDTMPipelineResponse {
  study_id: string;
  snapshot_id: string | null;
  domain_counts: Record<string, any>;
  total_records: number;
  domains: Record<string, any>;
}

export interface SafetyAlertsResponse {
  study_id: string;
  total_count: number;
  unacknowledged_count: number;
  alerts?: SensorSafetyAlert[];
}

export interface SemanticMappedItemDTO {
  source_resource_type: string;
  source_id?: string | null;
  source_code?: string | null;
  source_system?: string | null;
  source_display?: string | null;
  target_domain: string;
  target_variable: string;
  cdash_testcd?: string | null;
  cdash_test?: string | null;
  extracted_value?: any;
  extracted_unit?: string | null;
  observation_date?: string | string | null;
  mapping_tier: string;
  confidence_score: number;
  provenance: string;
  needs_human_review?: boolean;
  status?: string;
}

export type SensorAlertType =
  | "SEVERE_HYPOGLYCEMIA"
  | "HYPOGLYCEMIA"
  | "SEVERE_HYPERGLYCEMIA"
  | "HYPERGLYCEMIA"
  | "EXTREME_TACHYCARDIA"
  | "TACHYCARDIA"
  | "SEVERE_BRADYCARDIA"
  | "BRADYCARDIA"
  | "HYPERTENSIVE_CRISIS"
  | "STAGE2_HYPERTENSION"
  | "SEVERE_HYPOTENSION"
  | "ACUTE_BRONCHOCONSTRICTION"
  | "DEVICE_CLOCK_DRIFT_EXCESSIVE"
  | "BATTERY_CRITICAL";

export interface SensorDashboardMetrics {
  study_id: string;
  total_streams_ingested?: number;
  total_readings_ingested?: number;
  active_devices_count?: number;
  device_breakdown?: Record<string, any>;
  total_safety_alerts?: number;
  unacknowledged_alerts?: number;
  mean_time_in_range_percent?: number | null;
  mean_daily_steps?: number | null;
  mean_resting_hr_bpm?: number | null;
  mean_fev1_liters?: number | null;
  last_telemetry_received_at?: string | null;
}

export type SensorDeviceType =
  | "CGM"
  | "ACTIGRAPHY"
  | "SPIROMETRY"
  | "BLOOD_PRESSURE"
  | "PULSE_OXIMETER"
  | "ECG";

export interface SensorIngestResponse {
  success?: boolean;
  result: SensorIngestResult;
  message?: string;
}

export interface SensorIngestResult {
  stream_id?: string;
  study_id: string;
  subject_id_pseudonymized: string;
  device_token: string;
  device_type: SensorDeviceType;
  readings_ingested: number;
  clock_drift_offset_ms: number;
  drift_corrected: boolean;
  omh_data_points_count: number;
  fhir_observations_count: number;
  safety_alerts_triggered?: SensorSafetyAlert[];
  daily_summary?: DailySummaryEndpoints | null;
  lakehouse_record_ids?: string[];
  data_hash: string;
  ingested_at?: string;
}

export interface SensorIngestStreamPayload {
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  device_type: SensorDeviceType;
  vendor: string;
  model_name: string;
  device_udi?: string | null;
  mac_address?: string | null;
  serial_number?: string | null;
  firmware_version?: string | null;
  device_sync_timestamp: string;
  gateway_receive_timestamp: string;
  readings: RawSensorReading[];
  feed_to_lakehouse?: boolean;
  reason_for_change?: string;
}

export interface SensorMetricsResponse {
  metrics: SensorDashboardMetrics;
}

export interface SensorSafetyAlert {
  alert_id?: string;
  study_id: string;
  site_id?: string | null;
  subject_id: string;
  device_type: SensorDeviceType;
  device_token: string;
  alert_type: SensorAlertType;
  severity: AlertSeverity;
  parameter_name: string;
  observed_value: number;
  threshold_value: number;
  unit: string;
  triggered_at: string;
  is_acknowledged?: boolean;
  acknowledged_by?: string | null;
  acknowledged_at?: string | null;
  acknowledgment_notes?: string | null;
  audit_signature?: string | null;
  notified_roles?: string[];
}

export type SignalingMessageType =
  | "OFFER"
  | "ANSWER"
  | "ICE_CANDIDATE"
  | "JOIN_ROOM"
  | "LEAVE_ROOM"
  | "CO_VIEW_STATE"
  | "ANNOTATION"
  | "CHAT";

export interface SmartAssertionVerifyRequest {
  assertion_jwt: string;
  public_key_pem: string;
  expected_audience?: string | null;
  expected_client_id?: string | null;
}

export interface SmartAssertionVerifyResponse {
  is_valid: boolean;
  claims?: Record<string, any>;
  error_message?: string | null;
}

export interface SmartClientAssertionRequest {
  client_id: string;
  token_endpoint: string;
  private_key_pem: string;
  algorithm?: string;
  key_id?: string | null;
  expiration_seconds?: number;
}

export interface SmartClientAssertionResponse {
  client_assertion_jwt: string;
  token_endpoint: string;
  algorithm: string;
  expires_in_seconds: number;
}

export interface SnapshotAuditTrailResponse {
  snapshot_id: string;
  audit_entries?: Record<string, any>[];
  total_entries?: number;
}

export interface SnapshotDataQueryRequest {
  filter_criteria?: SnapshotFilterCriteria | null;
  request_unblinded?: boolean;
}

export interface SnapshotDataQueryResponse {
  snapshot_id: string;
  sha256_digest: string;
  total_records?: number;
  is_masked?: boolean;
  masked_count?: number;
  records?: CanonicalClinicalRecord[];
}

export interface SnapshotFilterCriteria {
  subject_ids?: string[] | null;
  site_ids?: string[] | null;
  stream_types?: DataStreamType[] | null;
  domains?: string[] | null;
  cutoff_datetime?: string | null;
  include_unblinded?: boolean;
}

export interface SnapshotListResponse {
  study_id: string;
  snapshots?: ClinicalSnapshot[];
  total?: number;
}

export interface SnapshotResponse {
  snapshot: ClinicalSnapshot;
  message?: string;
}

export interface SnapshotVerificationResult {
  snapshot_id: string;
  version_tag: string;
  stored_digest: string;
  computed_digest: string;
  is_valid: boolean;
  record_count: number;
  verified_at?: string;
  verified_by: string;
  verification_message: string;
}

export interface SpirometryDailySummary {
  best_fev1_liters: number;
  best_fvc_liters: number;
  fev1_fvc_ratio: number;
  fev1_percent_predicted: number;
  peak_expiratory_flow_l_min: number;
  maneuver_count: number;
  session_quality_grade?: string;
}

export interface StatisticalProgram {
  program_id?: string;
  study_id: string;
  name: string;
  title: string;
  description?: string;
  language: ExecutionLanguage;
  version?: string;
  source_code: string;
  code_hash: string;
  lockfile_content?: string | null;
  lock_type?: PackageLockType;
  lock_digest: string;
  target_domain?: string | null;
  created_by: string;
  created_at?: string;
  reason_for_change?: string;
}

export interface SubjectAssignmentCreateDTO {
  study_id: string;
  subject_id: string;
  instrument_id: string;
  start_date: string;
  end_date: string;
  recurrence_pattern?: string | null;
  due_at?: string | null;
  reason_for_change: string;
}

export interface SubjectAssignmentResponseDTO {
  id: string;
  subject_id: string;
  instrument_id: string;
  start_date: string;
  end_date: string;
  recurrence_pattern: string | null;
  due_at: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SubjectComplianceResponseDTO {
  subject_id: string;
  compliance_rate: number;
  completed_count: number;
  pending_count: number;
  overdue_count: number;
  assignments: AssignmentComplianceDetailDTO[];
}

export interface SubjectNotificationResponse {
  id: string;
  subject_id: string;
  assignment_id: string | null;
  due_at: string;
  channel: string;
  delivery_status: string;
  is_read: boolean;
  read_at: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface TelehealthSessionCreateRequest {
  study_id: string;
  subject_id: string;
  title?: string;
  scheduled_start_utc: string;
  site_id?: string | null;
  scheduled_end_utc?: string | null;
}

export interface TelehealthSessionDTO {
  id: string;
  room_id: string;
  study_id: string;
  site_id: string | null;
  subject_id: string;
  title: string;
  scheduled_start_utc: string;
  scheduled_end_utc: string | null;
  status: TelehealthSessionStatus;
  co_view_state: Record<string, any>;
  signaling_messages: Record<string, any>[];
  participant_records: Record<string, any>[];
  recording_seal_metadata: Record<string, any> | null;
  created_at: string;
  created_by: string;
}

export interface TelehealthSessionListResponse {
  success: boolean;
  sessions: TelehealthSessionDTO[];
  total: number;
}

export interface TelehealthSessionResponse {
  success: boolean;
  session: TelehealthSessionDTO;
  message?: string;
}

export type TelehealthSessionStatus =
  | "SCHEDULED"
  | "WAITING_ROOM"
  | "IN_PROGRESS"
  | "COMPLETED"
  | "CANCELLED"
  | "RECORDING_PROCESSING";

export interface TemperatureIngestRequest {
  readings: TemperatureReadingDTO[];
}

export interface TemperatureReadingDTO {
  timestamp_utc: string;
  temperature_celsius: number;
  logger_id?: string;
  battery_percent?: number;
}

export type TemperatureZone =
  "CRYOGENIC" | "ULTRA_COLD" | "FROZEN" | "REFRIGERATED" | "CONTROLLED_AMBIENT";

export interface ValidateDatasetJSONRequest {
  payload: Record<string, any> | string;
}

export interface ValidateDatasetJSONResponse {
  is_valid: boolean;
  errors?: string[];
  warnings?: string[];
  dataset_count?: number;
  record_count?: number;
}

export interface ValidateDefineXMLRequest {
  xml_content: string;
}

export interface ValidateLockfileRequest {
  lock_type: PackageLockType;
  content: string;
  expected_digest?: string | null;
  strict_qualification?: boolean;
}

export interface ValidateLockfileResponse {
  result: PackageLockValidationResult;
}

export interface VerifyAssentResponse {
  assent_id: string;
  is_valid: boolean;
  bundle_hash: string;
}

export interface VerifyManifestResponse {
  result: Record<string, any>;
}

export interface VerifySnapshotResponse {
  result: SnapshotVerificationResult;
}

export interface WebRTCSignalingRequest {
  sender_id: string;
  sender_role: ParticipantRole;
  message_type: SignalingMessageType;
  sdp?: string | null;
  candidate?: string | null;
  sdp_mid?: string | null;
  sdp_m_line_index?: number | null;
  payload?: Record<string, any>;
}

export interface ArticleApproveRequest {
  reason_for_change: string;
}

export interface ArticleCreate {
  title: string;
  slug: string;
  category_id: string;
  body_markdown: string;
  version_label?: string;
  tags?: string[] | string | null;
  reason_for_change: string;
}

export interface ArticleDraftSave {
  body_markdown: string;
  title?: string | null;
  slug?: string | null;
  category_id?: string | null;
  tags?: string[] | string | null;
  reason_for_change?: string | null;
}

export interface ArticlePublishRequest {
  reason_for_change: string;
  version_label?: string | null;
}

export interface ArticleRejectRequest {
  reason_for_change?: string | null;
}

export interface ArticleResponse {
  id: string;
  slug: string;
  title: string;
  category_id: string;
  status: ArticleStatus;
  version_index: number;
  version_label: string;
  current_published_version_id?: string | null;
  tags?: string[] | null;
  author_user_id: string;
  last_edited_by: string | null;
  approved_by: string | null;
  body_markdown?: string | null;
  body_html?: string | null;
  is_deleted: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface ArticleSearchResult {
  id: string;
  slug: string;
  title: string;
  category_id: string;
  category_name?: string | null;
  tags?: string[] | null;
  version_label: string;
  version_index: number;
  snippet: string;
  highlighted_snippet?: string;
  relevance_score: number;
  created_at: string;
}

export type ArticleStatus =
  | "DRAFT"
  | "IN_REVIEW"
  | "APPROVED"
  | "PUBLISHED"
  | "SUPERSEDED"
  | "ARCHIVED"
  | "REJECTED";

export interface ArticleSubmitReviewRequest {
  reason_for_change?: string | null;
}

export interface ArticleTransitionRequest {
  target_status: ArticleStatus;
  reason_for_change?: string | null;
  version_label?: string | null;
}

export interface ArticleUpdate {
  title?: string | null;
  slug?: string | null;
  category_id?: string | null;
  body_markdown: string;
  tags?: string[] | string | null;
  reason_for_change?: string | null;
}

export interface ArticleVersionResponse {
  id: string;
  article_id: string;
  version_index: number;
  version_label: string;
  status_at_snapshot: string;
  body_markdown: string;
  body_html: string | null;
  is_locked?: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
}

export interface AssignCurriculumRequest {
  study_id: string;
  site_id: string;
  user_id: string;
  user_name: string;
  user_role?: string;
  reason_for_change?: string;
}

export interface Body_upload_protocol_file_api_v1_knowledge_protocols_upload_post {
  study_id: string;
  protocol_version: string;
  document_type?: string;
  is_approved?: boolean;
  reason_for_change?: string;
  file: string;
}

export interface CategoryCreate {
  name: string;
  slug: string;
  description?: string | null;
  persona_visibility?: string | null;
  parent_id?: string | null;
  reason_for_change: string;
}

export interface CategoryResponse {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  persona_visibility: string | null;
  parent_id: string | null;
  is_deleted: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface CertificateResponse {
  id: string;
  assignment_id: string;
  curriculum_id: string;
  study_id: string;
  site_id: string;
  user_id: string;
  user_name: string;
  user_role: string;
  protocol_version: string;
  score_achieved: number;
  time_spent_seconds: number;
  signature_manifestation: Record<string, any>;
  sha256_seal: string;
  pdf_base64?: string | null;
  etmf_doc_id?: string | null;
  eisf_doc_id?: string | null;
  routed_to_etmf: boolean;
  routed_to_eisf: boolean;
  created_at: string;
}

export interface ContextualHelpMappingCreate {
  route_pattern: string;
  persona?: string | null;
  article_id: string;
  section_anchor?: string | null;
  priority?: number;
  is_active?: boolean;
  reason_for_change: string;
}

export interface ContextualHelpMappingResponse {
  id: string;
  route_pattern: string;
  persona: string | null;
  article_id: string;
  section_anchor?: string | null;
  priority: number;
  is_active?: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface ContextualHelpMappingUpdate {
  route_pattern?: string | null;
  persona?: string | null;
  article_id?: string | null;
  section_anchor?: string | null;
  priority?: number | null;
  is_active?: boolean | null;
  reason_for_change: string;
}

export interface ContextualHelpResolutionResponse {
  matched_mapping?: ContextualHelpMappingResponse | null;
  primary_article?: ArticleResponse | null;
  primary_version?: ArticleVersionResponse | null;
  section_anchor?: string | null;
  related_articles?: ArticleResponse[];
  article?: ArticleResponse | null;
  version?: ArticleVersionResponse | null;
}

export interface GenerateAmendmentCurriculumRequest {
  study_id: string;
  protocol_version: string;
  baseline_snapshot: Record<string, any>;
  amended_snapshot: Record<string, any>;
  target_roles?: string[] | null;
  site_assignments?: Record<string, any>[] | null;
  reason_for_change?: string;
}

export interface ProtocolChunkSummary {
  chunk_id: string;
  document_id: string;
  study_id: string;
  protocol_version: string;
  section_number: string | null;
  section_title: string;
  page_number: number;
  token_count: number;
  is_approved: boolean;
  citation_marker: string;
}

export interface ProtocolIngestTextRequest {
  study_id: string;
  protocol_version: string;
  raw_text: string;
  filename?: string;
  document_id?: string | null;
  document_type?: string;
  is_approved?: boolean;
  reason_for_change?: string;
}

export interface ProtocolSearchRequest {
  query: string;
  study_id: string;
  protocol_version?: string | null;
  only_approved?: boolean;
  top_k?: number;
}

export interface ProtocolSearchResult {
  chunk_id: string;
  document_id: string;
  study_id: string;
  protocol_version: string;
  section_number: string | null;
  section_title: string;
  page_number: number;
  chunk_text: string;
  is_approved: boolean;
  similarity_score: number;
  citation_marker: string;
}

export interface QualificationGateResult {
  is_qualified: boolean;
  user_id: string;
  study_id: string;
  protocol_version: string;
  site_id?: string | null;
  blocked_reason?: string | null;
  pending_curricula?: Record<string, any>[];
}

export interface QuizEvaluationResponse {
  score_percentage: number;
  passed: boolean;
  passing_threshold: number;
  total_questions: number;
  correct_count: number;
  feedback?: Record<string, any>[];
}

export interface RecordProgressRequest {
  time_spent_delta_seconds: number;
  reason_for_change?: string;
}

export interface SignTrainingRequest {
  signer_name: string;
  signer_role: string;
  meaning?: string;
  reason_for_change?: string;
}

export interface SubmitQuizRequest {
  answers: Record<string, any>;
  reason_for_change?: string;
}

export interface TrainingAssignmentDetailResponse {
  id: string;
  curriculum_id: string;
  study_id: string;
  site_id: string;
  user_id: string;
  user_name: string;
  user_role: string;
  status: string;
  time_spent_seconds: number;
  quiz_score?: number | null;
  attempts_count: number;
  completed_at?: string | null;
  certificate_id?: string | null;
  created_at: string;
  curriculum?: TrainingCurriculumResponse | null;
}

export interface TrainingAssignmentResponse {
  id: string;
  curriculum_id: string;
  study_id: string;
  site_id: string;
  user_id: string;
  user_name: string;
  user_role: string;
  status: string;
  time_spent_seconds: number;
  quiz_score?: number | null;
  attempts_count: number;
  completed_at?: string | null;
  certificate_id?: string | null;
  created_at: string;
}

export interface TrainingCurriculumResponse {
  id: string;
  study_id: string;
  protocol_version: string;
  title: string;
  description: string;
  target_roles: string[];
  is_mandatory: boolean;
  passing_score: number;
  min_reading_time_seconds: number;
  is_active: boolean;
  modules?: TrainingModuleSummary[];
  created_at: string;
  created_by: string;
}

export interface TrainingModuleSummary {
  id: string;
  curriculum_id: string;
  title: string;
  module_type: string;
  content_text: string;
  video_url?: string | null;
  document_reference?: string | null;
  order_index: number;
  min_reading_time_seconds: number;
  quiz_questions?: Record<string, any>[];
}

export interface VerifyGateRequest {
  user_id: string;
  study_id: string;
  protocol_version: string;
  site_id?: string | null;
  user_role?: string | null;
}

export type NotificationCategory = "ALERTS" | "SYSTEM" | "ACTION_ITEMS";

export interface NotificationCreate {
  recipient_user_id?: string | null;
  recipient_role?: string | null;
  category: NotificationCategory;
  priority: NotificationPriority;
  channels?: string;
  message_content: string;
  related_entity_id?: string | null;
  related_entity_type?: string | null;
}

export type NotificationPriority = "LOW" | "MEDIUM" | "HIGH" | "CRITICAL";

export interface NotificationResponse {
  id: string;
  recipient_user_id?: string | null;
  recipient_role?: string | null;
  category: NotificationCategory;
  priority: NotificationPriority;
  channels: string;
  message_content: string;
  related_entity_id?: string | null;
  related_entity_type?: string | null;
  status: NotificationStatus;
  delivery_state: string;
  retries: number;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type NotificationStatus = "OPEN" | "ACKNOWLEDGED" | "RESOLVED";

export interface AssignmentResolutionResponse {
  personnel_id: string;
  roles: string[];
  assigned_sites: string[];
  assigned_studies: string[];
}

export type ClinicalStaffRole =
  | "Principal Investigator"
  | "Sub-Investigator"
  | "CRC"
  | "CRA/Monitor"
  | "External Monitor";

export interface DelegationCreate {
  delegator_id: string;
  delegatee_id: string;
  site_id: string;
  study_id: string;
  duties: string[];
  start_date: string;
  end_date?: string | null;
  reason_for_change: string;
}

export interface DelegationResponse {
  id: string;
  delegator_id: string;
  delegatee_id: string;
  site_id: string;
  study_id: string;
  duties: string[];
  start_date: string;
  end_date?: string | null;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  signature?: string | null;
  signed_payload?: Record<string, any> | null;
  signed_at?: string | null;
  signed_by?: string | null;
  revoked_at?: string | null;
  revoked_by?: string | null;
  revocation_reason?: string | null;
}

export interface DelegationRevoke {
  reason_for_change: string;
}

export interface DelegationSign {
  payload: Record<string, any>;
  signature: string;
  reason_for_change: string;
}

export interface OrgAuditLogResponse {
  id: string;
  timestamp: string;
  actor_id: string;
  actor_role: string;
  action: string;
  record_id?: string | null;
  details: string;
  reason_for_change: string;
}

export interface OrganizationCreate {
  name: string;
  org_type: OrganizationType;
  reason_for_change: string;
}

export interface OrganizationResponse {
  id: string;
  name: string;
  org_type: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type OrganizationType =
  "sponsor" | "CRO" | "IRB/IEC" | "central laboratory" | "site";

export interface OrganizationUpdate {
  name?: string | null;
  org_type?: OrganizationType | null;
  reason_for_change: string;
}

export interface PersonnelAssignmentCreate {
  site_id: string;
  study_id: string;
  is_active?: boolean;
  reason_for_change: string;
}

export interface PersonnelAssignmentResponse {
  id: string;
  personnel_id: string;
  site_id: string;
  study_id: string;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface PersonnelAssignmentUpdate {
  site_id?: string | null;
  study_id?: string | null;
  is_active?: boolean | null;
  reason_for_change: string;
}

export interface PersonnelCreate {
  keycloak_user_id?: string | null;
  first_name: string;
  last_name: string;
  email: string;
  role: ClinicalStaffRole;
  organization_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  reason_for_change: string;
}

export interface PersonnelResponse {
  id: string;
  keycloak_user_id?: string | null;
  first_name: string;
  last_name: string;
  email: string;
  role: string;
  organization_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface PersonnelUpdate {
  keycloak_user_id?: string | null;
  first_name?: string | null;
  last_name?: string | null;
  email?: string | null;
  role?: ClinicalStaffRole | null;
  organization_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  reason_for_change: string;
}

export interface SiteCreate {
  site_id: string;
  name: string;
  organization_id: string;
  study_id?: string | null;
  reason_for_change: string;
}

export interface SiteResponse {
  id: string;
  site_id: string;
  name: string;
  organization_id: string;
  study_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SiteUpdate {
  site_id?: string | null;
  name?: string | null;
  organization_id?: string | null;
  study_id?: string | null;
  reason_for_change: string;
}

export interface TrainingLogCreate {
  personnel_id: string;
  site_id: string;
  study_id: string;
  training_topic: string;
  completion_date: string;
  reason_for_change: string;
}

export interface TrainingLogResponse {
  id: string;
  personnel_id: string;
  site_id: string;
  study_id: string;
  training_topic: string;
  completion_date: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  signature_manifestation?: Record<string, any> | null;
  signer?: string | null;
  signing_timestamp?: string | null;
}

export interface TrainingLogSignRequest {
  payload: Record<string, any>;
  signature: string;
  reason_for_change: string;
}

export interface TrainingLogUpdate {
  personnel_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  training_topic?: string | null;
  completion_date?: string | null;
  reason_for_change?: string | null;
}

export interface BalanceInquiryResponse {
  wallet_id: string;
  subject_id: string;
  study_id: string;
  site_id: string;
  current_balance: number;
  available_balance: number;
  pending_claims_total: number;
  lifetime_stipends: number;
  lifetime_reimbursements: number;
  lifetime_disbursements: number;
  taxable_ytd: number;
  currency: string;
  virtual_card_mask: string | null;
  card_status: string;
  card_type: string;
  ach_account_mask: string | null;
  is_1099_threshold_exceeded: boolean;
  w9_status: string;
}

export interface BatchEventIngestDto {
  ecrf_submissions?: EcrfSubmissionEventDto[];
  sdv_verifications?: CraSdvVerificationEventDto[];
  milestones?: MilestoneCompletionEventDto[];
}

export interface BillingCycleCompilationRequest {
  study_id: string;
  site_id: string;
  billing_cycle?: string;
  period_start: string;
  period_end: string;
  auto_approve?: boolean;
}

export interface BudgetContractCreate {
  study_id: string;
  site_id: string;
  contract_name: string;
  tier_level?: string;
  currency?: string;
  total_budget?: number;
  overhead_percentage?: number;
  withholding_percentage?: number;
  effective_date?: string | null;
}

export interface BudgetContractResponse {
  id: string;
  study_id: string;
  site_id: string;
  contract_name: string;
  tier_level: string;
  currency: string;
  total_budget: number;
  overhead_percentage: number;
  withholding_percentage: number;
  effective_date: string | null;
  status: string;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
  fee_schedules_count?: number;
  split_payees_count?: number;
}

export interface CraSdvVerificationEventDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_code: string;
  form_id?: string | null;
  verified_by: string;
  event_id: string;
  timestamp?: string | null;
}

export interface DisbursementRequest {
  disbursement_type?: string;
  amount?: number | null;
  destination_mask?: string | null;
}

export interface DisbursementResponse {
  id: string;
  wallet_id: string;
  subject_id: string;
  study_id: string;
  disbursement_type: string;
  amount: number;
  currency: string;
  status: string;
  reference_number: string;
  destination_mask: string;
  dispatched_at: string;
  settled_at: string | null;
  error_message: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface EcrfSubmissionEventDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_code: string;
  form_id: string;
  event_id: string;
  timestamp?: string | null;
  actor_id?: string;
}

export interface ErpExportResponse {
  erp_system: string;
  invoice_number: string;
  document_type?: string;
  payload: Record<string, any>;
  export_timestamp: string;
}

export interface ErpSyncLogResponse {
  id: string;
  invoice_id: string;
  erp_system: string;
  sync_direction: string;
  status: string;
  payload: Record<string, any> | null;
  response_data: Record<string, any> | null;
  external_reference: string | null;
  reconciled: boolean;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface ErpWebhookPayload {
  erp_system?: string;
  invoice_number?: string | null;
  invoice_id?: string | null;
  erp_reference_id: string;
  status: string;
  payment_date?: string | null;
  amount_paid?: number | null;
  currency?: string;
  bank_reference?: string | null;
  notes?: string | null;
}

export interface ErpWebhookResponse {
  status: string;
  invoice_id: string;
  invoice_number: string;
  erp_system: string;
  erp_reference_id: string;
  previous_status: string;
  new_status: string;
  reconciled: boolean;
  message: string;
}

export interface ExpenseClaimResponse {
  id: string;
  wallet_id: string;
  subject_id: string;
  study_id: string;
  site_id: string;
  expense_date: string;
  category: string;
  merchant_name: string;
  gross_amount: number;
  tax_deduction: number;
  net_amount: number;
  currency: string;
  is_meal_allowance?: boolean;
  per_diem_cap?: number | null;
  receipt_image_name: string | null;
  receipt_ocr_data: Record<string, any> | null;
  status: string;
  reviewed_by: string | null;
  reviewed_at: string | null;
  review_notes: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface ExpenseClaimReviewRequest {
  status: string;
  review_notes?: string | null;
  adjusted_amount?: number | null;
  adjusted_category?: string | null;
  adjusted_merchant?: string | null;
}

export interface ExpenseClaimSubmitRequest {
  wallet_id?: string | null;
  subject_id: string;
  study_id: string;
  site_id: string;
  expense_date: string;
  category?: string;
  merchant_name: string;
  gross_amount: number;
  tax_deduction?: number;
  net_amount?: number | null;
  currency?: string;
  is_meal_allowance?: boolean;
  per_diem_cap?: number | null;
  receipt_image_name?: string | null;
  receipt_image_base64?: string | null;
  receipt_ocr_data?: Record<string, any> | null;
}

export interface FeeScheduleCreate {
  visit_code: string;
  activity_id?: string | null;
  form_id?: string | null;
  procedure_code: string;
  procedure_name: string;
  base_amount: number;
  overhead_percentage?: number | null;
  withholding_percentage?: number | null;
  patient_stipend?: number;
  travel_reimbursement?: number;
  requires_sdv?: boolean;
}

export interface FeeScheduleResponse {
  id: string;
  contract_id: string;
  visit_code: string;
  activity_id: string | null;
  form_id: string | null;
  procedure_code: string;
  procedure_name: string;
  base_amount: number;
  overhead_percentage: number;
  withholding_percentage: number;
  patient_stipend: number;
  travel_reimbursement: number;
  requires_sdv: boolean;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
}

export interface HoldbackReleaseRequest {
  study_id: string;
  site_id: string;
  release_reason: string;
  dm_signoff_by: string;
  quality_signoff_by: string;
  holdback_ids?: string[] | null;
}

export interface HoldbackReleaseResponse {
  study_id: string;
  site_id: string;
  total_released_amount: number;
  currency: string;
  released_count: number;
  dm_signoff_by: string;
  quality_signoff_by: string;
  status: string;
  released_reserves?: HoldbackReserveResponse[];
}

export interface HoldbackReserveResponse {
  id: string;
  study_id: string;
  site_id: string;
  contract_id: string;
  payable_id: string;
  holdback_amount: number;
  currency: string;
  status: string;
  release_authorized_by: string | null;
  release_authorized_at: string | null;
  dm_signoff_by: string | null;
  dm_signoff_at: string | null;
  quality_signoff_by: string | null;
  quality_signoff_at: string | null;
  release_notes: string | null;
  created_at: string;
  version_index: number;
}

export interface InvoiceLineItemResponse {
  id: string;
  invoice_id: string;
  payable_id: string | null;
  subject_id: string;
  visit_code: string;
  procedure_code: string | null;
  procedure_name: string | null;
  base_amount: number;
  overhead_amount: number;
  patient_stipend: number;
  travel_reimbursement: number;
  gross_amount: number;
  withholding_rate: number;
  withholding_amount: number;
  net_amount: number;
  currency: string;
  created_at: string;
  version_index: number;
}

export interface IssueReloadableCardRequest {
  cardholder_name?: string | null;
  card_type?: string;
  initial_load?: number;
}

export interface IssueReloadableCardResponse {
  wallet_id: string;
  subject_id: string;
  card_token: string;
  card_mask: string;
  card_expiry: string;
  card_type: string;
  card_status: string;
  cardholder_name: string;
  current_balance: number;
  issued_at: string;
}

export interface LaySummaryCreate {
  study_id: string;
  protocol_id?: string | null;
  title: string;
  trial_phase?: string;
  therapeutic_area: string;
  sponsor_name: string;
  summary_text: string;
  plain_language_sections?: Record<string, any> | null;
  irb_approval_number: string;
  irb_approval_date: string;
  language?: string;
  status?: string;
}

export interface LaySummaryPublishRequest {
  irb_approval_number?: string | null;
  irb_approval_date?: string | null;
}

export interface LaySummaryReceiptConfirmRequest {
  subject_id: string;
  study_id: string;
  comprehension_score?: number;
  comprehension_feedback?: string | null;
  confirmation_statement?: string;
}

export interface LaySummaryReceiptResponse {
  id: string;
  summary_id: string;
  subject_id: string;
  study_id: string;
  comprehension_score: number;
  comprehension_feedback: string | null;
  confirmation_statement: string;
  receipt_hash: string;
  signer_identity: string;
  confirmed_at: string;
  status?: string;
}

export interface LaySummaryResponse {
  id: string;
  study_id: string;
  protocol_id: string | null;
  title: string;
  trial_phase: string;
  therapeutic_area: string;
  sponsor_name: string;
  summary_text: string;
  plain_language_sections: Record<string, any> | null;
  irb_approval_number: string;
  irb_approval_date: string;
  language: string;
  status: string;
  published_at: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  receipts_count?: number;
}

export interface MilestoneCompletionEventDto {
  study_id: string;
  site_id: string;
  milestone_code: string;
  milestone_name: string;
  amount: number;
  verified_by: string;
  event_id: string;
  timestamp?: string | null;
}

export interface ParticipantStipendRuleCreate {
  study_id: string;
  visit_code: string;
  visit_name: string;
  procedure_code?: string | null;
  amount: number;
  currency?: string;
  requires_verification?: boolean;
  taxable?: boolean;
  anti_coercion_cap?: number | null;
  is_milestone?: boolean;
  milestone_code?: string | null;
}

export interface ParticipantStipendRuleResponse {
  id: string;
  study_id: string;
  visit_code: string;
  visit_name: string;
  procedure_code: string | null;
  amount: number;
  currency: string;
  requires_verification: boolean;
  taxable: boolean;
  anti_coercion_cap?: number | null;
  is_milestone?: boolean;
  milestone_code?: string | null;
  is_active: boolean;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface ParticipantTransactionResponse {
  id: string;
  wallet_id: string;
  subject_id: string;
  study_id: string;
  transaction_type: string;
  amount: number;
  balance_after: number;
  reference_id: string;
  description: string;
  created_at: string;
  created_by: string;
  version_index: number;
}

export interface ParticipantWalletCreate {
  subject_id: string;
  study_id: string;
  site_id: string;
  currency?: string;
  card_type?: string;
  virtual_card_token?: string | null;
  virtual_card_mask?: string | null;
  virtual_card_expiry?: string | null;
  anti_coercion_cap?: number | null;
  ach_routing_number?: string | null;
  ach_account_mask?: string | null;
  ach_account_holder?: string | null;
}

export interface ParticipantWalletPayoutMethodsUpdate {
  virtual_card_token?: string | null;
  virtual_card_mask?: string | null;
  virtual_card_expiry?: string | null;
  card_status?: string | null;
  ach_routing_number?: string | null;
  ach_account_mask?: string | null;
  ach_account_holder?: string | null;
}

export interface ParticipantWalletResponse {
  id: string;
  subject_id: string;
  study_id: string;
  site_id: string;
  current_balance: number;
  lifetime_stipends: number;
  lifetime_reimbursements: number;
  lifetime_disbursements: number;
  currency: string;
  status: string;
  taxable_ytd?: number;
  is_1099_threshold_exceeded?: boolean;
  w9_status?: string;
  w9_submitted_at?: string | null;
  w9_verified_at?: string | null;
  tax_id_last_four?: string | null;
  card_type?: string;
  card_status?: string;
  virtual_card_token?: string | null;
  virtual_card_mask?: string | null;
  virtual_card_expiry?: string | null;
  anti_coercion_cap?: number | null;
  ach_routing_number?: string | null;
  ach_account_mask?: string | null;
  ach_account_holder?: string | null;
  created_at: string;
  created_by: string;
  updated_at: string;
  version_index: number;
  pending_claims_count?: number;
  total_claims_count?: number;
  disbursements_count?: number;
}

export interface PayableAllocationResponse {
  id: string;
  payable_id: string;
  payee_id: string;
  payee_name: string;
  payee_role: string;
  allocation_percentage: number;
  gross_share: number;
  withholding_share: number;
  net_share: number;
  currency: string;
  status: string;
  created_at: string;
  version_index: number;
}

export interface PayableResponse {
  id: string;
  study_id: string;
  site_id: string;
  contract_id: string;
  subject_id: string;
  visit_code: string;
  form_id: string | null;
  procedure_code: string | null;
  procedure_name: string | null;
  trigger_type: string;
  trigger_event_id: string;
  trigger_source: string;
  base_amount: number;
  overhead_amount: number;
  patient_stipend_amount: number;
  travel_reimbursement_amount: number;
  gross_amount: number;
  withholding_rate: number;
  withholding_amount: number;
  net_amount: number;
  currency: string;
  status: string;
  is_sdv_gated: boolean;
  sdv_verified_at: string | null;
  sdv_verified_by: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
  allocations?: PayableAllocationResponse[];
  holdback_reserve?: HoldbackReserveResponse | null;
}

export interface PaymentAuditLogResponse {
  id: string;
  entity_type: string;
  entity_id: string;
  action: string;
  previous_state: Record<string, any> | null;
  new_state: Record<string, any> | null;
  details: string | null;
  user_id: string;
  user_roles: string;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
}

export interface PaymentReconciliationRequest {
  payment_reference: string;
  erp_system?: string;
  payment_date?: string | null;
  amount_paid: number;
  notes?: string | null;
}

export interface PaymentReconciliationResponse {
  invoice_id: string;
  invoice_number: string;
  status: string;
  erp_sync_status: string;
  reconciled_by: string;
  reconciled_at: string;
  reconciliation_reference: string;
  total_net_amount: number;
  amount_paid: number;
  reconciled_payables_count: number;
}

export interface ReceiptOcrExtractRequest {
  image_base64?: string | null;
  raw_text?: string | null;
  category_hint?: string | null;
  filename?: string | null;
}

export interface ReceiptOcrExtractResponse {
  merchant_name: string;
  expense_date: string;
  gross_amount: number;
  tax_deduction: number;
  net_amount: number;
  category: string;
  confidence_score: number;
  detected_keywords: string[];
  raw_text: string;
}

export interface RemittanceAdviceDispatchRequest {
  recipient_email: string;
}

export interface RemittanceAdviceResponse {
  id: string;
  remittance_number: string;
  invoice_id: string;
  study_id: string;
  site_id: string;
  currency: string;
  gross_amount: number;
  overhead_amount: number;
  holdback_amount: number;
  net_amount: number;
  pdf_sha256_hash: string;
  is_sealed: boolean;
  is_signed: boolean;
  signed_by: string | null;
  signed_at: string | null;
  signer_role: string | null;
  signature_manifest: Record<string, any> | null;
  dispatched_to: string | null;
  dispatched_at: string | null;
  dispatch_status: string;
  created_at: string;
  version_index: number;
}

export interface RemittanceAdviceSignRequest {
  signer_name: string;
  signer_role?: string;
  signature_reason?: string;
  cert_pem?: string | null;
  private_key_pem?: string | null;
}

export interface RemittanceSignatureVerificationResponse {
  is_valid: boolean;
  status: string;
  failure_reason?: string | null;
  pdf_sha256_hash: string;
  signed_by?: string | null;
  signed_at?: string | null;
  signer_role?: string | null;
}

export interface SelfBillingInvoiceResponse {
  id: string;
  invoice_number: string;
  study_id: string;
  site_id: string;
  contract_id: string;
  billing_cycle: string;
  period_start: string;
  period_end: string;
  currency: string;
  total_base_amount: number;
  total_overhead_amount: number;
  total_patient_stipend: number;
  total_travel_reimbursement: number;
  total_gross_amount: number;
  total_holdback_amount: number;
  total_net_amount: number;
  payable_count: number;
  status: string;
  approved_by: string | null;
  approved_at: string | null;
  erp_system: string | null;
  erp_reference_id: string | null;
  erp_sync_status: string;
  erp_synced_at: string | null;
  reconciled_by: string | null;
  reconciled_at: string | null;
  reconciliation_reference: string | null;
  reconciliation_notes: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
  line_items?: InvoiceLineItemResponse[];
  remittance_advice?: RemittanceAdviceResponse | null;
}

export interface SplitPayeeConfigBulkUpdate {
  payees: SplitPayeeConfigItem[];
}

export interface SplitPayeeConfigItem {
  payee_id: string;
  payee_name: string;
  payee_role: string;
  allocation_percentage: number;
  bank_account_reference?: string | null;
  tax_identifier?: string | null;
}

export interface SplitPayeeConfigResponse {
  id: string;
  contract_id: string;
  payee_id: string;
  payee_name: string;
  payee_role: string;
  allocation_percentage: number;
  bank_account_reference: string | null;
  tax_identifier: string | null;
  is_active: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
}

export interface Tax1099ThresholdResponse {
  wallet_id: string;
  subject_id: string;
  study_id: string;
  taxable_ytd: number;
  threshold_limit?: number;
  threshold_percentage: number;
  is_threshold_exceeded: boolean;
  w9_status: string;
  w9_required: boolean;
  warning_level: string;
  currency?: string;
}

export interface VisitVerificationEventDto {
  study_id: string;
  site_id: string;
  subject_id: string;
  visit_code: string;
  event_id: string;
  verified_by?: string;
  verification_timestamp?: string | null;
}

export interface W9SubmitRequest {
  tax_id_last_four: string;
  full_legal_name: string;
  certify_accurate?: boolean;
  address?: string | null;
}

export interface W9SubmitResponse {
  wallet_id: string;
  subject_id: string;
  w9_status: string;
  tax_id_last_four: string;
  submitted_at: string;
  message: string;
}

export interface W9VerifyRequest {
  notes?: string | null;
}

export type ActionItemStatus =
  "CANCELLED" | "COMPLETED" | "IN_PROGRESS" | "OPEN";

export interface AuditCreate {
  audit_number: string;
  study_id: string;
  site_id?: string | null;
  vendor_name?: string | null;
  audit_type?: string;
  lead_auditor: string;
  planned_start_date: string;
  planned_end_date: string;
  scope_summary: string;
}

export interface AuditEventPayload {
  entry_id: string;
  subject_id: string;
  site_id: string;
  user_id: string;
  timestamp: string;
  assessment_type: string;
  procedure_pair?: string | null;
  elapsed_time_seconds?: number | null;
}

export interface AuditFindingCreate {
  finding_number: string;
  severity?: string;
  category: string;
  condition: string;
  criteria: string;
  cause: string;
  effect: string;
}

export interface AuditFindingResponse {
  id: string;
  audit_id: string;
  finding_number: string;
  severity: string;
  category: string;
  condition: string;
  criteria: string;
  cause: string;
  effect: string;
  capa_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface AuditResponse {
  id: string;
  audit_number: string;
  study_id: string;
  site_id?: string | null;
  vendor_name?: string | null;
  audit_type: string;
  lead_auditor: string;
  planned_start_date: string;
  planned_end_date: string;
  actual_start_date?: string | null;
  actual_end_date?: string | null;
  status: string;
  scope_summary: string;
  findings?: AuditFindingResponse[];
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type AuditStatus =
  | "CANCELLED"
  | "CAPA_PENDING"
  | "COMPLETED"
  | "FINDINGS_REPORTED"
  | "IN_PROGRESS"
  | "PLANNED";

export interface AuditStatusUpdate {
  status: AuditStatus;
  actual_start_date?: string | null;
  actual_end_date?: string | null;
}

export type BenfordConformity =
  | "ACCEPTABLE_CONFORMITY"
  | "CLOSE_CONFORMITY"
  | "MARGINAL_CONFORMITY"
  | "NON_CONFORMING";

export interface BenfordDigitDistributionResponse {
  sample_size: number;
  chi_square: number;
  p_value: number;
  mad: number;
  conformity: BenfordConformity;
  observed_frequencies: Record<string, any>;
  expected_frequencies: Record<string, any>;
}

export type BreachStatus =
  | "AUTHORITY_NOTIFIED"
  | "CONFIRMED_BREACH"
  | "DISMISSED"
  | "RESOLVED"
  | "UNDER_EVALUATION";

export interface CAPAActionItemCreate {
  title: string;
  description: string;
  action_type?: string;
  assigned_to: string;
  due_date?: string | null;
}

export interface CAPAActionItemResponse {
  id: string;
  capa_id: string;
  title: string;
  description: string;
  action_type: string;
  assigned_to: string;
  due_date?: string | null;
  status: string;
  completed_at?: string | null;
  evidence_url?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface CAPAActionItemUpdate {
  status: ActionItemStatus;
  evidence_url?: string | null;
}

export interface CAPACreate {
  deviation_id: string;
  rca_id?: string | null;
  capa_type?: string;
  action_plan: string;
  preventive_measures?: string | null;
  risk_level?: string;
  lead_investigator_id?: string | null;
  target_completion_date?: string | null;
  effectiveness_interval_days?: number;
  audit_finding_id?: string | null;
}

export interface CAPAEffectivenessCheckCreate {
  planned_date: string;
  metric_evaluated: string;
  baseline_value: string;
  target_value: string;
  actual_value: string;
  outcome: EffectivenessOutcome;
  comments?: string | null;
}

export interface CAPAEffectivenessCheckResponse {
  id: string;
  capa_id: string;
  planned_date: string;
  executed_date?: string | null;
  metric_evaluated: string;
  baseline_value: string;
  target_value: string;
  actual_value?: string | null;
  outcome: string;
  evaluator_id?: string | null;
  comments?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface CAPAResponse {
  id: string;
  deviation_id: string;
  rca_id?: string | null;
  capa_type: string;
  action_plan: string;
  status: CAPAStatus;
  preventive_measures?: string | null;
  risk_level?: string;
  lead_investigator_id?: string | null;
  qa_approver_id?: string | null;
  target_completion_date?: string | null;
  actual_completion_date?: string | null;
  effectiveness_interval_days?: number;
  effectiveness_review_date?: string | null;
  effectiveness_outcome?: string;
  recurrence_detected?: boolean;
  audit_finding_id?: string | null;
  study_id: string;
  site_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
  action_items?: CAPAActionItemResponse[];
  effectiveness_checks?: CAPAEffectivenessCheckResponse[];
}

export type CAPAStatus =
  | "INITIATED"
  | "UNDER_REVIEW"
  | "APPROVED"
  | "IMPLEMENTATION"
  | "IMPLEMENTATION_VERIFIED"
  | "EFFECTIVENESS_CHECK"
  | "CLOSED"
  | "INEFFECTIVE"
  | "CANCELLED";

export interface CAPATransitionRequest {
  to_status: CAPAStatus;
  version_index?: number | null;
}

export interface CAPAUpdate {
  action_plan?: string | null;
  preventive_measures?: string | null;
  risk_level?: string | null;
  target_completion_date?: string | null;
  effectiveness_interval_days?: number | null;
  version_index?: number | null;
}

export interface CSMDossierResponse {
  id: string;
  study_id: string;
  dossier_name: string;
  generated_at: string;
  generated_by: string;
  status: string;
  ract_summary: Record<string, any>;
  kri_surveillance_summary: Record<string, any>;
  site_risk_rankings: Record<string, any>[];
  anomaly_forensics_summary: Record<string, any>;
  electronic_signature: Record<string, any>;
  audit_trail_snapshot: Record<string, any>[];
  merkle_seal?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface CompileCSMDossierRequest {
  study_id: string;
  dossier_name?: string | null;
  signer_name: string;
  signer_role: string;
  signature_meaning?: string;
}

export interface CtQFactorCreate {
  study_id: string;
  category?: string;
  critical_aspect: string;
  risk_description: string;
  impact_area: string;
  mitigation_strategy: string;
}

export interface CtQFactorResponse {
  id: string;
  study_id: string;
  category: string;
  critical_aspect: string;
  risk_description: string;
  impact_area: string;
  mitigation_strategy: string;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface DataIntegrityComprehensiveScanRequest {
  study_id: string;
  subjects?: SubjectClinicalVectorPayload[] | null;
  audit_events?: AuditEventPayload[] | null;
  image_attachments?: ImageAttachmentPayload[] | null;
  auto_dispatch_investigation?: boolean;
}

export interface DataIntegrityFindingResponse {
  id: string;
  run_id: string;
  study_id: string;
  sentinel_type: DataIntegritySentinelType;
  severity: DeviationSeverity;
  anomaly_flag: ForensicsAnomalyFlag;
  primary_subject_id: string;
  primary_site_id: string;
  matched_subject_id?: string | null;
  matched_site_id?: string | null;
  confidence_score: number;
  similarity_score: number;
  hamming_distance?: number | null;
  transformation_type?: ImageTransformationType | null;
  evidence_summary: string;
  forensic_metadata: Record<string, any>;
  investigation_deviation_id?: string | null;
  kri_evaluation_id?: string | null;
  status: DataIntegrityFindingStatus;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type DataIntegrityFindingStatus =
  "CONFIRMED" | "DISMISSED" | "INVESTIGATING" | "OPEN";

export interface DataIntegrityInvestigationDispatchRequest {
  finding_id: string;
  rationale?: string;
}

export interface DataIntegritySentinelRunResponse {
  id: string;
  study_id: string;
  run_name: string;
  scan_timestamp: string;
  sentinel_types: string[];
  total_subjects_scanned: number;
  total_images_scanned: number;
  total_events_scanned: number;
  anomalies_detected_count: number;
  risk_tier: RiskTier;
  status: string;
  summary_findings: Record<string, any>;
  findings: DataIntegrityFindingResponse[];
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type DataIntegritySentinelType =
  | "COMPREHENSIVE_INTEGRITY_SCAN"
  | "DUPLICATE_PATIENT_RECORD"
  | "PERCEPTUAL_IMAGE_HASH"
  | "TEMPORAL_BEHAVIORAL_ANOMALY";

export interface DataIntegritySimulationRequest {
  study_id?: string;
  num_subjects_per_site?: number;
  include_duplicate_patient?: boolean;
  include_temporal_burst?: boolean;
  include_identical_intervals?: boolean;
  include_image_duplication?: boolean;
}

export interface DataIntegritySimulationResponse {
  study_id: string;
  subjects_count: number;
  audit_events_count: number;
  image_attachments_count: number;
  simulated_scenarios: string[];
  sample_subjects: Record<string, any>[];
  sample_audit_events: Record<string, any>[];
  sample_image_attachments: Record<string, any>[];
}

export interface DeviationCreate {
  study_id: string;
  site_id?: string | null;
  title: string;
  description: string;
  severity: DeviationSeverity;
  type: DeviationType;
  category?: string | null;
  is_protocol_violation?: boolean;
  impact_safety?: boolean;
  impact_data?: boolean;
  impact_compliance?: boolean;
  source_system?: string;
  source_reference_id?: string | null;
}

export interface DeviationIngestRequest {
  study_id: string;
  site_id?: string | null;
  title: string;
  description: string;
  severity?: DeviationSeverity;
  type?: DeviationType;
  category?: string | null;
  is_protocol_violation?: boolean;
  impact_safety?: boolean;
  impact_data?: boolean;
  impact_compliance?: boolean;
  source_system: string;
  source_reference_id?: string | null;
}

export interface DeviationResponse {
  id: string;
  study_id: string;
  site_id?: string | null;
  title: string;
  description: string;
  severity: DeviationSeverity;
  status: DeviationStatus;
  type: DeviationType;
  category?: string | null;
  is_protocol_violation: boolean;
  impact_safety?: boolean;
  impact_data?: boolean;
  impact_compliance?: boolean;
  source_system?: string;
  source_reference_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export type DeviationSeverity =
  "MINOR" | "MAJOR" | "CRITICAL" | "SYSTEMATIC_TREND";

export type DeviationStatus =
  | "REPORTED"
  | "UNDER_INVESTIGATION"
  | "RCA_COMPLETE"
  | "CAPA_INITIATED"
  | "RESOLVED"
  | "CLOSED";

export type DeviationType =
  | "CONCOMITANT_MED"
  | "ELIGIBILITY"
  | "GCP_COMPLIANCE"
  | "INFORMED_CONSENT"
  | "INVESTIGATIONAL_PRODUCT"
  | "IP_MANAGEMENT"
  | "LAB_SPECIMEN"
  | "OTHER"
  | "PROTOCOL_PROCEDURE"
  | "SAFETY_REPORTING"
  | "VISIT_WINDOW";

export interface DuplicatePatientScanRequest {
  study_id: string;
  subjects?: SubjectClinicalVectorPayload[] | null;
  raw_subjects?: Record<string, any>[] | null;
  similarity_threshold?: number;
  auto_dispatch_investigation?: boolean;
}

export type EffectivenessOutcome =
  "EFFECTIVE" | "INEFFECTIVE" | "PARTIALLY_EFFECTIVE" | "PENDING";

export type ForensicsAnomalyFlag =
  | "ANOMALOUS_VARIANCE_SHRINKAGE"
  | "BENFORD_NON_CONFORMANCE"
  | "CROSS_SITE_CLONING_SUSPICION"
  | "DATA_FABRICATION_SUSPICION"
  | "DUPLICATE_PATIENT_RECORD"
  | "IDENTICAL_PROCEDURE_INTERVALS"
  | "MULTIVARIATE_MAHALANOBIS_OUTLIER"
  | "OFF_HOURS_BURST_ANOMALY"
  | "PERCEPTUAL_IMAGE_HASH_MATCH"
  | "RAPID_BURST_SUBMISSION"
  | "SUSPICIOUS_TIMESTAMP_CLUSTER"
  | "TERMINAL_DIGIT_ROUNDING_BIAS";

export interface ForensicsSimulationRequest {
  study_id?: string;
  num_normal_sites?: number;
  include_benford_anomaly?: boolean;
  include_rounding_bias?: boolean;
  include_variance_shrinkage?: boolean;
  include_multivariate_outlier?: boolean;
  samples_per_site?: number;
}

export interface ForensicsSimulationResponse {
  study_id: string;
  target_variables: string[];
  site_data: Record<string, any>;
  total_sites: number;
  total_observations: number;
  simulated_anomalies: string[];
}

export interface ImageAttachmentPayload {
  image_id: string;
  subject_id: string;
  site_id: string;
  study_id: string;
  visit_name?: string;
  modality?: string;
  pixel_matrix?: number[][] | null;
  base64_data?: string | null;
  phash?: string | null;
}

export interface ImageHashScanRequest {
  study_id: string;
  image_attachments?: ImageAttachmentPayload[] | null;
  raw_images?: Record<string, any>[] | null;
  hamming_threshold?: number;
  auto_dispatch_investigation?: boolean;
}

export type ImageTransformationType =
  | "BRIGHTNESS_ADJUSTED"
  | "CROPPED_OR_RESCALED"
  | "EXACT"
  | "FLIPPED_HORIZONTAL"
  | "FLIPPED_VERTICAL"
  | "ROTATED_180"
  | "ROTATED_270"
  | "ROTATED_90";

export interface KRIBatchEvaluationRequest {
  study_id: string;
  kri_code: string;
  site_raw_values: Record<string, any>;
}

export interface KRIDefinitionCreate {
  code: string;
  name: string;
  category?: string;
  description: string;
  calculation_formula: string;
  green_threshold?: number;
  amber_threshold?: number;
  red_threshold?: number;
  weight?: number;
}

export interface KRIDefinitionResponse {
  id: string;
  code: string;
  name: string;
  category: string;
  description: string;
  calculation_formula: string;
  green_threshold: number;
  amber_threshold: number;
  red_threshold: number;
  weight: number;
  is_active: boolean;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface KRIMetricEvaluationResponse {
  id: string;
  study_id: string;
  site_id: string;
  kri_code: string;
  evaluation_date: string;
  raw_value: number;
  standardized_z_score: number;
  risk_tier: string;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface MahalanobisMetricsResponse {
  mahalanobis_distance: number;
  mahalanobis_p_value: number;
  correlation_matrix_distance: number;
  site_mean_vector: Record<string, any>;
  site_covariance_matrix: Record<string, any>;
}

export interface PromoteFindingToCAPARequest {
  action_plan: string;
  preventive_measures?: string | null;
  target_completion_date?: string | null;
}

export interface QTLBreachEventResponse {
  id: string;
  qtl_id: string;
  study_id: string;
  breach_date: string;
  observed_value: number;
  threshold_value: number;
  root_cause: string;
  corrective_action_summary: string;
  csr_narrative: string;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface QTLCreate {
  study_id: string;
  parameter_name: string;
  target_value: number;
  tolerance_limit: number;
  unit?: string;
}

export interface QTLEvaluateBreachRequest {
  observed_value: number;
  root_cause: string;
  corrective_action_summary: string;
}

export interface QTLResponse {
  id: string;
  study_id: string;
  parameter_name: string;
  target_value: number;
  tolerance_limit: number;
  unit: string;
  is_breached: boolean;
  breach_count: number;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface QualitySurveillanceRunRequest {
  study_id: string;
  run_name?: string | null;
  auto_dispatch_deviations?: boolean;
  target_variables?: string[] | null;
  min_subjects_power?: number;
  min_observations_power?: number;
  similarity_threshold?: number;
  hamming_threshold?: number;
}

export interface QualitySurveillanceRunResponse {
  run_id: string;
  study_id: string;
  run_name: string;
  timestamp: string;
  total_sites: number;
  gated_sites_count: number;
  analyzed_sites_count: number;
  total_subjects: number;
  total_observations: number;
  total_images_scanned: number;
  total_events_scanned: number;
  anomalies_count: number;
  deviations_dispatched_count: number;
  status: string;
  site_summaries: SiteSurveillanceSummaryResponse[];
  findings: Record<string, any>[];
  deviations: Record<string, any>[];
  forensics_run_id?: string | null;
  sentinel_run_id?: string | null;
}

export type RACTCategory =
  | "SAFETY"
  | "CONSENT"
  | "STUDY_CONDUCT"
  | "DATA_INTEGRITY"
  | "IP_MANAGEMENT"
  | "TECHNOLOGY";

export interface RACTCategorySummary {
  category: string;
  total_factors: number;
  high_count: number;
  medium_count: number;
  low_count: number;
  average_score: number;
  linked_kri_count: number;
}

export interface RACTMatrixSummaryResponse {
  study_id: string;
  total_risk_factors: number;
  high_risk_count: number;
  medium_risk_count: number;
  low_risk_count: number;
  overall_average_score: number;
  linked_kri_coverage_percentage: number;
  category_summaries: RACTCategorySummary[];
  risk_factors: RACTRiskFactorResponse[];
}

export interface RACTRiskFactorCreate {
  study_id: string;
  category: RACTCategory;
  risk_topic: string;
  risk_description: string;
  probability_score: number;
  impact_score: number;
  detectability_score: number;
  mitigation_strategy: string;
  linked_kri_code?: string | null;
}

export interface RACTRiskFactorResponse {
  id: string;
  study_id: string;
  category: string;
  risk_topic: string;
  risk_description: string;
  probability_score: number;
  impact_score: number;
  detectability_score: number;
  total_risk_score: number;
  risk_tier: string;
  mitigation_strategy: string;
  linked_kri_code?: string | null;
  is_active?: boolean;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface RACTRiskFactorUpdate {
  risk_topic?: string | null;
  risk_description?: string | null;
  probability_score?: number | null;
  impact_score?: number | null;
  detectability_score?: number | null;
  mitigation_strategy?: string | null;
  linked_kri_code?: string | null;
  is_active?: boolean | null;
}

export interface RCACreateOrUpdate {
  methodology?: string;
  investigation_details: string;
  root_cause_summary: string;
  five_whys_chain?: Record<string, any> | any[] | null;
  fishbone_categories?: Record<string, any> | null;
  contributing_factors?: any[] | null;
  version_index?: number | null;
}

export interface RCAResponse {
  id: string;
  deviation_id: string;
  methodology: string;
  investigation_details: string;
  root_cause_summary: string;
  five_whys_chain?: Record<string, any> | any[] | null;
  fishbone_categories?: Record<string, any> | null;
  contributing_factors?: any[] | null;
  study_id: string;
  site_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface RegulatoryClockStatusResponse {
  breach_id: string;
  study_id: string;
  status: string;
  reporting_deadline?: string | null;
  regulatory_clock_hours_remaining: number;
  is_approaching_deadline: boolean;
  is_overdue: boolean;
  affected_authorities?: any[] | null;
}

export type RiskTier = "CRITICAL" | "HIGH" | "LOW" | "MEDIUM";

export type RoundingBiasTier =
  "MODERATE_ROUNDING" | "NORMAL" | "SEVERE_ROUNDING";

export interface SeriousBreachConfirmRequest {
  affected_authorities: string[];
}

export interface SeriousBreachReportRequest {
  study_id: string;
  site_id?: string | null;
  title: string;
  summary: string;
  event_date: string;
  discovery_date: string;
  affected_authorities?: string[];
}

export interface SeriousBreachResponse {
  id: string;
  study_id: string;
  site_id?: string | null;
  title: string;
  summary: string;
  event_date: string;
  discovery_date: string;
  confirmation_date?: string | null;
  reporting_deadline?: string | null;
  affected_authorities?: any[] | null;
  status: string;
  regulatory_clock_hours_remaining?: number;
  lead_qa_id: string;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface SeriousBreachStatusUpdate {
  status: BreachStatus;
}

export interface SiteForensicsResponse {
  id: string;
  run_id: string;
  study_id: string;
  site_id: string;
  sample_size: number;
  benford: BenfordDigitDistributionResponse;
  terminal_digit: TerminalDigitDistributionResponse;
  multivariate: MahalanobisMetricsResponse;
  variance_shrinkage: VarianceShrinkageMetricsResponse;
  composite_risk_score: number;
  risk_tier: RiskTier;
  is_anomalous_outlier: boolean;
  flagged_anomaly_reasons: string[];
  kri_evaluation_id?: string | null;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface SiteObservationPayload {
  site_id: string;
  observations: Record<string, any>[];
}

export interface SiteRiskProfileResponse {
  id: string;
  study_id: string;
  site_id: string;
  evaluation_date: string;
  composite_risk_score: number;
  risk_rank: number;
  percentile_rank?: number;
  risk_tier?: string;
  high_risk_kri_count: number;
  active_deviations_count: number;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface SiteSurveillanceSummaryResponse {
  site_id: string;
  sample_size: number;
  observation_count: number;
  power_status: string;
  is_sample_gated: boolean;
  composite_risk_score: number;
  risk_tier: string;
  anomalies_detected: string[];
  deviations_created: string[];
  kri_alerts_triggered: string[];
  details: Record<string, any>;
}

export interface StatisticalForensicsAnalyzeRequest {
  study_id: string;
  target_variables?: string[];
  site_observations?: Record<string, any> | null;
  site_data?: SiteObservationPayload[] | null;
  run_name?: string | null;
  auto_trigger_kri?: boolean;
}

export interface StatisticalForensicsRunResponse {
  id: string;
  study_id: string;
  run_name: string;
  analysis_timestamp: string;
  target_variables: string[];
  total_sites: number;
  total_observations: number;
  anomalous_sites_count: number;
  status: string;
  site_results: SiteForensicsResponse[];
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

export interface SubjectClinicalVectorPayload {
  subject_id: string;
  site_id: string;
  study_id: string;
  demographics?: Record<string, any>;
  vital_signs?: Record<string, any>;
  ecg_intervals?: Record<string, any>;
  lab_panel?: Record<string, any>;
  medical_history?: string[];
}

export interface TemporalAnomalyScanRequest {
  study_id: string;
  audit_events?: AuditEventPayload[] | null;
  raw_events?: Record<string, any>[] | null;
  work_hour_start?: number;
  work_hour_end?: number;
  auto_dispatch_investigation?: boolean;
}

export interface TerminalDigitDistributionResponse {
  sample_size: number;
  chi_square: number;
  p_value: number;
  zero_five_index: number;
  rounding_bias_tier: RoundingBiasTier;
  observed_frequencies: Record<string, any>;
  expected_frequencies: Record<string, any>;
}

export interface VarianceShrinkageMetricsResponse {
  variance_ratio: number;
  variance_shrinkage_p_value: number;
  kl_divergence: number;
  is_variance_shrunk: boolean;
}

export type AIReviewStatus =
  "DRAFT_AI" | "PENDING_REVIEW" | "APPROVED" | "REJECTED";

export type AS2Authority =
  "FDA_ESG" | "EMA_GATEWAY" | "PMDA" | "MHRA" | "HEALTH_CANADA";

export interface AS2OutboundTransmissionRequestDTO {
  case_id?: string | null;
  study_id?: string | null;
  icsr_xml_payload: string;
  target_authority: AS2Authority;
  sender_as2_id?: string;
  receiver_as2_id?: string | null;
  environment?: string;
  custom_headers?: Record<string, any>;
}

export interface AS2TransmissionAuditLogDTO {
  id: string;
  transmission_id: string;
  action: string;
  from_status?: string | null;
  to_status?: string | null;
  details: string;
  created_at: string;
  created_by: string;
  reason_for_change?: string | null;
  version_index: number;
}

export interface AS2TransmissionResponseDTO {
  id: string;
  transmission_id: string;
  case_id?: string | null;
  study_id?: string | null;
  worldwide_unique_case_id?: string | null;
  message_id: string;
  as2_from: string;
  as2_to: string;
  target_authority: string;
  environment: string;
  status: string;
  payload_digest_sha256: string;
  mic: string;
  encryption_algorithm: string;
  signature_algorithm: string;
  outbound_payload_size: number;
  outbound_xml?: string | null;
  mdn_message_id?: string | null;
  mdn_disposition?: string | null;
  mdn_mic?: string | null;
  mdn_status?: string | null;
  mdn_received_at?: string | null;
  ack_a_status?: string | null;
  ack_a_received_at?: string | null;
  ack_b_status?: string | null;
  ack_b_received_at?: string | null;
  ack_b_errors?: Record<string, any>[];
  ack_c_status?: string | null;
  ack_c_received_at?: string | null;
  ack_c_details?: Record<string, any>;
  error_message?: string | null;
  retry_count?: number;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  audit_events?: AS2TransmissionAuditLogDTO[];
}

export type AckType = "ACK_A" | "ACK_B" | "ACK_C";

export interface AuthorityRuleEvaluationResult {
  authority: ReportingAuthority;
  country_code: string;
  is_expedited: boolean;
  timeframe_days?: number | null;
  due_date?: string | null;
  rule_name: string;
  statutory_citation: string;
  classification: ExpeditedClassification;
  rationale: string;
  countdown?: CountdownClockStatus | null;
}

export type BenchmarkReferenceDataset =
  "INTERNAL_TRIAL_POOL" | "FAERS" | "EUDRAVIGILANCE" | "CUSTOM";

export type CausalityCategory =
  | "RELATED"
  | "SUSPECTED"
  | "POSSIBLY_RELATED"
  | "PROBABLY_RELATED"
  | "DEFINITELY_RELATED"
  | "NOT_RELATED"
  | "UNLIKELY";

export interface ClinicalTimelineEvent {
  event_id: string;
  event_type: TimelineEventType;
  event_date?: string | null;
  title: string;
  description: string;
  domain?: string | null;
  sequence?: number | null;
  source_record_id?: string | null;
  details?: Record<string, any>;
}

export type ClockAlertLevel = "NORMAL" | "WARNING" | "CRITICAL" | "OVERDUE";

export interface CountdownClockStatus {
  clock_start_date: string;
  clock_due_date: string;
  total_hours: number;
  elapsed_hours: number;
  elapsed_percentage: number;
  remaining_hours: number;
  is_overdue?: boolean;
  alert_level?: ClockAlertLevel;
}

export interface DSURLineListingItem {
  case_id: string;
  subject_id: string;
  study_id: string;
  treatment_arm?: string;
  dosage_cohort?: string | null;
  age?: number | null;
  sex?: string | null;
  country?: string;
  meddra_pt: string;
  meddra_soc: string;
  ae_term: string;
  onset_date: string;
  resolution_date?: string | null;
  outcome?: string;
  seriousness_death?: boolean;
  seriousness_life_threatening?: boolean;
  seriousness_hospitalization?: boolean;
  seriousness_disability?: boolean;
  seriousness_congenital_anomaly?: boolean;
  seriousness_other?: boolean;
  causality?: string;
  expectedness?: string;
  susar?: boolean;
  is_aesi?: boolean;
  aesi_category?: string | null;
  action_taken?: string;
}

export interface DSURSummaryTabulationRow {
  meddra_soc: string;
  meddra_pt: string;
  is_aesi?: boolean;
  aesi_category?: string | null;
  is_fatal?: boolean;
  is_susar?: boolean;
  active_arm_count?: number;
  active_arm_subjects?: number;
  comparator_arm_count?: number;
  comparator_arm_subjects?: number;
  placebo_arm_count?: number;
  placebo_arm_subjects?: number;
  total_event_count?: number;
  total_subjects_count?: number;
  cumulative_event_count?: number;
  cumulative_subjects_count?: number;
  rsi_listed?: boolean;
  rsi_expected_rate?: number | null;
  observed_rate_pct?: number;
  observed_incidence_rate_pyr?: number;
  rsi_rate_ratio?: number | null;
  rsi_threshold_breached?: boolean;
}

export interface DeviceMedWatchReportResponseDTO {
  case_number: string;
  report_type?: string;
  reporting_authority?: string;
  device_name: string;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  classification: string;
  defect_type: string;
  is_5_day_expedited: boolean;
  incident_summary: string;
  vigilance_deadline_days: number;
  vigilance_due_date: string;
  payload_json: Record<string, any>;
  payload_xml: string;
}

export interface DeviceSafetyInvestigationAdjudicateDTO {
  classification: string;
  causality: string;
  is_usade?: boolean;
  investigation_notes?: string | null;
  root_cause_summary?: string | null;
  capa_required?: boolean;
  capa_reference?: string | null;
  fsca_required?: boolean;
  signature_token: string;
  reason_for_change?: string;
}

export interface DeviceSafetyInvestigationCreateDTO {
  case_number?: string | null;
  study_id: string;
  site_id: string;
  subject_id?: string | null;
  deficiency_id: string;
  device_name?: string;
  device_udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  classification?: string;
  causality?: string;
  is_usade?: boolean;
  investigation_notes?: string | null;
  reason_for_change?: string;
}

export interface DeviceSafetyInvestigationResponseDTO {
  id: string;
  case_number: string;
  study_id: string;
  site_id: string;
  subject_id?: string | null;
  deficiency_id: string;
  device_name: string;
  device_udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  classification: string;
  causality: string;
  is_usade: boolean;
  vigilance_reporting_required: boolean;
  vigilance_deadline_days: number;
  vigilance_due_date: string;
  days_remaining: number;
  is_overdue: boolean;
  notified_body_notified: boolean;
  competent_authority_notified: boolean;
  status: string;
  investigation_notes?: string | null;
  root_cause_summary?: string | null;
  capa_required: boolean;
  capa_reference?: string | null;
  fsca_required: boolean;
  safety_physician_user_id?: string | null;
  adjudicated_at?: string | null;
  esignature_manifest_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface DeviceVigilanceReportResponseDTO {
  case_number: string;
  report_type?: string;
  reporting_authority?: string;
  device_name: string;
  udi_di: string;
  lot_number: string;
  serial_number?: string | null;
  classification: string;
  incident_summary: string;
  vigilance_deadline_days: number;
  vigilance_due_date: string;
  payload_json: Record<string, any>;
  payload_xml: string;
}

export interface DisproportionalityMetricResult {
  drug_name: string;
  meddra_pt: string;
  meddra_soc?: string | null;
  term_level?: StratificationLevel;
  case_count_a: number;
  case_count_b?: number;
  case_count_c?: number;
  case_count_d?: number;
  total_n?: number;
  expected_count_e: number;
  prr: number;
  prr_ci_lower: number;
  prr_ci_upper: number;
  chi_square_yates: number;
  ror: number;
  ror_ci_lower: number;
  ror_ci_upper: number;
  ebgm: number;
  eb05: number;
  eb95: number;
  is_prr_signal?: boolean;
  is_ror_signal?: boolean;
  is_ebgm_signal?: boolean;
  overall_signal_flag?: boolean;
  signal_strength?: SignalStrength;
  stratum?: string | null;
  benchmark_dataset?: string | null;
  benchmark_relative_risk?: number | null;
}

export type ExpectednessCategory = "EXPECTED" | "UNEXPECTED";

export type ExpeditedClassification =
  | "SUSAR_FATAL_LIFE_THREATENING_7_DAY"
  | "SUSAR_SERIOUS_UNEXPECTED_15_DAY"
  | "NON_EXPEDITED_PERIODIC"
  | "EXEMPT";

export type ExposureStratificationCategory =
  "TREATMENT_ARM" | "DOSAGE_COHORT" | "AGE_GROUP" | "SEX" | "OVERALL";

export interface ExposureStratificationRow {
  category: ExposureStratificationCategory;
  group_name: string;
  subject_count?: number;
  interval_patient_years?: number;
  cumulative_patient_years?: number;
  mean_duration_days?: number;
  median_duration_days?: number;
}

export interface GroundedClaim {
  claim_id: string;
  sentence_text: string;
  section_type: NarrativeSectionType;
  grounded_event_ids?: string[];
  confidence_score?: number;
}

export type ICSRApprovalStatus =
  "DRAFT" | "PHYSICIAN_APPROVED" | "TRANSMITTED" | "REJECTED";

export interface ICSRDataExportRequest {
  job_name: string;
  icsr: IndividualCaseSafetyReport;
}

export interface ICSRHeader {
  sender_organization: string;
  receiver_organization: string;
  message_type?: string;
  transmission_date: string;
  message_id: string;
}

export interface ICSRPatient {
  patient_id: string;
  sex: string;
  age?: number | null;
  age_unit?: string | null;
  birth_date?: string | null;
}

export interface ICSRReactionEvent {
  reaction_term: string;
  meddra_coding?: MedDRACoding | null;
  start_date?: string | null;
  end_date?: string | null;
  outcome?: string | null;
  seriousness_death?: string;
  seriousness_life_threatening?: string;
  seriousness_hospitalization?: string;
  seriousness_disability?: string;
  seriousness_congenital_anomaly?: string;
  seriousness_other_medically_important?: string;
}

export interface ICSRReportIdentifiers {
  worldwide_unique_case_id: string;
  local_report_id?: string | null;
  first_sender_type?: string | null;
}

export interface ICSRRuleEvaluationInput {
  case_id: string;
  study_id: string;
  subject_id: string;
  receipt_date: string;
  seriousness_death?: boolean;
  seriousness_life_threatening?: boolean;
  seriousness_hospitalization?: boolean;
  seriousness_disability?: boolean;
  seriousness_congenital_anomaly?: boolean;
  seriousness_other?: boolean;
  causality?: CausalityCategory;
  expectedness?: ExpectednessCategory;
  target_authorities?: ReportingAuthority[] | null;
  reason_for_change?: string | null;
}

export interface ICSRRuleEvaluationSummary {
  id: string;
  case_id: string;
  study_id: string;
  subject_id: string;
  receipt_date: string;
  is_serious: boolean;
  is_unexpected: boolean;
  is_causal: boolean;
  is_susar: boolean;
  any_expedited: boolean;
  shortest_timeframe_days?: number | null;
  overall_alert_level?: ClockAlertLevel;
  evaluations?: AuthorityRuleEvaluationResult[];
  approval_status?: ICSRApprovalStatus;
  approved_by_user_id?: string | null;
  approved_at?: string | null;
  esignature_manifest_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
}

export interface ICSRSuspectDrug {
  drug_name: string;
  active_substance_name?: string | null;
  dosage_text?: string | null;
  route_of_administration?: string | null;
  action_taken_with_drug?: string | null;
  drug_role?: string;
}

export interface IndividualCaseSafetyReport {
  version_index?: number;
  reason_for_change?: string | null;
  header: ICSRHeader;
  report_identifiers: ICSRReportIdentifiers;
  patient: ICSRPatient;
  reactions?: ICSRReactionEvent[];
  suspect_drugs?: ICSRSuspectDrug[];
}

export interface MDNProcessRequestDTO {
  mdn_payload: string;
  authority_cert_pem?: string | null;
}

export interface MDNProcessResponseDTO {
  is_valid_mdn: boolean;
  original_message_id?: string | null;
  disposition_mode?: string | null;
  disposition_type: string;
  disposition_raw?: string | null;
  received_content_mic?: string | null;
  mic_matches?: boolean | null;
  is_signed?: boolean;
  signature_valid?: boolean | null;
  human_readable_text?: string | null;
  error_details?: string | null;
}

export interface MGPSMultiItemResult {
  item_set: string[];
  observed_n: number;
  expected_e: number;
  ebgm: number;
  eb05: number;
  eb95: number;
  interaction_score: number;
  is_interaction_signal?: boolean;
}

export interface MedDRACoding {
  llt_code: string;
  llt_name: string;
  pt_code: string;
  pt_name: string;
  hlt_code: string;
  hlt_name: string;
  hlgt_code: string;
  hlgt_name: string;
  soc_code: string;
  soc_name: string;
  primary_soc_flag?: string | null;
  score?: number;
}

export interface NarrativeGenerateRequest {
  study_id: string;
  subject_id: string;
  sae_event_key: string;
  worldwide_unique_case_id?: string | null;
  reason_for_change: string;
  additional_context?: string | null;
}

export type NarrativeSectionType =
  | "DEMOGRAPHICS_BASELINE"
  | "MEDICAL_TREATMENT_HISTORY"
  | "INDEX_AE_CHRONOLOGY"
  | "DIAGNOSTIC_LABS"
  | "CLINICAL_MANAGEMENT"
  | "OUTCOME_CAUSALITY";

export interface NarrativeSignRequest {
  narrative_id: string;
  reason_for_change: string;
  signature_secret?: string | null;
}

export interface NarrativeSignResponse {
  narrative_id: string;
  review_status?: AIReviewStatus;
  approved_by_user_id: string;
  approved_at: string;
  esignature_manifest_id: string;
  message: string;
}

export interface PeriodicReportCompilationRequest {
  report_type?: PeriodicSafetyReportType;
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  sponsor_name?: string;
  include_line_listings?: boolean;
  include_summary_tabulations?: boolean;
  include_signal_analyses?: boolean;
  benefit_risk_narrative?: string | null;
  cumulative_exposure_subjects?: number;
  rsi_config?: ReferenceSafetyInformationConfig | null;
  aesi_terms?: string[] | null;
  cases?: Record<string, any>[] | null;
  subjects_exposure_data?: Record<string, any>[] | null;
  auto_lock_snapshot?: boolean;
}

export interface PeriodicReportPayload {
  report_id: string;
  report_type: PeriodicSafetyReportType;
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  sponsor_name: string;
  snapshot?: RegulatorySnapshot | null;
  exposure_metrics?: SubjectExposureMetrics;
  total_subject_exposure: number;
  total_saes: number;
  total_fatal_cases?: number;
  total_susars?: number;
  total_aesi?: number;
  line_listings?: DSURLineListingItem[];
  summary_tabulations?: DSURSummaryTabulationRow[];
  rsi_comparison_summary?: Record<string, any>[];
  signal_summary?: Record<string, any>[];
  benefit_risk_assessment: string;
  conclusions: string;
  regulatory_guidelines?: string[];
  qppv_signoff?: QPPVSignoffManifest | null;
  xml_submission_package?: string | null;
}

export interface PeriodicReportResponseDTO {
  id: string;
  report_type: string;
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  status: string;
  total_subject_exposure: number;
  total_saes: number;
  total_fatal_cases?: number;
  total_susars?: number;
  total_aesi?: number;
  snapshot_hash?: string | null;
  line_listings_count: number;
  summary_tabulations_count: number;
  signals_included_count: number;
  payload: PeriodicReportPayload;
  approved_by_user_id?: string | null;
  approved_at?: string | null;
  esignature_manifest_id?: string | null;
  qppv_signoff?: QPPVSignoffManifest | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface PeriodicReportSignRequest {
  user_id: string;
  meaning_of_signature?: string;
  signature_secret: string;
  role?: string | null;
  comments?: string | null;
}

export interface PeriodicReportSignResponse {
  report_id: string;
  status: PeriodicReportStatus;
  approved_by_user_id: string;
  approved_at: string;
  esignature_manifest_id: string;
  message: string;
  qppv_manifest?: QPPVSignoffManifest | null;
}

export type PeriodicReportStatus =
  "DRAFT" | "IN_REVIEW" | "APPROVED" | "ARCHIVED";

export type PeriodicSafetyReportType = "DSUR" | "PSUR" | "PBRER";

export interface QPPVSignoffManifest {
  qppv_user_id: string;
  qppv_name: string;
  qppv_role?: string;
  meaning_of_signature?: string;
  signed_at: string;
  manifest_id: string;
  snapshot_hash: string;
  report_hash: string;
  signature_token: string;
  comments?: string | null;
  verification_status?: string;
}

export interface ReferenceSafetyInformationConfig {
  rsi_document_id?: string;
  rsi_version?: string;
  effective_date?: string;
  listed_terms?: ReferenceSafetyInformationTerm[];
}

export interface ReferenceSafetyInformationTerm {
  meddra_pt: string;
  meddra_soc: string;
  is_listed?: boolean;
  expected_frequency_rate?: number;
  severity_grade_threshold?: number;
  rsi_version?: string;
}

export interface RegulatoryAckProcessRequestDTO {
  ack_payload: string;
  ack_type: AckType;
  transmission_id?: string | null;
}

export interface RegulatoryAckProcessResponseDTO {
  ack_type: string;
  ack_status: string;
  is_accepted: boolean;
  authority_receipt_id?: string | null;
  worldwide_unique_case_id?: string | null;
  message_id?: string | null;
  authority_status_code?: string | null;
  errors?: string[];
  warnings?: string[];
  received_at: string;
}

export interface RegulatorySnapshot {
  snapshot_id: string;
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  snapshot_hash: string;
  total_cases_frozen?: number;
  total_subjects_frozen?: number;
  is_locked?: boolean;
  locked_at?: string | null;
  locked_by?: string | null;
  lock_reason?: string | null;
  dataset_manifest?: Record<string, any>;
}

export interface RegulatorySnapshotLockRequest {
  study_id: string;
  compound_name: string;
  reporting_period_start: string;
  reporting_period_end: string;
  dibd_anniversary?: string | null;
  lock_reason?: string;
  cases?: Record<string, any>[] | null;
  subjects_data?: Record<string, any>[] | null;
}

export interface RegulatorySnapshotLockResponse {
  snapshot: RegulatorySnapshot;
}

export interface RegulatorySnapshotVerifyRequest {
  snapshot_id: string;
  snapshot_hash: string;
  dataset_manifest: Record<string, any>;
}

export interface RegulatorySnapshotVerifyResponse {
  snapshot_id: string;
  is_valid: boolean;
  computed_hash: string;
  expected_hash: string;
  match_status: string;
  verified_at: string;
}

export type ReportingAuthority =
  "US_FDA" | "EMA" | "PMDA" | "MHRA" | "HEALTH_CANADA";

export interface SAEDiscrepancyResponse {
  id: string;
  run_id: string;
  source: string;
  case_event_key: string;
  field_name: string;
  expected_value?: string | null;
  actual_value?: string | null;
  meddra_version?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SAEReconciliationJobRequest {
  study_id: string;
}

export interface SAEReconciliationJobResponse {
  id: string;
  study_id: string;
  status: string;
  error_message?: string | null;
  run_id?: string | null;
  result_summary?: Record<string, any> | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SAEReconciliationRunRequest {
  study_id: string;
}

export interface SAEReconciliationRunResponse {
  id: string;
  study_id: string;
  run_date: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
  discrepancies?: SAEDiscrepancyResponse[];
}

export interface SafetyAuditLogResponse {
  id: string;
  created_at: string;
  created_by: string;
  reason_for_change?: string | null;
  version_index: number;
  action: string;
  details: string;
  record_id?: string | null;
}

export interface SafetyCaseICSRCreate {
  worldwide_unique_case_id: string;
  patient_id: string;
  case_data: Record<string, any>;
}

export interface SafetyCaseICSRResponse {
  id: string;
  worldwide_unique_case_id: string;
  patient_id: string;
  case_data: Record<string, any>;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SafetyExportJobCreate {
  job_name: string;
}

export interface SafetyExportJobResponse {
  id: string;
  job_name: string;
  status: string;
  output?: string | null;
  error?: string | null;
  error_message?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SafetyNarrativeDTO {
  created_at?: string;
  created_by: string;
  reason_for_change: string;
  version_index?: number;
  model_identifier: string;
  prompt_hash: string;
  confidence_score: number;
  review_status?: AIReviewStatus;
  approved_by_user_id?: string | null;
  approved_at?: string | null;
  esignature_manifest_id?: string | null;
  id: string;
  study_id: string;
  subject_id: string;
  case_id: string;
  sae_event_key: string;
  title: string;
  sections?: SafetyNarrativeSection[];
  raw_narrative_text: string;
  timeline_events?: ClinicalTimelineEvent[];
  grounded_claims?: GroundedClaim[];
}

export interface SafetyNarrativeSection {
  section_type: NarrativeSectionType;
  section_title: string;
  content: string;
  grounded_claims?: GroundedClaim[];
  order_index?: number;
}

export interface SafetySignalAlertDTO {
  id: string;
  detection_run_id?: string | null;
  study_id?: string | null;
  target_drug: string;
  meddra_term: string;
  term_level: string;
  meddra_soc?: string | null;
  case_count_a: number;
  expected_count_e: number;
  prr: number;
  prr_ci_lower: number;
  prr_ci_upper: number;
  chi_square_yates: number;
  ror: number;
  ror_ci_lower: number;
  ror_ci_upper: number;
  ebgm: number;
  eb05: number;
  eb95: number;
  severity: string;
  status: string;
  triage_summary: string;
  assigned_safety_officer?: string | null;
  review_decision?: string | null;
  review_justification?: string | null;
  clinical_summary?: string | null;
  reviewed_by?: string | null;
  reviewed_at?: string | null;
  esignature_manifest_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface SafetySignalAlertReviewRequest {
  decision: SafetySignalReviewDecision;
  justification: string;
  clinical_summary?: string | null;
  assigned_safety_officer?: string | null;
  recommended_action?: string | null;
}

export interface SafetySignalAlertSignRequest {
  user_id: string;
  meaning_of_signature?: string;
  signature_secret: string;
  decision: SafetySignalReviewDecision;
  justification: string;
  clinical_summary?: string | null;
}

export interface SafetySignalAlertSignResponse {
  alert_id: string;
  status: SafetySignalAlertStatus;
  reviewed_by: string;
  reviewed_at: string;
  esignature_manifest_id: string;
  message: string;
}

export type SafetySignalAlertStatus =
  "TRIGGERED" | "UNDER_REVIEW" | "CONFIRMED_SIGNAL" | "REFUTED" | "CLOSED";

export type SafetySignalReviewDecision =
  "CONFIRM_SIGNAL" | "REFUTE_SIGNAL" | "REQUEST_SAFETY_INQUIRY" | "CLOSE_ALERT";

export interface SignICSRRuleEvaluationRequest {
  reason_for_change: string;
  signature_secret?: string | null;
  meaning_of_signature?: string;
}

export interface SignICSRRuleEvaluationResponse {
  evaluation_id: string;
  approval_status: ICSRApprovalStatus;
  approved_by_user_id: string;
  approved_at: string;
  esignature_manifest_id: string;
  message: string;
}

export interface SignalDetectionScanRequest {
  target_drug: string;
  study_id?: string | null;
  comparator_drugs?: string[];
  benchmark_dataset?: BenchmarkReferenceDataset;
  stratification_levels?: StratificationLevel[];
  min_case_count?: number;
  prr_threshold?: number;
  chi_square_threshold?: number;
  ror_ci_lower_threshold?: number;
  eb05_threshold?: number;
  auto_trigger_alerts?: boolean;
  cases?: Record<string, any>[] | null;
  benchmark_cases?: Record<string, any>[] | null;
}

export interface SignalDetectionScanResponse {
  run_id: string;
  study_id?: string | null;
  target_drug: string;
  benchmark_dataset: string;
  total_cases_analyzed: number;
  total_terms_evaluated: number;
  signals_detected_count: number;
  alerts_triggered_count: number;
  results: DisproportionalityMetricResult[];
  multi_item_results?: MGPSMultiItemResult[];
  created_at: string;
}

export type SignalStrength =
  "CRITICAL" | "STRONG" | "MODERATE" | "WEAK" | "NONE";

export type StratificationLevel = "PT" | "SOC";

export interface SubjectExposureCalculationRequest {
  study_id: string;
  reporting_period_start: string;
  reporting_period_end: string;
  subjects_exposure_data?: Record<string, any>[] | null;
  cases?: Record<string, any>[] | null;
}

export interface SubjectExposureCalculationResponse {
  study_id: string;
  reporting_period_start: string;
  reporting_period_end: string;
  metrics: SubjectExposureMetrics;
}

export interface SubjectExposureMetrics {
  total_cumulative_subjects?: number;
  total_interval_subjects?: number;
  total_cumulative_patient_years?: number;
  total_interval_patient_years?: number;
  by_treatment_arm?: ExposureStratificationRow[];
  by_dosage_cohort?: ExposureStratificationRow[];
  by_age_group?: ExposureStratificationRow[];
  by_sex?: ExposureStratificationRow[];
}

export type TimelineEventType =
  | "DEMOGRAPHICS"
  | "MEDICAL_HISTORY"
  | "DRUG_ADMINISTRATION"
  | "CONCOMITANT_MEDICATION"
  | "ADVERSE_EVENT"
  | "DIAGNOSTIC_LAB"
  | "HOSPITALIZATION"
  | "DECHALLENGE_RECHALLENGE";

export interface AttachmentResponse {
  id: string;
  ticket_id: string;
  filename: string;
  file_size_bytes: number;
  mime_type: string;
  storage_uri: string;
  sha256_hash: string;
  uploaded_by: string;
  uploaded_at: string;
  deid_scrubbed: boolean;
  version_index: number;
}

export interface Body_upload_ticket_attachment_api_v1_tickets__id__attachments_post {
  file: string;
  reason_for_change?: string;
}

export interface CommentCreate {
  body: string;
  visibility?: CommentVisibility;
}

export interface CommentResponse {
  id: string;
  ticket_id: string;
  body: string;
  visibility?: string;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export type CommentVisibility = "PUBLIC" | "INTERNAL_SPONSOR";

export interface CrossAppEventCreate {
  event_type: string;
  title: string;
  description: string;
  category?: TicketCategory;
  priority?: TicketPriority;
  gxp_severity?: GxPSeverity;
  source_service: string;
  study_id?: string | null;
  site_id?: string | null;
  related_entity_type: string;
  related_entity_id: string;
  context_payload?: Record<string, any> | null;
}

export type GxPSeverity = "NOT_APPLICABLE" | "MINOR" | "MAJOR" | "CRITICAL";

export interface PaginatedTicketAuditLogResponse {
  items: TicketAuditLogResponse[];
  total_count: number;
  limit: number;
  offset: number;
  has_more: boolean;
  next_page?: string | null;
  next_cursor?: string | null;
}

export interface RAGPreviewRequest {
  query: string;
  study_id: string;
  protocol_version?: string | null;
  top_k?: number;
}

export interface RAGTriageResponse {
  ticket_id: string;
  rag_status: string;
  faithfulness_score: number;
  is_grounded: boolean;
  draft_answer?: string | null;
  citations?: Record<string, any>[];
  routed_to_role?: string | null;
  routing_reason: string;
  latency_ms?: number;
}

export interface RegulatoryRiskAssessment {
  risk_level: "HIGH_RISK" | "MEDIUM_RISK" | "LOW_RISK";
  affected_gxp_clauses: string[];
  requires_qa_signoff: boolean;
  summary: string;
  risk_summary: string;
}

export type ResolutionCode =
  | "RESOLVED_AS_EXPECTED"
  | "PROTOCOL_CLARIFICATION_ISSUED"
  | "DATA_AMENDED"
  | "CAPA_INITIATED"
  | "FALSE_POSITIVE_VOIDED"
  | "WORKAROUND_APPLIED"
  | "CLOSED_UNRESOLVED";

export type RootCauseCategory =
  | "HUMAN_ERROR"
  | "PROTOCOL_DESIGN"
  | "PROCESS_WORKFLOW"
  | "SYSTEM_TECHNICAL"
  | "INVESTIGATIONAL_PRODUCT"
  | "VENDOR_LAB"
  | "TRAINING_GAP"
  | "OTHER";

export interface SettingDiffEntry {
  setting_key: string;
  old_value: string;
  new_value: string;
  data_type?: string;
}

export interface TicketAssignPayload {
  assignee_user?: string | null;
  assignee_role?: string | null;
  version_index: number;
}

export interface TicketAuditLogResponse {
  id: string;
  ticket_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change?: string | null;
  version_index: number;
  action: string;
  details: string;
  record_id?: string | null;
}

export type TicketCategory =
  | "TECHNICAL"
  | "CLINICAL"
  | "HARDWARE"
  | "ACCESS"
  | "PROTOCOL_DEVIATION"
  | "DATA_QUERY"
  | "SAFETY_ADVERSE_EVENT"
  | "SUPPLY_EXCURSION"
  | "SITE_OPERATIONS"
  | "REGULATORY_ETMF"
  | "CHANGE_REQUEST"
  | "OTHER";

export interface TicketCreate {
  title: string;
  description: string;
  category?: TicketCategory;
  priority?: TicketPriority;
  gxp_severity?: GxPSeverity;
  reporter?: string | null;
  assignee_user?: string | null;
  assignee_role?: string | null;
  org_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  related_entity_type?: string | null;
  related_entity_id?: string | null;
  context_payload?: string | null;
  due_date?: string | null;
  custom_sla_hours?: number | null;
  workflow_type?: string | null;
  action_type?: string | null;
  signature_action?: string | null;
}

export interface TicketKPISummaryResponse {
  total_tickets: number;
  active_tickets: number;
  open_tickets: number;
  in_progress_tickets: number;
  waiting_tickets: number;
  resolved_tickets: number;
  closed_tickets: number;
  critical_deviations: number;
  sla_breaches: number;
  sla_amber_warnings: number;
  sla_compliance_rate: number;
  mean_time_to_resolution_hours: number;
  category_distribution: Record<string, any>;
  severity_distribution: Record<string, any>;
  rca_distribution: Record<string, any>;
  site_distribution: Record<string, any>;
}

export type TicketPriority = "LOW" | "MEDIUM" | "HIGH" | "CRITICAL";

export interface TicketResponse {
  id: string;
  reference: string;
  title: string;
  description: string;
  category: string;
  priority: string;
  status: string;
  gxp_severity?: string;
  root_cause_category?: string | null;
  root_cause_summary?: string | null;
  resolution_code?: string | null;
  reporter: string;
  assignee_user?: string | null;
  assignee_role?: string | null;
  org_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  related_entity_type?: string | null;
  related_entity_id?: string | null;
  context_payload?: string | null;
  due_date?: string | null;
  sla_target_at?: string | null;
  sla_paused_at?: string | null;
  sla_total_paused_seconds?: number;
  sla_breached?: boolean;
  sla_amber_warned?: boolean;
  signature_token?: string | null;
  signature_meaning?: string | null;
  signature_timestamp?: string | null;
  signature_user?: string | null;
  is_deleted: boolean;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface TicketSignaturePayload {
  signature_token: string;
  meaning: string;
  version_index: number;
}

export type TicketStatus =
  | "OPEN"
  | "IN_PROGRESS"
  | "WAITING_ON_SITE"
  | "WAITING_ON_SPONSOR"
  | "PENDING_REGULATORY_REVIEW"
  | "RESOLVED"
  | "CLOSED"
  | "REOPENED"
  | "CANCELLED";

export interface TicketTransitionPayload {
  status: TicketStatus;
  version_index: number;
  root_cause_category?: RootCauseCategory | null;
  root_cause_summary?: string | null;
  resolution_code?: ResolutionCode | null;
  signature_token?: string | null;
  signature_meaning?: string | null;
}

export interface TicketUpdate {
  title?: string | null;
  description?: string | null;
  category?: TicketCategory | null;
  priority?: TicketPriority | null;
  gxp_severity?: GxPSeverity | null;
  status?: TicketStatus | null;
  assignee_user?: string | null;
  assignee_role?: string | null;
  org_id?: string | null;
  site_id?: string | null;
  study_id?: string | null;
  related_entity_type?: string | null;
  related_entity_id?: string | null;
  context_payload?: string | null;
  due_date?: string | null;
  is_deleted?: boolean | null;
  version_index?: number | null;
}
