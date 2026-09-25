using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Bloodfall.Contracts;
using Bloodfall.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Bloodfall.Client.Backend
{
    /// <summary>Result of an API call: either a value or a user-presentable error.</summary>
    public sealed class ApiResult<T>
    {
        public T Value;
        public ApiError Error;
        public int Status;
        public bool Ok => Error == null;
        public string Message => Error?.Message;
        public static ApiResult<T> Fail(int status, string code, string message) => new ApiResult<T> { Status = status, Error = new ApiError { Code = code, Message = message } };
    }

    /// <summary>
    /// REST client for the Bloodfall services (UnityWebRequest + JsonMapper). Attaches the access token, refreshes it
    /// once on 401, and converts transport failures into readable messages ("Unable to reach authentication server").
    /// Must be used from the main thread.
    /// </summary>
    public sealed class ApiClient
    {
        private readonly string _base;
        private readonly SessionStore _session;
        private Task<bool> _refreshing;
        public int TimeoutSeconds = 10;
        public event Action SessionExpired;

        public ApiClient(string baseUrl, SessionStore session) { _base = baseUrl.TrimEnd('/'); _session = session; }

        public Task<ApiResult<T>> Get<T>(string path, bool auth = true) => Send<T>("GET", path, null, auth);
        public Task<ApiResult<T>> Post<T>(string path, object body, bool auth = true) => Send<T>("POST", path, body ?? new object(), auth);
        public Task<ApiResult<T>> Patch<T>(string path, object body) => Send<T>("PATCH", path, body, true);
        public Task<ApiResult<T>> Delete<T>(string path) => Send<T>("DELETE", path, null, true);

        private async Task<ApiResult<T>> Send<T>(string method, string path, object body, bool auth, bool retried = false)
        {
            using (var req = new UnityWebRequest(_base + "/" + path.TrimStart('/'), method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    var json = body is string s ? s : JsonMapper.ToJson(body);
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.SetRequestHeader("Accept", "application/json");
                req.SetRequestHeader("X-Bloodfall-Client", Core.GameApp.Instance != null ? Core.GameApp.Instance.Config.ClientVersion : "unknown");
                if (auth && !string.IsNullOrEmpty(_session.AccessToken)) req.SetRequestHeader("Authorization", "Bearer " + _session.AccessToken);
                req.timeout = TimeoutSeconds;
                await req.SendWebRequest().AsTask();

                if (req.result == UnityWebRequest.Result.ConnectionError)
                    return ApiResult<T>.Fail(0, "unreachable", ServiceName(path) + " is unreachable. Check your connection or try again shortly.");
                long code = req.responseCode;
                string text = req.downloadHandler?.text ?? "";
                if (code == 401 && auth && !retried && !string.IsNullOrEmpty(_session.RefreshToken))
                {
                    if (await RefreshAsync()) return await Send<T>(method, path, body, auth, true);
                    SessionExpired?.Invoke();
                    return ApiResult<T>.Fail(401, "session_expired", "Your session has expired. Please log in again.");
                }
                if (code >= 200 && code < 300)
                {
                    if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(text) || text == "null") return new ApiResult<T> { Status = (int)code };
                    try { return new ApiResult<T> { Value = JsonMapper.FromJson<T>(text), Status = (int)code }; }
                    catch (Exception e) { return ApiResult<T>.Fail((int)code, "bad_response", "Unexpected response from " + ServiceName(path) + ": " + e.Message); }
                }
                ApiError err = null;
                try { if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{")) err = JsonMapper.FromJson<ApiError>(text); } catch { }
                if (err == null || string.IsNullOrEmpty(err.Message))
                    err = new ApiError { Code = "http_" + code, Message = code == 429 ? "Too many requests. Please wait a moment." : code >= 500 ? ServiceName(path) + " reported an error (" + code + ")." : "Request failed (" + code + ")." };
                return new ApiResult<T> { Error = err, Status = (int)code };
            }
        }

        public Task<bool> RefreshAsync()
        {
            if (_refreshing != null && !_refreshing.IsCompleted) return _refreshing;
            _refreshing = DoRefresh();
            return _refreshing;
        }

        private async Task<bool> DoRefresh()
        {
            var r = await Send<AuthResponse>("POST", "api/auth/refresh", new RefreshRequest { RefreshToken = _session.RefreshToken }, false, true);
            if (!r.Ok) { _session.Clear(); return false; }
            _session.Apply(r.Value, _session.RememberMe);
            return true;
        }

        private static string ServiceName(string path)
        {
            if (path.Contains("auth")) return "The authentication server";
            if (path.Contains("lobbies")) return "The lobby service";
            if (path.Contains("matchmaking")) return "The matchmaking service";
            if (path.Contains("social") || path.Contains("clans")) return "The social service";
            if (path.Contains("stats")) return "The statistics service";
            if (path.Contains("directory")) return "The server directory";
            return "Bloodfall Services";
        }
    }

    public static class UnityWebRequestExtensions
    {
        public static Task AsTask(this UnityWebRequestAsyncOperation op)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (op.isDone) tcs.SetResult(true);
            else op.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }
    }

    /// <summary>Access/refresh tokens. The refresh token is kept only when "Remember me" is ticked.</summary>
    public sealed class SessionStore
    {
        private const string Key = "bloodfall.session";
        public string AccessToken { get; private set; }
        public string RefreshToken { get; private set; }
        public bool RememberMe { get; private set; }
        public DateTime AccessExpires { get; private set; }
        public AccountSummary Account { get; set; }
        public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken) && Account != null;

        public void Apply(AuthResponse r, bool remember)
        {
            AccessToken = r.AccessToken;
            RefreshToken = r.RefreshToken;
            AccessExpires = DateTime.UtcNow.AddSeconds(Math.Max(30, r.ExpiresIn - 30));
            if (r.Account != null) Account = r.Account;
            RememberMe = remember;
            if (remember) SecureStore.Save(Key, RefreshToken);
            else SecureStore.Delete(Key);
        }

        public bool LoadRemembered()
        {
            var t = SecureStore.Load(Key);
            if (string.IsNullOrEmpty(t)) return false;
            RefreshToken = t;
            RememberMe = true;
            return true;
        }

        public void Clear()
        {
            AccessToken = null;
            RefreshToken = null;
            Account = null;
            SecureStore.Delete(Key);
        }
    }

    /// <summary>
    /// Stores small secrets for "remember me". Obfuscated with a per-install key in PlayerPrefs; not equivalent to an
    /// OS credential vault (planned: Windows DPAPI / Credential Manager - see TODO.md). The refresh token is revocable
    /// server-side and rotates on every use, which limits the damage of a stolen token.
    /// </summary>
    public static class SecureStore
    {
        private static byte[] InstallKey()
        {
            var k = PlayerPrefs.GetString("bloodfall.ik", "");
            if (string.IsNullOrEmpty(k))
            {
                var b = new byte[32];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(b);
                k = Convert.ToBase64String(b);
                PlayerPrefs.SetString("bloodfall.ik", k);
            }
            var device = Encoding.UTF8.GetBytes(SystemInfo.deviceUniqueIdentifier ?? "device");
            var key = Convert.FromBase64String(k);
            for (int i = 0; i < key.Length; i++) key[i] ^= device[i % device.Length];
            return key;
        }

        public static void Save(string name, string value)
        {
            if (value == null) { Delete(name); return; }
            var data = Encoding.UTF8.GetBytes(value);
            var key = InstallKey();
            for (int i = 0; i < data.Length; i++) data[i] ^= key[i % key.Length];
            PlayerPrefs.SetString(name, Convert.ToBase64String(data));
            PlayerPrefs.Save();
        }

        public static string Load(string name)
        {
            var s = PlayerPrefs.GetString(name, "");
            if (string.IsNullOrEmpty(s)) return null;
            try
            {
                var data = Convert.FromBase64String(s);
                var key = InstallKey();
                for (int i = 0; i < data.Length; i++) data[i] ^= key[i % key.Length];
                return Encoding.UTF8.GetString(data);
            }
            catch { return null; }
        }

        public static void Delete(string name) { PlayerPrefs.DeleteKey(name); PlayerPrefs.Save(); }
    }
}
