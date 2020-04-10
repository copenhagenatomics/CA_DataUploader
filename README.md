# CA_DataUploader
C# software package for the Copenhagen Atomics data logger, products and services. 

This software was originally made for the 10 port K-type temperature sensors product from Copenhagen Atomics called Hub10. 
You can connect several of these to your computer via USB and have 100+ temperature sensors upload data to a web graph in real time. 
Sample rate e.g. 10 Hz for all sensors. 

More information:

http://www.copenhagenatomics.com/products.php

https://www.copenhagenatomics.com/pdf/Hub10_DataLogger_Datasheet.pdf

____

## How to run this datalogger from RaspberryPi (Linux):

This software run on .Net Core 3.0 and you do not need Mono any more. 

You can download the latest release files here.


Or build the source code and copy the files from the output direct to a new directory you create on RaspberryPi.
\CA_DataUploader\CA_DataUploader\bin\Debug\netcoreapp3.0\linux-arm

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/CA_AverageTemperature.exe.png)

First you need to set a few files as executable
> chmod 755 USBmapper.sh
> chmod 755 LoopControl

> ./CA_AverageTemperature.exe

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




