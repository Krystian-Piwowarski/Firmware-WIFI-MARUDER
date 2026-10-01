# Kwiatownik 🌸 – rozpoznawanie kwiatów i porady uprawowe (Android)

Aplikacja na Androida, która:

- **rozpoznaje rośliny (głównie kwiaty) ze zdjęcia** – z aparatu lub z galerii, przez darmowe API [Pl@ntNet](https://my.plantnet.org/) (~50 tys. gatunków, polskie nazwy),
- pokazuje **5 najbardziej prawdopodobnych gatunków** z procentem pewności i zdjęciem porównawczym,
- wyświetla **opis rośliny z polskiej Wikipedii** (z angielską jako zapasową),
- podaje **porady uprawowe po polsku** z wbudowanej bazy 53 popularnych kwiatów i roślin doniczkowych:
  światło, podlewanie, podłoże, temperatura, nawożenie, kwitnienie, przycinanie,
  **na co zwrócić uwagę**, **czego nie robić** oraz **toksyczność dla dzieci i zwierząt**,
- ma **Atlas** z wyszukiwarką – porady można przeglądać bez robienia zdjęcia.

Dla gatunków spoza bazy aplikacja pokazuje ogólne zasady uprawy roślin kwitnących.

## Jak zdobyć APK

1. Wejdź w zakładkę **Actions** repozytorium → workflow **„Android – Kwiatownik (APK)”**.
2. Otwórz ostatnie zielone uruchomienie i pobierz artefakt **Kwiatownik-debug-apk** (zip z plikiem `.apk`).
3. Przenieś `app-debug.apk` na telefon i zainstaluj (trzeba zezwolić na instalację z nieznanych źródeł).

Albo zbuduj sam w **Android Studio**: *File → Open* → folder `android-plant-app` → *Run*.

## Klucz API Pl@ntNet (darmowy)

1. Załóż konto na <https://my.plantnet.org/>.
2. Skopiuj swój klucz API (limit: 500 rozpoznań dziennie).
3. Wklej go w aplikacji: **⚙ Ustawienia → Klucz API → Zapisz**.

Klucz można też wbudować w APK:
- lokalnie: dopisz `PLANTNET_API_KEY=twój_klucz` do `android-plant-app/local.properties`,
- w GitHub Actions: dodaj sekret repozytorium `PLANTNET_API_KEY`.

## Technologie

Kotlin, Jetpack Compose (Material 3), OkHttp, Coil. Min. Android 8.0 (API 26).

```
app/src/main/
├── assets/plants_care.json      # baza porad uprawowych (łatwo ją rozszerzać)
└── java/pl/kwiatownik/
    ├── data/                    # Pl@ntNet, Wikipedia, baza porad, obróbka zdjęć
    └── ui/                      # ViewModel i ekrany: start, wyniki, szczegóły, atlas, ustawienia
```

### Dodawanie roślin do bazy

Dopisz obiekt do `plants_care.json`. Pole `match` to lista nazw łacińskich – rodzaj (np. `"Rosa"`)
albo pełna nazwa gatunku (np. `"Euphorbia pulcherrima"`). Aplikacja dopasowuje najpierw gatunek, potem rodzaj.

> Porady mają charakter ogólny. W razie podejrzenia zatrucia skontaktuj się z lekarzem lub weterynarzem.
