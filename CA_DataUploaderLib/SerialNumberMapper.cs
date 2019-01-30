using CA_DataUploaderLib.Extensions;
using LoopComponents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;

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

    public class SerialNumberMapper
    {
        public ObservableCollection<MCUBoard> McuBoards = new ObservableCollection<MCUBoard>();
        // private SerialPortWatcher _watcher = new SerialPortWatcher();

        public SerialNumberMapper(bool debug)
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string name in ports)
            {
                try
                {
                    var mcu = new MCUBoard(name, 115200);
                    SetUnknownSerialNumber(mcu);
                    McuBoards.Add(mcu);

                    if (mcu.boardFamily is null)
                        mcu.boardFamily = GetStringFromDmesg(mcu.PortName);

                    if (debug)
                        CALog.LogInfoAndConsoleLn(LogID.A, mcu.ToString());
                }
                catch(UnauthorizedAccessException ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, $"Unable to open {name}, Exception: {ex.Message}" + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, $"Unable to open {name}, Exception: {ex.ToString()}" + Environment.NewLine);
                }
            }

        }

        private void SetUnknownSerialNumber(MCUBoard mcu)
        {
            if (mcu.serialNumber.IsNullOrEmpty())
            {
                mcu.serialNumber = "unknown1";
                if (McuBoards.Any())
                    mcu.serialNumber = "unknown" + (McuBoards.Count(x => x.serialNumber.StartsWith("unknown")) + 1);
            }
        }

        private string GetStringFromDmesg(string portName)
        {
            if (portName.StartsWith("COM"))
                return null;

            portName = portName.Substring(portName.LastIndexOf('/') + 1);
            var info = new ProcessStartInfo();
            info.FileName = "sudo";
            info.Arguments = "dmesg";

            info.UseShellExecute = false;
            info.CreateNoWindow = true;

            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            var p = Process.Start(info);
            p.WaitForExit(1000);
            var result = p.StandardOutput.ReadToEnd().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var line = result.FirstOrDefault(x => x.EndsWith(portName));
            if (line == null)
                return null;

            return line.StringBetween(": ", " to ttyUSB");
        }

        public List<MCUBoard> ByFamily(string family)
        {
            return McuBoards.Where(x => x.boardFamily != null && x.boardFamily.Contains(family)).ToList();
        }
    }
}
