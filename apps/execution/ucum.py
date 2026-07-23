from typing import Dict, Optional, Tuple

UNIT_ALIASES: Dict[str, str] = {
    # Temperature
    "cel": "Cel",
    "c": "Cel",
    "°c": "Cel",
    "celsius": "Cel",
    "fahr": "[Fahr]",
    "f": "[Fahr]",
    "°f": "[Fahr]",
    "fahrenheit": "[Fahr]",
    "[fahr]": "[Fahr]",
    "k": "K",
    "kelvin": "K",
    # Weight
    "kg": "kg",
    "kilogram": "kg",
    "kilograms": "kg",
    "lb": "[lb_av]",
    "lbs": "[lb_av]",
    "pound": "[lb_av]",
    "pounds": "[lb_av]",
    "[lb_av]": "[lb_av]",
    "g": "g",
    "gram": "g",
    "grams": "g",
    "mg": "mg",
    "milligram": "mg",
    "milligrams": "mg",
    # Length
    "m": "m",
    "meter": "m",
    "meters": "m",
    "cm": "cm",
    "centimeter": "cm",
    "centimeters": "cm",
    "mm": "mm",
    "millimeter": "mm",
    "millimeters": "mm",
    "in": "[in_i]",
    "inch": "[in_i]",
    "inches": "[in_i]",
    "[in_i]": "[in_i]",
    "ft": "[ft_i]",
    "foot": "[ft_i]",
    "feet": "[ft_i]",
    "[ft_i]": "[ft_i]",
    # Pressure
    "mm[hg]": "mm[Hg]",
    "mmhg": "mm[Hg]",
    "mm_hg": "mm[Hg]",
    "kpa": "kPa",
    "kilopascal": "kPa",
    "kilopascals": "kPa",
}

# Mapping of standard UCUM units to their base unit and multiplication factor to base unit
CONVERSIONS: Dict[str, Tuple[str, float]] = {
    # Mass (Base: kg)
    "kg": ("kg", 1.0),
    "[lb_av]": ("kg", 0.45359237),
    "g": ("kg", 0.001),
    "mg": ("kg", 0.000001),
    "[oz_av]": ("kg", 0.028349523125),
    # Length (Base: m)
    "m": ("m", 1.0),
    "cm": ("m", 0.01),
    "mm": ("m", 0.001),
    "[in_i]": ("m", 0.0254),
    "[ft_i]": ("m", 0.3048),
    # Pressure (Base: mm[Hg])
    "mm[Hg]": ("mm[Hg]", 1.0),
    "kPa": ("mm[Hg]", 7.5006156),
}


def normalize_unit_name(unit: str) -> str:
    """Normalizes a unit name using common clinical aliases to standard UCUM representation.

    Args:
        unit (str): The unit string to normalize.

    Returns:
        str: Normalized standard UCUM unit code.
    """
    cleaned = unit.strip().lower()
    return UNIT_ALIASES.get(cleaned, unit)


def convert_unit(value: float, from_unit: str, to_unit: str) -> float:
    """Convert a numeric clinical measurement value between two UCUM units.

    Supports temperature offset conversions and linear multiplicative conversions
    for mass, length, and pressure domains.

    Args:
        value (float): The numeric value to convert.
        from_unit (str): The starting unit code/alias.
        to_unit (str): The target unit code/alias.

    Returns:
        float: The converted numeric value.

    Raises:
        ValueError: If units are incompatible or unrecognized.
    """
    norm_from = normalize_unit_name(from_unit)
    norm_to = normalize_unit_name(to_unit)

    if norm_from == norm_to:
        return value

    # Temperature special handling
    temp_units = {"Cel", "[Fahr]", "K"}
    if norm_from in temp_units and norm_to in temp_units:
        # Convert to Cel first
        if norm_from == "Cel":
            cel_val = value
        elif norm_from == "[Fahr]":
            cel_val = (value - 32.0) * 5.0 / 9.0
        else:  # K
            cel_val = value - 273.15

        # Convert Cel to target
        if norm_to == "Cel":
            return cel_val
        elif norm_to == "[Fahr]":
            return cel_val * 9.0 / 5.0 + 32.0
        else:  # K
            return cel_val + 273.15

    # Multiplicative conversions
    if norm_from in CONVERSIONS and norm_to in CONVERSIONS:
        base_from, factor_from = CONVERSIONS[norm_from]
        base_to, factor_to = CONVERSIONS[norm_to]

        if base_from != base_to:
            raise ValueError(
                f"Incompatible unit conversion from {from_unit} ({base_from}) to {to_unit} ({base_to})"
            )

        # Value in base unit
        value_base = value * factor_from
        # Value in target unit
        return value_base / factor_to

    raise ValueError(
        f"Unrecognized or unsupported conversion from '{from_unit}' to '{to_unit}'"
    )


def get_normalized_representation(
    value: Optional[float], unit: Optional[str]
) -> Tuple[Optional[float], Optional[str]]:
    """Normalize a clinical measurement to its standard reference base UCUM unit.

    Standard references used:
    - Temperature -> "Cel"
    - Mass/Weight -> "kg"
    - Length/Height -> "m"
    - Pressure -> "mm[Hg]"

    Args:
        value (Optional[float]): The numeric value to normalize.
        unit (Optional[str]): The starting unit.

    Returns:
        Tuple[Optional[float], Optional[str]]: The normalized value and unit.
    """
    if value is None or unit is None:
        return value, unit

    norm_unit = normalize_unit_name(unit)

    # Temperature
    if norm_unit in {"Cel", "[Fahr]", "K"}:
        try:
            val_cel = convert_unit(value, norm_unit, "Cel")
            return val_cel, "Cel"
        except Exception:
            return value, unit

    # Multiplicative
    if norm_unit in CONVERSIONS:
        base_unit, factor = CONVERSIONS[norm_unit]
        return value * factor, base_unit

    return value, unit
