using CA_DataUploaderLib.Extensions;
using System;
using System.Threading;
using System.IO.Ports;
using System.Diagnostics;
using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using System.Buffers;
using System.Text;

namespace CA_DataUploaderLib
{
    /// <remarks>
    /// Thread safety: reading and writing from 2 different threads is supported as long as only one thread reads and the other one writes.
    /// In addition to that, the Safe* methods must be used so that no operations run in parallel while trying to close/open the connection. 
    /// </remarks>
    public class MCUBoard
    {
        public string BoxName = null;
        public const string BoxNameHeader = "IOconf.Map BoxName: ";

        public string serialNumber = null;
        public const string serialNumberHeader = "Serial Number: ";

        public string productType = null;
        public const string boardFamilyHeader = "Board Family: ";
        public const string productTypeHeader = "Product Type: ";

        public string subProductType = null;
        public const string subProductTypeHeader = "Sub Product Type: ";

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
        public bool InitialConnectionSucceeded {get; private set;} = false;

        public const string CalibrationHeader = "Calibration: ";
        public string Calibration { get; private set; }
        public string UpdatedCalibration { get; private set; }

        private static int _detectedUnknownBoards;
        // the "writer" for this lock are operations that close/reopens the connection, while the readers are any other operation including SafeWriteLine.
        // This prevents operations being ran when the connections are being closed/reopened.
        private readonly AsyncReaderWriterLock _reconnectionLock = new AsyncReaderWriterLock();

        public BoardSettings ConfigSettings { get; set; } = BoardSettings.Default;
        public string PortName => port.PortName;
        private readonly SerialPort port;
        private PipeReader pipeReader;
        public delegate bool TryReadLineDelegate(ref ReadOnlySequence<byte> buffer, out string line);
        private TryReadLineDelegate TryReadLine;
        public delegate (bool finishedDetection, BoardInfo info) CustomProtocolDetectionDelegate(ReadOnlySequence<byte> buffer, string portName);
        private static readonly List<CustomProtocolDetectionDelegate> customProtocolDetectors = new List<CustomProtocolDetectionDelegate>();

        private MCUBoard(SerialPort port) 
        {
            this.port = port;
        }

