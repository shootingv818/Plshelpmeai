"""
کلاینت احراز هویت آیوا - پورت شده از IvaAuthClient.cs
ارتباط async با API آیوا
"""
import json
import random
import time
import uuid
from typing import Any, Callable, Optional

import httpx

from core.constants import (
    API_BASE_URL, API_PREFIX, APP_VERSION, PUBLIC_KEY_URL,
    Endpoints, SIGN_EXCLUDE, StorageKeys,
    VALID_KEYWORDS, RATE_LIMIT_KEYWORDS, BLOCKED_KEYWORDS,
)
from core.crypto import IvaCrypto, base64_modulus_to_pem


class IvaAuthClient:
    """کلاینت async برای ارتباط با API آیوا"""

    def __init__(self, key_store: dict = None, timeout: float = 65.0):
        self.key_store: dict = key_store if key_store is not None else {}
        self.crypto = IvaCrypto(self.key_store)
        self.timeout = timeout
        self.current_phone: Optional[str] = None
        self._client: Optional[httpx.AsyncClient] = None

    @staticmethod
    def _generate_transaction_id() -> str:
        """
        تولید transactionId معتبر برای شاپرک
        فرمت: رشته عددی دقیقاً ۲۰ رقمی
        ساختار: timestamp میلی‌ثانیه‌ای (۱۳ رقم) + ارقام تصادفی (۷ رقم)
        """
        # timestamp میلی‌ثانیه‌ای (۱۳ رقم)
        timestamp_ms = str(int(time.time() * 1000))
        
        # ارقام تصادفی برای تکمیل تا ۲۰ رقم
        remaining_digits = 20 - len(timestamp_ms)
        random_part = "".join(str(random.randint(0, 9)) for _ in range(remaining_digits))
        
        return timestamp_ms + random_part

    @property
    def base_address(self) -> str:
        return API_BASE_URL.rstrip("/") + API_PREFIX

    async def _get_client(self) -> httpx.AsyncClient:
        if self._client is None or self._client.is_closed:
            self._client = httpx.AsyncClient(timeout=self.timeout)
        return self._client

    async def close(self):
        if self._client and not self._client.is_closed:
            await self._client.aclose()
            self._client = None

    def _apply_headers(self, headers: dict, path: str, body_str: str = ""):
        """اضافه کردن هدرهای احراز هویت و امضا"""
        token = self.key_store.get(StorageKeys.TOKEN)
        if token:
            headers["Authorization"] = f"Bearer {token}"
            headers["iva-versioncode"] = APP_VERSION.replace(".", "")
            headers["iva-versionname"] = APP_VERSION

        if body_str and path not in SIGN_EXCLUDE:
            try:
                headers["Sign-Data"] = self.crypto.hmac_sign(body_str)
            except (ValueError, Exception):
                pass

    def _is_token_expired(self, skew: int = 30) -> bool:
        """بررسی انقضای توکن"""
        obtained = self.key_store.get(StorageKeys.ACCESS_TOKEN_OBTAINED_AT)
        expires_in = self.key_store.get(StorageKeys.ACCESS_TOKEN_EXP_TIME)
        if not obtained or not expires_in:
            return False
        try:
            expires_at = int(obtained) + int(expires_in)
            return int(time.time()) >= expires_at - skew
        except (ValueError, TypeError):
            return False

    def _persist_tokens(self, data: dict):
        """ذخیره توکن‌ها در key_store"""
        if data.get("refreshToken"):
            self.key_store[StorageKeys.REFRESH_TOKEN] = data["refreshToken"]
        if data.get("accessToken"):
            self.key_store[StorageKeys.TOKEN] = data["accessToken"]
        if data.get("expiresIn") is not None:
            self.key_store[StorageKeys.ACCESS_TOKEN_EXP_TIME] = str(data["expiresIn"])
        if data.get("tokenType"):
            self.key_store[StorageKeys.TOKEN_TYPE] = data["tokenType"]
        self.key_store[StorageKeys.ACCESS_TOKEN_OBTAINED_AT] = str(int(time.time()))

        # اگر کلید جدید برگشت
        if data.get("key"):
            try:
                pem = base64_modulus_to_pem(data["key"])
                self.key_store[StorageKeys.RSA_PUBLIC] = pem
            except Exception:
                pass

    async def fetch_public_key(self, key_id: str = "1", transaction_id: str = None) -> str:
        """دریافت کلید عمومی از سرور شاپرک"""
        # هر بار یک transactionId تازه تولید می‌شود
        tid = transaction_id or self._generate_transaction_id()

        body = {"keyId": int(key_id), "transactionId": tid}
        client = await self._get_client()
        resp = await client.post(
            PUBLIC_KEY_URL,
            json=body,
            headers={
                "Content-Type": "application/json",
                "Accept": "application/json",
                "User-Agent": "okhttp/4.12.0",
            },
        )

        if resp.status_code != 200:
            raise Exception(f"getKey ناموفق (HTTP {resp.status_code}): {resp.text}")

        result = resp.json()

        # بررسی خطا
        errors = result.get("errors", [])
        if errors:
            msg = ", ".join(e.get("errorDescription", str(e)) for e in errors)
            raise Exception(f"getKey خطا: {msg}")

        # استخراج keyData
        key_data = (
            result.get("keyData")
            or (result.get("data", {}).get("keyData") if isinstance(result.get("data"), dict) else None)
            or result.get("data")
        )

        if not key_data:
            raise Exception(f"keyData در پاسخ یافت نشد: {resp.text[:200]}")

        # مشکل قبلی: key_data خام (base64 مدولوس) مستقیم ذخیره می‌شد.
        # بعداً rsa_encrypt -> import_public_key سعی می‌کرد load_der_public_key کند که fail می‌شد.
        # راه‌حل: همین‌جا به PEM تبدیل کن و PEM را ذخیره کن.
        try:
            pem = base64_modulus_to_pem(key_data)
            self.key_store[StorageKeys.RSA_PUBLIC] = pem
        except Exception:
            # اگر key_data از قبل DER/PEM باشد، خام ذخیره می‌شود
            self.key_store[StorageKeys.RSA_PUBLIC] = key_data

        return key_data

    async def key_exchange(self):
        """تبادل کلید با سرور - ارسال SharedKey و WorkingKey رمزشده با RSA"""
        import base64

        shared_key = self.crypto.generate_key(32)
        self.key_store[StorageKeys.SHARED_KEY] = base64.b64encode(shared_key).decode()

        working_key = self.crypto.generate_key(32)
        self.key_store[StorageKeys.WORKING_KEY] = base64.b64encode(working_key).decode()

        shared_hex = shared_key.hex()
        working_hex = working_key.hex()

        data_key = self.crypto.rsa_encrypt(shared_hex)
        mac_key = self.crypto.rsa_encrypt(working_hex)

        body = {"DataKey": data_key, "MacKey": mac_key}
        await self._post_tolerant(Endpoints.KEY_EXCHANGE, body)

    async def request_otp(self, phone: str) -> dict:
        """درخواست کد OTP"""
        self.current_phone = phone
        data = await self._post_data(Endpoints.REGISTER_REQUEST, {"PhoneNumber": phone})
        return {"token": data.get("token"), "reagent_number": data.get("reagentNumber")}

    async def verify_code(self, code: str, token: str, reagent_number: str) -> dict:
        """تایید کد OTP و دریافت توکن"""
        data = await self._post_data(Endpoints.ACTIVATION, {
            "VerificationCode": code,
            "Token": token,
            "ReagentNumber": reagent_number,
        })
        self._persist_tokens(data)
        return data

    async def refresh_auth(self):
        """تمدید توکن"""
        rt = self.key_store.get(StorageKeys.REFRESH_TOKEN)
        if not rt:
            raise Exception("توکن refresh موجود نیست.")
        data = await self._post_data(Endpoints.REFRESH_TOKEN, {"RefreshToken": rt})
        self._persist_tokens(data)
        # بعد از refresh، کلیدها را دوباره مبادله کن
        if self.key_store.get(StorageKeys.RSA_PUBLIC):
            await self.key_exchange()

    async def ensure_secure_channel(self):
        """اطمینان از برقراری کانال امن"""
        if (self.key_store.get(StorageKeys.SHARED_KEY)
                and self.key_store.get(StorageKeys.WORKING_KEY)):
            return

        if not self.key_store.get(StorageKeys.RSA_PUBLIC):
            await self.fetch_public_key()

        await self.key_exchange()

    async def buy_charge(self, amount: int, target_mobile: str, provider_id: str,
                         card: dict) -> dict:
        """
        خرید شارژ - تست اصلی برای اسکن کارت
        card: {pan, cvv2, expire_month, expire_year, pin}
        """
        await self.ensure_secure_channel()

        # ساخت بدنه پرداخت
        media = {}

        if card.get("cvv2"):
            media["Cvv2"] = self.crypto.aes_encrypt(card["cvv2"])
        if card.get("pin"):
            media["Pin"] = self.crypto.aes_encrypt(card["pin"])

        expire = (card.get("expire_year") or "") + _pad_left2(card.get("expire_month"))
        if len(_digits_only(expire)) == 4:
            media["ExpireDate"] = self.crypto.aes_encrypt(expire)

        if card.get("pan"):
            media["Pan"] = self.crypto.aes_encrypt(card["pan"])

        body = {
            "paymentMedia": media,
            "Amount": amount,
            "TTL": int(time.time() * 1000),
            "TargetMobileNo": target_mobile,
            "ProviderId": provider_id,
            "OrderId": int(time.time() * 1000),
        }

        content_type = "application/vnd.sadad.payment.charge.pan+json"

        # ارسال درخواست
        status, text = await self._post_signed_once(Endpoints.PAY_CHARGE, body, content_type)

        if status == 401:
            await self.refresh_auth()
            body["OrderId"] = int(time.time() * 1000)
            body["TTL"] = int(time.time() * 1000)
            status, text = await self._post_signed_once(Endpoints.PAY_CHARGE, body, content_type)

        return self._parse_charge_outcome(text, status)

    # --- متدهای داخلی ---

    async def _post_data(self, path: str, body: dict) -> dict:
        """ارسال POST و دریافت data از envelope"""
        result = await self._post(path, body)
        data = result.get("data")
        if data is None:
            raise Exception("پاسخ خالی از سرور")
        return data

    async def _post(self, path: str, body: dict) -> dict:
        """ارسال POST به API"""
        url = self.base_address + path
        body_str = json.dumps(body, ensure_ascii=False)

        headers = {"Content-Type": "application/json"}
        self._apply_headers(headers, path, body_str)

        client = await self._get_client()
        resp = await client.post(url, content=body_str.encode("utf-8"), headers=headers)
        text = resp.text

        try:
            parsed = json.loads(text)
        except (json.JSONDecodeError, ValueError):
            raise Exception(f"پاسخ غیرمنتظره (HTTP {resp.status_code})")

        # بررسی خطا
        error = parsed.get("error")
        if error and str(error.get("code", "200")) != "200":
            raise Exception(error.get("message") or "عملیات ناموفق")

        return parsed

    async def _post_tolerant(self, path: str, body: dict):
        """ارسال POST بدون نیاز به پاسخ دقیق"""
        url = self.base_address + path
        body_str = json.dumps(body, ensure_ascii=False)

        headers = {"Content-Type": "application/json"}
        self._apply_headers(headers, path, body_str)

        client = await self._get_client()
        resp = await client.post(url, content=body_str.encode("utf-8"), headers=headers)

        if resp.status_code >= 400:
            raise Exception(f"{path} ناموفق (HTTP {resp.status_code})")

        if resp.text.strip():
            try:
                env = json.loads(resp.text)
                error = env.get("error")
                if error and str(error.get("code", "200")) != "200":
                    raise Exception(error.get("message") or "عملیات ناموفق")
            except (json.JSONDecodeError, ValueError):
                pass

    async def _post_signed_once(self, path: str, body: dict, content_type: str) -> tuple:
        """ارسال یک درخواست امضاشده - خروجی (status_code, text)"""
        url = self.base_address + path
        body_str = json.dumps(body, ensure_ascii=False)

        headers = {"Content-Type": content_type}
        self._apply_headers(headers, path, body_str)

        client = await self._get_client()
        resp = await client.post(url, content=body_str.encode("utf-8"), headers=headers)
        return resp.status_code, resp.text

    @staticmethod
    def _parse_charge_outcome(text: str, status: int) -> dict:
        """تجزیه نتیجه خرید شارژ"""
        try:
            env = json.loads(text)
            error = env.get("error")
            if error and str(error.get("code", "200")) != "200":
                return {
                    "success": False,
                    "error_code": error.get("code"),
                    "message": error.get("message"),
                }
            data = env.get("data", {})
            if isinstance(data, dict):
                data["success"] = True
            else:
                data = {"success": True}
            return data
        except (json.JSONDecodeError, ValueError):
            return {
                "success": False,
                "error_code": str(status),
                "message": f"HTTP {status}",
            }

    def get_response_type(self, result: dict) -> str:
        """تشخیص نوع پاسخ: valid, rate_limit, blocked, error"""
        msg = result.get("message", "")
        if not msg:
            if result.get("success"):
                return "valid"
            return "error"

        for kw in VALID_KEYWORDS:
            if kw in msg:
                return "valid"
        for kw in RATE_LIMIT_KEYWORDS:
            if kw in msg:
                return "rate_limit"
        for kw in BLOCKED_KEYWORDS:
            if kw in msg:
                return "blocked"

        return "error"


def _pad_left2(s: Optional[str]) -> str:
    if not s:
        return ""
    return s.zfill(2) if len(s) < 2 else s


def _digits_only(s: str) -> str:
    return "".join(c for c in s if c.isdigit())
