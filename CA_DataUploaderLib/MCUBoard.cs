using CA_DataUploaderLib.Extensions;
using System;
using System.Threading;
using System.IO.Ports;
using System.Diagnostics;
using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

namespace CA_DataUploaderLib
{
    public class MCUBoard : SerialPort
    {
        private int _safeLimit = 100;
        private int _reconnectLimit = 100;

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
        public const string pcbVersionHeader = "PCB version: ";
        public const string boardVersionHeader = "Board Version: ";

        public string mcuFamily = null;
        public const string mcuFamilyHeader = "MCU Family: ";
        private readonly Regex _startsWithNumberRegex = new Regex(@"^(-|\d+)");
        public bool InitialConnectionSucceeded {get;} = false;

        public DateTime PortOpenTimeStamp;

        private DateTime _lastReopenTime;

        // "O 0213.1 T +21.0 P 1019 % 020.92 e 0000"
        private static readonly Regex _luminoxRegex = new Regex(
            "O (([0-9]*[.])?[0-9]+) T ([+-]?([0-9]*[.])?[0-9]+) P (([0-9]*[.])?[0-9]+) % (([0-9]*[.])?[0-9]+) e ([0-9]*)");
        private static int _luminoxSensorsDetected;
        private static readonly Regex _scaleRegex = new Regex("[+-](([0-9]*[.])?[0-9]+) kg"); // "+0000.00 kg"
        private static int _detectedScaleBoards;
        private static int _detectedUnknownBoards;

        public BoardSettings ConfigSettings { get; set; } = BoardSettings.Default;

        private MCUBoard(string name, int baudrate, bool skipBoardAutoDetection) 
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

                if (skipBoardAutoDetection)
                    InitialConnectionSucceeded = true;
                else
                    InitialConnectionSucceeded = ReadSerialNumber();

