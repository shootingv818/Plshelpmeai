"""ثابت‌های API آیوا - پورت شده از IvaConstants.cs"""

# آدرس‌های پایه
API_BASE_URL = "https://ivaapi.sadadpsp.ir"
API_PREFIX = "/pwa/api"
PUBLIC_KEY_URL = "https://tsm.shaparak.ir/mobileApp/getKey"
APP_VERSION = "3.10.24"


def base_address() -> str:
    return API_BASE_URL.rstrip("/") + API_PREFIX


# اندپوینت‌ها
class Endpoints:
    KEY_EXCHANGE = "/v1/users/auth/keyExchange"
    REGISTER_REQUEST = "/v1/users/auth/verifyCode"
    ACTIVATION = "/v1/users/auth/token"
    REFRESH_TOKEN = "/v1/users/auth/refreshtoken"
    USER_PROFILE = "/v1/users/me"
    APP_CONFIGURATION = "/v1/baseInfo/configs/list"
    CHARGE_CATALOG = "/v3/charges/pin/mobile/catalog"
    PAY_CHARGE = "/v1/charges/pin/payment"
    TOPUP_REQUEST = "/v1/charges/topup/payment"


# مسیرهایی که نیاز به Sign-Data ندارند
SIGN_EXCLUDE = {
    Endpoints.KEY_EXCHANGE,
    Endpoints.REGISTER_REQUEST,
    Endpoints.ACTIVATION,
    Endpoints.REFRESH_TOKEN,
}


# کلیدهای ذخیره‌سازی
class StorageKeys:
    TOKEN = "token"
    REFRESH_TOKEN = "refreshToken"
    ACCESS_TOKEN_EXP_TIME = "accessTokenExpTime"
    TOKEN_TYPE = "tokenType"
    ACCESS_TOKEN_OBTAINED_AT = "accessTokenObtainedAt"
    SHARED_KEY = "shared_key"
    WORKING_KEY = "working_key"
    RSA_PUBLIC = "rsaPublic"
    PICHAK_RSA_PUBLIC = "pichakRSAPublic"


# IV پیش‌فرض (16 بایت صفر)
DEFAULT_AES_IV = bytes(16)

# IV سفارشی
CUSTOM_AES_IV = bytes([48, 148, 136, 186, 72, 57, 83, 116, 19, 138, 210, 230, 3, 165, 240, 35])

# پین‌های رایج
COMMON_PINS = ["1234", "1111", "0000", "4321", "2222", "5555", "1212", "1122"]

# کلمات کلیدی پاسخ
VALID_KEYWORDS = ["موجودی", "اعتبار"]
RATE_LIMIT_KEYWORDS = ["محدودیت"]
BLOCKED_KEYWORDS = ["مسدود"]

# پیام‌های قابل تکرار
RETRYABLE_MESSAGES = [
    "محدودیت روزانه تراکنش",
    "عملیات ناموفق بود",
    "سرویس در حال حاضر قادر به پاسخگویی نیست",
]

DAILY_LIMIT_MESSAGES = [
    "محدودیت روزانه تراکنش",
]
