"""
error_logger.py — ماژول مستقل لاگ‌گیری و گزارش خطا
=====================================================
ویژگی‌ها:
  1. هندلر خطای سراسری (sys.excepthook + asyncio exception handler)
  2. دکوریتور @catch_errors برای توابع sync و async
  3. ارسال پیام خطا به چت ادمین (LOG_CHAT_ID) با اطلاعات کامل
  4. تقسیم پیام‌های بلند: خلاصه در پیام + traceback کامل به‌صورت فایل .txt
  5. ضد اسپم: هش traceback، cooldown 60 ثانیه، شمارنده تکرار
  6. لاگ محلی در logs/bot.log با RotatingFileHandler (5MB × 3 بکاپ)
  7. هرگز crash نمی‌کند اگر ارسال تلگرام شکست بخورد
  8. HTML escape برای parse_mode='html' تلگرام
  9. تابع test_error_logger() برای تست عملکرد
"""

import asyncio
import functools
import hashlib
import html
import inspect
import io
import logging
import os
import sys
import time
import traceback
from datetime import datetime
from logging.handlers import RotatingFileHandler
from typing import Optional


# ──────────────────────────────────────────────
# ۶. راه‌اندازی لاگ محلی
# ──────────────────────────────────────────────

os.makedirs("logs", exist_ok=True)

_file_handler = RotatingFileHandler(
    filename="logs/bot.log",
    maxBytes=5 * 1024 * 1024,   # 5 مگابایت
    backupCount=3,
    encoding="utf-8",
)
_file_handler.setFormatter(logging.Formatter(
    fmt="%(asctime)s [%(levelname)s] %(name)s — %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
))

_console_handler = logging.StreamHandler(sys.stdout)
_console_handler.setFormatter(logging.Formatter(
    fmt="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
))

logger = logging.getLogger("bot_error_logger")
logger.setLevel(logging.DEBUG)
if not logger.handlers:
    logger.addHandler(_file_handler)
    logger.addHandler(_console_handler)



# ──────────────────────────────────────────────
# ۵. ضد اسپم — ذخیره هش‌های اخیر
# ──────────────────────────────────────────────

# {hash_str: {"first_seen": float, "count": int, "last_seen": float}}
_spam_cache: dict = {}
_COOLDOWN_SECONDS = 60


def _tb_hash(tb_text: str) -> str:
    """هش MD5 از متن traceback برای تشخیص تکراری بودن"""
    return hashlib.md5(tb_text.encode("utf-8", errors="replace")).hexdigest()


def _check_spam(tb_hash: str) -> tuple[bool, int]:
    """
    بررسی آیا این خطا تکراری است.
    خروجی: (is_spam, repeat_count)
      is_spam=True  → نفرست، فقط شمارنده را بالا ببر
      is_spam=False → بفرست (جدید یا cooldown تموم شده)
    """
    now = time.monotonic()
    entry = _spam_cache.get(tb_hash)

    if entry is None:
        # اولین بار
        _spam_cache[tb_hash] = {"first_seen": now, "count": 1, "last_seen": now}
        return False, 1

    elapsed = now - entry["last_seen"]
    entry["count"] += 1
    entry["last_seen"] = now

    if elapsed < _COOLDOWN_SECONDS:
        # در پنجره cooldown — اسپم است
        return True, entry["count"]
    else:
        # cooldown تمام شده — دوباره بفرست
        return False, entry["count"]


def _clear_spam_entry(tb_hash: str):
    """پاک کردن یک ورودی بعد از ارسال گزارش تکرار"""
    _spam_cache.pop(tb_hash, None)



# ──────────────────────────────────────────────
# ۸. HTML escape و فرمت‌دهی پیام
# ──────────────────────────────────────────────

def _h(text: str) -> str:
    """HTML escape برای parse_mode='html' تلگرام"""
    return html.escape(str(text), quote=False)


