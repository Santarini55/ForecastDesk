# Forecast Desk

Forecast Desk is a Windows desktop application for traders. It combines an embedded TradingView workspace, forecast preparation, editable Telegram message preview, chart screenshots, and Telegram signal publishing.

## Features

- Open TradingView inside the app via WebView2.
- Keep TradingView login and cookies in the local user profile.
- Select the symbol and timeframe directly on the chart.
- Sync exchange, symbol, and timeframe into the forecast panel.
- Choose forecast direction: `UP` or `DOWN`.
- Choose forecast mode: `Time`, `Price`, or `Price + Time`.
- Enter Level, SL, and TP manually.
- Capture chart screenshots.
- Preview and edit the Telegram message before sending.
- Send the signal with a chart screenshot to Telegram.
- Keep a local forecast journal.

## How It Works

TradingView is used for charting, drawing, and screenshots. The user prepares the forecast on the chart, syncs the forecast details into the side panel, edits the Telegram preview if needed, and sends the signal to Telegram.

Forecast Desk does not store your Telegram credentials in the source code. Bot Token, Chat ID, Topic ID, journal data, screenshots, and the WebView2 profile are stored locally on the user's computer.

## Requirements

- Windows
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime

## Run From Source

dotnet run

## Publish

dotnet publish -c Release -r win-x64 --self-contained false -o ForecastDesk.publish

## User Data Location

Local settings and working data are stored in:

%APPDATA%\ForecastDesk

This folder may contain:

- Telegram settings
- Forecast journal
- Screenshots
- WebView2 profile for TradingView

Do not publish this folder to GitHub.

## Basic Usage

1. Open the TradingView chart inside Forecast Desk.
2. Select the exchange, symbol, and timeframe on the chart.
3. Draw your forecast or chart markup in TradingView.
4. Click `Sync from chart` if you need to update the right-side fields.
5. Choose the direction: `UP` or `DOWN`.
6. Choose the mode: `Time`, `Price`, or `Price + Time`.
7. Enter Level, SL, and TP manually.
8. Click `Preview` to review and edit the Telegram message.
9. Click `Send` to publish the signal to Telegram.

## Telegram Setup

To send signals, you need a Telegram bot:

1. Create a bot with `@BotFather`.
2. Copy the `Bot Token`.
3. Add the bot to your group, channel, or topic.
4. Get the `Chat ID`.
5. If you send messages to a forum topic, enter the `Topic ID`.
6. Enter these values in the `Settings` window.

Never publish your Bot Token. If it becomes public, regenerate it via `@BotFather`.

## Download

The latest Windows installer is available in the GitHub Releases section.

Download `ForecastDeskSetup.exe` from the Assets section of the latest release.





// ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------




# Forecast Desk

Windows-приложение для трейдера: график TradingView, оформление прогноза, предпросмотр сообщения и отправка торгового сигнала в Telegram.

## Возможности

- открывать TradingView внутри окна через WebView2;
- сохранять вход TradingView и cookies в локальном профиле пользователя;
- выбирать актив и таймфрейм прямо на графике;
- переносить биржу, актив и таймфрейм в панель прогноза;
- указывать направление `UP` или `DOWN`;
- выбирать режим прогноза: `Time`, `Price`, `Price + Time`;
- вручную вводить уровень, SL и TP;
- делать скриншот графика;
- редактировать предпросмотр Telegram-сообщения перед отправкой;
- отправлять сигнал со скриншотом в Telegram;
- хранить журнал прогнозов локально.

## Логика программы

TradingView используется для графика, разметки и скриншотов. Пользователь сам рисует прогноз на графике, затем переносит данные в форму прогноза и отправляет подготовленное сообщение в Telegram.

Forecast Desk не хранит ваши Telegram-настройки в исходном коде. Bot Token, Chat ID, Topic ID, журнал, скриншоты и профиль WebView2 сохраняются локально на компьютере пользователя.

## Запуск из исходного кода

Требования:

- Windows;
- .NET 8 SDK;
- Microsoft Edge WebView2 Runtime.

Запуск:

dotnet run

Публикация:

dotnet publish -c Release -r win-x64 --self-contained false -o ForecastDesk.publish

## Где хранятся данные пользователя

Локальные настройки и рабочие данные сохраняются в:

%APPDATA%\ForecastDesk

В эту папку попадают:

- настройки Telegram;
- журнал прогнозов;
- скриншоты;
- профиль WebView2 для TradingView.


## Как пользоваться

1. Откройте график TradingView внутри программы.
2. Выберите нужную биржу, актив и таймфрейм на графике.
3. Нарисуйте прогноз или нужную разметку в TradingView.
4. Нажмите `Взять с графика`, если нужно обновить поля справа.
5. Выберите направление `UP` или `DOWN`.
6. Выберите режим: `Time`, `Price` или `Price + Time`.
7. Введите уровень, SL и TP вручную.
8. Нажмите `Preview`, чтобы проверить и отредактировать текст сообщения.
9. Нажмите `Send`, чтобы отправить сигнал в Telegram.

## Telegram

Для отправки сигналов нужен Telegram-бот:

1. Создайте бота через `@BotFather`.
2. Получите `Bot Token`.
3. Добавьте бота в группу, канал или тему.
4. Получите `Chat ID`.
5. Если отправка идёт в тему группы, укажите `Topic ID`.
6. Введите эти данные в окне `Настройки`.


