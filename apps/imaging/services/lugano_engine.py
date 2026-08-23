"""Lugano 2014 and Cheson Oncologic Response Derivation Engine for Lymphoma.

Implements Lugano 2014 classification criteria for lymphoma clinical trials:
- Bidimensional Product of Perpendicular Diameters (PPD = LDi * SDi)
- Sum of the Product of Perpendicular Diameters (SPD) across up to 6 target lesions
- Baseline SPD reference comparison (>= 50% decrease for PR)
- Nadir SPD tracking (>= 50% increase from nadir or single lesion progression for PD)
- PET Deauville 5-point scale scoring (CMR, PMR, NMR, PMD)
- Integrated PET/CT metabolic and anatomic tumor response derivations

Requirements: ADR-2199, PRD-IMG-004, PRD-SYS-001
"""

from apps.imaging.domain.models import (
    LuganoLesionInput,
    LuganoResponseCode,
    LuganoResponseResult,
    OncologicCriteria,
)


class LuganoEngine:
    """Core domain engine for Lugano 2014 & Cheson lymphoma response criteria.

    @req:PRD-IMG-004
    """

    PR_SPD_DECREASE_THRESHOLD_PCT = -50.0  # >= 50% decrease in SPD from baseline
    PD_SPD_INCREASE_THRESHOLD_PCT = 50.0  # >= 50% increase in SPD from nadir
    NORMAL_NODE_LDI_MAX_MM = 15.0  # Target node regression to <= 15 mm in LDi
    MAX_TARGET_LESIONS = 6  # Up to 6 representative nodal and extranodal lesions

    @classmethod
    def calculate_ppd(
        cls, longest_diameter_mm: float, perpendicular_diameter_mm: float
    ) -> float:
        """Calculates the Product of Perpendicular Diameters (PPD) in mm^2."""
        return round(longest_diameter_mm * perpendicular_diameter_mm, 2)

    @classmethod
    def calculate_spd(cls, lesions: list[LuganoLesionInput]) -> float:
        """Calculates the Sum of the Product of Perpendicular Diameters (SPD) across target lesions in mm^2.

        Args:
            lesions: List of target lesion input measurements.

        Returns:
            Sum of PPDs in mm^2, rounded to 2 decimal places.
        """
        total = 0.0
        for lesion in lesions:
            ppd = cls.calculate_ppd(
                lesion.longest_diameter_mm, lesion.perpendicular_diameter_mm
            )
            total += ppd
        return round(total, 2)

    @classmethod
    def are_all_nodes_normal(cls, lesions: list[LuganoLesionInput]) -> bool:
        """Checks if all target nodal lesions have regressed to <= 15 mm in longest transverse diameter."""
        for lesion in lesions:
            if not lesion.is_extranodal:
                if lesion.longest_diameter_mm > cls.NORMAL_NODE_LDI_MAX_MM:
                    return False
        return True

    @classmethod
    def evaluate_ct_response(
        cls,
        current_spd: float,
        baseline_spd: float,
        nadir_spd: float,
        all_nodes_normal: bool = True,
        single_lesion_progression: bool = False,
        non_measured_progression: bool = False,
        new_lesions_present: bool = False,
    ) -> LuganoResponseCode:
        """Derives Lugano CT-based anatomical response.

        Args:
            current_spd: Current timepoint Sum of Products in mm^2.
            baseline_spd: Baseline Sum of Products in mm^2.
            nadir_spd: Nadir Sum of Products in mm^2.
            all_nodes_normal: Whether all target nodes are <= 15 mm in LDi.
            single_lesion_progression: True if any single lesion increased >= 50% from nadir PPD.
            non_measured_progression: True if non-measured lesions showed unequivocal progression.
            new_lesions_present: True if any new lesion appeared.

        Returns:
            LuganoResponseCode (CR, PR, SD, PD, NE).
        """
        if new_lesions_present or non_measured_progression or single_lesion_progression:
            return LuganoResponseCode.PD

        # Progressive Disease (PD): >= 50% increase from nadir SPD
        if nadir_spd > 0:
            pct_change_nadir = ((current_spd - nadir_spd) / nadir_spd) * 100.0
            if pct_change_nadir >= cls.PD_SPD_INCREASE_THRESHOLD_PCT:
                return LuganoResponseCode.PD

        # Complete Response (CR): Normalization of all target nodes <= 15 mm LDi and disappearance of extranodal
        if all_nodes_normal and current_spd <= (
            cls.NORMAL_NODE_LDI_MAX_MM * cls.NORMAL_NODE_LDI_MAX_MM * len([1])
        ):
            # If all nodes <= 15mm and no extranodal disease
            if baseline_spd > 0:
                pct_change_baseline = (
                    (current_spd - baseline_spd) / baseline_spd
                ) * 100.0
                if pct_change_baseline <= cls.PR_SPD_DECREASE_THRESHOLD_PCT:
                    return LuganoResponseCode.CR

        # Partial Response (PR): >= 50% decrease in SPD from baseline
        if baseline_spd > 0:
            pct_change_baseline = ((current_spd - baseline_spd) / baseline_spd) * 100.0
            if pct_change_baseline <= cls.PR_SPD_DECREASE_THRESHOLD_PCT:
                return LuganoResponseCode.PR

        # Stable Disease (SD): < 50% decrease from baseline to < 50% increase from nadir
        return LuganoResponseCode.SD

    @classmethod
    def evaluate_pet_response(
        cls,
        deauville_score: int,
        baseline_score: int | None = None,
        new_fdg_avid_lesions: bool = False,
    ) -> LuganoResponseCode:
        """Derives PET Metabolic Response based on the 5-point Deauville criteria.

        Score 1: No uptake above background.
        Score 2: Uptake <= mediastinum.
        Score 3: Uptake > mediastinum but <= liver.
        Score 4: Uptake moderately increased above liver.
        Score 5: Uptake markedly increased above liver and/or new lesions.

        Returns:
            CMR (Complete Metabolic Response), PMR (Partial), NMR (No Response), PMD (Progressive Disease).
        """
        if new_fdg_avid_lesions:
            return LuganoResponseCode.PMD

        if deauville_score in (1, 2, 3):
            return LuganoResponseCode.CMR
        if deauville_score in (4, 5):
            if baseline_score is not None and deauville_score < baseline_score:
                return LuganoResponseCode.PMR
            if baseline_score is not None and deauville_score > baseline_score:
                return LuganoResponseCode.PMD
            if baseline_score is None:
                return LuganoResponseCode.PMR
            return LuganoResponseCode.NMR

        return LuganoResponseCode.NE

    @classmethod
    def evaluate_overall_lugano(
        cls,
        ct_response: LuganoResponseCode,
        pet_response: LuganoResponseCode | None = None,
    ) -> LuganoResponseCode:
        """Combines CT and PET metabolic evaluations into final Lugano response.

        When PET is available, PET metabolic response (CMR, PMR, NMR, PMD) takes precedence.
        """
        if pet_response is not None:
            return pet_response
        return ct_response

    @classmethod
    def evaluate_timepoint(
        cls,
        current_lesions: list[LuganoLesionInput],
        baseline_spd: float,
        prior_timepoints_spd: list[float] | None = None,
        new_lesions_present: bool = False,
        single_lesion_progression: bool = False,
        non_measured_progression: bool = False,
        is_baseline: bool = False,
    ) -> LuganoResponseResult:
        """Performs full Lugano 2014 timepoint response derivation."""
        current_spd = cls.calculate_spd(current_lesions)
        all_nodes_normal = cls.are_all_nodes_normal(current_lesions)

        # Calculate max Deauville score if present
        scores = [
            lesion.deauville_score
            for lesion in current_lesions
            if lesion.deauville_score is not None
        ]
        max_deauville = max(scores) if scores else None

        if is_baseline:
            pet_resp = None
            if max_deauville is not None:
                pet_resp = cls.evaluate_pet_response(max_deauville)

            return LuganoResponseResult(
                criteria=OncologicCriteria.LUGANO_2014,
                baseline_spd_mm2=current_spd,
                nadir_spd_mm2=current_spd,
                current_spd_mm2=current_spd,
                pct_change_spd_baseline=0.0,
                pct_change_spd_nadir=0.0,
                ct_response=LuganoResponseCode.SD,
                pet_metabolic_response=pet_resp,
                max_deauville_score=max_deauville,
                new_lesions_present=False,
                overall_response=pet_resp or LuganoResponseCode.SD,
                details={
                    "is_baseline": True,
                    "target_lesions_count": len(current_lesions),
                },
            )

        effective_baseline = baseline_spd if baseline_spd > 0 else current_spd
        all_spds = [effective_baseline]
        if prior_timepoints_spd:
            all_spds.extend(prior_timepoints_spd)

        nadir_spd = min(all_spds)

        pct_baseline = 0.0
        if effective_baseline > 0:
            pct_baseline = round(
                ((current_spd - effective_baseline) / effective_baseline) * 100.0, 2
            )

        pct_nadir = 0.0
        if nadir_spd > 0:
            pct_nadir = round(((current_spd - nadir_spd) / nadir_spd) * 100.0, 2)

        ct_resp = cls.evaluate_ct_response(
            current_spd=current_spd,
            baseline_spd=effective_baseline,
            nadir_spd=nadir_spd,
            all_nodes_normal=all_nodes_normal,
            single_lesion_progression=single_lesion_progression,
            non_measured_progression=non_measured_progression,
            new_lesions_present=new_lesions_present,
        )

        pet_resp = None
        if max_deauville is not None:
            pet_resp = cls.evaluate_pet_response(
                deauville_score=max_deauville,
                new_fdg_avid_lesions=new_lesions_present,
            )

        overall_resp = cls.evaluate_overall_lugano(ct_resp, pet_resp)

        return LuganoResponseResult(
            criteria=OncologicCriteria.LUGANO_2014,
            baseline_spd_mm2=effective_baseline,
            nadir_spd_mm2=nadir_spd,
            current_spd_mm2=current_spd,
            pct_change_spd_baseline=pct_baseline,
            pct_change_spd_nadir=pct_nadir,
            ct_response=ct_resp,
            pet_metabolic_response=pet_resp,
            max_deauville_score=max_deauville,
            new_lesions_present=new_lesions_present,
            overall_response=overall_resp,
            details={
                "target_lesions_count": len(current_lesions),
                "all_nodes_normal": all_nodes_normal,
            },
        )
