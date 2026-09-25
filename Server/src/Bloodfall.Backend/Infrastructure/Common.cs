using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Bloodfall.Backend.Infrastructure
{
    public static class JsonConfig
    {
        /// <summary>Wire format shared with the Unity client's JsonMapper: camelCase fields, string enums.</summary>
        public static void Apply(JsonSerializerOptions o)
        {
            o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.DictionaryKeyPolicy = null;
            o.IncludeFields = true;
            o.PropertyNameCaseInsensitive = true;
            o.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            o.Converters.Add(new JsonStringEnumConverter());
        }

        public static readonly JsonSerializerOptions Options = Create();

        private static JsonSerializerOptions Create()
        {
            var o = new JsonSerializerOptions();
            Apply(o);
            return o;
        }
    }

    public static class Api
    {
        public static IResult Error(int status, string code, string message, Dictionary<string, string> fields = null) =>
            Results.Json(new ApiError { Code = code, Message = message, Fields = fields }, JsonConfig.Options, statusCode: status);

        public static IResult BadRequest(string code, string message, string field = null) =>
            Error(StatusCodes.Status400BadRequest, code, message, field == null ? null : new Dictionary<string, string> { [field] = message });

        public static IResult NotFound(string message) => Error(StatusCodes.Status404NotFound, "not_found", message);
        public static IResult Forbidden(string message) => Error(StatusCodes.Status403Forbidden, "forbidden", message);
        public static IResult Conflict(string code, string message, string field = null) =>
            Error(StatusCodes.Status409Conflict, code, message, field == null ? null : new Dictionary<string, string> { [field] = message });
        public static IResult Ok<T>(T value) => Results.Json(value, JsonConfig.Options);
    }

    /// <summary>Endpoint filter: only dedicated game servers holding the configured server key may call.</summary>
    public sealed class GameServerKeyFilter : IEndpointFilter
    {
        public const string Header = "X-Bloodfall-Server-Key";
        private readonly byte[] _key;

        public GameServerKeyFilter(IOptions<BackendOptions> options) { _key = Encoding.UTF8.GetBytes(options.Value.GameServerKey ?? ""); }

        public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var provided = context.HttpContext.Request.Headers[Header].ToString();
            var bytes = Encoding.UTF8.GetBytes(provided ?? "");
            if (_key.Length == 0 || bytes.Length != _key.Length || !CryptographicOperations.FixedTimeEquals(bytes, _key))
                return Api.Error(StatusCodes.Status401Unauthorized, "server_unauthorized", "Invalid game server key.");
            return await next(context);
        }
    }

    public static class Ranks
    {
        public static readonly (string name, int min)[] Tiers =
        {
            ("Initiate", 0), ("Ironbound", 1100), ("Bloodforged", 1300), ("Warlord", 1500), ("Dread Knight", 1700),
            ("Crimson Lord", 1900), ("Immortal", 2150), ("Abyssal Sovereign", 2450),
        };

        public static (string tier, int division, float progress) For(int rating)
        {
            int idx = 0;
            for (int i = 0; i < Tiers.Length; i++) if (rating >= Tiers[i].min) idx = i;
            if (idx == Tiers.Length - 1) return (Tiers[idx].name, 1, 1f);
            int lo = Tiers[idx].min, hi = Tiers[idx + 1].min;
            float t = Math.Clamp((rating - lo) / (float)(hi - lo), 0f, 0.9999f);
            int division = 3 - (int)(t * 3); // III -> II -> I
            float progress = t * 3 - (int)(t * 3);
            return (Tiers[idx].name, division, progress);
        }

        public static int XpForLevel(int level) => 500 + 150 * (level - 1);
    }
}
