import pytest
from apps.execution.ucum import (
    normalize_unit_name,
    convert_unit,
    get_normalized_representation,
)


def test_normalize_unit_name():
    """Verify that clinical unit aliases are normalized to standard UCUM representation."""
    assert normalize_unit_name("kg") == "kg"
    assert normalize_unit_name("celsius") == "Cel"
    assert normalize_unit_name("°c") == "Cel"
    assert normalize_unit_name("fahrenheit") == "[Fahr]"
    assert normalize_unit_name("pound") == "[lb_av]"
    assert normalize_unit_name("unknown-unit") == "unknown-unit"


def test_convert_unit_identical():
    """Identical units should return the same value without conversion."""
    assert convert_unit(42.0, "kg", "kg") == 42.0
    assert convert_unit(100.0, "Cel", "celsius") == 100.0


def test_convert_unit_temperature():
    """Verify temperature offset conversions between Celsius, Fahrenheit, and Kelvin."""
    # Fahr to Cel
    assert pytest.approx(convert_unit(32.0, "fahrenheit", "celsius")) == 0.0
    assert pytest.approx(convert_unit(98.6, "[fahr]", "Cel")) == 37.0

    # Cel to Fahr
    assert pytest.approx(convert_unit(0.0, "Cel", "Fahrenheit")) == 32.0
    assert pytest.approx(convert_unit(37.0, "Cel", "[fahr]")) == 98.6

    # Kelvin to Cel and vice versa
    assert pytest.approx(convert_unit(273.15, "Kelvin", "Cel")) == 0.0
    assert pytest.approx(convert_unit(100.0, "Cel", "Kelvin")) == 373.15

    # Kelvin to Fahr
    assert pytest.approx(convert_unit(273.15, "Kelvin", "Fahrenheit")) == 32.0


def test_convert_unit_multiplicative():
    """Verify linear multiplicative conversions for mass, length, and pressure."""
    # Mass
    assert pytest.approx(convert_unit(1.0, "lb", "kg")) == 0.45359237
    assert pytest.approx(convert_unit(1000.0, "g", "kg")) == 1.0

    # Length
    assert pytest.approx(convert_unit(1.0, "in", "cm")) == 2.54
    assert pytest.approx(convert_unit(1.0, "ft", "m")) == 0.3048

    # Pressure
    assert pytest.approx(convert_unit(1.0, "kPa", "mmHg"), abs=1e-3) == 7.5006156


def test_convert_unit_errors():
    """Verify that incompatible or unrecognized unit conversions raise ValueError."""
    # Incompatible domains
    with pytest.raises(ValueError, match="Incompatible unit conversion"):
        convert_unit(10.0, "kg", "m")

    # Unrecognized units
    with pytest.raises(ValueError, match="Unrecognized or unsupported"):
        convert_unit(10.0, "unknown-from", "kg")


def test_get_normalized_representation():
    """Verify normalization of measurements to standard reference base UCUM units."""
    # None checks
    assert get_normalized_representation(None, "kg") == (None, "kg")
    assert get_normalized_representation(10.0, None) == (10.0, None)

    # Temperature
    val, unit = get_normalized_representation(98.6, "Fahrenheit")
    assert pytest.approx(val) == 37.0
    assert unit == "Cel"

    # Mass/Weight (Base: kg, CONVERSIONS maps lb_av to base kg)
    val, unit = get_normalized_representation(150.0, "lbs")
    assert pytest.approx(val) == 150.0 * 0.45359237
    assert unit == "kg"

    # Unrecognized unit stays as-is
    assert get_normalized_representation(10.0, "unknown") == (10.0, "unknown")
