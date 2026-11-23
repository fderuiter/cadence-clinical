/**
 * Generated from docs/openapi/notifications_openapi.json by scripts/generate-api-types.js.
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
    /** Notifications dispatch health probe. */
    get: operations["NotificationsHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/notifications/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns Notifications operational status. */
    get: operations["NotificationsStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/notifications/render": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Renders a Liquid notification template asynchronously. */
    post: operations["RenderNotificationTemplate"];
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
    Notifications_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Notifications_NotificationPayload: {
      /** @default  */
      subjectId: string;
      /** @default  */
      adverseEventDescription: string;
      /** @default  */
      flagReason: string;
      /** @default  */
      discrepancySource: string;
      /** @default  */
      protocolVersion: string;
      /** @default  */
      versionNumber: string;
      /** @default  */
      changeSummary: string;
      /** @default  */
      summaryOfChanges: string;
      /** @default  */
      siteId: string;
      /** @default  */
      formName: string;
      /** @default  */
      fieldName: string;
      /** @default  */
      queryMessage: string;
      /** @default  */
      diaryName: string;
      /** @default  */
      dueDate: string;
      /** @default  */
      amendmentTag: string;
      /** @default  */
      versionIndex: string;
      /** @default  */
      changeReason: string;
      /** @default  */
      submittedBy: string;
      /** @default  */
      documentName: string;
      /** @default  */
      artifactCode: string;
      /** @default  */
      expirationDate: string;
    };
    Notifications_NotificationRenderModel: {
      studyId: string;
      eventId: string;
      timestampUtc: string;
      payload: components["schemas"]["Notifications_NotificationPayload"];
    };
    Notifications_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Notifications_RenderedNotification: {
      templateName: string;
      content: string;
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
  NotificationsHealthCheck: {
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
          "application/json": components["schemas"]["Notifications_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  NotificationsStatus: {
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
          "application/json": components["schemas"]["Notifications_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  RenderNotificationTemplate: {
    parameters: {
      query: {
        templateName: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Notifications_NotificationRenderModel"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Notifications_RenderedNotification"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Notifications_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Notifications_ProblemDetails"];
        };
      };
    };
  };
}
