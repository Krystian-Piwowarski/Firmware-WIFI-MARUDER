# Sniffer RS485 – ESP32-C3

Pasywny podsłuch magistrali RS485 (moduł RE=GPIO3, DE=GPIO2, RO→GPIO4, DI←GPIO1) z GUI WWW i BLE.

## Prędkość i format magistrali

Ustawiane w GUI: **Ustawienia → RS485 → Baud rate / Format ramki → Zapisz**.
Zmiana działa od razu (bez restartu) i jest zapamiętywana w NVS. Domyślnie **9600 / 8N1**.
Dostępne: 1200–115200 baud, 8N1 / 8E1 / 8O1 / 8N2. Aktualne ustawienie widać w nagłówku zakładki Sniffer
i w `GET /api/status` (`baud`, `fmt`).

Wcześniejsza wersja miała na sztywno 19200/8E1 i ignorowała ustawienie z GUI.

## Wgrywanie

- Gotowy obraz: `bin/rs485-sniffer-c3-full.bin` – wgrać pod adres **0x0**
  (np. https://espressif.github.io/esptool-js/ w Chrome/Edge). Kasuje zapisane ustawienia Wi-Fi.
- Ze źródeł: `pio run -t upload`.

## GUI

Źródło strony: `gui/index.html`. Po zmianie uruchom `python3 tools_embed_gui.py`,
który kompresuje stronę i podmienia tablicę `index_html_gz` w `src/main.cpp`.
