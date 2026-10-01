from app import database

_COLUMNS = "id, email, name, role, active, created_at"


def get_by_id(user_id: int) -> dict | None:
    # password_changed_at so' aqui: e' o que a sessao usa para recusar token
    # emitido antes de uma troca de senha.
    return database.fetch_one(f"SELECT {_COLUMNS}, password_changed_at FROM users WHERE id = %s", (user_id,))


def get_by_email(email: str) -> dict | None:
    return database.fetch_one(f"SELECT {_COLUMNS} FROM users WHERE email = %s", (normalize_email(email),))


def get_with_password(email: str) -> dict | None:
    return database.fetch_one(f"SELECT {_COLUMNS}, password_hash FROM users WHERE email = %s", (normalize_email(email),))


def normalize_email(email: str) -> str:
    return email.strip().lower()


def create(email: str, name: str, password_hash: str, referred_by: int | None = None) -> dict:
    return database.fetch_one(
        f"INSERT INTO users (email, name, password_hash, referred_by) VALUES (%s, %s, %s, %s) RETURNING {_COLUMNS}",
        (normalize_email(email), name.strip(), password_hash, referred_by),
    )


def set_password(user_id: int, password_hash: str) -> int:
    return database.execute(
        "UPDATE users SET password_hash = %s, password_changed_at = now() WHERE id = %s", (password_hash, user_id))


def set_name(user_id: int, name: str) -> dict | None:
    return database.fetch_one(
        f"UPDATE users SET name = %s WHERE id = %s RETURNING {_COLUMNS}", (name.strip(), user_id))


def search(query: str, limit: int, offset: int) -> list[dict]:
    like = f"%{query.strip().lower()}%"
    return database.fetch_all(
        f"""SELECT {_COLUMNS},
                   (SELECT count(*) FROM devices d WHERE d.user_id = u.id AND d.deactivated_at IS NULL) AS devices
            FROM users u WHERE lower(email) LIKE %s OR lower(name) LIKE %s
            ORDER BY id DESC LIMIT %s OFFSET %s""",
        (like, like, limit, offset),
    )


def set_active(user_id: int, active: bool) -> int:
    return database.execute("UPDATE users SET active = %s WHERE id = %s", (active, user_id))
