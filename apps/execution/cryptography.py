import base64
import json
import secrets
from datetime import datetime, timedelta
from typing import Any, Dict, List, Tuple

from cryptography.fernet import Fernet

"""
Module providing multi-party threshold cryptography for blinding keys.
Implements Shamir's Secret Sharing to split and reconstruct treatment allocation keys,
along with automatic key rotation to ensure secure and compliant operations.
"""

# A simple prime for Shamir's Secret Sharing (2^127 - 1)
PRIME = 170141183460469231731687303715884105727


def _eval_poly(poly: List[int], x: int) -> int:
    """Evaluates a polynomial at a given point x using Horner's method."""
    result = 0
    for coeff in reversed(poly):
        result = (result * x + coeff) % PRIME
    return result


def _extended_gcd(a: int, b: int) -> Tuple[int, int, int]:
    """Computes the extended Greatest Common Divisor of two numbers."""
    x, y, u, v = 0, 1, 1, 0
    while a != 0:
        q, r = b // a, b % a
        m, n = x - u * q, y - v * q
        b, a, x, y, u, v = a, r, u, v, m, n
    gcd = b
    return gcd, x, y


def _mod_inverse(k: int, prime: int) -> int:
    """Computes the modular inverse of k modulo prime."""
    gcd, x, y = _extended_gcd(k, prime)
    if gcd != 1:
        raise ValueError("No modular inverse")
    return x % prime


class AllocationKeyManager:
    """
    Manages treatment allocation keys using multi-share cryptography
    and supports automatic key rotation.
    """

    def __init__(self):
        # We store keys historically for decryption, and use the latest for encryption
        self._keys: Dict[int, bytes] = {}
        self._current_version = 1
        self._keys[self._current_version] = Fernet.generate_key()
        # Track when keys were created to enforce rotation
        self._key_creation_dates: Dict[int, datetime] = {
            self._current_version: datetime.now()
        }

    def generate_master_key(self) -> int:
        """Generates a large integer master key for multi-share splitting."""
        return secrets.randbelow(PRIME)

    def split_key(self, secret: int, n: int, k: int) -> List[Tuple[int, int]]:
        """Splits a secret into n shares, requiring k to reconstruct."""
        if k > n:
            raise ValueError("k cannot be greater than n")

        # Generate random coefficients for the polynomial of degree k-1
        # The constant term (poly[0]) is the secret
        poly = [secret] + [secrets.randbelow(PRIME) for _ in range(k - 1)]

        shares = []
        for x in range(1, n + 1):
            y = _eval_poly(poly, x)
            shares.append((x, y))

        return shares

    def reconstruct_key(self, shares: List[Tuple[int, int]]) -> int:
        """Reconstructs the secret from shares."""
        if not shares:
            raise ValueError("No shares provided")

        k = len(shares)
        secret = 0

        for i in range(k):
            xi, yi = shares[i]

            # Compute Lagrange basis polynomial l_i(0)
            numerator = 1
            denominator = 1

            for j in range(k):
                if i != j:
                    xj, _ = shares[j]
                    numerator = (numerator * (-xj)) % PRIME
                    denominator = (denominator * (xi - xj)) % PRIME

            lagrange_val = (numerator * _mod_inverse(denominator, PRIME)) % PRIME
            term = (yi * lagrange_val) % PRIME
            secret = (secret + term) % PRIME

        return secret

    def check_rotation_needed(self) -> bool:
        """Checks if the current key is older than 365 days."""
        created = self._key_creation_dates[self._current_version]
        return datetime.now() - created > timedelta(days=365)

    def rotate_keys(self):
        """Automatically rotates the encryption key."""
        self._current_version += 1
        self._keys[self._current_version] = Fernet.generate_key()
        self._key_creation_dates[self._current_version] = datetime.now()

    def encrypt(self, data: Dict[str, Any]) -> str:
        """Encrypts data using the current active key version."""
        if self.check_rotation_needed():
            self.rotate_keys()

        f = Fernet(self._keys[self._current_version])
        payload = json.dumps(data).encode("utf-8")
        encrypted = f.encrypt(payload)

        # Prepend version indicator
        version_bytes = self._current_version.to_bytes(4, byteorder="big")
        final_payload = base64.b64encode(version_bytes + encrypted).decode("utf-8")
        return final_payload

    def decrypt(self, encrypted_str: str) -> Dict[str, Any]:
        """Decrypts data using the appropriate historical key."""
        raw_bytes = base64.b64decode(encrypted_str.encode("utf-8"))
        version = int.from_bytes(raw_bytes[:4], byteorder="big")
        encrypted_payload = raw_bytes[4:]

        if version not in self._keys:
            raise ValueError(f"Key version {version} not found.")

        f = Fernet(self._keys[version])
        decrypted = f.decrypt(encrypted_payload)
        return json.loads(decrypted.decode("utf-8"))
