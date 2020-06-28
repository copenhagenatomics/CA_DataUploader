﻿using CA_DataUploaderLib.Extensions;
using System;
using System.Threading;
using System.IO.Ports;
using System.Diagnostics;
using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    public class MCUBoard : SerialPort
    {
        private int _safeLimit = 100;

        public string BoxName = null;
        public const string BoxNameHeader = "IOconf.Map BoxName: ";

        public string serialNumber = null;
        public const string serialNumberHeader = "Serial Number: ";

        public string productType = null;
        public const string boardFamilyHeader = "Board Family: ";
        public const string productTypeHeader = "Product Type: ";

        public string softwareVersion = null;
        public const string softwareVersionHeader = "Software Version: ";
        public const string boardSoftwareHeader = "Board Software: ";

        public string softwareCompileDate = null;
        public const string softwareCompileDateHeader = "Software Compile Date: ";

        public string pcbVersion = null;
        public const string pcbVersionHeader = "PCB Version: ";
        public const string boardVersionHeader = "Board Version: ";

        public string mcuFamily = null;
        public const string mcuFamilyHeader = "MCU Family: ";

        public bool UnableToRead = true;

        public DateTime PortOpenTimeStamp;

        private Queue<string> _readBuffer = new Queue<string>();
        private string _overshoot = string.Empty;

        public MCUBoard(string name, int baudrate) // : base(name, baudrate, 0, 8, 1, 0)
        {

            try
            {
                if(IsOpen)
                {
                    throw new Exception($"Something is wrong, port {name} is already open. You may need to reboot!");
                }

                BaudRate = 1;
                DtrEnable = true;
                RtsEnable = true;
                BaudRate = baudrate;
                PortName = name;
                productType = "NA";
                PortOpenTimeStamp = DateTime.UtcNow;
                ReadTimeout = 2000;
                WriteTimeout = 2000;
                // DataReceived += new SerialDataReceivedEventHandler(port_DataReceived);

                Open();

                ReadSerialNumber();

                foreach(var ioconfMap in IOconfFile.GetMap())
                {
                    if (ioconfMap.SetMCUboard(this))
                        BoxName = ioconfMap.BoxName;
                }
            }
            catch (Exception ex)
            {
                CALog.LogException(LogID.A, ex);
            }

            if (UnableToRead)
            {
                Close();
                Thread.Sleep(100);
            }

        }

        public bool IsEmpty()
        {
            return serialNumber.IsNullOrEmpty() ||
                    productType.IsNullOrEmpty() ||
                    softwareVersion.IsNullOrEmpty();
        }

        public override string ToString()
        {
            return ToString(Environment.NewLine);
        }

        public string SafeReadLine()
        {
            try
            {
                lock (this)
                {
                    if (IsOpen)
                        return ReadLine();

                    Thread.Sleep(100);
                    Open();

                    if (IsOpen)
                        return ReadLine();
                }
            }
            catch (Exception)
            {
                var frame = new StackTrace().GetFrame(1);
                CALog.LogErrorAndConsoleLn(LogID.A, $"Error while reading from serial port: {PortName} {productType} {serialNumber} {frame.GetMethod().DeclaringType.Name}.{frame.GetMethod().Name}{Environment.NewLine}");
                if (_safeLimit-- == 0) throw;
            }

            return string.Empty;
        }

        public bool SafeHasDataInReadBuffer()
        {
            try
            {
                lock (this)
                {
                    if (IsOpen)
                        return BytesToRead > 0;

                    Thread.Sleep(100);
                    Open();

                    if (IsOpen)
                        return BytesToRead > 0;
                }
            }
            catch (Exception)
            {
                var frame = new StackTrace().GetFrame(1);
                CALog.LogErrorAndConsoleLn(LogID.A, $"Error while checking serial port read buffer: {PortName} {productType} {serialNumber} {frame.GetMethod().DeclaringType.Name}.{frame.GetMethod().Name}{Environment.NewLine}");
                if (_safeLimit-- == 0) throw;
            }

            return false;
        }

        public void SafeWriteLine(string msg)
        {
            try
            {
                lock (this)
                {
                    if (IsOpen)
                    {
                        WriteLine(msg);
                        return;
                    }

                    Thread.Sleep(100);
                    Open();

                    if (IsOpen)
                    {
                        WriteLine(msg);
                        return;
                    }
                }
            }
            catch (Exception)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to write to serial port: {PortName} {productType} {serialNumber}{Environment.NewLine}");
                if (_safeLimit-- == 0) throw;
            }
        }

        public string SafeReadExisting()
        {
            try
            {
                lock (this)
                {
                    if (IsOpen)
                        return ReadExisting();

                    Thread.Sleep(100);
                    Open();

                    if (IsOpen)
                        return ReadExisting();
                }
            }
            catch (Exception)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to ReadExisting() from serial port: {PortName} {productType} {serialNumber}{Environment.NewLine}");
                if (_safeLimit-- == 0) throw;
            }

            return string.Empty;
        }

        public void SafeClose()
        {
            lock (this)
            {
                if (IsOpen)
                    Close();
            }
        }

        public string ToString(string seperator)
        {
            return $"{BoxNameHeader}{BoxName}{seperator}Port name: {PortName}{seperator}Baud rate: {BaudRate}{seperator}{serialNumberHeader}{serialNumber}{seperator}{productTypeHeader}{productType}{seperator}{pcbVersionHeader}{pcbVersion}{seperator}{softwareVersionHeader}{softwareVersion}{seperator}";
        }

        public string ToStringSimple(string seperator)
        {
            return $"{PortName}{seperator}{serialNumber}{seperator}{productType}";
        }

        private void ReadSerialNumber()
        {
            // CALog.LogColor(LogID.A, ConsoleColor.Green, Environment.NewLine + "Sending Serial request");
            WriteLine("Serial");
            var stop = DateTime.Now.AddSeconds(2);
            while (IsEmpty() && DateTime.Now < stop)
            {

                if (BytesToRead > 0)
                {
                    try
                    {
                        var input = SafeReadLine();
                        if (Debugger.IsAttached && input.Length > 0)
                        {
                            //stop = DateTime.Now.AddMinutes(1);
                            CALog.LogColor(LogID.A, ConsoleColor.Green, input);
                        }

                        UnableToRead = false;
                        if (input.Contains(MCUBoard.serialNumberHeader))
                            serialNumber = input.Substring(input.IndexOf(MCUBoard.serialNumberHeader) + MCUBoard.serialNumberHeader.Length).Trim();

                        if (input.Contains(MCUBoard.boardFamilyHeader))
                            productType = input.Substring(input.IndexOf(MCUBoard.boardFamilyHeader) + MCUBoard.boardFamilyHeader.Length).Trim();
                        if (input.Contains(MCUBoard.productTypeHeader))
                            productType = input.Substring(input.IndexOf(MCUBoard.productTypeHeader) + MCUBoard.productTypeHeader.Length).Trim();

                        if (input.Contains(MCUBoard.boardVersionHeader))
                            pcbVersion = input.Substring(input.IndexOf(MCUBoard.boardVersionHeader) + MCUBoard.boardVersionHeader.Length).Trim();
                        if (input.Contains(MCUBoard.pcbVersionHeader))
                            pcbVersion = input.Substring(input.IndexOf(MCUBoard.pcbVersionHeader) + MCUBoard.pcbVersionHeader.Length).Trim();

                        if (input.Contains(MCUBoard.boardSoftwareHeader))
                            softwareCompileDate = input.Substring(input.IndexOf(MCUBoard.boardSoftwareHeader) + MCUBoard.boardSoftwareHeader.Length).Trim();
                        if (input.Contains(MCUBoard.softwareCompileDateHeader))
                            softwareCompileDate = input.Substring(input.IndexOf(MCUBoard.softwareCompileDateHeader) + MCUBoard.softwareCompileDateHeader.Length).Trim();

                        if (input.Contains(MCUBoard.boardSoftwareHeader))
                            softwareVersion = input.Substring(input.IndexOf(MCUBoard.boardSoftwareHeader) + MCUBoard.boardSoftwareHeader.Length).Trim();
                        if (input.Contains(MCUBoard.softwareVersionHeader))
                            softwareVersion = input.Substring(input.IndexOf(MCUBoard.softwareVersionHeader) + MCUBoard.softwareVersionHeader.Length).Trim();

                        if (input.Contains(MCUBoard.mcuFamilyHeader))
                            mcuFamily = input.Substring(input.IndexOf(MCUBoard.mcuFamilyHeader) + MCUBoard.mcuFamilyHeader.Length).Trim();
                    }
                    catch (Exception ex)
                    {
                        CALog.LogColor(LogID.A, ConsoleColor.Red, $"Unable to read from {PortName}: " + ex.Message);
                    }

                }
            }
        }
    }
}
