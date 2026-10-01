# ESP32-S3 Symulator

Wirtualna płytka ESP32-S3 działająca w przeglądarce — pozwala testować logikę i UI
softu bez fizycznego egzemplarza.

## Co zawiera

- Wyświetlacz OLED 128×64 (SSD1306) renderowany na canvasie
- Dioda RGB (GPIO48), zwykła LED (GPIO2)
- Przyciski BOOT (GPIO0) i użytkownika (GPIO14) z pull-upem (wciśnięty = `LOW`)
- Potencjometr na wejściu ADC (GPIO4, zakres 0–4095)
- Edytor kodu w stylu Arduino (`setup()` / `loop()`)
- Monitor Serial i podgląd stanu pinów na żywo
- Zestaw gotowych przykładów

## Uruchomienie

To pojedynczy, samodzielny plik `index.html` — otwórz go w przeglądarce.
Nie wymaga builda ani serwera. Można też serwować przez GitHub Pages
(ustaw źródło na ten katalog lub przenieś plik do katalogu publikowanego).

## API (styl Arduino)

- **GPIO:** `pinMode(pin, OUTPUT|INPUT|INPUT_PULLUP)`, `digitalWrite`, `digitalRead`,
  `analogRead` (0–4095), `neopixelWrite(48, r, g, b)`
- **Czas:** `await delay(ms)`, `millis()`, `micros()`
- **Serial:** `Serial.println / print / printf`
- **OLED (Adafruit_GFX):** `clearDisplay`, `setCursor`, `setTextSize`, `print`/`println`,
  `drawPixel`, `drawLine`, `drawRect`, `fillRect`, `drawCircle`, `fillCircle`, `display()`

Pauzy w `loop()` rób przez `await delay(ms)` — dlatego pętla może być `async`.

## Ograniczenia

Symulator wykonuje logikę w JavaScript (styl Arduino). Służy do szybkiego sprawdzenia
UI, logiki menu, rysowania na ekranie i reakcji na wejścia. **Nie** jest pełnym
emulatorem rdzenia Xtensa — nie uruchamia skompilowanego `.bin` ani prawdziwego
stosu Wi-Fi/BLE.
