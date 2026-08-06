"""پنل مدیریت ورکرها"""
import asyncio

from telethon import events, Button

from bot.utils import LINE, panel_message, format_worker_status
import config
import db


# وضعیت مکالمه
_conv_state = {}


def register(bot):
    """ثبت هندلرهای مدیریت ورکر"""

    @bot.on(events.CallbackQuery(data=b"panel_workers"))
    async def workers_panel(event):
        """نمایش پنل ورکرها"""
        if event.sender_id != config.OWNER_ID:
            return

        workers = await db.list_workers()
        rows = []
        if workers:
            for w in workers:
                rows.append(format_worker_status(w))
        else:
            rows.append("هیچ ورکری ثبت نشده")

        rows.append(f"\nتعداد: {len(workers)}")

        text = panel_message("🖥 مدیریت ورکرها", rows)

        buttons = [
            [Button.inline("➕ افزودن ورکر", b"wk_add")],
            [Button.inline("🏥 بررسی سلامت", b"wk_health")],
            [Button.inline("🔄 آپدیت ورکر", b"wk_update")],
            [Button.inline("❌ حذف ورکر", b"wk_remove")],
            [Button.inline("🔙 بازگشت", b"panel_main")],
        ]

        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"wk_add"))
    async def add_worker_start(event):
        """شروع فرایند افزودن ورکر"""
        if event.sender_id != config.OWNER_ID:
            return

        _conv_state[event.sender_id] = {"step": "ip", "action": "add_worker"}

        text = panel_message(
            "➕ افزودن ورکر",
            [
                "اطلاعات SSH سرور را وارد کنید:",
                "فرمت: ip:port:user:password",
                "مثال: 1.2.3.4:22:root:mypass123",
            ]
        )
        await event.edit(text, buttons=[[Button.inline("🔙 انصراف", b"panel_workers")]])

    @bot.on(events.CallbackQuery(data=b"wk_health"))
    async def health_check(event):
        """بررسی سلامت همه ورکرها"""
        if event.sender_id != config.OWNER_ID:
            return

        workers = await db.list_workers()
        if not workers:
            await event.answer("ورکری ثبت نشده!", alert=True)
            return

        await event.edit(panel_message("🏥 بررسی سلامت...", ["لطفا صبر کنید..."]))

        rows = []
        for w in workers:
            rows.append(format_worker_status(w))

        text = panel_message("🏥 وضعیت سلامت ورکرها", rows, f"🕒 {config.now_str()}")
        buttons = [[Button.inline("🔙 بازگشت", b"panel_workers")]]
        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"wk_remove"))
    async def remove_worker_start(event):
        """شروع حذف ورکر"""
        if event.sender_id != config.OWNER_ID:
            return

        workers = await db.list_workers()
        if not workers:
            await event.answer("ورکری ثبت نشده!", alert=True)
            return

        buttons = []
        for w in workers:
            buttons.append([Button.inline(
                f"❌ {w['tag']} ({w['ip']})", f"wk_del_{w['id']}".encode()
            )])
        buttons.append([Button.inline("🔙 بازگشت", b"panel_workers")])

        text = panel_message("❌ حذف ورکر", ["ورکر مورد نظر را انتخاب کنید:"])
        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"wk_update"))
    async def update_worker_start(event):
        """آپدیت ورکر"""
        if event.sender_id != config.OWNER_ID:
            return

        workers = await db.list_workers()
        if not workers:
            await event.answer("ورکری ثبت نشده!", alert=True)
            return

        buttons = []
        for w in workers:
            buttons.append([Button.inline(
                f"🔄 {w['tag']} ({w['ip']})", f"wk_upd_{w['id']}".encode()
            )])
        buttons.append([Button.inline("🔙 بازگشت", b"panel_workers")])

        text = panel_message("🔄 آپدیت ورکر", ["ورکر مورد نظر را انتخاب کنید:"])
        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(pattern=b"wk_del_"))
    async def delete_worker_confirm(event):
        """تایید حذف ورکر"""
        if event.sender_id != config.OWNER_ID:
            return
        worker_id = int(event.data.decode().replace("wk_del_", ""))
        worker = await db.get_worker(worker_id)
        if worker:
            await db.delete_worker(worker_id)
            await db.add_log("info", "worker", f"ورکر {worker['tag']} حذف شد")
            await event.answer(f"ورکر {worker['tag']} حذف شد!", alert=True)
        else:
            await event.answer("ورکر پیدا نشد!", alert=True)

    @bot.on(events.CallbackQuery(pattern=b"wk_upd_"))
    async def update_worker_exec(event):
        """اجرای آپدیت ورکر"""
        if event.sender_id != config.OWNER_ID:
            return
        worker_id = int(event.data.decode().replace("wk_upd_", ""))
        worker = await db.get_worker(worker_id)
        if not worker:
            await event.answer("ورکر پیدا نشد!", alert=True)
            return

        await event.edit(panel_message(
            "🔄 آپدیت ورکر",
            [f"در حال آپدیت {worker['tag']}..."]
        ))

        try:
            from worker.deploy import update_worker as do_update
            result = await do_update(worker)
            if result.get("ok"):
                await db.add_log("info", "worker", f"ورکر {worker['tag']} آپدیت شد")
                await event.edit(panel_message(
                    "✅ آپدیت موفق",
                    [f"ورکر {worker['tag']} آپدیت شد"]
                ), buttons=[[Button.inline("🔙 بازگشت", b"panel_workers")]])
            else:
                await event.edit(panel_message(
                    "❌ آپدیت ناموفق",
                    [f"خطا: {result.get('error', 'نامشخص')[:200]}"]
                ), buttons=[[Button.inline("🔙 بازگشت", b"panel_workers")]])
        except Exception as e:
            await event.edit(panel_message(
                "❌ خطا",
                [f"{str(e)[:200]}"]
            ), buttons=[[Button.inline("🔙 بازگشت", b"panel_workers")]])


