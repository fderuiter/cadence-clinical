/**
 * Generated from docs/openapi/execution_forms_openapi.json by scripts/generate-api-types.js.
 * Do not edit; run `pnpm --filter web generate:api` after regenerating the spec.
 */

export interface paths {
  "/api/v1/execution/forms": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists the forms opened for an enrollment, oldest first. */
    get: operations["ExecutionListEnrollmentForms"];
    put?: never;
    /** Opens a form for a participant's visit under the protocol version in force (collection.open). */
    post: operations["ExecutionOpenForm"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Reads a form: its items in layout order with their values, completeness, locks, assertions and signatures. */
    get: operations["ExecutionGetForm"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/values": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    /** Saves a page of values: all of them or, on one stale revision or invalid value, none (collection.record). */
    put: operations["ExecutionSaveFormValues"];
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/preview": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Evaluates pinned study collection rules in memory over draft form values without mutating state (K-RULE-3). */
    post: operations["ExecutionPreviewFormValues"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/queries": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists the queries on every form of an enrollment or across visible enrollments of a study, oldest first. */
    get: operations["ExecutionListEnrollmentQueries"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/queries/digests": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists daily de-identified query aging digests for visible scopes of a study. */
    get: operations["ExecutionListFormQueryAgingDigests"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/queries/escalate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Escalates open queries past their 14-day SLA on IClock and emits daily aging digests (execution.query.manage). */
    post: operations["ExecutionEscalateOverdueFormQueries"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists a form's queries, oldest first, each with whether its value changed since it was raised. */
    get: operations["ExecutionListFormQueries"];
    put?: never;
    /** Raises a query on the current revision of one of the form's values or on a blank item (execution.query.raise). */
    post: operations["ExecutionRaiseFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/answer": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Answers an open query (execution.query.answer). */
    post: operations["ExecutionAnswerFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/reopen": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Reopens an answered or closed query with why it is not settled (execution.query.manage). */
    post: operations["ExecutionReopenFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/close": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Closes an answered manual query (separation of duties) or system query (execution.query.manage). */
    post: operations["ExecutionCloseFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/queries/{queryId}/cancel": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Cancels an open or answered query raised in error (execution.query.manage). */
    post: operations["ExecutionCancelFormQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{formId}/signatures": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Signs the form's current values with an X-Sig-Token step-up token (collection.sign); 409 when the form changed since it was read. */
    post: operations["ExecutionSignForm"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
}
export type webhooks = Record<string, never>;
export interface components {
  schemas: {
    /** @description One option in a collection item's governed codelist. */
    ExecutionForms_CodelistOptionView: {
      /** @description The code recorded in string CodedValue.Code. */
      code: string;
      /** @description Human-readable label for the code. */
      display: string;
      /** @description Optional standards submission value (for example CDISC CT submission value). */
      submission_value?: null | string;
    };
    /** @description Strongly-typed UUIDv7 identifier for an immutable, checksum-verified release of a code system in the Concept Registry. */
    ExecutionForms_CodeSystemVersionId: unknown;
    /** @enum {unknown} */
    ExecutionForms_DateTimePrecision:
      "Year" | "Month" | "Day" | "Minute" | "Second" | null;
    ExecutionForms_FormAssertionResponse: {
      /** Format: uuid */
      assertion_id: string;
      kind: string;
      target: string;
      /** Format: int32 */
      revision: null | number | string;
      text: null | string;
      asserted_by: string;
      /** Format: date-time */
      asserted_at: string;
      reason: string;
      /** Format: date-time */
      retracted_at: null | string;
    };
    ExecutionForms_FormItemResponse: {
      item_id: string;
      concept: null | string;
      unit: null | string;
      value_kind: null | string;
      required: boolean;
      value: null | components["schemas"]["ExecutionForms_ObservationValue"];
      /** Format: uuid */
      observation_id: null | string;
      /** Format: int32 */
      revision: number | string;
      recorded_by: null | string;
      /** Format: date-time */
      recorded_at: null | string;
      /** Format: uuid */
      code_system_version_id?: null | string;
      codelist?: null | string;
      codelist_options?:
        null | components["schemas"]["ExecutionForms_CodelistOptionView"][];
    };
    ExecutionForms_FormLockResponse: {
      /** Format: uuid */
      lock_id: string;
      kind: string;
      scope: string;
      /** Format: date-time */
      locked_at: string;
      locked_by: string;
      reason: string;
    };
    ExecutionForms_FormPreviewResponse: {
      /** Format: uuid */
      form_id: string;
      warnings: components["schemas"]["ExecutionForms_FormPreviewWarningResponse"][];
    };
    ExecutionForms_FormPreviewWarningResponse: {
      pack_key: string;
      rule_id: string;
      /** Format: int32 */
      rule_version: number | string;
      severity: string;
      dataset: string;
      item_id: string;
      message: string;
      remediation_guidance?: null | string;
    };
    ExecutionForms_FormQueryAgingDigestEntry: {
      /** Format: uuid */
      query_id: string;
      /** Format: uuid */
      form_id: string;
      item_id: string;
      site_code: string;
      subject_code: string;
      owner_role: string;
      priority: string;
      /** Format: date-time */
      opened_at: string;
      /** Format: date-time */
      due_at: string;
      /** Format: int32 */
      age_days: number | string;
    };
    ExecutionForms_FormQueryAgingDigestResponse: {
      /** Format: uuid */
      digest_id: string;
      /** Format: uuid */
      study_id: string;
      /** Format: uuid */
      study_site_id: null | string;
      scope_key: string;
      recipient_role: string;
      /** Format: date */
      digest_date: string;
      /** Format: date-time */
      generated_at: string;
      study_code: string;
      site_code: null | string;
      /** Format: int32 */
      escalated_count: number | string;
      entries: components["schemas"]["ExecutionForms_FormQueryAgingDigestEntry"][];
    };
    ExecutionForms_FormQueryEscalationPassResponse: {
      /** Format: int32 */
      escalated_queries: number | string;
      digests: components["schemas"]["ExecutionForms_FormQueryAgingDigestResponse"][];
    };
    ExecutionForms_FormQueryMessageResponse: {
      kind: string;
      text: string;
      by: string;
      /** Format: date-time */
      at: string;
      /** @default  */
      reason_for_change: string;
    };
    ExecutionForms_FormQueryNoteRequest: {
      text?: null | string;
    };
    ExecutionForms_FormQueryResponse: {
      /** Format: uuid */
      query_id: string;
      /** Format: uuid */
      form_id: string;
      /** Format: uuid */
      enrollment_id: string;
      item_id: string;
      /** Format: uuid */
      observation_id: string;
      /** Format: int32 */
      revision: number | string;
      /** Format: int32 */
      current_revision: null | number | string;
      value_changed: boolean;
      status: string;
      text: string;
      messages: components["schemas"]["ExecutionForms_FormQueryMessageResponse"][];
      raised_by: string;
      /** Format: date-time */
      raised_at: string;
      /** Format: int64 */
      version: number | string;
      /** Format: uuid */
      study_id?: null | string;
      /** Format: uuid */
      study_site_id?: null | string;
      site_code?: null | string;
      subject_code?: null | string;
      collection_spec_id?: null | string;
      planned_activity_id?: null | string;
      /** @default manual */
      origin: string;
      /** @default site */
      owner_role: string;
      assignee?: null | string;
      /** @default normal */
      priority: string;
      /** Format: date-time */
      opened_at?: null | string;
      /** Format: date-time */
      status_since?: null | string;
      /** Format: date-time */
      due_at?: null | string;
      /** Format: date-time */
      escalated_at?: null | string;
      last_answered_by?: null | string;
    };
    ExecutionForms_FormQueryTextRequest: {
      text: string;
    };
    ExecutionForms_FormResponse: {
      /** Format: uuid */
      form_id: string;
      /** Format: uuid */
      enrollment_id: string;
      /** Format: uuid */
      participant_id: string;
      /** Format: uuid */
      study_id: string;
      /** Format: uuid */
      study_site_id: string;
      /** Format: uuid */
      protocol_version_id: string;
      collection_spec_id: string;
      planned_activity_id: null | string;
      /** Format: date-time */
      collected_at: string;
      opened_by: string;
      /** Format: date-time */
      opened_at: string;
      /** Format: int64 */
      version: number | string;
      complete: boolean;
      missing_required: string[];
      items: components["schemas"]["ExecutionForms_FormItemResponse"][];
      locks: components["schemas"]["ExecutionForms_FormLockResponse"][];
      assertions: components["schemas"]["ExecutionForms_FormAssertionResponse"][];
      signatures: components["schemas"]["ExecutionForms_FormSignatureResponse"][];
      content_hash: null | string;
      collection_title?: null | string;
      /** @default true */
      can_sign: boolean;
      /**
       * Format: int32
       * @default 0
       */
      open_query_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      answered_query_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      open_error_finding_count: number | string;
      readiness_blockers?: null | string[];
    };
    ExecutionForms_FormSavedResponse: {
      /** Format: uuid */
      form_id: string;
      /** Format: int64 */
      version: number | string;
      values: components["schemas"]["ExecutionForms_FormSavedValueResponse"][];
      form?: null | components["schemas"]["ExecutionForms_FormResponse"];
    };
    ExecutionForms_FormSavedValueResponse: {
      item_id: string;
      /** Format: uuid */
      observation_id: string;
      value: components["schemas"]["ExecutionForms_ObservationValue"];
      /** Format: int32 */
      revision: number | string;
      changed: boolean;
    };
    ExecutionForms_FormSignatureResponse: {
      /** Format: uuid */
      attestation_id: string;
      meaning: string;
      signed_by: string;
      /** Format: date-time */
      signed_at: string;
      reason: string;
      content_hash: string;
      superseded: boolean;
      signed_values:
        | null
        | components["schemas"]["ExecutionForms_FormSignedValueResponse"][];
    };
    ExecutionForms_FormSignedValueResponse: {
      item_id: string;
      /** Format: uuid */
      observation_id: string;
      /** Format: int32 */
      revision: number | string;
      value: components["schemas"]["ExecutionForms_ObservationValue"];
    };
    ExecutionForms_FormStateResponse: {
      /** Format: uuid */
      form_id: string;
      /** Format: uuid */
      enrollment_id: string;
      /** Format: uuid */
      protocol_version_id: string;
      collection_spec_id: string;
      planned_activity_id: null | string;
      /** Format: date-time */
      collected_at: string;
      /** Format: int64 */
      version: number | string;
    };
    ExecutionForms_FormSummaryResponse: {
      /** Format: uuid */
      form_id: string;
      /** Format: uuid */
      enrollment_id: string;
      /** Format: uuid */
      participant_id: string;
      /** Format: uuid */
      study_id: string;
      /** Format: uuid */
      study_site_id: string;
      /** Format: uuid */
      protocol_version_id: string;
      collection_spec_id: string;
      planned_activity_id: null | string;
      /** Format: date-time */
      collected_at: string;
      opened_by: string;
      /** Format: date-time */
      opened_at: string;
      /** Format: int64 */
      version: number | string;
      complete: boolean;
      /** Format: int32 */
      item_count: number | string;
      /** Format: int32 */
      recorded_count: number | string;
      collection_title?: null | string;
      /**
       * Format: int32
       * @default 0
       */
      missing_required_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      open_query_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      answered_query_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      verified_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      unverified_count: number | string;
      /** @default false */
      locked: boolean;
      /** @default false */
      frozen: boolean;
      /**
       * Format: int32
       * @default 0
       */
      lock_count: number | string;
      /** @default false */
      signed: boolean;
      /** @default false */
      signature_superseded: boolean;
      /**
       * Format: int32
       * @default 0
       */
      signature_count: number | string;
      /**
       * Format: int32
       * @default 0
       */
      open_error_finding_count: number | string;
      readiness_blockers?: null | string[];
    };
    ExecutionForms_FormValueRequest: {
      item_id: string;
      value: null | components["schemas"]["ExecutionForms_ObservationValue"];
      /** Format: int32 */
      expected_revision: number | string;
      /** Format: uuid */
      observation_id: null | string;
      /** Format: date-time */
      observed_at: null | string;
    };
    /**
     * @description Why a value is missing (HL7 null flavors, narrowed to the ones clinical data collection uses).
     * @enum {unknown}
     */
    ExecutionForms_NullFlavor:
      | "NotDone"
      | "Unknown"
      | "AskedButUnknown"
      | "NotAsked"
      | "NotApplicable"
      | "Masked";
    /**
     * @description The value of a recorded clinical fact (spec §8.1, K-FACT-2, K-FACT-3, ADR-2623): exactly one of a quantity with its
     *     UCUM unit, a code from a pinned code-system version, text, a boolean, an ISO 8601 date or time at the precision it was
     *     known, or a reasoned absence. It is the value <em>as entered</em>: a normalised unit, a standardised result or a
     *     decoded label is a derivation of it, never a replacement (K-FACT-2).
     */
    ExecutionForms_ObservationValue:
      | components["schemas"]["ExecutionForms_ObservationValueQuantityValue"]
      | components["schemas"]["ExecutionForms_ObservationValueCodedValue"]
      | components["schemas"]["ExecutionForms_ObservationValueTextValue"]
      | components["schemas"]["ExecutionForms_ObservationValueBooleanValue"]
      | components["schemas"]["ExecutionForms_ObservationValueDateTimeValue"]
      | components["schemas"]["ExecutionForms_ObservationValueMissingValue"];
    /** @description A yes or no answer. */
    ExecutionForms_ObservationValueBooleanValue: {
      /** @enum {string} */
      type?: "boolean";
      /** @description The answer. */
      value: boolean;
    };
    /**
     * @description A code from one pinned code-system version (spec §6.1), such as an NCI CT submission value in CDISC CT 2025-09-26 or
     *     a LOINC answer code. The version is part of the value, so the same code read years later still means what it meant.
     */
    ExecutionForms_ObservationValueCodedValue: {
      /** @enum {string} */
      type?: "coded";
      /** @description The code-system version the code was chosen from. */
      code_system_version_id: components["schemas"]["ExecutionForms_CodeSystemVersionId"];
      /** @description The code, exactly as the code system writes it. */
      code: string;
    };
    /**
     * @description An ISO 8601 date or date and time at the precision it was known (ISO 8601-1 reduced precision): `2026`,
     *     `2026-03`, `2026-03-14`, `2026-03-14T09:30` or `2026-03-14T09:30:15`. A time may carry <!-- deid-ignore: ISO 8601 format examples, not PHI -->`Z` or an offset; without one it is the local time where it was observed. A partial date is never padded to a
     *     full one.
     */
    ExecutionForms_ObservationValueDateTimeValue: {
      /** @enum {string} */
      type?: "dateTime";
      /** @description The ISO 8601 text. */
      value: string;
      /** @description How precisely the date or time is known, or null when string DateTimeValue.Value is not valid ISO 8601. */
      precision?: components["schemas"]["ExecutionForms_DateTimePrecision"];
    };
    /**
     * @description A value that was not obtained, with why (K-FACT-3): the test was not done, the answer is unknown, the question does
     *     not apply, and so on. It is a recorded fact in its own right, corrected like any other value.
     */
    ExecutionForms_ObservationValueMissingValue: {
      /** @enum {string} */
      type?: "missing";
      /** @description Why there is no value. */
      reason: components["schemas"]["ExecutionForms_NullFlavor"];
      /** @description More about why, such as the reason a test was not done (SDTM `--REASND`); optional. */
      detail?: null | string;
    };
    /**
     * @description A measured amount in the UCUM unit it was entered in (K-FACT-2), such as 120 `mm[Hg]`, with an optional
     *     comparator for a bound such as &lt;0.5 `mg/dL`. The decimal keeps the precision it was entered with (1.50 stays
     *     1.50).
     */
    ExecutionForms_ObservationValueQuantityValue: {
      /** @enum {string} */
      type?: "quantity";
      /**
       * Format: double
       * @description The amount as entered.
       */
      value: number | string;
      /** @description The UCUM unit expression, case-sensitive; `1` for a dimensionless quantity. */
      unit: string;
      comparator?:
        null | components["schemas"]["ExecutionForms_QuantityComparator"];
    };
    /** @description Free text as entered, such as a verbatim adverse event term. */
    ExecutionForms_ObservationValueTextValue: {
      /** @enum {string} */
      type?: "text";
      /** @description The text. */
      value: string;
    };
    ExecutionForms_OpenFormRequest: {
      /** Format: uuid */
      form_id: null | string;
      /** Format: uuid */
      enrollment_id: string;
      collection_spec_id: string;
      planned_activity_id: null | string;
      /** Format: date-time */
      collected_at: string;
    };
    ExecutionForms_PreviewFormItemValueRequest: {
      item_id: string;
      value: null | components["schemas"]["ExecutionForms_ObservationValue"];
    };
    ExecutionForms_PreviewFormValuesRequest: {
      values: components["schemas"]["ExecutionForms_PreviewFormItemValueRequest"][];
    };
    ExecutionForms_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    /** @enum {unknown} */
    ExecutionForms_QuantityComparator: "<" | "<=" | ">" | ">=" | null;
    ExecutionForms_RaiseFormQueryRequest: {
      /** Format: uuid */
      observation_id: null | string;
      /** Format: int32 */
      revision: null | number | string;
      text: string;
      /** Format: uuid */
      query_id?: null | string;
      item_id?: null | string;
      owner_role?: null | string;
      priority?: null | string;
      assignee?: null | string;
    };
    ExecutionForms_SaveFormValuesRequest: {
      values: components["schemas"]["ExecutionForms_FormValueRequest"][];
    };
    ExecutionForms_SignFormRequest: {
      meaning: string;
      content_hash: string;
      /** Format: uuid */
      signature_id: null | string;
    };
  };
  responses: never;
  parameters: never;
  requestBodies: never;
  headers: never;
  pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
  ExecutionListEnrollmentForms: {
    parameters: {
      query?: {
        enrollment_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormSummaryResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionOpenForm: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["ExecutionForms_OpenFormRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormStateResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Collection.EnrollmentUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.NoGoverningVersion`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantWithdrawn`, `K.Observation.StudySiteNotActive`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Collection.CollectedAtInvalid`, `K.Collection.SpecUnknown`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Observation.ApplicabilityInconsistent`, `K.Observation.Locked`, `K.Observation.ReconsentRequired`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionGetForm: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionSaveFormValues: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["ExecutionForms_SaveFormValuesRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormSavedResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.RevisionStale`, `K.Command.IdempotencyConflict`, `K.Observation.ParticipantWithdrawn`, `K.Observation.StudySiteNotActive`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Collection.ValuesInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Observation.ObservedAtInvalid`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Observation.ApplicabilityInconsistent`, `K.Observation.Locked`, `K.Observation.ReconsentRequired`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionPreviewFormValues: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          | null
          | components["schemas"]["ExecutionForms_PreviewFormValuesRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormPreviewResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionListEnrollmentQueries: {
    parameters: {
      query?: {
        enrollment_id?: string;
        study_id?: string;
        status?: string;
        owner?: string;
        origin?: string;
        priority?: string;
        escalated?: boolean;
        site?: string;
        participant?: string;
        value_changed?: boolean;
        as_of?: string;
        valid_at?: string;
        known_at?: string;
        limit?: number | string;
        offset?: number | string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormQueryResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionListFormQueryAgingDigests: {
    parameters: {
      query?: {
        study_id?: string;
        site?: string;
        recipient_role?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormQueryAgingDigestResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionEscalateOverdueFormQueries: {
    parameters: {
      query?: {
        study_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormQueryEscalationPassResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionListFormQueries: {
    parameters: {
      query?: {
        as_of?: string;
        valid_at?: string;
        known_at?: string;
      };
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormQueryResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionRaiseFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["ExecutionForms_RaiseFormQueryRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormQueryResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. Kernel errors: `M.Execution.Form.CollectionInstanceUnknown`, `M.Execution.Query.ObservationUnknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.RevisionStale`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Collection.ValuesInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionAnswerFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
        queryId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["ExecutionForms_FormQueryTextRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormQueryResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionReopenFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
        queryId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["ExecutionForms_FormQueryTextRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormQueryResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionCloseFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
        queryId: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json": components["schemas"]["ExecutionForms_FormQueryNoteRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormQueryResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`, `M.Execution.Query.SeparationOfDuties`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionCancelFormQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
        queryId: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json": components["schemas"]["ExecutionForms_FormQueryNoteRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormQueryResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Execution.Query.InvalidTransition`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Locked. Kernel errors: `K.Assertion.Locked`. */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
  ExecutionSignForm: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        formId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["ExecutionForms_SignFormRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["ExecutionForms_FormSignatureResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`, `K.Trust.AuthenticationStale`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`, `K.Trust.PersonRequired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `K.Aggregate.NotFound`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Collection.NotReadyToSign`, `K.Command.IdempotencyConflict`, `K.Trust.TargetContentChanged`, `K.Trust.TargetNotSignable`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Collection.SignatureMeaningInvalid`, `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `K.Trust.MethodRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["ExecutionForms_ProblemDetails"];
        };
      };
    };
  };
}
