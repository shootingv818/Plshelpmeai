"""
ماژول دیتابیس - SQLite async با aiosqlite
جداول: iva_accounts, cards, workers, scan_jobs, logs
تمام نوشتن‌ها با asyncio.Lock محافظت می‌شوند
"""
import asyncio
import time
from typing import Optional

import aiosqlite

import config

DB_PATH = "data/iva_scanner.db"

_db: Optional[aiosqlite.Connection] = None
_write_lock: asyncio.Lock = None


async def init():
    """ایجاد جداول و اتصال به دیتابیس"""
    global _db, _write_lock
    import os
    os.makedirs("data", exist_ok=True)

    _write_lock = asyncio.Lock()
    _db = await aiosqlite.connect(DB_PATH)
    _db.row_factory = aiosqlite.Row

    await _db.executescript("""
        CREATE TABLE IF NOT EXISTS iva_accounts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            phone TEXT NOT NULL UNIQUE,
            name TEXT DEFAULT '',
            token TEXT DEFAULT '',
            refresh_token TEXT DEFAULT '',
            shared_key TEXT DEFAULT '',
            working_key TEXT DEFAULT '',
            rsa_public TEXT DEFAULT '',
            status TEXT DEFAULT 'active',
            daily_limit_hit INTEGER DEFAULT 0,
            created_at TEXT DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS cards (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            pan TEXT NOT NULL,
            expire_month TEXT DEFAULT '',
            expire_year TEXT DEFAULT '',
            cvv2 TEXT DEFAULT '',
            pin TEXT DEFAULT '',
            status TEXT DEFAULT 'pending',
            scanned_at TEXT DEFAULT '',
            tests_performed INTEGER DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS workers (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            tag TEXT NOT NULL UNIQUE,
            ip TEXT DEFAULT '',
            ssh_port INTEGER DEFAULT 22,
            ssh_user TEXT DEFAULT '',
            ssh_pass_enc TEXT DEFAULT '',
            api_port INTEGER DEFAULT 8765,
            api_token_enc TEXT DEFAULT '',
            is_master INTEGER DEFAULT 0,
            enabled INTEGER DEFAULT 1,
            status TEXT DEFAULT 'unknown',
            ping_ms INTEGER DEFAULT -1,
            created_at TEXT DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS scan_jobs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            card_pan TEXT NOT NULL,
            status TEXT DEFAULT 'pending',
            phase INTEGER DEFAULT 0,
            current_test INTEGER DEFAULT 0,
            total_tests INTEGER DEFAULT 0,
            checkpoint_index INTEGER DEFAULT 0,
            found_expiry TEXT DEFAULT '',
            found_cvv TEXT DEFAULT '',
            found_pin TEXT DEFAULT '',
            worker_id INTEGER DEFAULT NULL,
            account_id INTEGER DEFAULT NULL,
            started_at TEXT DEFAULT '',
            finished_at TEXT DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            level TEXT DEFAULT 'info',
            category TEXT DEFAULT 'general',
            message TEXT DEFAULT '',
            details TEXT DEFAULT '',
            created_at TEXT DEFAULT (datetime('now'))
        );
    """)
    await _db.commit()


async def close():
    """بستن اتصال دیتابیس"""
    global _db
    if _db:
        await _db.close()
        _db = None


async def _execute_write(query: str, params=None):
    """اجرای کوئری نوشتنی با قفل - جلوگیری از تداخل همزمان"""
    async with _write_lock:
        if params:
            await _db.execute(query, params)
        else:
            await _db.execute(query)
        await _db.commit()


async def _execute_write_returning(query: str, params=None) -> int:
    """اجرای INSERT با قفل و برگرداندن lastrowid"""
    async with _write_lock:
        async with _db.execute(query, params or []) as cursor:
            await _db.commit()
            return cursor.lastrowid


# ======== اکانت‌های آیوا ========

async def add_account(phone: str, name: str = "", **kwargs) -> int:
    """اضافه کردن اکانت"""
    cols = ["phone", "name"]
    vals = [phone, name]
    for k, v in kwargs.items():
        cols.append(k)
        vals.append(v)
    placeholders = ", ".join(["?"] * len(vals))
    col_str = ", ".join(cols)
    return await _execute_write_returning(
        f"INSERT OR REPLACE INTO iva_accounts ({col_str}) VALUES ({placeholders})", vals
    )