def _tz_now() -> str:
    """زمان فعلی با تایم‌زون از config (fallback: UTC)"""
    try:
        import config
        return config.now_dt().strftime("%Y-%m-%d %H:%M:%S %Z")
    except Exception:
        return datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S UTC")


def _build_error_report(
    exc: BaseException,
    tb_text: str,
    handler_name: str = "—",
    user_id: Optional[int] = None,
    username: Optional[str] = None,
    chat_id: Optional[int] = None,
    user_input: Optional[str] = None,
    extra: Optional[str] = None,
) -> str:
    """
    ساخت متن HTML گزارش خطا برای تلگرام.
    شامل: زمان، exception class، متن خطا، فایل+خط+تابع،
           traceback (تا 800 کاراکتر)، اطلاعات کاربر، ورودی کاربر.
    """
    # استخراج آخرین فریم traceback
    tb_obj = exc.__traceback__
    last_frame_info = "—"
    if tb_obj:
        import traceback as _tb
        frames = list(_tb.extract_tb(tb_obj))
        if frames:
            lf = frames[-1]
            last_frame_info = (
                f"{_h(lf.filename)}:{lf.lineno} در <code>{_h(lf.name)}()</code>"
            )

    # خلاصه traceback (برای متن پیام — حداکثر 800 کاراکتر)
    tb_short = tb_text[-800:] if len(tb_text) > 800 else tb_text

    lines = [
        "🔴 <b>خطای ربات</b>",
        "",
        f"🕐 <b>زمان:</b> <code>{_h(_tz_now())}</code>",
        f"⚡ <b>Exception:</b> <code>{_h(type(exc).__name__)}</code>",
        f"💬 <b>پیام:</b> <code>{_h(str(exc))}</code>",
        f"📍 <b>مکان:</b> {last_frame_info}",
        f"🔧 <b>هندلر:</b> <code>{_h(handler_name)}</code>",
        "",
        "👤 <b>کاربر</b>",
        f"  ID: <code>{_h(user_id or '—')}</code>",
        f"  Username: <code>{_h(username or '—')}</code>",
        f"  Chat ID: <code>{_h(chat_id or '—')}</code>",
        f"  ورودی: <code>{_h((user_input or '—')[:200])}</code>",
    ]

    if extra:
        lines += ["", f"ℹ️ <b>اطلاعات اضافه:</b> {_h(extra)}"]

    lines += [
        "",
        "📋 <b>Traceback (آخرین بخش):</b>",
        f"<pre>{_h(tb_short)}</pre>",
    ]

    return "\n".join(lines)



# ──────────────────────────────────────────────
# ۳ و ۴ و ۷. ارسال گزارش به تلگرام
# ──────────────────────────────────────────────

# نگهداری مرجع به bot برای ارسال پیام
_bot_ref = None
_log_chat_id: Optional[int] = None

TELEGRAM_MAX_LEN = 4096


def init_logger(bot, log_chat_id: int):
    """
    مقداردهی اولیه — باید یک‌بار پس از start_bot() فراخوانی شود.
    bot       : TelegramClient instance از Telethon
    log_chat_id: آیدی چت ادمین (LOG_CHAT_ID)
    """
    global _bot_ref, _log_chat_id
    _bot_ref = bot
    _log_chat_id = log_chat_id
    logger.info(f"ErrorLogger فعال شد | log_chat_id={log_chat_id}")


