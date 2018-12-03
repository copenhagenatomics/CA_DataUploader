using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class MCUBoard
    {
        public string serialNumber;
        public const string serialNumberHeader = "Serial Number: ";
        public string boardFamily;
        public const string boardFamilyHeader = "Board Family: ";
        public string boardVersion;
        public const string boardVersionHeader = "Board Version: ";
        public string boardSoftware;
        public const string boardSoftwareHeader = "Board Software: ";
        public string portName;
        public int baudRate;

        public bool IsEmpty()
        {
            return (serialNumber is null) || (boardFamily is null) || (boardVersion is null) || (boardSoftware is null);
        }

        public override string ToString()
        {
            return $"{serialNumberHeader}{serialNumber}{Environment.NewLine}{boardFamilyHeader}{boardFamily}{Environment.NewLine}{boardVersionHeader}{boardVersion}{Environment.NewLine}{boardSoftwareHeader}{boardSoftware}{Environment.NewLine}Baud rate: {baudRate}{Environment.NewLine}Port name: {portName}{Environment.NewLine}";
        }
    }

    public class SerialNumberMapper
    {
        public Dictionary<string, MCUBoard> _dictionary = new Dictionary<string, MCUBoard>();

        public SerialNumberMapper(bool debug)
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string name in ports)
            {
                var port = new SerialPort(name, 115200);
                try
                {              
                    port.Open();
                    port.WriteLine("Serial");
                    var mcu = new MCUBoard { portName = name, baudRate = 115200, serialNumber = "unknown" + (_dictionary.Count()+1) };
                    var stop = DateTime.Now.AddSeconds(2);
                    while(DateTime.Now < stop && mcu.IsEmpty())
                    {
                        if (port.BytesToRead > 0)
                        {
                            var input = port.ReadLine();

                            if (input.Contains(MCUBoard.serialNumberHeader))
                                mcu.serialNumber = input.Substring(input.IndexOf(MCUBoard.serialNumberHeader) + MCUBoard.serialNumberHeader.Length).Trim();
                            if (input.Contains(MCUBoard.boardFamilyHeader))
                                mcu.boardFamily = input.Substring(input.IndexOf(MCUBoard.boardFamilyHeader) + MCUBoard.boardFamilyHeader.Length).Trim();
                            if (input.Contains(MCUBoard.boardVersionHeader))
                                mcu.boardVersion = input.Substring(input.IndexOf(MCUBoard.boardVersionHeader) + MCUBoard.boardVersionHeader.Length).Trim();
                            if (input.Contains(MCUBoard.boardSoftwareHeader))
                                mcu.boardSoftware = input.Substring(input.IndexOf(MCUBoard.boardSoftwareHeader) + MCUBoard.boardSoftwareHeader.Length).Trim();
                        }
                    }

                    port.Close();
                    _dictionary.Add(mcu.serialNumber, mcu);

                    if(mcu.boardFamily is null)
                        mcu.boardFamily = GetStringFromDmesg(mcu.portName);

                    if (debug)
                        Console.WriteLine(mcu.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unable to open {name}, Exception: {ex.Message}");
                    port.Close();
                }
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
            var line = result.First(x => x.EndsWith(portName));
            return line.StringBetween(": ", " to ttyUSB");
        }

        public MCUBoard this[string key]
        {
            get
            {
                return _dictionary[key];
            }
        }

        public List<MCUBoard> ByFamily(string family)
        {
            return _dictionary.Where(x => x.Value.boardFamily != null && x.Value.boardFamily.Contains(family)).Select(x => x.Value).ToList();
        }
    }
}
