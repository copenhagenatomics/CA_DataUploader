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

class MAX31855
{
	public:
		MAX31855(int SO[], unsigned char SCK, unsigned char CS);
	
		double	GetPortCelsius(unsigned char port);
		double	GetPortFahrenheit(unsigned char port);
		double	GetJunctionCelsius(unsigned char port);
		double	GetJunctionFahrenheit(unsigned char port);
    double  GetAverageJunctionCelsius();
    double  GetAverageJunctionFahrenheit();
    int ReadAllData(bool overwrite);
		
	private:
		int* _so;
    int* _add;
		unsigned char _sck;
    unsigned char _cs;
		
		double _dataThermocouple[16];
		double _dataJunction[16];
		
		double ExtractPortCelsius(unsigned long);
		double ExtractJunctionCelsius(unsigned long);
};

#endif
