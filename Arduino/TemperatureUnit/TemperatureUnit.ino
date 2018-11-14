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
* 2.00      Copenhagen Atomics Temperature sensor 4xRJ45 board. 
*
*******************************************************************************/
// ***** INCLUDES *****
#include  "MAX31855.h"

const String serialNumber = "AD8Kr0fb";
const String boardFamily = "Temperature 4xRJ45";
const String boardVersion = "1";
const String boardSoftware = "2018-22-10 11:37";

// ***** PIN DEFINITIONS *****
const  unsigned  char thermocoupleSO_0 = 4; // DB-9 pin 3
const  unsigned  char thermocoupleSO_1 = 5; // DB-9 pin 3
const  unsigned  char thermocoupleSO_2 = 6; // DB-9 pin 3
const  unsigned  char thermocoupleSO_3 = 7; // DB-9 pin 3
const  unsigned  char thermocoupleA0 = 9;  // Address 0
const  unsigned  char thermocoupleA1 = 10;  // Address 1
const  unsigned  char thermocoupleA2 = 11;  // Address 2
const  unsigned  char thermocoupleA3 = 12;  // Address 3
int SO[] = {4,5,6,7};
int hubCount = 4;
int ADD[] = {9, 10, 12, 11};
bool junction = false;
String inString = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";    // string to hold user input

const  unsigned  char thermocoupleCLK = 8; 
unsigned long timeStamp; 

MAX31855 MAX31855(SO, hubCount, ADD, thermocoupleCLK); 

void  setup()
{
  Serial.begin(115200);
  inString = "";
  printSerial();
}

void  loop()
{ 
  timeStamp = millis() + 95;
  GetInput();
  
  double value[33]; 
  MAX31855.ReadAllData(true);

  for(int j=0; j<hubCount; j++)
  {
    value[0] = MAX31855.GetAverageJunctionCelsius(j);
    if(value[0] > 150 || value[0] == 0)
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

    int columns = 17;
    if(junction)
    {
      columns = 33;
      for(int i=17; i<33; i++)
      {
        value[i] = MAX31855.GetJunctionCelsius(j,i-1);
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
    while(millis() < timeStamp)
    {
      delay(1); // delay to match 100 miliseconds between read operations
    }
  }
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
