using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace VPNApp.Models
{
    // =====================================================
    //  ПРОТОКОЛЫ VPN
    // =====================================================

    public enum VpnProtocol
    {
        VLESS,
        VMess,
        Shadowsocks,
        Trojan,
        Hysteria2,
        Unknown
    }

    // =====================================================
    //  МОДЕЛЬ СЕРВЕРА
    // =====================================================

    public class ServerModel : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Неизвестный сервер";
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; } = 443;

        public VpnProtocol Protocol { get; set; } = VpnProtocol.Unknown;

        public string Country { get; set; } = "Unknown";

        private int _ping = -1;
        public int Ping
        {
            get => _ping;
            set
            {
                if (_ping == value) return;
                _ping = value;
                OnPropertyChanged(nameof(Ping));
                OnPropertyChanged(nameof(PingDisplay));
                OnPropertyChanged(nameof(PingColor));
            }
        }

        public bool IsAvailable { get; set; } = true;
        public DateTime AddedAt { get; set; } = DateTime.Now;

        // --- Настройки протоколов ---

        public string Uuid { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string EncryptionMethod { get; set; } = "aes-128-gcm";
        public string Transport { get; set; } = "tcp";
        public string Sni { get; set; } = string.Empty;
        public bool UseTls { get; set; } = true;
        public string WsPath { get; set; } = "/";
        public string WsHost { get; set; } = string.Empty;
        public string GrpcServiceName { get; set; } = string.Empty;
        public int AlterId { get; set; } = 0;
        public string RawLink { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;

        // --- Reality ---

        public string RealityPublicKey { get; set; } = string.Empty;
        public string RealityShortId { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public string Flow { get; set; } = string.Empty;

        // --- Вычисляемые ---

        public string CleanName
        {
            get
            {
                if (string.IsNullOrEmpty(Name)) return Name;

                var sb = new StringBuilder(Name.Length);
                foreach (var ch in Name)
                {
                    if (ch >= 0x0300 && ch <= 0x036F) continue;
                    sb.Append(ch);
                }
                return sb.ToString().Trim();
            }
        }

        public string FlagEmoji
        {
            get
            {
                var flagFromName = ExtractEmojiFlag(Name);
                if (!string.IsNullOrEmpty(flagFromName))
                    return flagFromName;

                return Country?.ToLower() switch
                {
                    "russia" or "ru" or "россия" => "🇷🇺",
                    "germany" or "de" or "германия" => "🇩🇪",
                    "usa" or "us" or "сша" => "🇺🇸",
                    "netherlands" or "nl" or "нидерланды" => "🇳🇱",
                    "france" or "fr" or "франция" => "🇫🇷",
                    "uk" or "gb" or "великобритания" => "🇬🇧",
                    "japan" or "jp" or "япония" => "🇯🇵",
                    "singapore" or "sg" or "сингапур" => "🇸🇬",
                    "finland" or "fi" or "финляндия" => "🇫🇮",
                    "canada" or "ca" or "канада" => "🇨🇦",
                    "poland" or "pl" or "польша" => "🇵🇱",
                    "turkey" or "tr" or "турция" => "🇹🇷",
                    "sweden" or "se" or "швеция" => "🇸🇪",
                    "switzerland" or "ch" or "швейцария" => "🇨🇭",
                    "austria" or "at" or "австрия" => "🇦🇹",
                    "italy" or "it" or "италия" => "🇮🇹",
                    "spain" or "es" or "испания" => "🇪🇸",
                    "india" or "in" or "индия" => "🇮🇳",
                    "korea" or "kr" or "корея" => "🇰🇷",
                    "china" or "cn" or "китай" => "🇨🇳",
                    "hong" or "hk" or "гонконг" => "🇭🇰",
                    "australia" or "au" or "австралия" => "🇦🇺",
                    "ukraine" or "ua" or "украина" => "🇺🇦",
                    "belarus" or "by" or "беларусь" => "🇧🇾",
                    "kazakhstan" or "kz" or "казахстан" => "🇰🇿",
                    _ => "🌐"
                };
            }
        }

        public string PingDisplay => Ping switch
        {
            -1 => "— ms",
            < 80 => $"{Ping} ms ⚡",
            < 150 => $"{Ping} ms ✅",
            < 300 => $"{Ping} ms ⚠️",
            _ => $"{Ping} ms ❌"
        };

        public string PingColor => Ping switch
        {
            -1 => "#555555",
            < 80 => "#00FF88",
            < 150 => "#FFFFFF",
            < 300 => "#FFD700",
            _ => "#FF3B3B"
        };

        public string ProtocolDisplay => Protocol.ToString().ToUpper();
        public string Subtitle => $"{ProtocolDisplay} • {Address}:{Port}";

        // --- Утилиты ---

        private static string ExtractEmojiFlag(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            for (int i = 0; i < text.Length - 3; i++)
            {
                if (!char.IsHighSurrogate(text[i])) continue;

                int code1;
                try { code1 = char.ConvertToUtf32(text, i); }
                catch { continue; }

                if (code1 < 0x1F1E6 || code1 > 0x1F1FF) continue;

                int nextIndex = i + 2;
                if (nextIndex >= text.Length - 1) continue;
                if (!char.IsHighSurrogate(text[nextIndex])) continue;

                int code2;
                try { code2 = char.ConvertToUtf32(text, nextIndex); }
                catch { continue; }

                if (code2 < 0x1F1E6 || code2 > 0x1F1FF) continue;

                return text.Substring(i, 4);
            }

            return string.Empty;
        }

        public ServerModel Clone() => new ServerModel
        {
            Id = this.Id,
            Name = this.Name,
            Address = this.Address,
            Port = this.Port,
            Protocol = this.Protocol,
            Country = this.Country,
            Ping = this.Ping,
            IsAvailable = this.IsAvailable,
            Uuid = this.Uuid,
            Password = this.Password,
            EncryptionMethod = this.EncryptionMethod,
            Transport = this.Transport,
            Sni = this.Sni,
            UseTls = this.UseTls,
            WsPath = this.WsPath,
            WsHost = this.WsHost,
            GrpcServiceName = this.GrpcServiceName,
            AlterId = this.AlterId,
            RawLink = this.RawLink,
            GroupId = this.GroupId,
            AddedAt = this.AddedAt,
            RealityPublicKey = this.RealityPublicKey,
            RealityShortId = this.RealityShortId,
            Fingerprint = this.Fingerprint,
            Flow = this.Flow
        };

        public override string ToString() => $"[{ProtocolDisplay}] {Name} ({Address}:{Port})";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // =====================================================
    //  ГРУППА ПОДПИСКИ
    // =====================================================

    public class SubscriptionGroup
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Новая группа";
        public string Url { get; set; } = string.Empty;

        public List<ServerModel> Servers { get; set; } = new();

        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
        public bool AutoUpdate { get; set; } = true;
        public bool IsEnabled { get; set; } = true;

        public int ServerCount => Servers?.Count ?? 0;

        public string LastUpdatedDisplay
        {
            get
            {
                if (LastUpdated == DateTime.MinValue)
                    return "Никогда";

                var diff = DateTime.Now - LastUpdated;

                if (diff.TotalMinutes < 1) return "Только что";
                if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} мин. назад";
                if (diff.TotalDays < 1) return $"{(int)diff.TotalHours} ч. назад";
                return LastUpdated.ToString("dd.MM.yyyy HH:mm");
            }
        }

        public string Subtitle => $"{ServerCount} серверов • {LastUpdatedDisplay}";
    }

    // =====================================================
    //  СТАТИСТИКА ТРАФИКА
    // =====================================================

    public class TrafficStats
    {
        public long TotalDownload { get; set; } = 0;
        public long TotalUpload { get; set; } = 0;
        public long DownloadSpeed { get; set; } = 0;
        public long UploadSpeed { get; set; } = 0;
    }

    // =====================================================
    //  НАСТРОЙКИ ПРИЛОЖЕНИЯ  ← ЭТО ЗДЕСЬ!
    // =====================================================

    public class AppSettings
    {
        public VpnProtocol DefaultProtocol { get; set; } = VpnProtocol.VLESS;
        public bool AutoConnect { get; set; } = false;
        public int UpdateIntervalHours { get; set; } = 24;
        public int LocalSocksPort { get; set; } = 1080;
        public int LocalHttpPort { get; set; } = 8080;
        public bool KillSwitch { get; set; } = false;
        public string DnsServer { get; set; } = "8.8.8.8";
        public RoutingMode Routing { get; set; } = RoutingMode.Proxy;
        public string LastServerId { get; set; } = string.Empty;
    }

    public enum RoutingMode
    {
        Global,
        Proxy,
        Direct
    }

    public class ConnectionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }

        public static ConnectionResult Ok()
            => new() { Success = true, ConnectedAt = DateTime.Now };

        public static ConnectionResult Fail(string error)
            => new() { Success = false, ErrorMessage = error };
    }
}