async def _send_to_telegram(
    exc: BaseException,
    tb_text: str,
    handler_name: str = "—",
    user_id: Optional[int] = None,
    username: Optional[str] = None,
    chat_id: Optional[int] = None,
    user_input: Optional[str] = None,
    extra: Optional[str] = None,
    repeat_count: int = 1,
):
    """
    ارسال گزارش خطا به تلگرام.
    — اگر bot آماده نیست یا ارسال fail کرد، هرگز crash نمی‌کند (ویژگی ۷).
    — اگر پیام > 4096 کاراکتر: خلاصه + traceback به‌صورت فایل .txt (ویژگی ۴).
    """
    if _bot_ref is None or _log_chat_id is None:
        return

    report = _build_error_report(
        exc, tb_text, handler_name, user_id, username, chat_id, user_input, extra
    )

    # اگر تکراری بود، یک خط اضافه
    if repeat_count > 1:
        report = f"🔁 <b>تکرار {repeat_count} بار</b>\n\n" + report

    try:
        if len(report) <= TELEGRAM_MAX_LEN:
            # ویژگی ۴ — پیام کوتاه: مستقیم بفرست
            await _bot_ref.send_message(
                _log_chat_id,
                report,
                parse_mode="html",
            )
        else:
            # ویژگی ۴ — پیام بلند: خلاصه + فایل .txt
            summary_lines = report.split("\n")[:20]
            summary = "\n".join(summary_lines) + "\n\n📎 <b>Traceback کامل در فایل پیوست</b>"

            # اطمینان از اینکه خلاصه هم از حد مجاز تجاوز نمی‌کند
            if len(summary) > TELEGRAM_MAX_LEN:
                summary = summary[:TELEGRAM_MAX_LEN - 50] + "\n... (کوتاه شد)"

            await _bot_ref.send_message(
                _log_chat_id,
                summary,
                parse_mode="html",
            )

            # ارسال فایل متنی traceback کامل
            timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
            filename = f"traceback_{timestamp}.txt"
            file_content = (
                f"Timestamp : {_tz_now()}\n"
                f"Exception : {type(exc).__name__}: {exc}\n"
                f"Handler   : {handler_name}\n"
                f"User ID   : {user_id}\n"
                f"Username  : {username}\n"
                f"Chat ID   : {chat_id}\n"
                f"Input     : {user_input}\n"
                f"Extra     : {extra}\n"
                f"Repeat    : {repeat_count}\n"
                f"\n{'='*60}\nFULL TRACEBACK\n{'='*60}\n\n"
                f"{tb_text}"
            )
            file_obj = io.BytesIO(file_content.encode("utf-8"))
            file_obj.name = filename
            await _bot_ref.send_file(
                _log_chat_id,
                file_obj,
                caption=f"📎 traceback کامل — {_h(type(exc).__name__)}",
                parse_mode="html",
            )

    except Exception as send_err:
        # ویژگی ۷ — هرگز crash نمی‌کند
        logger.error(
            f"ارسال گزارش به تلگرام شکست خورد: {send_err}\n"
            f"خطای اصلی که گزارش نشد: {type(exc).__name__}: {exc}"
        )



# ──────────────────────────────────────────────
# هسته مشترک: پردازش یک خطا
# ──────────────────────────────────────────────

async def _process_exception(
    exc: BaseException,
    handler_name: str = "—",
    user_id: Optional[int] = None,
    username: Optional[str] = None,
    chat_id: Optional[int] = None,
    user_input: Optional[str] = None,
    extra: Optional[str] = None,
):
    """
    پردازش مرکزی یک exception:
    ۱. لاگ محلی (همیشه)
    ۲. بررسی ضد اسپم
    ۳. ارسال به تلگرام (اگر جدید یا cooldown تمام شده)
    """
    # تولید traceback کامل
    tb_text = "".join(
        traceback.format_exception(type(exc), exc, exc.__traceback__)
    )

    # لاگ محلی — همیشه
    logger.error(
        f"[{handler_name}] {type(exc).__name__}: {exc} "
        f"| user={user_id} | chat={chat_id} | input={str(user_input or '')[:100]}\n"
        f"{tb_text}"
    )

    # ۵. ضد اسپم
    h = _tb_hash(tb_text)
    is_spam, count = _check_spam(h)

    if is_spam:
        # تکراری در پنجره cooldown — فقط لاگ محلی کافی است
        logger.debug(f"خطای تکراری ({count} بار): {type(exc).__name__} — گزارش تلگرام حذف شد")
        return

    # اگر تعداد تکرار > 1 یعنی cooldown تمام شده، entry را پاک می‌کنیم
    if count > 1:
        _clear_spam_entry(h)

    await _send_to_telegram(
        exc=exc,
        tb_text=tb_text,
        handler_name=handler_name,
        user_id=user_id,
        username=username,
        chat_id=chat_id,
        user_input=user_input,
        extra=extra,
        repeat_count=count,
    )


