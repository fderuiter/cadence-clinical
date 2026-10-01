/**
 * Generated from docs/openapi/execution_openapi.json by scripts/generate-api-types.js.
 * Do not edit; run `pnpm --filter web generate:api` after regenerating the spec.
 */

export interface paths {
  "/healthz": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Execution engine health probe. */
    get: operations["ExecutionHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/webhooks/etmf": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["EtmfCompletenessWebhook"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/sites/{siteId}/activate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["ActivateSite"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/sites/{siteId}/compliance-status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetSiteComplianceStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/subjects": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists one page of a study's subjects in enrollment order. */
    get: operations["ListSubjects"];
    put?: never;
    post: operations["CreateSubject"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/subjects/{subjectId}/consent": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["RecordSubjectConsent"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/subjects/{subjectId}/state": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch: operations["UpdateSubjectState"];
    trace?: never;
  };
  "/api/v1/execution/subjects/{subjectId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetSubjectDetail"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/subjects/{subjectId}/demographics": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put: operations["UpdateSubjectDemographics"];
    post?: never;
    delete: operations["DeleteSubjectDemographics"];
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/observations": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["CreateClinicalObservation"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/observations/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetObservationById"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/observations/subject/{studyId}/{subjectId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListObservationsBySubject"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/observations/{id}/sdv": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["VerifyObservationSdv"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/observations/{id}/sdv/flag": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["FlagObservationSdv"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["CreateFormSubmission"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetFormSubmissionById"];
    put: operations["UpdateFormPayload"];
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/subject/{studyId}/{subjectId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListFormSubmissionsBySubject"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{id}/complete": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["CompleteFormSubmission"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{id}/approve": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["ApproveFormSubmission"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/forms/{id}/unlock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["UnlockFormSubmission"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/queries": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListClinicalQueries"];
    put?: never;
    post: operations["CreateClinicalQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/queries/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetClinicalQueryById"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/queries/{id}/respond": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["RespondClinicalQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/queries/{id}/close": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["CloseClinicalQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/queries/{id}/reopen": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["ReopenClinicalQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/queries/{id}/cancel": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["CancelClinicalQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/cec/dossiers/{id}/chair-adjudicate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["ChairAdjudicateDossier"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/cec/dossiers/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetCecDossierById"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/pre-flight": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["PreflightLockCheck"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/lock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["LockData"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/unlock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["UnlockData"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListLocks"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/site/{site_id}/freeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          site_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/site/{site_id}/lock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          site_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/site/{site_id}/unfreeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          site_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/site/{site_id}/unlock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          site_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/visit/{visit_id}/freeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          visit_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/visit/{visit_id}/lock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          visit_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/visit/{visit_id}/unfreeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          visit_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/visit/{visit_id}/unlock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          visit_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/form/{form_id}/freeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          form_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/form/{form_id}/lock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          form_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/form/{form_id}/unfreeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          form_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/form/{form_id}/unlock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          form_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/subject/{subject_id}/freeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          subject_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/subject/{subject_id}/lock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          subject_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/subject/{subject_id}/unfreeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          subject_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/subject/{subject_id}/unlock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          subject_id: string;
        };
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/trial/freeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/trial/lock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockExecuteRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/trial/unfreeze": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/locks/trial/unlock": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody?: {
        content: {
          "application/json":
            null | components["schemas"]["Execution_DataLockUnlockRequest"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
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
    Execution_AnonymousTypeOfstringAndstringAndstring: {
      status: null | string;
      service: null | string;
      engine: null | string;
    };
    Execution_CecDossier: {
      event_id?: string;
      study_id?: string;
      subject_id?: string;
      masked_subject_id?: string;
      site_id?: null | string;
      masked_site_id?: null | string;
      candidate_event_type?: string;
      reported_term?: string;
      reported_onset_date?: string;
      workflow_status?: string;
      is_discordant?: boolean;
      discordance_reasons?: string[];
      final_consensus_classification?: null | string;
      final_consensus_subtype?: null | string;
      final_adjudicated_onset_date?: null | string;
      adjudication_rationale?: null | string;
      reader1_status?: string;
      reader2_status?: string;
      signature_manifest?:
        null | components["schemas"]["Execution_SignatureManifest"];
      /** Format: date-time */
      finalized_at?: null | string;
      id?: null | string;
      /** Format: date-time */
      created_at?: string;
      created_by?: string;
      reason_for_change?: string;
      /** Format: int64 */
      version_index?: number | string;
    };
    Execution_ChairAdjudicateRequest: {
      final_classification: string;
      final_subtype: null | string;
      final_adjudicated_onset_date: string;
      /** @default CHAIR_CUSTOM_DETERMINATION */
      binding_reader: null | string;
      criteria_confirmed?: null | string[];
      /** @default  */
      adjudication_rationale: string;
      signature_token?: null | string;
      signer_name?: null | string;
      password?: null | string;
      signature_meaning?: null | string;
    };
    Execution_ChairAdjudicationResponse: {
      determination_id: string;
      dossier_id: string;
      chair_id: string;
      chair_name: string;
      final_classification: string;
      final_subtype: null | string;
      final_adjudicated_onset_date: string;
      binding_reader: string;
      criteria_confirmed: string[];
      adjudication_rationale: string;
      electronic_signature: components["schemas"]["Execution_SignatureManifest"];
      finalized_at: string;
    };
    Execution_ClinicalQueryResponse: {
      id: string;
      study_id: string;
      subject_id: string;
      test_code: string;
      status: components["schemas"]["Execution_QueryStatus"];
      site_id?: null | string;
      visit_id?: null | string;
      domain?: null | string;
      explanation?: null | string;
      response?: null | string;
      observation_id?: null | string;
      field_link?: null | string;
      message?: null | string;
      origin?: null | string;
      priority?: null | string;
      rule_id?: null | string;
      created_by?: null | string;
      responder?: null | string;
      resolver?: null | string;
      /** Format: date-time */
      resolved_at?: null | string;
      cancellation_reason?: null | string;
      /** Format: date-time */
      escalated_at?: null | string;
      form_id?: null | string;
      field_id?: null | string;
      query_type?: null | string;
      action_required?: null | string;
      /** Format: date-time */
      created_at?: null | string;
      /**
       * Format: int64
       * @default 1
       */
      version: number | string;
    };
    Execution_ComplianceStatusResponse: {
      study_id: components["schemas"]["Execution_StudyCode"];
      site_id: null | components["schemas"]["Execution_SiteCode"];
      milestone: string;
      is_complete: boolean;
      missing_documents: string[];
    };
    Execution_DataLockExecuteRequest: {
      scope_type: string;
      scope_id: string;
      study_id: string;
      site_id?: null | string;
      subject_id?: null | string;
      visit_id?: null | string;
      form_id?: null | string;
      field_name?: null | string;
      /** @default HARD_LOCK */
      action: string;
      /** @default HARD_LOCK */
      lock_type: string;
      /** @default  */
      reason_for_change: string;
      sig_token?: null | string;
      /** @default false */
      override_approved: boolean;
      override_justification?: null | string;
    };
    Execution_DataLockRecordResponse: {
      lock_id: string;
      scope_type: string;
      scope_id: string;
      study_id: string;
      site_id: null | string;
      subject_id: null | string;
      visit_id: null | string;
      form_id: null | string;
      field_name: null | string;
      lock_type: string;
      status: string;
      is_active: boolean;
      created_by: string;
      /** Format: date-time */
      created_at: string;
      reason_for_change: string;
      preflight_summary: null | string;
      /** Format: int32 */
      open_query_count: number | string;
      /** Format: int32 */
      unanswered_query_count: number | string;
      /** Format: int32 */
      unverified_source_data_count: number | string;
      is_override: boolean;
      override_justification: null | string;
      unlock_justification: null | string;
      unlocked_by: null | string;
      /** Format: date-time */
      unlocked_at: null | string;
    };
    Execution_DataLockUnlockRequest: {
      lock_id: null | string;
      scope_type: string;
      scope_id: string;
      justification: string;
      /** @default  */
      reason_for_change: string;
    };
    Execution_DemographicsPayload: {
      name: null | string;
      birthdate: null | string;
      gender: null | string;
      race: null | string;
    };
    Execution_DiscrepancyCounts: {
      /** Format: int32 */
      open_queries: number | string;
      /** Format: int32 */
      unanswered_queries: number | string;
      /** Format: int32 */
      unverified_source_data: number | string;
      /** Format: int32 */
      total_discrepancies: number | string;
    };
    Execution_EtmfCompletenessWebhookPayload: {
      study_id: string;
      site_id: null | string;
      milestone: string;
      is_complete: boolean;
      missing_artifacts: null | string[];
    };
    Execution_FlagObservationRequest: {
      reason: string;
    };
    Execution_FormPayloadUpdateRequest: {
      payload: string;
    };
    Execution_FormSubmissionApproveRequest: {
      signing_reason: string;
      signature_manifest: components["schemas"]["Execution_SignatureManifest"];
    };
    Execution_FormSubmissionCreateRequest: {
      study_id: string;
      subject_id: string;
      form_id: string;
      site_id?: null | string;
      visit_id?: null | string;
      protocol_version?: null | string;
      payload?: null | string;
    };
    Execution_FormSubmissionResponse: {
      id: string;
      study_id: string;
      subject_id: string;
      form_id: string;
      status: components["schemas"]["Execution_FormSubmissionStatus"];
      site_id?: null | string;
      visit_id?: null | string;
      protocol_version?: null | string;
      payload?: null | string;
      signature_manifest?:
        null | components["schemas"]["Execution_SignatureManifest"];
      /** @default true */
      is_active: boolean;
      /** @default false */
      is_readonly: boolean;
      cloned_from_id?: null | string;
      /**
       * Format: int64
       * @default 1
       */
      version: number | string;
    };
    /** @enum {string} */
    Execution_FormSubmissionStatus: "DRAFT" | "COMPLETED" | "APPROVED";
    Execution_FormUnlockRequest: {
      reason: string;
    };
    Execution_HttpValidationProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
      errors?: {
        [key: string]: string[];
      };
    };
    Execution_ObservationCreateRequest: {
      subject_id: string;
      domain: string;
      test_code: string;
      test_name: string;
      /** Format: date-time */
      observation_date: null | string;
      study_id?: null | string;
      site_id?: null | string;
      visit_id?: null | string;
      /** Format: double */
      value?: null | number | string;
      value_string?: null | string;
      unit?: null | string;
      lab_source?: null | string;
      lab_site_id?: null | string;
      page_id?: null | string;
      protocol_version_tag?: null | string;
      /** Format: int32 */
      protocol_version_index?: null | number | string;
    };
    Execution_ObservationResponse: {
      id: string;
      subject_id: string;
      study_id: string;
      domain: string;
      test_code: string;
      test_name: string;
      /** Format: date-time */
      observation_date: string;
      site_id?: null | string;
      visit_id?: null | string;
      /** Format: double */
      value?: null | number | string;
      value_string?: null | string;
      unit?: null | string;
      /** Format: double */
      normalized_value?: null | number | string;
      normalized_unit?: null | string;
      /** @default false */
      is_outlier: boolean;
      /** @default false */
      is_sdv_verified: boolean;
      sdv_verified_by?: null | string;
      /** Format: date-time */
      sdv_verified_at?: null | string;
      lab_source?: null | string;
      lab_site_id?: null | string;
      lab_indicator?: null | string;
      lab_out_of_range?: null | boolean;
      matched_normal_bounds?: null | string;
      range_indicator?: null | string;
      is_out_of_range?: null | boolean;
      /** Format: double */
      reference_range_low?: null | number | string;
      /** Format: double */
      reference_range_high?: null | number | string;
      protocol_version_tag?: null | string;
      /** Format: int32 */
      protocol_version_index?: null | number | string;
    };
    Execution_OperationEnvelopeResponse: {
      status: string;
      message: string;
    };
    Execution_PreflightCheckRequest: {
      scope_type: string;
      scope_id: string;
      study_id: string;
      site_id?: null | string;
      subject_id?: null | string;
      visit_id?: null | string;
      form_id?: null | string;
      field_name?: null | string;
      /** @default HARD_LOCK */
      action: null | string;
    };
    Execution_PreflightCheckResponse: {
      scope_type: string;
      scope_id: string;
      status: string;
      can_proceed: boolean;
      requires_override: boolean;
      summary: string;
      discrepancy_counts: components["schemas"]["Execution_DiscrepancyCounts"];
      discrepancies: components["schemas"]["Execution_PreLockDiscrepancyItem"][];
      /** Format: date-time */
      checked_at: string;
      /** Format: double */
      execution_time_ms: number | string;
    };
    Execution_PreLockDiscrepancyItem: {
      id: string;
      discrepancy_type: string;
      scope_type: string;
      scope_id: string;
      subject_id: null | string;
      visit_id: null | string;
      form_id: null | string;
      field_id: null | string;
      description: string;
      severity: string;
      status: string;
    };
    Execution_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Execution_ProtocolVersionPayload: {
      study_id: string;
      version_tag: string;
      /** Format: int32 */
      version_index: number | string;
    };
    Execution_QueryCancelRequest: {
      reason: string;
    };
    Execution_QueryCreateRequest: {
      study_id: string;
      subject_id: string;
      test_code: string;
      explanation: string;
      /** @default OPEN */
      status: null | string;
      site_id?: null | string;
      visit_id?: null | string;
      domain?: null | string;
      observation_id?: null | string;
      field_link?: null | string;
      message?: null | string;
      /** @default MANUAL */
      origin: null | string;
      /** @default MEDIUM */
      priority: null | string;
      rule_id?: null | string;
      form_id?: null | string;
      field_id?: null | string;
      query_type?: null | string;
      action_required?: null | string;
    };
    Execution_QueryReopenRequest: {
      reason: string;
    };
    Execution_QueryRespondRequest: {
      response: string;
    };
    /** @enum {string} */
    Execution_QueryStatus:
      | "NONE"
      | "CANDIDATE"
      | "DRAFT_AI"
      | "OPEN"
      | "ANSWERED"
      | "RESOLVED"
      | "CLOSED"
      | "REOPENED"
      | "CANCELLED";
    Execution_SignatureManifest: {
      signer_id: string;
      signer_name: string;
      signer_role: string;
      signing_reason: string;
      payload_hash: string;
      hmac_signature: null | string;
      /** Format: date-time */
      signed_at: string;
    };
    Execution_SiteActivationRequest: {
      study_id: string;
    };
    Execution_SiteCode: string;
    Execution_StudyCode: string;
    Execution_SubjectCode: string;
    Execution_SubjectConsentId: string;
    Execution_SubjectConsentRequest: {
      protocol_version: components["schemas"]["Execution_ProtocolVersionPayload"];
      icf_signed: boolean;
      /** Format: date-time */
      icf_signed_date: null | string;
      /** @default false */
      requires_reconsent: boolean;
      /** @default false */
      is_paper_override: boolean;
    };
    Execution_SubjectConsentResponse: {
      id: components["schemas"]["Execution_SubjectConsentId"];
      subject_id: components["schemas"]["Execution_SubjectCode"];
      study_id: components["schemas"]["Execution_StudyCode"];
      version_tag: string;
      /** Format: int32 */
      version_index: number | string;
      icf_signed: boolean;
      /** Format: date-time */
      icf_signed_date: null | string;
      requires_reconsent: boolean;
      is_paper_override: boolean;
      /** Format: int64 */
      version: number | string;
    };
    Execution_SubjectCreateRequest: {
      subject_id: string;
      study_id: string;
      site_id: null | string;
      arm_id: null | string;
      demographics:
        null | components["schemas"]["Execution_DemographicsPayload"];
    };
    Execution_SubjectDemographicsUpdateRequest: {
      demographics:
        null | components["schemas"]["Execution_DemographicsPayload"];
      strat_factors: null | Record<string, never>;
    };
    Execution_SubjectDetailResponse: {
      subject_id: components["schemas"]["Execution_SubjectCode"];
      study_id: components["schemas"]["Execution_StudyCode"];
      status: string;
      site_id: null | components["schemas"]["Execution_SiteCode"];
      treatment_group: null | string;
      randomization_seed: null | string;
      investigational_product_id: null | string;
    };
    Execution_SubjectId: string;
    Execution_SubjectListResponse: {
      items: components["schemas"]["Execution_SubjectSummaryResponse"][];
      /** Format: int32 */
      total: number | string;
      /** Format: int32 */
      limit: number | string;
      /** Format: int32 */
      offset: number | string;
    };
    Execution_SubjectResponse: {
      id: components["schemas"]["Execution_SubjectId"];
      subject_id: components["schemas"]["Execution_SubjectCode"];
      study_id: components["schemas"]["Execution_StudyCode"];
      site_id: null | components["schemas"]["Execution_SiteCode"];
      treatment_group: null | string;
      status: string;
      active_protocol_version: null | string;
      encrypted_demographics: null | string;
    };
    Execution_SubjectStateUpdateRequest: {
      status: null | string;
      state: null | string;
      target?: null | string;
    };
    Execution_SubjectSummaryResponse: {
      id: components["schemas"]["Execution_SubjectId"];
      subject_id: components["schemas"]["Execution_SubjectCode"];
      study_id: components["schemas"]["Execution_StudyCode"];
      site_id: null | components["schemas"]["Execution_SiteCode"];
      status: string;
      active_protocol_version: string;
      /** Format: int32 */
      enrollment_index: number | string;
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
  ExecutionHealthCheck: {
    parameters: {
      query?: never;
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
          "application/json": components["schemas"]["Execution_AnonymousTypeOfstringAndstringAndstring"];
        };
      };
    };
  };
  EtmfCompletenessWebhook: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_EtmfCompletenessWebhookPayload"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_OperationEnvelopeResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_HttpValidationProblemDetails"];
        };
      };
    };
  };
  ActivateSite: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        siteId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_SiteActivationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_OperationEnvelopeResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  GetSiteComplianceStatus: {
    parameters: {
      query: {
        study_id: string;
        milestone?: string;
      };
      header?: never;
      path: {
        siteId: string;
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
          "application/json": components["schemas"]["Execution_ComplianceStatusResponse"];
        };
      };
    };
  };
  ListSubjects: {
    parameters: {
      query?: {
        study_id?: string;
        site_id?: string;
        status?: string;
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
          "application/json": components["schemas"]["Execution_SubjectListResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  CreateSubject: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_SubjectCreateRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_SubjectResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_HttpValidationProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  RecordSubjectConsent: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        subjectId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_SubjectConsentRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_SubjectConsentResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  UpdateSubjectState: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        subjectId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_SubjectStateUpdateRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_SubjectResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  GetSubjectDetail: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        subjectId: string;
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
          "application/json": components["schemas"]["Execution_SubjectDetailResponse"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  UpdateSubjectDemographics: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        subjectId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_SubjectDemographicsUpdateRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_SubjectResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  DeleteSubjectDemographics: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        subjectId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  CreateClinicalObservation: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_ObservationCreateRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_ObservationResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  GetObservationById: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
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
          "application/json": components["schemas"]["Execution_ObservationResponse"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  ListObservationsBySubject: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyId: string;
        subjectId: string;
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
          "application/json": components["schemas"]["Execution_ObservationResponse"][];
        };
      };
    };
  };
  VerifyObservationSdv: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
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
          "application/json": components["schemas"]["Execution_ObservationResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  FlagObservationSdv: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_FlagObservationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_ObservationResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  CreateFormSubmission: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_FormSubmissionCreateRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_FormSubmissionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  GetFormSubmissionById: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
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
          "application/json": components["schemas"]["Execution_FormSubmissionResponse"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  UpdateFormPayload: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_FormPayloadUpdateRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_FormSubmissionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Locked */
      423: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  ListFormSubmissionsBySubject: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyId: string;
        subjectId: string;
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
          "application/json": components["schemas"]["Execution_FormSubmissionResponse"][];
        };
      };
    };
  };
  CompleteFormSubmission: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
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
          "application/json": components["schemas"]["Execution_FormSubmissionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  ApproveFormSubmission: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_FormSubmissionApproveRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_FormSubmissionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  UnlockFormSubmission: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Execution_FormUnlockRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_FormSubmissionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  ListClinicalQueries: {
    parameters: {
      query?: {
        study_id?: string;
        subject_id?: string;
        visit_id?: string;
        status?: string;
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
          "application/json": components["schemas"]["Execution_ClinicalQueryResponse"][];
        };
      };
    };
  };
  CreateClinicalQuery: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_QueryCreateRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_ClinicalQueryResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  GetClinicalQueryById: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
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
          "application/json": components["schemas"]["Execution_ClinicalQueryResponse"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  RespondClinicalQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_QueryRespondRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_ClinicalQueryResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  CloseClinicalQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
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
          "application/json": components["schemas"]["Execution_ClinicalQueryResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  ReopenClinicalQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_QueryReopenRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_ClinicalQueryResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  CancelClinicalQuery: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_QueryCancelRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_ClinicalQueryResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  ChairAdjudicateDossier: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_ChairAdjudicateRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_ChairAdjudicationResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  GetCecDossierById: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
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
          "application/json": components["schemas"]["Execution_CecDossier"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  PreflightLockCheck: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_PreflightCheckRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_PreflightCheckResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  LockData: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_DataLockExecuteRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_DataLockRecordResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  UnlockData: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_DataLockUnlockRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_DataLockRecordResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Execution_ProblemDetails"];
        };
      };
    };
  };
  ListLocks: {
    parameters: {
      query?: {
        study_id?: string;
        scope_type?: string;
        scope_id?: string;
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
          "application/json": components["schemas"]["Execution_DataLockRecordResponse"][];
        };
      };
    };
  };
}
