"""Unit tests for the enhanced hexagonal kernel primitives and error mappings."""

from dataclasses import dataclass

from packages.hexagonal import (
    AggregateRoot,
    BaseEntity,
    ConflictError,
    DatabaseError,
    DomainError,
    DomainEvent,
    EntityAlreadyExistsError,
    EntityNotFoundError,
    PreconditionFailedError,
    UnauthorizedActionError,
    ValidationError,
    ValueObject,
    map_domain_error_to_http_status,
)


@dataclass(frozen=True)
class SubjectEnrolledEvent(DomainEvent):
    subject_id: str = ""
    site_id: str = ""


class SampleSubject(AggregateRoot[str]):
    def __init__(self, subject_id: str, site_id: str):
        super().__init__(id=subject_id)
        self.site_id = site_id

    def enroll(self):
        self.record_event(
            SubjectEnrolledEvent(subject_id=self.id, site_id=self.site_id)
        )


class AddressValueObject(ValueObject):
    def __init__(self, city: str, country: str):
        self.city = city
        self.country = country


def test_base_entity_equality():
    e1 = BaseEntity(id="E-001")
    e2 = BaseEntity(id="E-001")
    e3 = BaseEntity(id="E-002")

    assert e1 == e2
    assert e1 != e3
    assert hash(e1) == hash(e2)


def test_value_object_equality():
    v1 = AddressValueObject("San Francisco", "USA")
    v2 = AddressValueObject("San Francisco", "USA")
    v3 = AddressValueObject("New York", "USA")

    assert v1 == v2
    assert v1 != v3
    assert hash(v1) == hash(v2)


def test_aggregate_root_events():
    subject = SampleSubject("SUBJ-101", "SITE-A")
    assert len(subject._domain_events) == 0

    subject.enroll()
    assert len(subject._domain_events) == 1

    events = subject.flush_events()
    assert len(events) == 1
    assert isinstance(events[0], SubjectEnrolledEvent)
    assert events[0].subject_id == "SUBJ-101"
    assert len(subject._domain_events) == 0


def test_domain_error_http_mappings():
    assert map_domain_error_to_http_status(EntityNotFoundError("Not found")) == 404
    assert map_domain_error_to_http_status(EntityAlreadyExistsError("Exists")) == 409
    assert map_domain_error_to_http_status(ValidationError("Invalid")) == 422
    assert map_domain_error_to_http_status(UnauthorizedActionError("Forbidden")) == 403
    assert (
        map_domain_error_to_http_status(PreconditionFailedError("Precondition")) == 412
    )
    assert map_domain_error_to_http_status(ConflictError("Conflict")) == 409
    assert map_domain_error_to_http_status(DatabaseError("DB Fail")) == 500
    assert map_domain_error_to_http_status(DomainError("Generic")) == 400
