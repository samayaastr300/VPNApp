namespace VPNApp
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        protected override void OnStart() => base.OnStart();
        protected override void OnSleep() => base.OnSleep();
        protected override void OnResume() => base.OnResume();
    }
}