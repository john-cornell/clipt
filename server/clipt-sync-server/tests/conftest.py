from pathlib import Path

import pytest
from fastapi.testclient import TestClient

TEST_TOKEN = "test-token"


@pytest.fixture(autouse=True)
def sync_token_env(monkeypatch):
    monkeypatch.setenv("CLIPT_SYNC_TOKEN", TEST_TOKEN)


@pytest.fixture
def db_path(tmp_path: Path) -> str:
    return str(tmp_path / "test.db")


@pytest.fixture
def client(db_path: str, sync_token_env) -> TestClient:
    from app import db as db_module
    from app.main import app, get_db

    def override_get_db():
        conn = db_module.get_connection(db_path)
        db_module.init_db(conn)
        try:
            yield conn
        finally:
            conn.close()

    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as c:
        c.headers.update({"Authorization": f"Bearer {TEST_TOKEN}"})
        yield c
    app.dependency_overrides.clear()


@pytest.fixture
def unauthed_client(db_path: str, sync_token_env) -> TestClient:
    from app import db as db_module
    from app.main import app, get_db

    def override_get_db():
        conn = db_module.get_connection(db_path)
        db_module.init_db(conn)
        try:
            yield conn
        finally:
            conn.close()

    app.dependency_overrides[get_db] = override_get_db
    with TestClient(app) as c:
        yield c
    app.dependency_overrides.clear()
