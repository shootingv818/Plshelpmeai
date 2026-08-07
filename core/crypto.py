"""
ماژول رمزنگاری آیوا - پورت شده از IvaCrypto.cs
AES-256-CBC, HMAC-SHA256, RSA PKCS1v15
"""
import base64
import hashlib
import hmac
import os

from cryptography.hazmat.primitives import hashes, padding, serialization
from cryptography.hazmat.primitives.asymmetric import rsa as rsa_module, padding as asym_padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

from core.constants import DEFAULT_AES_IV, CUSTOM_AES_IV, StorageKeys


class IvaCrypto:
    """کلاس رمزنگاری آیوا"""

    def __init__(self, key_store: dict = None):
        self.key_store = key_store if key_store is not None else {}

    def generate_key(self, n: int = 32) -> bytes:
        """تولید کلید تصادفی"""
        return os.urandom(n)

    def _resolve_shared_key(self, key_base64: str = None) -> bytes:
        """دریافت کلید مشترک از store یا پارامتر"""
        b64 = key_base64 or self.key_store.get(StorageKeys.SHARED_KEY)
        if not b64:
            raise ValueError("کلید مشترک تنظیم نشده. ابتدا KeyExchange را اجرا کنید.")
        return base64.b64decode(b64)

    def _aes_encrypt_with_iv(self, plaintext: str, key_base64: str = None, iv: bytes = None) -> str:
        """رمزنگاری AES-256-CBC با IV مشخص"""
        key = self._resolve_shared_key(key_base64)
        if iv is None:
            iv = DEFAULT_AES_IV

        # PKCS7 padding
        padder = padding.PKCS7(128).padder()
        data = padder.update(plaintext.encode("utf-8")) + padder.finalize()

        cipher = Cipher(algorithms.AES(key), modes.CBC(iv))
        encryptor = cipher.encryptor()
        ciphertext = encryptor.update(data) + encryptor.finalize()

        return ciphertext.hex()

    def aes_encrypt(self, plaintext: str, key_base64: str = None) -> str:
        """رمزنگاری با IV پیش‌فرض (صفر)"""
        return self._aes_encrypt_with_iv(plaintext, key_base64, DEFAULT_AES_IV)

    def aes_encrypt2(self, plaintext: str, key_base64: str = None, iv: bytes = None) -> str:
        """رمزنگاری با IV سفارشی"""
        return self._aes_encrypt_with_iv(plaintext, key_base64, iv or CUSTOM_AES_IV)

    def aes_decrypt(self, hex_str: str, key_base64: str = None) -> str:
        """رمزگشایی AES-256-CBC"""
        key = self._resolve_shared_key(key_base64)
        data = bytes.fromhex(hex_str)

        cipher = Cipher(algorithms.AES(key), modes.CBC(DEFAULT_AES_IV))
        decryptor = cipher.decryptor()
        padded = decryptor.update(data) + decryptor.finalize()

        # Remove PKCS7 padding
        unpadder = padding.PKCS7(128).unpadder()
        plaintext = unpadder.update(padded) + unpadder.finalize()

        return plaintext.decode("utf-8")

    def hmac_sign(self, data: str) -> str:
        """امضای HMAC-SHA256 با working_key"""
        key_b64 = self.key_store.get(StorageKeys.WORKING_KEY)
        if not key_b64:
            raise ValueError("Working key تنظیم نشده. ابتدا KeyExchange را اجرا کنید.")
        key = base64.b64decode(key_b64)
        signature = hmac.new(key, data.encode("utf-8"), hashlib.sha256).digest()
        return base64.b64encode(signature).decode("utf-8")

    def rsa_encrypt(self, plaintext: str) -> str:
        """رمزنگاری RSA PKCS1v15"""
        key_str = self.key_store.get(StorageKeys.RSA_PUBLIC)
        if not key_str:
            raise ValueError("کلید عمومی RSA تنظیم نشده.")
        public_key = import_public_key(key_str)
        ciphertext = public_key.encrypt(
            plaintext.encode("utf-8"),
            asym_padding.PKCS1v15()
        )
        return ciphertext.hex()


