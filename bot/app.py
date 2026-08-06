"""
نقطه ورود اصلی ربات تلگرام - Telethon
مدیریت هندلرها و دیسپچر اصلی
"""
import asyncio

from telethon import TelegramClient, events

import config
import db
from bot.panels import main_menu, accounts, scan, workers, logs


async def start_bot():
    """راه‌اندازی و اجرای ربات تلگرام"""
    # بررسی تنظیمات
    problems = config.validate()
    if problems:
        print(f"[ERROR] تنظیمات ناقص: {', '.join(problems)}")
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
            return

        # بررسی ورکر
        handled = await workers.handle_worker_message(bot, event)
        if handled:
            return

    # لاگ شروع
    await db.add_log("info", "general", "ربات راه‌اندازی شد")
    print(f"[+] ربات آیوا اسکنر راه‌اندازی شد - {config.now_str()}")

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
    await bot.run_until_disconnected()


async def shutdown_bot():
    """خاموش کردن ربات"""
    await db.close()
