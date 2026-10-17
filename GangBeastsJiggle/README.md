# Gang Beasts Jiggle 2.3 (MelonLoader)

1. Wrzuc `GangBeastsJiggle.dll` do `Gang Beasts/Mods/`.
2. Uruchom gre. Log: `GangBeastsJiggle_log.txt` w glownym folderze gry. Ustawienia: `UserData/MelonPreferences.cfg`.
3. Mod wybiera jedna kosc miednicy ("pupe") kazdej postaci, powieksza ja (`BaseScale`) i wprawia w sprezyste drgania.
4. Zla kosc? Wpisz jej nazwe z logu w `ForceBone`.

## Klawisze w grze
F6 wlacz/wylacz, F7/F8 rozmiar -/+, F9/F10 sila drgan -/+ (ustawienia zapisuja sie przy wyjsciu z gry).

## Kupa i sraczka (od wersji 2.2)
**G (przytrzymaj)** – każda postać robi kupę (kopczyk za nią) i puszcza fontannę sraczki do tyłu. Plamy zostają na podłodze i się rozlewają.
**H** – odwróć kierunek (jeśli leci do przodu zamiast do tyłu), **J** – posprzątaj.
Ustawienia: `UserData/MelonPreferences.cfg`, sekcja `GBPoop` (`BackOffset`, `UpOffset`, `Speed`, `Spread`, `Rate`, `Size`, `MaxSplats`).

## Pierdzenie (od wersji 2.3)
**P (przytrzymaj)** – każda postać pierdzi (dźwięk generowany w kodzie, 5 wariantów o losowej wysokości), a pupa trzęsie się bardzo mocno przez ok. 1 sekundę.
Ustawienia w sekcji `GBPoop`: `FartKey` (domyślnie `P`), `FartVolume`, `FartShake` (siła trzęsienia, 0 = bez trzęsienia).

## Zmiany 2.3
- Klawisze (G, H, J, P, F6–F10) czytane przez WinAPI – wcześniej G nie reagowało, bo stary Input w tej grze rzuca wyjątek.
- Kupa i sraczka używają domyślnego materiału gry i warstwy modelu postaci, więc są widoczne.

## Zmiany 3.0
- **Pupa przepisana**: dwie widoczne, sprężynujące półkule w kolorze postaci, przyczepione do bioder. Poprzednia wersja skalowała kość przez własny komponent, a w IL2CPP nie działa on bez rejestracji (w logu był też `MissingMethodException` na `FindObjectsOfType`).
- Klawisze do ustawienia pupy: **PgUp/PgDn** wyżej/niżej, **Home/End** dalej/bliżej ciała, **F7/F8** rozmiar, **F9/F10** siła drgań, **H** odwróć przód/tył. Wartości: `CheekBack`, `CheekUp`, `BaseScale`, `JiggleScale` w `UserData/MelonPreferences.cfg`.
- **Pierdzenie to teraz prawdziwe dźwięki** (6 nagrań z Real_fart_sounds.mp3 wbudowanych w DLL). Gdyby się nie wczytały, używane są dźwięki generowane w kodzie.
- Naprawa niewidocznej kupy i sraczki (shader `Sprites/Default` zamiast "Standard", który w URP się nie renderuje).
