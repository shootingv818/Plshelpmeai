"""ابزارهای کمکی ربات تلگرام"""

# خط جداکننده پنل (31 خط تیره)
LINE = "-------------------------------"


def panel_message(title: str, rows: list, footer: str = "") -> str:
    """ساخت پیام پنلی با خطوط جداکننده"""
    parts = [LINE, title, LINE]
    parts.extend(r for r in rows if r)
    parts.append(LINE)
    if footer:
        parts.append(footer)
    return "\n".join(parts)


def format_card(pan: str, expire: str = "", cvv: str = "", pin: str = "") -> str:
    """فرمت‌دهی اطلاعات کارت"""
    lines = [f"💳 شماره کارت: {pan}"]
    if expire:
        lines.append(f"📅 انقضا: {expire}")
    if cvv:
        lines.append(f"🔐 CVV2: {cvv}")
    if pin:
        lines.append(f"🔑 PIN: {pin}")
    return "\n".join(lines)


def progress_bar(current: int, total: int, width: int = 20) -> str:
    """نوار پیشرفت متنی"""
    if total <= 0:
        return "[" + "." * width + "] 0%"
    ratio = min(current / total, 1.0)
    filled = int(width * ratio)
    bar = "█" * filled + "░" * (width - filled)
    percent = int(ratio * 100)
    return f"[{bar}] {percent}%"


def format_elapsed(seconds: float) -> str:
    """فرمت زمان سپری‌شده"""
    if seconds < 60:
        return f"{int(seconds)} ثانیه"
    elif seconds < 3600:
        m = int(seconds // 60)
        s = int(seconds % 60)
        return f"{m} دقیقه و {s} ثانیه"
    else:
        h = int(seconds // 3600)
        m = int((seconds % 3600) // 60)
        return f"{h} ساعت و {m} دقیقه"


def persian_number(n: int) -> str:
    """تبدیل عدد به ارقام فارسی"""
    fa_digits = "۰۱۲۳۴۵۶۷۸۹"
    return "".join(fa_digits[int(d)] if d.isdigit() else d for d in str(n))


def mask_pan(pan: str) -> str:
    """مخفی کردن بخشی از شماره کارت"""
    if len(pan) < 8:
        return pan
    return pan[:6] + "****" + pan[-4:]


def format_worker_status(worker: dict) -> str:
    """فرمت وضعیت ورکر"""
    ping = worker.get("ping_ms", -1)
    enabled = worker.get("enabled", 0)

    if not enabled:
        emoji = "⚫"
        status_text = "غیرفعال"
    elif ping is None or ping < 0:
        emoji = "🔴"
        status_text = "آفلاین"
    elif ping <= 800:
        emoji = "🟢"
        status_text = f"آنلاین ({ping}ms)"
    elif ping <= 2000:
        emoji = "🟡"
        status_text = f"کند ({ping}ms)"
    else:
        emoji = "🔴"
        status_text = f"خیلی کند ({ping}ms)"

    return f"{emoji} {worker.get('tag', '?')} | {worker.get('ip', '?')} | {status_text}"
