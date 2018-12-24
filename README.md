# CA_DataUploader
C# software package to upload temperature data from CA temperature hub product

This software is generally made for the 16 port K-type temperature sensors product from Copenhagen Atomics. 
You can connect several of these to your computer via USB and have 64 or 128 temperature sensors upload data to a web graph in real time. 

More information:

http://www.copenhagenatomics.com/products.php

http://www.copenhagenatomics.com/pdf/Temperature%20Data%20Logger%20Datasheet.pdf

____

## How to run this datalogger from RaspberryPi  Raspbain:

You first need to install Mono

First run the 4 lines from this file:

https://www.mono-project.com/download/stable/#download-lin-raspbian

Then you need to increase your swap file, described here:

https://stackoverflow.com/questions/53214618/failed-to-precompile-microsoft-codeanalysis-csharp-on-armbian-stretch

I did it like this:
> sudo nano /etc/dphys-swapfile

then I change CONF_SWAPFILE to 500

save and exit with CTRL+o, CTRL+x

> sudo dphys-swapfile setup

> sudo /etc/init.d/dphys-swapfile stop

> sudo /etc/init.d/dphys-swapfile start


Then I run
 
> sudo apt-get install mono-complete

After this you can download the release zip file here form github or FTP it to your RaspberryPi and run it with this command:

> sudo mono --debug CA_AverageTemperature.exe
