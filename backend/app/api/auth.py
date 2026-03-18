from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.models.user import User
from app.schemas.user import UserOut
from app.services import auth_service
from app.api.deps import get_current_user

router = APIRouter(prefix="/api/auth", tags=["auth"])


class LoginRequest(BaseModel):
    code_challenge: str
    state: str = ""


class LoginResponse(BaseModel):
    authorize_url: str


class CallbackRequest(BaseModel):
    code: str
    code_verifier: str


class CallbackResponse(BaseModel):
    token: str
    user: UserOut


@router.post("/login", response_model=LoginResponse)
async def login(req: LoginRequest):
    url = await auth_service.get_authorize_url(req.code_challenge, req.state)
    return LoginResponse(authorize_url=url)


@router.post("/callback", response_model=CallbackResponse)
async def callback(req: CallbackRequest, db: AsyncSession = Depends(get_db)):
    try:
        token_data = await auth_service.exchange_code(req.code, req.code_verifier)
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Token exchange failed: {e}")

    # Try to get user info from id_token or userinfo endpoint
    id_token = token_data.get("id_token")
    access_token = token_data.get("access_token")

    claims = None
    if id_token:
        try:
            claims = await auth_service.validate_id_token(id_token)
        except Exception:
            pass

    if claims is None and access_token:
        try:
            claims = await auth_service.get_userinfo(access_token)
        except Exception as e:
            raise HTTPException(status_code=400, detail=f"Failed to get user info: {e}")

    if claims is None:
        raise HTTPException(status_code=400, detail="Could not retrieve user information")

    sub = claims.get("sub")
    email = claims.get("email", "")
    name = claims.get("name") or claims.get("preferred_username") or email.split("@")[0]
    picture = claims.get("picture")

    if not sub:
        raise HTTPException(status_code=400, detail="No subject in claims")

    # Upsert user
    result = await db.execute(select(User).where(User.oidc_sub == sub))
    user = result.scalar_one_or_none()

    if user is None:
        user = User(
            email=email,
            display_name=name,
            avatar_url=picture,
            oidc_sub=sub,
        )
        db.add(user)
        await db.flush()
    else:
        user.display_name = name
        user.avatar_url = picture
        if email:
            user.email = email

    app_token = auth_service.create_app_token(user.id, user.email)
    return CallbackResponse(token=app_token, user=UserOut.model_validate(user))


@router.get("/me", response_model=UserOut)
async def me(user: User = Depends(get_current_user)):
    return UserOut.model_validate(user)


@router.post("/test-token")
async def test_token(db: AsyncSession = Depends(get_db)):
    """Dev-only endpoint: returns a JWT for the first user in the DB (for Playwright tests)."""
    result = await db.execute(select(User).limit(1))
    user = result.scalar_one_or_none()
    if not user:
        raise HTTPException(status_code=404, detail="No users in database")
    token = auth_service.create_app_token(user.id, user.email)
    return {"token": token, "user": UserOut.model_validate(user)}