def _extract_event_info(event) -> tuple[Optional[int], Optional[str], Optional[int], Optional[str]]:
    """
    استخراج اطلاعات کاربر از یک Telethon event.
    خروجی: (user_id, username, chat_id, user_input)
    """
    user_id = None
    username = None
    chat_id = None
    user_input = None

    try:
        user_id = getattr(event, "sender_id", None)
    except Exception:
        pass

    try:
        sender = getattr(event, "sender", None)
        if sender:
            uname = getattr(sender, "username", None)
            first = getattr(sender, "first_name", None)
            last = getattr(sender, "last_name", None)
            username = uname or " ".join(filter(None, [first, last])) or None
    except Exception:
        pass

    try:
        chat_id = getattr(event, "chat_id", None)
    except Exception:
        pass

    try:
        # NewMessage: متن پیام
        msg = getattr(event, "message", None)
        if msg:
            user_input = getattr(msg, "text", None) or getattr(msg, "raw_text", None)
        # اگر text مستقیم در event باشد
        if not user_input:
            user_input = getattr(event, "text", None) or getattr(event, "raw_text", None)
        # CallbackQuery: data
        if not user_input:
            data = getattr(event, "data", None)
            if data:
                user_input = f"[callback] {data.decode('utf-8', errors='replace')}"
    except Exception:
        pass

    return user_id, username, chat_id, user_input



# ──────────────────────────────────────────────
# ۲. دکوریتور @catch_errors
# ──────────────────────────────────────────────

def catch_errors(handler_name: str = None, extra: str = None):
    """
    دکوریتور برای گرفتن exception در توابع sync و async.

    استفاده:
        @catch_errors()
        async def my_handler(event): ...

        @catch_errors(handler_name="scan_start", extra="فاز اسکن")
        async def scan_start(event): ...

        @catch_errors()
        def sync_func(): ...

    — اگر event به‌عنوان اولین آرگومان باشد، اطلاعات کاربر استخراج می‌شود.
    — exception دوباره raise نمی‌شود (ربات crash نمی‌کند).
    """
    def decorator(func):
        _name = handler_name or func.__qualname__

        if inspect.iscoroutinefunction(func):
            @functools.wraps(func)
            async def async_wrapper(*args, **kwargs):
                # استخراج event از آرگومان‌ها (معمولاً اولین arg غیر-self)
                event = None
                for arg in args:
                    if hasattr(arg, "sender_id") or hasattr(arg, "chat_id"):
                        event = arg
                        break

                try:
                    return await func(*args, **kwargs)
                except Exception as exc:
                    uid, uname, cid, uinput = (None, None, None, None)
                    if event is not None:
                        uid, uname, cid, uinput = _extract_event_info(event)
                    await _process_exception(
                        exc=exc,
                        handler_name=_name,
                        user_id=uid,
                        username=uname,
                        chat_id=cid,
                        user_input=uinput,
                        extra=extra,
                    )
                    # بازگشت None — ربات ادامه می‌دهد
                    return None

            return async_wrapper

        else:
            @functools.wraps(func)
            def sync_wrapper(*args, **kwargs):
                try:
                    return func(*args, **kwargs)
                except Exception as exc:
                    tb_text = "".join(
                        traceback.format_exception(type(exc), exc, exc.__traceback__)
                    )
                    logger.error(
                        f"[{_name}] {type(exc).__name__}: {exc}\n{tb_text}"
                    )
                    # برای sync نمی‌توانیم await کنیم — فقط لاگ محلی
                    # اگر event loop در دسترس است، task ایجاد می‌کنیم
                    try:
                        loop = asyncio.get_event_loop()
                        if loop.is_running():
                            loop.create_task(
                                _process_exception(
                                    exc=exc,
                                    handler_name=_name,
                                    extra=extra,
                                )
                            )
                    except Exception:
                        pass
                    return None

            return sync_wrapper

    # پشتیبانی از @catch_errors بدون پرانتز
    if callable(handler_name):
        _func = handler_name
        handler_name = _func.__qualname__
        return decorator(_func)

    return decorator



