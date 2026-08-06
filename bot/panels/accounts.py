"""پنل مدیریت اکانت‌های آیوا"""
import asyncio

from telethon import events, Button

from bot.utils import LINE, panel_message
import config
import db
from core.auth import IvaAuthClient


# وضعیت مکالمه (conversation state)
_conv_state = {}


def register(bot):
    """ثبت هندلرهای مدیریت اکانت"""

    @bot.on(events.CallbackQuery(data=b"panel_accounts"))
    async def accounts_panel(event):
        """نمایش پنل اکانت‌ها"""
        if event.sender_id != config.OWNER_ID:
            return

        accounts = await db.list_accounts()
        rows = []
        if accounts:
            for acc in accounts:
                status_emoji = "🟢" if acc["status"] == "active" else "🔴"
                rows.append(f"{status_emoji} {acc['phone']} | {acc.get('name', '-')}")
        else:
            rows.append("هیچ اکانتی ثبت نشده")

        rows.append(f"\nتعداد: {len(accounts)}")

        text = panel_message("👤 مدیریت اکانت‌های آیوا", rows)

        buttons = [
            [Button.inline("➕ افزودن اکانت", b"acc_add")],
            [Button.inline("🧪 تست اکانت", b"acc_test")],
            [Button.inline("❌ حذف اکانت", b"acc_remove")],
            [Button.inline("🔄 ریست محدودیت", b"acc_reset")],
            [Button.inline("🔙 بازگشت", b"panel_main")],
        ]

        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"acc_add"))
    async def add_account_start(event):
        """شروع فرایند افزودن اکانت"""
        if event.sender_id != config.OWNER_ID:
            return

        _conv_state[event.sender_id] = {"step": "phone", "action": "add_account"}

        text = panel_message(
            "➕ افزودن اکانت آیوا",
            [
                "شماره تلفن را وارد کنید:",
                "مثال: 09121234567",
            ]
        )
        await event.edit(text, buttons=[[Button.inline("🔙 انصراف", b"panel_accounts")]])

    @bot.on(events.CallbackQuery(data=b"acc_test"))
    async def test_account_start(event):
        """شروع تست اکانت"""
        if event.sender_id != config.OWNER_ID:
            return

        accounts = await db.list_accounts()
        if not accounts:
            await event.answer("هیچ اکانتی ثبت نشده!", alert=True)
            return

        buttons = []
        for acc in accounts:
            buttons.append([Button.inline(
                f"🧪 {acc['phone']}", f"acc_test_{acc['phone']}".encode()
            )])
        buttons.append([Button.inline("🔙 بازگشت", b"panel_accounts")])

        text = panel_message("🧪 تست اکانت", ["اکانت مورد نظر را انتخاب کنید:"])
        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"acc_remove"))
    async def remove_account_start(event):
        """شروع حذف اکانت"""
        if event.sender_id != config.OWNER_ID:
            return

        accounts = await db.list_accounts()
        if not accounts:
            await event.answer("هیچ اکانتی ثبت نشده!", alert=True)
            return

        buttons = []
        for acc in accounts:
            buttons.append([Button.inline(
                f"❌ {acc['phone']}", f"acc_del_{acc['phone']}".encode()
            )])
        buttons.append([Button.inline("🔙 بازگشت", b"panel_accounts")])

        text = panel_message("❌ حذف اکانت", ["اکانت مورد نظر را انتخاب کنید:"])
        await event.edit(text, buttons=buttons)

    @bot.on(events.CallbackQuery(data=b"acc_reset"))
    async def reset_limits(event):
        """ریست محدودیت روزانه همه اکانت‌ها"""
        if event.sender_id != config.OWNER_ID:
            return

        accounts = await db.list_accounts()
        for acc in accounts:
            await db.update_account(acc["phone"], daily_limit_hit=0, status="active")

        await event.answer(f"محدودیت {len(accounts)} اکانت ریست شد!", alert=True)

    @bot.on(events.CallbackQuery(pattern=b"acc_del_"))
    async def delete_account_confirm(event):
        """تایید حذف اکانت"""
        if event.sender_id != config.OWNER_ID:
            return
        phone = event.data.decode().replace("acc_del_", "")
        await db.delete_account(phone)
        await db.add_log("info", "account", f"اکانت {phone} حذف شد")
        await event.answer(f"اکانت {phone} حذف شد!", alert=True)

    @bot.on(events.CallbackQuery(pattern=b"acc_test_"))
    async def test_account_exec(event):
        """اجرای تست اکانت"""
        if event.sender_id != config.OWNER_ID:
            return
        phone = event.data.decode().replace("acc_test_", "")
        account = await db.get_account(phone)
        if not account:
            await event.answer("اکانت پیدا نشد!", alert=True)
            return

        await event.edit(panel_message("🧪 در حال تست...", [f"اکانت: {phone}"]))

        try:
            # بازسازی auth client با اطلاعات ذخیره‌شده
            key_store = {}
            if account.get("token"):
                key_store["token"] = account["token"]
            if account.get("refresh_token"):
                key_store["refreshToken"] = account["refresh_token"]
            if account.get("shared_key"):
                key_store["shared_key"] = account["shared_key"]
            if account.get("working_key"):
                key_store["working_key"] = account["working_key"]
            if account.get("rsa_public"):
                key_store["rsaPublic"] = account["rsa_public"]

            client = IvaAuthClient(key_store=key_store)
            await client.ensure_secure_channel()
            await client.close()

            await db.update_account(phone, status="active")
            await db.add_log("info", "account", f"تست اکانت {phone}: موفق")
            result_text = panel_message("🧪 نتیجه تست", [
                f"✅ اکانت {phone} سالم است",
            ])
        except Exception as e:
            await db.add_log("error", "account", f"تست اکانت {phone}: ناموفق - {str(e)[:100]}")
            result_text = panel_message("🧪 نتیجه تست", [
                f"❌ اکانت {phone} مشکل دارد",
                f"خطا: {str(e)[:100]}",
            ])

        buttons = [[Button.inline("🔙 بازگشت", b"panel_accounts")]]
        await event.edit(result_text, buttons=buttons)

    # هندلر پیام متنی برای مکالمه
    @bot.on(events.NewMessage(func=lambda e: e.sender_id == config.OWNER_ID and e.is_private))
    async def handle_conversation(event):
        """مدیریت مکالمه افزودن اکانت"""
        state = _conv_state.get(event.sender_id)
        if not state:
            return

        if state.get("action") == "add_account":
            if state["step"] == "phone":
                phone = event.text.strip()
                if not phone.startswith("09") or len(phone) != 11:
                    await event.respond("شماره نامعتبر! مثال: 09121234567")
                    raise events.StopPropagation

                state["phone"] = phone
                state["step"] = "otp_request"

                await event.respond(panel_message(
                    "➕ افزودن اکانت",
                    [f"📱 شماره: {phone}", "در حال درخواست کد..."]
                ))

                try:
                    client = IvaAuthClient()
                    await client.fetch_public_key()
                    await client.key_exchange()
                    otp_result = await client.request_otp(phone)

                    state["auth_client"] = client
                    state["otp_token"] = otp_result.get("token")
                    state["reagent_number"] = otp_result.get("reagent_number")
                    state["step"] = "verify_code"

                    await db.add_log("info", "account", f"کد OTP برای {phone} ارسال شد")
                    await event.respond(panel_message(
                        "➕ افزودن اکانت",
                        [f"✅ کد تایید به {phone} ارسال شد", "کد 6 رقمی را وارد کنید:"]
                    ))
                except Exception as e:
                    await db.add_log("error", "account", f"خطا درخواست OTP {phone}: {str(e)[:100]}")
                    await event.respond(f"❌ خطا: {str(e)[:150]}")
                    _conv_state.pop(event.sender_id, None)

                raise events.StopPropagation

            elif state["step"] == "verify_code":
                code = event.text.strip()
                if not code.isdigit() or len(code) < 4:
                    await event.respond("کد نامعتبر! کد عددی وارد کنید.")
                    raise events.StopPropagation

                phone = state["phone"]
                client = state.get("auth_client")
                if not client:
                    await event.respond("❌ خطا: لطفا دوباره شروع کنید")
                    _conv_state.pop(event.sender_id, None)
                    raise events.StopPropagation

                try:
                    result = await client.verify_code(
                        code, state.get("otp_token"), state.get("reagent_number")
                    )

                    # رمزنگاری توکن‌ها قبل از ذخیره
                    token_val = client.key_store.get("token", "")
                    refresh_val = client.key_store.get("refreshToken", "")
                    shared_val = client.key_store.get("shared_key", "")
                    working_val = client.key_store.get("working_key", "")
                    rsa_val = client.key_store.get("rsaPublic", "")

                    if config.is_encryption_enabled():
                        from cryptography.fernet import Fernet
                        fernet = Fernet(config.WORKER_SECRET.encode())
                        if token_val:
                            token_val = fernet.encrypt(token_val.encode()).decode()
                        if refresh_val:
                            refresh_val = fernet.encrypt(refresh_val.encode()).decode()
                        if shared_val:
                            shared_val = fernet.encrypt(shared_val.encode()).decode()
                        if working_val:
                            working_val = fernet.encrypt(working_val.encode()).decode()

                    # ذخیره اطلاعات در دیتابیس
                    await db.add_account(
                        phone=phone,
                        name=phone,
                        token=token_val,
                        refresh_token=refresh_val,
                        shared_key=shared_val,
                        working_key=working_val,
                        rsa_public=rsa_val,
                        status="active",
                    )

                    await client.close()
                    await db.add_log("info", "account", f"اکانت {phone} با موفقیت اضافه شد")

                    await event.respond(panel_message(
                        "✅ اکانت اضافه شد",
                        [f"📱 شماره: {phone}", "وضعیت: فعال"],
                        f"🕒 {config.now_str()}"
                    ))
                except Exception as e:
                    await db.add_log("error", "account", f"خطا تایید {phone}: {str(e)[:100]}")
                    await event.respond(f"❌ خطا در تایید: {str(e)[:150]}")
                finally:
                    _conv_state.pop(event.sender_id, None)

                raise events.StopPropagation
