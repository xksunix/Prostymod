# Gang Beasts Grab 1.0 (MelonLoader, IL2CPP)

Myszka jak "pistolet grawitacyjny": chwytasz, przenosisz i rzucasz czym chcesz, bijesz i wysadzasz ludziki.

## Instalacja
Wrzuć `GangBeastsGrab.dll` do folderu `Mods` w katalogu gry (`...\Gang Beasts\Mods`) i uruchom grę.

## Sterowanie
| Mysz | Działanie |
|---|---|
| **LPM przytrzymaj** na czymkolwiek | chwyta (postać, kończyna, skrzynia, cokolwiek z fizyką) i przenosi za kursorem |
| **Rolka** podczas trzymania | dalej / bliżej od kamery (do góry = dalej) |
| **Puść LPM w ruchu** | RZUT (leci tak szybko, jak ruszałeś myszką) |
| **Szybkie kliknięcie LPM** | CIOS w to, na co klikniesz |
| **PPM** | WYBUCH w miejscu kursora - odrzuca wszystko wokół, z błyskiem, odłamkami i dźwiękiem |

Klawisze: **F2** wł./wył. moda, **F3 / F4** mniejszy / większy wybuch.

## Ustawienia
`UserData/MelonPreferences.cfg`, sekcja `GBGrab`:
`GrabStrength`, `GrabMaxSpeed`, `ThrowBoost`, `MaxThrowSpeed`, `HoldDelay` (po ilu sekundach przytrzymania to już chwyt, a nie cios),
`PunchSpeed`, `PunchRadius`, `ExplosionRadius`, `ExplosionSpeed`, `ExplosionUp`, `ExplosionVolume`,
`ForceCursor` (wymusza widoczny kursor w grze), `TearApart` (true = wybuch rozrywa stawy blisko środka; domyślnie wyłączone, bo może rozwalić logikę postaci).
