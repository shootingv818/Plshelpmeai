"""
نقطه ورود اصلی ربات تلگرام - Telethon
مدیریت هندلرها و دیسپچر اصلی
"""
import asyncio

from telethon import TelegramClient, events

import config
import db
from bot.panels import main_menu, accounts, scan, workers, logs


async def _periodic_health_check(bot):
    """بررسی دوره‌ای سلامت ورکرها در پس‌زمینه"""
    while True:
        try:
            await asyncio.sleep(config.HEALTH_INTERVAL)
            worker_list = await db.list_workers(enabled_only=True)
            if not worker_list:
                continue

            from worker.deploy import check_worker_health
            for w in worker_list:
                try:
                    health = await check_worker_health(w)
                    if health.get("ok"):
                        await db.update_worker(w["id"], status="online",
                                               ping_ms=health.get("ping_ms", -1))
                    else:
                        await db.update_worker(w["id"], status="offline",
                                               ping_ms=health.get("ping_ms", -1))
                except Exception:
                    await db.update_worker(w["id"], status="error", ping_ms=-1)
        except asyncio.CancelledError:
            break
        except Exception:
            await asyncio.sleep(60)


async def start_bot():
    """راه‌اندازی و اجرای ربات تلگرام"""
    # بررسی تنظیمات
    problems = config.validate()
    if problems:
        print(f"[ERROR] تنظیمات ناقص: {', '.join(problems)}")
        print("[ERROR] برای رمزنگاری، WORKER_SECRET الزامی است.")
        return

    # اتصال به دیتابیس
    await db.init()

    # ساخت کلاینت Telethon
    bot = TelegramClient(
        "bot_session",
        config.API_ID,
        config.API_HASH,
    )

    await bot.start(bot_token=config.BOT_TOKEN)

    # ثبت هندلرهای پنل‌ها
    main_menu.register(bot)
    accounts.register(bot)
    scan.register(bot)
    workers.register(bot)
    logs.register(bot)

    # هندلر پیام عمومی (مکالمه‌ها)
    @bot.on(events.NewMessage(func=lambda e: e.sender_id == config.OWNER_ID and e.is_private))
    async def message_dispatcher(event):
        """توزیع پیام‌های متنی به پنل‌های مربوطه"""
        # بررسی اسکن
        handled = await scan.handle_scan_message(bot, event)
        if handled:
            raise events.StopPropagation

        # بررسی ورکر
        handled = await workers.handle_worker_message(bot, event)
        if handled:
            raise events.StopPropagation

    # لاگ شروع
    await db.add_log("info", "general", "ربات راه‌اندازی شد")
    print(f"[+] ربات آیوا اسکنر راه‌اندازی شد - {config.now_str()}")

    # شروع بررسی دوره‌ای سلامت ورکرها
    health_task = asyncio.create_task(_periodic_health_check(bot))

    # ارسال پیام شروع به مالک
    try:
        from bot.utils import panel_message
        start_msg = panel_message(
            "🟢 ربات آنلاین شد",
            [
                f"⏰ زمان: {config.now_str()}",
                f"حالت: {config.MODE}",
            ]
        )
        await bot.send_message(config.OWNER_ID, start_msg)
    except Exception:
        pass

    # اجرای ربات
    try:
        await bot.run_until_disconnected()
    finally:
        health_task.cancel()


async def shutdown_bot():
    """خاموش کردن ربات"""
    await db.close()
