from packages.database import create_transactional_decorator

from .context import current_session
from .core import db_manager

transactional = create_transactional_decorator(db_manager, current_session)
