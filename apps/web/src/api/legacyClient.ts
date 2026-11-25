import { createOpenApiClient } from "./client";
import type { paths } from "./types";

export const openApiClient = createOpenApiClient<paths>();

export default openApiClient;
