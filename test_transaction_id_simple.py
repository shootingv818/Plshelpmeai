#!/usr/bin/env python3
"""تست تولید transactionId - بدون وابستگی"""

import random
import time


def generate_transaction_id() -> str:
    """
    تولید transactionId معتبر برای شاپرک
    فرمت: رشته عددی دقیقاً ۲۰ رقمی
    ساختار: timestamp میلی‌ثانیه‌ای (۱۳ رقم) + ارقام تصادفی (۷ رقم)
    """
    # timestamp میلی‌ثانیه‌ای (۱۳ رقم)
    timestamp_ms = str(int(time.time() * 1000))
    
    # ارقام تصادفی برای تکمیل تا ۲۰ رقم
    remaining_digits = 20 - len(timestamp_ms)
    random_part = "".join(str(random.randint(0, 9)) for _ in range(remaining_digits))
    
    return timestamp_ms + random_part


def test_transaction_id_format():
    """بررسی فرمت transactionId"""
    print("تست تولید transactionId برای شاپرک\n")
    print("=" * 60)
    
    for i in range(10):
        tid = generate_transaction_id()
        print(f"\nتست {i+1}:")
        print(f"  transactionId: {tid}")
        
        # بررسی طول
        assert len(tid) == 20, f"❌ طول باید ۲۰ باشد، نه {len(tid)}"
        
        # بررسی فقط عددی بودن
        assert tid.isdigit(), f"❌ باید فقط عدد باشد: {tid}"
        
        print(f"  ✓ طول: {len(tid)} رقم")
        print(f"  ✓ فقط عددی: بله")
        print(f"  ✓ شروع (timestamp): {tid[:13]}")
        print(f"  ✓ انتها (random): {tid[13:]}")
    
    print("\n" + "=" * 60)
    print("✅ همه تست‌ها موفق بودند!")
    print("\nنمونه transactionId معتبر برای شاپرک:")
    print(f"  {generate_transaction_id()}")


if __name__ == "__main__":
    test_transaction_id_format()