async def get_account(phone: str) -> Optional[dict]:
    """دریافت اکانت با شماره"""
    async with _db.execute("SELECT * FROM iva_accounts WHERE phone=?", (phone,)) as cur:
        row = await cur.fetchone()
        return dict(row) if row else None


async def get_account_by_id(account_id: int) -> Optional[dict]:
    """دریافت اکانت با آیدی"""
    async with _db.execute("SELECT * FROM iva_accounts WHERE id=?", (account_id,)) as cur:
        row = await cur.fetchone()
        return dict(row) if row else None


async def list_accounts(status: str = None) -> list:
    """لیست اکانت‌ها"""
    if status:
        async with _db.execute("SELECT * FROM iva_accounts WHERE status=?", (status,)) as cur:
            return [dict(r) for r in await cur.fetchall()]
    async with _db.execute("SELECT * FROM iva_accounts") as cur:
        return [dict(r) for r in await cur.fetchall()]


async def update_account(phone: str, **kwargs):
    """بروزرسانی اکانت"""
    if not kwargs:
        return
    sets = ", ".join(f"{k}=?" for k in kwargs.keys())
    vals = list(kwargs.values()) + [phone]
    await _execute_write(f"UPDATE iva_accounts SET {sets} WHERE phone=?", vals)


async def delete_account(phone: str):
    """حذف اکانت"""
    await _execute_write("DELETE FROM iva_accounts WHERE phone=?", (phone,))


async def get_active_accounts() -> list:
    """دریافت اکانت‌های فعال (بدون محدودیت روزانه)"""
    async with _db.execute(
        "SELECT * FROM iva_accounts WHERE status='active' AND daily_limit_hit=0"
    ) as cur:
        return [dict(r) for r in await cur.fetchall()]


async def mark_account_limited(phone: str):
    """علامت‌گذاری اکانت به عنوان محدود"""
    await update_account(phone, daily_limit_hit=1, status="limited")


# ======== کارت‌ها ========

async def add_card(pan: str, **kwargs) -> int:
    """اضافه کردن کارت"""
    cols = ["pan"]
    vals = [pan]
    for k, v in kwargs.items():
        cols.append(k)
        vals.append(v)
    placeholders = ", ".join(["?"] * len(vals))
    col_str = ", ".join(cols)
    return await _execute_write_returning(
        f"INSERT INTO cards ({col_str}) VALUES ({placeholders})", vals
    )


async def update_card(card_id: int, **kwargs):
    """بروزرسانی کارت"""
    if not kwargs:
        return
    sets = ", ".join(f"{k}=?" for k in kwargs.keys())
    vals = list(kwargs.values()) + [card_id]
    await _execute_write(f"UPDATE cards SET {sets} WHERE id=?", vals)


async def list_cards(status: str = None) -> list:
    """لیست کارت‌ها"""
    if status:
        async with _db.execute("SELECT * FROM cards WHERE status=?", (status,)) as cur:
            return [dict(r) for r in await cur.fetchall()]
    async with _db.execute("SELECT * FROM cards ORDER BY id DESC") as cur:
        return [dict(r) for r in await cur.fetchall()]


# ======== ورکرها ========

