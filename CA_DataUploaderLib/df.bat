cls

@echo off

echo.
echo ------- Existing disks in the system -------
echo.

for /f "usebackq tokens=1 delims=\"  %%d in (`fsutil volume list ^| find ":\"`) do (
	for /f "usebackq tokens=3 delims=:()" %%i in (`fsutil volume diskfree %%d ^| findstr /C:"of free bytes"`) do (
	    for /f "usebackq tokens=2 delims=:" %%f in (`fsutil fsinfo volumeinfo %%d ^| find "System Name"`) do (
		for /f "usebackq tokens=2 delims=-" %%t in (`fsutil fsinfo drivetype %%d`) do (
			echo %%d %%i	%%f	%%t
		)
	    )	    
	)
)

echo.
echo ----------- ^< End of the List^> ------------
echo.