"""
نقطه ورود اصلی پروژه
master = ربات تلگرام
worker = سرور FastAPI
"""
import asyncio
import sys

import config


def main():
    """اجرای برنامه بر اساس حالت تنظیم‌شده"""
    mode = config.MODE

    if mode == "master":
        # اجرای ربات تلگرام
        from bot.app import start_bot
        print(f"[+] شروع در حالت MASTER - {config.now_str()}")
        asyncio.run(start_bot())

    elif mode == "worker":
        # اجرای سرور API ورکر
        problems = config.validate_worker()
        if problems:
            print(f"[ERROR] تنظیمات ورکر ناقص: {', '.join(problems)}")
            sys.exit(1)
        from worker.server import run_server
        print(f"[+] شروع در حالت WORKER - {config.now_str()}")
        run_server()

    else:
        print(f"[ERROR] حالت نامعتبر: {mode}")
        print("MODE باید master یا worker باشد")
        sys.exit(1)


if __name__ == "__main__":
    main()
