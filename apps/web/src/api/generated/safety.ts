/**
 * Generated from docs/openapi/safety_openapi.json by scripts/generate-api-types.js.
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
    /** Clinical Safety & Pharmacovigilance health probe. */
    get: operations["SafetyHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns Safety operational status. */
    get: operations["SafetyStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/icsr/render": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Renders an ICH E2B(R3) ICSR as an HL7 v3 MCCI_IN200100UV01 message validated against the ICH schema; a report that breaks an E2B(R3) conformance rule is refused with 400 naming each member. */
    post: operations["RenderIcsrReport"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/dsur/compile": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Compiles an aggregate DSUR periodic safety report with patient-years exposure and MedDRA cross-tabulation. */
    post: operations["CompileDsurReport"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/dsur/{id}/approve": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Signs and locks a DSUR as the verified caller: needs safety_report:approve, a step-up signature token bound to the report and a reason for change; the compiler cannot approve. */
    post: operations["ApproveDsurReport"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/dsur/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieves a DSUR periodic safety report record by identifier. */
    get: operations["GetDsurReportById"];
    /** Updates a draft DSUR periodic safety report record. Returns 409 Conflict if LOCKED. */
    put: operations["UpdateDsurReport"];
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/dsur": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists all DSUR periodic safety report records. */
    get: operations["ListDsurReports"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/periodic-reports/compile": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["CompilePeriodicReport"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/periodic-reports/{id}/approve": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["ApprovePeriodicReport"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/periodic-reports/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetPeriodicReportById"];
    put: operations["UpdatePeriodicReport"];
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/safety/periodic-reports": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListPeriodicReports"];
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
    Safety_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Safety_DsurApproveRequest: {
      signingReason: null | string;
      reasonForChange: null | string;
      /** Format: int32 */
      expectedVersionIndex?: null | number | string;
    };
    Safety_DsurCompileRequest: {
      compoundName: string;
      studyId: null | string;
      /** Format: date-time */
      periodStart: string;
      /** Format: date-time */
      periodEnd: string;
      reasonForChange: null | string;
      subjectEnrollments?:
        null | components["schemas"]["Safety_SubjectEnrollmentRecord"][];
      safetyCases?:
        null | components["schemas"]["Safety_SafetyCaseIcsrInput"][];
    };
    Safety_DsurUpdateRequest: {
      compoundName: null | string;
      studyId: null | string;
      /** Format: date-time */
      periodStart: null | string;
      /** Format: date-time */
      periodEnd: null | string;
      reasonForChange: null | string;
      /** Format: int32 */
      expectedVersionIndex?: null | number | string;
    };
    Safety_HttpValidationProblemDetails: {
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
    Safety_IcsrDrug: {
      drugRole: string;
      drugName: string;
      activeSubstanceName?: null | string;
      dosageText?: null | string;
      routeOfAdministration?: null | string;
      actionTakenWithDrug?: null | string;
    };
    Safety_IcsrHeader: {
      batchNumber: string;
      batchSenderIdentifier: string;
      batchReceiverIdentifier: string;
      batchTransmissionDate: string;
      messageIdentifier: string;
      messageSenderIdentifier: string;
      messageReceiverIdentifier: string;
    };
    Safety_IcsrMeddraCoding: {
      version: string;
      lltCode: string;
    };
    Safety_IcsrOtherCaseIdentifier: {
      source: string;
      caseIdentifier: string;
    };
    Safety_IcsrPatient: {
      patientId?: null | string;
      sex?: null | string;
      /** Format: int32 */
      age?: null | number | string;
      ageUnit?: null | string;
      birthDate?: null | string;
    };
    Safety_IcsrPrimarySource: {
      qualification: string;
      countryCode?: null | string;
      /** @default false */
      primaryForRegulatoryPurposes: boolean;
    };
    Safety_IcsrReaction: {
      meddraCoding: components["schemas"]["Safety_IcsrMeddraCoding"];
      outcome: string;
      reactionTerm?: null | string;
      reactionTermLanguage?: null | string;
      startDate?: null | string;
      endDate?: null | string;
      /** @default false */
      resultsInDeath: boolean;
      /** @default false */
      lifeThreatening: boolean;
      /** @default false */
      causedOrProlongedHospitalisation: boolean;
      /** @default false */
      disablingIncapacitating: boolean;
      /** @default false */
      congenitalAnomalyBirthDefect: boolean;
      /** @default false */
      otherMedicallyImportantCondition: boolean;
    };
    Safety_IcsrReport: {
      header: components["schemas"]["Safety_IcsrHeader"];
      safetyReport: components["schemas"]["Safety_IcsrSafetyReport"];
      primarySources: components["schemas"]["Safety_IcsrPrimarySource"][];
      sender: components["schemas"]["Safety_IcsrSender"];
      patient: components["schemas"]["Safety_IcsrPatient"];
      reactions: components["schemas"]["Safety_IcsrReaction"][];
      drugs: components["schemas"]["Safety_IcsrDrug"][];
      narrative: string;
      study?: null | components["schemas"]["Safety_IcsrStudy"];
    };
    Safety_IcsrSafetyReport: {
      senderSafetyReportId: string;
      creationDate: string;
      reportType: string;
      firstReceivedDate: string;
      mostRecentInformationDate: string;
      fulfilsLocalCriteriaForExpeditedReport: null | boolean;
      worldwideUniqueCaseId: string;
      firstSender: string;
      documentsHeldBySender?: null | string[];
      otherCaseIdentifiers?:
        null | components["schemas"]["Safety_IcsrOtherCaseIdentifier"][];
    };
    Safety_IcsrSender: {
      senderType: string;
      organisation?: null | string;
    };
    Safety_IcsrStudy: {
      studyType: string;
      sponsorStudyNumber?: null | string;
      studyName?: null | string;
    };
    Safety_PeriodicSafetyReportRecord: {
      id?: string;
      reportType?: string;
      studyId?: string;
      compoundName?: string;
      /** Format: date-time */
      periodStart?: string;
      /** Format: date-time */
      periodEnd?: string;
      status?: string;
      /** Format: double */
      totalPatientYearsExposure?: number | string;
      /** Format: int32 */
      totalSaes?: number | string;
      reportPayloadJson?: string;
      approvedByUserId?: null | string;
      /** Format: date-time */
      approvedAt?: null | string;
      esignatureManifestId?: null | string;
      /** Format: date-time */
      createdAt: string;
      createdBy?: string;
      reasonForChange?: string;
      /** Format: int32 */
      versionIndex?: number | string;
    };
    Safety_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Safety_SafetyCaseIcsrInput: {
      caseId: string;
      subjectId: string;
      studyId: string;
      /** Format: date-time */
      receiptDate: string;
      aeTerm: string;
      meddraPt: string;
      meddraSoc: string;
      isSerious: boolean;
      isFatal: boolean;
      isAesi: boolean;
      aesiCategory: null | string;
      causality: string;
      expectedness: string;
      treatmentArm: string;
      actionTaken: string;
      /** Format: date-time */
      onsetDate: string;
      /** Format: date-time */
      resolutionDate: null | string;
    };
    Safety_SubjectEnrollmentRecord: {
      subjectId: string;
      studyId: string;
      treatmentArm: string;
      /** Format: date-time */
      enrollmentDate: string;
      /** Format: date-time */
      discontinuationDate: null | string;
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
  SafetyHealthCheck: {
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
          "application/json": components["schemas"]["Safety_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  SafetyStatus: {
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
          "application/json": components["schemas"]["Safety_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  RenderIcsrReport: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Safety_IcsrReport"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/xml": string;
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_HttpValidationProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Internal Server Error */
      500: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
    };
  };
  CompileDsurReport: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Safety_DsurCompileRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Safety_PeriodicSafetyReportRecord"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
    };
  };
  ApproveDsurReport: {
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
          null | components["schemas"]["Safety_DsurApproveRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Safety_PeriodicSafetyReportRecord"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
    };
  };
  GetDsurReportById: {
    parameters: {
      query?: {
        revision?: number | string;
        as_of?: string;
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
          "application/json": components["schemas"]["Safety_PeriodicSafetyReportRecord"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
    };
  };
  UpdateDsurReport: {
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
        "application/json": components["schemas"]["Safety_DsurUpdateRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Safety_PeriodicSafetyReportRecord"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
    };
  };
  ListDsurReports: {
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
          "application/json": components["schemas"]["Safety_PeriodicSafetyReportRecord"][];
        };
      };
    };
  };
  CompilePeriodicReport: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Safety_DsurCompileRequest"];
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
  ApprovePeriodicReport: {
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
          null | components["schemas"]["Safety_DsurApproveRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Safety_PeriodicSafetyReportRecord"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Safety_ProblemDetails"];
        };
      };
    };
  };
  GetPeriodicReportById: {
    parameters: {
      query?: {
        revision?: number | string;
        as_of?: string;
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
        content?: never;
      };
    };
  };
  UpdatePeriodicReport: {
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
        "application/json": components["schemas"]["Safety_DsurUpdateRequest"];
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
  ListPeriodicReports: {
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
}
