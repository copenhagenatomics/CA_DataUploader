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

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/CA_AverageTemperature.exe.png)

or

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/CA_DataUploader.exe.png)

## How to Debug your system. 

First make sure the hardware is connected correctly. 

To do this open Putty.exe or some other terminal program 
https://www.chiark.greenend.org.uk/~sgtatham/putty/latest.html

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/putty.exe.png)

Next make sure you have setup your IO.conf file correctly. You need something like this, but with another name in the first line 
Put something other than JoeScottTest1 (e.g. BenBrownTest1)

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/IO.conf.png)

You also need to put your own name, email and a password of your choice in the next line

When you have started CA_DataUploader.exe and it say: "Now connected to server", then you need to visit

www.copenhagenatomics.com/plots to login and see your plot. Your login with the same email and password as you entered in IO.conf 

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/OnlineChart.png)




