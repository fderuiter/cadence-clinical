import { buildLedgerBlock } from "ui";
import { demoNowIso } from "../../utils/demoMode.js";
import {
  writeStorageItem,
  type ClinicalState,
  type LedgerBlock,
} from "../clinicalScope";
import { assertValidUsdmGraph } from "./clinicalUsdm";

export function createClinicalLedgerState(
  savedLedgerBlocks: LedgerBlock[] | null = null
): Pick<ClinicalState, "ledgerBlocks"> {
  return {
    ledgerBlocks: savedLedgerBlocks || [],
  };
}

export function createClinicalLedgerActions() {
  return {
    async addLedgerBlock(
      this: ClinicalState,
      action: string,
      details: Record<string, unknown>,
      reason: string = "System Action"
    ): Promise<LedgerBlock> {
      const index = this.ledgerBlocks.length;
      const timestamp = demoNowIso(index);
      const prevHash =
        index === 0
          ? "0000000000000000000000000000000000000000000000000000000000000000" // deid-ignore
          : this.ledgerBlocks[index - 1].hash;

      const block = (await buildLedgerBlock(
        index,
        timestamp,
        action,
        details,
        reason,
        prevHash
      )) as unknown as LedgerBlock;
      block.synced = false;

      // Validate USDM Graph before persisting state to localStorage!
      assertValidUsdmGraph(
        this,
        "Local storage persistence blocked by USDM Graph Validator"
      );

      this.ledgerBlocks.push(block);

      // Save persistent fields to localStorage
      if (typeof window !== "undefined" && window.localStorage) {
        writeStorageItem("subjects", this.subjects);
        writeStorageItem("formValues", this.formValues);
        writeStorageItem("formQueries", this.formQueries);
        writeStorageItem("ledgerBlocks", this.ledgerBlocks);
        writeStorageItem("currentUsdm", this.currentUsdm);
      }

      return block;
    },

    clearLedger(this: ClinicalState) {
      this.ledgerBlocks = [];
      if (typeof window !== "undefined" && window.localStorage) {
        writeStorageItem("ledgerBlocks", this.ledgerBlocks);
      }
    },
  };
}
