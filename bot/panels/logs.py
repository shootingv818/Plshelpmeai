"""پنل لاگ‌های پیشرفته"""
from telethon import events, Button

from bot.utils import LINE, panel_message
import config
import db


def register(bot):
    """ثبت هندلرهای لاگ"""

    @bot.on(events.CallbackQuery(data=b"panel_logs"))
    async def logs_panel(event):
        """نمایش پنل لاگ‌ها"""
        if event.sender_id != config.OWNER_ID:
            return

        text = panel_message(
            "📋 لاگ‌های سیستم",
            ["دسته‌بندی مورد نظر را انتخاب کنید:"]
        )

        buttons = [
            [Button.inline("🔍 لاگ اسکن", b"log_scan")],
            [Button.inline("👤 لاگ اکانت", b"log_account")],
            [Button.inline("🖥 لاگ ورکر", b"log_worker")],
            [Button.inline("❌ لاگ خطا", b"log_error")],
            [Button.inline("📜 همه لاگ‌ها", b"log_all")],
            [Button.inline("🗑 پاک کردن لاگ‌ها", b"log_clear")],
            [Button.inline("🔙 بازگشت", b"panel_main")],
        ]

        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"log_scan"))
    async def log_scan(event):
        """لاگ‌های اسکن"""
        if event.sender_id != config.OWNER_ID:
            return
        await _show_logs(event, category="scan", title="🔍 لاگ اسکن")

    @bot.on(events.CallbackQuery(data=b"log_account"))
    async def log_account(event):
        """لاگ‌های اکانت"""
        if event.sender_id != config.OWNER_ID:
            return
        await _show_logs(event, category="account", title="👤 لاگ اکانت")

    @bot.on(events.CallbackQuery(data=b"log_worker"))
    async def log_worker(event):
        """لاگ‌های ورکر"""
        if event.sender_id != config.OWNER_ID:
            return
        await _show_logs(event, category="worker", title="🖥 لاگ ورکر")

    @bot.on(events.CallbackQuery(data=b"log_error"))
    async def log_error(event):
        """لاگ‌های خطا"""
        if event.sender_id != config.OWNER_ID:
            return
        await _show_logs(event, level="error", title="❌ لاگ خطاها")

    @bot.on(events.CallbackQuery(data=b"log_all"))
    async def log_all(event):
        """همه لاگ‌ها"""
        if event.sender_id != config.OWNER_ID:
            return
        await _show_logs(event, title="📜 همه لاگ‌ها")

    @bot.on(events.CallbackQuery(data=b"log_clear"))
    async def log_clear(event):
        """پاک کردن لاگ‌ها"""
        if event.sender_id != config.OWNER_ID:
            return
        await db.clear_logs()
        await event.answer("لاگ‌ها پاک شدند!", alert=True)


async def _show_logs(event, category: str = None, level: str = None, title: str = "لاگ"):
    """نمایش لاگ‌ها"""
    logs = await db.list_logs(category=category, level=level, limit=20)

    rows = []
    if logs:
        for log in logs:
            level_emoji = {
                "info": "ℹ️",
                "warning": "⚠️",
                "error": "❌",
                "debug": "🔧",
            }.get(log.get("level", "info"), "📝")

            msg = log.get("message", "")[:60]
            time_str = log.get("created_at", "")
            rows.append(f"{level_emoji} [{time_str}] {msg}")
    else:
        rows.append("لاگی موجود نیست")

    text = panel_message(title, rows)
    buttons = [[Button.inline("🔙 بازگشت", b"panel_logs")]]
    await event.edit(text, buttons=buttons)
