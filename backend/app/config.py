from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    DATABASE_URL: str = "sqlite+aiosqlite:///./neuraldamage.db"

    # OIDC (Pocket ID)
    OIDC_ISSUER: str = "https://auth.gaggao.com"
    OIDC_CLIENT_ID: str = ""
    OIDC_REDIRECT_URI: str = "http://localhost:5173/callback"
    OIDC_TOKEN_ENDPOINT: str = "https://auth.gaggao.com/api/oidc/token"
    OIDC_USERINFO_ENDPOINT: str = "https://auth.gaggao.com/api/oidc/userinfo"
    OIDC_JWKS_URI: str = "https://auth.gaggao.com/.well-known/jwks.json"
    OIDC_AUTHORIZE_ENDPOINT: str = "https://auth.gaggao.com/authorize"

    # App JWT
    JWT_SECRET: str = "change-me"
    JWT_ALGORITHM: str = "HS256"
    JWT_EXPIRY_HOURS: int = 24

    # OpenRouter
    OPENROUTER_API_KEY: str = ""
    APP_URL: str = "http://localhost:5173"

    # Response engine
    RESPONSE_THRESHOLD: float = 0.35
    JUDGE_MODEL: str = "google/gemini-2.0-flash-001"

    model_config = SettingsConfigDict(
        env_file=("../.env", ".env"),
        env_file_encoding="utf-8",
        extra="ignore",
    )


settings = Settings()