        private async static Task<MCUBoard> Create(string name, int baudrate, bool skipBoardAutoDetection)
        {
            MCUBoard board = null;
            try
            {
                var port = new SerialPort(name);
                board = new MCUBoard(port);
                port.BaudRate = 1;
                port.DtrEnable = true;
                port.RtsEnable = true;
                port.BaudRate = baudrate;
                port.PortName = name;
                port.ReadTimeout = 2000;
                port.WriteTimeout = 2000;
                port.Open();
                board.pipeReader = PipeReader.Create(port.BaseStream);
                Thread.Sleep(30); // it needs to await that the board registers that the COM port has been opened before sending commands (work arounds issue when first opening the connection and sending serial).

                board.TryReadLine = board.TryReadAsciiLine; //this is the default which can be changed in ReadSerial based on the ThirdPartyProtocolDetection
                board.productType = "NA";

                if (skipBoardAutoDetection)
                    board.InitialConnectionSucceeded = true;
                else
                    board.InitialConnectionSucceeded = await board.ReadSerialNumber();

                if (File.Exists("IO.conf"))
                { // note that unlike the discovery at MCUBoard.OpenDeviceConnection that only considers the usb port, in here we can find the boards by the serial number too.
                    foreach (var ioconfMap in IOconfFile.GetMap())
                    {
                        if (ioconfMap.SetMCUboard(board))
                        {
                            board.BoxName = ioconfMap.BoxName;
                            board.ConfigSettings = ioconfMap.BoardSettings;
                            port.ReadTimeout = ioconfMap.BoardSettings.MaxMillisecondsWithoutNewValues;
                            await board.UpdateCalibration(board.ConfigSettings);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CALog.LogException(LogID.A, ex);
            }

            if (board != null && !board.InitialConnectionSucceeded)
            {
                board.pipeReader?.Complete();
                board.port.Close();
                Thread.Sleep(100);
            }
            else if (board != null && board.IsEmpty() && board.BoxName == null)
            {//note we don't log this for devices without serial that were mapped by usb (last check above)
                CALog.LogInfoAndConsoleLn(LogID.B, $"some data without serial detected for device at port {name} - {baudrate} / still connected");
            }

            return board;
        }

        private async Task<string> UpdateCalibration(BoardSettings configSettings)
        {
            string newCalibration = configSettings.Calibration;
            if (newCalibration == default || Calibration == newCalibration)
                return default; // ignore if there is no calibration in configuration or if the board already had the expected configuration

            var calibrationMessage = $"replaced board calibration '{Calibration}' with '{newCalibration}";
            CALog.LogInfoAndConsoleLn(LogID.A, $"{calibrationMessage}' - {ToShortDescription()}");
            await SafeWriteLine(newCalibration, CancellationToken.None);
            UpdatedCalibration = newCalibration;
            return calibrationMessage;
        }

        private bool IsEmpty()
        {
            // pcbVersion is included in this list because at the time of writting is the last value in the readEEPROM header, 
            // which avoids the rest of the header being treated as "values".
            return serialNumber.IsNullOrEmpty() ||
                    productType.IsNullOrEmpty() ||
                    softwareVersion.IsNullOrEmpty() ||
                    pcbVersion.IsNullOrEmpty();  
        }
        
        public async Task<string> SafeReadLine(CancellationToken token) => (await RunEnsuringConnectionIsOpen("ReadLine", ReadLine, token)) ?? string.Empty;
        public Task SafeWriteLine(string msg, CancellationToken token) => RunEnsuringConnectionIsOpen("WriteLine", () => port.WriteLine(msg), token);

        public async Task SafeClose(CancellationToken token)
        {
            await _reconnectionLock.AcquireWriterLock(token);
            try
            {
                await pipeReader.CompleteAsync();
                if (port.IsOpen)
                    port.Close();                
            }
            finally
            {
                _reconnectionLock.ReleaseWriterLock();
            }
        }

        public string ToDebugString(string seperator) => 
            $"{BoxNameHeader}{BoxName}{seperator}Port name: {PortName}{seperator}Baud rate: {port.BaudRate}{seperator}{serialNumberHeader}{serialNumber}{seperator}{productTypeHeader}{productType}{seperator}{pcbVersionHeader}{pcbVersion}{seperator}{softwareVersionHeader}{softwareVersion}{seperator}{CalibrationHeader}{Calibration}{seperator}";
        public override string ToString() => $"{productTypeHeader}{productType,-20} {serialNumberHeader}{serialNumber,-12} Port name: {PortName}";
        public string ToShortDescription() => $"{BoxName} {productType} {serialNumber} {PortName}";

        /// <summary>
        /// Reopens the connection skipping the header.
        /// </summary>
        /// <param name="expectedHeaderLines">The amount of header lines expected.</param>
        /// <returns><c>false</c> if the reconnect attempt failed.</returns>
        /// <remarks>
        /// This method assumes only value lines start with numbers, 
        /// so it considers such a line to be past the header.
        /// 
        /// A log entry to <see cref="LogID.B"/> is added with the skipped header 
        /// and the bytes in the receive buffer 500ms after the port was opened again.
        /// </remarks>
        public async Task<bool> SafeReopen(CancellationToken token)
        {
            var lines = new List<string>();
            var bytesToRead500ms = 0;
            await _reconnectionLock.AcquireWriterLock(token);
            try
            {
                await pipeReader.CompleteAsync();
                if (port.IsOpen)
                {
                    CALog.LogData(LogID.B, $"(Reopen) Closing port {PortName} {productType} {serialNumber}");
                    port.Close();
                    await Task.Delay(500, token);
                }
                
                CALog.LogData(LogID.B, $"(Reopen) opening port {PortName} {productType} {serialNumber}");
                port.Open();
                await Task.Delay(500, token);

                bytesToRead500ms = port.BytesToRead;
                pipeReader = PipeReader.Create(port.BaseStream);
                CALog.LogData(LogID.B, $"(Reopen) skipping {ConfigSettings.ExpectedHeaderLines} header lines for port {PortName} {productType} {serialNumber} ");
                lines = await SkipExtraHeaders(ConfigSettings.ExpectedHeaderLines);
            }
            catch (Exception ex)
            {
                CALog.LogError(
                    LogID.B, 
                    $"Failure reopening port {PortName} {productType} {serialNumber} - {bytesToRead500ms} bytes in read buffer.{Environment.NewLine}Skipped header lines '{string.Join("§",lines)}'",
                    ex);
                return false;
            }
            finally
            {
                _reconnectionLock.ReleaseWriterLock();
            }

            CALog.LogData(LogID.B, $"Reopened port {PortName} {productType} {serialNumber} - {bytesToRead500ms} bytes in read buffer.{Environment.NewLine}Skipped header lines '{string.Join("§", lines)}'");
            return true;
        }

        public async static Task<MCUBoard> OpenDeviceConnection(string name)
        {
            // note this map is only found by usb, for map entries configured by serial we use auto detection with standard baud rates instead.
            var map = File.Exists("IO.conf") ? IOconfFile.GetMap().SingleOrDefault(m => m.USBPort == name) : null;
            var initialBaudrate = map != null && map.BaudRate != 0 ? map.BaudRate : 115200;
            bool skipAutoDetection = (map?.BoardSettings ?? BoardSettings.Default).SkipBoardAutoDetection;
            var mcu = await Create(name, initialBaudrate, skipAutoDetection);
            if (!mcu.InitialConnectionSucceeded)
                mcu = await OpenWithAutoDetection(name, initialBaudrate);
            if (mcu.serialNumber.IsNullOrEmpty())
                mcu.serialNumber = "unknown" + Interlocked.Increment(ref _detectedUnknownBoards);
            if (mcu.InitialConnectionSucceeded && map != null && map.BaudRate != 0 && mcu.port.BaudRate != map.BaudRate)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unexpected baud rate for {map}. Board info {mcu}");
            if (mcu.ConfigSettings.ExpectedHeaderLines > 8)
                await mcu.SkipExtraHeaders(mcu.ConfigSettings.ExpectedHeaderLines - 8);
            return mcu;
        }

        private async Task<List<string>> SkipExtraHeaders(int extraHeaderLinesToSkip)
        {
            var lines = new List<string>(extraHeaderLinesToSkip);
            for (int i = 0; i < ConfigSettings.ExpectedHeaderLines; i++)
            {
                var line = await ReadLine();
                lines.Add(line);
                if (ConfigSettings.Parser.MatchesValuesFormat(line))
                    break; // we are past the header.
            }

            return lines;
        }

        private async static Task<MCUBoard> OpenWithAutoDetection(string name, int previouslyAttemptedBaudRate)
        { 
            var skipAutoDetection = false;
            if (previouslyAttemptedBaudRate == 115200)
                return await Create(name, 9600, skipAutoDetection);
            if (previouslyAttemptedBaudRate == 9600)
                return await Create(name, 115200, skipAutoDetection);
            var board = await Create(name, 115200, skipAutoDetection);
            return board.InitialConnectionSucceeded ? board : await Create(name, 9600, skipAutoDetection);
        }

        /// <returns><c>true</c> if we were able to read</returns>
        private async Task<bool> ReadSerialNumber()
        {
            var detectedInfo = await DetectThirdPartyProtocol();
            if (detectedInfo != default)
            {
                serialNumber = detectedInfo.SerialNumber;
                productType = detectedInfo.ProductType;
                TryReadLine = detectedInfo.LineParser;
                return true; 
            }

            port.WriteLine("Serial");
            bool sentSerialCommandTwice = false;
            bool ableToRead = false;
            var finishedReadingHeader = false;

            // A cancellation token is made here rather than a simple timer since the ReadAsync function can hang
            // if a device never sends a line for it to read.
            int millisecondsTimeout = 5000;
            using var cts = new CancellationTokenSource(millisecondsTimeout);
            var token = cts.Token;

            while (!finishedReadingHeader && !token.IsCancellationRequested)
            {
                try
                {
                    // we use the regular reads as only 1 thread uses the board during instance initialization
                    var res = await pipeReader.ReadAsync(token);

                    if (res.IsCanceled)
                        return ableToRead;

                    var buffer = res.Buffer;

                    while (IsEmpty() && TryReadLine(ref buffer, out var input))
                    {
                        if (Debugger.IsAttached && input.Length > 0)
                            CALog.LogColor(LogID.A, ConsoleColor.Green, input);

                        ableToRead |= input.Length >= 2;
                        input = input.Trim();
                        if (input.StartsWith(serialNumberHeader))
                            serialNumber = input[(input.IndexOf(serialNumberHeader) + serialNumberHeader.Length)..].Trim();
                        else if (input.StartsWith(boardFamilyHeader))
                            productType = input[(input.IndexOf(boardFamilyHeader) + boardFamilyHeader.Length)..].Trim();
                        else if (input.StartsWith(productTypeHeader))
                            productType = input[(input.IndexOf(productTypeHeader) + productTypeHeader.Length)..].Trim();
                        else if (input.StartsWith(boardVersionHeader))
                            pcbVersion = input[(input.IndexOf(boardVersionHeader) + boardVersionHeader.Length)..].Trim();
                        else if (input.StartsWith(pcbVersionHeader, StringComparison.InvariantCultureIgnoreCase))
                            pcbVersion = input[(input.IndexOf(pcbVersionHeader, StringComparison.InvariantCultureIgnoreCase) + pcbVersionHeader.Length)..].Trim();
                        else if (input.StartsWith(boardSoftwareHeader))
                            softwareCompileDate = input[(input.IndexOf(boardSoftwareHeader) + boardSoftwareHeader.Length)..].Trim();
                        else if (input.StartsWith(softwareCompileDateHeader))
                            softwareCompileDate = input[(input.IndexOf(softwareCompileDateHeader) + softwareCompileDateHeader.Length)..].Trim();
                        else if (input.StartsWith(boardSoftwareHeader))
                            softwareVersion = input[(input.IndexOf(boardSoftwareHeader) + boardSoftwareHeader.Length)..].Trim();
                        else if (input.StartsWith(softwareVersionHeader))
                            softwareVersion = input[(input.IndexOf(softwareVersionHeader) + softwareVersionHeader.Length)..].Trim();
                        else if (input.StartsWith(mcuFamilyHeader))
                            mcuFamily = input[(input.IndexOf(mcuFamilyHeader) + mcuFamilyHeader.Length)..].Trim();
                        else if (input.StartsWith(subProductTypeHeader))
                            subProductType = input[(input.IndexOf(subProductTypeHeader) + subProductTypeHeader.Length)..].Trim();
                        else if (input.Contains("MISREAD") && !sentSerialCommandTwice && serialNumber == null)
                        {
                            port.WriteLine("Serial");
                            CALog.LogInfoAndConsoleLn(LogID.A, $"Received misread without any serial on port {PortName} - re-sending serial command");
                            sentSerialCommandTwice = true;
                        }
                    }

                    if (!IsEmpty() && TryReadOptionalCalibration(ref buffer, out var calibration))
                    {
                        finishedReadingHeader = true;
                        Calibration = UpdatedCalibration = calibration;
                    }

                    // Tell the PipeReader how much of the buffer has been consumed.
                    pipeReader.AdvanceTo(buffer.Start, buffer.End);

                    if (res.IsCompleted)
                    {
                        CALog.LogColor(LogID.A, ConsoleColor.Red, $"Unable to read from {PortName} ({port.BaudRate}): pipe reader was closed");
                        break; // typically means the connection was closed.
                    }
                }
                catch (TimeoutException ex)
                {
                    CALog.LogColor(LogID.A, ConsoleColor.Red, $"Unable to read from {PortName} ({port.BaudRate}): " + ex.Message);
                    break;
                }
                catch (OperationCanceledException ex)
                {
                    CALog.LogColor(LogID.A, ConsoleColor.Red, $"Unable to read from {PortName} ({port.BaudRate}): " + ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    CALog.LogColor(LogID.A, ConsoleColor.Red, $"Unable to read from {PortName} ({port.BaudRate}): " + ex.Message);
                }
            }

            return ableToRead;
        }

        ///<returns>true if it finished attempting to read the optional calibration, or false if more data is needed</returns>
        private bool TryReadOptionalCalibration(ref ReadOnlySequence<byte> buffer, out string calibration)
        {
            calibration = default;
            var localBuffer = buffer; // take a copy to avoid unnecesarily throwing away data coming after the header
            if (!TryReadLine(ref localBuffer, out var input))
                return false; //we did not get a line, more data is needed
            if (input.Contains(CalibrationHeader, StringComparison.InvariantCultureIgnoreCase))
            {
                calibration = input[(input.IndexOf(CalibrationHeader, StringComparison.InvariantCultureIgnoreCase) + CalibrationHeader.Length)..].Trim();
                buffer = localBuffer; //avoids the calibration line being treated as data
            }
            return true;
        }

        /// <summary>detects if we are talking with a supported third party device</summary>
        private Task<BoardInfo> DetectThirdPartyProtocol() => 
            DetectThirdPartyProtocol(port.BaudRate, PortName, pipeReader);

        public static void AddCustomProtocol(CustomProtocolDetectionDelegate detector) => customProtocolDetectors.Add(detector ?? throw new ArgumentNullException(nameof(detector)));

        /// <summary>detects if we are talking with a supported third party device</summary>
        public static async Task<BoardInfo> DetectThirdPartyProtocol(
            int baudRate, string portName, PipeReader pipeReader)
        {
            if (baudRate != 9600) 
                return default; //all the third party devices currently supported are running at 9600<

            // A cancellation token is made here rather than a simple timer since the ReadAsync function can hang
            // if a device never sends a line for it to read.
            int millisecondsTimeout = 3000;
            using var cts = new CancellationTokenSource(millisecondsTimeout);
            var token = cts.Token;
            foreach (var detector in customProtocolDetectors)
            {
                while (!token.IsCancellationRequested) //only allow up to 3 seconds to detect a third party device
                {
                    try 
                    { 
                        var result = await pipeReader.ReadAsync(token); 
                    
                        var buffer = result.Buffer;

                        var (finishedDetection, detectedInfo) = detector(buffer, portName);
                        if (finishedDetection && detectedInfo != default)
                            return detectedInfo; //confirmed it is a third party, leave pipeReader data not examined so next read processes data available right away
                        if (finishedDetection && detectedInfo == default)
                            break; //move to next third party. Note data is not set as examined in the pipeReader so ReadAsync returns inmediately with already read data.

                        // set all data as examined as we need more data to finish detection
                        pipeReader.AdvanceTo(buffer.Start, buffer.End);

                        if (result.IsCompleted)
                            return default; //should only happen if the connection was closed
                    } 
                    catch (OperationCanceledException ex)
                    {
                        if (ex.CancellationToken == token)
                            break;
                        else
                            throw;
                    }
                }
            }
            return default;
        }

        private Task<string> ReadLine() => 
            ReadLine(pipeReader, PortName, ConfigSettings.MaxMillisecondsWithoutNewValues, TryReadLine);
        //exposing this one for testing purposes
        public static async Task<string> ReadLine(
            PipeReader pipeReader, string portName, int millisecondsTimeout, TryReadLineDelegate tryReadLine)
        {
            using var cts = new CancellationTokenSource(millisecondsTimeout);
            var token = cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var res = await pipeReader.ReadAsync(token);
                    if (res.IsCanceled)
                        throw new TimeoutException("timed out (soft)");
                    var buffer = res.Buffer;

                    //In the event that no message is parsed successfully, mark consumed as nothing and examined as the entire buffer. 
                    //This means the next call to ReadAsync will wait for more data to arrive.
                    //Note this still means the buffer keeps growing as we get more data until we get a message we can process,
                    //but normally a timeout would be hit and board reconnection would be initiated by the caller.
                    var consumed = buffer.Start;
                    var examined = buffer.End;

                    try
                    {
                        var readLine = tryReadLine(ref buffer, out var line);
                        consumed = buffer.Start; //we update consumed regardless of reading a line, as TryReadLineDelegate can decide to skip broken data / frames.
                        if (readLine)
                        {
                            //after reading a line, the call to tryReadLine above updates the buffer reference to start at the next line
                            //by pointing examined to it, we are signaling we can inmediately use the data in the buffer 
                            //so the first ReadAsync in the next ReadLine call does not wait for more data but returns inmediately.
                            //not doing this would be a memory leak if we already had a full line in the buffer (as we keep waiting for an extra line and over time the buffer keeps accumulating extra lines).
                            examined = buffer.Start;
                            return line; //we return a single line for now to keep a similar API before introducing the pipe reader, but would be fine to explore changing the API shape in the future
                        }

                        if (res.IsCompleted)
                            throw new ObjectDisposedException(portName, "closed connection detected");
                    }
                    finally
                    {
                        pipeReader.AdvanceTo(consumed, examined);  
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                if (ex.CancellationToken == token)
                    throw new TimeoutException("timed out (while reading)");
                throw;
            }

            throw new TimeoutException("timed out (idle)");
        }

        private bool TryReadAsciiLine(ref ReadOnlySequence<byte> buffer, out string line) => TryReadAsciiLine(ref buffer, out line, ConfigSettings.ValuesEndOfLineChar);
        public static bool TryReadAsciiLine(ref ReadOnlySequence<byte> buffer, out string line, char endOfLineChar) 
        {
            // Look for a EOL in the buffer.
            SequencePosition? position = buffer.PositionOf((byte)endOfLineChar);
            if (position == null)
            {
                line = default;
                return false;
            }

            // Skip the line + the end of line char.
            line = EncodingExtensions.GetString(Encoding.ASCII, buffer.Slice(0, position.Value));
            buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
            return true;
        }

        private async Task<TResult> RunEnsuringConnectionIsOpen<TResult>(string actionName, Func<TResult> action, CancellationToken token)
        {
            await _reconnectionLock.AcquireReaderLock(token);
            try
            {
                return action(); // the built actions like ReadLine, etc are documented to throw InvalidOperationException if the connection is not opened.                
            }
            finally
            {
                _reconnectionLock.ReleaseReaderLock();
            }
        }

        private async Task<TResult> RunEnsuringConnectionIsOpen<TResult>(string actionName, Func<Task<TResult>> action, CancellationToken token)
        {
            await _reconnectionLock.AcquireReaderLock(token);
            try
            {
                return await action(); // the built actions like ReadLine, etc are documented to throw InvalidOperationException if the connection is not opened.                
            }
            finally
            {
                _reconnectionLock.ReleaseReaderLock();
            }
        }

        private Task RunEnsuringConnectionIsOpen(string actionName, Action action, CancellationToken token) => 
            RunEnsuringConnectionIsOpen(actionName, () => { action(); return true; }, token);

        public class BoardInfo
        {
            public BoardInfo(string serialNumber, string productType, TryReadLineDelegate lineParser)
            {
                SerialNumber = serialNumber;
                ProductType = productType;
                LineParser = lineParser;
            }

            public string SerialNumber { get; }
            public string ProductType { get; }
            public TryReadLineDelegate LineParser { get; }
        }
    }
}
