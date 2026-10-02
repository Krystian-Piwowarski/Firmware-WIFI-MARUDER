# Ewidencja Czasu Pracy RCP

Program dla Windows do rejestracji czasu pracy (RCP) z czytnikami kart **Mifare** na wejściu i wyjściu.
Uruchamiany jako **jeden plik `EwidencjaRCP.exe`**. Nie trzeba instalować Pythona ani .NET.

## Funkcje

- **Monitor na żywo**: duży komunikat „WEJŚCIE/WYJŚCIE: Imię Nazwisko” po każdym odbiciu, lista ostatnich odbić,
  lista osób obecnych w pracy i stan każdego czytnika.
- **Pracownicy**: kartoteka z wyszukiwaniem w trakcie pisania (nazwisko, imię, nr, karta, stanowisko,
  wielkość liter i polskie znaki bez znaczenia), filtr działu i statusu. Tabela pobiera z bazy tylko
  widoczne wiersze, więc płynnie przewija setki tysięcy rekordów.
- **Przypisywanie kart**: przyłóż kartę do dowolnego czytnika. Odbicia nieznanej karty zarejestrowane
  wcześniej zostaną automatycznie przypisane do pracownika.
- **Kalendarz pracownika**: widok miesiąca z godzinami pracy, wejściem i wyjściem, nadgodzinami, świętami i nieobecnościami
  (urlop, L4, opieka, delegacja…). Anomalie są zaznaczone na czerwono: brak wyjścia, brak wejścia, nieobecność
  nieusprawiedliwiona. Ręczne korekty wymagają podania uzasadnienia.
- **Zmiany nocne**: wyjście po północy liczy się do dnia wejścia.
- **Święta ustawowe w Polsce** (także Wielkanoc i Boże Ciało, Wigilia od 2025) są wyliczane automatycznie.
  Można dodać własne dni wolne firmy.
- **Zdarzenia**: rejestr wszystkich odbić z filtrem dat, pracownika, karty i czytnika oraz nieznanych kart.
- **Zestawienia**: miesięczne lub za dowolny okres, dla całej firmy albo działu: przepracowane godziny, norma, nadgodziny,
  niedopracowanie, nieobecności, anomalie. Eksport do CSV (Excel).
- **Wydruk karty ewidencji czasu pracy** z miejscem na podpisy.
- Import i eksport pracowników z CSV, kopia zapasowa bazy „na gorąco”, dziennik zmian (audyt) i dziennik pracy programu.

## Obsługiwane czytniki Mifare

| Typ | Przykłady | Jak podłączyć |
|---|---|---|
| **USB – klawiatura (HID)** | tanie czytniki USB 13,56 MHz „wpisujące” numer karty | *Konfiguracja → Czytniki → Dodaj → Typ: USB (klawiatura) → **Wykryj…*** i przyłóż kartę do tego czytnika |
| **PC/SC** | ACR122U, ACR1252U, HID Omnikey 5x21 | Typ: PC/SC; nazwa czytnika z listy (puste = pierwszy) |
| **Port COM** | czytniki RS232 / RS485 (przez konwerter) / USB‑COM | Typ: Port COM, wybierz COMx i prędkość |
| **Sieć TCP/IP** | czytniki z Ethernetem, konwertery RS485↔TCP | Typ: Sieć TCP/IP, adres `IP:port` |

**Wejście i wyjście.** Każdy czytnik ma kierunek: *Wejście*, *Wyjście* albo *Wejście/wyjście (naprzemiennie)*,
gdy jest jeden czytnik. Kilka czytników USB‑klawiatur jest rozróżnianych po identyfikatorze urządzenia
(Windows Raw Input). Czytnik WEJŚCIA i czytnik WYJŚCIA mogą więc być podłączone do jednego komputera.
Program odbiera odczyty także wtedy, gdy jego okno nie jest aktywne. Gdy okno programu jest aktywne, cyfry z czytnika nie trafiają
do pól tekstowych programu. Inne programy otwarte na tym komputerze nadal je otrzymają, dlatego stanowisko RCP
najlepiej przeznaczyć tylko do tego programu albo użyć czytników PC/SC, COM lub TCP.

