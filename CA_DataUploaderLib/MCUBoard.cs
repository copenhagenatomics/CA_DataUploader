using CA_DataUploaderLib.Extensions;
using System;
using System.Diagnostics;
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

        public DateTime PortOpenTimeStamp;

        public MCUBoard(string name, int baudrate)
        {

            try
            {
                BaudRate = baudrate;
                PortName = name;
                Open();

                PortOpenTimeStamp = DateTime.UtcNow;
                ReadTimeout = 2000;
                WriteTimeout = 2000;
                // DiscardInBuffer();
                // DiscardOutBuffer();
                WriteLine("Serial");
                var stop = DateTime.Now.AddSeconds(2);
                while (IsEmpty() && DateTime.Now < stop)
                {

                    if (BytesToRead > 0)
                    {
                        var input = ReadLine();
                        if (!input.Contains(" 10000.00"))
                            Debug.Print("input: " + input);

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
            catch (Exception ex)
            {
                Console.WriteLine("fejl: " + ex.ToString());
                throw;
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
            return $"Port name: {PortName}{seperator}Baud rate: {BaudRate}{seperator}Port open timestamp: {PortOpenTimeStamp}{seperator}{serialNumberHeader}{serialNumber}{seperator}{boardFamilyHeader}{boardFamily}{seperator}{boardVersionHeader}{boardVersion}{seperator}{boardSoftwareHeader}{boardSoftware}{seperator}";
        }
    }
}
