/*******************************************************************************
* MAX31855 Library
* Version: 2.0
* Date: Feb-2018
* Company: Th Engineering, based on initial work by Rocket Scream Electronics
* Website: www.theng.dk
*
* This is a MAX31855 library for Arduino Nano. 
*
* This library is licensed under Creative Commons Attribution-ShareAlike 3.0 
* Unsuported License.
*
*******************************************************************************/
#include  "MAX31855.h"

MAX31855::MAX31855(int SO[], int hubCount, int ADD[], unsigned char SCK)
{
  _so = SO;
  _hubCount = hubCount;
  _add = ADD;
  _sck = SCK;

  // MAX31855 data pin
  for(int i=0; i<_hubCount; i++)
  {
    pinMode(_so[i], INPUT);
  }

  // MAX31855 address pin
  for(int i=0; i<4; i++)
  {
    pinMode(_add[i], OUTPUT);
    digitalWrite(_add[i], LOW);
  }

  // MAX31855 clock input pin
  pinMode(_sck, OUTPUT);
  digitalWrite(_sck, LOW);
  
  // initialize data arrays;
  for(int hubID=0; hubID<MAX_HUBS; hubID++)
  for(int address=0; address<MAX_ADDRESSES; address++)
  {
    _dataThermocouple[hubID][address] = INITIALIZED;
    _dataJunction[hubID][address] = INITIALIZED; 
  }
}

double MAX31855::GetThermocoupleCelsius(unsigned char hubID, unsigned char address)
{
  return _dataThermocouple[hubID][address];
}

double MAX31855::GetJunctionCelsius(unsigned char hubID, unsigned char address)
{
  return _dataJunction[hubID][address];
}

double MAX31855::GetThermocoupleFahrenheit(unsigned char hubID, unsigned char address)
{
  // Convert Degree Celsius to Fahrenheit
  return (_dataThermocouple[hubID][address] * 9.0/5.0)+ 32; 
}

double MAX31855::GetJunctionFahrenheit(unsigned char hubID, unsigned char address)
{
  // Convert Degree Celsius to Fahrenheit
  return (_dataJunction[hubID][address] * 9.0/5.0)+ 32; 
}

double MAX31855::GetAverageJunctionCelsius(unsigned char hubID)
{
  double sum = 0;
  for(int i=0; i<16; i++)
  {
    if(_dataJunction[hubID][i] < -10 || _dataJunction[hubID][i] > 100)
       return JUNCTION_TEMPERATURE_OUTSIDE_RANGE;
    sum += _dataJunction[hubID][i];
  }
  
  return sum/16.0;
}

double MAX31855::GetAverageJunctionFahrenheit(unsigned char hubID)
{
  // Convert Degree Celsius to Fahrenheit
  return (GetAverageJunctionCelsius(hubID) * 9.0/5.0)+ 32; 
}

double  MAX31855::GetThermocoupleCelsius(unsigned long data)
{
  double temperature = 0;
  
  // If fault is detected
  if (data & 0x00010000)
  {
    // Check for fault type (3 LSB)
    switch (data & 0x00000007)
    {
      // Open circuit 
      case 0x01:
        temperature = FAULT_OPEN;
        break;
      
      // Thermocouple short to GND
      case 0x02:
        temperature = FAULT_SHORT_GND;
        break;
      
      // Thermocouple short to VCC  
      case 0x04:
        temperature = FAULT_SHORT_VCC;
        break;
    }
  }
  // No fault detected
  else
  {
    // Retrieve thermocouple temperature data and strip redundant data
    data = data >> 18;
    // Bit-14 is the sign
    temperature = (data & 0x00001FFF);

    // Check for negative temperature   
    if (data & 0x00002000)
    {
      // 2's complement operation
      // Invert
      data = ~data; 
      // Ensure operation involves lower 13-bit only
      temperature = data & 0x00001FFF;
      // Add 1 to obtain the positive number
      temperature += 1;
      // Make temperature negative
      temperature *= -1; 
    }
    
    // Convert to Degree Celsius
    temperature *= 0.25;
  }
  
  return (temperature);
}

double  MAX31855::GetJunctionCelsius(unsigned long data)
{
  double  temperature = 0;
  
  // Strip fault data bits & reserved bit
  data = data >> 4;
  // Bit-12 is the sign
  temperature = (data & 0x000007FF);
  
  // Check for negative temperature
  if (data & 0x00000800)
  {
    // 2's complement operation
    // Invert
    data = ~data; 
    // Ensure operation involves lower 11-bit only
    temperature = data & 0x000007FF;
    // Add 1 to obtain the positive number
    temperature += 1; 
    // Make temperature negative
    temperature *= -1; 
  }
  
  // Convert to Degree Celsius
  return temperature * 0.0625;
}

/*******************************************************************************
* Name: readData
* Description: Shift in 32-bit of data from MAX31855 chip. Minimum clock pulse
*              width is 100 ns. No delay is required in this case.
*******************************************************************************/
void MAX31855::ReadData(unsigned char address, bool overwrite)
{
  unsigned long data[MAX_HUBS];
  
  // Clear data 
  for(int hubID=0; hubID<_hubCount; hubID++)
  { 
    data[hubID] = 0;
  }
 
  // Shift in 32-bit of data
  for (int bitCount = 31; bitCount >= 0; bitCount--)
  {
    digitalWrite(_sck, HIGH);
    
    for(int hubID=0; hubID<_hubCount; hubID++)
    {
      // If data bit is high
      if (digitalRead(_so[hubID]))
      {
        data[hubID] |= ((unsigned long)1 << bitCount); // Need to type cast data type to unsigned long, else compiler will truncate to 16-bit
      }       
    }

    // write LOW 4 times to make equal high and low period. 
    digitalWrite(_sck, LOW);
    digitalWrite(_sck, LOW);
    digitalWrite(_sck, LOW);
    digitalWrite(_sck, LOW);
    
  }
  
  for(int hubID=0; hubID<_hubCount; hubID++)
  {
    _dataJunction[hubID][address] = GetJunctionCelsius(data[hubID]);
    double temperature = GetThermocoupleCelsius(data[hubID]);
    if(overwrite || temperature < FAULT_OPEN || _dataThermocouple[hubID][address] == INITIALIZED)
    {
      _dataThermocouple[hubID][address] = temperature;
    }
  }
}

int MAX31855::ReadAllData(bool overwrite)
{
  for(int i=0; i<MAX_ADDRESSES; i++)
  {
    // set address
    digitalWrite(_add[0], HIGH && (i & B00000001));
    digitalWrite(_add[1], HIGH && (i & B00000010));
    digitalWrite(_add[2], HIGH && (i & B00000100));
    digitalWrite(_add[3], HIGH && (i & B00001000));
    ReadData(i, overwrite);
  }

  int nOKvalues=0;
  for(int hubID=0; hubID<MAX_HUBS; hubID++)
  for(int address=0; address<MAX_ADDRESSES; address++)
  {
    if(_dataThermocouple[hubID][address] < FAULT_OPEN) nOKvalues++;
  }

  return nOKvalues;
}
