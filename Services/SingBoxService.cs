using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VPNApp.Models;

namespace VPNApp.Services
{
    public class SingBoxService
    {
        // =====================================================
        //  КОНСТАНТЫ
        // =====================================================

        private const int SOCKS_PORT = 2080;
        private const int HTTP_PORT = 2081;
        private const int API_PORT = 9090;

        private const string API_BASE = "http://127.0.0.1:9090";

        private const int MAX_LOG_LINES = 50;

        // =====================================================
        //  ПОЛЯ
        // =====================================================

        private Process? _singBoxProcess;
        private CancellationTokenSource? _cts;

        private readonly HttpClient _httpClient;
        private readonly string _configPath;
        private readonly string _binaryPath;

        private long _prevDownload = 0;
        private long _prevUpload = 0;

        private bool _binaryReady = false;

        private readonly Queue<string> _lastLogs = new();

        // =====================================================
        //  КОНСТРУКТОР
        // =====================================================

        public SingBoxService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            var appDir = FileSystem.AppDataDirectory;

            _configPath = Path.Combine(appDir, "singbox_config.json");

            var binaryFileName = DeviceInfo.Platform == DevicePlatform.WinUI
                ? "sing-box.exe"
                : "sing-box";

            _binaryPath = Path.Combine(appDir, binaryFileName);
        }

        // =====================================================
        //  РАСПАКОВКА
        // =====================================================

        private async Task<bool> EnsureBinaryExtractedAsync()
        {
            if (_binaryReady) return true;

            try
            {
                if (File.Exists(_binaryPath))
                {
                    _binaryReady = true;
                    Debug.WriteLine($"[SingBox] Бинарник уже есть: {_binaryPath}");
                    return true;
                }

                var assetName = DeviceInfo.Platform == DevicePlatform.WinUI
                    ? "sing-box.exe"
                    : "sing-box";

                Debug.WriteLine($"[SingBox] Распаковка из ресурсов: {assetName}");

                using var assetStream = await FileSystem.OpenAppPackageFileAsync(assetName);
                using var outStream = File.Create(_binaryPath);
                await assetStream.CopyToAsync(outStream);

                _binaryReady = true;
                Debug.WriteLine($"[SingBox] Бинарник распакован: {_binaryPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingBox] НЕ УДАЛОСЬ распаковать бинарник: {ex}");
                return false;
            }
        }

        // =====================================================
        //  ПОДКЛЮЧЕНИЕ
        // =====================================================

