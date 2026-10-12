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
  "/api/v1/execution/batch-sign-off": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["BatchSignOff"];
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
  "/api/v1/execution/queries/batch-close": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["BatchCloseClinicalQueries"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/queries/batch-cancel": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["BatchCancelClinicalQueries"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/labs/toxicity/review-queue": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * List Staged Ae Review Queue
     * @description Retrieve laboratory Grade 3/4 toxicity triggers staged for Investigator review.
     */
    get: operations["ListStagedAeReviewQueue"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/labs/toxicity/review/{review_id}/action": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /**
     * Action Staged Ae Review
     * @description Perform Investigator workflow action on a staged Grade 3/4 AE (Confirm, Dismiss, Escalate).
     */
    post: operations["ActionStagedAeReview"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/labs/toxicity/evaluate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /**
     * Evaluate Lab Toxicity Endpoint
     * @description Evaluate CTCAE v5.0 Grade, Hy's Law criteria, and delta checks for a single lab observation.
     */
    post: operations["EvaluateLabToxicityEndpoint"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/lab-ranges": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Get reference ranges for study/site */
    get: operations["GetReferenceRanges"];
    put?: never;
    /** Create laboratory reference range */
    post: operations["CreateReferenceRange"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/lab-ranges/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Get reference range by ID */
    get: operations["GetReferenceRangeById"];
    /** Update laboratory reference range */
    put: operations["UpdateReferenceRange"];
    post?: never;
    /** Deactivate laboratory reference range */
    delete: operations["DeleteReferenceRange"];
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/labs/import/dry-run": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Pre-flight validation preview for bulk lab range CSV/XLSX file */
    post: operations["DryRunLabRangeImport"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/labs/import/commit": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Commit validated bulk lab ranges into persistent store with Part 11 audit logging */
    post: operations["CommitLabRangeImport"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/labs/batch-status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieve batch status history for bulk lab range imports */
    get: operations["GetLabBatchStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/labs/batch-status/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Get lab batch import job status by ID */
    get: operations["GetLabBatchStatusById"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/labs/convert-unit": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Convert clinical laboratory unit via UCUM SI formulas */
    post: operations["ConvertLabUnit"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/labs/loinc-catalog": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieve standardized LOINC catalog mappings */
    get: operations["GetLoincCatalog"];
    put?: never;
    post?: never;
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
  "/api/v1/execution/rtsm/cgt/apheresis": {
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
      requestBody: {
        content: {
          "application/json": components["schemas"]["Execution_CgtApheresisCreateRequest"];
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
  "/api/v1/execution/rtsm/cgt/milestone": {
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
      requestBody: {
        content: {
          "application/json": components["schemas"]["Execution_CgtMilestoneCreateRequest"];
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
  "/api/v1/execution/rtsm/cgt/manufacturing-bind": {
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
      requestBody: {
        content: {
          "application/json": components["schemas"]["Execution_CgtManufacturingBindRequest"];
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
  "/api/v1/execution/rtsm/cgt/bedside-verify": {
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
      requestBody: {
        content: {
          "application/json": components["schemas"]["Execution_CgtBedsideVerificationRequest"];
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
  "/api/v1/execution/rtsm/cgt/infuse": {
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
      requestBody: {
        content: {
          "application/json": components["schemas"]["Execution_CgtInfusionExecuteRequest"];
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
  "/api/v1/execution/rtsm/cgt/returns": {
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
      requestBody: {
        content: {
          "application/json": components["schemas"]["Execution_CgtVialReturnReconciliationRequest"];
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
  "/api/v1/execution/rtsm/cgt/destruction-certificate": {
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
      requestBody: {
        content: {
          "application/json": components["schemas"]["Execution_CgtDestructionCertificateCreateRequest"];
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
  "/api/v1/execution/rtsm/cgt/verify-barcode": {
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
      requestBody: {
        content: {
          "application/json": components["schemas"]["Execution_CgtBarcodeVerifyRequest"];
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
  "/api/v1/execution/rtsm/cgt/chain-of-custody/{studyId}/{subjectId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
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
          content?: never;
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/rtsm/cgt/certificates/{studyId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          studyId: string;
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
          content?: never;
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/rtsm/cgt/reconciliations/{studyId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          studyId: string;
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
          content?: never;
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/coding/synonyms/validate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Pre-flight validation report for bulk synonym dictionary CSV/XLSX file. */
    post: operations["ValidateSynonymFile"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/coding/synonyms/import": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Ingest validated synonym dictionary rules into persistent store with GxP audit lineage. */
    post: operations["ImportSynonymFile"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/coding/synonyms": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieve active synonym rules for study auto-coding engine. */
    get: operations["GetActiveSynonymRules"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/coding/synonyms/jobs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieve GxP audit ledger of bulk synonym import jobs. */
    get: operations["GetSynonymImportJobs"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/coding/synonyms/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post?: never;
    /** Deactivate an active synonym rule. */
    delete: operations["DeactivateSynonymRule"];
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/devices/parse-udi": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /**
     * Parse UDI Barcode
     * @description Parses and validates GS1-128, GS1-DataMatrix, and HIBCC barcode strings into structured UDI attributes.
     */
    post: operations["ParseUdi"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/devices/inventory": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * List Device Inventory
     * @description Retrieves investigational medical device inventory matching query filters.
     */
    get: operations["ListDeviceInventory"];
    put?: never;
    /**
     * Register Device Inventory
     * @description Registers serialized investigational medical device items in RTSM.
     */
    post: operations["RegisterDeviceInventory"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/devices/dispense": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /**
     * Dispense Device
     * @description Dispenses an investigational device to a study subject while enforcing quarantine and expiration gating rules.
     */
    post: operations["DispenseDevice"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/devices/deficiencies": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** List Device Deficiencies */
    get: operations["ListDeviceDeficiencies"];
    put?: never;
    /**
     * Log Device Deficiency
     * @description Logs a device deficiency/malfunction and triggers automated quarantine + safety cases.
     */
    post: operations["LogDeviceDeficiency"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/devices/deficiencies/{deficiency_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Get Device Deficiency */
    get: operations["GetDeviceDeficiencyById"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/devices/quarantine": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** List Device Quarantines */
    get: operations["ListDeviceQuarantines"];
    put?: never;
    /** Initiate Quarantine */
    post: operations["InitiateDeviceQuarantine"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/devices/quarantine/release": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Release Quarantine */
    post: operations["ReleaseDeviceQuarantine"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/execution/devices/investigation-summary": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Get Investigation Summary */
    get: operations["GetDeviceInvestigationSummary"];
    put?: never;
    post?: never;
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
    Execution_BatchQueryRequest: {
      query_ids: string[];
      reason?: null | string;
    };
    Execution_BatchQueryResponse: {
      queries: components["schemas"]["Execution_ClinicalQueryResponse"][];
      /** Format: int32 */
      count: number | string;
    };
    Execution_BatchSignOffRequest: {
      study_id: string;
      target_type: string;
      target_ids: string[];
      signing_reason: string;
    };
    Execution_BatchSignOffResponse: {
      status: string;
      approved_submission_ids: string[];
      skipped_submission_ids: string[];
      signature_id?: null | string;
      study_id?: null | string;
      subject_id?: null | string;
      /**
       * Format: int32
       * @default 0
       */
      signed_forms_count: number | string;
      content_digest?: null | string;
      timestamp_utc?: null | string;
      audit_tx?: null | string;
    };
    Execution_BulkLabRangeDryRunResponse: {
      is_success: boolean;
      /** Format: int32 */
      total_rows: number | string;
      /** Format: int32 */
      valid_rows: number | string;
      /** Format: int32 */
      invalid_rows: number | string;
      /** Format: int32 */
      loinc_mapped_count: number | string;
      /** Format: int32 */
      unit_converted_count: number | string;
      rows: components["schemas"]["Execution_BulkLabRangeRowPreview"][];
      summary: string;
      dry_run_id: string;
    };
    Execution_BulkLabRangeImportJobResponse: {
      job_id: string;
      study_id: string;
      site_id: null | string;
      file_name: string;
      file_type: string;
      status: string;
      /** Format: int32 */
      total_rows: number | string;
      /** Format: int32 */
      success_rows: number | string;
      /** Format: int32 */
      error_rows: number | string;
      error_summary_json: null | string;
      preview_summary_json: null | string;
      reason_for_change: string;
      submitted_by: string;
      /** Format: date-time */
      submitted_at: string;
      /** Format: date-time */
      completed_at: null | string;
    };
    Execution_BulkLabRangeRowPreview: {
      /** Format: int32 */
      row_number: number | string;
      is_valid: boolean;
      test_code: string;
      test_name: string;
      source: string;
      site_id: null | string;
      sex_applicability: string;
      /** Format: double */
      age_low: null | number | string;
      /** Format: double */
      age_high: null | number | string;
      unit: string;
      normalized_unit: string;
      /** Format: double */
      low_bound: null | number | string;
      /** Format: double */
      high_bound: null | number | string;
      /** Format: double */
      converted_low_bound: null | number | string;
      /** Format: double */
      converted_high_bound: null | number | string;
      /** Format: double */
      critical_low: null | number | string;
      /** Format: double */
      critical_high: null | number | string;
      loinc_code: null | string;
      /** Format: double */
      conversion_factor: null | number | string;
      errors: string[];
      warnings: string[];
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
    Execution_CgtApheresisCreateRequest: {
      study_id: string;
      collection_site_id: string;
      subject_id: string;
      din?: null | string;
      /** Format: date-time */
      collection_timestamp?: null | string;
      apheresis_bag_identifier?: null | string;
      /** Format: double */
      volume_ml?: null | number | string;
      /** Format: double */
      cell_count_billions?: null | number | string;
      custodian_id?: null | string;
      custodian_name?: null | string;
      custodian_role?: null | string;
      notes?: null | string;
    };
    Execution_CgtBarcodeVerifyRequest: {
      barcode_payload: string;
      expected_subject_id?: null | string;
      expected_din?: null | string;
      expected_batch_id?: null | string;
    };
    Execution_CgtBedsideVerificationRequest: {
      study_id: string;
      site_id: string;
      subject_id: string;
      din: string;
      batch_id: null | string;
      bag_barcode_payload: string;
      patient_wristband_id: string;
      primary_verifier_id: string;
      primary_verifier_name?: null | string;
      primary_verifier_role?: null | string;
      /** @default  */
      primary_signature_token: string;
      /** @default  */
      secondary_verifier_id: string;
      secondary_verifier_name?: null | string;
      secondary_verifier_role?: null | string;
      /** @default  */
      secondary_signature_token: string;
      notes?: null | string;
    };
    Execution_CgtDestructionCertificateCreateRequest: {
      study_id: string;
      site_id: string;
      lot_number: string;
      kit_numbers: string[];
      vial_identifiers: string[];
      destruction_reason: string;
      destruction_method: string;
      destruction_facility: string;
      witness_1_id: string;
      witness_1_name?: null | string;
      witness_1_role?: null | string;
      /** @default  */
      witness_1_signature_token: string;
      /** @default  */
      witness_2_id: string;
      witness_2_name?: null | string;
      witness_2_role?: null | string;
      /** @default  */
      witness_2_signature_token: string;
      /**
       * Format: int32
       * @default 1
       */
      quantity_destroyed: number | string;
      /** Format: double */
      residual_volume_ml?: null | number | string;
      subject_id?: null | string;
      din?: null | string;
      /** Format: uuid */
      coc_id?: null | string;
      notes?: null | string;
    };
    Execution_CgtInfusionExecuteRequest: {
      study_id: string;
      site_id: string;
      subject_id: string;
      /** Format: uuid */
      chain_of_custody_id: string;
      unlock_token: string;
      primary_verifier_id: string;
      /** Format: date-time */
      infusion_start?: null | string;
      /** Format: date-time */
      infusion_end?: null | string;
      /** Format: double */
      volume_infused_ml?: null | number | string;
      /** @default false */
      adverse_event_observed: boolean;
      notes?: null | string;
    };
    Execution_CgtManufacturingBindRequest: {
      study_id: string;
      /** Format: uuid */
      chain_of_custody_id: string;
      batch_id: string;
      manufacturing_facility_id: string;
      /** Format: date-time */
      synthesis_start?: null | string;
      /** Format: date-time */
      synthesis_end?: null | string;
      release_qc_status?: null | string;
      qualified_person_signoff_id?: null | string;
      notes?: null | string;
    };
    Execution_CgtMilestoneCreateRequest: {
      study_id: string;
      /** Format: uuid */
      chain_of_custody_id: string;
      stage: string;
      milestone_type: string;
      /** Format: date-time */
      timestamp?: null | string;
      location_name?: null | string;
      facility_id?: null | string;
      /** Format: double */
      latitude?: null | number | string;
      /** Format: double */
      longitude?: null | number | string;
      /** Format: double */
      temperature_celsius?: null | number | string;
      custodian_id?: null | string;
      custodian_name?: null | string;
      custodian_role?: null | string;
      carrier?: null | string;
      tracking_number?: null | string;
      /** @default true */
      seal_intact: boolean;
      seal_id?: null | string;
      dry_shipper_id?: null | string;
      notes?: null | string;
    };
    Execution_CgtVialReturnReconciliationRequest: {
      study_id: string;
      site_id: string;
      subject_id: string;
      kit_number: string;
      vial_identifier: string;
      /**
       * Format: int32
       * @default 1
       */
      returned_quantity: number | string;
      /**
       * Format: int32
       * @default 0
       */
      intact_vials: number | string;
      /**
       * Format: int32
       * @default 1
       */
      used_vials: number | string;
      /** Format: double */
      residual_volume_ml?: null | number | string;
      logged_by?: null | string;
      notes?: null | string;
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
    Execution_DemographicsPayload: {
      name: null | string;
      birthdate: null | string;
      gender: null | string;
      race: null | string;
    };
    Execution_DeviceDeficiencyCreateRequest: {
      study_id: string;
      site_id: string;
      udi_di: string;
      lot_number: string;
      deficiency_description: string;
      /** Format: date-time */
      incident_date: string;
      /** Format: date-time */
      awareness_date: string;
      subject_id?: null | string;
      device_inventory_id?: null | string;
      serial_number?: null | string;
      /** @default Investigational Medical Device */
      device_name: string;
      /** @default MALFUNCTION */
      deficiency_type: string;
      /** @default false */
      could_lead_to_sade: boolean;
      /** @default false */
      patient_involved: boolean;
      /** @default NONE */
      health_impact: string;
      /** @default QUARANTINED_AT_SITE */
      action_taken_device: string;
      /** @default false */
      device_returned_to_sponsor: boolean;
      return_tracking_number?: null | string;
      /** @default DEVICE_SERIAL */
      quarantine_scope: string;
      /** @default Report investigational medical device deficiency / malfunction */
      reason_for_change: string;
      signature_token?: null | string;
    };
    Execution_DeviceDeficiencyResponse: {
      id: string;
      study_id: string;
      site_id: string;
      subject_id: null | string;
      device_inventory_id: null | string;
      udi_di: string;
      lot_number: string;
      serial_number: null | string;
      device_name: string;
      deficiency_type: string;
      deficiency_description: string;
      /** Format: date-time */
      incident_date: string;
      /** Format: date-time */
      awareness_date: string;
      could_lead_to_sade: boolean;
      patient_involved: boolean;
      health_impact: string;
      action_taken_device: string;
      device_returned_to_sponsor: boolean;
      return_tracking_number: null | string;
      root_cause_analysis: null | string;
      sponsor_evaluation: null | string;
      quarantine_triggered: boolean;
      quarantine_scope: string;
      safety_case_id: null | string;
      safety_classification: null | string;
      investigation_status: string;
      /** Format: date-time */
      created_at: string;
      created_by: string;
      reason_for_change: string;
      /** Format: int64 */
      version_index: number | string;
    };
    Execution_DeviceDispensationRequest: {
      study_id: string;
      site_id: string;
      subject_id: string;
      device_inventory_id?: null | string;
      serial_number?: null | string;
      scanned_barcode?: null | string;
      /** @default Investigational device dispensation to participant */
      reason_for_change: string;
    };
    Execution_DeviceDispensationResponse: {
      status: string;
      message: string;
      device: components["schemas"]["Execution_DeviceInventoryResponse"];
      /** Format: date-time */
      dispensed_at: string;
    };
    Execution_DeviceInventoryCreateRequest: {
      study_id: string;
      site_id: string;
      brand_name: string;
      udi_di: string;
      lot_number: string;
      serial_number?: null | string;
      device_identifier?: null | string;
      device_model?: null | string;
      /** @default THERAPEUTIC */
      device_type: string;
      /** @default CLASS_IIA */
      device_classification: string;
      raw_barcode?: null | string;
      /** Format: date-time */
      expiration_date?: null | string;
      /** Format: date-time */
      manufacturing_date?: null | string;
      software_version?: null | string;
      /**
       * Format: int32
       * @default 1
       */
      quantity: number | string;
      /** @default Investigational medical device inventory intake */
      reason_for_change: string;
    };
    Execution_DeviceInventoryResponse: {
      id: string;
      study_id: string;
      site_id: string;
      device_identifier: string;
      brand_name: string;
      device_model: null | string;
      device_type: string;
      device_classification: string;
      udi_di: string;
      udi_pi: null | string;
      udi_carrier_format: string;
      lot_number: string;
      serial_number: null | string;
      /** Format: date-time */
      expiration_date: null | string;
      /** Format: date-time */
      manufacturing_date: null | string;
      software_version: null | string;
      status: string;
      is_dispensation_blocked: boolean;
      allocated_subject_id: null | string;
      /** Format: date-time */
      dispensed_at: null | string;
      dispensed_by: null | string;
      /** Format: date-time */
      quarantined_at: null | string;
      quarantined_by: null | string;
      quarantine_reason: null | string;
      /** Format: date-time */
      created_at: string;
      created_by: string;
      reason_for_change: string;
      /** Format: int64 */
      version_index: number | string;
    };
    Execution_DeviceInvestigationSummaryResponse: {
      study_id: string;
      /** Format: int32 */
      total_devices: number | string;
      /** Format: int32 */
      available_devices: number | string;
      /** Format: int32 */
      dispensed_devices: number | string;
      /** Format: int32 */
      quarantined_devices: number | string;
      /** Format: int32 */
      total_deficiencies: number | string;
      /** Format: int32 */
      open_safety_cases: number | string;
      /** Format: int32 */
      sade_count: number | string;
      /** Format: int32 */
      active_quarantines: number | string;
    };
    Execution_DeviceQuarantineCreateRequest: {
      study_id: string;
      site_id: string;
      reason: string;
      /** @default DEVICE_SERIAL */
      scope: string;
      device_inventory_id?: null | string;
      lot_number?: null | string;
      serial_number?: null | string;
      /** @default Initiate medical device quarantine */
      reason_for_change: string;
    };
    Execution_DeviceQuarantineReleaseRequest: {
      quarantine_id: string;
      release_justification: string;
      signature_token: string;
      /** @default Authorized medical device quarantine release */
      reason_for_change: string;
    };
    Execution_DeviceQuarantineResponse: {
      id: string;
      study_id: string;
      site_id: string;
      deficiency_id: null | string;
      device_inventory_id: null | string;
      scope: string;
      lot_number: null | string;
      serial_number: null | string;
      reason: string;
      status: string;
      /** Format: date-time */
      quarantined_at: string;
      quarantined_by: string;
      /** Format: date-time */
      released_at: null | string;
      released_by: null | string;
      release_justification: null | string;
      /** Format: date-time */
      created_at: string;
      created_by: string;
      reason_for_change: string;
      /** Format: int64 */
      version_index: number | string;
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
      form_hash?: null | string;
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
    Execution_LabReferenceRangeCreateRequest: {
      study_id: string;
      test_code: string;
      test_name: string;
      /** @default CENTRAL */
      source: string;
      site_id?: null | string;
      /** @default  */
      unit: string;
      normalized_unit?: null | string;
      /** @default ALL */
      sex_applicability: string;
      /** Format: double */
      age_low?: null | number | string;
      /** Format: double */
      age_high?: null | number | string;
      /** Format: double */
      low_bound?: null | number | string;
      /** Format: double */
      high_bound?: null | number | string;
      /** Format: double */
      critical_low?: null | number | string;
      /** Format: double */
      critical_high?: null | number | string;
      loinc_code?: null | string;
      /** @default Creating lab reference range */
      reason_for_change: string;
    };
    Execution_LabReferenceRangeResponse: {
      id: string;
      study_id: string;
      test_code: string;
      test_name: string;
      source: string;
      site_id: null | string;
      unit: string;
      normalized_unit: string;
      sex_applicability: string;
      /** Format: double */
      age_low: null | number | string;
      /** Format: double */
      age_high: null | number | string;
      /** Format: double */
      low_bound: null | number | string;
      /** Format: double */
      high_bound: null | number | string;
      /** Format: double */
      critical_low: null | number | string;
      /** Format: double */
      critical_high: null | number | string;
      loinc_code: null | string;
      is_active: boolean;
      /** Format: int64 */
      version: number | string;
      /** Format: date-time */
      created_at: string;
      created_by: string;
    };
    Execution_LabReferenceRangeUpdateRequest: {
      test_name?: null | string;
      unit?: null | string;
      normalized_unit?: null | string;
      sex_applicability?: null | string;
      /** Format: double */
      age_low?: null | number | string;
      /** Format: double */
      age_high?: null | number | string;
      /** Format: double */
      low_bound?: null | number | string;
      /** Format: double */
      high_bound?: null | number | string;
      /** Format: double */
      critical_low?: null | number | string;
      /** Format: double */
      critical_high?: null | number | string;
      loinc_code?: null | string;
      /** @default Updating lab reference range */
      reason_for_change: string;
    };
    Execution_LabToxicityEvaluateRequest: {
      test_code: string;
      /** Format: double */
      value: number | string;
      unit: string;
      /** Format: double */
      ref_low?: null | number | string;
      /** Format: double */
      ref_high?: null | number | string;
      /** Format: double */
      baseline_value?: null | number | string;
      /** Format: date-time */
      evaluated_at?: null | string;
    };
    Execution_LabToxicityEvaluateResponse: {
      test_code: string;
      test_name: string;
      /** Format: double */
      observed_value: number | string;
      unit: string;
      /** Format: int32 */
      ctcae_grade: number | string;
      grading_scale: string;
      toxicity_description: string;
      is_critical_ae_trigger: boolean;
      is_hys_law_warning: boolean;
      is_delta_check_alert: boolean;
      /** Format: double */
      delta_percentage: null | number | string;
    };
    Execution_LoincCatalogItemResponse: {
      test_code: string;
      test_name: string;
      loinc_code: string;
      standard_unit: string;
      category: string;
      /** Format: double */
      molecular_weight: null | number | string;
    };
    Execution_LoincCatalogResponse: {
      items: components["schemas"]["Execution_LoincCatalogItemResponse"][];
      /** Format: int32 */
      total_count: number | string;
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
      form_submission_id?: null | string;
      /** @default true */
      is_active: boolean;
    };
    Execution_OperationEnvelopeResponse: {
      status: string;
      message: string;
    };
    Execution_ParsedUdiResponse: {
      raw_barcode: string;
      udi_carrier_format: string;
      udi_di: string;
      lot_number: null | string;
      serial_number: null | string;
      expiration_date: null | string;
      manufacturing_date: null | string;
      software_version: null | string;
      is_valid: boolean;
      validation_errors: string[];
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
    Execution_StagedAdverseEventActionRequest: {
      action: string;
      reason_for_change: string;
      investigator_notes?: null | string;
      causality_assessment?: null | string;
      electronic_signature_password?: null | string;
    };
    Execution_StagedAdverseEventActionResponse: {
      id: string;
      review_status: string;
      action_taken: string;
      form_submission_id: null | string;
      query_id: null | string;
      message: string;
      /** Format: date-time */
      timestamp: string;
    };
    Execution_StagedAdverseEventReviewResponse: {
      id: string;
      study_id: string;
      site_id: null | string;
      subject_id: string;
      visit_id: null | string;
      observation_id: null | string;
      form_submission_id: null | string;
      query_id: null | string;
      test_code: string;
      test_name: string;
      /** Format: double */
      lab_value: number | string;
      unit: string;
      /** Format: int32 */
      ctcae_grade: number | string;
      grading_scale: string;
      toxicity_term: string;
      clinical_description: string;
      is_hys_law_warning: boolean;
      is_delta_check_alert: boolean;
      /** Format: double */
      delta_percentage: null | number | string;
      review_status: string;
      reviewed_by: null | string;
      /** Format: date-time */
      reviewed_at: null | string;
      investigator_notes: null | string;
      /** Format: int32 */
      version: number | string;
      is_deleted: boolean;
      /** Format: date-time */
      created_at: null | string;
    };
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
    Execution_UdiParseRequest: {
      raw_barcode: string;
    };
    Execution_UnitConversionRequest: {
      test_code: string;
      /** Format: double */
      value: number | string;
      from_unit: string;
      to_unit: string;
    };
    Execution_UnitConversionResponse: {
      test_code: string;
      /** Format: double */
      input_value: number | string;
      /** Format: double */
      converted_value: number | string;
      from_unit: string;
      to_unit: string;
      /** Format: double */
      factor: number | string;
      loinc_code: null | string;
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
  BatchSignOff: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_BatchSignOffRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_BatchSignOffResponse"];
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
      query?: {
        include_inactive?: boolean;
      };
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
      query?: {
        include_inactive?: boolean;
      };
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
  BatchCloseClinicalQueries: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_BatchQueryRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_BatchQueryResponse"];
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
  BatchCancelClinicalQueries: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_BatchQueryRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_BatchQueryResponse"];
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
  ListStagedAeReviewQueue: {
    parameters: {
      query?: {
        study_id?: string;
        site_id?: string;
        review_status?: string;
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
          "application/json": components["schemas"]["Execution_StagedAdverseEventReviewResponse"][];
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
  ActionStagedAeReview: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        review_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_StagedAdverseEventActionRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_StagedAdverseEventActionResponse"];
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
  EvaluateLabToxicityEndpoint: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_LabToxicityEvaluateRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_LabToxicityEvaluateResponse"];
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
  GetReferenceRanges: {
    parameters: {
      query?: {
        study_id?: string;
        studyId?: string;
        site_id?: string;
        siteId?: string;
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
          "application/json": components["schemas"]["Execution_LabReferenceRangeResponse"][];
        };
      };
    };
  };
  CreateReferenceRange: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_LabReferenceRangeCreateRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_LabReferenceRangeResponse"];
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
  GetReferenceRangeById: {
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
          "application/json": components["schemas"]["Execution_LabReferenceRangeResponse"];
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
  UpdateReferenceRange: {
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
        "application/json": components["schemas"]["Execution_LabReferenceRangeUpdateRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_LabReferenceRangeResponse"];
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
  DeleteReferenceRange: {
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
          "application/json": unknown;
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
  DryRunLabRangeImport: {
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
          "application/json": components["schemas"]["Execution_BulkLabRangeDryRunResponse"];
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
  CommitLabRangeImport: {
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
          "application/json": components["schemas"]["Execution_BulkLabRangeImportJobResponse"];
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
  GetLabBatchStatus: {
    parameters: {
      query?: {
        study_id?: string;
        studyId?: string;
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
          "application/json": components["schemas"]["Execution_BulkLabRangeImportJobResponse"][];
        };
      };
    };
  };
  GetLabBatchStatusById: {
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
          "application/json": components["schemas"]["Execution_BulkLabRangeImportJobResponse"];
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
  ConvertLabUnit: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_UnitConversionRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_UnitConversionResponse"];
        };
      };
    };
  };
  GetLoincCatalog: {
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
          "application/json": components["schemas"]["Execution_LoincCatalogResponse"];
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
  ValidateSynonymFile: {
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
        content?: never;
      };
    };
  };
  ImportSynonymFile: {
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
        content?: never;
      };
    };
  };
  GetActiveSynonymRules: {
    parameters: {
      query?: {
        study_id?: string;
        studyId?: string;
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
        content?: never;
      };
    };
  };
  GetSynonymImportJobs: {
    parameters: {
      query?: {
        study_id?: string;
        studyId?: string;
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
        content?: never;
      };
    };
  };
  DeactivateSynonymRule: {
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
        content?: never;
      };
    };
  };
  ParseUdi: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_UdiParseRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_ParsedUdiResponse"];
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
  ListDeviceInventory: {
    parameters: {
      query: {
        study_id: string;
        site_id?: string;
        status?: string;
        udi_di?: string;
        lot_number?: string;
        serial_number?: string;
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
          "application/json": components["schemas"]["Execution_DeviceInventoryResponse"][];
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
  RegisterDeviceInventory: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_DeviceInventoryCreateRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_DeviceInventoryResponse"][];
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
  DispenseDevice: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_DeviceDispensationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_DeviceDispensationResponse"];
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
  ListDeviceDeficiencies: {
    parameters: {
      query: {
        study_id: string;
        site_id?: string;
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
          "application/json": components["schemas"]["Execution_DeviceDeficiencyResponse"][];
        };
      };
    };
  };
  LogDeviceDeficiency: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_DeviceDeficiencyCreateRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_DeviceDeficiencyResponse"];
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
  GetDeviceDeficiencyById: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        deficiency_id: string;
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
          "application/json": components["schemas"]["Execution_DeviceDeficiencyResponse"];
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
  ListDeviceQuarantines: {
    parameters: {
      query: {
        study_id: string;
        site_id?: string;
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
          "application/json": components["schemas"]["Execution_DeviceQuarantineResponse"][];
        };
      };
    };
  };
  InitiateDeviceQuarantine: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_DeviceQuarantineCreateRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_DeviceQuarantineResponse"];
        };
      };
    };
  };
  ReleaseDeviceQuarantine: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Execution_DeviceQuarantineReleaseRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Execution_DeviceQuarantineResponse"];
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
  GetDeviceInvestigationSummary: {
    parameters: {
      query: {
        study_id: string;
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
          "application/json": components["schemas"]["Execution_DeviceInvestigationSummaryResponse"];
        };
      };
    };
  };
}
