"""Multi-Planar Reconstruction (MPR) & Caliper Geometry Engine for Medical Imaging.

Provides 3D spatial coordinate calculations, physical millimeter scaling from DICOM
pixel spacing, perpendicular crosshair caliper calculations, ellipsoidal volume estimation,
Hounsfield unit rescaling, and multi-plane view projection metadata.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-004
"""

import math
from typing import Any

from apps.imaging.domain.models import (
    MprPlane,
    MprVolumeMetadataEntity,
    PointCoordinate,
)


class MprGeometryEngine:
    """Core domain engine for DICOM MPR spatial geometry and caliper calculations.

    @req:PRD-IMG-001
    @req:PRD-IMG-004
    """

    DEFAULT_WINDOW_PRESETS: dict[str, dict[str, float]] = {
        "SOFT_TISSUE": {"window_center": 40.0, "window_width": 350.0},
        "LUNG": {"window_center": -600.0, "window_width": 1500.0},
        "BONE": {"window_center": 400.0, "window_width": 2000.0},
        "BRAIN": {"window_center": 40.0, "window_width": 80.0},
        "LIVER": {"window_center": 30.0, "window_width": 150.0},
        "MEDIASTINUM": {"window_center": 50.0, "window_width": 350.0},
    }

    @classmethod
    def calculate_distance_mm(
        cls,
        p1: PointCoordinate,
        p2: PointCoordinate,
        pixel_spacing: list[float] | None = None,
    ) -> float:
        """Calculates physical Euclidean distance between two points in millimeters.

        Args:
            p1: Starting point (x, y).
            p2: Ending point (x, y).
            pixel_spacing: [row_spacing, column_spacing] in mm/pixel. Defaults to [1.0, 1.0].

        Returns:
            Distance in millimeters, rounded to 2 decimal places.
        """
        row_spacing = (
            pixel_spacing[0] if pixel_spacing and len(pixel_spacing) > 0 else 1.0
        )
        col_spacing = (
            pixel_spacing[1]
            if pixel_spacing and len(pixel_spacing) > 1
            else row_spacing
        )

        dx = (p2.x - p1.x) * col_spacing
        dy = (p2.y - p1.y) * row_spacing
        dz = 0.0
        if p1.z is not None and p2.z is not None:
            dz = p2.z - p1.z

        dist = math.sqrt(dx * dx + dy * dy + dz * dz)
        return round(dist, 2)

    @classmethod
    def calculate_perpendicular_caliper(
        cls,
        p1: PointCoordinate,
        p2: PointCoordinate,
        cross_p1: PointCoordinate,
        cross_p2: PointCoordinate,
        pixel_spacing: list[float] | None = None,
    ) -> tuple[float, float, float]:
        """Calculates longest diameter, perpendicular short axis diameter, and PPD.

        Returns:
            Tuple of (longest_diameter_mm, perpendicular_diameter_mm, product_perpendicular_mm2).
        """
        ld = cls.calculate_distance_mm(p1, p2, pixel_spacing)
        sd = cls.calculate_distance_mm(cross_p1, cross_p2, pixel_spacing)
        ppd = round(ld * sd, 2)
        return ld, sd, ppd

    @classmethod
    def compute_automatic_perpendicular_line(
        cls,
        p1: PointCoordinate,
        p2: PointCoordinate,
        length_mm: float,
        pixel_spacing: list[float] | None = None,
        anchor_ratio: float = 0.5,
    ) -> tuple[PointCoordinate, PointCoordinate]:
        """Computes symmetric perpendicular line segment centered at the given ratio along line p1-p2.

        Args:
            p1: Line start point.
            p2: Line end point.
            length_mm: Desired physical length in mm of the perpendicular line.
            pixel_spacing: [row_spacing, col_spacing].
            anchor_ratio: Position along p1-p2 (0.5 is midpoint).

        Returns:
            Tuple of (cross_p1, cross_p2) PointCoordinate objects in pixel space.
        """
        row_spacing = (
            pixel_spacing[0] if pixel_spacing and len(pixel_spacing) > 0 else 1.0
        )
        col_spacing = (
            pixel_spacing[1]
            if pixel_spacing and len(pixel_spacing) > 1
            else row_spacing
        )

        # Anchor point in pixel space
        mx = p1.x + (p2.x - p1.x) * anchor_ratio
        my = p1.y + (p2.y - p1.y) * anchor_ratio

        # Direction vector in physical mm space
        p_dx = (p2.x - p1.x) * col_spacing
        p_dy = (p2.y - p1.y) * row_spacing
        p_len = math.sqrt(p_dx * p_dx + p_dy * p_dy)

        if p_len == 0.0:
            return (
                PointCoordinate(x=mx, y=my - (length_mm / 2.0) / row_spacing),
                PointCoordinate(x=mx, y=my + (length_mm / 2.0) / row_spacing),
            )

        # Unit normal in physical space (-dy, dx)
        nx = -p_dy / p_len
        ny = p_dx / p_len

        half_len = length_mm / 2.0
        c1_x = mx - (half_len * nx) / col_spacing
        c1_y = my - (half_len * ny) / row_spacing

        c2_x = mx + (half_len * nx) / col_spacing
        c2_y = my + (half_len * ny) / row_spacing

        return (
            PointCoordinate(x=round(c1_x, 2), y=round(c1_y, 2)),
            PointCoordinate(x=round(c2_x, 2), y=round(c2_y, 2)),
        )

    @classmethod
    def calculate_ellipsoid_volume(
        cls,
        d1_mm: float,
        d2_mm: float,
        d3_mm: float,
    ) -> float:
        """Calculates 3D ellipsoidal tumor volume in mm^3 using the formula: V = (pi / 6) * d1 * d2 * d3."""
        vol = (math.pi / 6.0) * d1_mm * d2_mm * d3_mm
        return round(vol, 2)

    @classmethod
    def calculate_rescaled_hu(
        cls,
        raw_pixel_value: float,
        rescale_slope: float = 1.0,
        rescale_intercept: float = 0.0,
    ) -> float:
        """Converts raw stored DICOM pixel value to calibrated Hounsfield Units (HU)."""
        return round(raw_pixel_value * rescale_slope + rescale_intercept, 2)

    @classmethod
    def map_mpr_slice_index(
        cls,
        axial_slice_index: int,
        axial_x: float,
        axial_y: float,
        rows: int,
        columns: int,
        slice_count: int,
    ) -> dict[MprPlane, dict[str, Any]]:
        """Maps 2D pixel coordinates on an Axial slice to corresponding Sagittal and Coronal plane slice indices and crosshairs.

        Axial: X is horizontal (col), Y is vertical (row), slice index is Z.
        Sagittal: Y is horizontal, Z is vertical, slice index is X.
        Coronal: X is horizontal, Z is vertical, slice index is Y.
        """
        clamped_x = max(0, min(columns - 1, int(round(axial_x))))
        clamped_y = max(0, min(rows - 1, int(round(axial_y))))
        clamped_z = max(0, min(slice_count - 1, axial_slice_index))

        return {
            MprPlane.AXIAL: {
                "slice_index": clamped_z,
                "max_slices": slice_count,
                "crosshair": {"x": clamped_x, "y": clamped_y},
            },
            MprPlane.SAGITTAL: {
                "slice_index": clamped_x,
                "max_slices": columns,
                "crosshair": {"x": clamped_y, "y": clamped_z},
            },
            MprPlane.CORONAL: {
                "slice_index": clamped_y,
                "max_slices": rows,
                "crosshair": {"x": clamped_x, "y": clamped_z},
            },
        }

    @classmethod
    def generate_series_mpr_metadata(
        cls,
        series_instance_uid: str,
        modality: str,
        rows: int,
        columns: int,
        slice_count: int,
        pixel_spacing: list[float] | None = None,
        slice_thickness: float | None = None,
        window_center: float | None = None,
        window_width: float | None = None,
    ) -> MprVolumeMetadataEntity:
        """Builds comprehensive 3D volume & MPR orientation descriptor for the web viewer."""
        effective_spacing = (
            pixel_spacing if pixel_spacing and len(pixel_spacing) >= 2 else [1.0, 1.0]
        )
        effective_thickness = (
            slice_thickness if slice_thickness and slice_thickness > 0 else 2.5
        )

        dim_x_mm = round(columns * effective_spacing[1], 2)
        dim_y_mm = round(rows * effective_spacing[0], 2)
        dim_z_mm = round(slice_count * effective_thickness, 2)

        wc = window_center if window_center is not None else 40.0
        ww = window_width if window_width is not None else 350.0

        return MprVolumeMetadataEntity(
            series_instance_uid=series_instance_uid,
            modality=modality,
            rows=rows,
            columns=columns,
            slice_count=slice_count,
            pixel_spacing=effective_spacing,
            slice_thickness=effective_thickness,
            slice_spacing=effective_thickness,
            volume_dimensions_mm=[dim_x_mm, dim_y_mm, dim_z_mm],
            window_center=wc,
            window_width=ww,
            available_planes=[MprPlane.AXIAL, MprPlane.SAGITTAL, MprPlane.CORONAL],
            window_presets=cls.DEFAULT_WINDOW_PRESETS,
        )
