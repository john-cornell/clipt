import base64


def _b64(s: str) -> str:
    return base64.b64encode(s.encode()).decode("ascii")


def test_pull_since_zero_returns_everything(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})
    client.put("/groups/g2", json={"ciphertext": _b64("v1"), "based_on_version": 0})

    response = client.get("/groups", params={"since": 0})

    assert response.status_code == 200
    body = response.json()
    ids = {g["id"] for g in body["groups"]}
    assert ids == {"g1", "g2"}
    assert body["latest_version"] == 2


def test_pull_since_filters_older_changes(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})
    g2 = client.put("/groups/g2", json={"ciphertext": _b64("v1"), "based_on_version": 0}).json()

    response = client.get("/groups", params={"since": 1})

    body = response.json()
    ids = [g["id"] for g in body["groups"]]
    assert ids == ["g2"]
    assert body["groups"][0]["version"] == g2["version"]


def test_pull_includes_tombstones_with_null_ciphertext(client):
    created = client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0}).json()
    client.request("DELETE", "/groups/g1", json={"based_on_version": created["version"]})

    response = client.get("/groups", params={"since": 0})

    body = response.json()
    assert len(body["groups"]) == 1
    assert body["groups"][0]["id"] == "g1"
    assert body["groups"][0]["deleted"] is True
    assert body["groups"][0]["ciphertext"] is None


def test_pull_with_no_changes_returns_empty_list(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})

    response = client.get("/groups", params={"since": 1})

    body = response.json()
    assert body["groups"] == []
    assert body["latest_version"] == 1
