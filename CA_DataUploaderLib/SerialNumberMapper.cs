using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    public sealed class SerialNumberMapper : IDisposable
    {
        public List<MCUBoard> McuBoards = new List<MCUBoard>();
        private static string[] _serialPorts = RpiVersion.GetUSBports();
        private CALogLevel _logLevel = CALogLevel.Normal;

        private static ManagementEventWatcher arrival;

        private static ManagementEventWatcher removal;
        private static int _detectedUnknownBoards;

        public event EventHandler<PortsChangedArgs> PortsChanged;

        public SerialNumberMapper()
        {
            if(File.Exists("IO.conf"))
                _logLevel = IOconfFile.GetOutputLevel();

            Parallel.ForEach(_serialPorts, name =>
            {
                try
                {
                    int baudRate = File.Exists("IO.conf") ?
                        (IOconfFile.GetMap().SingleOrDefault(m => m.USBPort == name)?.BaudRate ?? 115200) : 
                        115200;
                    var mcu = new MCUBoard(name, baudRate);
                    if (mcu.UnableToRead && mcu.BaudRate != 9600)
                        mcu = new MCUBoard(name, 9600); // for luminox & scale sensors not yet in IOconfFile / or that moved usb ports due to (un)plugged devices
                    if (mcu.serialNumber.IsNullOrEmpty())
                        mcu.serialNumber = "unknown" + Interlocked.Increment(ref _detectedUnknownBoards);

                    McuBoards.Add(mcu);

                    if (mcu.productType is null)
                        mcu.productType = GetStringFromDmesg(mcu.PortName);

                    string logline = _logLevel == CALogLevel.Debug ? mcu.ToDebugString(Environment.NewLine) : mcu.ToString();
                    CALog.LogInfoAndConsoleLn(LogID.A, logline);

                    MonitorDeviceChanges();
                }
                catch (UnauthorizedAccessException ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex.Message}" + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to open {name}, Exception: {ex.ToString()}" + Environment.NewLine);
                }
            });
        }

        private string GetStringFromDmesg(string portName)
        {
            if (portName.StartsWith("COM"))
                return null;

            portName = portName.Substring(portName.LastIndexOf('/') + 1);
            var result = DULutil.ExecuteShellCommand($"dmesg | grep {portName}").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            return result.FirstOrDefault(x => x.EndsWith(portName))?.StringBetween(": ", " to ttyUSB");
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
            if (!OperatingSystem.IsWindows())
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

        public void Dispose()
        { // class is sealed without unmanaged resources, no need for the full disposable pattern.
            if (!OperatingSystem.IsWindows()) return;
            arrival?.Stop();
            removal?.Stop();
            arrival = null;
            removal = null;
        }
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
