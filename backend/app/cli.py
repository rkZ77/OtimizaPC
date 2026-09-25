"""Tarefas de operacao.

    python -m app.cli make-admin email@dominio.com

O primeiro admin nao pode nascer pela API (ninguem teria permissao para
criar), entao nasce aqui, rodado por quem tem acesso ao banco.
"""
import sys

from app import database


def make_admin(email: str) -> None:
    n = database.execute("UPDATE users SET role = 'admin' WHERE email = %s", (email.strip().lower(),))
    print("ok" if n else f"nenhuma conta com o e-mail {email}")


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "make-admin":
        make_admin(sys.argv[2])
    else:
        print(__doc__)
        sys.exit(2)
