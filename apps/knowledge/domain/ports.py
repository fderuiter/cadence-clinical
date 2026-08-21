from typing import Any

from packages.hexagonal import RepositoryPort


class IKnowledgeRepository(RepositoryPort[Any]):
    """Abstract repository port for Knowledge microservice."""

    pass


IKnowledgeRepositoryPort = IKnowledgeRepository
