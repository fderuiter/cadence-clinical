# execution_forms API

Generated from `docs/openapi/execution_forms_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

The service exposes 16 operations.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/execution/forms` | ExecutionListEnrollmentForms | Lists the forms opened for an enrollment, oldest first. |
| `POST` | `/api/v1/execution/forms` | ExecutionOpenForm | Opens a form for a participant's visit under the protocol version in force (collection.open). |
| `GET` | `/api/v1/execution/forms/queries` | ExecutionListEnrollmentQueries | Lists the queries on every form of an enrollment or across visible enrollments of a study, oldest first. |
| `GET` | `/api/v1/execution/forms/queries/digests` | ExecutionListFormQueryAgingDigests | Lists daily de-identified query aging digests for visible scopes of a study. |
| `POST` | `/api/v1/execution/forms/queries/escalate` | ExecutionEscalateOverdueFormQueries | Escalates open queries past their 14-day SLA on IClock and emits daily aging digests (execution.query.manage). |
| `GET` | `/api/v1/execution/forms/{formId}` | ExecutionGetForm | Reads a form: its items in layout order with their values, completeness, locks, assertions and signatures. |
| `POST` | `/api/v1/execution/forms/{formId}/preview` | ExecutionPreviewFormValues | Evaluates pinned study collection rules in memory over draft form values without mutating state (K-RULE-3). |
| `GET` | `/api/v1/execution/forms/{formId}/queries` | ExecutionListFormQueries | Lists a form's queries, oldest first, each with whether its value changed since it was raised. |
| `POST` | `/api/v1/execution/forms/{formId}/queries` | ExecutionRaiseFormQuery | Raises a query on the current revision of one of the form's values or on a blank item (execution.query.raise). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/answer` | ExecutionAnswerFormQuery | Answers an open query (execution.query.answer). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/cancel` | ExecutionCancelFormQuery | Cancels an open or answered query raised in error (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/close` | ExecutionCloseFormQuery | Closes an answered manual query (separation of duties) or system query (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/reassign` | ExecutionReassignFormQuery | Reassigns an open or answered query's assignee or responsible role with audited bitemporal history (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/queries/{queryId}/reopen` | ExecutionReopenFormQuery | Reopens an answered or closed query with why it is not settled (execution.query.manage). |
| `POST` | `/api/v1/execution/forms/{formId}/signatures` | ExecutionSignForm | Signs the form's current values with an X-Sig-Token step-up token (collection.sign); 409 when the form changed since it was read. |
| `PUT` | `/api/v1/execution/forms/{formId}/values` | ExecutionSaveFormValues | Saves a page of values: all of them or, on one stale revision or invalid value, none (collection.record). |
