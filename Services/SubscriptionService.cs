using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VPNApp.Models;

namespace VPNApp.Services
{
    public class SubscriptionService
    {
        // =====================================================
        //  КОНСТАНТЫ
        // =====================================================

        private const string SUBSCRIPTIONS_FILE = "subscriptions.json";
        private const int DOWNLOAD_TIMEOUT_SEC = 15;
        private const string MANUAL_GROUP_NAME = "📌 Мои конфиги";

        // =====================================================
        //  ПОЛЯ
        // =====================================================

        private readonly HttpClient _httpClient;
        private readonly string _savePath;

        // =====================================================
        //  КОНСТРУКТОР
        // =====================================================

        public SubscriptionService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(DOWNLOAD_TIMEOUT_SEC)
            };

            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "clash-meta/1.0 (sing-box)"
            );

            _savePath = Path.Combine(
                FileSystem.AppDataDirectory,
                SUBSCRIPTIONS_FILE
            );
        }

        // =====================================================
        //  ДОБАВЛЕНИЕ ПОДПИСКИ
        // =====================================================

        public async Task<SubscriptionGroup?> AddSubscriptionAsync(string url, string groupName)
        {
            string? rawContent = await DownloadAsync(url);
            if (string.IsNullOrWhiteSpace(rawContent))
                return null;

            var servers = ParseSubscription(rawContent);
            if (servers == null || servers.Count == 0)
                return null;

            var group = new SubscriptionGroup
            {
                Id = Guid.NewGuid().ToString(),
                Name = groupName,
                Url = url,
                Servers = servers,
                LastUpdated = DateTime.Now,
                AutoUpdate = true,
                IsEnabled = true
            };

            return group;
        }

        // =====================================================
        //  ДОБАВЛЕНИЕ КОНФИГА ВРУЧНУЮ
        // =====================================================

        /// <summary>
        /// Добавить одну или несколько ссылок-конфигов вручную.
        /// Все они попадут в группу «📌 Мои конфиги» (создастся при первом добавлении).
        /// </summary>
        public async Task<SubscriptionGroup?> AddManualServerAsync(string rawLink)
        {
            if (string.IsNullOrWhiteSpace(rawLink))
                return null;

            var parsed = ParsePlainLinks(rawLink.Trim());
            if (parsed == null || parsed.Count == 0)
                return null;

            var groups = await LoadSavedGroupsAsync();

            var manualGroup = groups.FirstOrDefault(g => g.Name == MANUAL_GROUP_NAME);

            if (manualGroup == null)
            {
                manualGroup = new SubscriptionGroup
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = MANUAL_GROUP_NAME,
                    Url = string.Empty,
                    Servers = new List<ServerModel>(),
                    LastUpdated = DateTime.Now,
                    AutoUpdate = false,
                    IsEnabled = true
                };
                groups.Add(manualGroup);
            }

            foreach (var server in parsed)
            {
                server.GroupId = manualGroup.Id;
                manualGroup.Servers.Add(server);
            }

            manualGroup.LastUpdated = DateTime.Now;

            await SaveGroupsAsync(groups);

            return manualGroup;
        }

        // =====================================================
        //  ОБНОВЛЕНИЕ
        // =====================================================

        public async Task<SubscriptionGroup?> UpdateGroupAsync(SubscriptionGroup group)
        {
            try
            {
                // Ручные конфиги не обновляем
                if (string.IsNullOrEmpty(group.Url))
                    return group;

                string? rawContent = await DownloadAsync(group.Url);
                if (string.IsNullOrWhiteSpace(rawContent))
                    return group;

                var servers = ParseSubscription(rawContent);
                if (servers != null && servers.Count > 0)
                {
                    group.Servers = servers;
                    group.LastUpdated = DateTime.Now;
                }

                return group;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SubscriptionService] Ошибка обновления: {ex.Message}");
                return group;
            }
        }

        public async Task<List<SubscriptionGroup>> UpdateAllGroupsAsync(List<SubscriptionGroup> groups)
        {
            var tasks = new List<Task<SubscriptionGroup?>>();

            foreach (var group in groups)
            {
                if (group.AutoUpdate && !string.IsNullOrEmpty(group.Url))
                    tasks.Add(UpdateGroupAsync(group));
            }

            var results = await Task.WhenAll(tasks);
            return new List<SubscriptionGroup>(results.Where(g => g != null)!);
        }

        // =====================================================
        //  ЗАГРУЗКА
        // =====================================================

        private async Task<string?> DownloadAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SubscriptionService] Ошибка загрузки {url}: {ex.Message}");
                return null;
            }
        }

        // =====================================================
        //  ПАРСИНГ
        // =====================================================

        private List<ServerModel> ParseSubscription(string content)
        {
            content = content.Trim();

            if (IsBase64(content))
            {
                try
                {
                    string decoded = Encoding.UTF8.GetString(
                        Convert.FromBase64String(content));
                    return ParsePlainLinks(decoded);
                }
                catch { }
            }

            if (content.Contains("://"))
            {
                return ParsePlainLinks(content);
            }

            if (content.TrimStart().StartsWith("{"))
            {
                return ParseJsonConfig(content);
            }

            return new List<ServerModel>();
        }

        private List<ServerModel> ParsePlainLinks(string content)
        {
            var servers = new List<ServerModel>();
            var lines = content.Split(
                new[] { '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                ServerModel? server = null;

                try
                {
                    if (trimmed.StartsWith("vless://")) server = ParseVless(trimmed);
                    else if (trimmed.StartsWith("vmess://")) server = ParseVmess(trimmed);
                    else if (trimmed.StartsWith("ss://")) server = ParseShadowsocks(trimmed);
                    else if (trimmed.StartsWith("trojan://")) server = ParseTrojan(trimmed);
                    else if (trimmed.StartsWith("hy2://") ||
                             trimmed.StartsWith("hysteria2://")) server = ParseHysteria2(trimmed);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Parser] Ошибка парсинга строки: {ex.Message}");
                }

                if (server != null)
                    servers.Add(server);
            }

            return servers;
        }

        // =====================================================
        //  ПАРСЕРЫ ПРОТОКОЛОВ
        // =====================================================

        private ServerModel ParseVless(string link)
        {
            var uri = new Uri(link);
            var query = ParseQuery(uri.Query);
            var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

            var security = query.GetValueOrDefault("security", "none");
            var isReality = security == "reality";

            return new ServerModel
            {
                Protocol = VpnProtocol.VLESS,
                Name = string.IsNullOrEmpty(name) ? $"{uri.Host}:{uri.Port}" : name,
                Address = uri.Host,
                Port = uri.Port,
                Uuid = uri.UserInfo,
                Transport = query.GetValueOrDefault("type", "tcp"),
                Sni = query.GetValueOrDefault("sni", uri.Host),
                WsPath = query.GetValueOrDefault("path", "/"),
                WsHost = query.GetValueOrDefault("host", ""),
                GrpcServiceName = query.GetValueOrDefault("serviceName", ""),
                UseTls = security == "tls" || isReality,
                RawLink = link,

                RealityPublicKey = query.GetValueOrDefault("pbk", ""),
                RealityShortId = query.GetValueOrDefault("sid", ""),
                Fingerprint = query.GetValueOrDefault("fp", ""),
                Flow = query.GetValueOrDefault("flow", "")
            };
        }

        private ServerModel ParseVmess(string link)
        {
            string b64 = link.Substring("vmess://".Length);
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');

            string json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string GetStr(string key, string def = "") =>
                root.TryGetProperty(key, out var v) ? (v.GetString() ?? def) : def;
            int GetInt(string key, int def = 0) =>
                root.TryGetProperty(key, out var v) ? v.GetInt32() : def;

            int port = 0;
            if (root.TryGetProperty("port", out var portElem))
            {
                if (portElem.ValueKind == JsonValueKind.Number)
                    port = portElem.GetInt32();
                else
                    int.TryParse(portElem.GetString(), out port);
            }

            return new ServerModel
            {
                Protocol = VpnProtocol.VMess,
                Name = GetStr("ps", GetStr("add")),
                Address = GetStr("add"),
                Port = port,
                Uuid = GetStr("id"),
                AlterId = GetInt("aid"),
                Transport = GetStr("net", "tcp"),
                Sni = GetStr("sni", GetStr("add")),
                WsPath = GetStr("path", "/"),
                WsHost = GetStr("host", ""),
                GrpcServiceName = GetStr("path", ""),
                UseTls = GetStr("tls") == "tls",
                RawLink = link
            };
        }

        private ServerModel ParseShadowsocks(string link)
        {
            var uri = new Uri(link);
            var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

            string userInfo = uri.UserInfo;
            string method, password;

            if (userInfo.Contains(":"))
            {
                var parts = userInfo.Split(':', 2);
                method = Uri.UnescapeDataString(parts[0]);
                password = Uri.UnescapeDataString(parts[1]);
            }
            else
            {
                string padding = userInfo.PadRight(userInfo.Length + (4 - userInfo.Length % 4) % 4, '=');
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padding));
                var parts = decoded.Split(':', 2);
                method = parts[0];
                password = parts.Length > 1 ? parts[1] : "";
            }

            return new ServerModel
            {
                Protocol = VpnProtocol.Shadowsocks,
                Name = string.IsNullOrEmpty(name) ? $"{uri.Host}:{uri.Port}" : name,
                Address = uri.Host,
                Port = uri.Port,
                EncryptionMethod = method,
                Password = password,
                UseTls = false,
                RawLink = link
            };
        }

        private ServerModel ParseTrojan(string link)
        {
            var uri = new Uri(link);
            var query = ParseQuery(uri.Query);
            var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

            return new ServerModel
            {
                Protocol = VpnProtocol.Trojan,
                Name = string.IsNullOrEmpty(name) ? $"{uri.Host}:{uri.Port}" : name,
                Address = uri.Host,
                Port = uri.Port,
                Password = Uri.UnescapeDataString(uri.UserInfo),
                Transport = query.GetValueOrDefault("type", "tcp"),
                Sni = query.GetValueOrDefault("sni", uri.Host),
                WsPath = query.GetValueOrDefault("path", "/"),
                WsHost = query.GetValueOrDefault("host", ""),
                GrpcServiceName = query.GetValueOrDefault("serviceName", ""),
                UseTls = true,
                RawLink = link
            };
        }

        private ServerModel ParseHysteria2(string link)
        {
            string normalized = link.Replace("hysteria2://", "hy2://");
            var uri = new Uri(normalized);
            var query = ParseQuery(uri.Query);
            var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

            return new ServerModel
            {
                Protocol = VpnProtocol.Hysteria2,
                Name = string.IsNullOrEmpty(name) ? $"{uri.Host}:{uri.Port}" : name,
                Address = uri.Host,
                Port = uri.Port,
                Password = Uri.UnescapeDataString(uri.UserInfo),
                Sni = query.GetValueOrDefault("sni", uri.Host),
                UseTls = true,
                RawLink = link
            };
        }

        // =====================================================
        //  ПАРСИНГ JSON
        // =====================================================

        private List<ServerModel> ParseJsonConfig(string json)
        {
            var servers = new List<ServerModel>();

            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("outbounds", out var outbounds))
                    return servers;

                foreach (var ob in outbounds.EnumerateArray())
                {
                    string type = ob.TryGetProperty("type", out var t)
                        ? (t.GetString() ?? "")
                        : "";

                    ServerModel? server = null;

                    switch (type.ToLower())
                    {
                        case "vless": server = ParseJsonOutbound(ob, VpnProtocol.VLESS); break;
                        case "vmess": server = ParseJsonOutbound(ob, VpnProtocol.VMess); break;
                        case "shadowsocks": server = ParseJsonOutbound(ob, VpnProtocol.Shadowsocks); break;
                        case "trojan": server = ParseJsonOutbound(ob, VpnProtocol.Trojan); break;
                        case "hysteria2": server = ParseJsonOutbound(ob, VpnProtocol.Hysteria2); break;
                    }

                    if (server != null)
                        servers.Add(server);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Parser] Ошибка JSON: {ex.Message}");
            }

            return servers;
        }

        private ServerModel ParseJsonOutbound(JsonElement ob, VpnProtocol protocol)
        {
            string GetStr(string key, string def = "") =>
                ob.TryGetProperty(key, out var v) ? (v.GetString() ?? def) : def;
            int GetInt(string key, int def = 0) =>
                ob.TryGetProperty(key, out var v) ? v.GetInt32() : def;

            return new ServerModel
            {
                Protocol = protocol,
                Name = GetStr("tag", GetStr("server")),
                Address = GetStr("server"),
                Port = GetInt("server_port"),
                Uuid = GetStr("uuid"),
                Password = GetStr("password"),
                UseTls = ob.TryGetProperty("tls", out _)
            };
        }

        // =====================================================
        //  СОХРАНЕНИЕ
        // =====================================================

        public async Task SaveGroupsAsync(List<SubscriptionGroup> groups)
        {
            try
            {
                var json = JsonSerializer.Serialize(groups, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_savePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SubscriptionService] Ошибка сохранения: {ex.Message}");
            }
        }

        public async Task<List<SubscriptionGroup>> LoadSavedGroupsAsync()
        {
            try
            {
                if (!File.Exists(_savePath))
                    return new List<SubscriptionGroup>();

                var json = await File.ReadAllTextAsync(_savePath, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<SubscriptionGroup>>(json)
                    ?? new List<SubscriptionGroup>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SubscriptionService] Ошибка загрузки: {ex.Message}");
                return new List<SubscriptionGroup>();
            }
        }

        // =====================================================
        //  ВСПОМОГАТЕЛЬНЫЕ
        // =====================================================

        private bool IsBase64(string str)
        {
            if (string.IsNullOrEmpty(str)) return false;
            str = str.Trim();
            return str.Length % 4 == 0
                && System.Text.RegularExpressions.Regex.IsMatch(
                    str, @"^[a-zA-Z0-9\+/]*={0,3}$");
        }

        private Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(query)) return result;

            query = query.TrimStart('?');

            foreach (var pair in query.Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    result[Uri.UnescapeDataString(parts[0])] =
                        Uri.UnescapeDataString(parts[1]);
                }
            }

            return result;
        }
    }
}