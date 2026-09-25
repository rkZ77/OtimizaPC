from app import database

_COLUMNS = "id, email, name, role, active, created_at"


def get_by_id(user_id: int) -> dict | None:
    return database.fetch_one(f"SELECT {_COLUMNS} FROM users WHERE id = %s", (user_id,))


def get_with_password(email: str) -> dict | None:
    return database.fetch_one(f"SELECT {_COLUMNS}, password_hash FROM users WHERE email = %s", (normalize_email(email),))


def normalize_email(email: str) -> str:
    return email.strip().lower()


def create(email: str, name: str, password_hash: str) -> dict:
    return database.fetch_one(
        f"INSERT INTO users (email, name, password_hash) VALUES (%s, %s, %s) RETURNING {_COLUMNS}",
        (normalize_email(email), name.strip(), password_hash),
    )


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
