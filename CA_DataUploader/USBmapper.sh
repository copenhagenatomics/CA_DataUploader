#!/bin/bash
count=0
FILE=99-usbdevices.rules
DIR=/etc/udev/rules.d/

#ACTION=="add", SUBSYSTEM=="usb", RUN=="~/bin/USBmapper"
#KERNEL=="sd*", ATTRS{vendor}=="Yoyodyne", ATTRS{model}=="XYZ42", ATTRS{serial}=="123465789", RUN+="/pathto/script"


#ACTION=="add", ATTRS{idVendor}=="****", ATTRS{idProduct}=="****", RUN+="/home/pi/bin/USBmapper"

if test -f "$FILE"; then
    rm -f $FILE
fi


printf "ACTION==\"add\", ATTRS{idVendor}==\"****\", ATTRS{idProduct}==\"****\", RUN+=\"/home/pi/bin/USBmapper  2&>>/tmp/usb.log\"\n\n" >> $FILE

{ 
  for i in $(ls /sys/bus/usb/devices); do 
	if [[ $i == *":"* ]]; then
        printf "KERNELS==\"%s\", SUBSYSTEM==\"tty\", SYMLINK+=\"USB" "$i" >> $FILE
        printf "%s\"\n\n" "$count" >> $FILE
        ((count++))
	fi
  done
}

 cp $FILE $DIR$FILE
udevadm control --reload-rules && udevadm trigger


