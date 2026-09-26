using Microsoft.Extensions.Logging;
using VPNApp.Services;

namespace VPNApp
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    // Шрифты приложения
                    fonts.AddFont("OpenSans-Regular.ttf",    "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf",   "OpenSansSemibold");
                });

            // ── Регистрация сервисов ───────────────────────────
            // Singleton — один экземпляр на всё приложение
            builder.Services.AddSingleton<SingBoxService>();
            builder.Services.AddSingleton<SubscriptionService>();

            // ── Логирование (только в режиме отладки) ─────────
#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
