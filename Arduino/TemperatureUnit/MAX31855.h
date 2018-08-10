#ifndef	MAX31855_H
#define MAX31855_H

#if	ARDUINO >= 100
	#include "Arduino.h"
#else  
	#include "WProgram.h"
#endif

#define	FAULT_OPEN		10000
#define	FAULT_SHORT_GND	10001
#define	FAULT_SHORT_VCC	10002	
#define	INITIALIZED		10003	
#define JUNCTION_TEMPERATURE_OUTSIDE_RANGE 10004

#define MAX_HUBS 10
#define MAX_ADDRESSES 16

class MAX31855
{
	public:
		MAX31855(int SO[], int hubCount, int ADD[], unsigned char SCK);
	
		double	GetThermocoupleCelsius(unsigned char hubID, unsigned char address);
		double	GetThermocoupleFahrenheit(unsigned char hubID, unsigned char address);
		double	GetJunctionCelsius(unsigned char hubID, unsigned char address);
		double	GetJunctionFahrenheit(unsigned char hubID, unsigned char address);
    double  GetAverageJunctionCelsius(unsigned char hubID);
    double  GetAverageJunctionFahrenheit(unsigned char hubID);
		void ReadData(unsigned char address, bool overwrite); // read all available data on the currently selected address.
    int ReadAllData(bool overwrite);
		
	private:
		int* _so;
    int _hubCount;
    int* _add;
    int _addCount;
		unsigned char _sck;
		
		double _dataThermocouple[MAX_HUBS][MAX_ADDRESSES];
		double _dataJunction[MAX_HUBS][MAX_ADDRESSES];
		
		double GetThermocoupleCelsius(unsigned long);
		double GetJunctionCelsius(unsigned long);
};

#endif
