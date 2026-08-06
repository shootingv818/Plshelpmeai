"""
استقرار ورکر از راه دور - SSH + Docker
الگو از Makiioo worker.py
"""
import asyncio
import random
import secrets
import time

import config
import db

# مسیرهای ریموت
REMOTE_DIR = "~/iva_scanner_worker"
REMOTE_DATA = "~/iva_scanner_data"
CONTAINER = "iva-scanner-worker"
IMAGE = "iva-scanner-worker"

# تانل‌های فعال (worker_id -> dict)
_tunnels: dict = {}


def gen_tag() -> str:
    """تولید تگ یکتا برای ورکر"""
    for _ in range(200):
        tag = f"#W{random.randint(1, 9)}_{random.randint(100, 999)}"
        return tag
    return f"#W{secrets.token_hex(2)}"


async def _ssh_connect(ip: str, port: int, user: str, password: str, keepalive: bool = True):
    """اتصال SSH"""
    import asyncssh
    base = dict(
        host=ip, port=int(port or 22), username=user, password=password,
        known_hosts=None,
        login_timeout=8,
    )
    if keepalive:
        base["keepalive_interval"] = 15
        base["keepalive_count_max"] = 3

    async def _do():
        try:
            return await asyncssh.connect(connect_timeout=8, **base)
        except TypeError:
            return await asyncssh.connect(**base)

    return await asyncio.wait_for(_do(), timeout=10)


async def _run(conn, command: str):
    """اجرای دستور SSH"""
    res = await conn.run(command)
    return res.exit_status, (res.stdout or ""), (res.stderr or "")


async def provision_worker(ip: str, ssh_port: int, ssh_user: str, ssh_pass: str,
                           tag: str = None, on_progress=None) -> dict:
    """نصب ورکر روی سرور - SSH + Docker"""

    async def say(msg: str):
        if on_progress:
            try:
                if asyncio.iscoroutinefunction(on_progress):
                    await on_progress(msg)
                else:
                    on_progress(msg)
            except Exception:
                pass

    api_port = config.WORKER_API_PORT
    api_token = secrets.token_urlsafe(24)
    tag = tag or gen_tag()

    try:
        import asyncssh  # noqa
    except ImportError:
        return {"ok": False, "error": "بسته asyncssh نصب نیست (pip install asyncssh)"}

    conn = None
    try:
        await say("🔌 اتصال SSH به سرور ...")
        conn = await _ssh_connect(ip, ssh_port, ssh_user, ssh_pass, keepalive=False)

        await say("🐳 بررسی/نصب Docker ...")
        install_script = (
            "export DEBIAN_FRONTEND=noninteractive\n"
            "if ! command -v docker >/dev/null 2>&1; then\n"
            "  apt-get -o DPkg::Lock::Timeout=180 update -qq || true\n"
            "  apt-get -o DPkg::Lock::Timeout=180 install -y -qq "
            "ca-certificates curl git docker.io || true\n"
            "fi\n"
            "command -v docker >/dev/null 2>&1 || "
            "{ curl -fsSL https://get.docker.com | sh; } || true\n"
            "command -v git >/dev/null 2>&1 || "
            "apt-get -o DPkg::Lock::Timeout=180 install -y -qq git || true\n"
            "systemctl enable --now docker >/dev/null 2>&1 || true\n"
            "if command -v docker >/dev/null 2>&1; then echo DOCKER_OK; "
            "else echo DOCKER_MISSING; fi\n"
        )
        code, out, err = await _run(conn, install_script)
        if "DOCKER_OK" not in (out or ""):
            return {"ok": False,
                    "error": f"نصب Docker ناموفق: {(err or out)[-300:]}"}

        await say("📥 دریافت سورس از گیت ...")
        if not config.GIT_REPO_URL:
            return {"ok": False, "error": "GIT_REPO_URL تنظیم نشده"}
        code, out, err = await _run(
            conn,
            f"rm -rf {REMOTE_DIR} && "
            f"git clone --depth 1 -b {config.GIT_BRANCH} {config.GIT_REPO_URL} {REMOTE_DIR}",
        )
        if code != 0:
            return {"ok": False, "error": f"git clone ناموفق: {(err or out)[:200]}"}

        await say("📝 نوشتن تنظیمات ورکر (.env) ...")
        env_lines = (
            "MODE=worker\n"
            f"WORKER_API_TOKEN={api_token}\n"
            f"WORKER_API_PORT={api_port}\n"
            "WORKER_BIND_HOST=127.0.0.1\n"
            f"TIMEZONE={config.TIMEZONE}\n"
        )
        await _run(conn, f"mkdir -p {REMOTE_DATA}")
        await _run(conn, f"cat > {REMOTE_DIR}/.env <<'ENVEOF'\n{env_lines}ENVEOF")

        await say("🏗 ساخت ایمیج Docker ...")
        code, out, err = await _run(
            conn, f"cd {REMOTE_DIR} && docker build --network=host -t {IMAGE} .")
        if code != 0:
            return {"ok": False, "error": f"docker build ناموفق: {(err or out)[-400:]}"}

        await say("🚀 اجرای کانتینر ...")
        run_cmd = (
            f"docker rm -f {CONTAINER} 2>/dev/null; "
            f"docker run -d --name {CONTAINER} --restart always "
            f"--network=host "
            f"--env-file {REMOTE_DIR}/.env "
            f"-v {REMOTE_DATA}:/app/data {IMAGE}"
        )
        code, out, err = await _run(conn, run_cmd)
        if code != 0:
            return {"ok": False, "error": f"docker run ناموفق: {(err or out)[:200]}"}

        await say("✅ نصب کامل شد.")
        return {"ok": True, "tag": tag, "api_port": api_port, "api_token": api_token}

    except Exception as e:
        return {"ok": False, "error": f"{type(e).__name__}: {str(e)[:200]}"}
    finally:
        if conn is not None:
            try:
                conn.close()
            except Exception:
                pass


