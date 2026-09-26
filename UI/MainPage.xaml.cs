using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using Microsoft.Maui.Controls;
using VPNApp.Models;
using VPNApp.Services;

namespace VPNApp.UI
{
    public partial class MainPage : ContentPage
    {
        // =====================================================
        //  ПОЛЯ
        // =====================================================

        private ConnectionState _currentState = ConnectionState.Disconnected;

        private System.Timers.Timer _connectionTimer = null!;
        private System.Timers.Timer _trafficTimer = null!;

        private TimeSpan _connectionDuration = TimeSpan.Zero;

        private ServerModel? _selectedServer;

        private readonly ObservableCollection<SubscriptionGroup> _subscriptions;

        private readonly SubscriptionService _subscriptionService;
        private readonly SingBoxService _singBoxService;

        private const string PREF_LAST_SERVER_ID = "last_server_id";
        private const string PREF_LAST_UPDATE = "last_update";

        // =====================================================
        //  КОНСТРУКТОР
        // =====================================================

        public MainPage()
        {
            InitializeComponent();

            _subscriptionService = new SubscriptionService();
            _singBoxService = new SingBoxService();

            _subscriptions = new ObservableCollection<SubscriptionGroup>();
            SubscriptionsCollection.ItemsSource = _subscriptions;

            InitTimers();

            LoadDataOnStart();
        }

        // =====================================================
        //  ИНИЦИАЛИЗАЦИЯ
        // =====================================================

        private void InitTimers()
        {
            _connectionTimer = new System.Timers.Timer(1000);
            _connectionTimer.Elapsed += OnConnectionTimerTick;
            _connectionTimer.AutoReset = true;

            _trafficTimer = new System.Timers.Timer(2000);
            _trafficTimer.Elapsed += OnTrafficTimerTick;
            _trafficTimer.AutoReset = true;
        }

        private async void LoadDataOnStart()
        {
            try
            {
                var savedGroups = await _subscriptionService.LoadSavedGroupsAsync();
                foreach (var group in savedGroups)
                    _subscriptions.Add(group);

                var lastUpdate = Preferences.Get(PREF_LAST_UPDATE, "никогда");
                LastUpdateLabel.Text = $"Последнее: {lastUpdate}";

                var lastServerId = Preferences.Get(PREF_LAST_SERVER_ID, string.Empty);
                if (!string.IsNullOrEmpty(lastServerId))
                {
                    var server = savedGroups
                        .SelectMany(g => g.Servers)
                        .FirstOrDefault(s => s.Id == lastServerId);

                    if (server != null)
                    {
                        _selectedServer = server;
                        ApplyServerToUi(server);
                        Debug.WriteLine($"[Main] Восстановлен сервер: {server.Name}");
                    }
                    else
                    {
                        Debug.WriteLine($"[Main] Сервер с ID {lastServerId} не найден");
                        ResetServerUi();
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Ошибка", $"Не удалось загрузить данные: {ex.Message}", "OK");
            }
        }

        private void ApplyServerToUi(ServerModel server)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ServerNameLabel.Text = server.CleanName;
                ProtocolLabel.Text = server.ProtocolDisplay;
                PingLabel.Text = server.Ping > 0 ? $"{server.Ping} ms" : "— ms";
                ServerFlagLabel.Text = server.FlagEmoji;
            });
        }