                if (File.Exists("IO.conf"))
                { // note that unlike the discovery at MCUBoard.OpenDeviceConnection that only considers the usb port, in here we can find the boards by the serial number too.
                    foreach (var ioconfMap in IOconfFile.GetMap())
                    {
                        if (ioconfMap.SetMCUboard(this))
                        {
                            BoxName = ioconfMap.BoxName;
                            ConfigSettings = ioconfMap.BoardSettings;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CALog.LogException(LogID.A, ex);
            }

            if (!InitialConnectionSucceeded)
            {
                Close();
                Thread.Sleep(100);
            }

        }

        public bool IsEmpty()
        {
            // pcbVersion is included in this list because at the time of writting is the last value in the readEEPROM header, 
            // which avoids the rest of the header being treated as "values".
            return serialNumber.IsNullOrEmpty() ||
                    productType.IsNullOrEmpty() ||
                    softwareVersion.IsNullOrEmpty() ||
                    pcbVersion.IsNullOrEmpty();  
        }
        
        public string SafeReadLine() => RunEnsuringConnectionIsOpen("ReadLine", ReadLine) ?? string.Empty;
        public bool SafeHasDataInReadBuffer() => RunEnsuringConnectionIsOpen("HasDataInReadBuffer", () => BytesToRead > 0);
        public void SafeWriteLine(string msg) => RunEnsuringConnectionIsOpen("WriteLine", () => WriteLine(msg));
        public string SafeReadExisting() => RunEnsuringConnectionIsOpen("ReadExisting", ReadExisting) ?? string.Empty;

        public void SafeClose()
        {
            lock (this)
            {
                if (IsOpen)
                    Close();
            }
        }

        public string ToDebugString(string seperator)
        {
            return $"{BoxNameHeader}{BoxName}{seperator}Port name: {PortName}{seperator}Baud rate: {BaudRate}{seperator}{serialNumberHeader}{serialNumber}{seperator}{productTypeHeader}{productType}{seperator}{pcbVersionHeader}{pcbVersion}{seperator}{softwareVersionHeader}{softwareVersion}{seperator}";
        }

        public override string ToString()
        {
            return $"{productTypeHeader}{productType,-20} {serialNumberHeader}{serialNumber,-12} Port name: {PortName}";
        }

        public string ToStringSimple(string seperator)
        {
            return $"{PortName}{seperator}{serialNumber}{seperator}{productType}";
        }

        /// <summary>
        /// Reopens the connection skipping the header.
        /// </summary>
        /// <param name="expectedHeaderLines">The amount of header lines expected.</param>
        /// <param name="secondsBetweenReopens">The amount of seconds to wait since the last reconnect attempt.</param>
        /// <returns><c>false</c> if the reconnect limit has been exceeded.</returns>
        /// <remarks>
        /// This method assumes only value lines start with numbers, 
        /// so it considers such a line to be past the header.
        /// 
        /// A log entry to <see cref="LogID.B"/> is added with the skipped header 
        /// and the bytes in the receive buffer 500ms after the port was opened again.
        /// 
        /// No exceptions are produced if there is a failure to reopen, although information about is added to log B.
        /// </remarks>
        public bool SafeReopen()
        {
            var lines = new List<string>();
            var bytesToRead500ms = 0;
            try
            {
                lock (this)
                {
                    TimeSpan timeSinceLastReopen = DateTime.Now.Subtract(_lastReopenTime);
                    if (timeSinceLastReopen.TotalSeconds < ConfigSettings.SecondsBetweenReopens)
                    {
                        CALog.LogData(LogID.B, $"(Reopen) skipped {PortName} {productType} {serialNumber} - time since last reopen {timeSinceLastReopen}");
                        return true;
                    }

                    if (_reconnectLimit-- < 0)
                        return false;

                    if (IsOpen)
                    {
                        CALog.LogData(LogID.B, $"(Reopen) Closing port {PortName} {productType} {serialNumber}");
                        Close();
                        Thread.Sleep(500);
                    }
                    
                    CALog.LogData(LogID.B, $"(Reopen) opening port {PortName} {productType} {serialNumber}");
                    Open();
                    Thread.Sleep(500);

                    bytesToRead500ms = BytesToRead;
                    CALog.LogData(LogID.B, $"(Reopen) skipping {ConfigSettings.ExpectedHeaderLines} header lines for port {PortName} {productType} {serialNumber} ");
                    lines = SkipExtraHeaders(ConfigSettings.ExpectedHeaderLines);
                    _lastReopenTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                _lastReopenTime = DateTime.Now;
                CALog.LogErrorAndConsoleLn(
                    LogID.B, 
                    $"Failure reopening port {PortName} {productType} {serialNumber} - {bytesToRead500ms} bytes in read buffer.{Environment.NewLine}Skipped header lines '{string.Join("§",lines)}'",
                    ex);
            }

            CALog.LogData(LogID.B, $"Reopened port {PortName} {productType} {serialNumber} - {bytesToRead500ms} bytes in read buffer.{Environment.NewLine}Skipped header lines '{string.Join("§", lines)}'");
            return true;
        }

        public static MCUBoard OpenDeviceConnection(string name)
        {
            // note this map is only found by usb, for map entries configured by serial we use auto detection with standard baud rates instead.
            var map = File.Exists("IO.conf") ? IOconfFile.GetMap().SingleOrDefault(m => m.USBPort == name) : null;
            var initialBaudrate = map != null && map.BaudRate != 0 ? map.BaudRate : 115200;
            var mcu = new MCUBoard(name, initialBaudrate, (map?.BoardSettings ?? BoardSettings.Default).SkipBoardAutoDetection);
            if (!mcu.InitialConnectionSucceeded)
                mcu = OpenWithAutoDetection(name, initialBaudrate);
            if (mcu.serialNumber.IsNullOrEmpty())
                mcu.serialNumber = "unknown" + Interlocked.Increment(ref _detectedUnknownBoards);
            if (mcu.InitialConnectionSucceeded && map != null && map.BaudRate != 0 && mcu.BaudRate != map.BaudRate)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unexpected baud rate for {map}. Board info {mcu}");
            if (mcu.ConfigSettings.ExpectedHeaderLines > 8)
                mcu.SkipExtraHeaders(mcu.ConfigSettings.ExpectedHeaderLines - 8);
            return mcu;
        }

        private List<string> SkipExtraHeaders(int extraHeaderLinesToSkip)
        {
            var lines = new List<string>(extraHeaderLinesToSkip);
            for (int i = 0; i < ConfigSettings.ExpectedHeaderLines; i++)
            {
                var line = ReadLine().Trim();
                lines.Add(line);
                if (ConfigSettings.Parser.MatchesValuesFormat(line))
                    break; // we are past the header.
            }

            return lines;
        }

        private static MCUBoard OpenWithAutoDetection(string name, int previouslyAttemptedBaudRate)
        { 
            var skipAutoDetection = false;
            if (previouslyAttemptedBaudRate == 115200)
                return new MCUBoard(name, 9600, skipAutoDetection);
            if (previouslyAttemptedBaudRate == 9600)
                return new MCUBoard(name, 115200, skipAutoDetection);
            var mcu = new MCUBoard(name, 115200, skipAutoDetection);
            return mcu.InitialConnectionSucceeded ? mcu : new MCUBoard(name, 9600, skipAutoDetection);
        }

        /// <returns><c>true</c> if we were able to read</returns>
        private bool ReadSerialNumber()
        {
            WriteLine("Serial");
            var stop = DateTime.Now.AddSeconds(5);
            bool sentSerialCommandTwice = false;
            bool ableToRead = false;
            while (IsEmpty() && DateTime.Now < stop)
            {

                if (BytesToRead > 0)
                {
                    try
                    {
                        // we use the regular ReadLine to capture timeouts early 
                        // (also only 1 thread uses the board during instance initialization)
                        var input = ReadLine();
                        if (Debugger.IsAttached && input.Length > 0)
                        {
                            CALog.LogColor(LogID.A, ConsoleColor.Green, input);
                        }

                        ableToRead = input.Length >= 2;
                        if (input.Contains(MCUBoard.serialNumberHeader))
                            serialNumber = input.Substring(input.IndexOf(MCUBoard.serialNumberHeader) + MCUBoard.serialNumberHeader.Length).Trim();
                        else if (input.Contains(MCUBoard.boardFamilyHeader))
                            productType = input.Substring(input.IndexOf(MCUBoard.boardFamilyHeader) + MCUBoard.boardFamilyHeader.Length).Trim();
                        else if (input.Contains(MCUBoard.productTypeHeader))
                            productType = input.Substring(input.IndexOf(MCUBoard.productTypeHeader) + MCUBoard.productTypeHeader.Length).Trim();
                        else if (input.Contains(MCUBoard.boardVersionHeader))
                            pcbVersion = input.Substring(input.IndexOf(MCUBoard.boardVersionHeader) + MCUBoard.boardVersionHeader.Length).Trim();
                        else if (input.Contains(MCUBoard.pcbVersionHeader))
                            pcbVersion = input.Substring(input.IndexOf(MCUBoard.pcbVersionHeader) + MCUBoard.pcbVersionHeader.Length).Trim();
                        else if (input.Contains(MCUBoard.boardSoftwareHeader))
                            softwareCompileDate = input.Substring(input.IndexOf(MCUBoard.boardSoftwareHeader) + MCUBoard.boardSoftwareHeader.Length).Trim();
                        else if (input.Contains(MCUBoard.softwareCompileDateHeader))
                            softwareCompileDate = input.Substring(input.IndexOf(MCUBoard.softwareCompileDateHeader) + MCUBoard.softwareCompileDateHeader.Length).Trim();
                        else if (input.Contains(MCUBoard.boardSoftwareHeader))
                            softwareVersion = input.Substring(input.IndexOf(MCUBoard.boardSoftwareHeader) + MCUBoard.boardSoftwareHeader.Length).Trim();
                        else if (input.Contains(MCUBoard.softwareVersionHeader))
                            softwareVersion = input.Substring(input.IndexOf(MCUBoard.softwareVersionHeader) + MCUBoard.softwareVersionHeader.Length).Trim();
                        else if (input.Contains(MCUBoard.mcuFamilyHeader))
                            mcuFamily = input.Substring(input.IndexOf(MCUBoard.mcuFamilyHeader) + MCUBoard.mcuFamilyHeader.Length).Trim();
                        else if (DetectLuminoxSensor(input)) // avoid waiting for a never present serial for luminox sensors 
                            return true;
                        else if (DetectAscale(input))
                            return true;
                        else if (input.Contains("MISREAD") && !sentSerialCommandTwice && serialNumber == null)
                        {
                            WriteLine("Serial");
                            CALog.LogInfoAndConsoleLn(LogID.A, $"Received misread without any serial on port {PortName} - re-sending serial command");
                            sentSerialCommandTwice = true;
                        }
                    }
                    catch (TimeoutException ex)
                    {
                        CALog.LogColor(LogID.A, ConsoleColor.Red, $"Unable to read from {PortName} ({BaudRate}): " + ex.Message);
                        break;
                    }
                    catch (Exception ex)
                    {
                        CALog.LogColor(LogID.A, ConsoleColor.Red, $"Unable to read from {PortName} ({BaudRate}): " + ex.Message);
                    }
                }
            }

            return ableToRead;
        }

        private bool DetectLuminoxSensor(string line)
        {
            if (!_luminoxRegex.IsMatch(line)) return false;
            // later on we should get the actual serial number. 
            serialNumber = "Oxygen" + Interlocked.Increment(ref _luminoxSensorsDetected);
            productType = "Luminox O2";
            return true;
        }

        private bool DetectAscale(string line)
        {
            if (!_scaleRegex.IsMatch(line)) return false;

            serialNumber = "Scale" + Interlocked.Increment(ref _detectedScaleBoards);
            productType = "Scale";
            return true;
        }

        private TResult RunEnsuringConnectionIsOpen<TResult>(string actionName, Func<TResult> action) 
        {
            try
            {
                lock (this)
                {
                    if (IsOpen)
                        return action();

                    Thread.Sleep(100);
                    Open();

                    if (IsOpen)
                        return action();
                }

                CALog.LogErrorAndConsoleLn(LogID.A, $"Failed to open port to {actionName}(): {PortName} {productType} {serialNumber}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unable to {actionName}() from serial port: {PortName} {productType} {serialNumber}{Environment.NewLine}", ex);
                if (_safeLimit-- <= 0) throw;
            }

            return default;
        }

        private void RunEnsuringConnectionIsOpen(string actionName, Action action) => 
            RunEnsuringConnectionIsOpen(actionName, () => { action(); return true; });
    }
}
