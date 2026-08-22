"""
Knowledge service client adapter for Tickets microservice.
"""

from apps.tickets.infrastructure.knowledge_client import (
    KnowledgeServiceClient,
    register_in_process_knowledge_provider,
)

__all__ = [
    "KnowledgeServiceClient",
    "register_in_process_knowledge_provider",
]
