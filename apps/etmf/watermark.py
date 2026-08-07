"""
Watermarking helper module re-exporting apply_watermark from apps.execution.src.domain.watermark.
"""

from apps.execution.src.domain.watermark import apply_watermark

__all__ = ["apply_watermark"]
