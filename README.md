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

This software runs on .Net 6 and you do not need Mono any more. 

You can download the latest release files here.

Or build the source code with the .net 6 sdk installed (note to build from VS you need to upgrade to 2022):
* on a command line in the repository folder, run (replacing linux-arm with your target platform): 
  * dotnet publish CA_DataUploader -c Release -p:PublishSingleFile=true --runtime linux-arm --self-contained
  * other options for target are: linux-x64, osx-x64, win-arm, win-x64, win-x86
* copy the files in the listed publish folder to a new directory on the target device.
* on linux, first run: chmod +x USBmapper.sh && sudo ./USBmapper.sh
* run the CA_DataUploader and follow the instructions it gives

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/CA_DataUploader.exe.png)

## How to Debug your system. 

First make sure the hardware is connected correctly. 

To do this open Putty.exe or some other terminal program (if you go with Putty, you might want to try MTPuTTY with it)
https://www.chiark.greenend.org.uk/~sgtatham/putty/latest.html

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/putty.exe.png)

Next make sure you have setup your IO.conf file correctly. You need something like this, but with another name in the first line 
Put something other than JoeScottTest1 (e.g. BenBrownTest1)

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/IO.conf.png)

You also need to put your own name, email and a password of your choice in the next line

When you have started CA_DataUploader.exe and it say: "Now connected to server", then you need to visit

www.copenhagenatomics.com/plots to login and see your plot. Your login with the same email and password as you entered in IO.conf 

![alt text](https://github.com/copenhagenatomics/CA_DataUploader/blob/master/ScreenShots/OnlineChart.png)




