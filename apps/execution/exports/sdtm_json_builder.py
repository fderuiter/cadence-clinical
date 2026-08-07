"""SDTM Dataset-JSON Builder with integrated HIPAA scrubber.

Requirements: PRD-SYS-001
"""

from typing import Any

from apps.execution.biostat.serializer import serialize_to_dataset_json
from apps.execution.services.deident_scrubber import HIPAADataScrubber, scrub_dataset
from apps.execution.src.domain.sdtm.scrubber_models import DeidentConfig


class SDTMJSONBuilder:
    """Builder coordinating the scrubbing of clinical records and serializing to Dataset-JSON.

    Requirements: PRD-SYS-001
    """

    def __init__(self, config: DeidentConfig):
        self.config = config
        self.scrubber = HIPAADataScrubber(study_salt=config.study_salt)

    def build_sdtm_dataset_json(
        self,
        study_id: str,
        domain: str,
        records: list[dict[str, Any]],
        supp_records: list[dict[str, Any]] | None = None,
    ) -> dict[str, Any]:
        """Scrubs clinical records and serializes them to CDISC Dataset-JSON format.

        Requirements: PRD-SYS-001
        """
        # Scrub the main records
        scrubbed_records, _ = scrub_dataset(records, self.config)

        export_data = {domain: scrubbed_records}

        if supp_records:
            scrubbed_supp, _ = scrub_dataset(supp_records, self.config)
            export_data[f"SUPP{domain}"] = scrubbed_supp

        # Serialize using the existing serialize_to_dataset_json helper
        dataset_json = serialize_to_dataset_json(
            data=export_data,
            study_id=study_id,
        )

        return dataset_json.model_dump()
