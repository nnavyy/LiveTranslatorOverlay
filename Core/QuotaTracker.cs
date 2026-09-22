using System;
using System.Net.Http.Headers;

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
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public double ProgressValue
        {
            get
            {
                if (IsUnlimited) return 100;
                if (CooldownSeconds > 0) return 0;
                if (LimitRequests.HasValue && LimitRequests > 0 && RemainingRequests.HasValue)
                {
                    return Math.Clamp((double)RemainingRequests.Value / LimitRequests.Value * 100.0, 0, 100);
                }
                if (LimitTokens.HasValue && LimitTokens > 0 && RemainingTokens.HasValue)
                {
                    return Math.Clamp((double)RemainingTokens.Value / LimitTokens.Value * 100.0, 0, 100);
                }
                return 100;
            }
        }

        public string BadgeText
        {
            get
            {
                if (CooldownSeconds > 0) return $"{CooldownSeconds}s";
                if (IsUnlimited) return "Unlimited";
                if (RemainingRequests.HasValue && LimitRequests.HasValue)
                {
                    return $"{(int)ProgressValue}%";
                }
                return "Active";
            }
        }

        public string BadgeBgColor
        {
            get
            {
                if (CooldownSeconds > 0) return "#7F1D1D"; // Dark Red
                if (IsUnlimited) return "#065F46";        // Dark Emerald
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
                    return $"Cooldown: {CooldownSeconds}s";
                }

                if (IsUnlimited)
                {
                    return "Unlimited";
                }

                if (RemainingRequests.HasValue && LimitRequests.HasValue)
                {
                    string reqStr = $"{RemainingRequests:N0}/{LimitRequests:N0} req";
                    if (RemainingTokens.HasValue && LimitTokens.HasValue)
                    {
                        return $"{reqStr}  |  {RemainingTokens:N0}/{LimitTokens:N0} tokens";
                    }
                    return reqStr;
                }

                if (RemainingTokens.HasValue && LimitTokens.HasValue)
                {
                    return $"{RemainingTokens:N0}/{LimitTokens:N0} tokens";
                }

                return "Tracking...";
            }
        }
    }

    public class QuotaTracker
    {
        private static readonly Lazy<QuotaTracker> _instance = new(() => new QuotaTracker());
        public static QuotaTracker Instance => _instance.Value;

        private QuotaInfo _lastInfo = new QuotaInfo
        {
            Source = "Google Translate",
            IsUnlimited = true
        };

        private readonly QuotaInfo _groqLlm = new QuotaInfo { Source = "Groq AI (Translation)", IsUnlimited = false };
        private readonly QuotaInfo _groqWhisper = new QuotaInfo { Source = "Groq Whisper (STT)", IsUnlimited = false };

        public event EventHandler<QuotaInfo>? QuotaChanged;

        public QuotaInfo CurrentInfo => _lastInfo;

        public void UpdateFromHeaders(HttpResponseHeaders headers, string source)
        {
            try
            {
                bool isWhisper = source.Contains("Whisper", StringComparison.OrdinalIgnoreCase);
                var target = isWhisper ? _groqWhisper : _groqLlm;

                target.Source = source;
                target.IsUnlimited = false;
                target.CooldownSeconds = 0;
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
                QuotaChanged?.Invoke(this, target);
            }
            catch { }
        }

        public void SetUnlimited(string source = "Google Translate")
        {
            _lastInfo = new QuotaInfo
            {
                Source = source,
                IsUnlimited = true,
                CooldownSeconds = 0,
                LastUpdated = DateTime.UtcNow
            };
            QuotaChanged?.Invoke(this, _lastInfo);
        }

        public void SetDeepgram()
        {
            _lastInfo = new QuotaInfo
            {
                Source = "Deepgram STT",
                IsUnlimited = true,
                CooldownSeconds = 0,
                LastUpdated = DateTime.UtcNow
            };
            QuotaChanged?.Invoke(this, _lastInfo);
        }

        public void SetCooldown(string source, int seconds)
        {
            var info = new QuotaInfo
            {
                Source = source,
                IsUnlimited = false,
                CooldownSeconds = seconds,
                LastUpdated = DateTime.UtcNow
            };
            _lastInfo = info;
            QuotaChanged?.Invoke(this, info);
        }

        public void NotifyCurrentGroq()
        {
            _lastInfo = _groqLlm;
            QuotaChanged?.Invoke(this, _groqLlm);
        }

        public void NotifyCurrentGroqWhisper()
        {
            _lastInfo = _groqWhisper;
            QuotaChanged?.Invoke(this, _groqWhisper);
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
