from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from sqlalchemy.orm import DeclarativeBase

from app.config import settings

engine = create_async_engine(settings.DATABASE_URL, echo=False)
async_session = async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)


class Base(DeclarativeBase):
    pass


async def get_db():
    async with async_session() as session:
        try:
            yield session
            await session.commit()
        except Exception:
            await session.rollback()
            raise


async def init_db():
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    # Run lightweight migrations for columns added after initial schema
    async with engine.begin() as conn:
        await _migrate(conn)


async def _migrate(conn):
    """Add missing columns to existing tables (SQLite ALTER TABLE)."""
    import sqlalchemy

    migrations = [
        ("messages", "reply_to_id", "VARCHAR(36) REFERENCES messages(id) ON DELETE SET NULL"),
    ]
    for table, column, col_type in migrations:
        try:
            await conn.execute(sqlalchemy.text(
                f"ALTER TABLE {table} ADD COLUMN {column} {col_type}"
            ))
        except Exception:
            pass  # Column already exists
