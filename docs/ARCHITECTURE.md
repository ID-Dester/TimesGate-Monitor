# Архитектура milestone 2

## Поток данных

```text
LibreHardwareMonitor
   ↓
IHardwareMonitor ──> HardwareSnapshot ──> load smoothing
                                            ↓
                       5 × IHardwareScreenRenderer
                                            ↓
                              5 × DisplayFrame(JPEG)
                                            ↓
ITimesGateService ─────────────────> Times Gate LAN API
```

## Зачем интерфейсы

Интерфейс описывает, **что** умеет компонент, не раскрывая, **как** он это делает. `IHardwareMonitor` отделяет LibreHardwareMonitor от модели приложения, а `ITimesGateService` позволяет будущей ViewModel отправлять кадры, не зная JSON, endpoint и правила `PicID`. Это уменьшает связанность и локализует изменения.

Альтернатива — вызывать `HttpClient` из окна WPF. Такой вариант короче в первые полчаса, но затем сетевые ошибки, reconnect, UI thread и настройки оказываются смешаны в `MainWindow.xaml.cs`. Его трудно тестировать и расширять.

## Dependency Injection и lifetime

Регистрация находится в `TimesGateMonitor.Diagnostic/Program.cs` — это **composition root**, место сборки приложения:

- renderers — Singleton: они не хранят состояние и безопасно переиспользуются;
- `IHardwareMonitor` — Singleton: один объект владеет одним открытым LibreHardwareMonitor `Computer`;
- typed `HttpClient`/`ITimesGateService` — создаётся DI, а внутренние handlers переиспользуются `IHttpClientFactory`;
- logging — один общий provider.

Новый `HttpClient` на каждый кадр не создаётся: частое создание handlers может исчерпать сокеты и игнорирует DNS lifecycle. `IHttpClientFactory` управляет этим централизованно.

Scoped lifetime не нужен: desktop/CLI не обрабатывает независимые web requests. Сессией владеет composition root, а `IHardwareMonitor.Dispose` закрывает native/hardware-ресурсы.

## Почему async и CancellationToken

HTTP — ожидание внешнего устройства. `async` освобождает поток на время ожидания, а `CancellationToken` позволяет будущей кнопке Pause, tray Exit или закрытию приложения остановить операцию без зависания. Это особенно важно для WPF: UI thread должен оставаться свободным для перерисовки и ввода.

## Почему renderer отделён от сети

Renderer переводит модель состояния в изображение. LAN service переводит изображение в протокол устройства. Такое разделение даёт:

- один и тот же JPEG для preview и часов;
- unit tests renderer без устройства;
- замену WPF renderer на SkiaSharp без изменения сети;
- замену Times Gate transport без изменения дизайна.

Аппаратная панель реализована как пять `IHardwareScreenRenderer`, принимающих только `HardwareSnapshot`. Диагностический renderer остаётся отдельным инструментом проверки LAN. Позже один screen renderer можно заменить компоновщиком списка `Widget`, не затрагивая hardware provider или transport.

## Change detection

Перед отправкой вычисляется SHA-256 JPEG-байтов отдельно для каждого LCD. Совпадающий hash означает, что повторный POST не нужен.

Плюсы: меньше LAN-трафика, меньше работы device decoder, меньше мерцания. Минусы: вычисление hash стоит немного CPU, а lossy encoder может породить разные байты для визуально одинакового кадра. Для 128×128 это малая цена; позже можно сравнить bitmap до JPEG, если измерения покажут пользу.

Hash запоминается только после успешного ответа. Поэтому сетевой сбой не превращает непринятый кадр в «уже отправленный».

## Что начинающему посмотреть в первую очередь

1. `ITimesGateService.cs` — небольшой контракт без деталей HTTP.
2. `TimesGateService.SendFrameAsync` — one-hot `LcdArray`, hash, Base64 и сериализация команды.
3. `DiagnosticFrameRenderer.cs` — `DrawingVisual`, immutable/frozen brushes и освобождение временного `MemoryStream` через `using`.
4. `HardwareSnapshotMapper.cs` — превращение множества vendor-specific sensors в стабильную модель.
5. `Program.cs` — `PeriodicTimer`, `CancellationToken` и Dependency Injection.
6. `HardwareDashboardRendererTests.cs` — проверка renderer без реального устройства.

Unit test здесь подменяет HTTP handler и проверяет JSON детерминированно. Маленький package-free runner сохраняет тесты независимыми от тестового framework; при желании их можно механически перенести в xUnit.

Integration test требует физический Times Gate, LAN, firmware и token; его нельзя сделать надёжным обычным unit test прогоном CI.
