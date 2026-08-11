import { apiClient, apiConfig, ApiError } from "shared-api-client";
import { useAuthStore } from "../stores/auth";
import { generateGatewaySignature } from "ui";

apiConfig.setTokenProvider(() => {
  try {
    const authStore = useAuthStore();
    return authStore?.token || authStore?.accessToken;
  } catch (e) {
    return null;
  }
});

apiConfig.setSignatureGenerator(generateGatewaySignature);

export const getBaseUrl = () => apiConfig.getBaseUrl();
export { apiClient, ApiError };
