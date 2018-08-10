/*******************************************************************************
* Thermocouple to serial for MAX31855 example 
* Version: 1.00
* Date: 26-12-2011
* Company: Rocket Scream Electronics
* Website: www.rocketscream.com
*
* This is an example of using the MAX31855 library for Arduino to read 
* temperature from a thermocouple and send the reading to serial interfacec. 
* Please check our wiki (www.rocketscream.com/wiki) for more information on 
* using this piece of library.
*
* This example code is licensed under Creative Commons Attribution-ShareAlike 
* 3.0 Unported License.
*
* Revision  Description
* ========  ===========
* 1.00      Initial public release.
*
*******************************************************************************/
// ***** INCLUDES *****
#include  "MAX31855.h"

#define ERROR_MEMORY 200 // almost one minute. 

// ***** PIN DEFINITIONS *****
const  unsigned  char thermocoupleSO_0 = 0; // DB-9 pin 3
const  unsigned  char thermocoupleSO_1 = 1; // DB-9 pin 3
const  unsigned  char thermocoupleSO_2 = 2; // DB-9 pin 3
const  unsigned  char thermocoupleSO_3 = 3; // DB-9 pin 3
const  unsigned  char thermocoupleSO_4 = 4; // DB-9 pin 3
const  unsigned  char thermocoupleSO_5 = 5; // DB-9 pin 3
const  unsigned  char thermocoupleSO_6 = 6; // DB-9 pin 3
const  unsigned  char thermocoupleSO_7 = 7; // DB-9 pin 3
const  unsigned  char thermocoupleSO_8 = 8; // DB-9 pin 3
const  unsigned  char thermocoupleSO_9 = 13; // DB-9 pin 3
const  unsigned  char thermocoupleA0 = 12;  // DB-9 pin 6  Address 0
const  unsigned  char thermocoupleA1 = 11;  // DB-9 pin 7  Address 1
const  unsigned  char thermocoupleA2 = 10;  // DB-9 pin 8  Address 2
const  unsigned  char thermocoupleA3 = 9;  // DB-9 pin 9  Address 3
int SO[] = {2,3,4,5,6,7,8,13};
int hubCount = 8;
int ADD[] = {12, 11, 10, 9};
unsigned long loopRestart;

const  unsigned  char thermocoupleCLK = 15;  // DB-9 pin 2  (A1 = 15)
const  unsigned  char thermocoupleE1 = 14;  // DB-9 pin 4  Address E1

MAX31855 MAX31855(SO, hubCount, ADD, thermocoupleCLK); 

void  setup()
{
  pinMode(thermocoupleE1, OUTPUT);
  digitalWrite(thermocoupleE1, LOW);

  Serial.begin(57600);
  loopRestart = millis();
}

void PrintDouble(double value)
{
  char str[11];
  dtostrf(value, 6,2, str);
  strcat(str, ", \0");
  Serial.print(str);
}

void  loop()
{ 
  double value[17]; 
  while(millis() < loopRestart)
  { 
    delay(5);  
  }
  
  loopRestart += 150;  // restart loop every 100 ms. 
  MAX31855.ReadAllData(true);

  for(int j=0; j<hubCount; j++)
  {
    value[0] = MAX31855.GetAverageJunctionCelsius(j);
    if(value[0] > 100 || value[0] == 0)
      continue;  // this hub is not connected, try next hub

    Serial.print(j); // print hub ID
    Serial.print(", ");
    PrintDouble(value[0]);
  
    int valid = 0;
    for(int i=1; i<17; i++)
    {
      value[i] = MAX31855.GetThermocoupleCelsius(j,i-1);
      if(value[i] > -10 && value[i] < FAULT_OPEN && value[i] != 0)
      {
        valid++;
      }
    }
    
    if(valid)
    {
      for(int i=1; i<17; i++)
      {
        PrintDouble(value[i]);
      }
    }

    Serial.println();
  }
}




