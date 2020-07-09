using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib
{
    public class SerialNumberMapper : IDisposable
    {
        public List<MCUBoard> McuBoards = new List<MCUBoard>();
        private static string[] _serialPorts = RpiVersion.GetUSBports();
        private CALogLevel _logLevel;

        private static ManagementEventWatcher arrival;

        private static ManagementEventWatcher removal;

        public event EventHandler<PortsChangedArgs> PortsChanged;

        public SerialNumberMapper()
        {
            _logLevel = IOconfFile.GetOutputLevel();

            foreach (string name in _serialPorts)
            {
                try
                {
                    var mcu = new MCUBoard(name, 115200);
                    if(mcu.UnableToRead)
                        mcu = new MCUBoard(name, 9600);

                    SetUnknownSerialNumber(mcu);
                    McuBoards.Add(mcu);

                    if (mcu.productType is null)
                        mcu.productType = GetStringFromDmesg(mcu.PortName);

                    if (_logLevel == CALogLevel.Debug)
                        CALog.LogInfoAndConsoleLn(LogID.A, mcu.ToDebugString(Environment.NewLine));
                    else
                        CALog.LogInfoAndConsoleLn(LogID.A, mcu.ToString());

                    MonitorDeviceChanges();
                }
                catch(UnauthorizedAccessException ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex.Message}" + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex.ToString()}" + Environment.NewLine);
                }
            }
        }
        
        private void SetUnknownSerialNumber(MCUBoard mcu)
        {
            if (mcu.serialNumber.IsNullOrEmpty())
            {
                if (!IsAscale(mcu) && !IsALuminox(mcu))
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

            var line = mcu.SafeReadLine();
            if (line.StartsWith("+0") || line.StartsWith("-0"))
            {
                mcu.serialNumber = "Scale1";
                mcu.productType = "Scale";
            }

            if (McuBoards.Any(x => x.serialNumber.StartsWith("Scale")))
                mcu.serialNumber = "Scale" + (McuBoards.Count(x => x.serialNumber.StartsWith("Scale")) + 1);

            return mcu.serialNumber?.StartsWith("Scale") ?? false;
        }

        // "O 0213.1 T +21.0 P 1019 % 020.92 e 0000"
        private const string _luminoxPattern = "O (([0-9]*[.])?[0-9]+) T ([+-]?([0-9]*[.])?[0-9]+) P (([0-9]*[.])?[0-9]+) % (([0-9]*[.])?[0-9]+) e ([0-9]*)";

        private bool IsALuminox(MCUBoard mcu)
        {
            if (!mcu.IsOpen)
                return false;

            var line = mcu.SafeReadLine();
            if (Regex.Match(line, _luminoxPattern).Success)
            {
                // I should implement the real command to get the serial number. 
                mcu.serialNumber = "Oxygen1";
                mcu.productType = "Luminox O2";
            }

            if (McuBoards.Any(x => x.serialNumber.StartsWith("Oxygen")))
                mcu.serialNumber = "Oxygen" + (McuBoards.Count(x => x.serialNumber.StartsWith("Oxygen")) + 1);

            return mcu.serialNumber?.StartsWith("Oxygen") ??false;
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
        /// Return a list of MCU boards where productType contains the input string. 
        /// </summary>
        /// <param name="productType">type of boards to look for. (Case sensitive)</param>
        /// <returns></returns>
        public List<MCUBoard> ByProductType(string productType)
        {
            return McuBoards.Where(x => x.productType != null && x.productType.Contains(productType)).ToList();
        }

        /// <summary>
        /// Return a list of MCU boards where IOconfName contains the input string. 
        /// </summary>
        /// <param name="boxname">then name of boards to look for. (Case sensitive)</param>
        /// <returns></returns>
        public List<MCUBoard> ByIOconfName(string boxname)
        {
            return McuBoards.Where(x => x.BoxName != null && x.BoxName.Contains(boxname)).ToList();
        }

        // Mono: Exception: System.NotImplementedException: The method or operation is not implemented.
        // Mono: /build/mono-5.18.0.268/mcs/class/System.Management/System.Management/EventQuery.cs:38
        // Mono: https://github.com/mono/mono/blob/master/mcs/class/System.Management/System.Management/WqlEventQuery.cs
        private void MonitorDeviceChanges()
        {
            if (!RpiVersion.IsWindows())
                return;

            try
            {
                var deviceArrivalQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");
                var deviceRemovalQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

                arrival = new ManagementEventWatcher(deviceArrivalQuery);
                removal = new ManagementEventWatcher(deviceRemovalQuery);

                arrival.EventArrived += (o, args) => RaisePortsChangedIfNecessary(EventType.Insertion);
                removal.EventArrived += (sender, eventArgs) => RaisePortsChangedIfNecessary(EventType.Removal);

                // Start listening for events
                arrival.Start();
                removal.Start();
            }
            catch (ManagementException)
            {

            }
        }

        private void RaisePortsChangedIfNecessary(EventType eventType)
        {
            try
            {
                lock (_serialPorts)
                {
                    var availableSerialPorts = SerialPort.GetPortNames();
                    var newPorts = availableSerialPorts.Except(_serialPorts);
                    var removedPorts = _serialPorts.Except(availableSerialPorts);
                    if (!_serialPorts.SequenceEqual(availableSerialPorts))
                    {
                        _serialPorts = availableSerialPorts;
                        if(newPorts.Any())
                            PortsChanged?.Invoke(this, new PortsChangedArgs(eventType, McuBoards.Where(x => newPorts.Contains(x.PortName))));
                        else
                            PortsChanged?.Invoke(this, new PortsChangedArgs(eventType, McuBoards.Where(x => removedPorts.Contains(x.PortName))));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Serial port error: " + ex.ToString());
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if(arrival != null) arrival.Stop();
            if(removal != null) removal.Stop();
            if (!disposedValue)
            {
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        #endregion
    }

    public enum EventType
    {
        Insertion,
        Removal,
    }

    public class PortsChangedArgs : EventArgs
    {
        public EventType EventType { get; private set; }

        public List<MCUBoard> MCUBoards { get; private set; }

        public PortsChangedArgs(EventType eventType, IEnumerable<MCUBoard> mcuBoards)
        {
            EventType = eventType;
            MCUBoards = mcuBoards.ToList();
        }
    }
}
