"""تنظیمات پروژه - بارگذاری از فایل .env"""
import os

from dotenv import load_dotenv

load_dotenv()


def _int(name: str, default: int = 0) -> int:
    try:
        return int(os.getenv(name, str(default)))
    except (TypeError, ValueError):
        return default


def _float(name: str, default: float = 0.0) -> float:
    try:
        return float(os.getenv(name, str(default)))
    except (TypeError, ValueError):
        return default


def _bool(name: str, default: bool = False) -> bool:
    return os.getenv(name, str(default)).strip().lower() in ("1", "true", "yes", "on")


# ---- تلگرام ----
API_ID = _int("API_ID")
API_HASH = os.getenv("API_HASH", "")
BOT_TOKEN = os.getenv("BOT_TOKEN", "")
OWNER_ID = _int("OWNER_ID")
LOG_GROUP_ID = _int("LOG_GROUP_ID")

# ---- حالت اجرا ----
# master = ربات تلگرام (هماهنگ‌کننده)
# worker = سرور API بدون پنل (فقط اجرای دستورات مستر)
MODE = (os.getenv("MODE", "master") or "master").strip().lower()

# ---- ورکر ----
# کلید Fernet برای رمزنگاری رمزهای ورکر در دیتابیس
WORKER_SECRET = os.getenv("WORKER_SECRET", "").strip()
WORKER_API_PORT = _int("WORKER_API_PORT", 8765)
WORKER_API_TOKEN = os.getenv("WORKER_API_TOKEN", "").strip()
WORKER_BIND_HOST = os.getenv("WORKER_BIND_HOST", "127.0.0.1").strip()

# ---- گیت ----
GIT_REPO_URL = os.getenv("GIT_REPO_URL", "").strip()
GIT_BRANCH = os.getenv("GIT_BRANCH", "main").strip()

# ---- اسکنر ----
SCAN_DELAY = _float("SCAN_DELAY", 0.15)
SCAN_CHARGE_AMOUNT = _int("SCAN_CHARGE_AMOUNT", 10000)
SCAN_TARGET_MOBILE = os.getenv("SCAN_TARGET_MOBILE", "").strip()
SCAN_PROVIDER_ID = os.getenv("SCAN_PROVIDER_ID", "10").strip()

# ---- پروکسی (فیلترشکن) ----
# فرمت: socks5://user:pass@host:port یا http://host:port
TELEGRAM_PROXY = os.getenv("TELEGRAM_PROXY", "").strip() or None

# ---- تایم‌زون ----
TIMEZONE = os.getenv("TIMEZONE", "Asia/Tehran").strip()

# ---- محدودیت‌ها ----
MIN_SCAN_DELAY = 0.05
MAX_SCAN_DELAY = 5.0

# ---- سلامت ----
HEALTH_INTERVAL = _int("HEALTH_INTERVAL", 300)
PING_GREEN_MS = _int("PING_GREEN_MS", 800)
PING_YELLOW_MS = _int("PING_YELLOW_MS", 2000)


def clamp_scan_delay(value) -> float:
    """محدود کردن تاخیر اسکن در بازه مجاز"""
    try:
        value = float(value)
    except (TypeError, ValueError):
        return SCAN_DELAY
    return max(MIN_SCAN_DELAY, min(MAX_SCAN_DELAY, value))


def _tzinfo():
    try:
        from zoneinfo import ZoneInfo
        return ZoneInfo(TIMEZONE)
    except Exception:
        try:
            import pytz
            return pytz.timezone(TIMEZONE)
        except Exception:
            return None


def now_dt():
    """زمان فعلی با تایم‌زون تنظیم‌شده"""
    from datetime import datetime
    tz = _tzinfo()
    return datetime.now(tz) if tz else datetime.now()


def now_str() -> str:
    """زمان فعلی به صورت رشته"""
    return now_dt().strftime("%Y-%m-%d %H:%M:%S")


def validate() -> list:
    """بررسی تنظیمات ضروری - خروجی لیست موارد ناقص"""
    problems = []
    if not API_ID:
        problems.append("API_ID")
    if not API_HASH:
        problems.append("API_HASH")
    if not BOT_TOKEN:
        problems.append("BOT_TOKEN")
    if not OWNER_ID:
        problems.append("OWNER_ID")
    if not WORKER_SECRET:
        problems.append("WORKER_SECRET (کلید Fernet برای رمزنگاری)")
    return problems


def validate_worker() -> list:
    """بررسی تنظیمات ضروری ورکر"""
    problems = []
    if not WORKER_API_TOKEN:
        problems.append("WORKER_API_TOKEN")
    return problems


def is_encryption_enabled() -> bool:
    """آیا رمزنگاری فعال است"""
    return bool(WORKER_SECRET)
