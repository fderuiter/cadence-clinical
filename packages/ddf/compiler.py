"""Deep Digital Data Flow (DDF) Compiler.

Transforms raw CDISC USDM study protocol definitions into validated CDISC ODM v1.3.2
and OpenRosa XForms XML layouts through isolated, hermetic pipeline stages.
"""

from __future__ import annotations

from typing import Any

from packages.ddf.models import (
    AccessibilityReport,
    CompileOptions,
    DDFCompilationResult,
)
from packages.ddf.ports import AccessibilityValidatorPort
from packages.ddf.stages.accessibility import LayoutAccessibilityStage
from packages.ddf.stages.ir_builder import DDFSemanticIRBuilderStage
from packages.ddf.stages.normalizer import USDMNormalizerStage
from packages.ddf.stages.xml_renderer import XmlLayoutRendererStage


class DDFCompiler:
    """Deep module orchestrating the 5-stage DDF compilation pipeline."""

    def __init__(
        self,
        normalizer: USDMNormalizerStage | None = None,
        ir_builder: DDFSemanticIRBuilderStage | None = None,
        renderer: XmlLayoutRendererStage | None = None,
        accessibility_stage: LayoutAccessibilityStage | None = None,
        accessibility_validator: AccessibilityValidatorPort | None = None,
    ) -> None:
        self.normalizer = normalizer or USDMNormalizerStage()
        self.ir_builder = ir_builder or DDFSemanticIRBuilderStage()
        self.renderer = renderer or XmlLayoutRendererStage()
        if accessibility_stage is not None:
            if accessibility_stage.validator is None:
                raise ValueError(
                    "Accessibility validator port is uninitialized or missing. "
                    "DDFCompiler requires an explicit AccessibilityValidatorPort instance."
                )
            self.accessibility_stage = accessibility_stage
        elif accessibility_validator is not None:
            self.accessibility_stage = LayoutAccessibilityStage(accessibility_validator)
        else:
            raise ValueError(
                "Accessibility validator port is uninitialized or missing. "
                "DDFCompiler requires an explicit AccessibilityValidatorPort instance."
            )

    async def compile(
        self,
        study_id: str,
        payload: dict[str, Any],
        options: CompileOptions | None = None,
    ) -> DDFCompilationResult:
        """Executes full DDF compilation on the supplied USDM study payload.

        Args:
            study_id: Unique identifier for the clinical study.
            payload: Raw or partially populated CDISC USDM study definition dictionary.
            options: Runtime compilation options (accessibility validation, strictness).

        Returns:
            DDFCompilationResult containing rendered XML strings, hash, and diagnostics.

        Raises:
            ValueError: If schema normalization, rule compilation, or accessibility fails.
        """
        opts = options or CompileOptions()

        # Stage 1: USDM Schema Normalization & Fallback Synthesis
        normalized_payload, warnings = self.normalizer.normalize(study_id, payload)

        # Stage 2 & 3: Semantic Intermediate Representation & XPath Rule Compilation
        study_ir = self.ir_builder.build_ir(study_id, normalized_payload)

        # Stage 4: CDISC ODM and OpenRosa XML Layout Rendering
        odm_xml, openrosa_xml, form_hash = self.renderer.render(study_ir)

        # Stage 5: Layout & Accessibility Verification
        accessibility_report: AccessibilityReport | None = None
        if opts.validate_accessibility:
            accessibility_report = await self.accessibility_stage.validate(
                openrosa_xml, form_hash
            )

        return DDFCompilationResult(
            study_id=study_id,
            name=study_ir.name,
            odm_xml=odm_xml,
            openrosa_xml=openrosa_xml,
            form_hash=form_hash,
            warnings=[w.to_dict() for w in warnings],
            accessibility_report=accessibility_report,
            status="COMPLETED",
        )
