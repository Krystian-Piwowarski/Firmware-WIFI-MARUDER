#include <Arduino.h>

HardwareSerial FlipperSerial(1);

const int candidateRX[] = {17, 18, 21, 33, 34, 38, 39, 40, 43, 44};
const int numCandidates = sizeof(candidateRX) / sizeof(candidateRX[0]);
int currentIndex = 0;
unsigned long lastSwitch = 0;

void setup() {
  Serial.begin(115200);
  delay(2000);
  Serial.println("Start testu UART z Flipperem.");
  Serial.println("Otworz teraz apke WiFi Marauder na Flipperze i wpisz cokolwiek/nacisnij przyciski.");
}

void loop() {
  if (millis() - lastSwitch > 3000) {
    FlipperSerial.end();
    delay(20);
    int rx = candidateRX[currentIndex];
    FlipperSerial.begin(115200, SERIAL_8N1, rx, -1); // -1 = TX nieuzywany, nasluchujemy tylko
    Serial.printf("--- Test RX=GPIO%d (baud 115200) ---\n", rx);
    currentIndex = (currentIndex + 1) % numCandidates;
    lastSwitch = millis();
  }

  while (FlipperSerial.available()) {
    int b = FlipperSerial.read();
    Serial.printf("ODEBRANO bajt: 0x%02X ('%c') na GPIO%d\n", b, (b >= 32 && b < 127) ? b : '.', candidateRX[(currentIndex - 1 + numCandidates) % numCandidates]);
  }
}