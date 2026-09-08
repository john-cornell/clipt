import base64


def test_get_kdf_returns_params(client):
    response = client.get("/kdf")

    assert response.status_code == 200
    body = response.json()
    assert base64.b64decode(body["salt"])  # decodes without error, non-empty
    assert body["argon2_time_cost"] > 0
    assert body["argon2_memory_cost_kib"] > 0
    assert body["argon2_parallelism"] > 0


def test_get_kdf_returns_same_salt_on_second_call(client):
    first = client.get("/kdf").json()
    second = client.get("/kdf").json()

    assert first["salt"] == second["salt"]
