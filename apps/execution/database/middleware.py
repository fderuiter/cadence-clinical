from starlette.middleware.base import BaseHTTPMiddleware
from fastapi import Request
from apps.execution.database.context import current_user_id, current_change_reason, current_session

class ContextResetMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next):
        user_id = request.headers.get("x-user-id", "system")
        change_reason = request.headers.get("x-change-reason", "system_operation")
        
        user_token = current_user_id.set(user_id)
        reason_token = current_change_reason.set(change_reason)
        # We also want to make sure the session is cleared in case it was accidentally left over,
        # but session token is usually set in @transactional. We'll capture its token if we need to.
        session_token = current_session.set(None)

        try:
            response = await call_next(request)
            return response
        finally:
            current_user_id.reset(user_token)
            current_change_reason.reset(reason_token)
            current_session.reset(session_token)
