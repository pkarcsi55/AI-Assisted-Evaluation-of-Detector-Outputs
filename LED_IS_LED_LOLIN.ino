//Version 20260204 WEMOS LOLIN 32 LITE
#include "BluetoothSerial.h"

#if !defined(CONFIG_BT_ENABLED) || !defined(CONFIG_BLUEDROID_ENABLED)
  #error Bluetooth is not enabled! Please enable it in menuconfig.
#endif

BluetoothSerial SerialBT;

// --------- Detector channels (ADC inputs) ---------
enum Channel : uint8_t { CH_R, CH_O, CH_Y, CH_G, CH_B, CH_U, CH_COUNT };

// GPIOs used as ADC inputs (your original mapping)
const int kAdcPin[CH_COUNT] = {
  12, // R
  14, // O
  27, // Y
  26, // G
  25, // B
  33  // U
};

// --------- Sampling / output settings ---------
const float kVref   = 3.3f;      // Approx. ADC reference voltage
const float kAdcMax = 4095.0f;   // 12-bit ADC max value (0..4095)
const uint32_t kLoopDelayMs = 1000;

// One measurement frame (6 channels)
float v[CH_COUNT];

// Blink one detector channel pin to demonstrate the detector "color"
void blinkPin(int pin, uint16_t onMs = 500, uint16_t offMs = 500) {
  pinMode(pin, OUTPUT);
  digitalWrite(pin, HIGH);
  delay(onMs);
  digitalWrite(pin, LOW);
  delay(offMs);
}

// Run a startup demo: blink all detector channels in order
void detectorDemo() {
  for (uint8_t i = 0; i < CH_COUNT; i++) {
    blinkPin(kAdcPin[i]);
  }
}

// Read one ADC pin and convert to voltage (approx.)
float readVoltage(int pin) {
  const int raw = analogRead(pin);
  return kVref * (float)raw / kAdcMax;
}

// Read all channels once per loop
void sampleAllChannels() {
  for (uint8_t i = 0; i < CH_COUNT; i++) {
    v[i] = readVoltage(kAdcPin[i]);
  }
}

// Build CSV line: "R,O,Y,G,B,U," with 2 decimals (keeps your trailing comma style)
String buildCsvLine() {
  String s;
  s.reserve(64);
  for (uint8_t i = 0; i < CH_COUNT; i++) {
    s += String(v[i], 2);
    s += ",";
  }
  return s;
}

// Optional BT command handling:
// - "D" -> re-run detector demo
void handleBluetoothCommands() {
  if (!SerialBT.available()) return;

  String cmd = SerialBT.readStringUntil('\n');
  cmd.trim();
  if (cmd.length() == 0) return;

  if (cmd.startsWith("D")) {
    detectorDemo();
  }
}

void setup() {
  Serial.begin(9600);
  SerialBT.begin("Foto_effect");

  // Startup demo: show the 6 detector channels
  detectorDemo();

  // After demo, switch pins back to input (so ADC reading is clean/consistent)
  for (uint8_t i = 0; i < CH_COUNT; i++) {
    pinMode(kAdcPin[i], INPUT);
  }
}

void loop() {
  handleBluetoothCommands();
  sampleAllChannels();
  const String line = buildCsvLine();
  Serial.println(line);
  SerialBT.println(line);
  delay(kLoopDelayMs);
}
