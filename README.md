# VPNApp

VPN-клиент на .NET MAUI 10.

## Требования для запуска

1. Visual Studio 2022 (17.12+)
2. .NET 10 SDK
3. **sing-box.exe** — скачать с https://github.com/SAGernet/sing-box/releases
   и положить в `Resources/Raw/` (в репозитории его нет — 50 МБ)

## Запуск

1. Клонировать репозиторий
2. Открыть `VPNApp.sln`
3. `F5`

## Структура

- `Models/` — модели данных
- `Services/` — SingBoxService, SubscriptionService
- `UI/` — страницы MAUI
