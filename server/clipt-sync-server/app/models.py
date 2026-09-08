from pydantic import BaseModel


class KdfParamsResponse(BaseModel):
    salt: str  # base64
    argon2_time_cost: int
    argon2_memory_cost_kib: int
    argon2_parallelism: int


class GroupUpsertRequest(BaseModel):
    ciphertext: str  # base64
    based_on_version: int


class GroupDeleteRequest(BaseModel):
    based_on_version: int


class GroupUpsertResponse(BaseModel):
    id: str
    version: int


class GroupRecord(BaseModel):
    id: str
    ciphertext: str | None  # null for tombstoned (deleted) rows
    version: int
    updated_at: str
    deleted: bool


class GroupsPullResponse(BaseModel):
    groups: list[GroupRecord]
    latest_version: int
