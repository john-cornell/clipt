import base64


def _b64(s: str) -> str:
    return base64.b64encode(s.encode()).decode("ascii")


def test_delete_existing_group(client):
    created = client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0}).json()

    response = client.request("DELETE", "/groups/g1", json={"based_on_version": created["version"]})

    assert response.status_code == 200
    assert response.json()["version"] == created["version"] + 1


def test_delete_with_stale_version_is_409(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})

    response = client.request("DELETE", "/groups/g1", json={"based_on_version": 0})

    assert response.status_code == 409


def test_delete_unknown_group_is_404(client):
    response = client.request("DELETE", "/groups/does-not-exist", json={"based_on_version": 0})

    assert response.status_code == 404
