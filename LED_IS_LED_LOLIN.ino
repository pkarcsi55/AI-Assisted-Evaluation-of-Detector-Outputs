
#include "BluetoothSerial.h"
#if !defined(CONFIG_BT_ENABLED) || !defined(CONFIG_BLUEDROID_ENABLED)
#error Bluetooth is not enabled! Please run `make menuconfig` to and enable it
#endif  
BluetoothSerial SerialBT;
int R=12;int O=14;int Y=27;int G=26;int B=25;int U=33;
int CR=13;int CG=15;int CB=2;
void setup() {
  setColor(0, 0, 0);
  SerialBT.begin("Foto_effect");
  Serial.begin(9600);
  pinMode(CR,OUTPUT);pinMode(CG,OUTPUT);pinMode(CB,OUTPUT);
  villant(R); villant(O); villant(Y); villant(G); villant(B); villant(U);//Bemutatjuk a detektor színeit
}
void loop() {
    if (SerialBT.available() > 0){
      String S=SerialBT.readString();S.trim();
      if (S.substring(0,1)=="D"){
        villant(R); villant(O); villant(Y); villant(G); villant(B); villant(U);
      }
    }

    String ki="";
    ki=String(volt(R),2) + "," +String(volt(O),2) + ","+String(volt(Y),2) + ","+String(volt(G),2) + ","+String(volt(B),2) + ","+String(volt(U),2) + ",";
    Serial.println(ki);
    SerialBT.println(ki);
    setColor(0,0,0);
    if(volt(R)>0.3){ setColor(255,0,0); }
    if(volt(O)>0.3){ setColor(255,20,0); }
    if(volt(Y)>0.3){ setColor(255,20,0); }
    if(volt(G)>0.3){ setColor(0,100, 0); }
    if(volt(B)>0.3){ setColor(0,0,100); }
    if(volt(U)>0.3){ setColor(255,0,80); }
   delay(1000);
     //analogWrite(CR, 100);
    
}
void villant(int pin){
  pinMode(pin,OUTPUT);
  digitalWrite(pin,HIGH);
  delay(500);
  digitalWrite(pin,LOW);
  delay(500);
}

float volt (int pin){
  float volt=0;
  volt=3.3*analogRead(pin)/4096;
  return volt;
}

void setColor(int A, int B, int C) {
  analogWrite(CR, A);
  analogWrite(CG, B);
  analogWrite(CB, C);
}
