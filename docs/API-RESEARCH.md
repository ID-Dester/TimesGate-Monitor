# Исследование Times Gate LAN API — 26 августа 2026

## Главный вывод

Divoom прямо пишет, что официальной стабильной API-спецификации Times Gate нет: LAN API исследована сообществом и может измениться после обновления firmware. Поэтому протокол изолирован в проекте `TimesGateMonitor.TimesGate`.

Для частого обновления пяти 128×128 LCD подтверждена следующая схема:

```http
POST http://<device-ip>/post
Content-Type: application/json
```

```json
{
  "Command": "Draw/SendHttpGif",
  "LcdArray": [0, 0, 1, 0, 0],
  "PicNum": 1,
  "PicWidth": 128,
  "PicOffset": 0,
  "PicID": 42,
  "PicSpeed": 1000,
  "PicData": "<base64 JPEG>",
  "LocalToken": 123456
}
```

Критические детали:

1. `PicData` для Times Gate — Base64 от готового JPEG, а не raw RGB/RGB565 из старых Pixoo-примеров.
2. `LcdArray` всегда содержит пять элементов; единица выбирает физический экран.
3. `LocalToken` обязателен на современных прошивках. Без него устройство может вернуть текстовую ошибку `DeviceToken is err`.
4. Перед первой последовательностью выполняется `Draw/ResetHttpGifId`; затем `PicID` монотонно увеличивается.
5. Успешный ответ имеет `error_code: 0`, причём поле встречается и числом, и строкой. Клиент принимает оба варианта.
6. На проверенной Times Gate firmware JSON body должен иметь явный `Content-Length` и простой `Content-Type: application/json`. `PostAsJsonAsync` с потоковым/chunked content приводил к `Request data illegal json`, хотя сам JSON был корректным. Клиент поэтому заранее сериализует UTF-8 bytes и использует `ByteArrayContent`.

## Использованные команды

- health check: `Channel/GetAllConf`;
- initialize: `Draw/ResetHttpGifId`;
- frame: `Draw/SendHttpGif`;
- brightness: `Channel/SetBrightness`.

Другие команды в milestone 1 не используются. В частности, `Device/PlayGif` требует доступный самому устройству URL и не подходит для обновления мониторинга каждую секунду.

## Источники и степень доверия

- [Divoom Times Gate API Developer Guide](https://divoom.com/blogs/app-guide/divoom-times-gate-api-developer-guide) — страница Divoom Editorial, подтверждает отсутствие официальной спецификации, LAN-модель, пять 128×128 экранов и ограничения cloud bootstrap.
- [Практическое описание рабочего Times Gate JPEG-протокола](https://lsz.wtf/projects) — современная реализация с `LocalToken`, Base64 JPEG, `LcdArray` и `PicID`.
- [Рабочий пример `Device/PlayGif` с `LcdArray`](https://www.reddit.com/r/Divoom/comments/1isdv3y) — независимая адресация LCD и URL-based альтернатива.
- [DivoomDevelop LAN tooling](https://github.com/DivoomDevelop/mcp-divoom-lan) — подтверждает более новый `/divoom_api`/port 9000 протокол локальных watchface-команд. Это другой сценарий; для live-кадров milestone использует проверенный `/post`.
- [Community Times Gate library](https://github.com/polynomial/divoom-times-gate) — полезный каталог команд, но README сам отмечает, что часть image/text функций не работает. Его утверждение о raw RGB не принято как основание для реализации.

## Почему не использован raw RGB

Старые Pixoo-библиотеки отправляют `width × height × 3` RGB bytes. Times Gate может ответить `error_code: 0`, но остаться на экране загрузки. JPEG также значительно уменьшает JSON payload: это полезно при пяти обновлениях в секунду. Компромисс — JPEG lossy; для мелкого текста качество 92 даёт хороший баланс, а в будущем можно измерить результат на устройстве и сделать качество настройкой.

## Ограничения, которые нельзя проверить без устройства

- расположение конкретного `LcdArray` индекса на конкретной аппаратной ревизии;
- необходимость порта 80 или 9000 на firmware пользователя;
- визуальное качество JPEG и максимальная безопасная частота;
- поведение при потере cloud connection после перезапуска.

Диагностический executable специально показывает разные номера и progress bars: пользовательская проверка сразу обнаружит переставленные индексы или несовместимую прошивку.
