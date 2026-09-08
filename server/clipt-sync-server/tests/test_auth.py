def test_kdf_without_token_is_401(unauthed_client):
    response = unauthed_client.get("/kdf")
    assert response.status_code == 401


def test_kdf_with_wrong_token_is_401(unauthed_client):
    unauthed_client.headers.update({"Authorization": "Bearer wrong-token"})
    response = unauthed_client.get("/kdf")
    assert response.status_code == 401
