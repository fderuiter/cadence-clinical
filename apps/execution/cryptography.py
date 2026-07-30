import base64
import hashlib
import json
import os
import secrets
from datetime import datetime, timedelta
from typing import Any, Dict, List, Optional, Tuple

from cryptography.fernet import Fernet
from sqlalchemy.ext.asyncio import AsyncSession

"""
Module providing multi-party threshold cryptography for blinding keys.
Implements Shamir's Secret Sharing to split and reconstruct treatment allocation keys,
along with automatic key rotation to ensure secure and compliant operations.
"""

# A simple prime for Shamir's Secret Sharing (2^127 - 1)
PRIME = 170141183460469231731687303715884105727  # deid-ignore


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

        # Default fallback salt for unit testing without database
        default_salt = "default-v1-salt-for-bootstrap"
        self._keys[self._current_version] = self._derive_key_from_secret_and_salt(
            default_salt
        )

        # Track when keys were created to enforce rotation
        self._key_creation_dates: Dict[int, datetime] = {
            self._current_version: datetime.now()
        }
        self._custody_versions = set()  # To track custody-restricted key versions

    def _derive_key_from_secret_and_salt(self, salt: str) -> bytes:
        """Derives a Fernet key deterministically from master secret + salt using PBKDF2/SHA256."""
        master_secret = os.getenv(
            "RTSM_MASTER_SECRET", "default-master-secret-change-me-in-production"
        )
        salt_bytes = salt.encode("utf-8")
        derived = hashlib.pbkdf2_hmac(
            "sha256", master_secret.encode("utf-8"), salt_bytes, 100000, 32
        )
        return base64.urlsafe_b64encode(derived)

    async def load_from_db(self, session: AsyncSession) -> None:
        """Loads all persisted salts/versions from the database and derives their keys."""
        from sqlalchemy import select

        from apps.execution.database.models import AllocationKeyMetadata

        stmt = select(AllocationKeyMetadata).order_by(
            AllocationKeyMetadata.key_version.asc()
        )
        result = await session.execute(stmt)
        metadatas = result.scalars().all()

        if not metadatas:
            # First time bootstrapping in DB context: generate random salt for version 1
            v1_salt = secrets.token_hex(16)
            self._keys[1] = self._derive_key_from_secret_and_salt(v1_salt)
            self._key_creation_dates[1] = datetime.now()

            v1_metadata = AllocationKeyMetadata(
                key_version=1, salt=v1_salt, created_at=self._key_creation_dates[1]
            )
            session.add(v1_metadata)
            await session.flush()
        else:
            for metadata in metadatas:
                version = metadata.key_version
                salt = metadata.salt
                created_at = metadata.created_at

                # Derive and store the key
                derived_key = self._derive_key_from_secret_and_salt(salt)
                self._keys[version] = derived_key
                self._key_creation_dates[version] = created_at

                if version > self._current_version:
                    self._current_version = version

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

    def rotate_keys(self, session: Optional[AsyncSession] = None):
        """Automatically rotates the encryption key."""
        self._current_version += 1
        salt = secrets.token_hex(16)
        self._keys[self._current_version] = self._derive_key_from_secret_and_salt(salt)
        self._key_creation_dates[self._current_version] = datetime.now()

        if session is None:
            try:
                from apps.execution.database.context import current_session

                session = current_session.get()
            except Exception:
                pass

        if session is not None:
            from apps.execution.database.models import AllocationKeyMetadata

            metadata = AllocationKeyMetadata(
                key_version=self._current_version,
                salt=salt,
                created_at=self._key_creation_dates[self._current_version],
            )
            session.add(metadata)

    def derive_fernet_key(self, master_key: int) -> bytes:
        """Derives a Fernet key from a 127-bit master key using SHA-256."""
        # Convert integer to exactly 16 bytes (127 bits fit in 16 bytes)
        master_bytes = master_key.to_bytes(16, byteorder="big")
        key_hash = hashlib.sha256(master_bytes).digest()
        return base64.urlsafe_b64encode(key_hash)

    def create_custody_key_version(self, version: int) -> List[Dict[str, Any]]:
        """
        Creates a new key version designated for dual-custody access.
        Generates a master key, splits it into two shares for the Lead Unblinded Statistician and IDMC,
        and derives the encryption Fernet key.
        """
        if version in self._keys:
            raise ValueError(f"Key version {version} already exists.")

        master_key = self.generate_master_key()
        fernet_key = self.derive_fernet_key(master_key)

        self._keys[version] = fernet_key
        self._key_creation_dates[version] = datetime.now()
        self._custody_versions.add(version)

        # Split into 2 shares requiring 2 (k=2, n=2)
        shares = self.split_key(master_key, n=2, k=2)

        custody_shares = [
            {
                "custodian": "Lead Unblinded Statistician",
                "version": version,
                "x": shares[0][0],
                "y": shares[0][1],
            },
            {
                "custodian": "IDMC",
                "version": version,
                "x": shares[1][0],
                "y": shares[1][1],
            },
        ]
        return custody_shares

    def encrypt(
        self, data: Dict[str, Any], session: Optional[AsyncSession] = None
    ) -> str:
        """Encrypts data using the current active key version."""
        if self.check_rotation_needed():
            self.rotate_keys(session=session)

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

        if version in self._custody_versions:
            raise PermissionError(
                "This key version is custody-restricted and requires dual-share reconstruction for decryption."
            )

        if version not in self._keys:
            raise ValueError(f"Key version {version} not found.")

        f = Fernet(self._keys[version])
        decrypted = f.decrypt(encrypted_payload)
        return json.loads(decrypted.decode("utf-8"))

    def decrypt_with_shares(
        self, encrypted_str: str, shares: List[Dict[str, Any]]
    ) -> Dict[str, Any]:
        """
        Reconstructs the decryption key using two custody-bound shares
        and decrypts the given ciphertext. Enforces that both shares are valid,
        custodian roles match exactly, and no single share can decrypt.
        """
        if not shares:
            raise ValueError(
                "Exactly two shares are required for dual-custody reconstruction."
            )

        if len(shares) != 2:
            raise ValueError(
                "Exactly two shares are required for dual-custody reconstruction."
            )

        # Extract and parse version from encrypted string
        try:
            raw_bytes = base64.b64decode(encrypted_str.encode("utf-8"))
            ct_version = int.from_bytes(raw_bytes[:4], byteorder="big")
            encrypted_payload = raw_bytes[4:]
        except Exception:
            raise ValueError("Malformed encrypted data payload.")

        # Validate each share and extract values
        parsed_shares = []
        custodians = set()
        share_versions = set()

        for idx, s in enumerate(shares):
            if not isinstance(s, dict):
                raise ValueError("Invalid or malformed share format.")

            # Check for required keys
            for k in ["custodian", "version", "x", "y"]:
                if k not in s:
                    raise ValueError(f"Share at index {idx} is missing key: '{k}'")

            custodian = s["custodian"]
            version = s["version"]
            x = s["x"]
            y = s["y"]

            if not isinstance(custodian, str) or not custodian.strip():
                raise ValueError("Custodian role must be a non-empty string.")
            if not isinstance(version, int):
                raise ValueError("Share version must be an integer.")
            if not isinstance(x, int) or not isinstance(y, int):
                raise ValueError("Share coordinates 'x' and 'y' must be integers.")
            if x <= 0:
                raise ValueError("Share coordinate 'x' must be a positive integer.")
            if y < 0 or y >= PRIME:
                raise ValueError(
                    "Share coordinate 'y' must be a non-negative integer less than PRIME."
                )

            custodians.add(custodian)
            share_versions.add(version)
            parsed_shares.append((x, y))

        # Enforce exactly the two required custody roles
        expected_roles = {"Lead Unblinded Statistician", "IDMC"}
        if custodians != expected_roles:
            raise PermissionError(
                "Custodian roles must be exactly 'Lead Unblinded Statistician' and 'IDMC'."
            )

        # Enforce that shares have matching versions
        if len(share_versions) > 1:
            raise ValueError("Mismatched key versions between custody shares.")

        share_version = share_versions.pop()
        if share_version != ct_version:
            raise ValueError(
                "Mismatched key version between custody shares and encrypted data."
            )

        # Reconstruct master key from the shares
        try:
            reconstructed_master = self.reconstruct_key(parsed_shares)
        except Exception:
            raise ValueError("Failed to reconstruct master key from provided shares.")

        # Derive Fernet key
        try:
            derived_key = self.derive_fernet_key(reconstructed_master)
            f = Fernet(derived_key)
            decrypted = f.decrypt(encrypted_payload)
            return json.loads(decrypted.decode("utf-8"))
        except Exception:
            # Prevent leaking of keys, tracebacks or data in exception message
            raise ValueError("Decryption failed: invalid key reconstruction.")
