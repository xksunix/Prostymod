# Gang Beasts Faces (MelonLoader)

Dodaje postaciom w Gang Beasts usta i czarne brwi, które reagują na to, co się dzieje.

## Instalacja
Wrzuć `GangBeastsFaces.dll` do folderu `Mods` w katalogu gry (`...\Gang Beasts\Mods`) i uruchom grę.

## Reakcje
- **Nokaut** – usta szeroko otwarte, brwi w górze
- **Bicie / atak** – zaciśnięte usta, brwi opuszczone w złości
- **Upadek** – usta lekko otwarte, brwi uniesione
- **Chwytanie / wspinanie** – wysiłek, lekko zmarszczone brwi
- **Śmiech** – gdy ktoś inny dostanie nokaut, reszta się śmieje (usta ruszają się w rytm)
- Normalnie – lekki uśmiech

## Ustawianie twarzy w grze
Jeśli twarz jest w złym miejscu (z tyłu głowy, w środku, odwrócona), ustaw ją klawiszami:

| Klawisz | Działanie |
|---|---|
| F6 | następna oś „przodu" twarzy |
| F7 | następna oś „góry" twarzy |
| F8 / F9 | mniejsza / większa twarz |
| PageUp / PageDown | usta wyżej / niżej |
| Insert / Delete | brwi wyżej / niżej |
| Home / End | twarz dalej od / bliżej środka głowy |
| F10 | reset |

Ustawienia zapisują się same (`UserData/MelonPreferences.cfg`, sekcja `GBFaces`).

## Jeśli coś nie działa
Wyślij `MelonLoader/Latest.log`. Mod wypisuje tam znalezioną klasę postaci, kość głowy i zrzut hierarchii obiektów.