**Format numeru karty.** UID jest zapisywany w HEX, wielkimi literami, bez separatorów (np. `04A23B11`).
Jeśli czytnik podaje numer dziesiętny albo bajty w odwrotnej kolejności, zaznacz odpowiednie opcje w konfiguracji
czytnika, aby ta sama karta miała ten sam numer na każdym czytniku.

**Podwójne odbicie.** Ponowne odbicie tej samej karty w ciągu 30 s jest ignorowane. Czas zmienisz w *Konfiguracja → Ustawienia*.

## Uruchomienie

1. Pobierz `EwidencjaRCP.exe`: z zakładki **Actions** w GitHubie (artefakt `EwidencjaRCP-win-x64`)
   albo zbuduj go samodzielnie (niżej).
2. Skopiuj plik na komputer z Windows 10/11 (64‑bit) i uruchom.
3. Przy pierwszym starcie program zaproponuje konfigurację czytników.
4. Dodaj pracowników ręcznie lub zaimportuj CSV (*Pracownicy → Import CSV…*, wzór w `przyklad-pracownicy.csv`).

Dane są w `C:\ProgramData\EwidencjaRCP\` (`rcp.db` to baza, `logs\` to dzienniki).
Inną lokalizację bazy ustawisz w *Konfiguracja → Ustawienia* albo parametrem `EwidencjaRCP.exe --db D:\RCP\rcp.db`.

### Import CSV

Separator `;` lub `,`, kodowanie UTF‑8. Kolejność kolumn jest dowolna, liczą się nagłówki.

```
nr;nazwisko;imie;dzial;stanowisko;karta;norma_h;aktywny
1001;Kowalski;Jan;Produkcja;Operator;04A23B11;8;1
```

Wymagane są `nr` i `nazwisko`. Działy tworzą się automatycznie. Ponowny import aktualizuje pracowników po numerze `nr`.

### Skróty

`F2` monitor · `F3` szukaj pracownika · `F4` kalendarz · `F5` zdarzenia · `F6` zestawienia · `Ctrl+N` nowy pracownik

## Wydajność (duża liczba pracowników)

Baza to SQLite w trybie WAL z indeksami i wyszukiwaniem stronicowanym. Zmierzone na 100 000 pracowników
i 4,4 mln zdarzeń (plik bazy ok. 630 MB):

| Operacja | Czas |
|---|---|
| rejestracja odbicia karty | ~5 ms |
| wyszukanie pracownika (tekst) | 20–70 ms |
| zdarzenia z tygodnia (1,4 mln), filtr po pracowniku | 20–90 ms |
| kalendarz pracownika (miesiąc) | ~30 ms |
| zestawienie miesięczne – jeden dział (2 000 os.) | ~0,2 s |
| zestawienie miesięczne – cała firma (100 000 os.) | ~15 s (w tle, z postępem) |

Baza jest zwykłym plikiem i działa najlepiej na dysku lokalnym komputera RCP.
Nie umieszczaj jej na udziale sieciowym, z którego korzysta kilka komputerów naraz.

## Budowanie EXE

Wymagany [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
build.cmd
```

albo ręcznie:

```
dotnet test tests\Rcp.Tests
dotnet publish src\Rcp.App -c Release -r win-x64 -p:PublishSingleFile=true -o dist
```

Wynik to `dist\EwidencjaRCP.exe` (ok. 65 MB, zawiera środowisko .NET i SQLite).
Ten sam proces uruchamia GitHub Actions (`.github/workflows/ewidencja-rcp.yml`) przy każdej zmianie w tym katalogu.

## Struktura

```
src/Rcp.Core   – model, baza SQLite, obliczanie czasu pracy, święta, import/eksport (bez zależności od Windows)
src/Rcp.App    – aplikacja WinForms: okna, sterowniki czytników (Raw Input, PC/SC, COM, TCP)
tests/Rcp.Tests – testy jednostkowe (xUnit)
```