async def update_worker(worker: dict) -> dict:
    """آپدیت ورکر - pull + rebuild + restart"""
    try:
        from cryptography.fernet import Fernet
        f = Fernet(config.WORKER_SECRET.encode()) if config.WORKER_SECRET else None
        password = f.decrypt(worker["ssh_pass_enc"].encode()).decode() if f else worker["ssh_pass_enc"]
    except Exception:
        password = worker.get("ssh_pass_enc", "")

    try:
        conn = await _ssh_connect(
            worker["ip"], worker["ssh_port"], worker["ssh_user"],
            password, keepalive=False,
        )
    except Exception as e:
        return {"ok": False, "error": f"اتصال SSH ناموفق: {str(e)[:150]}"}

    try:
        br = config.GIT_BRANCH
        repo = config.GIT_REPO_URL
        cmd = (
            f"cd {REMOTE_DIR} && "
            f"git remote set-url origin '{repo}' && "
            f"git fetch --depth 1 origin {br} && "
            f"git checkout -B {br} FETCH_HEAD && "
            f"docker build --network=host -t {IMAGE} . && "
            f"(docker rm -f {CONTAINER} 2>/dev/null || true) && "
            f"docker run -d --name {CONTAINER} --restart always --network=host "
            f"--env-file {REMOTE_DIR}/.env -v {REMOTE_DATA}:/app/data {IMAGE}"
        )
        code, out, err = await _run(conn, cmd)
        if code != 0:
            return {"ok": False, "error": f"آپدیت ناموفق: {(err or out)[-300:]}"}
        return {"ok": True}
    except Exception as e:
        return {"ok": False, "error": f"{type(e).__name__}: {str(e)[:200]}"}
    finally:
        try:
            conn.close()
        except Exception:
            pass