# ──────────────────────────────────────────────
# ۱. هندلر خطای سراسری
# ──────────────────────────────────────────────

def _sync_excepthook(exc_type, exc_value, exc_tb):
    """هندلر سراسری برای exception های thread اصلی"""
    if issubclass(exc_type, KeyboardInterrupt):
        # Ctrl+C را نمی‌گیریم
        sys.__excepthook__(exc_type, exc_value, exc_tb)
        return

    logger.critical(
        "خطای سراسری catch‌نشده:\n"
        + "".join(traceback.format_exception(exc_type, exc_value, exc_tb))
    )

    # تلاش برای ارسال به تلگرام
    try:
        loop = asyncio.get_event_loop()
        if loop.is_running():
            loop.create_task(
                _process_exception(
                    exc=exc_value,
                    handler_name="[global:sys.excepthook]",
                )
            )
        else:
            loop.run_until_complete(
                _process_exception(
                    exc=exc_value,
                    handler_name="[global:sys.excepthook]",
                )
            )
    except Exception as e:
        logger.error(f"خطا در ارسال سراسری: {e}")


def _async_exception_handler(loop, context: dict):
    """
    هندلر سراسری برای exception های داخل task های asyncio.
    این‌ها توسط sys.excepthook گرفته نمی‌شوند.
    """
    exc = context.get("exception")
    msg = context.get("message", "خطای asyncio ناشناخته")
    future = context.get("future") or context.get("task")
    task_name = getattr(future, "__name__", None) or getattr(future, "_coro", None)
    if hasattr(task_name, "__qualname__"):
        task_name = task_name.__qualname__

    handler_label = f"[global:asyncio] {task_name or msg}"

    if exc is None:
        logger.error(f"asyncio context خطا (بدون exception): {msg}")
        return

    logger.error(
        f"خطای asyncio task:\n"
        + "".join(traceback.format_exception(type(exc), exc, exc.__traceback__))
    )

    loop.create_task(
        _process_exception(
            exc=exc,
            handler_name=handler_label,
        )
    )


def install_global_handlers():
    """
    نصب هندلرهای سراسری.
    باید یک‌بار قبل از شروع ربات فراخوانی شود.
    برای async exception handler، باید بعد از راه‌اندازی event loop صدا زده شود.
    """
    # هندلر thread اصلی
    sys.excepthook = _sync_excepthook
    logger.info("هندلر سراسری sys.excepthook نصب شد")

    # هندلر asyncio — اگر loop در دسترس است
    try:
        loop = asyncio.get_event_loop()
        loop.set_exception_handler(_async_exception_handler)
        logger.info("هندلر سراسری asyncio نصب شد")
    except RuntimeError:
        # اگر هنوز event loop ایجاد نشده — در start_bot نصب می‌شود
        logger.warning("Event loop هنوز آماده نیست — asyncio handler بعداً نصب می‌شود")


def install_async_handler():
    """
    نصب هندلر asyncio روی loop جاری.
    باید داخل یک coroutine (مثلاً start_bot) فراخوانی شود.
    """
    try:
        loop = asyncio.get_running_loop()
        loop.set_exception_handler(_async_exception_handler)
        logger.info("هندلر asyncio روی loop جاری نصب شد")
    except RuntimeError as e:
        logger.error(f"نصب asyncio handler شکست خورد: {e}")



