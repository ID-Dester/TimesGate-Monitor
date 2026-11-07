# Публикация TGm на GitHub

Эта инструкция рассчитана на владельца, который впервые публикует open-source проект. До выполнения раздела «Первый push» никакие файлы не покидают ПК.

## 1. Что публикуется

Публичный репозиторий должен содержать исходники, документацию, тесты и GitHub Actions. Не публикуйте локальные сборки из `artifacts/`, `release/`, NuGet-кэши, логи, diagnostic ZIP, импортированные картинки пользователя или LocalToken.

Первая версия рекомендуется как **preview**, а не stable: `0.1.0-preview.1`. В README и release notes уже раскрыта известная проблема: в некоторых длительных сессиях Times Gate возвращается к стандартному циферблату примерно около 35 000 запросов, посчитанных сервером TGm. Это наблюдаемая корреляция, а не доказанный порог прошивки.

## 2. Перед поездкой ничего отправлять не нужно

Текущий локальный проект можно сохранить как обычную резервную копию. Не выполняйте `git push`, не создавайте публичный репозиторий и не ставьте тег до возвращения и финальной проверки.

## 3. Финальная локальная проверка

Откройте PowerShell в корне проекта:

```powershell
cd "C:\path\to\TimesGateMonitor"
git status --short
dotnet restore TimesGateMonitor.sln --locked-mode
dotnet build TimesGateMonitor.sln -c Release --no-restore
dotnet run --project tests\TimesGateMonitor.Tests -c Release --no-build
```

После этого проверьте кандидатов первого commit:

```powershell
git status --short --untracked-files=all
git check-ignore diagnostics\incoming\tgm.log
git check-ignore release\TGm-0.1.0-preview.1-win-x64.zip
```

Обе команды `git check-ignore` должны вывести переданный путь. Просмотрите `README.md`, `docs/KNOWN-ISSUES.md`, `SECURITY.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md` и `docs/PUBLIC-RELEASE-CHECKLIST.md`.

Особенно важно убедиться, что в commit не попали:

- реальный LocalToken;
- персональные LAN/IP-адреса и имя пользователя;
- `%LOCALAPPDATA%\TGm` и `%LOCALAPPDATA%\TimesGateMonitor`;
- `diagnostics/`, `artifacts/`, `release/`, `bin/`, `obj/` и локальные NuGet-кэши;
- сторонние изображения, на распространение которых нет прав.

## 4. Имя и описание репозитория

Рекомендуемое имя: `TGm` или `tgm-times-gate-monitor`. Название продукта TGm остаётся самостоятельным, а Divoom Times Gate используется в описании только для указания совместимости.

Описание GitHub:

> Unofficial Windows PC monitor for Divoom Times Gate using Network Text.

Сохраните дисклеймер в верхней части README:

> Unofficial monitor for Divoom Times Gate. This project is not affiliated with or endorsed by Divoom.

Отдельный дисклеймер при каждом запуске приложения не требуется: он уже виден в публичной документации и настройках проекта.

## 5. Создание репозитория на GitHub

1. Войдите в GitHub и нажмите **New repository**.
2. Выберите владельца и имя.
3. Установите **Public**.
4. Не добавляйте через сайт README, `.gitignore` или лицензию — эти файлы уже есть локально.
5. Создайте пустой репозиторий и скопируйте его HTTPS URL.

Перед первым commit при необходимости задайте публичное имя автора. E-mail можно скрыть через GitHub noreply-адрес:

```powershell
git config user.name "YOUR_PUBLIC_NAME"
git config user.email "YOUR_GITHUB_NOREPLY_EMAIL"
```

## 6. Первый commit и push

Сначала ещё раз посмотрите точный список файлов:

```powershell
git add --dry-run .
```

Если список чистый:

```powershell
git add .
git commit -m "Initial public preview"
git remote add origin https://github.com/OWNER/REPOSITORY.git
git branch -M main
git push -u origin main
```

Замените `OWNER/REPOSITORY` реальными значениями. Если `origin` уже существует, сначала проверьте его командой `git remote -v`; не заменяйте неизвестный remote вслепую.

## 7. Настройки после первого push

В GitHub:

1. Откройте **Actions** и дождитесь зелёного результата workflow `CI`.
2. В **Settings → General → Features** оставьте Issues включёнными.
3. В **Settings → Code security and analysis** включите Dependabot alerts и private vulnerability reporting, если доступны.
4. В **Settings → Branches** добавьте защиту `main`: требовать pull request и успешный статус `build-and-test` перед merge.
5. В блоке **About** добавьте описание, topics `windows`, `wpf`, `dotnet`, `hardware-monitoring`, `divoom`, `times-gate` и ссылку на Releases после первой публикации.

## 8. Первый preview release

Не создавайте тег, пока CI первого source commit не прошёл. После проверки поставьте подписанный или обычный аннотированный тег:

```powershell
git tag -a v0.1.0-preview.1 -m "TGm 0.1.0 preview 1"
git push origin v0.1.0-preview.1
```

Тег запускает `.github/workflows/release.yml`: workflow повторно собирает и тестирует проект, создаёт portable ZIP, SHA-256 и per-user installer, затем публикует GitHub prerelease.

После завершения:

1. Откройте созданный prerelease.
2. Замените автосгенерированное описание содержимым `docs/RELEASE-NOTES-0.1.0-preview.1.md` или добавьте его в начало.
3. Проверьте, что приложены ZIP, `.sha256` и installer.
4. Скачайте опубликованный ZIP обратно и сверьте SHA-256:

```powershell
Get-FileHash .\TGm-0.1.0-preview.1-win-x64.zip -Algorithm SHA256
Get-Content .\TGm-0.1.0-preview.1-win-x64.zip.sha256
```

5. Убедитесь, что release помечен как **Pre-release** и в описании заметна известная проблема перезапуска/сброса Times Gate.

## 9. Что делать с сообщениями о перезапуске

Не закрывайте такие reports как дубликаты без просмотра диагностики. Просите использовать форму **Times Gate restart / display reset**. Сравнивайте отдельно:

- прошедшее время до события;
- total request count из журнала;
- один LCD или все пять;
- предшествующие повреждения текста или `err at jsondecode!`;
- доступность устройства по LAN;
- прошивку, интервалы, питание, VPN и firewall.

Фраза «перезагрузка после 35 000 запросов» допустима только как краткое описание известного наблюдения. Пока внутренний uptime устройства и причина не измерены, нельзя объявлять 35 000 точным лимитом прошивки или утверждать, что TGm однозначно физически перезагружает устройство.

## 10. Откат публикации

Если после публикации обнаружен токен или другой секрет, простого удаления следующим commit недостаточно: секрет остаётся в истории. Немедленно смените LocalToken через официальный механизм Divoom, удалите public release, сделайте репозиторий private и очистите историю специализированным инструментом. После этого публикуйте заново только проверенную историю.
