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
                DataReceived += new SerialDataReceivedEventHandler(port_DataReceived);

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

        private void port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // Show all the incoming data in the port's buffer in the output window
            _readBuffer.Enqueue(ReadExisting());
        }

        public string SafeReadLine()
        {
            try
            {
                if (IsOpen)
                    return ReadLine();

                Thread.Sleep(100);
                Open();

                if (IsOpen)
                    return ReadLine();

            }
            catch (Exception)
            {
                CALog.LogErrorAndConsole(LogID.A, $"Error while reading from serial port: {PortName} {productType} {serialNumber}");
                if (_safeLimit-- == 0) throw;
            }

            return string.Empty;
        }

        public string SafeReadLine2()
        {
            try
            {
                var stop = DateTime.Now.AddMilliseconds(ReadTimeout);
                string result = _overshoot;  // stuff from last read;

                while(DateTime.Now < stop)
                {
                    if(_readBuffer.Count > 0)
                        result += _readBuffer.Dequeue();

                    if (result.Contains("\n"))
                        return RemoveOvershoot(result);

                    Thread.Sleep(10);
                }

                var message = $"Timeout while reading from serial port: {PortName} {productType} {serialNumber}";
                CALog.LogErrorAndConsole(LogID.A, message);
                if (_safeLimit-- == 0) throw new Exception(message);

            }
            catch (Exception)
            {
                CALog.LogErrorAndConsole(LogID.A, $"Error while reading from serial port: {PortName} {productType} {serialNumber}");
                if (_safeLimit-- == 0) throw;
            }

            return string.Empty;
        }

        private string RemoveOvershoot(string result)
        {
            if (result.EndsWith("\n") || result.EndsWith("\r"))
                return result;

            var pos = Math.Max(result.IndexOf("\n"), result.IndexOf("\r"));
            if(pos != -1)
            {
                _overshoot = result.Substring(pos+1);
                return result.Substring(0, pos+1);
            }

            throw new Exception("Error in the RemoveOvershoot algorithm: " + result);
        }

        public void SafeWriteLine(string msg)
        {
            try
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
            catch (Exception)
            {
                CALog.LogErrorAndConsole(LogID.A, $"Unable to write to serial port: {PortName} {productType} {serialNumber}");
                if (_safeLimit-- == 0) throw;
            };
        }

        public void SafeClose()
        {
            if (IsOpen)
                Close();
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
                        var input = ReadLine();
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
