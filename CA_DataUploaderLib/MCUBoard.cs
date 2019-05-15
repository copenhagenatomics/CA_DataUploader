using CA_DataUploaderLib.Extensions;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.IO.Ports;

namespace CA_DataUploaderLib
{
    public class MCUBoard : SerialPort
    {
        public string serialNumber = null;
        public const string serialNumberHeader = "Serial Number: ";
        public string boardFamily = null;
        public const string boardFamilyHeader = "Board Family: ";
        public string boardVersion = null;
        public const string boardVersionHeader = "Board Version: ";
        public string boardSoftware = null;
        public const string boardSoftwareHeader = "Board Software: ";
        public bool UnableToRead = true;

        public DateTime PortOpenTimeStamp;

        public MCUBoard(string name, int baudrate)
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
                PortOpenTimeStamp = DateTime.UtcNow;
                ReadTimeout = 2000;
                WriteTimeout = 2000;
                Open();

                ReadSerialNumber();
            }
            catch (IOException ex)
            {
                CALog.LogException(LogID.A, ex);
                DiscardInBuffer();
                DiscardOutBuffer();
                BaudRate = 1;
                Close();
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
                    boardFamily.IsNullOrEmpty() ||
                    boardVersion.IsNullOrEmpty() ||
                    boardSoftware.IsNullOrEmpty();
        }

        public override string ToString()
        {
            return ToString(Environment.NewLine);
        }

        public string ToString(string seperator)
        {
            return $"Port name: {PortName}{seperator}Baud rate: {BaudRate}{seperator}Port open timestamp (UTC): {PortOpenTimeStamp}{seperator}{serialNumberHeader}{serialNumber}{seperator}{boardFamilyHeader}{boardFamily}{seperator}{boardVersionHeader}{boardVersion}{seperator}{boardSoftwareHeader}{boardSoftware}{seperator}";
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
                    var input = ReadLine();
                    // CALog.LogColor(LogID.A, ConsoleColor.Green, input);

                    UnableToRead = false;
                    if (input.Contains(MCUBoard.serialNumberHeader))
                        serialNumber = input.Substring(input.IndexOf(MCUBoard.serialNumberHeader) + MCUBoard.serialNumberHeader.Length).Trim();
                    if (input.Contains(MCUBoard.boardFamilyHeader))
                        boardFamily = input.Substring(input.IndexOf(MCUBoard.boardFamilyHeader) + MCUBoard.boardFamilyHeader.Length).Trim();
                    if (input.Contains(MCUBoard.boardVersionHeader))
                        boardVersion = input.Substring(input.IndexOf(MCUBoard.boardVersionHeader) + MCUBoard.boardVersionHeader.Length).Trim();
                    if (input.Contains(MCUBoard.boardSoftwareHeader))
                        boardSoftware = input.Substring(input.IndexOf(MCUBoard.boardSoftwareHeader) + MCUBoard.boardSoftwareHeader.Length).Trim();
                }
            }
        }
    }
}
