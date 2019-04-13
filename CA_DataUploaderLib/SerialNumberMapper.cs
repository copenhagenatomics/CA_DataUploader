using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;

namespace CA_DataUploaderLib
{
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
                    if(mcu.UnableToRead)
                        mcu = new MCUBoard(name, 9600);

                    if (!mcu.UnableToRead)
                    {
                        SetUnknownSerialNumber(mcu);
                        McuBoards.Add(mcu);

                        if (mcu.boardFamily is null)
                            mcu.ReadSerialNumber();  // I am not sure this is needed any more. 

                        if (mcu.boardFamily is null)
                            mcu.boardFamily = GetStringFromDmesg(mcu.PortName);

                        if (debug)
                            CALog.LogInfoAndConsoleLn(LogID.A, mcu.ToString());
                    }
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
                if (!IsAscale(mcu))
                {
                    mcu.serialNumber = "unknown1";

                    if (McuBoards.Any(x => x.serialNumber.StartsWith("unknown")))
                        mcu.serialNumber = "unknown" + (McuBoards.Count(x => x.serialNumber.StartsWith("unknown")) + 1);
                }
            }
        }

        private bool IsAscale(MCUBoard mcu)
        {
            if (!mcu.IsOpen)
                return false;

            var line = mcu.ReadLine();
            if (line.StartsWith("+0") || line.StartsWith("-0"))
                mcu.serialNumber = "Scale1";

            if (McuBoards.Any(x => x.serialNumber.StartsWith("Scale")))
                mcu.serialNumber = "Scale" + (McuBoards.Count(x => x.serialNumber.StartsWith("Scale")) + 1);

            return mcu.serialNumber.StartsWith("Scale");
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

        /// <summary>
        /// Return a list of MCU boards where boardFamily contains the input string. 
        /// </summary>
        /// <param name="family">type of boards to look for. (Case sensitive)</param>
        /// <returns></returns>
        public List<MCUBoard> ByFamily(string family)
        {
            return McuBoards.Where(x => x.boardFamily != null && x.boardFamily.Contains(family)).ToList();
        }
    }
}
