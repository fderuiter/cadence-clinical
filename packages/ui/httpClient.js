export class HttpClient {
  constructor(config = {}) {
    this.baseUrl = config.baseUrl || "";
    this.authResolver = config.authResolver || null;
  }

  async request(path, options = {}) {
    const {
      method = "GET",
      headers = {},
      body,
      changeReason,
      responseType, // "json" | "text" | "blob" | "arrayBuffer" | "raw"
      ...customOptions
    } = options;

    const requestHeaders = { ...headers };

    // 1. Dynamic authorization resolving
    const resolvedAuth = this.authResolver ? await this.authResolver(options) : null;
    if (resolvedAuth) {
      if (typeof resolvedAuth === "string") {
        requestHeaders["Authorization"] = `Bearer ${resolvedAuth}`;
      } else if (typeof resolvedAuth === "object") {
        Object.assign(requestHeaders, resolvedAuth);
      }
    }

    // 2. Body Payload handling (JSON vs FormData)
    const isFormData = body instanceof globalThis.FormData || (typeof FormData !== "undefined" && body instanceof FormData);

    if (!isFormData) {
      if (!requestHeaders["Content-Type"] && !requestHeaders["content-type"]) {
        requestHeaders["Content-Type"] = "application/json";
      }
    } else {
      // Browser must set boundary automatically for multi-part FormData
      delete requestHeaders["Content-Type"];
      delete requestHeaders["content-type"];
    }

    // 3. GxP Compliance Header Injection
    const upperMethod = method.toUpperCase();
    const isMutation = ["POST", "PUT", "DELETE", "PATCH"].includes(upperMethod);
    const resolvedChangeReason =
      changeReason ||
      options.change_reason ||
      requestHeaders["X-Change-Reason"] ||
      requestHeaders["x-change-reason"];

    if (isMutation && resolvedChangeReason) {
      requestHeaders["X-Change-Reason"] = resolvedChangeReason;
    }

    // 4. Base URL formatting
    const resolvedBaseUrl = typeof this.baseUrl === "function" ? this.baseUrl() : this.baseUrl;
    let url = path;
    if (resolvedBaseUrl && !path.startsWith("http://") && !path.startsWith("https://") && !options.relative) {
      const cleanBaseUrl = resolvedBaseUrl.endsWith("/") ? resolvedBaseUrl.slice(0, -1) : resolvedBaseUrl;
      const cleanPath = path.startsWith("/") ? path : `/${path}`;
      url = `${cleanBaseUrl}${cleanPath}`;
    }

    const fetchOptions = {
      method: upperMethod,
      headers: requestHeaders,
      ...customOptions,
    };

    if (body !== undefined && body !== null) {
      if (isFormData) {
        fetchOptions.body = body;
      } else if (typeof body === "string") {
        fetchOptions.body = body;
      } else {
        fetchOptions.body = JSON.stringify(body);
      }
    }

    const response = await fetch(url, fetchOptions);

    if (responseType === "raw") {
      return response;
    }

    if (!response.ok) {
      let data = null;
      try {
        data = await response.json();
      } catch {
        // Not JSON
      }
      throw new HttpClientError(
        data?.detail ||
          data?.message ||
          `HTTP Error ${response.status}`,
        response.status,
        response.statusText,
        data
      );
    }

    if (responseType === "blob") {
      return typeof response.blob === "function"
        ? await response.blob()
        : typeof response.text === "function"
        ? new Blob([await response.text()])
        : new Blob();
    } else if (responseType === "text") {
      return await response.text();
    } else if (responseType === "arrayBuffer") {
      return await response.arrayBuffer();
    }

    if (response.status === 204) {
      return null;
    }

    return await response.json();
  }

  get(path, options = {}) {
    return this.request(path, { ...options, method: "GET" });
  }

  post(path, body, options = {}) {
    return this.request(path, { ...options, method: "POST", body });
  }

  put(path, body, options = {}) {
    return this.request(path, { ...options, method: "PUT", body });
  }

  patch(path, body, options = {}) {
    return this.request(path, { ...options, method: "PATCH", body });
  }

  delete(path, options = {}) {
    return this.request(path, { ...options, method: "DELETE" });
  }
}

export class HttpClientError extends Error {
  constructor(message, status = null, statusText = null, data = null) {
    super(message);
    this.name = "HttpClientError";
    this.status = status;
    this.statusText = statusText;
    this.data = data;
  }
}
