using System;
using System.Collections.Generic;

namespace IvaScanner.Core
{
    public static class IvaConstants
    {
        public const string ApiBaseUrl = "https://ivaapi.sadadpsp.ir";
        public const string ApiPrefix = "/pwa/api";
        public const string PublicKeyUrl = "https://tsm.shaparak.ir/mobileApp/getKey";
        public const string AppVersion = "3.10.24";

        public static class Endpoints
        {
            public const string KeyExchange = "/v1/users/auth/keyExchange";
            public const string RegisterRequest = "/v1/users/auth/verifyCode";
            public const string Activation = "/v1/users/auth/token";
            public const string RefreshToken = "/v1/users/auth/refreshtoken";
            public const string UserProfile = "/v1/users/me";
            public const string AppConfiguration = "/v1/baseInfo/configs/list";
            public const string ChargeCatalog = "/v3/charges/pin/mobile/catalog";
            public const string PayCharge = "/v1/charges/pin/payment";
            public const string TopupRequest = "/v1/charges/topup/payment";
        }

        public static readonly IReadOnlySet<string> SignExclude = new HashSet<string>
        {
            Endpoints.KeyExchange,
            Endpoints.RegisterRequest,
            Endpoints.Activation,
            Endpoints.RefreshToken,
        };

        public static class StorageKeys
        {
            public const string Token = "token";
            public const string RefreshToken = "refreshToken";
            public const string AccessTokenExpTime = "accessTokenExpTime";
            public const string TokenType = "tokenType";
            public const string AccessTokenObtainedAt = "accessTokenObtainedAt";
            public const string SharedKey = "shared_key";
            public const string WorkingKey = "working_key";
            public const string RsaPublic = "rsaPublic";
            public const string PichakRsaPublic = "pichakRSAPublic";
        }

        public static readonly byte[] DefaultAesIv = new byte[16];
        public static readonly byte[] CustomAesIv = new byte[]
        {
            48, 148, 136, 186, 72, 57, 83, 116, 19, 138, 210, 230, 3, 165, 240, 35
        };
    }
}