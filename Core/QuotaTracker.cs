using System;
using System.Globalization;
using System.IO;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace LiveTranslatorOverlay.Core
{
    public class QuotaInfo
    {
        public string Source { get; set; } = "Google Translate";
        public int? LimitRequests { get; set; }
        public int? RemainingRequests { get; set; }
        public string? ResetRequests { get; set; }

        public int? LimitTokens { get; set; }
        public int? RemainingTokens { get; set; }
        public string? ResetTokens { get; set; }

        public bool IsUnlimited { get; set; } = true;
        public int CooldownSeconds { get; set; } = 0;
        public DateTime? CooldownUntilUtc { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public double ProgressValue
        {
            get
            {
                if (IsUnlimited) return 100;
                if (CooldownSeconds > 0) return 0;
                if (LimitRequests.HasValue && LimitRequests.Value > 0 && RemainingRequests.HasValue)
                {
                    return Math.Clamp((double)RemainingRequests.Value / LimitRequests.Value * 100.0, 0, 100);
                }
                if (LimitTokens.HasValue && LimitTokens.Value > 0 && RemainingTokens.HasValue)
                {
                    return Math.Clamp((double)RemainingTokens.Value / LimitTokens.Value * 100.0, 0, 100);
                }
                // When awaiting first request / no data yet, do NOT display a false 100% full bar
                return 0;
            }
        }

        public string BadgeText
        {
            get
            {
                if (CooldownSeconds > 0) return $"{CooldownSeconds}s";
                if (IsUnlimited) return "Unlimited";
                if (RemainingRequests.HasValue && LimitRequests.HasValue && LimitRequests.Value > 0)
                {
                    return $"{(int)ProgressValue}%";
                }
                if (RemainingTokens.HasValue && LimitTokens.HasValue && LimitTokens.Value > 0)
                {
                    return $"{(int)ProgressValue}%";
                }
                return "Ready";
            }
        }

        public string BadgeBgColor
        {
            get
            {
                if (CooldownSeconds > 0) return "#7F1D1D"; // Dark Red
                if (IsUnlimited) return "#065F46";        // Dark Emerald
                if (!RemainingRequests.HasValue && !RemainingTokens.HasValue) return "#27272A"; // Dark Zinc
                double p = ProgressValue;
                if (p < 20) return "#7F1D1D";             // Dark Red
                if (p < 50) return "#78350F";             // Dark Amber
                return "#065F46";                         // Dark Emerald
            }
        }

        public string BadgeFgColor
        {
            get
            {
                if (CooldownSeconds > 0) return "#F87171"; // Red
                if (IsUnlimited) return "#34D399";        // Emerald
                if (!RemainingRequests.HasValue && !RemainingTokens.HasValue) return "#A1A1AA"; // Silver / Gray
                double p = ProgressValue;
                if (p < 20) return "#F87171";             // Red
                if (p < 50) return "#FBBF24";             // Amber
                return "#34D399";                         // Emerald
            }
        }

        public string DisplayMain
        {
            get
            {
                if (CooldownSeconds > 0)
                {
                    return $"Cooldown: {CooldownSeconds}s (Rate limit exceeded)";
                }

                if (IsUnlimited)
                {
                    return "Unlimited";
                }

                if (RemainingRequests.HasValue && LimitRequests.HasValue)
                {
                    string reqStr = $"{RemainingRequests:N0}/{LimitRequests:N0} req";
                    string resetDesc = QuotaTracker.GetRemainingResetDescription(ResetRequests, LastUpdated);
                    if (!string.IsNullOrEmpty(resetDesc))
                    {
                        reqStr += $" ({resetDesc})";
                    }

                    if (RemainingTokens.HasValue && LimitTokens.HasValue)
                    {
                        return $"{reqStr}  |  {RemainingTokens:N0}/{LimitTokens:N0} tok";
                    }
                    return reqStr;
                }

                if (RemainingTokens.HasValue && LimitTokens.HasValue)
                {
                    return $"{RemainingTokens:N0}/{LimitTokens:N0} tokens";
                }

                return "Ready (Tracking active)";
            }
        }

        public void EvaluateExpiration()
        {
            if (IsUnlimited) return;

            DateTime now = DateTime.UtcNow;

            // 1. Evaluate Cooldown
            if (CooldownUntilUtc.HasValue)
            {
                if (now < CooldownUntilUtc.Value)
                {
                    CooldownSeconds = Math.Max(1, (int)Math.Ceiling((CooldownUntilUtc.Value - now).TotalSeconds));
                }
                else
                {
                    CooldownSeconds = 0;
                    CooldownUntilUtc = null;
                }
            }

            // 2. Evaluate Requests Reset (Groq rate limits reset after reset duration)
            if (LimitRequests.HasValue && RemainingRequests.HasValue && RemainingRequests.Value < LimitRequests.Value)
            {
                bool shouldResetRequests = false;
                if (QuotaTracker.TryParseResetDuration(ResetRequests, out var duration))
                {
                    if (now >= LastUpdated + duration)
                    {
                        shouldResetRequests = true;
                    }
                }
                else
                {
                    // Fallback heuristics: daily (RPD) vs minute (RPM)
                    if (LimitRequests.Value >= 1000)
                    {
                        if (now.Date != LastUpdated.Date || (now - LastUpdated).TotalHours >= 24)
                        {
                            shouldResetRequests = true;
                        }
                    }
                    else
                    {
                        if ((now - LastUpdated).TotalSeconds >= 60)
                        {
                            shouldResetRequests = true;
                        }
                    }
                }

                if (shouldResetRequests)
                {
                    RemainingRequests = LimitRequests.Value;
                }
            }

            // 3. Evaluate Tokens Reset (Groq TPM tokens typically reset in seconds/minutes)
            if (LimitTokens.HasValue && RemainingTokens.HasValue && RemainingTokens.Value < LimitTokens.Value)
            {
                bool shouldResetTokens = false;
                if (QuotaTracker.TryParseResetDuration(ResetTokens, out var tokenDuration))
                {
                    if (now >= LastUpdated + tokenDuration)
                    {
                        shouldResetTokens = true;
                    }
                }
                else
                {
                    if ((now - LastUpdated).TotalSeconds >= 60)
                    {
                        shouldResetTokens = true;
                    }
                }

                if (shouldResetTokens)
                {
                    RemainingTokens = LimitTokens.Value;
                }
            }
        }
    }

    public class PersistedQuotaData
    {
        public string? ApiKeyHash { get; set; }
        public QuotaInfo? GroqLlm { get; set; }
        public QuotaInfo? GroqWhisper { get; set; }
        public string LastActiveType { get; set; } = "google";
        public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class QuotaTracker
    {
        private static readonly Lazy<QuotaTracker> _instance = new(() => new QuotaTracker());
        public static QuotaTracker Instance => _instance.Value;

        private readonly object _lock = new();
        private System.Threading.Timer? _cooldownTimer;

        private QuotaInfo _lastInfo = new QuotaInfo
        {
            Source = "Google Translate",
            IsUnlimited = true
        };

        private QuotaInfo _groqLlm = new QuotaInfo { Source = "Groq AI (Translation)", IsUnlimited = false };
        private QuotaInfo _groqWhisper = new QuotaInfo { Source = "Groq Whisper (STT)", IsUnlimited = false };
        private string _lastActiveSourceType = "google"; // "google", "groq", "whisper", "deepgram"
        private string _currentApiKey = "";

        public event EventHandler<QuotaInfo>? QuotaChanged;

        public QuotaInfo CurrentInfo => _lastInfo;

        public QuotaTracker()
        {
            try
            {
                LoadState();
            }
            catch { }
        }

        public void Initialize(string apiKey, int engineIndex, int sttModeIndex)
        {
            lock (_lock)
            {
                _currentApiKey = apiKey ?? "";
                LoadState();

                if (engineIndex == 1)
                {
                    _groqLlm.EvaluateExpiration();
                    _lastInfo = _groqLlm;
                    _lastActiveSourceType = "groq";
                }
                else if (sttModeIndex == 1 && _lastActiveSourceType == "whisper")
                {
                    _groqWhisper.EvaluateExpiration();
                    _lastInfo = _groqWhisper;
                    _lastActiveSourceType = "whisper";
                }
                else if (_lastActiveSourceType == "deepgram")
                {
                    _lastInfo = new QuotaInfo { Source = "Deepgram STT", IsUnlimited = true, CooldownSeconds = 0, LastUpdated = DateTime.UtcNow };
                }
                else
                {
                    _lastInfo = new QuotaInfo { Source = "Google Translate", IsUnlimited = true, CooldownSeconds = 0, LastUpdated = DateTime.UtcNow };
                    _lastActiveSourceType = "google";
                }

                CheckAndStartCooldownTimer();
            }

            QuotaChanged?.Invoke(this, _lastInfo);
        }

        public void SetApiKey(string apiKey)
        {
            lock (_lock)
            {
                string oldHash = ComputeKeyHash(_currentApiKey);
                string newHash = ComputeKeyHash(apiKey);

                _currentApiKey = apiKey ?? "";

                if (!string.IsNullOrEmpty(newHash) && oldHash != newHash)
                {
                    // Different API key detected: reset cached counters for Groq
                    _groqLlm = new QuotaInfo { Source = "Groq AI (Translation)", IsUnlimited = false };
                    _groqWhisper = new QuotaInfo { Source = "Groq Whisper (STT)", IsUnlimited = false };
                    SaveState();
                }
            }
        }

        public void UpdateFromHeaders(HttpResponseHeaders headers, string source)
        {
            try
            {
                lock (_lock)
                {
                    bool isWhisper = source.Contains("Whisper", StringComparison.OrdinalIgnoreCase);
                    var target = isWhisper ? _groqWhisper : _groqLlm;

                    target.Source = source;
                    target.IsUnlimited = false;
                    target.CooldownSeconds = 0;
                    target.CooldownUntilUtc = null;
                    target.LastUpdated = DateTime.UtcNow;

                    if (TryGetHeaderInt(headers, "x-ratelimit-limit-requests", out int limitReq))
                        target.LimitRequests = limitReq;

                    if (TryGetHeaderInt(headers, "x-ratelimit-remaining-requests", out int remReq))
                        target.RemainingRequests = remReq;

                    if (TryGetHeaderString(headers, "x-ratelimit-reset-requests", out string resetReq))
                        target.ResetRequests = resetReq;

                    if (TryGetHeaderInt(headers, "x-ratelimit-limit-tokens", out int limitTok))
                        target.LimitTokens = limitTok;

                    if (TryGetHeaderInt(headers, "x-ratelimit-remaining-tokens", out int remTok))
                        target.RemainingTokens = remTok;

                    if (TryGetHeaderString(headers, "x-ratelimit-reset-tokens", out string resetTok))
                        target.ResetTokens = resetTok;

                    _lastInfo = target;
                    _lastActiveSourceType = isWhisper ? "whisper" : "groq";

                    SaveState();
                }

                QuotaChanged?.Invoke(this, _lastInfo);
            }
            catch { }
        }

        public void SetUnlimited(string source = "Google Translate")
        {
            lock (_lock)
            {
                _lastInfo = new QuotaInfo
                {
                    Source = source,
                    IsUnlimited = true,
                    CooldownSeconds = 0,
                    LastUpdated = DateTime.UtcNow
                };
                _lastActiveSourceType = "google";
                SaveState();
            }
            QuotaChanged?.Invoke(this, _lastInfo);
        }

        public void SetDeepgram()
        {
            lock (_lock)
            {
                _lastInfo = new QuotaInfo
                {
                    Source = "Deepgram STT",
                    IsUnlimited = true,
                    CooldownSeconds = 0,
                    LastUpdated = DateTime.UtcNow
                };
                _lastActiveSourceType = "deepgram";
                SaveState();
            }
            QuotaChanged?.Invoke(this, _lastInfo);
        }

        public void SetCooldown(string source, int seconds)
        {
            lock (_lock)
            {
                bool isWhisper = source.Contains("Whisper", StringComparison.OrdinalIgnoreCase);
                var target = isWhisper ? _groqWhisper : _groqLlm;

                target.Source = source;
                target.IsUnlimited = false;
                target.CooldownSeconds = Math.Max(1, seconds);
                target.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
                target.LastUpdated = DateTime.UtcNow;

                _lastInfo = target;
                _lastActiveSourceType = isWhisper ? "whisper" : "groq";

                SaveState();
                CheckAndStartCooldownTimer();
            }

            QuotaChanged?.Invoke(this, _lastInfo);
        }

        public void NotifyCurrentGroq()
        {
            lock (_lock)
            {
                _groqLlm.EvaluateExpiration();
                _lastInfo = _groqLlm;
                _lastActiveSourceType = "groq";
                CheckAndStartCooldownTimer();
            }
            QuotaChanged?.Invoke(this, _groqLlm);
        }

        public void NotifyCurrentGroqWhisper()
        {
            lock (_lock)
            {
                _groqWhisper.EvaluateExpiration();
                _lastInfo = _groqWhisper;
                _lastActiveSourceType = "whisper";
                CheckAndStartCooldownTimer();
            }
            QuotaChanged?.Invoke(this, _groqWhisper);
        }

        public void Flush()
        {
            lock (_lock)
            {
                SaveState();
            }
        }

        private static string GetCacheFilePath()
        {
            try
            {
                string localAppData = AppContext.GetData("LiveTranslatorOverlay.QuotaCacheDirectory") as string
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveTranslatorOverlay");
                Directory.CreateDirectory(localAppData);
                return Path.Combine(localAppData, "quota_state.json");
            }
            catch
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quota_state.json");
            }
        }

        private void SaveState()
        {
            try
            {
                var data = new PersistedQuotaData
                {
                    ApiKeyHash = ComputeKeyHash(_currentApiKey),
                    GroqLlm = _groqLlm,
                    GroqWhisper = _groqWhisper,
                    LastActiveType = _lastActiveSourceType,
                    SavedAtUtc = DateTime.UtcNow
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                string path = GetCacheFilePath();
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private void LoadState()
        {
            try
            {
                string path = GetCacheFilePath();
                if (!File.Exists(path)) return;

                string json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<PersistedQuotaData>(json);
                if (data == null) return;

                string currentHash = ComputeKeyHash(_currentApiKey);
                if (string.IsNullOrEmpty(_currentApiKey) || string.IsNullOrEmpty(data.ApiKeyHash) || currentHash == data.ApiKeyHash)
                {
                    if (data.GroqLlm != null)
                    {
                        _groqLlm = data.GroqLlm;
                        _groqLlm.EvaluateExpiration();
                    }

                    if (data.GroqWhisper != null)
                    {
                        _groqWhisper = data.GroqWhisper;
                        _groqWhisper.EvaluateExpiration();
                    }

                    _lastActiveSourceType = data.LastActiveType ?? "google";
                }
            }
            catch { }
        }

        private void CheckAndStartCooldownTimer()
        {
            bool hasCooldown = (_lastInfo.CooldownSeconds > 0) ||
                               (_groqLlm.CooldownSeconds > 0) ||
                               (_groqWhisper.CooldownSeconds > 0);

            if (hasCooldown)
            {
                _cooldownTimer ??= new System.Threading.Timer(OnCooldownTick, null, 1000, 1000);
            }
            else
            {
                _cooldownTimer?.Dispose();
                _cooldownTimer = null;
            }
        }

        private void OnCooldownTick(object? state)
        {
            bool anyActive = false;
            QuotaInfo infoToReport;

            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                UpdateCooldownItem(_groqLlm, now, ref anyActive);
                UpdateCooldownItem(_groqWhisper, now, ref anyActive);

                if (_lastInfo == _groqLlm)
                {
                    _lastInfo.CooldownSeconds = _groqLlm.CooldownSeconds;
                    _lastInfo.CooldownUntilUtc = _groqLlm.CooldownUntilUtc;
                }
                else if (_lastInfo == _groqWhisper)
                {
                    _lastInfo.CooldownSeconds = _groqWhisper.CooldownSeconds;
                    _lastInfo.CooldownUntilUtc = _groqWhisper.CooldownUntilUtc;
                }

                infoToReport = _lastInfo;

                if (!anyActive)
                {
                    _cooldownTimer?.Dispose();
                    _cooldownTimer = null;
                    SaveState();
                }
            }

            QuotaChanged?.Invoke(this, infoToReport);
        }

        private static void UpdateCooldownItem(QuotaInfo item, DateTime now, ref bool anyActive)
        {
            if (item.CooldownUntilUtc.HasValue)
            {
                if (now < item.CooldownUntilUtc.Value)
                {
                    item.CooldownSeconds = Math.Max(1, (int)Math.Ceiling((item.CooldownUntilUtc.Value - now).TotalSeconds));
                    anyActive = true;
                }
                else
                {
                    item.CooldownSeconds = 0;
                    item.CooldownUntilUtc = null;
                }
            }
            else
            {
                item.CooldownSeconds = 0;
            }
        }

        public static string ComputeKeyHash(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key.Trim()));
            return Convert.ToHexString(bytes).Substring(0, 16);
        }

        public static bool TryParseResetDuration(string? text, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim().ToLowerInvariant();

            // Format: "547ms"
            if (text.EndsWith("ms") && double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
            {
                duration = TimeSpan.FromMilliseconds(ms);
                return true;
            }

            // Format: "14.2s" or "8s"
            if (text.EndsWith("s") && !text.Contains('m') && !text.Contains('h') &&
                double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double sec))
            {
                duration = TimeSpan.FromSeconds(sec);
                return true;
            }

            // Pure numeric: e.g. "6.5"
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rawSec))
            {
                duration = TimeSpan.FromSeconds(rawSec);
                return true;
            }

            // Composite format: e.g. "1m26.4s", "2h30m10s", "24h0m"
            try
            {
                var match = Regex.Match(text, @"^(?:(\d+(?:\.\d+)?)h)?(?:(\d+(?:\.\d+)?)m(?!s))?(?:(\d+(?:\.\d+)?)s)?(?:(\d+(?:\.\d+)?)ms)?$");
                if (match.Success)
                {
                    double h = match.Groups[1].Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                    double m = match.Groups[2].Success ? double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                    double s = match.Groups[3].Success ? double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                    double milli = match.Groups[4].Success ? double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 0;

                    if (h > 0 || m > 0 || s > 0 || milli > 0)
                    {
                        duration = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s) + TimeSpan.FromMilliseconds(milli);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public static string GetRemainingResetDescription(string? resetText, DateTime lastUpdated)
        {
            if (TryParseResetDuration(resetText, out var duration))
            {
                DateTime resetAt = lastUpdated + duration;
                DateTime now = DateTime.UtcNow;
                if (now < resetAt)
                {
                    TimeSpan left = resetAt - now;
                    if (left.TotalHours >= 1)
                        return $"resets in {(int)left.TotalHours}h {left.Minutes}m";
                    if (left.TotalMinutes >= 1)
                        return $"resets in {(int)left.TotalMinutes}m {left.Seconds}s";
                    return $"resets in {Math.Max(1, (int)left.TotalSeconds)}s";
                }
                return "resetting...";
            }
            return "";
        }

        private static bool TryGetHeaderInt(HttpResponseHeaders headers, string key, out int value)
        {
            value = 0;
            if (headers.TryGetValues(key, out var vals))
            {
                foreach (var v in vals)
                {
                    if (int.TryParse(v, out value)) return true;
                }
            }
            return false;
        }

        private static bool TryGetHeaderString(HttpResponseHeaders headers, string key, out string value)
        {
            value = "";
            if (headers.TryGetValues(key, out var vals))
            {
                foreach (var v in vals)
                {
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        value = v;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