async def restart_worker(worker: dict) -> dict:
    """ریستارت کانتینر ورکر"""
    try:
        from cryptography.fernet import Fernet
        f = Fernet(config.WORKER_SECRET.encode()) if config.WORKER_SECRET else None
        password = f.decrypt(worker["ssh_pass_enc"].encode()).decode() if f else worker["ssh_pass_enc"]
    except Exception:
        password = worker.get("ssh_pass_enc", "")

    try:
        conn = await _ssh_connect(
            worker["ip"], worker["ssh_port"], worker["ssh_user"],
            password, keepalive=False,
        )
        code, out, err = await _run(conn, f"docker restart {CONTAINER}")
        conn.close()
        if code == 0:
            return {"ok": True}
        return {"ok": False, "error": (err or out)[:200]}
    except Exception as e:
        return {"ok": False, "error": str(e)[:200]}


async def open_tunnel(worker: dict) -> int:
    """باز کردن تانل SSH به API ورکر"""
    import asyncssh
    wid = worker["id"]
    if wid in _tunnels:
        return _tunnels[wid]["local_port"]

    try:
        from cryptography.fernet import Fernet
        f = Fernet(config.WORKER_SECRET.encode()) if config.WORKER_SECRET else None
        password = f.decrypt(worker["ssh_pass_enc"].encode()).decode() if f else worker["ssh_pass_enc"]
    except Exception:
        password = worker.get("ssh_pass_enc", "")

    conn = await _ssh_connect(
        worker["ip"], worker["ssh_port"], worker["ssh_user"],
        password, keepalive=True,
    )
    listener = await conn.forward_local_port(
        "127.0.0.1", 0, "127.0.0.1", int(worker["api_port"]),
    )
    local_port = listener.get_port()
    _tunnels[wid] = {"conn": conn, "listener": listener, "local_port": local_port}
    return local_port


async def close_tunnel(worker_id: int):
    """بستن تانل SSH"""
    t = _tunnels.pop(worker_id, None)
    if not t:
        return
    try:
        t["listener"].close()
    except Exception:
        pass
    try:
        t["conn"].close()
    except Exception:
        pass


async def api_call(worker: dict, method: str, path: str, payload: dict = None,
                   timeout: int = 120) -> dict:
    """فراخوانی API ورکر از طریق تانل"""
    import httpx
    local_port = await open_tunnel(worker)

    try:
        from cryptography.fernet import Fernet
        f = Fernet(config.WORKER_SECRET.encode()) if config.WORKER_SECRET else None
        token = f.decrypt(worker["api_token_enc"].encode()).decode() if f else worker["api_token_enc"]
    except Exception:
        token = worker.get("api_token_enc", "")

    url = f"http://127.0.0.1:{local_port}{path}"
    headers = {"Authorization": f"Bearer {token}"}
    try:
        async with httpx.AsyncClient(timeout=timeout) as client:
            resp = await client.request(method, url, json=payload, headers=headers)
            resp.raise_for_status()
            return resp.json()
    except Exception:
        await close_tunnel(worker["id"])
        raise


async def check_worker_health(worker: dict) -> dict:
    """بررسی سلامت یک ورکر"""
    start = time.time()
    try:
        # TCP ping
        fut = asyncio.open_connection(worker["ip"], int(worker.get("ssh_port", 22)))
        reader, writer = await asyncio.wait_for(fut, timeout=5)
        writer.close()
        ping_ms = int((time.time() - start) * 1000)

        # API ping
        result = await api_call(worker, "GET", "/ping", timeout=8)
        return {"ok": True, "ping_ms": ping_ms, "api_ok": True}
    except Exception as e:
        ping_ms = int((time.time() - start) * 1000)
        return {"ok": False, "ping_ms": ping_ms, "api_ok": False, "error": str(e)[:100]}


def pick_worker(workers: list) -> dict:
    """انتخاب ورکر (round-robin ساده)"""
    enabled = [w for w in workers if w.get("enabled")]
    if not enabled:
        return None
    # اولویت با ورکر سالم
    healthy = [w for w in enabled if w.get("status") == "ok"]
    pool = healthy if healthy else enabled
    return random.choice(pool)
