# Gang Beasts Faces 1.3 (MelonLoader, IL2CPP)

Dodaje postaciom w Gang Beasts usta (z językiem i zębami) i brwi, które reagują na to, co się dzieje.

## Instalacja
Wrzuć `GangBeastsFaces.dll` do folderu `Mods` w katalogu gry (`...\Gang Beasts\Mods`) i uruchom grę.

## Reakcje
- **Nokaut** – usta szeroko otwarte, język, brwi w górze
- **Uderzenie / wstrząs / lądowanie** – krótkie „au!" (usta otwarte, brwi smutne)
- **Bicie** – zaciśnięte zęby, brwi w złości (wykrywane po szybkim ruchu rąk)
- **Głos postaci** – gdy postać wydaje dźwięk (krzyk, jęk), usta ruszają się w rytm
- **Skok** – otwarte usta, szeroki uśmiech, brwi w górze
- **Upadek** – „aaa!"
- **Bieg** – usta sapią w rytm kroków
- **Wspinaczka** – zaciśnięte zęby, brwi w złości
- **Pływanie** – usta w ciup, bąbelki
- **Śmiech** – gdy ktoś inny dostanie nokaut, reszta się śmieje
- **Postój** – lekki uśmiech + losowe miny (mruczenie, uniesiona brew, szeroki uśmiech, „ooo", podejrzliwy wzrok)

## Ustawianie twarzy w grze
Mod sam wylicza orientację i rozmiar twarzy z kości postaci. Jeśli twarz jest w złym miejscu, ustaw ją klawiszami:

| Klawisz | Działanie |
|---|---|
| Num+ / Num- | **cała twarz wyżej / niżej** |
| F6 / F7 | następna oś „przodu" / „góry" twarzy (tryb ręczny) |
| F8 / F9 | mniejsza / większa twarz |
| PageUp / PageDown | usta wyżej / niżej |
| Insert / Delete | brwi wyżej / niżej |
| Home / End | twarz dalej od / bliżej środka głowy |
| F11 | ponowna automatyczna kalibracja (postać powinna stać prosto) |
| F10 | reset |

Ustawienia zapisują się same (`UserData/MelonPreferences.cfg`, sekcja `GBFaces2`).
`FaceY` – wysokość całej twarzy, `HitThreshold` – jak mocne musi być uderzenie, żeby postać zrobiła minę „au!" (mniej = czulej).
