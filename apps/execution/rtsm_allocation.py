"""RTSM Randomization Strategies Module delegation wrapper.

Delegates and exposes the core RTSM randomization algorithms from the newly designed
`apps.execution.randomization` module.
"""

from apps.execution.randomization import (
    RandomizationConfigSchema as RandomizationConfigSchema,
    RTSMAllocator as RTSMAllocator,
    generate_canonical_stratum_key as generate_canonical_stratum_key,
)