def import_public_key(key_str: str):
    """وارد کردن کلید عمومی RSA از فرمت‌های مختلف"""
    trimmed = key_str.strip()

    # فرمت PEM
    if "BEGIN" in trimmed:
        return serialization.load_pem_public_key(trimmed.encode("utf-8"))

    # حذف فاصله و خط جدید
    compact = trimmed.replace("\r", "").replace("\n", "").replace(" ", "")
    raw_bytes = base64.b64decode(compact)

    # تلاش برای بارگذاری به عنوان SubjectPublicKeyInfo (DER)
    try:
        return serialization.load_der_public_key(raw_bytes)
    except Exception:
        pass

    # اگر فقط مدولوس باشد، کلید عمومی می‌سازیم
    return _build_rsa_from_modulus(raw_bytes)


def _build_rsa_from_modulus(modulus_bytes: bytes):
    """ساخت کلید RSA از مدولوس خام (اکسپوننت = 65537)"""
    from cryptography.hazmat.primitives.asymmetric.rsa import RSAPublicNumbers
    modulus_int = int.from_bytes(modulus_bytes, byteorder="big")
    public_numbers = RSAPublicNumbers(e=65537, n=modulus_int)
    return public_numbers.public_key()


def base64_to_hex(b64: str) -> str:
    """تبدیل Base64 به hex"""
    return base64.b64decode(b64).hex()


def hex_to_base64(hex_str: str) -> str:
    """تبدیل hex به Base64"""
    return base64.b64encode(bytes.fromhex(hex_str)).decode("utf-8")


def base64_modulus_to_pem(base64_modulus: str) -> str:
    """
    ساخت SubjectPublicKeyInfo PEM از مدولوس Base64 خام.

    مشکل نسخه قبلی: طول‌های DER کاملاً hardcode بودند (فقط RSA-2048).
    اگر سرور مدولوسی با طول متفاوت برمی‌گرداند، DER ساخته‌شده اشتباه بود
    و cryptography library خطای DataFormatMismatch می‌داد.

    این نسخه برای هر اندازه مدولوسی (1024، 2048، 4096 بیت و ...) کار می‌کند.
    """
    # --- رمزگشایی مدولوس ---
    raw = base64.b64decode(base64_modulus)

    # DER INTEGER باید unsigned باشد؛ اگر بیت MSB یک باشد leading 0x00 اضافه می‌شود
    if raw[0] >= 0x80:
        raw = b"\x00" + raw

    def _encode_len(n: int) -> bytes:
        """کدگذاری طول DER (کوتاه یا بلند)"""
        if n < 0x80:
            return bytes([n])
        elif n < 0x100:
            return bytes([0x81, n])
        else:
            return bytes([0x82, (n >> 8) & 0xFF, n & 0xFF])

    # --- INTEGER: مدولوس ---
    mod_der = b"\x02" + _encode_len(len(raw)) + raw

    # --- INTEGER: اکسپوننت ثابت 65537 = 0x010001 ---
    exp_der = b"\x02\x03\x01\x00\x01"

    # --- SEQUENCE: RSAPublicKey ---
    rsa_inner = mod_der + exp_der
    rsa_seq = b"\x30" + _encode_len(len(rsa_inner)) + rsa_inner

    # --- BIT STRING: padding=0x00 + RSAPublicKey ---
    bit_str_body = b"\x00" + rsa_seq
    bit_str = b"\x03" + _encode_len(len(bit_str_body)) + bit_str_body

    # --- SEQUENCE: AlgorithmIdentifier (rsaEncryption OID + NULL) ---
    alg_id = bytes.fromhex("300D06092A864886F70D0101010500")

    # --- SEQUENCE: SubjectPublicKeyInfo ---
    spki_body = alg_id + bit_str
    spki = b"\x30" + _encode_len(len(spki_body)) + spki_body

    # --- تبدیل به PEM ---
    b64 = base64.b64encode(spki).decode("ascii")
    lines = [b64[i:i + 64] for i in range(0, len(b64), 64)]
    pem_body = "\n".join(lines)
    return f"-----BEGIN PUBLIC KEY-----\n{pem_body}\n-----END PUBLIC KEY-----"
