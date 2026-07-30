POE ALTAR RECOGNIZER
====================

What it does
------------
POE Altar Recognizer watches the entire virtual desktop, including all connected
monitors. You provide lists of good and bad altar modifiers, one modifier
per line. It draws a click-through green box over good matches and a red box
over bad matches, without adding text labels. Highlights follow the detected
text when it moves and disappear as soon as the altar frame is gone.
Before OCR, the panel is converted to a high-contrast text mask so map
effects and terrain behind the translucent altar do not obscure the words.
The status line shows the duration of the latest live scan in milliseconds.
It does not read or modify Path of Exile memory and it does not send input
to the game.

Screen reading automatically pauses whenever the foreground application is
not a Path of Exile executable. Standalone, Steam, and x64 executable names
are recognized.
Only the focused Path of Exile window is captured; other monitors and desktop
areas are not processed.
OCR locates boxes by the orange frame color #E36B01, then requires modifier
text near #9FB9CA or #9797EB inside the box before reading it. A tolerance is
used for anti-aliasing and small color variations.
Numeric ranges copied from PoE DB, such as (50-80)%, (50–80)%, or
(50—80)%, are ignored when matching against the rolled in-game value.

Setup
-----
1. Start POEAltarRecognizer.exe.
2. Enter good and bad modifiers, with one modifier on each line.
3. Choose "Start watching the entire screen".

The good and bad lists show a numbered gutter. Each pasted line is treated as
one modifier, and long modifiers scroll horizontally instead of wrapping.

Use Windowed Fullscreen mode in Path of Exile so the overlay can appear.
Both modifier lists are remembered for the next launch.
They are saved automatically after editing and when the app closes in:

  settings.json (beside POEAltarRecognizer.exe)

On the first launch of this version, existing settings from the older
AppData location are imported automatically. Keep the app in a writable
folder; do not place it under Program Files.

Requirement
-----------
This smaller separated-files build requires the Microsoft .NET 8 Desktop
Runtime. It is already installed on the computer this build was created for.
If Windows asks for it on another computer, follow the provided Microsoft
download prompt once.

Stop shortcut
-------------
Press Ctrl+Shift+F8 at any time to stop watching and reopen the app.

Troubleshooting
---------------
- If nothing is detected, make sure the altar text is fully visible.
- Keep the in-game UI text size at its normal setting.
- If the red box does not display, use Windowed Fullscreen rather than
  exclusive Fullscreen.
- Keep all supplied files and folders together beside POEAltarRecognizer.exe.
