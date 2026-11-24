import { webDemoStore } from "./demoStore.js";
import { createAmendmentsAndSoaHandlers } from "./handlers/amendmentsAndSoaHandlers.js";
import { createCtmsAndFinancialsHandlers } from "./handlers/ctmsAndFinancialsHandlers.js";
import { createEconsentLabsDctAndTrainingHandlers } from "./handlers/econsentLabsDctAndTrainingHandlers.js";
import { createEtmfAndTicketsHandlers } from "./handlers/etmfAndTicketsHandlers.js";
import { createExecutionRtsmAndDataLocksHandlers } from "./handlers/executionRtsmAndDataLocksHandlers.js";
import { createExecutionSubjectsAndFormsHandlers } from "./handlers/executionSubjectsAndFormsHandlers.js";
import { createHealthAndAuthHandlers } from "./handlers/healthAndAuthHandlers.js";
import { createKernelObservationsHandlers } from "./handlers/kernelObservationsHandlers.js";
import { createKernelPlatformHandlers } from "./handlers/kernelPlatformHandlers.js";
import { createKernelStudyAndGovernanceHandlers } from "./handlers/kernelStudyAndGovernanceHandlers.js";
import { createKnowledgeAndTerminologyHandlers } from "./handlers/knowledgeAndTerminologyHandlers.js";
import { createPaymentsHandlers } from "./handlers/paymentsHandlers.js";

export function createWebDemoHandlers(store = webDemoStore) {
  return [
    ...createHealthAndAuthHandlers(store),
    ...createKernelPlatformHandlers(store),
    ...createKernelStudyAndGovernanceHandlers(store),
    ...createKernelObservationsHandlers(store),
    ...createExecutionSubjectsAndFormsHandlers(store),
    ...createExecutionRtsmAndDataLocksHandlers(store),
    ...createCtmsAndFinancialsHandlers(store),
    ...createKnowledgeAndTerminologyHandlers(store),
    ...createEtmfAndTicketsHandlers(store),
    ...createPaymentsHandlers(store),
    ...createAmendmentsAndSoaHandlers(store),
    ...createEconsentLabsDctAndTrainingHandlers(),
  ];
}
