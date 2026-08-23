"""Digital Data Flow (DDF) Deep Compiler Platform Package."""

from packages.ddf.compiler import DDFCompiler
from packages.ddf.models import (
    AccessibilityReport,
    CompileOptions,
    CompilerWarning,
    DDFCompilationResult,
    DDFItemIR,
    DDFStudyIR,
    ItemLocalization,
)
from packages.ddf.ports import AccessibilityValidatorPort
from packages.ddf.rules.compiler import (
    compile_condition_to_xpath,
    compile_to_xpath,
    extract_appearance,
    sanitize_identifier,
)

__all__ = [
    "AccessibilityReport",
    "AccessibilityValidatorPort",
    "CompileOptions",
    "CompilerWarning",
    "DDFCompilationResult",
    "DDFCompiler",
    "DDFItemIR",
    "DDFStudyIR",
    "ItemLocalization",
    "compile_condition_to_xpath",
    "compile_to_xpath",
    "extract_appearance",
    "sanitize_identifier",
]
