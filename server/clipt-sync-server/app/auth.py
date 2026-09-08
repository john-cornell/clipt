import hmac
import os

from fastapi import Header, HTTPException, status


def require_auth(authorization: str = Header(default="")) -> None:
    expected = os.environ.get("CLIPT_SYNC_TOKEN")
    if not expected:
        raise RuntimeError("CLIPT_SYNC_TOKEN environment variable is not set.")

    prefix = "Bearer "
    if not authorization.startswith(prefix):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Missing bearer token.")

    provided = authorization[len(prefix):]
    if not hmac.compare_digest(provided, expected):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid token.")
