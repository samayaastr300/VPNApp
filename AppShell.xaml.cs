namespace VPNApp
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Здесь НЕ регистрируем MainPage — он уже объявлен как ShellContent
            // в AppShell.xaml с Route="MainPage".
            //
            // Регистрация маршрутов нужна только если ты используешь
            // Shell.Current.GoToAsync("route"). У нас везде PushAsync — не нужно.
        }
    }
}