        private void ResetServerUi()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ServerNameLabel.Text = "Выберите сервер";
                ProtocolLabel.Text = "—";
                PingLabel.Text = "— ms";
                ServerFlagLabel.Text = "🌐";
            });
        }

        // =====================================================
        //  КНОПКА ПОДКЛЮЧЕНИЯ
        // =====================================================

        private async void OnConnectClicked(object? sender, EventArgs e)
        {
            switch (_currentState)
            {
                case ConnectionState.Disconnected:
                    await StartConnection();
                    break;

                case ConnectionState.Connected:
                    await StopConnection();
                    break;

                case ConnectionState.Connecting:
                    await CancelConnection();
                    break;
            }
        }

        private async Task StartConnection()
        {
            if (_selectedServer == null)
            {
                bool pick = await DisplayAlertAsync(
                    "Сервер не выбран",
                    "Сначала выберите сервер из списка.",
                    "Выбрать", "Отмена");

                if (pick)
                    await Navigation.PushAsync(new ServersPage());
                return;
            }

            Debug.WriteLine($"[Main] Начинаю подключение к {_selectedServer.Name}");

            SetState(ConnectionState.Connecting);

            try
            {
                bool success = await _singBoxService.ConnectAsync(_selectedServer);

                if (success)
                {
                    SetState(ConnectionState.Connected);

                    // Запускаем проверку IP в фоне
                    _ = UpdateIpInfoAsync();
                }
                else
                {
                    SetState(ConnectionState.Disconnected);
                    var logTail = _singBoxService.GetLastLogLines(5);
                    await DisplayAlertAsync("Ошибка подключения",
                        "Не удалось подключиться к серверу.\n\n" +
                        "Последние строки лога:\n" + logTail,
                        "OK");
                }
            }
            catch (Exception ex)
            {
                SetState(ConnectionState.Disconnected);
                await DisplayAlertAsync("Ошибка", ex.Message, "OK");
            }
        }

        private async Task StopConnection()
        {
            bool confirm = await DisplayAlertAsync("Отключение",
                "Вы уверены что хотите отключиться?",
                "Да", "Нет");

            if (!confirm) return;

            try
            {
                await _singBoxService.DisconnectAsync();
                SetState(ConnectionState.Disconnected);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Ошибка", ex.Message, "OK");
            }
        }

        private async Task CancelConnection()
        {
            await _singBoxService.DisconnectAsync();
            SetState(ConnectionState.Disconnected);
        }

        // =====================================================
        //  УПРАВЛЕНИЕ СОСТОЯНИЕМ
        // =====================================================

        private void SetState(ConnectionState state)
        {
            _currentState = state;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                switch (state)
                {
                    case ConnectionState.Disconnected:
                        StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#FF3B3B"));
                        StatusLabel.Text = "НЕ ПОДКЛЮЧЕНО";
                        StatusLabel.TextColor = Color.FromArgb("#FF3B3B");
                        ConnectBtnLabel.Text = "ПОДКЛЮЧИТЬ";
                        ConnectButton.Stroke = Color.FromArgb("#FFFFFF");
                        TimerLabel.Text = "00:00:00";
                        _connectionTimer.Stop();
                        _trafficTimer.Stop();
                        _connectionDuration = TimeSpan.Zero;

                        IpInfoLabel.IsVisible = false;
                        IpInfoLabel.Text = "";

                        ResetTrafficLabels();
                        break;

                    case ConnectionState.Connecting:
                        StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#FFD700"));
                        StatusLabel.Text = "ПОДКЛЮЧЕНИЕ...";
                        StatusLabel.TextColor = Color.FromArgb("#FFD700");
                        ConnectBtnLabel.Text = "ОТМЕНА";
                        ConnectButton.Stroke = Color.FromArgb("#FFD700");
                        break;

                    case ConnectionState.Connected:
                        StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#00FF88"));
                        StatusLabel.Text = "ПОДКЛЮЧЕНО";
                        StatusLabel.TextColor = Color.FromArgb("#00FF88");
                        ConnectBtnLabel.Text = "ОТКЛЮЧИТЬ";
                        ConnectButton.Stroke = Color.FromArgb("#00FF88");
                        _connectionTimer.Start();
                        _trafficTimer.Start();
                        break;
                }
            });
        }

        private void ResetTrafficLabels()
        {
            DownloadLabel.Text = "0 B";
            UploadLabel.Text = "0 B";
            DownloadSpeedLabel.Text = "0 B/s";
            UploadSpeedLabel.Text = "0 B/s";
        }

        // =====================================================
        //  IP-ИНФО
        // =====================================================

        private async Task UpdateIpInfoAsync()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IpInfoLabel.Text = "🔍 Проверка IP...";
                    IpInfoLabel.IsVisible = true;
                });

                var info = await _singBoxService.GetIpInfoAsync();

                if (info == null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        IpInfoLabel.Text = "не удалось определить IP";
                    });
                    return;
                }

                var (ip, country, city) = info.Value;
                var flag = CountryCodeToFlag(country);

                var text = string.IsNullOrEmpty(city)
                    ? $"{flag} {ip} · {country}"
                    : $"{flag} {ip} · {city}, {country}";

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IpInfoLabel.Text = text;
                });

                Debug.WriteLine($"[Main] IP info: {text}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Main] UpdateIpInfoAsync: {ex.Message}");
            }
        }

        private static string CountryCodeToFlag(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 2)
                return "🌐";

            code = code.ToUpperInvariant();
            const int baseCode = 0x1F1E6 - 'A';
            return char.ConvertFromUtf32(baseCode + code[0])
                 + char.ConvertFromUtf32(baseCode + code[1]);
        }

        // =====================================================
        //  ТАЙМЕРЫ
        // =====================================================

        private void OnConnectionTimerTick(object? sender, ElapsedEventArgs e)
        {
            _connectionDuration = _connectionDuration.Add(TimeSpan.FromSeconds(1));

            MainThread.BeginInvokeOnMainThread(() =>
            {
                TimerLabel.Text = _connectionDuration.ToString(@"hh\:mm\:ss");
            });
        }

        private async void OnTrafficTimerTick(object? sender, ElapsedEventArgs e)
        {
            try
            {
                var stats = await _singBoxService.GetTrafficStatsAsync();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DownloadLabel.Text = FormatBytes(stats.TotalDownload);
                    UploadLabel.Text = FormatBytes(stats.TotalUpload);
                    DownloadSpeedLabel.Text = $"{FormatBytes(stats.DownloadSpeed)}/s";
                    UploadSpeedLabel.Text = $"{FormatBytes(stats.UploadSpeed)}/s";
                });
            }
            catch { }
        }

        // =====================================================
        //  ВЫБОР СЕРВЕРА
        // =====================================================

        private async void OnChangeServerClicked(object? sender, EventArgs e)
        {
            var serversPage = new ServersPage();
            serversPage.ServerSelected += OnServerSelected;
            await Navigation.PushAsync(serversPage);
        }

        private void OnServerSelected(object? sender, ServerModel server)
        {
            _selectedServer = server;

            ApplyServerToUi(server);

            Preferences.Set(PREF_LAST_SERVER_ID, server.Id);

            Debug.WriteLine($"[Main] Сервер выбран: {server.Name}, ID={server.Id}");
        }

        // =====================================================
        //  ПОДПИСКИ
        // =====================================================

        private async void OnAddSubscriptionClicked(object? sender, EventArgs e)
        {
            string url = await DisplayPromptAsync(
                "Добавить подписку",
                "Вставьте URL подписки:",
                placeholder: "https://example.com/subscription",
                keyboard: Keyboard.Url);

            if (string.IsNullOrWhiteSpace(url)) return;

            string groupName = await DisplayPromptAsync(
                "Название группы",
                "Введите название для этой группы серверов:",
                placeholder: "Моя группа");

            if (string.IsNullOrWhiteSpace(groupName)) return;

            var loadingPage = new LoadingPage("Загрузка серверов...");
            await Navigation.PushModalAsync(loadingPage);

            SubscriptionGroup? group = null;
            string? errorMessage = null;

            try
            {
                group = await _subscriptionService.AddSubscriptionAsync(url, groupName);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }
            finally
            {
                await Navigation.PopModalAsync();
            }

            if (errorMessage != null)
            {
                await DisplayAlertAsync("Ошибка", errorMessage, "OK");
                return;
            }

            if (group != null)
            {
                _subscriptions.Add(group);

                var now = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                LastUpdateLabel.Text = $"Последнее: {now}";
                Preferences.Set(PREF_LAST_UPDATE, now);

                await DisplayAlertAsync("Успешно",
                    $"Добавлено {group.ServerCount} серверов в группу «{groupName}»",
                    "OK");
            }
            else
            {
                await DisplayAlertAsync("Ошибка",
                    "Не удалось загрузить серверы по этому URL. Проверьте ссылку.",
                    "OK");
            }
        }

        // =====================================================
        //  НАВИГАЦИЯ
        // =====================================================

        private async void OnServersClicked(object? sender, EventArgs e)
            => await Navigation.PushAsync(new ServersPage());

        private async void OnSettingsClicked(object? sender, EventArgs e)
            => await Navigation.PushAsync(new SettingsPage());

        private void OnNavHomeClicked(object? sender, EventArgs e) { }

        private async void OnNavServersClicked(object? sender, EventArgs e)
            => await Navigation.PushAsync(new ServersPage());

        private async void OnNavSubscriptionsClicked(object? sender, EventArgs e)
            => await Navigation.PushAsync(new SubscriptionsPage());

        private async void OnNavSettingsClicked(object? sender, EventArgs e)
            => await Navigation.PushAsync(new SettingsPage());

        // =====================================================
        //  ВСПОМОГАТЕЛЬНЫЕ
        // =====================================================

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            else if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            else if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            else return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            if (_subscriptions.Count == 0)
                LastUpdateLabel.Text = "Добавьте подписку для начала работы";
        }

        protected override void OnDisappearing() => base.OnDisappearing();
        protected override void OnNavigatedFrom(NavigatedFromEventArgs args) => base.OnNavigatedFrom(args);
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }
}