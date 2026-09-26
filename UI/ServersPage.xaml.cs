using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using VPNApp.Models;
using VPNApp.Services;

namespace VPNApp.UI
{
    public partial class ServersPage : ContentPage
    {
        // =====================================================
        //  СОБЫТИЯ
        // =====================================================

        public event EventHandler<ServerModel>? ServerSelected;

        // =====================================================
        //  ПОЛЯ
        // =====================================================

        private readonly SubscriptionService _subscriptionService;
        private readonly List<ServerModel> _allServers = new();

        private ObservableCollection<ServerGroupViewModel> _displayGroups = new();

        private List<SubscriptionGroup> _savedGroups = new();

        private const int PING_CONCURRENCY = 15;
        private const int DNS_TIMEOUT_MS = 2000;
        private const int CONNECT_TIMEOUT_MS = 3000;

        private bool _isPinging = false;

        // Текущий режим сортировки
        private ServerSortMode _sortMode = ServerSortMode.Default;

        // Ключ для Preferences
        private const string PREF_SORT_MODE = "server_sort_mode";

        // =====================================================
        //  КОНСТРУКТОР
        // =====================================================

        public ServersPage()
        {
            InitializeComponent();

            _subscriptionService = new SubscriptionService();

            // Восстанавливаем режим сортировки из настроек
            _sortMode = (ServerSortMode)Preferences.Get(PREF_SORT_MODE, (int)ServerSortMode.Default);

            ServersCollection.ItemsSource = _displayGroups;
            LoadServersAsync();
        }

        // =====================================================
        //  ЗАГРУЗКА
        // =====================================================

        private async void LoadServersAsync()
        {
            try
            {
                var groups = await _subscriptionService.LoadSavedGroupsAsync();

                _savedGroups = groups;
                _allServers.Clear();

                foreach (var group in groups.Where(g => g.IsEnabled))
                {
                    foreach (var server in group.Servers)
                    {
                        server.GroupId = group.Id;
                        _allServers.Add(server);
                    }
                }

                RebuildDisplay();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Ошибка", $"Не удалось загрузить серверы: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Пересобрать UI: применяет текущий фильтр (поиск) и сортировку.
        /// </summary>
        private void RebuildDisplay()
        {
            var query = SearchEntry?.Text?.Trim() ?? "";

            IEnumerable<ServerModel> source = _allServers;
            if (!string.IsNullOrEmpty(query))
            {
                source = source.Where(s =>
                    s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Protocol.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var filtered = source.ToList();

            var newGroups = new List<ServerGroupViewModel>();

            if (!string.IsNullOrEmpty(query))
            {
                // Поиск — плоский список «РЕЗУЛЬТАТЫ ПОИСКА»
                var sorted = ApplySort(filtered).ToList();
                if (sorted.Count > 0)
                    newGroups.Add(new ServerGroupViewModel("РЕЗУЛЬТАТЫ ПОИСКА", sorted));
            }
            else
            {
                // Обычная группировка по подпискам
                foreach (var group in _savedGroups)
                {
                    var serversInGroup = filtered
                        .Where(s => s.GroupId == group.Id)
                        .ToList();

                    if (serversInGroup.Count == 0) continue;

                    serversInGroup = ApplySort(serversInGroup).ToList();

                    newGroups.Add(new ServerGroupViewModel(group.Name.ToUpper(), serversInGroup));
                }
            }

            _displayGroups = new ObservableCollection<ServerGroupViewModel>(newGroups);
            ServersCollection.ItemsSource = _displayGroups;

            ServerCountLabel.Text = string.IsNullOrEmpty(query)
                ? $"{filtered.Count} серверов"
                : $"Найдено: {filtered.Count}";
        }

        // =====================================================
        //  СОРТИРОВКА
        // =====================================================

        private IEnumerable<ServerModel> ApplySort(List<ServerModel> servers)
        {
            return _sortMode switch
            {
                ServerSortMode.ByPing => servers.OrderBy(s => s.Ping == -1 ? int.MaxValue : s.Ping),
                ServerSortMode.ByName => servers.OrderBy(s => s.CleanName, StringComparer.OrdinalIgnoreCase),
                ServerSortMode.ByProtocol => servers.OrderBy(s => s.Protocol.ToString()),
                ServerSortMode.ByCountry => servers.OrderBy(s => s.Country ?? "zzz").ThenBy(s => s.CleanName),
                _ => servers
            };
        }

        private async void OnSortClicked(object? sender, EventArgs e)
        {
            var options = new[]
            {
                "По умолчанию",
                "По задержке ⚡",
                "По имени (А-Я)",
                "По протоколу",
                "По стране 🌍"
            };

            var picked = await DisplayActionSheetAsync("Сортировка серверов", "Отмена", null, options);

            if (picked == null || picked == "Отмена") return;

            _sortMode = picked switch
            {
                "По задержке ⚡" => ServerSortMode.ByPing,
                "По имени (А-Я)" => ServerSortMode.ByName,
                "По протоколу" => ServerSortMode.ByProtocol,
                "По стране 🌍" => ServerSortMode.ByCountry,
                _ => ServerSortMode.Default
            };

            // Сохраняем выбор
            Preferences.Set(PREF_SORT_MODE, (int)_sortMode);

            RebuildDisplay();
        }

        // =====================================================
        //  ПОИСК
        // =====================================================

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            RebuildDisplay();
        }

        // =====================================================
        //  ВЫБОР СЕРВЕРА
        // =====================================================

        private async void OnServerSelected(object? sender, SelectionChangedEventArgs e)
        {
            var selected = e.CurrentSelection.Count > 0
                ? e.CurrentSelection[0] as ServerModel
                : null;

            if (selected is null)
                return;

            ServersCollection.SelectedItem = null;

            ServerSelected?.Invoke(this, selected);

            await Navigation.PopAsync();
        }

        // =====================================================
        //  ПИНГ
        // =====================================================

        private async void OnPingAllClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_isPinging)
                {
                    await DisplayAlertAsync("Подожди", "Проверка пинга уже идёт.", "OK");
                    return;
                }

                if (_allServers.Count == 0)
                {
                    await DisplayAlertAsync("Нет серверов", "Сначала добавьте подписку", "OK");
                    return;
                }

                _isPinging = true;
                int total = _allServers.Count;
                int done = 0;

                ServerCountLabel.Text = $"Проверка пинга... 0/{total}";

                using var semaphore = new SemaphoreSlim(PING_CONCURRENCY);

                var tasks = _allServers.Select(async server =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await PingServerAsync(server);

                        int current = Interlocked.Increment(ref done);

                        if (current % 5 == 0 || current == total)
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                ServerCountLabel.Text = $"Проверка пинга... {current}/{total}";
                            });
                        }
                    }
                    catch
                    {
                        server.Ping = -1;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);

