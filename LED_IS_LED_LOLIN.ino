// Version 20260905 - WEMOS LOLIN32 LITE
#include "BluetoothSerial.h"

#if !defined(CONFIG_BT_ENABLED) || !defined(CONFIG_BLUEDROID_ENABLED)
  #error Bluetooth is not enabled! Please enable it in menuconfig.
#endif

BluetoothSerial SerialBT;

// --------- Detector channels (ADC inputs) ---------
enum Channel : uint8_t { CH_R, CH_O, CH_Y, CH_G, CH_B, CH_U, CH_COUNT };

// GPIOs used as ADC inputs
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

// Measurement interval:
// 200 ms -> approximately 5 measurement frames per second
const uint32_t kLoopDelayMs = 200;

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

// Read one ADC pin and convert the ADC value to voltage
float readVoltage(int pin) {
  const int raw = analogRead(pin);

  return kVref * (float)raw / kAdcMax;
}

// Read all six detector channels
void sampleAllChannels() {
  for (uint8_t i = 0; i < CH_COUNT; i++) {
    v[i] = readVoltage(kAdcPin[i]);
  }
}

// Build CSV line:
// R,O,Y,G,B,U,
//
// Example:
// 0.12,0.35,0.81,0.24,0.05,0.02,
String buildCsvLine() {
  String s;

  s.reserve(64);

  for (uint8_t i = 0; i < CH_COUNT; i++) {
    s += String(v[i], 2);
    s += ",";
  }

  return s;
}

// Optional Bluetooth command handling:
//
// D -> repeat detector startup demo
void handleBluetoothCommands() {
  if (!SerialBT.available()) {
    return;
  }

  String cmd = SerialBT.readStringUntil('\n');
  cmd.trim();

  if (cmd.length() == 0) {
    return;
  }

  if (cmd.startsWith("D")) {
    detectorDemo();

    // Restore detector pins to ADC input mode
    for (uint8_t i = 0; i < CH_COUNT; i++) {
      pinMode(kAdcPin[i], INPUT);
    }
  }
}

void setup() {
  Serial.begin(9600);

  SerialBT.begin("Foto_effect");

  // Startup detector demonstration
  detectorDemo();

  // Switch all detector pins back to ADC input mode
  for (uint8_t i = 0; i < CH_COUNT; i++) {
    pinMode(kAdcPin[i], INPUT);
  }
}

void loop() {
  // Check optional Bluetooth commands
  handleBluetoothCommands();

  // Read all detector channels
  sampleAllChannels();

  // Create one complete measurement frame
  const String line = buildCsvLine();

  // Send the same data through USB serial and Bluetooth
  Serial.println(line);
  SerialBT.println(line);

  // 200 ms interval -> approximately 5 Hz sampling/output rate
  delay(kLoopDelayMs);
}
