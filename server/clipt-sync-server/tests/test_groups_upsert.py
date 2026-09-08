import base64


def _b64(s: str) -> str:
    return base64.b64encode(s.encode()).decode("ascii")


def test_create_new_group_with_based_on_version_zero(client):
    response = client.put(
        "/groups/g1",
        json={"ciphertext": _b64("hello"), "based_on_version": 0},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["id"] == "g1"
    assert body["version"] == 1


def test_update_existing_group_with_matching_version(client):
    first = client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0}).json()

    response = client.put(
        "/groups/g1",
        json={"ciphertext": _b64("v2"), "based_on_version": first["version"]},
    )

    assert response.status_code == 200
    assert response.json()["version"] == first["version"] + 1


def test_update_with_stale_version_is_409(client):
    client.put("/groups/g1", json={"ciphertext": _b64("v1"), "based_on_version": 0})

    response = client.put(
        "/groups/g1",
        json={"ciphertext": _b64("v2"), "based_on_version": 0},  # stale — already at version 1
    )

    assert response.status_code == 409


def test_create_with_nonzero_based_on_version_is_409(client):
    response = client.put(
        "/groups/g1",
        json={"ciphertext": _b64("v1"), "based_on_version": 5},
    )

    assert response.status_code == 409
