/*******************************************************************************
* Thermocouple to serial for MAX31855 example 
* Version: 1.00
* Date: 26-12-2011
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
*
*******************************************************************************/
// ***** INCLUDES *****
#include  "MAX31855.h"

const String serialNumber = "R5gKr0fb";
const String boardFamily = "Temperature hubard16";
const String boardVersion = "1";
const String boardSoftware = "2018-12-18 07:37";

// ***** PIN DEFINITIONS *****
const  unsigned  char ChipSelect = 10; 
const  unsigned  char ClockPin = 9; 
int SO[] = {12,11,2,4,6,7,8,18, 13,19,3,5,14,15,16,17};
bool junction = false;
String inString = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";    // string to hold user input

unsigned long timeStamp = 0; 

MAX31855 MAX31855(SO, ClockPin, ChipSelect); 

void  setup()
{
  Serial.begin(115200);
  inString = "";
  printSerial();
}

void  loop()
{ 
  if(millis() < timeStamp)
    return;  // max 10 Hz
    
  timeStamp = millis() + 950;
  
  double value[33]; 
  MAX31855.ReadAllData(true);

  value[0] = MAX31855.GetAverageJunctionCelsius();
  if(value[0] > 150 || value[0] == 0)
  {
    Serial.println("Error: board junction temperature outside range");
    return;  
  }

  PrintDouble(value[0]);

  int valid = 0;
  for(int i=1; i<17; i++)
  {
    value[i] = MAX31855.GetPortCelsius(i-1);
    if(value[i] > -10 && value[i] < FAULT_OPEN && value[i] != 0)
    {
      valid++;
    }
  }

  int columns = 17;
  if(junction)
  {
    columns = 33;
    for(int i=17; i<33; i++)
    {
      value[i] = MAX31855.GetJunctionCelsius(i-1);
    }
  }
  
  if(valid || junction)
  {
    for(int i=1; i<columns; i++)
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
      inString += (char)inChar;
  }
  else if (inChar == 13) 
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
}
