using Microsoft.Maui.Controls;

namespace VPNApp.UI
{
    /// <summary>
    /// Простая модальная страница-заглушка с индикатором загрузки.
    /// Вынесена в отдельный файл, чтобы Shell корректно регистрировал route.
    /// </summary>
    public partial class LoadingPage : ContentPage
    {
        public LoadingPage(string message = "Загрузка...")
        {
            InitializeComponent();
            MessageLabel.Text = message;
        }
    }
}