# ──────────────────────────────────────────────
# ۹. تابع تست
# ──────────────────────────────────────────────

async def test_error_logger(bot=None, log_chat_id: int = None):
    """
    تست جامع عملکرد سیستم لاگ.

    استفاده از ربات:
        from error_logger import test_error_logger
        await test_error_logger(bot, config.LOG_CHAT_ID)

    استفاده مستقل (فقط لاگ محلی):
        await test_error_logger()
    """
    if bot is not None and log_chat_id is not None:
        init_logger(bot, log_chat_id)

    print("[TEST] شروع تست error_logger...")

    # ── تست ۱: خطای ساده با catch_errors ──
    @catch_errors(handler_name="test_simple_error")
    async def _raise_simple():
        x = {}
        return x["nonexistent_key"]  # KeyError

    print("[TEST 1] KeyError از طریق @catch_errors ...")
    await _raise_simple()
    print("[TEST 1] ✅ خطا catch شد — ربات ادامه داد")
    await asyncio.sleep(1)

    # ── تست ۲: خطای تو در تو با traceback بلند ──
    @catch_errors(handler_name="test_nested_error", extra="تست traceback بلند")
    async def _raise_nested():
        def _level3():
            raise ValueError("خطای داخلی — این پیام در traceback ظاهر می‌شود")

        def _level2():
            _level3()

        def _level1():
            _level2()

        _level1()

    print("[TEST 2] ValueError تو در تو ...")
    await _raise_nested()
    print("[TEST 2] ✅ خطای تو در تو catch شد")
    await asyncio.sleep(1)

    # ── تست ۳: ضد اسپم ──
    print("[TEST 3] تست ضد اسپم — همان خطا ۳ بار ...")

    @catch_errors(handler_name="test_spam")
    async def _spam_error():
        raise RuntimeError("خطای تکراری برای تست spam filter")

    for i in range(3):
        await _spam_error()
        print(f"[TEST 3] اجرای {i + 1}/3")
        await asyncio.sleep(0.1)
    print("[TEST 3] ✅ فقط اولین بار باید به تلگرام رفته باشد")
    await asyncio.sleep(1)

    # ── تست ۴: خطا با اطلاعات کاربر مصنوعی ──
    print("[TEST 4] تست گزارش با اطلاعات کاربر ...")
    try:
        raise TypeError("تست خطا با اطلاعات کاربر مصنوعی")
    except TypeError as e:
        await _process_exception(
            exc=e,
            handler_name="test_with_user_info",
            user_id=123456789,
            username="test_user",
            chat_id=987654321,
            user_input="/start — دستور تستی",
            extra="این یک تست دستی است",
        )
    print("[TEST 4] ✅ گزارش با اطلاعات کاربر ارسال شد")
    await asyncio.sleep(1)

    # ── تست ۵: پیام بسیار بلند (بیش از 4096 کاراکتر) ──
    print("[TEST 5] تست پیام طولانی (فایل .txt) ...")
    try:
        # ایجاد traceback بلند با stack recursion
        def _deep(n):
            if n == 0:
                raise OverflowError("خطای عمیق برای تولید traceback طولانی " + "x" * 200)
            return _deep(n - 1)
        _deep(30)
    except (OverflowError, RecursionError) as e:
        await _process_exception(
            exc=e,
            handler_name="test_long_message",
            user_id=111,
            username="tester",
            extra="تست پیام بلند — باید فایل .txt ضمیمه شود",
        )
    print("[TEST 5] ✅ تست پیام طولانی انجام شد")

    print("\n[TEST] تمام تست‌ها اجرا شدند ✅")
    print("[TEST] لاگ محلی در: logs/bot.log")
    if bot is not None:
        print(f"[TEST] گزارش‌ها در چت {log_chat_id} ارسال شدند")