                // ─────────────────────────────────────────────────
                // ✨ ГЛАВНОЕ: сохраняем обновлённые пинги на диск.
                // Теперь при возврате на страницу пинги не сбросятся.
                // ─────────────────────────────────────────────────
                try
                {
                    await _subscriptionService.SaveGroupsAsync(_savedGroups);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Ping] Не удалось сохранить пинги: {ex.Message}");
                }

                // Если включена сортировка по пингу — обновим порядок
                if (_sortMode == ServerSortMode.ByPing)
                {
                    RebuildDisplay();
                }
                else
                {
                    int available = _allServers.Count(s => s.Ping > 0);
                    ServerCountLabel.Text = $"{available} доступно из {total}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Ping] КРИТИЧЕСКАЯ ОШИБКА: {ex}");
                try
                {
                    await DisplayAlertAsync("Ошибка пинга",
                        $"{ex.GetType().Name}: {ex.Message}", "OK");
                }
                catch { }
            }
            finally
            {
                _isPinging = false;
            }
        }

        private static async Task PingServerAsync(ServerModel server)
        {
            server.Ping = -1;

            try
            {
                IPAddress? ipAddress;

                if (IPAddress.TryParse(server.Address, out var directIp))
                {
                    ipAddress = directIp;
                }
                else
                {
                    ipAddress = await ResolveDnsAsync(server.Address, DNS_TIMEOUT_MS);
                    if (ipAddress == null) return;
                }

                var port = server.Port;
                if (port <= 0 || port > 65535) return;

                using var client = new TcpClient();
                var sw = Stopwatch.StartNew();

                var connectTask = client.ConnectAsync(ipAddress, port);
                var timeoutTask = Task.Delay(CONNECT_TIMEOUT_MS);

                var completed = await Task.WhenAny(connectTask, timeoutTask);
                sw.Stop();

                if (completed == connectTask && client.Connected)
                {
                    try
                    {
                        await connectTask;
                        server.Ping = (int)sw.ElapsedMilliseconds;
                    }
                    catch
                    {
                        server.Ping = -1;
                    }
                }
            }
            catch
            {
                server.Ping = -1;
            }
        }

        private static async Task<IPAddress?> ResolveDnsAsync(string host, int timeoutMs)
        {
            try
            {
                var dnsTask = Dns.GetHostAddressesAsync(host);
                var timeoutTask = Task.Delay(timeoutMs);

                var completed = await Task.WhenAny(dnsTask, timeoutTask);
                if (completed != dnsTask) return null;

                var addresses = await dnsTask;
                return addresses.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // =====================================================
        //  НАВИГАЦИЯ
        // =====================================================

        private async void OnBackClicked(object? sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }

    // =====================================================
    //  РЕЖИМЫ СОРТИРОВКИ
    // =====================================================

    public enum ServerSortMode
    {
        Default,
        ByPing,
        ByName,
        ByProtocol,
        ByCountry
    }

    // =====================================================
    //  МОДЕЛЬ ГРУППЫ
    // =====================================================

    public partial class ServerGroupViewModel : List<ServerModel>
    {
        public string Name { get; }
        public int ServerCount => Count;

        public ServerGroupViewModel(string name, IEnumerable<ServerModel> items) : base(items)
        {
            Name = name;
        }
    }
}