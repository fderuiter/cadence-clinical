"""USDM Protocol Normalization Stage for DDF Compiler.

Performs schema validation, metadata fallback synthesis, and structural normalization
for incoming CDISC USDM study payloads.
"""

from __future__ import annotations

import copy
import uuid
from typing import Any

from packages.ddf.models import CompilerWarning


def _collect_ids(obj: Any, current_path: str, id_map: dict[str, list[str]]) -> None:
    """Recursively collects all element IDs and their paths."""
    if isinstance(obj, dict):
        if "id" in obj and obj["id"]:
            eid = str(obj["id"])
            id_map.setdefault(eid, []).append(current_path or "root")
        for k, v in obj.items():
            if k != "id":
                new_path = f"{current_path}.{k}" if current_path else k
                _collect_ids(v, new_path, id_map)
    elif isinstance(obj, list):
        for idx, item in enumerate(obj):
            new_path = f"{current_path}[{idx}]"
            _collect_ids(item, new_path, id_map)


class USDMNormalizerStage:
    """Normalizes raw USDM protocol payloads into canonical schema structures."""

    def __init__(self, strict_validation: bool = True) -> None:
        self.strict_validation = strict_validation

    def normalize(
        self, study_id: str, payload: dict[str, Any]
    ) -> tuple[dict[str, Any], list[CompilerWarning]]:
        """Validates and structurally normalizes incoming USDM protocol payloads.

        Args:
            study_id: The identifier of the clinical study.
            payload: The raw protocol payload dictionary.

        Returns:
            A tuple of (normalized_payload, compiler_warnings).

        Raises:
            ValueError: If the payload is invalid or contains critical schema violations.
        """
        if not payload or not isinstance(payload, dict):
            raise ValueError("Payload must be a dictionary.")
        if "protocol" not in payload:
            raise ValueError(
                "Validation Failed: 'protocol' missing from study definition."
            )

        warnings: list[CompilerWarning] = []
        v_payload = copy.deepcopy(payload)

        # Enforce UUID id or fallback to satisfy schema checks
        if "id" not in v_payload or not v_payload["id"]:
            try:
                uuid.UUID(str(study_id))
                fallback_val = str(study_id)
            except ValueError:
                fallback_val = "00000000-0000-0000-0000-000000000001"  # deid-ignore
            v_payload["id"] = fallback_val
            warnings.append(
                CompilerWarning(
                    field="study_id",
                    generated_value=fallback_val,
                    warning_reason="Missing required field 'study_id'; auto-populated fallback UUID.",
                )
            )

        if "name" not in v_payload or not v_payload["name"]:
            v_payload["name"] = "Default Study Name"
            warnings.append(
                CompilerWarning(
                    field="name",
                    generated_value="Default Study Name",
                    warning_reason="Missing required field 'name'; auto-populated fallback name.",
                )
            )

        # Auto-populate missing required USDM model fields for documentedBy items
        if "documentedBy" in v_payload and isinstance(v_payload["documentedBy"], list):
            for doc_idx, doc in enumerate(v_payload["documentedBy"]):
                if not isinstance(doc, dict):
                    continue
                if "id" not in doc or not doc.get("id"):
                    fallback_doc_id = (
                        f"00000000-0000-0000-0000-00000000100{doc_idx}"  # deid-ignore
                    )
                    doc["id"] = fallback_doc_id
                    warnings.append(
                        CompilerWarning(
                            field="documentedBy.id",
                            generated_value=fallback_doc_id,
                            warning_reason="Missing required field 'documentedBy.id'; auto-populated fallback UUID.",
                        )
                    )
                if "type" not in doc or not doc.get("type"):
                    doc["type"] = "Protocol"
                    warnings.append(
                        CompilerWarning(
                            field="documentedBy.type",
                            generated_value="Protocol",
                            warning_reason="Missing required field 'documentedBy.type'; auto-populated fallback type.",
                        )
                    )
                if "templateName" not in doc or not doc.get("templateName"):
                    doc["templateName"] = "Standard Template"
                    warnings.append(
                        CompilerWarning(
                            field="documentedBy.templateName",
                            generated_value="Standard Template",
                            warning_reason="Missing required field 'documentedBy.templateName'; auto-populated fallback template name.",
                        )
                    )
                if "instanceType" not in doc or not doc.get("instanceType"):
                    doc["instanceType"] = "StudyDefinitionDocument"
                    warnings.append(
                        CompilerWarning(
                            field="documentedBy.instanceType",
                            generated_value="StudyDefinitionDocument",
                            warning_reason="Missing required field 'documentedBy.instanceType'; auto-populated fallback instance type.",
                        )
                    )

                if "language" not in doc or not isinstance(doc.get("language"), dict):
                    fallback_lang_id = (
                        f"00000000-0000-0000-0000-00000000200{doc_idx}"  # deid-ignore
                    )
                    doc["language"] = {
                        "id": fallback_lang_id,
                        "codeSystem": "ISO 639-1",
                        "codeSystemVersion": "2002",
                        "code": "en",
                    }
                    warnings.append(
                        CompilerWarning(
                            field="documentedBy.language",
                            generated_value=doc["language"],
                            warning_reason="Missing required field 'documentedBy.language'; auto-populated fallback language object.",
                        )
                    )
                else:
                    lang = doc["language"]
                    if "id" not in lang or not lang.get("id"):
                        fallback_lang_id = f"00000000-0000-0000-0000-00000000200{doc_idx}"  # deid-ignore
                        lang["id"] = fallback_lang_id
                        warnings.append(
                            CompilerWarning(
                                field="documentedBy.language.id",
                                generated_value=fallback_lang_id,
                                warning_reason="Missing required field 'documentedBy.language.id'; auto-populated fallback UUID.",
                            )
                        )
                    if "codeSystem" not in lang or not lang.get("codeSystem"):
                        lang["codeSystem"] = "ISO 639-1"
                        warnings.append(
                            CompilerWarning(
                                field="documentedBy.language.codeSystem",
                                generated_value="ISO 639-1",
                                warning_reason="Missing required field 'documentedBy.language.codeSystem'; auto-populated fallback code system.",
                            )
                        )
                    if "codeSystemVersion" not in lang or not lang.get(
                        "codeSystemVersion"
                    ):
                        lang["codeSystemVersion"] = "2002"
                        warnings.append(
                            CompilerWarning(
                                field="documentedBy.language.codeSystemVersion",
                                generated_value="2002",
                                warning_reason="Missing required field 'documentedBy.language.codeSystemVersion'; auto-populated fallback code system version.",
                            )
                        )
                    if "code" not in lang or not lang.get("code"):
                        if "decode" in lang and lang.get("decode"):
                            fallback_code = str(lang["decode"]).lower()
                            lang["code"] = fallback_code
                            warnings.append(
                                CompilerWarning(
                                    field="documentedBy.language.code",
                                    generated_value=fallback_code,
                                    warning_reason=f"Missing required field 'documentedBy.language.code'; derived fallback from decode '{lang.get('decode')}'.",
                                )
                            )
                        else:
                            lang["code"] = "en"
                            warnings.append(
                                CompilerWarning(
                                    field="documentedBy.language.code",
                                    generated_value="en",
                                    warning_reason="Missing required field 'documentedBy.language.code'; auto-populated fallback language code.",
                                )
                            )

                if "versions" in doc and isinstance(doc["versions"], list):
                    for ver_idx, ver in enumerate(doc["versions"]):
                        if not isinstance(ver, dict):
                            continue
                        if "id" not in ver or not ver.get("id"):
                            fallback_ver_id = f"00000000-0000-0000-0000-00000000300{doc_idx}{ver_idx}"  # deid-ignore
                            ver["id"] = fallback_ver_id
                            warnings.append(
                                CompilerWarning(
                                    field="documentedBy.versions.id",
                                    generated_value=fallback_ver_id,
                                    warning_reason="Missing required field 'documentedBy.versions.id'; auto-populated fallback UUID.",
                                )
                            )
                        if "status" not in ver or not ver.get("status"):
                            ver["status"] = "Final"
                            warnings.append(
                                CompilerWarning(
                                    field="documentedBy.versions.status",
                                    generated_value="Final",
                                    warning_reason="Missing required field 'documentedBy.versions.status'; auto-populated fallback status.",
                                )
                            )
                        if "instanceType" not in ver or not ver.get("instanceType"):
                            ver["instanceType"] = "StudyDefinitionDocumentVersion"
                            warnings.append(
                                CompilerWarning(
                                    field="documentedBy.versions.instanceType",
                                    generated_value="StudyDefinitionDocumentVersion",
                                    warning_reason="Missing required field 'documentedBy.versions.instanceType'; auto-populated fallback instance type.",
                                )
                            )

                        if "contents" in ver and isinstance(ver["contents"], list):
                            for cnt_idx, content in enumerate(ver["contents"]):
                                if not isinstance(content, dict):
                                    continue
                                if "id" not in content or not content.get("id"):
                                    fallback_cnt_id = f"00000000-0000-0000-0000-00000000400{doc_idx}{ver_idx}{cnt_idx}"  # deid-ignore
                                    content["id"] = fallback_cnt_id
                                    warnings.append(
                                        CompilerWarning(
                                            field="documentedBy.versions.contents.id",
                                            generated_value=fallback_cnt_id,
                                            warning_reason="Missing required field 'documentedBy.versions.contents.id'; auto-populated fallback UUID.",
                                        )
                                    )
                                if (
                                    "displaySectionNumber" not in content
                                    or not content.get("displaySectionNumber")
                                ):
                                    fallback_sec_num = f"{cnt_idx + 1}"
                                    content["displaySectionNumber"] = fallback_sec_num
                                    warnings.append(
                                        CompilerWarning(
                                            field="documentedBy.versions.contents.displaySectionNumber",
                                            generated_value=fallback_sec_num,
                                            warning_reason="Missing required field 'documentedBy.versions.contents.displaySectionNumber'; auto-populated fallback section number.",
                                        )
                                    )
                                if (
                                    "displaySectionTitle" not in content
                                    or not content.get("displaySectionTitle")
                                ):
                                    content["displaySectionTitle"] = "Section Title"
                                    warnings.append(
                                        CompilerWarning(
                                            field="documentedBy.versions.contents.displaySectionTitle",
                                            generated_value="Section Title",
                                            warning_reason="Missing required field 'documentedBy.versions.contents.displaySectionTitle'; auto-populated fallback section title.",
                                        )
                                    )
                                if "instanceType" not in content or not content.get(
                                    "instanceType"
                                ):
                                    content["instanceType"] = "NarrativeContent"
                                    warnings.append(
                                        CompilerWarning(
                                            field="documentedBy.versions.contents.instanceType",
                                            generated_value="NarrativeContent",
                                            warning_reason="Missing required field 'documentedBy.versions.contents.instanceType'; auto-populated fallback instance type.",
                                        )
                                    )

        # Check duplicate IDs and structure
        id_map: dict[str, list[str]] = {}
        _collect_ids(v_payload, "", id_map)
        critical_errors: list[str] = []
        for eid, paths in id_map.items():
            if len(paths) > 1:
                critical_errors.append(
                    f"multiple_elements: Duplicate physical ID '{eid}' detected across: {', '.join(paths)}."
                )

        if not v_payload.get("id"):
            critical_errors.append("id: Study must contain a non-empty physical ID.")
        if not v_payload.get("name"):
            critical_errors.append(
                "name: Study must contain a non-empty physical name/title."
            )

        if critical_errors and self.strict_validation:
            raise ValueError(f"Validation Failed: {'; '.join(critical_errors)}")

        # Normalization of version/study roots
        normalized_payload = dict(v_payload)
        if "protocol" in payload:
            normalized_payload["protocol"] = payload["protocol"]

        return normalized_payload, warnings
