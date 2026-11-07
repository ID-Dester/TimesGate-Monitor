# Milestone 2: аппаратный мониторинг

> Исторический документ. Начиная с этапа 3 FPS исключён из пользовательских экранов и заменён локальным временем.

## Что сделано

`LibreHardwareMonitorService` открывает один экземпляр `Computer`, обновляет дерево устройств и переводит vendor-specific sensors в нейтральные `HardwareSensorSample`. `HardwareSnapshotMapper` затем формирует immutable `HardwareSnapshot` для renderer-ов.

Пять renderer-ов получают только snapshot и возвращают JPEG 128×128:

- экран 1 — CPU load, temperature, clock, power;
- экран 2 — GPU load, temperature, clock, VRAM, power;
- экран 3 — центральная сводка CPU/GPU;
- экран 4 — RAM load, used/total/available;
- экран 5 — FPS, uptime и температуры.

FPS пока закономерно показан как `--`: provider вынесен в отдельный milestone, чтобы необязательная интеграция не блокировала рабочий мониторинг.

## Почему слои разделены

`HardwareSnapshot` — это DTO состояния: renderer не знает ни LibreHardwareMonitor, ни имён конкретных сенсоров. Благодаря `IHardwareMonitor` источник позже можно заменить, объединить с WMI или дополнить fallback-ами без переписывания дизайна экранов.

Каждый экран имеет собственную реализацию `IHardwareScreenRenderer`. Сейчас реализации фиксированные. Будущая widget system сможет реализовать тот же контракт и внутри пройти по `Screen -> Widget[]`; сеть и сбор hardware snapshot при этом не изменятся.

## Polling, сглаживание и отправка

Датчики читаются каждые 500 мс через `PeriodicTimer`. Это асинхронное ожидание и оно не крутит CPU в busy loop. Изображения отправляются независимо с выбранным периодом 500/1000/2000/5000 мс. `CancellationToken` связывает Ctrl+C с корректной остановкой ожидания и HTTP-запросов.

Для CPU/GPU/RAM load применяется exponential moving average:

```text
display = previous × 0.7 + current × 0.3
```

Первый sample проходит без задержки. Температуры не сглаживаются: thresholds должны реагировать на актуальное значение. Hash каждого JPEG по-прежнему предотвращает повторную отправку неизменившегося экрана.

## Отсутствующие датчики

Nullable `double?` выбран сознательно: `null` означает «датчик недоступен», а `0` может быть реальным показанием. На проверенном Ryzen LibreHardwareMonitor без повышения прав возвращает нули для температуры, clock и package power; mapper превращает такие невозможные нули в `null`, и экран честно показывает `--`.

Диагностика Ryzen 7 9800X3D подтвердила конкретную причину: PawnIO 2.2.0 установлен и driver запущен, но не-elevated процесс получает Win32 error 5 (`Access denied`) при открытии `\\?\GLOBALROOT\Device\PawnIO`. Поэтому опубликованный EXE использует `requireAdministrator` manifest. При недоступных CPU temperature/power сервис также пишет версию PawnIO и состояние повышения в warning event 2004.

GPU NVIDIA и физическая RAM читаются полностью. При нескольких GPU mapper отдаёт приоритет дискретной NVIDIA/AMD. Для RAM отдельно исключена virtual memory: выбирается устройство `/ram`, а total вычисляется как used + available.

Запуск от администратора может открыть дополнительные CPU sensors, но приложение не требует elevation и не падает без них.

## Что изучить

1. `IHardwareMonitor.cs` и `HardwareSnapshot.cs` — dependency inversion и nullable measurements.
2. `LibreHardwareMonitorService.cs` — управление lifetime внешней библиотеки через `IDisposable`.
3. `HardwareSnapshotMapper.cs` — чистое и unit-testable преобразование данных.
4. `HardwareSnapshotSmoother.cs` — stateful service с простым математическим правилом.
5. `HardwareScreenRendererBase.cs` и пять renderer-ов — общий pipeline и отдельная ответственность каждого экрана.
6. `Program.cs`, метод `RunMonitoringAsync` — разделение частоты polling и частоты отправки.

## Проверки

- solution собирается в Release без предупреждений;
- 7/7 групп unit tests проходят;
- локально найдено 74 sensor samples;
- аппаратные preview JPEG проверены как 128×128;
- финальный integration test выполняется коротким `--monitor --cycles 3` на физическом Times Gate.
