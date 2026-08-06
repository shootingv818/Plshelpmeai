"""
سرور API ورکر - FastAPI
فقط روی loopback گوش می‌دهد - مستر از طریق تانل SSH دسترسی دارد
"""
import asyncio
import uuid

from fastapi import FastAPI, Header, HTTPException, Request
from pydantic import BaseModel

import config
import db
from worker.tasks import run_scan_task


app = FastAPI(title="IVA Scanner Worker", docs_url=None, redoc_url=None)

# وضعیت جاب‌ها در حافظه
_jobs: dict = {}


def _auth(authorization: str):
    """بررسی توکن احراز هویت"""
    expected = config.WORKER_API_TOKEN
    if not expected:
        raise HTTPException(status_code=500, detail="worker token not configured")
    if not authorization or authorization != f"Bearer {expected}":
        raise HTTPException(status_code=401, detail="unauthorized")


# --- مدل‌ها ---

class ScanStartRequest(BaseModel):
    pan: str
    account_id: int = 0
    target_mobile: str = ""
    provider_id: str = "10"
    amount: int = 10000
    delay: float = 0.15


# --- اندپوینت‌ها ---

@app.get("/ping")
async def ping():
    """بررسی زنده بودن سرویس - بدون نیاز به توکن"""
    return {"ok": True, "service": "iva-scanner-worker"}


@app.get("/health")
async def health(authorization: str = Header(None)):
    """بررسی سلامت ورکر"""
    _auth(authorization)
    return {
        "ok": True,
        "mode": config.MODE,
        "active_jobs": len([j for j in _jobs.values() if not j.get("done")]),
        "time": config.now_str(),
    }


@app.post("/scan/start")
async def scan_start(body: ScanStartRequest, authorization: str = Header(None)):
    """شروع اسکن کارت"""
    _auth(authorization)

    job_id = uuid.uuid4().hex[:12]
    job = {
        "id": job_id,
        "pan": body.pan,
        "status": "running",
        "phase": 0,
        "current_test": 0,
        "total_tests": 0,
        "found_expiry": "",
        "found_cvv": "",
        "found_pin": "",
        "done": False,
        "error": None,
        "success": False,
    }
    _jobs[job_id] = job

    # اجرای اسکن در background
    asyncio.create_task(run_scan_task(job, body))

    return {"ok": True, "job_id": job_id}


@app.get("/scan/status/{job_id}")
async def scan_status(job_id: str, authorization: str = Header(None)):
    """وضعیت جاب اسکن"""
    _auth(authorization)
    job = _jobs.get(job_id)
    if not job:
        raise HTTPException(status_code=404, detail="job not found")
    return job


@app.post("/scan/stop/{job_id}")
async def scan_stop(job_id: str, authorization: str = Header(None)):
    """توقف اسکن"""
    _auth(authorization)
    job = _jobs.get(job_id)
    if job:
        job["stopped"] = True
    return {"stopped": True}


@app.on_event("startup")
async def startup():
    """اجرا هنگام شروع"""
    await db.init()
    print(f"[+] Worker API started - {config.now_str()}")


def run_server():
    """اجرای سرور uvicorn"""
    import uvicorn
    uvicorn.run(
        app,
        host=config.WORKER_BIND_HOST,
        port=config.WORKER_API_PORT,
        log_level="info",
    )
