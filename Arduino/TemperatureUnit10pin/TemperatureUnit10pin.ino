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

ClosedCube_Si7051 si7051;


// ***** PIN DEFINITIONS *****
const  unsigned  char ChipSelect = 10; 
const  unsigned  char ClockPin = 9; 
// int SO[] = {12,11,2,4,6,7,8,18, 13,19,3,5,14,15,16,17}; //18 port
int SO[] = {12,11,2,4,6,7,8,3,13,5};
bool junction = false;
String inString = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";    // string to hold user input

unsigned long timeStamp = 0; 

MAX31855 MAX31855(SO, ClockPin, ChipSelect); 

void  setup()
{
  Serial.begin(115200);
  inString = "";
  //si7051.begin(0x40); // default I2C address is 0x40 and 14-bit measurement resolution
  printSerial();
  //si7051.printSerial();
}

void  loop()
{ 
  if(millis() < timeStamp)
    return;  // max 10 Hz
    
  timeStamp = millis() + 100;
  
  double value[33]; 
  MAX31855.ReadAllData(true);

  value[0] = MAX31855.GetAverageJunctionCelsius();
  if(value[0] > 150 || value[0] == 0)
  {
    Serial.print("Error: board junction temperature outside range ");
    PrintDouble(value[0]);
    Serial.println();
    return;  
  }

  PrintDouble(value[0]);

  //value[1] = si7051.readTemperature();
  //PrintDouble(value[1]);

  int valid = 0;
  int columns = 11;
  for(int i=columns-10; i<columns; i++)
  {
    value[i] = MAX31855.GetPortCelsius(i-1);
    if(value[i] > -10 && value[i] < FAULT_OPEN && value[i] != 0)
    {
      valid++;
    }
  }

  if(junction)
  {
    columns = 33;
    for(int i=11; i<33; i++)
    {
      value[i] = MAX31855.GetJunctionCelsius(i-1);
    }
  }
  
  if(valid || junction)
  {
    for(int i=columns-10; i<columns; i++)
    {
      PrintDouble(value[i]);
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
