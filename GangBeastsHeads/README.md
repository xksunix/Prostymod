# Gang Beasts Flying Heads (MelonLoader, IL2CPP)

Po mapie latają wielkie głowy (model z `source/head.glb`). Co jakiś czas rzucają się na losową postać, łapią ją językiem,
gryzą i wyrzucają. Gadają (każda swoim głosem z nagrań, mogą mówić jednocześnie), mrugają i ruszają ustami w rytm mowy.

## Instalacja
Wrzuć `GangBeastsHeads.dll` do folderu `Mods` w katalogu gry. Model, tekstura i dźwięki są wbudowane w ten jeden plik.

## Sterowanie
| Klawisz | Działanie |
|---|---|
| **H** | menu: liczba głów 0-10 na każdej mapie, rozmiar, częstotliwość ataków, głośność, atak/gadanie wł./wył., zmiana materiału |
| ← / → (przy otwartym menu) | zmiana liczby głów |
| Esc (przy otwartym menu) | zamknięcie menu |

Liczba głów jest zapamiętywana i obowiązuje na każdej mapie (też w menu gry). Ustawienia: `UserData/MelonPreferences.cfg`, sekcja `GBHeads`.

## Jak to działa
- **Lot:** głowy lecą do losowych punktów w kadrze kamery (zawsze widoczne, niezależnie od mapy), obracają się twarzą do kamery.
- **Atak:** głowa nurkuje na postać, wystrzeliwuje język, przyciąga ofiarę do ust, gryzie (dźwięk gryzienia) i wyrzuca ją w górę.
- **Mimika bez riga:** model jest jedną siatką, więc mod dodaje własne kości: szczęka (obrót), powieki (zjeżdżają na oczy).
  Wnętrze ust to osobna ciemna siatka pokazywana tylko przy otwartych ustach.
- **Dźwięk:** klipy z filmików (`tools/prep_audio.py`), ruch ust sterowany głośnością nagrania.

## Przebudowa zasobów
- `tools/prep_head.py source/head.glb assets/head.bin` - konwersja modelu (układ Unity, wagi szczęki/powiek, normalne, wnętrze ust)
- `tools/prep_audio.py <folder_z_mp4> assets` - wyciągnięcie dźwięku (ffmpeg); najmniej "dźwięczny" klip = odgłos gryzienia

## Jeśli coś nie działa
Wyślij `MelonLoader/Latest.log`. Mod wypisuje tam wszystko: zasoby, dostępne shadery, znalezione postacie, błędy.
Jeśli głowa jest niewidoczna, różowa albo dziwna, w menu (H) użyj **Zmień materiał** - przełącza shader.
