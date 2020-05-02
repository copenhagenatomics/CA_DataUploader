/*******************************************************************************
* Thermocouple to serial for MAX31855 example */
/* Date: 26-12-2011
* Company: Rocket Scream Electronics
* Website: www.rocketscream.com
* 
* Version: 2 - 3
* Date: 26-08-2018
* Company: Copenhagen Atomics
* Website: www.copenhagenatomics.com
* 
* This is an example of using the MAX31855 library for Arduino to read 
* temperature from a thermocouple and send the reading to serial interfacec. 
* Please check our wiki (www.github.com/copenhagenatomics) for more information on 
* using this piece of library.
*
* This example code is licensed under Creative Commons Attribution-ShareAlike 
* 3.0 Unported License.
*
* Revision  Description
* ========  ===========
* 1.00      Initial public release.
* 2.00      Copenhagen Atomics Temperature sensor 4xRJ45 board. 
* 3.00      Copenhagen Atomics Temperature sensor hubard16.
* 4.00      10 K-type connectors
* 5.00      Added CloseCube_Si70 Written by AA (MIT license)
*
*******************************************************************************/

#define softwareVersion "6.1"
//#define _serialNumber "Alwk32vv"
//#define _productType "SwitchBoard4x8A"
//#define _mcuFamily "Arduino Nano 3.0 Ali"

// ***** INCLUDES *****
#include <Wire.h>
#include  "MAX31855.h"
#include "readEEPROM.h"
#include "ClosedCube_Si7051.h"

// ***** CALIBRATION VALUES *******
double calibrateMul[] = {1,1,1,1,1,1,1,1,1,1};
double calibrateOff[] = {-1.5,-1.5,-1.5,-1.5,-1.5,-1.5,-1.5,-1.5,-1.5,-1.5};

// ***** PIN DEFINITIONS *****
const  unsigned  char ChipSelect = 10; 
const  unsigned  char ClockPin = 9; 
int SO[] = {12,11,2,4,6,7,8,3,13,5};
bool junction = false;
String inString = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";    // string to hold user input
unsigned long timeStamp = 0; 

ClosedCube_Si7051 si7051;
MAX31855 MAX31855(SO, ClockPin, ChipSelect); 

void  setup()
{
  Serial.begin(115200);
  inString = "";
  si7051.begin(0x40); // default I2C address is 0x40 and 14-bit measurement resolution
  printSerial();
  si7051.printSerial();
}

void  loop()
{ 
  if(millis() < timeStamp)
    return;  // max 10 Hz
    
  timeStamp = millis() + 100;
  
  int valid = 0;
  int columns = 10;
  double value[20]; 
  MAX31855.ReadAllData(true);

  value[0] = MAX31855.GetAverageJunctionCelsius();
  if(value[0] > 150 || value[0] == 0)
  {
    Serial.print("Error: board junction temperature outside range ");
    PrintDouble(value[0]);
    Serial.println();
    return;  
  }

  PrintDouble(value[0]); // print even when there are no thermocouples connected.
  if(si7051.IsOK())
  {
    value[1] = si7051.readTemperature();
    PrintDouble(value[1]);
  }

  for(int i=0; i<columns; i++)
  {
    value[i] = MAX31855.GetPortCelsius(i);
    if(value[i] > -10 && value[i] < FAULT_OPEN && value[i] != 0)
    {
      valid++;
    }
  }

  if(junction)
  {
    columns += 10;
    for(int i=10; i<columns; i++)
    {
      value[i] = MAX31855.GetJunctionCelsius(i-10);
    }
  }
  
  if(valid || junction)
  {
    for(int i=0; i<columns; i++)
    {
      if(i < 10 && value[i] < FAULT_OPEN && value[i] != 0) 
      {
        PrintDouble(value[i]*calibrateMul[i] + calibrateOff[i]);
      }
      else
      {
        PrintDouble(value[i]);
      }
    }
  }

  Serial.println();
  GetInput();
}

void PrintDouble(double value)
{
  char str[11];
  dtostrf(value, 6,2, str);
  strcat(str, ", \0");
  Serial.print(str);
}

double GetInput()
{
  char inChar = Serial.read();
  if (inChar == 'S' || inChar == 'e' || inChar == 'r' || inChar == 'i' || inChar == 'a' || inChar == 'l' || inChar == 'J' ||inChar == 'u' ||inChar == 'n' || inChar == 'c' ||inChar == 't' || inChar == 'o' ) 
  {
      // convert the incoming byte to a char and add it to the string:
      inString += inChar;
  }
  else if (inChar != -1) 
  {
      if(inString == "Serial")
      {
        printSerial();
      }
      else if(inString == "Junction")
      {
        junction = true;
      }

      inString = "";  
    return -1.0;
  }  

  return -1.0;
}


/*
void printSerial()
{
  Serial.print("Serial Number: ");
  Serial.println(serialNumber);
  Serial.print("Board Family: ");
  Serial.println(boardFamily);
  Serial.print("Board Version: ");
  Serial.println(boardVersion);
  Serial.print("Board Software: ");
  Serial.println(boardSoftware);
}*/
