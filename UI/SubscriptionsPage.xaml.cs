using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using VPNApp.Models;
using VPNApp.Services;

namespace VPNApp.UI
{
    public partial class SubscriptionsPage : ContentPage
    {
        private readonly SubscriptionService _service;
        private readonly ObservableCollection<SubscriptionGroup> _groups;

        public SubscriptionsPage()
        {
            InitializeComponent();
            _service = new SubscriptionService();
            _groups = new ObservableCollection<SubscriptionGroup>();
            GroupsCollection.ItemsSource = _groups;

            // Fire-and-forget: не ждём в конструкторе, но компилятор доволен
            _ = LoadGroupsAsync();
        }

        private async Task LoadGroupsAsync()
        {
            var saved = await _service.LoadSavedGroupsAsync();
            _groups.Clear();
            foreach (var g in saved) _groups.Add(g);
        }

        // ── Добавить подписку (по URL) ─────────────────────

        private async void OnAddClicked(object? s, EventArgs e)
        {
            string url = await DisplayPromptAsync(
                "Добавить подписку",
                "Вставьте URL подписки:",
                placeholder: "https://example.com/sub",
                keyboard: Keyboard.Url);

            if (string.IsNullOrWhiteSpace(url)) return;

            string name = await DisplayPromptAsync(
                "Название группы",
                "Введите название:",
                placeholder: "Моя группа");

            if (string.IsNullOrWhiteSpace(name)) return;

            var loading = new LoadingPage("Загрузка серверов...");
            await Navigation.PushModalAsync(loading);

            SubscriptionGroup? group = null;
            string? errorMessage = null;

            try
            {
                group = await _service.AddSubscriptionAsync(url, name);
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
                _groups.Add(group);
                await _service.SaveGroupsAsync(new List<SubscriptionGroup>(_groups));
                await DisplayAlertAsync("Успешно", $"Добавлено {group.ServerCount} серверов", "OK");
            }
            else
            {
                await DisplayAlertAsync("Ошибка", "Не удалось загрузить серверы. Проверьте URL.", "OK");
            }
        }

        // ── Добавить конфиг вручную ────────────────────────

        private async void OnAddManualClicked(object? s, EventArgs e)
        {
            string link = await DisplayPromptAsync(
                "Добавить конфиг",
                "Вставьте ссылку (vless://, vmess://, ss://, trojan://, hy2://):",
                placeholder: "vless://...",
                keyboard: Keyboard.Url);

            if (string.IsNullOrWhiteSpace(link)) return;

            var loading = new LoadingPage("Добавление сервера...");
            await Navigation.PushModalAsync(loading);

            SubscriptionGroup? group = null;
            string? errorMessage = null;

            try
            {
                group = await _service.AddManualServerAsync(link);
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
                await LoadGroupsAsync();
                await DisplayAlertAsync("Успешно",
                    $"Сервер добавлен в группу «{group.Name}».\n" +
                    $"Всего в группе: {group.ServerCount}",
                    "OK");
            }
            else
            {
                await DisplayAlertAsync("Ошибка",
                    "Не удалось распарсить ссылку.\n" +
                    "Поддерживаются: vless://, vmess://, ss://, trojan://, hy2://",
                    "OK");
            }
        }

        // ── Обновить группу ────────────────────────────────

        private async void OnUpdateGroupClicked(object? s, TappedEventArgs e)
        {
            if (e.Parameter is not SubscriptionGroup group) return;

            // Ручные конфиги не обновляются
            if (string.IsNullOrEmpty(group.Url))
            {
                await DisplayAlertAsync("Нечего обновлять",
                    "Группа «📌 Мои конфиги» содержит добавленные вручную конфиги — она не обновляется.",
                    "OK");
                return;
            }

            var loading = new LoadingPage("Обновление серверов...");
            await Navigation.PushModalAsync(loading);

            SubscriptionGroup? updated = null;
            string? errorMessage = null;

            try
            {
                updated = await _service.UpdateGroupAsync(group);
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

            if (updated != null)
            {
                var idx = _groups.IndexOf(group);
                if (idx >= 0) _groups[idx] = updated;

                await _service.SaveGroupsAsync(new List<SubscriptionGroup>(_groups));
                await DisplayAlertAsync("Обновлено",
                    $"Загружено {updated.ServerCount} серверов", "OK");
            }
        }

        // ── Удалить группу ─────────────────────────────────

        private async void OnDeleteGroupClicked(object? s, TappedEventArgs e)
        {
            if (e.Parameter is not SubscriptionGroup group) return;

            bool confirm = await DisplayAlertAsync(
                "Удалить группу",
                $"Удалить «{group.Name}» и все её серверы?",
                "Удалить", "Отмена");

            if (!confirm) return;

            _groups.Remove(group);
            await _service.SaveGroupsAsync(new List<SubscriptionGroup>(_groups));
        }

        private async void OnBackClicked(object? s, EventArgs e)
            => await Navigation.PopAsync();
    }
}