/**
 * Domain Formatter Composable
 * Converts database identifiers, column keys, and technical attributes
 * into standard human-readable clinical terminology.
 */

const DOMAIN_LABEL_MAP: Record<string, string> = {
  subject_id: "Subject Identifier",
  subjectId: "Subject Identifier",
  subject_uuid: "Subject Identifier",
  subjectUuid: "Subject Identifier",
  "Subject UUID": "Subject Identifier",
  "Subject ID / UUID": "Subject Identifier",
  collection_site_id: "Collection Site",
  collectionSiteId: "Collection Site",
  collection_site: "Collection Site",
  manufacturing_facility_id: "Manufacturing Facility",
  manufacturingFacilityId: "Manufacturing Facility",
  cmo: "Manufacturing Facility",
  study_id: "Study Protocol",
  studyId: "Study Protocol",
  protocol_id: "Study Protocol",
  protocolId: "Study Protocol",
  site_id: "Site Identifier",
  siteId: "Site Identifier",
  batch_id: "Manufacturing Batch",
  batchId: "Manufacturing Batch",
  din: "Donation ID (ISBT 128 DIN)",
  lot_number: "Lot Number",
  lotNumber: "Lot Number",
  dry_shipper_id: "Dry Shipper ID",
  dryShipperId: "Dry Shipper ID",
  collector_id: "Collector Identifier",
  collectorId: "Collector Identifier",
  collector_name: "Collector Name",
  collectorName: "Collector Name",
};

/**
 * Converts snake_case or camelCase keys to Title Case sentences.
 */
function toTitleCase(str: string): string {
  if (!str) return "";
  return str
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .replace(/\b\w/g, (char) => char.toUpperCase())
    .trim();
}

export function useDomainFormatter() {
  /**
   * Transforms a raw database key or technical label into standard clinical terminology.
   */
  function formatLabel(keyOrLabel: string): string {
    if (!keyOrLabel) return "";
    const trimmed = keyOrLabel.trim();
    if (DOMAIN_LABEL_MAP[trimmed]) {
      return DOMAIN_LABEL_MAP[trimmed];
    }
    const lowerKey = trimmed.toLowerCase();
    if (DOMAIN_LABEL_MAP[lowerKey]) {
      return DOMAIN_LABEL_MAP[lowerKey];
    }
    return toTitleCase(trimmed);
  }

  /**
   * Formats a database column name or key.
   */
  function formatDatabaseKey(key: string): string {
    return formatLabel(key);
  }

  /**
   * Formats a subject identifier for display.
   */
  function formatSubjectId(id: string | null | undefined): string {
    if (!id) return "N/A";
    return String(id).trim();
  }

  /**
   * Formats a facility code or CMO ID into human-readable facility text.
   */
  function formatFacilityCode(code: string | null | undefined): string {
    if (!code) return "N/A";
    const str = String(code).trim();
    if (str === "In Queue" || str === "N/A") return str;
    return str;
  }

  return {
    formatLabel,
    formatDatabaseKey,
    formatSubjectId,
    formatFacilityCode,
  };
}
