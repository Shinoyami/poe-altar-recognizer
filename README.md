# POE Altar Recognizer

POE Altar Recognizer is a lightweight Windows overlay for Path of Exile Eldritch Altars. It reads visible altar choices with OCR and highlights configured modifiers:

- green for wanted modifiers;
- red for unwanted modifiers.

The app reads screen pixels only. It does not attach to Path of Exile, inspect game memory, or send input to the game.

## Features

- Runs only while a `PathOfExile...` process is focused.
- Locates altar boxes from the orange `#E36B01` frame.
- Verifies modifier text colors near `#9FB9CA` and `#9797EB`.
- Supports good and bad modifier lists with numbered rows.
- Saves portable settings beside the executable.
- Ignores variable PoE DB ranges such as `(50—80)%`.
- Uses strict distinguishing-word matching so Quantity, Rarity, Basic Currency, and Unique Items are not confused.
- Provides a global `Ctrl+Shift+F8` stop shortcut.

## Download

Download the Windows ZIP from the [v1.2 release](../../releases/tag/v1.2), extract every file, and run `POEAltarRecognizer.exe`.

The [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) is required.

## Usage

1. Add one modifier per line to the good or bad list.
2. Click **Start watching the entire screen**.
3. Focus Path of Exile.
4. Open an Eldritch Altar.

The lists save automatically to `settings.json` beside the executable.

Suggested modifier source: [PoE DB — Eldritch Altar](https://poedb.tw/us/Eldritch_Altar#EldritchAltar).

## Build

Requirements:

- Windows
- .NET 8 SDK

```powershell
dotnet build -c Release
```

The build downloads Tesseract's English fast OCR data when it is not already present locally.

To create a framework-dependent Windows build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## Notes

- Windowed Fullscreen is recommended.
- Keep all published files and folders together.
- This project is an independent community tool and is not affiliated with Grinding Gear Games.
