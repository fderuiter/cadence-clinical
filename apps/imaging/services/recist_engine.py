"""RECIST 1.1 Oncologic Tumor Response Derivation Engine.

Implements Response Evaluation Criteria in Solid Tumors (RECIST v1.1) guidelines:
- Sum of Longest Diameters (SLD) for target lesions
- Baseline reference comparison (>= 30% decrease for PR)
- Nadir tracking and absolute progression (+20% and >= 5 mm absolute increase for PD)
- Lymph node short-axis criterion (< 10 mm normal, >= 15 mm measurable target)
- Non-target lesion qualitative progression and new lesion appearance
- Longitudinal timepoint trajectory evaluation

Requirements: ADR-2199, PRD-IMG-004, PRD-SYS-001
"""

from apps.imaging.domain.models import (
    LesionCategory,
    OncologicCriteria,
    RECIST11ResponseResult,
    RECISTLesionInput,
    RECISTResponseCode,
)


class RECIST11Engine:
    """Core domain engine for RECIST 1.1 oncologic tumor measurement derivations.

    @req:PRD-IMG-004
    """

    PR_THRESHOLD_PCT = -30.0  # >= 30% decrease from baseline
    PD_THRESHOLD_PCT = 20.0  # >= 20% increase from nadir
    PD_ABSOLUTE_MIN_MM = 5.0  # >= 5 mm absolute increase from nadir
    NORMAL_LYMPH_NODE_MAX_MM = 10.0  # Normal lymph node short axis < 10 mm
    TARGET_LYMPH_NODE_MIN_MM = 15.0  # Measurable target lymph node short axis >= 15 mm
    TARGET_NON_NODAL_MIN_MM = (
        10.0  # Measurable target non-nodal longest diameter >= 10 mm
    )
    MAX_TARGET_LESIONS_TOTAL = 5
    MAX_TARGET_LESIONS_PER_ORGAN = 2

    @classmethod
    def calculate_sld(cls, lesions: list[RECISTLesionInput]) -> float:
        """Calculates the Sum of Longest Diameters (SLD) across target lesions in mm.

        For non-nodal lesions, uses longest diameter.
        For nodal lesions (lymph nodes), uses short axis diameter.

        Args:
            lesions: List of target lesion input measurements.

        Returns:
            Sum of diameters in millimeters, rounded to 2 decimal places.
        """
        total = 0.0
        for lesion in lesions:
            if lesion.lesion_type == LesionCategory.TARGET:
                if lesion.is_lymph_node and lesion.short_axis_mm is not None:
                    total += lesion.short_axis_mm
                else:
                    total += lesion.longest_diameter_mm
        return round(total, 2)

    @classmethod
    def are_lymph_nodes_normal(cls, lesions: list[RECISTLesionInput]) -> bool:
        """Checks if all target lymph nodes have regressed to normal short axis (< 10 mm)."""
        for lesion in lesions:
            if lesion.lesion_type == LesionCategory.TARGET and lesion.is_lymph_node:
                sa = (
                    lesion.short_axis_mm
                    if lesion.short_axis_mm is not None
                    else lesion.longest_diameter_mm
                )
                if sa >= cls.NORMAL_LYMPH_NODE_MAX_MM:
                    return False
        return True

    @classmethod
    def evaluate_target_response(
        cls,
        current_sld: float,
        baseline_sld: float,
        nadir_sld: float,
        all_nodes_normal: bool = True,
        is_evaluable: bool = True,
    ) -> RECISTResponseCode:
        """Derives RECIST 1.1 target lesion response category.

        Args:
            current_sld: Current timepoint Sum of Longest Diameters in mm.
            baseline_sld: Baseline timepoint Sum of Longest Diameters in mm.
            nadir_sld: Smallest recorded SLD on study up to this timepoint in mm.
            all_nodes_normal: Whether all target lymph nodes are < 10 mm short axis.
            is_evaluable: Whether target lesions are evaluable at this timepoint.

        Returns:
            Target response category (CR, PR, SD, PD, NE).
        """
        if not is_evaluable:
            return RECISTResponseCode.NE

        # Check Progressive Disease (PD): >= 20% increase from nadir AND >= 5 mm absolute increase
        if nadir_sld > 0:
            pct_change_nadir = ((current_sld - nadir_sld) / nadir_sld) * 100.0
            absolute_increase = current_sld - nadir_sld
            if (
                pct_change_nadir >= cls.PD_THRESHOLD_PCT
                and absolute_increase >= cls.PD_ABSOLUTE_MIN_MM
            ):
                return RECISTResponseCode.PD
        elif current_sld >= cls.PD_ABSOLUTE_MIN_MM:
            # If nadir was 0, an increase to >= 5mm is PD
            return RECISTResponseCode.PD

        # Check Complete Response (CR): Disappearance of all target lesions and normal lymph nodes
        if current_sld <= 0.0 and all_nodes_normal:
            return RECISTResponseCode.CR

        # Check Partial Response (PR): >= 30% decrease from baseline
        if baseline_sld > 0:
            pct_change_baseline = ((current_sld - baseline_sld) / baseline_sld) * 100.0
            if pct_change_baseline <= cls.PR_THRESHOLD_PCT:
                return RECISTResponseCode.PR

        # Otherwise Stable Disease (SD)
        return RECISTResponseCode.SD

    @classmethod
    def evaluate_overall_response(
        cls,
        target_response: RECISTResponseCode,
        non_target_status: str = "NON_CR_NON_PD",
        new_lesions_present: bool = False,
    ) -> RECISTResponseCode:
        """Derives RECIST 1.1 overall timepoint response from target, non-target, and new lesions.

        Follows standard RECIST 1.1 Table 4 response matrix.

        Args:
            target_response: Derived target lesion response (CR, PR, SD, PD, NE).
            non_target_status: Non-target lesion status (CR, NON_CR_NON_PD, PD, NE, NOT_APPLICABLE).
            new_lesions_present: Whether new malignant lesions have appeared.

        Returns:
            Overall RECIST 1.1 response category.
        """
        # Rule 1: Any new lesion is unequivocal Progressive Disease (PD)
        if new_lesions_present:
            return RECISTResponseCode.PD

        # Rule 2: Unequivocal progression in target OR non-target is PD
        if (
            target_response == RECISTResponseCode.PD
            or non_target_status.upper() == "PD"
        ):
            return RECISTResponseCode.PD

        # Rule 3: Target CR
        if target_response == RECISTResponseCode.CR:
            if non_target_status.upper() in ("CR", "NOT_APPLICABLE"):
                return RECISTResponseCode.CR
            if (
                non_target_status.upper() in ("NON_CR_NON_PD", "PERSISTENT")
                or non_target_status.upper() == "NE"
            ):
                return RECISTResponseCode.PR

        # Rule 4: Target PR
        if target_response == RECISTResponseCode.PR:
            if non_target_status.upper() in (
                "CR",
                "NON_CR_NON_PD",
                "PERSISTENT",
                "NE",
                "NOT_APPLICABLE",
            ):
                return RECISTResponseCode.PR

        # Rule 5: Target SD
        if target_response == RECISTResponseCode.SD:
            if non_target_status.upper() in (
                "CR",
                "NON_CR_NON_PD",
                "PERSISTENT",
                "NE",
                "NOT_APPLICABLE",
            ):
                return RECISTResponseCode.SD

        # Rule 6: Target NE
        if target_response == RECISTResponseCode.NE:
            if non_target_status.upper() in (
                "CR",
                "NON_CR_NON_PD",
                "PERSISTENT",
                "NOT_APPLICABLE",
            ):
                return RECISTResponseCode.NE

        return RECISTResponseCode.NE

    @classmethod
    def evaluate_timepoint(
        cls,
        current_lesions: list[RECISTLesionInput],
        baseline_sld: float,
        prior_timepoints_sld: list[float] | None = None,
        non_target_status: str = "NON_CR_NON_PD",
        new_lesions_present: bool = False,
        new_lesions_count: int = 0,
        is_baseline: bool = False,
    ) -> RECIST11ResponseResult:
        """Performs comprehensive RECIST 1.1 timepoint evaluation.

        Args:
            current_lesions: List of target lesion measurements for this timepoint.
            baseline_sld: Baseline SLD in mm (or 0.0 if this is baseline).
            prior_timepoints_sld: Optional list of SLDs from previous timepoints.
            non_target_status: Non-target evaluation ("CR", "NON_CR_NON_PD", "PD", "NE").
            new_lesions_present: True if any new lesion appeared.
            new_lesions_count: Count of new lesions.
            is_baseline: True if this timepoint is the study baseline.

        Returns:
            RECIST11ResponseResult with quantitative deltas and categorical classification.
        """
        current_sld = cls.calculate_sld(current_lesions)
        all_nodes_normal = cls.are_lymph_nodes_normal(current_lesions)

        if is_baseline:
            return RECIST11ResponseResult(
                criteria=OncologicCriteria.RECIST_1_1,
                baseline_sld_mm=current_sld,
                nadir_sld_mm=current_sld,
                current_sld_mm=current_sld,
                pct_change_baseline=0.0,
                pct_change_nadir=0.0,
                absolute_change_nadir_mm=0.0,
                target_response=RECISTResponseCode.SD,
                non_target_status=non_target_status,
                new_lesions_present=False,
                new_lesions_count=0,
                overall_response=RECISTResponseCode.SD,
                is_nadir=True,
                details={
                    "is_baseline": True,
                    "target_lesions_count": len(
                        [
                            lesion
                            for lesion in current_lesions
                            if lesion.lesion_type == LesionCategory.TARGET
                        ]
                    ),
                    "all_nodes_normal": all_nodes_normal,
                },
            )

        effective_baseline = baseline_sld if baseline_sld > 0 else current_sld
        all_slds = [effective_baseline]
        if prior_timepoints_sld:
            all_slds.extend(prior_timepoints_sld)

        # Nadir is the smallest SLD observed since baseline (including baseline)
        nadir_sld = min(all_slds)
        is_nadir = current_sld <= nadir_sld

        # Percentage change from baseline
        pct_baseline = 0.0
        if effective_baseline > 0:
            pct_baseline = round(
                ((current_sld - effective_baseline) / effective_baseline) * 100.0,
                2,
            )

        # Percentage change and absolute delta from nadir
        pct_nadir = 0.0
        if nadir_sld > 0:
            pct_nadir = round(((current_sld - nadir_sld) / nadir_sld) * 100.0, 2)
        abs_nadir = round(current_sld - nadir_sld, 2)

        target_resp = cls.evaluate_target_response(
            current_sld=current_sld,
            baseline_sld=effective_baseline,
            nadir_sld=nadir_sld,
            all_nodes_normal=all_nodes_normal,
        )

        overall_resp = cls.evaluate_overall_response(
            target_response=target_resp,
            non_target_status=non_target_status,
            new_lesions_present=new_lesions_present,
        )

        return RECIST11ResponseResult(
            criteria=OncologicCriteria.RECIST_1_1,
            baseline_sld_mm=effective_baseline,
            nadir_sld_mm=nadir_sld,
            current_sld_mm=current_sld,
            pct_change_baseline=pct_baseline,
            pct_change_nadir=pct_nadir,
            absolute_change_nadir_mm=abs_nadir,
            target_response=target_resp,
            non_target_status=non_target_status,
            new_lesions_present=new_lesions_present,
            new_lesions_count=new_lesions_count,
            overall_response=overall_resp,
            is_nadir=is_nadir,
            details={
                "target_lesions_count": len(
                    [
                        lesion
                        for lesion in current_lesions
                        if lesion.lesion_type == LesionCategory.TARGET
                    ]
                ),
                "all_nodes_normal": all_nodes_normal,
            },
        )
