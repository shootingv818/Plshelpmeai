"""پنل منوی اصلی ربات"""
from telethon import events, Button

from bot.utils import LINE, panel_message
import config


def register(bot):
    """ثبت هندلرهای منوی اصلی"""

    @bot.on(events.NewMessage(pattern="/start"))
    async def start_handler(event):
        """هندلر دستور /start"""
        if event.sender_id != config.OWNER_ID:
            return

        text = panel_message(
            "🏠 منوی اصلی - اسکنر آیوا",
            [
                "به ربات اسکنر هوشمند آیوا خوش آمدید",
                "اسکن 3 فازی: انقضا ← CVV ← PIN",
                f"حداکثر ~10,000 تست (به جای 4,752,000)",
            ],
            f"🕒 {config.now_str()}"
        )

        buttons = [
            [Button.inline("🔍 اسکن کارت", b"panel_scan")],
            [Button.inline("👤 مدیریت اکانت‌ها", b"panel_accounts")],
            [Button.inline("🖥 مدیریت ورکرها", b"panel_workers")],
            [Button.inline("📋 لاگ‌ها", b"panel_logs")],
        ]

        await event.respond(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"panel_main"))
    async def main_menu_callback(event):
        """برگشت به منوی اصلی"""
        if event.sender_id != config.OWNER_ID:
            return

        text = panel_message(
            "🏠 منوی اصلی - اسکنر آیوا",
            [
                "به ربات اسکنر هوشمند آیوا خوش آمدید",
                "اسکن 3 فازی: انقضا ← CVV ← PIN",
                f"حداکثر ~10,000 تست (به جای 4,752,000)",
            ],
            f"🕒 {config.now_str()}"
        )

        buttons = [
            [Button.inline("🔍 اسکن کارت", b"panel_scan")],
            [Button.inline("👤 مدیریت اکانت‌ها", b"panel_accounts")],
            [Button.inline("🖥 مدیریت ورکرها", b"panel_workers")],
            [Button.inline("📋 لاگ‌ها", b"panel_logs")],
        ]

        await event.edit(text, buttons=buttons)
