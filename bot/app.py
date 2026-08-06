"""
نقطه ورود اصلی ربات تلگرام - Telethon
مدیریت هندلرها و دیسپچر اصلی
"""
import asyncio
from urllib.parse import urlparse

from telethon import TelegramClient, events
import socks

import config
import db
from bot.panels import main_menu, accounts, scan, workers, logs


def _parse_proxy(proxy_url: str):
    """
    پارس آدرس پروکسی به فرمت Telethon
    فرمت‌های پشتیبانی:
      socks5://user:pass@host:port
      socks4://host:port
      http://user:pass@host:port
      mtproxy://secret@host:port
    """
    if not proxy_url:
        return None

    parsed = urlparse(proxy_url)
    scheme = parsed.scheme.lower()
    host = parsed.hostname or ""
    port = parsed.port or 1080
    username = parsed.username
    password = parsed.password

    if scheme in ("socks5", "socks5h"):
        return (socks.SOCKS5, host, port, True, username, password)
    elif scheme in ("socks4", "socks4a"):
        return (socks.SOCKS4, host, port, True, username, password)
    elif scheme in ("http", "https"):
        return (socks.HTTP, host, port, True, username, password)
    elif scheme in ("mtproxy", "mtproto"):
        # MTProto proxy: secret در username یا password
        secret = username or password or ""
        return ("mtproxy", host, port, secret)
    else:
        print(f"[!] فرمت پروکسی ناشناخته: {scheme}")
        return None


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

    # ساخت کلاینت Telethon (پروکسی فقط برای تلگرام)
    proxy_settings = None
    if config.TELEGRAM_PROXY:
        proxy_settings = _parse_proxy(config.TELEGRAM_PROXY)

    # اگه API mirror تنظیم شده ولی پروکسی نه، هشدار بده
    # (Telethon مستقیم TCP به DC تلگرام وصل می‌شه، نه HTTP API)
    if config.TELEGRAM_API_BASE and not proxy_settings:
        print("[!] هشدار: TELEGRAM_API_BASE فقط برای HTTP bot API کار می‌کنه.")
        print("[!] Telethon نیاز به SOCKS5 proxy داره (TELEGRAM_PROXY)")

    bot = TelegramClient(
        "bot_session",
        config.API_ID,
        config.API_HASH,
        proxy=proxy_settings,
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
