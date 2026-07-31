"""Dependencies for apps.designer routers.

Requirements: PRD-SYS-001
"""

from apps.designer.services.artifact_cascade import ArtifactCascadeEngine
from apps.designer.services.quality_sentinel import ProtocolQualitySentinel


def get_cascade_engine() -> ArtifactCascadeEngine:
    """Return an instance of ArtifactCascadeEngine.

    Requirements: PRD-SYS-001
    """
    return ArtifactCascadeEngine()


def get_quality_sentinel() -> ProtocolQualitySentinel:
    """Return an instance of ProtocolQualitySentinel.

    Requirements: PRD-SYS-001
    """
    return ProtocolQualitySentinel()
