"""Sobe a API em modo desenvolvimento: python run_dev.py"""
import os

import uvicorn

os.environ.setdefault("APP_ENV", "development")

if __name__ == "__main__":
    uvicorn.run("app.main:app", host="127.0.0.1", port=8000, reload=True)