async def add_worker(tag: str, ip: str, ssh_port: int = 22, ssh_user: str = "",
                     ssh_pass_enc: str = "", api_port: int = 8765,
                     api_token_enc: str = "", is_master: int = 0) -> int:
    """اضافه کردن ورکر"""
    return await _execute_write_returning(
        """INSERT INTO workers (tag, ip, ssh_port, ssh_user, ssh_pass_enc,
           api_port, api_token_enc, is_master) VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
        (tag, ip, ssh_port, ssh_user, ssh_pass_enc, api_port, api_token_enc, is_master)
    )


async def get_worker(worker_id: int) -> Optional[dict]:
    """دریافت ورکر"""
    async with _db.execute("SELECT * FROM workers WHERE id=?", (worker_id,)) as cur:
        row = await cur.fetchone()
        return dict(row) if row else None


async def list_workers(enabled_only: bool = False) -> list:
    """لیست ورکرها"""
    if enabled_only:
        async with _db.execute("SELECT * FROM workers WHERE enabled=1") as cur:
            return [dict(r) for r in await cur.fetchall()]
    async with _db.execute("SELECT * FROM workers") as cur:
        return [dict(r) for r in await cur.fetchall()]


async def update_worker(worker_id: int, **kwargs):
    """بروزرسانی ورکر"""
    if not kwargs:
        return
    sets = ", ".join(f"{k}=?" for k in kwargs.keys())
    vals = list(kwargs.values()) + [worker_id]
    await _execute_write(f"UPDATE workers SET {sets} WHERE id=?", vals)


async def delete_worker(worker_id: int):
    """حذف ورکر"""
    await _execute_write("DELETE FROM workers WHERE id=?", (worker_id,))


async def get_master_worker() -> Optional[dict]:
    """دریافت ورکر مستر"""
    async with _db.execute("SELECT * FROM workers WHERE is_master=1 LIMIT 1") as cur:
        row = await cur.fetchone()
        return dict(row) if row else None


# ======== جاب‌های اسکن ========

async def add_scan_job(card_pan: str, **kwargs) -> int:
    """ایجاد جاب اسکن"""
    cols = ["card_pan", "started_at"]
    vals = [card_pan, config.now_str()]
    for k, v in kwargs.items():
        cols.append(k)
        vals.append(v)
    placeholders = ", ".join(["?"] * len(vals))
    col_str = ", ".join(cols)
    return await _execute_write_returning(
        f"INSERT INTO scan_jobs ({col_str}) VALUES ({placeholders})", vals
    )


async def update_scan_job(job_id: int, **kwargs):
    """بروزرسانی جاب اسکن"""
    if not kwargs:
        return
    sets = ", ".join(f"{k}=?" for k in kwargs.keys())
    vals = list(kwargs.values()) + [job_id]
    await _execute_write(f"UPDATE scan_jobs SET {sets} WHERE id=?", vals)


async def get_scan_job(job_id: int) -> Optional[dict]:
    """دریافت جاب اسکن"""
    async with _db.execute("SELECT * FROM scan_jobs WHERE id=?", (job_id,)) as cur:
        row = await cur.fetchone()
        return dict(row) if row else None


async def get_resumable_job(card_pan: str) -> Optional[dict]:
    """دریافت آخرین جاب ناتمام قابل ادامه برای یک کارت"""
    async with _db.execute(
        "SELECT * FROM scan_jobs WHERE card_pan=? AND status IN ('running','rate_limited') "
        "ORDER BY id DESC LIMIT 1",
        (card_pan,)
    ) as cur:
        row = await cur.fetchone()
        return dict(row) if row else None


async def list_scan_jobs(status: str = None) -> list:
    """لیست جاب‌های اسکن"""
    if status:
        async with _db.execute(
            "SELECT * FROM scan_jobs WHERE status=? ORDER BY id DESC", (status,)
        ) as cur:
            return [dict(r) for r in await cur.fetchall()]
    async with _db.execute("SELECT * FROM scan_jobs ORDER BY id DESC") as cur:
        return [dict(r) for r in await cur.fetchall()]


# ======== لاگ ========

async def add_log(level: str, category: str, message: str, details: str = ""):
    """افزودن لاگ"""
    await _execute_write(
        "INSERT INTO logs (level, category, message, details, created_at) VALUES (?, ?, ?, ?, ?)",
        (level, category, message, details, config.now_str())
    )


async def list_logs(category: str = None, level: str = None, limit: int = 50) -> list:
    """لیست لاگ‌ها"""
    query = "SELECT * FROM logs"
    params = []
    conditions = []
    if category:
        conditions.append("category=?")
        params.append(category)
    if level:
        conditions.append("level=?")
        params.append(level)
    if conditions:
        query += " WHERE " + " AND ".join(conditions)
    query += " ORDER BY id DESC LIMIT ?"
    params.append(limit)
    async with _db.execute(query, params) as cur:
        return [dict(r) for r in await cur.fetchall()]


async def clear_logs(category: str = None):
    """پاک کردن لاگ‌ها"""
    if category:
        await _execute_write("DELETE FROM logs WHERE category=?", (category,))
    else:
        await _execute_write("DELETE FROM logs")
