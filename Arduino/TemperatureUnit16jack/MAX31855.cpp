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
#include	"MAX31855.h"

MAX31855::MAX31855(int SO[], unsigned char SCK, unsigned char CS)
{
	_so = SO;
	_sck = SCK;
  _cs = CS;

	// MAX31855 data pin
  for(int i=0; i<16; i++)
  {
	  pinMode(_so[i], INPUT);
  }

	// MAX31855 clock input pin
	pinMode(_sck, OUTPUT);
	digitalWrite(_sck, LOW);
	
	// MAX31855 chip select input pin
  pinMode(_cs, OUTPUT);
  digitalWrite(_cs, HIGH);

  // initialize data arrays;
	for(int port=0; port<16; port++)
	{
		_dataThermocouple[port] = INITIALIZED;
		_dataJunction[port] = INITIALIZED; 
	}
}

double MAX31855::GetPortCelsius(unsigned char port)
{
	return _dataThermocouple[port];
}

double MAX31855::GetJunctionCelsius(unsigned char port)
{
	return _dataJunction[port];
}

double MAX31855::GetPortFahrenheit(unsigned char port)
{
	// Convert Degree Celsius to Fahrenheit
	return (_dataThermocouple[port] * 9.0/5.0)+ 32; 
}

double MAX31855::GetJunctionFahrenheit(unsigned char port)
{
	// Convert Degree Celsius to Fahrenheit
	return (_dataJunction[port] * 9.0/5.0)+ 32; 
}

double MAX31855::GetAverageJunctionCelsius()
{
  double sum = 0;
  for(int i=0; i<16; i++)
  {
    if(_dataJunction[i] < -10 || _dataJunction[i] > 100)
       return JUNCTION_TEMPERATURE_OUTSIDE_RANGE;
    sum += _dataJunction[i];
  }
  
  return sum/16.0;
}

double MAX31855::GetAverageJunctionFahrenheit()
{
  // Convert Degree Celsius to Fahrenheit
  return (GetAverageJunctionCelsius() * 9.0/5.0)+ 32; 
}

double	MAX31855::ExtractPortCelsius(unsigned long data)
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

double	MAX31855::ExtractJunctionCelsius(unsigned long data)
{
	double	temperature = 0;
	
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
* Description: Shift in 32-bit of data from all 16 MAX31855 chips. Minimum clock pulse
*							 width is 100 ns. No delay is required in this case.
*******************************************************************************/
int MAX31855::ReadAllData(bool overwrite)
{
	unsigned long data[16];
	digitalWrite(_cs, LOW); // select all chips
 
	// Clear data 
	for(int port=0; port<16; port++)
	{	
		data[port] = 0;
	}
 
	// Shift in 32-bit of data
	for (int bitCount = 31; bitCount >= 0; bitCount--)
	{
		digitalWrite(_sck, HIGH);
		
		for(int port=0; port<16; port++)
		{
			// If data bit is high
			if (digitalRead(_so[port]))
			{
				data[port] |= ((unsigned long)1 << bitCount); // Need to type cast data type to unsigned long, else compiler will truncate to 16-bit
			}				
		}

    // write LOW 4 times to make equal high and low period. 
		digitalWrite(_sck, LOW);
    digitalWrite(_sck, LOW);
    digitalWrite(_sck, LOW);
    digitalWrite(_sck, LOW);
    
	}

  digitalWrite(_cs, HIGH); // deselect all chips
  
	for(int port=0; port<16; port++)
	{
		_dataJunction[port] = ExtractJunctionCelsius(data[port]);
		double temperature = ExtractPortCelsius(data[port]);
		if(overwrite || temperature < FAULT_OPEN || _dataThermocouple[port] == INITIALIZED)
		{
			_dataThermocouple[port] = temperature;
		}
	}

  int nOKvalues=0;
  for(int port=0; port<16; port++)
  {
    if(_dataThermocouple[port] < FAULT_OPEN) nOKvalues++;
  }

  return nOKvalues;
}
