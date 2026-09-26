using Microsoft.Maui.Controls;
using VPNApp.Models;

namespace VPNApp.UI
{
    public partial class SettingsPage : ContentPage
    {
        private RoutingMode _selectedRouting = RoutingMode.Proxy;
        private int _updateInterval = 24;

        public SettingsPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            _selectedRouting = (RoutingMode)Preferences.Get("routing_mode", (int)RoutingMode.Proxy);
            KillSwitchToggle.IsToggled = Preferences.Get("kill_switch", false);
            DnsEntry.Text = Preferences.Get("dns_server", "8.8.8.8");
            _updateInterval = Preferences.Get("update_interval", 24);
            IntervalLabel.Text = $"{_updateInterval} часа(ов)";
            AutoUpdateToggle.IsToggled = Preferences.Get("auto_update", true);

            UpdateRoutingUI();
        }

        private void OnRoutingProxyClicked(object? s, EventArgs e)
        {
            _selectedRouting = RoutingMode.Proxy;
            UpdateRoutingUI();
        }

        private void OnRoutingGlobalClicked(object? s, EventArgs e)
        {
            _selectedRouting = RoutingMode.Global;
            UpdateRoutingUI();
        }

        private void OnRoutingDirectClicked(object? s, EventArgs e)
        {
            _selectedRouting = RoutingMode.Direct;
            UpdateRoutingUI();
        }

        private void UpdateRoutingUI()
        {
            RoutingProxyBtn.Stroke = _selectedRouting == RoutingMode.Proxy
                ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#2A2A2A");
            RoutingGlobalBtn.Stroke = _selectedRouting == RoutingMode.Global
                ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#2A2A2A");
            RoutingDirectBtn.Stroke = _selectedRouting == RoutingMode.Direct
                ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#2A2A2A");

            RoutingProxyCheck.IsVisible = _selectedRouting == RoutingMode.Proxy;
            RoutingGlobalCheck.IsVisible = _selectedRouting == RoutingMode.Global;
            RoutingDirectCheck.IsVisible = _selectedRouting == RoutingMode.Direct;
        }

        private async void OnIntervalClicked(object? s, EventArgs e)
        {
            var options = new[] { "6 часов", "12 часов", "24 часа", "48 часов" };
            var picked = await DisplayActionSheetAsync("Интервал обновления", "Отмена", null, options);

            if (picked == null || picked == "Отмена") return;

            _updateInterval = picked switch
            {
                "6 часов" => 6,
                "12 часов" => 12,
                "48 часов" => 48,
                _ => 24
            };

            IntervalLabel.Text = picked;
        }

        private void OnKillSwitchToggled(object? s, ToggledEventArgs e) { }

        private async void OnSaveClicked(object? s, EventArgs e)
        {
            Preferences.Set("routing_mode", (int)_selectedRouting);
            Preferences.Set("kill_switch", KillSwitchToggle.IsToggled);
            Preferences.Set("dns_server", DnsEntry.Text ?? "8.8.8.8");
            Preferences.Set("update_interval", _updateInterval);
            Preferences.Set("auto_update", AutoUpdateToggle.IsToggled);

            await DisplayAlertAsync("Сохранено", "Настройки применены!", "OK");
            await Navigation.PopAsync();
        }

        private async void OnBackClicked(object? s, EventArgs e)
            => await Navigation.PopAsync();
    }
}