async def handle_worker_message(bot, event):
    """هندلر پیام ورودی برای افزودن ورکر"""
    state = _conv_state.get(event.sender_id)
    if not state or state.get("action") != "add_worker":
        return False

    if state["step"] == "ip":
        parts = event.text.strip().split(":")
        if len(parts) < 4:
            await event.respond("فرمت نادرست! مثال: 1.2.3.4:22:root:mypass123")
            return True

        ip = parts[0]
        port = int(parts[1]) if parts[1].isdigit() else 22
        user = parts[2]
        password = ":".join(parts[3:])

        state["ip"] = ip
        state["port"] = port
        state["user"] = user
        state["password"] = password

        await event.respond(panel_message(
            "➕ افزودن ورکر",
            [f"🔌 سرور: {ip}:{port}", f"👤 کاربر: {user}", "در حال نصب..."]
        ))

        try:
            from worker.deploy import provision_worker, gen_tag

            tag = gen_tag()

            async def on_progress(msg):
                try:
                    await event.respond(msg)
                except Exception:
                    pass

            result = await provision_worker(
                ip=ip, ssh_port=port, ssh_user=user, ssh_pass=password,
                tag=tag, on_progress=on_progress,
            )

            if result.get("ok"):
                # ذخیره ورکر
                from cryptography.fernet import Fernet
                f = Fernet(config.WORKER_SECRET.encode()) if config.WORKER_SECRET else None
                pass_enc = f.encrypt(password.encode()).decode() if f else password
                token_enc = f.encrypt(result["api_token"].encode()).decode() if f else result["api_token"]

                await db.add_worker(
                    tag=tag, ip=ip, ssh_port=port, ssh_user=user,
                    ssh_pass_enc=pass_enc, api_port=result["api_port"],
                    api_token_enc=token_enc,
                )
                await db.add_log("info", "worker", f"ورکر {tag} ({ip}) اضافه شد")
                await event.respond(panel_message(
                    "✅ ورکر اضافه شد",
                    [f"🏷 تگ: {tag}", f"🌐 آدرس: {ip}:{port}"]
                ))
            else:
                await db.add_log("error", "worker", f"نصب ورکر {ip} ناموفق: {result.get('error', '')[:100]}")
                await event.respond(f"❌ خطا: {result.get('error', 'نامشخص')[:300]}")

        except Exception as e:
            await db.add_log("error", "worker", f"خطا نصب ورکر: {str(e)[:100]}")
            await event.respond(f"❌ خطا: {str(e)[:200]}")

        finally:
            _conv_state.pop(event.sender_id, None)

        return True

    return False
