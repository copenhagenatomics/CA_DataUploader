# CA_DataUploader
C# software package to upload temperature data from CA temperature hub product. It can also be used for other type of data.

This software is generally made for the 10 port K-type temperature sensors product from Copenhagen Atomics. 
You can connect several of these to your computer via USB and have 100+ temperature sensors upload data to a web graph in real time. 
Sample rate e.g. 10 Hz for all sensors. 

More information:

http://www.copenhagenatomics.com/products.php

http://www.copenhagenatomics.com/pdf/Temperature%20Data%20Logger%20Datasheet.pdf (need update)

____

## How to run this datalogger from RaspberryPi  Raspbain:

You first need to install Mono  (.Net Core 3.0 coming soon)

First run the 4 lines from this file:

https://www.mono-project.com/download/stable/#download-lin-raspbian

Then I run
 
> sudo apt-get install mono-complete

After this you can download the release zip file here form github or FTP it to your RaspberryPi and run it with this command:

> mono --debug CA_AverageTemperature.exe

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/CA_AverageTemperature.exe.png)

or

Run the CA_DataUploader.exe (on Windows or Linux)

> mono --debug CA_AverageTemperature.exe

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




