# Gang Beasts Pigeons

Mod do MelonLoadera (Gang Beasts, IL2CPP): **wszystkie postacie zamieniają się w gołębie**, gruchają zamiast mówić i zachowują się jak ptaki.

## Instalacja
1. Skopiuj `GangBeastsPigeons.dll` do `Gang Beasts/Mods/` (model, tekstura i dźwięki są wbudowane w dll).
2. Uruchom grę. **P** (lub **F8**) otwiera menu, **F9** włącza/wyłącza gołębie.

## Co robi
- Siatka postaci jest ukrywana, a na jej szkielecie fizycznym (biodra / klatka / głowa) rysowany jest gołąb z Twojego modelu.
- Kolor gołębia zależy od koloru gracza (można wyłączyć w menu).
- Dźwięki gołębia zastępują głos postaci: gruchanie, ból (gdy ktoś oberwie), wściekłe parskanie (walka), "chrapanie" po KO, trzepot skrzydeł.
- Zachowanie jak u ptaka: kiwanie głową przy chodzie, dziobanie, rozglądanie się, nadymanie szyi przy gruchaniu,
  machanie skrzydłami w locie / przy uderzeniu / w walce, podkulone nogi w powietrzu. Podniesione ręce postaci rozkładają skrzydła.

## Własne dźwięki
Wrzuć pliki `.wav` do `Mods/GangBeastsPigeons/`. Nazwa musi zaczynać się od: `coo`, `hurt`, `angry`, `sleep`, `laugh` lub `flap`
(np. `coo1.wav`). Pliki danej kategorii zastępują wbudowane.

## Gdyby coś było nie tak
- Gołąb niewidoczny / dziwny → w menu "Zmień materiał".
- Gołąb idzie tyłem → w menu "Odwróć przód" (mod sam to wykrywa po kilkunastu sekundach chodzenia).
- Gołąb za duży / za mały / lewituje → rozmiar i wysokość w menu.
- Log: `MelonLoader/Latest.log`, wpisy `[Pigeons]`.

## Budowanie
`tools/prep_pigeon.py` (Blender -> `assets/pigeon.bin`), `tools/make_sounds.py` (synteza dźwięków -> `assets/*.pcm`),
dll buduje GitHub Actions (`dist/GangBeastsPigeons.dll`).
