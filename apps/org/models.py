"""
SQLAlchemy 2.0 models for the Organization Directory and Delegation of Authority (DOA).

This module implements models for Organization, Site, Personnel (SiteStaff),
DelegationOfAuthority, and append-only OrgAuditLog in compliance with FDA 21 CFR
Part 11 and GxP standards.
"""

import uuid
from datetime import datetime
from typing import List, Optional

from sqlalchemy import JSON, Boolean, DateTime, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship


class Base(DeclarativeBase):
    """
    Declarative base class for the Organization Directory microservice.
    """

    pass


class Organization(Base):
    """
    Represents an entity/organization involved in clinical trials,
    such as a Sponsor, CRO, Central Laboratory, or Site Organization.
    """

    __tablename__ = "organizations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    org_type: Mapped[str] = mapped_column(String(50), nullable=False, index=True)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(
        Integer, primary_key=True, default=1, nullable=False
    )

    # Relationships
    sites: Mapped[List["Site"]] = relationship(
        "Site",
        primaryjoin="Organization.id == foreign(Site.organization_id)",
        back_populates="organization",
        cascade="all, delete-orphan",
    )
    personnel: Mapped[List["Personnel"]] = relationship(
        "Personnel",
        primaryjoin="Organization.id == foreign(Personnel.organization_id)",
        back_populates="organization",
        cascade="all, delete-orphan",
    )


class Site(Base):
    """
    Represents a clinical trial site, associated with an organization and study.
    Enforces site-scoping via site_id for TrialLockManager compliance.
    """

    __tablename__ = "sites"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    organization_id: Mapped[str] = mapped_column(String(36), nullable=False)
    study_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(
        Integer, primary_key=True, default=1, nullable=False
    )

    # Relationships
    organization: Mapped["Organization"] = relationship(
        "Organization",
        primaryjoin="foreign(Site.organization_id) == remote(Organization.id)",
        back_populates="sites",
    )
    personnel: Mapped[List["Personnel"]] = relationship(
        "Personnel",
        primaryjoin="foreign(Personnel.site_id) == Site.site_id",
        back_populates="site",
        cascade="all, delete-orphan",
    )
    delegations: Mapped[List["DelegationOfAuthority"]] = relationship(
        "DelegationOfAuthority",
        primaryjoin="foreign(DelegationOfAuthority.site_id) == Site.site_id",
        back_populates="site",
        cascade="all, delete-orphan",
    )


class Personnel(Base):
    """
    Represents clinical personnel or site staff, associated with Keycloak user IDs,
    organizations, and study/site scopes.
    """

    __tablename__ = "personnel"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    keycloak_user_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
    first_name: Mapped[str] = mapped_column(String(100), nullable=False)
    last_name: Mapped[str] = mapped_column(String(100), nullable=False)
    email: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    role: Mapped[str] = mapped_column(String(100), nullable=False)  # ClinicalStaffRole

    organization_id: Mapped[Optional[str]] = mapped_column(String(36), nullable=True)
    site_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
    study_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(
        Integer, primary_key=True, default=1, nullable=False
    )

    # Relationships
    organization: Mapped[Optional["Organization"]] = relationship(
        "Organization",
        primaryjoin="foreign(Personnel.organization_id) == remote(Organization.id)",
        back_populates="personnel",
    )
    site: Mapped[Optional["Site"]] = relationship(
        "Site",
        primaryjoin="foreign(Personnel.site_id) == Site.site_id",
        back_populates="personnel",
    )


# Alias as requested
SiteStaff = Personnel


class DelegationOfAuthority(Base):
    """
    Represents delegated duties assigned to clinical trial staff at a site.
    Complies with GxP, ICH E6(R2), and FDA 21 CFR Part 11 requirements.
    """

    __tablename__ = "delegations_of_authority"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    delegator_id: Mapped[str] = mapped_column(String(36), nullable=False)
    delegatee_id: Mapped[str] = mapped_column(String(36), nullable=False)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)

    duties: Mapped[List[str]] = mapped_column(JSON, default=list, nullable=False)
    start_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    end_date: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    # Cryptographic & Signatures metadata
    signature: Mapped[Optional[str]] = mapped_column(String(512), nullable=True)
    signed_payload: Mapped[Optional[dict]] = mapped_column(JSON, nullable=True)
    signed_at: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    signed_by: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)

    # Revocation metadata
    revoked_at: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    revoked_by: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)
    revocation_reason: Mapped[Optional[str]] = mapped_column(
        String(1000), nullable=True
    )

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(
        Integer, primary_key=True, default=1, nullable=False
    )

    # Relationships
    delegator: Mapped["Personnel"] = relationship(
        "Personnel",
        primaryjoin="foreign(DelegationOfAuthority.delegator_id) == Personnel.id",
    )
    delegatee: Mapped["Personnel"] = relationship(
        "Personnel",
        primaryjoin="foreign(DelegationOfAuthority.delegatee_id) == Personnel.id",
    )
    site: Mapped[Optional["Site"]] = relationship(
        "Site",
        primaryjoin="foreign(DelegationOfAuthority.site_id) == Site.site_id",
        back_populates="delegations",
        uselist=False,
    )


class OrgAuditLog(Base):
    """
    An immutable, append-only chronological log of all Organization Directory
    and Delegation of Authority state modifications to satisfy 21 CFR Part 11.
    """

    __tablename__ = "org_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    actor_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    actor_role: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(String(50), nullable=False)
    record_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
