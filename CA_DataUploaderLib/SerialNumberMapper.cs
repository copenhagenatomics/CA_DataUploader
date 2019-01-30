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