        public async Task<bool> ConnectAsync(ServerModel server)
        {
            Debug.WriteLine($"[SingBox] ConnectAsync: {server.Name} ({server.Address}:{server.Port})");

            try
            {
                if (!await EnsureBinaryExtractedAsync())
                {
                    Debug.WriteLine("[SingBox] Бинарник недоступен");
                    return false;
                }

                await DisconnectAsync();

                var config = GenerateConfig(server);

                if (string.IsNullOrEmpty(config) || config[0] != '{')
                {
                    Debug.WriteLine("[SingBox] ❌ Конфиг пустой или не начинается с '{'");
                    return false;
                }

                try
                {
                    if (File.Exists(_configPath))
                        File.Delete(_configPath);
                }
                catch { }

                var bytesNoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(config);
                await File.WriteAllBytesAsync(_configPath, bytesNoBom);
                Debug.WriteLine($"[SingBox] Конфиг записан ({bytesNoBom.Length} байт)");

                bool started = await StartSingBoxAsync();
                if (!started)
                {
                    Debug.WriteLine("[SingBox] Процесс НЕ запустился");
                    return false;
                }

                bool ready = await WaitForReadyAsync(timeoutSeconds: 8);
                Debug.WriteLine($"[SingBox] Готовность: {ready}");

                return ready;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingBox] Ошибка подключения: {ex}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _cts?.Cancel();

                var proc = _singBoxProcess;
                if (proc != null && !proc.HasExited)
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                        await Task.Run(() => proc.WaitForExit(3000));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SingBox] Kill: {ex.Message}");
                    }

                    proc.Dispose();
                    _singBoxProcess = null;
                }

                _prevDownload = 0;
                _prevUpload = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingBox] Disconnect: {ex.Message}");
            }
        }

        // =====================================================
        //  ЗАПУСК
        // =====================================================

        private async Task<bool> StartSingBoxAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    _cts = new CancellationTokenSource();

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = _binaryPath,
                        Arguments = $"run -c \"{_configPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = FileSystem.AppDataDirectory
                    };

                    var proc = new Process { StartInfo = startInfo };

                    proc.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Debug.WriteLine($"[SingBox OUT] {e.Data}");
                            lock (_lastLogs)
                            {
                                _lastLogs.Enqueue($"OUT: {e.Data}");
                                while (_lastLogs.Count > MAX_LOG_LINES) _lastLogs.Dequeue();
                            }
                        }
                    };
                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Debug.WriteLine($"[SingBox ERR] {e.Data}");
                            lock (_lastLogs)
                            {
                                _lastLogs.Enqueue($"ERR: {e.Data}");
                                while (_lastLogs.Count > MAX_LOG_LINES) _lastLogs.Dequeue();
                            }
                        }
                    };

                    bool started = proc.Start();

                    if (started)
                    {
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                    }

                    _singBoxProcess = proc;
                    return started;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SingBox] Не запустился: {ex}");
                    return false;
                }
            });
        }

        private async Task<bool> WaitForReadyAsync(int timeoutSeconds)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSeconds);

            while (DateTime.Now < deadline)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{API_BASE}/version");
                    if (response.IsSuccessStatusCode)
                        return true;
                }
                catch { }

                await Task.Delay(300);
            }

            return false;
        }

        // =====================================================
        //  ГЕНЕРАЦИЯ КОНФИГА
        // =====================================================

        public string GenerateConfig(ServerModel server)
        {
            var outbound = server.Protocol switch
            {
                VpnProtocol.VLESS => BuildVlessOutbound(server),
                VpnProtocol.VMess => BuildVmessOutbound(server),
                VpnProtocol.Shadowsocks => BuildShadowsocksOutbound(server),
                VpnProtocol.Trojan => BuildTrojanOutbound(server),
                VpnProtocol.Hysteria2 => BuildHysteria2Outbound(server),
                _ => throw new NotSupportedException($"Протокол {server.Protocol} не поддерживается")
            };

            var config = new
            {
                log = new
                {
                    level = "info",
                    output = Path.Combine(FileSystem.AppDataDirectory, "singbox.log"),
                    timestamp = true
                },

                experimental = new
                {
                    clash_api = new
                    {
                        external_controller = $"127.0.0.1:{API_PORT}",
                        secret = ""
                    }
                },

                inbounds = new object[]
                {
                    new
                    {
                        type        = "socks",
                        tag         = "socks-in",
                        listen      = "127.0.0.1",
                        listen_port = SOCKS_PORT
                    },
                    new
                    {
                        type        = "http",
                        tag         = "http-in",
                        listen      = "127.0.0.1",
                        listen_port = HTTP_PORT
                    }
                },

                outbounds = new object[]
                {
                    outbound,
                    new { type = "direct", tag = "direct" },
                    new { type = "block",  tag = "block"  }
                },

                route = new
                {
                    final = "proxy",
                    auto_detect_interface = true
                }
            };

            return JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        // =====================================================
        //  OUTBOUND-БИЛДЕРЫ
        // =====================================================

        private static object BuildVlessOutbound(ServerModel s)
        {
            var outbound = new Dictionary<string, object>
            {
                ["type"] = "vless",
                ["tag"] = "proxy",
                ["server"] = s.Address,
                ["server_port"] = s.Port,
                ["uuid"] = s.Uuid
            };

            if (s.Transport == "tcp" || string.IsNullOrEmpty(s.Transport))
            {
                if (!string.IsNullOrEmpty(s.Flow))
                    outbound["flow"] = s.Flow;
                else if (string.IsNullOrEmpty(s.RealityPublicKey))
                    outbound["flow"] = "xtls-rprx-vision";
            }

            if (s.UseTls)
            {
                if (!string.IsNullOrEmpty(s.RealityPublicKey))
                {
                    outbound["tls"] = new Dictionary<string, object>
                    {
                        ["enabled"] = true,
                        ["server_name"] = string.IsNullOrEmpty(s.Sni) ? s.Address : s.Sni,
                        ["utls"] = new
                        {
                            enabled = true,
                            fingerprint = string.IsNullOrEmpty(s.Fingerprint) ? "chrome" : s.Fingerprint
                        },
                        ["reality"] = new
                        {
                            enabled = true,
                            public_key = s.RealityPublicKey,
                            short_id = s.RealityShortId ?? ""
                        }
                    };
                }
                else
                {
                    outbound["tls"] = BuildTlsConfig(s);
                }
            }

            if (s.Transport != "tcp" && !string.IsNullOrEmpty(s.Transport))
                outbound["transport"] = BuildTransportConfig(s);

            return outbound;
        }

        private static object BuildVmessOutbound(ServerModel s)
        {
            var outbound = new Dictionary<string, object>
            {
                ["type"] = "vmess",
                ["tag"] = "proxy",
                ["server"] = s.Address,
                ["server_port"] = s.Port,
                ["uuid"] = s.Uuid,
                ["alter_id"] = s.AlterId,
                ["security"] = "auto"
            };

            if (s.UseTls)
                outbound["tls"] = BuildTlsConfig(s);

            if (s.Transport != "tcp" && !string.IsNullOrEmpty(s.Transport))
                outbound["transport"] = BuildTransportConfig(s);

            return outbound;
        }

        private static object BuildShadowsocksOutbound(ServerModel s)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "shadowsocks",
                ["tag"] = "proxy",
                ["server"] = s.Address,
                ["server_port"] = s.Port,
                ["method"] = s.EncryptionMethod,
                ["password"] = s.Password
            };
        }

        private static object BuildTrojanOutbound(ServerModel s)
        {
            var outbound = new Dictionary<string, object>
            {
                ["type"] = "trojan",
                ["tag"] = "proxy",
                ["server"] = s.Address,
                ["server_port"] = s.Port,
                ["password"] = s.Password,
                ["tls"] = BuildTlsConfig(s)
            };

            if (s.Transport != "tcp" && !string.IsNullOrEmpty(s.Transport))
                outbound["transport"] = BuildTransportConfig(s);

            return outbound;
        }

        private static object BuildHysteria2Outbound(ServerModel s)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "hysteria2",
                ["tag"] = "proxy",
                ["server"] = s.Address,
                ["server_port"] = s.Port,
                ["password"] = s.Password,
                ["tls"] = new
                {
                    enabled = true,
                    server_name = string.IsNullOrEmpty(s.Sni) ? s.Address : s.Sni,
                    insecure = false
                }
            };
        }

        private static object BuildTlsConfig(ServerModel s) => new
        {
            enabled = true,
            server_name = string.IsNullOrEmpty(s.Sni) ? s.Address : s.Sni,
            insecure = false
        };

        private static object BuildTransportConfig(ServerModel s)
        {
            return s.Transport switch
            {
                "ws" => new
                {
                    type = "ws",
                    path = string.IsNullOrEmpty(s.WsPath) ? "/" : s.WsPath,
                    headers = string.IsNullOrEmpty(s.WsHost)
                        ? (object?)null
                        : new { Host = s.WsHost }
                },
                "grpc" => new
                {
                    type = "grpc",
                    service_name = s.GrpcServiceName
                },
                "h2" => new
                {
                    type = "http",
                    host = new[] { s.WsHost },
                    path = s.WsPath
                },
                _ => new { type = s.Transport }
            };
        }

        // =====================================================
        //  ЛОГИ
        // =====================================================

        public string GetLastLogLines(int count)
        {
            lock (_lastLogs)
            {
                if (_lastLogs.Count == 0)
                    return "(лог пуст)";

                var lines = _lastLogs.ToArray();
                var take = Math.Min(count, lines.Length);

                var sb = new StringBuilder();
                for (int i = lines.Length - take; i < lines.Length; i++)
                    sb.AppendLine(lines[i]);

                return sb.ToString();
            }
        }

        // =====================================================
        //  IP-ИНФО
        // =====================================================

        public async Task<(string ip, string country, string city)?> GetIpInfoAsync()
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    Proxy = new System.Net.WebProxy($"socks5://127.0.0.1:{SOCKS_PORT}"),
                    UseProxy = true
                };

                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

                var json = await client.GetStringAsync("https://ipinfo.io/json");

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string ip = root.TryGetProperty("ip", out var ipEl) ? (ipEl.GetString() ?? "") : "";
                string country = root.TryGetProperty("country", out var cEl) ? (cEl.GetString() ?? "") : "";
                string city = root.TryGetProperty("city", out var cityEl) ? (cityEl.GetString() ?? "") : "";

                return (ip, country, city);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingBox] GetIpInfoAsync: {ex.Message}");
                return null;
            }
        }

        // =====================================================
        //  СТАТИСТИКА
        // =====================================================

        public async Task<TrafficStats> GetTrafficStatsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{API_BASE}/traffic");
                if (!response.IsSuccessStatusCode)
                    return new TrafficStats();

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                long download = 0;
                long upload = 0;

                if (doc.RootElement.TryGetProperty("download", out var dl))
                    download = dl.GetInt64();
                if (doc.RootElement.TryGetProperty("upload", out var ul))
                    upload = ul.GetInt64();

                long dlSpeed = Math.Max(0, download - _prevDownload) / 2;
                long ulSpeed = Math.Max(0, upload - _prevUpload) / 2;

                _prevDownload = download;
                _prevUpload = upload;

                return new TrafficStats
                {
                    TotalDownload = download,
                    TotalUpload = upload,
                    DownloadSpeed = dlSpeed,
                    UploadSpeed = ulSpeed
                };
            }
            catch
            {
                return new TrafficStats();
            }
        }

        public bool IsRunning =>
            _singBoxProcess != null && !_singBoxProcess.HasExited;

        public async Task<string> GetVersionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{API_BASE}/version");
                if (!response.IsSuccessStatusCode) return "неизвестно";

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                return doc.RootElement
                    .TryGetProperty("version", out var v)
                    ? (v.GetString() ?? "неизвестно")
                    : "неизвестно";
            }
            catch
            {
                return "неизвестно";
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _httpClient?.Dispose();
            _singBoxProcess?.Dispose();
        }
    }
}