import { canonicalSerialize } from "./signing.js";

export interface VerificationResult {
  is_valid: boolean;
  status: string;
  failure_reason?: string;
}

export class ComplianceSDK {
  private backendUrl: string;

  constructor(backendUrl: string = "") {
    this.backendUrl = backendUrl;
  }

  /**
   * Serialize payload using RFC 8785 deterministic standards.
   */
  public serialize(payload: any): string {
    const res = canonicalSerialize(payload);
    if (res === undefined) {
      throw new Error("Cannot serialize undefined payload under RFC 8785 JCS.");
    }
    return res;
  }

  /**
   * Generates PKCS#7 signature by delegating to the backend service.
   * Secure X.509 keys never enter the JS runtime.
   */
  public async generateSignature(payload: any): Promise<string> {
    const serialized = this.serialize(payload);
    const response = await fetch(
      `${this.backendUrl}/api/v1/execution/signatures/sign`,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ data: serialized }),
      }
    );

    if (!response.ok) {
      throw new Error(
        `Backend signature generation failed: ${response.statusText}`
      );
    }

    const resData = await response.json();
    return resData.signed_data;
  }

  /**
   * Verifies PKCS#7 signature by querying the secure Python backend.
   * Fails and blocks if local/client-side bypass attempts are detected.
   */
  public async verifySignature(
    signedData: string,
    bypassCertificateStore: boolean = false
  ): Promise<VerificationResult> {
    // Guardrail: Fail-closed if there is any attempt to bypass the certificate store via local/client-side validation options
    if (bypassCertificateStore) {
      return {
        is_valid: false,
        status: "BYPASS_ATTEMPT_BLOCKED",
        failure_reason:
          "Client-side validation bypass of the certificate store is strictly prohibited.",
      };
    }

    const response = await fetch(
      `${this.backendUrl}/api/v1/execution/signatures/verify`,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ signed_data: signedData }),
      }
    );

    if (!response.ok) {
      return {
        is_valid: false,
        status: "BACKEND_ERROR",
        failure_reason: `Backend verification query failed: ${response.statusText}`,
      };
    }

    return (await response.json()) as VerificationResult;
  }
}
