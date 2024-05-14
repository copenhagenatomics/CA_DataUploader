#nullable enable
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.Helpers;
using CA_DataUploaderLib.IOconf;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CA_DataUploaderLib
{
    /// <remarks>
    /// Thread safety: reading and writing from 2 different threads is supported as long as only one thread reads and the other one writes.
    /// In addition to that, the Safe* methods must be used so that no operations run in parallel while trying to close/open the connection. 
    /// </remarks>
    public class MCUBoard : Board
    {
        public const string BoxNameHeader = "IOconf.Map BoxName: ";

        public const string serialNumberHeader = "Serial Number:";

        public const string boardFamilyHeader = "Board Family:";
        public const string productTypeHeader = "Product Type:";

        public const string subProductTypeHeader = "Sub Product Type:";

        public const string softwareVersionHeader = "Software Version:";
        public const string boardSoftwareHeader = "Board Software:";

        public const string softwareCompileDateHeader = "Software Compile Date:";
        public const string compileDateHeader = "Compile Date:";

        public const string pcbVersionHeader = "PCB version:";
        public const string boardVersionHeader = "Board Version:";

        public const string mcuFamilyHeader = "MCU Family:";
        public bool InitialConnectionSucceeded {get; private set;} = false;

        public const string CalibrationHeader = "Calibration:";

        public const string GitShaHeader = "Git SHA:";
        public const string GitShaHeader2 = "Git SHA";

        private static int _detectedUnknownBoards;
        // the "writer" for this lock are operations that close/reopens the connection, while the readers are any other operation including SafeWriteLine.
        // This prevents operations being ran when the connections are being closed/reopened.
        private readonly AsyncReaderWriterLock _reconnectionLock = new();

        public BoardSettings ConfigSettings { get; set; } = BoardSettings.Default;
        private readonly SerialPort port;
        private PipeReader? pipeReader;
        public delegate bool TryReadLineDelegate(ref ReadOnlySequence<byte> buffer, [NotNullWhen(true)]out string? line);
        private TryReadLineDelegate TryReadLine;
        public delegate (bool finishedDetection, BoardInfo? info) CustomProtocolDetectionDelegate(ReadOnlySequence<byte> buffer, string portName);
        private static readonly List<CustomProtocolDetectionDelegate> customProtocolDetectors = [];

        private MCUBoard(SerialPort port) : base(port.PortName, null)
        {
            this.port = port;
            TryReadLine = TryReadAsciiLine; //this is the default which can be changed in ReadSerial based on the ThirdPartyProtocolDetection
        }

        private async static Task<MCUBoard?> Create(IIOconf? ioconf, string name, int baudrate, bool skipBoardAutoDetection, bool enableDtrRts = true)
        {
            MCUBoard? board = null;
            try
            {
                var port = new SerialPort(name);
                board = new MCUBoard(port);
                port.BaudRate = 1;
                port.DtrEnable = enableDtrRts;
                port.RtsEnable = enableDtrRts;
                port.BaudRate = baudrate;
                port.ReadTimeout = 2000;
                port.WriteTimeout = 2000;
                port.Open();
                board.pipeReader = PipeReader.Create(port.BaseStream);
                Thread.Sleep(30); // it needs to await that the board registers that the COM port has been opened before sending commands (work arounds issue when first opening the connection and sending serial).
                board.ProductType = "NA";
                board.InitialConnectionSucceeded = skipBoardAutoDetection || await board.ReadSerialNumber(board.pipeReader);

                if (ioconf is not null)
                { // note that unlike the discovery at MCUBoard.OpenDeviceConnection that only considers the usb port, in here we can find the boards by the serial number too.
                    foreach (var ioconfMap in ioconf.GetMap())
                    {
                        if (board.TrySetMap(ioconfMap))
                        {
                            board.ConfigSettings = ioconfMap.BoardSettings;
                            port.ReadTimeout = ioconfMap.BoardSettings.MaxMillisecondsWithoutNewValues;
                            await board.UpdateCalibration(board.ConfigSettings);
                            await board.SkipEmptyLines(ioconfMap.BoardSettings.MaxMillisecondsWithoutNewValues);//avoid any extra empty lines from getting read by callers on the connected + mapped board
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
            else if (board != null && board.BoxName == null && board.SerialNumber.IsNullOrEmpty())
            {//note we don't log this for devices without serial that were mapped by usb (last check above)
                CALog.LogInfoAndConsoleLn(LogID.B, $"Some data without serial detected for device at port {name} - {baudrate}");
            }
            else if (board != null && !board.SerialNumber.IsNullOrEmpty() && (board.ProductType.IsNullOrEmpty() || board.SoftwareVersion.IsNullOrEmpty() || board.PcbVersion.IsNullOrEmpty()))
            {
                CALog.LogInfoAndConsoleLn(LogID.B, $"Detected board with incomplete header {name} - {baudrate}");
            }

            return board;
        }

        private async Task<string?> UpdateCalibration(BoardSettings configSettings)
        {
            string? newCalibration = configSettings.Calibration;
            if (newCalibration == default || Calibration == newCalibration)
                return default; // ignore if there is no calibration in configuration or if the board already had the expected configuration
            if (Calibration == default && configSettings.SkipCalibrationWhenHeaderIsMissing)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Skipped detected board without calibration support - {ToShortDescription()}");
                return default;
            }

            var calibrationMessage = $"Replaced board calibration '{Calibration}' with '{newCalibration}";
            CALog.LogInfoAndConsoleLn(LogID.A, $"{calibrationMessage}' - {ToShortDescription()}");
            await SafeWriteLine(newCalibration, CancellationToken.None);
            UpdatedCalibration = newCalibration;
            return calibrationMessage;
        }

        public async Task<string> SafeReadLine(CancellationToken token) => (await RunWaitingForAnyOngoingReconnect(ReadLine, token)) ?? string.Empty;
        public Task SafeWriteLine(string msg, CancellationToken token) => RunWaitingForAnyOngoingReconnect(() => port.WriteLine(msg), token);

        public async Task SafeClose(CancellationToken token)
        {
            using var _ = await _reconnectionLock.AcquireWriterLock(token);
            if (pipeReader != null)
                await pipeReader.CompleteAsync();
            if (port.IsOpen)
                port.Close();                
        }

        public string ToDebugString(string seperator) =>
            $"{BoxNameHeader}{BoxName}{seperator}Port name: {PortName}{seperator}Baud rate: {port.BaudRate}{seperator}{serialNumberHeader} {SerialNumber}{seperator}{productTypeHeader} {ProductType}{seperator}{pcbVersionHeader}{PcbVersion}{seperator}{softwareVersionHeader} {SoftwareVersion}{seperator}{CalibrationHeader} {Calibration}{seperator}";
        public override string ToString() => $"{productTypeHeader} {ProductType,-20} {serialNumberHeader} {SerialNumber,-12} Port name: {PortName}";

        /// <summary>
        /// Reopens the connection.
        /// </summary>
        /// <returns><c>false</c> if the reconnect attempt failed.</returns>
        /// <remarks>
        /// The reconnection is only considered succesfull after the board returns the first non empty line within 5 seconds.
        /// Any initial empty lines returned by the board are skipped.
        /// 
        /// Log entries about te attempt to reopen the connection are added to <see cref="LogID.B"/>, but not to the console / event log.
        /// </remarks>
        public async Task<bool> SafeReopen(CancellationToken token)
        {
            try
            {
                using var _ = await _reconnectionLock.AcquireWriterLock(token);
                if (pipeReader != null)
                    await pipeReader.CompleteAsync();
                if (port.IsOpen)
                {
                    CALog.LogData(LogID.B, $"(Reopen) Closing port {PortName} {ProductType} {SerialNumber}");
                    port.Close();
                    await Task.Delay(500, token);
                }

                CALog.LogData(LogID.B, $"(Reopen) opening port {PortName} {ProductType} {SerialNumber}");
                port.Open();
                pipeReader = PipeReader.Create(port.BaseStream);
                await SkipEmptyLines(ConfigSettings.MaxMillisecondsWithoutNewValues);
            }
            catch (Exception ex)
            {
                CALog.LogError(LogID.B,$"Failure reopening port {PortName} {ProductType} {SerialNumber}.",ex);
                return false;
            }

            CALog.LogData(LogID.B, $"Reopened port {PortName} {ProductType} {SerialNumber}.");
            return true;
        }

        public async static Task<MCUBoard?> OpenDeviceConnection(IIOconf? ioconf, string name)
        {
            // note this map is only found by usb, for map entries configured by serial we use auto detection with standard baud rates instead.
            var map = ioconf?.GetMap().SingleOrDefault(m => m.IsLocalBoard && m.USBPort == name);
            var initialBaudrate = map != null && map.BaudRate != 0 ? map.BaudRate : 115200;
            var isVport = IOconfMap.IsVirtualPortName(name);
            bool skipAutoDetection = isVport || (map?.BoardSettings ?? BoardSettings.Default).SkipBoardAutoDetection;
            if (skipAutoDetection)
                CALog.LogInfoAndConsoleLn(LogID.A, $"Device detection disabled for {name}({map})");
            var mcu = await Create(ioconf, name, initialBaudrate, skipAutoDetection, !isVport);
            if (mcu == null || !mcu.InitialConnectionSucceeded)
                mcu = await OpenWithAutoDetection(ioconf, name, initialBaudrate);
            if (mcu == null)
                return null; //we normally only get here if the SerialPort(usbport) constructor failed, which might be due to a wrong port
            if (mcu.SerialNumber.IsNullOrEmpty())
                mcu.SerialNumber = "unknown" + Interlocked.Increment(ref _detectedUnknownBoards);
            if (mcu.InitialConnectionSucceeded && map != null && map.BaudRate != 0 && mcu.port.BaudRate != map.BaudRate)
                CALog.LogErrorAndConsoleLn(LogID.A, $"Unexpected baud rate for {map}. Board info {mcu}");
            mcu.ProductType ??= GetStringFromDmesg(mcu.PortName);
            return mcu;
        }

        public static string[] GetUSBports() => Environment.OSVersion.Platform == PlatformID.Unix
                //notice we check both /dev/USB and vports & we ignore missing when vports is missing (via the error redirect to /dev/null)
                ? DULutil.ExecuteShellCommand("ls -1a vports/* /dev/USB* 2>/dev/null").Split("\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).Select(x => x.Replace("\r", "").Trim()).ToArray()
                : SerialPort.GetPortNames();

        private static string? GetStringFromDmesg(string portName)
        {
            if (portName.StartsWith("COM"))
                return null;

            portName = portName[(portName.LastIndexOf('/') + 1)..];
            var result = DULutil.ExecuteShellCommand($"dmesg | grep {portName}").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            return result.FirstOrDefault(x => x.EndsWith(portName))?.StringBetween(": ", " to ttyUSB");
        }

        private async static Task<MCUBoard?> OpenWithAutoDetection(IIOconf? ioconf, string name, int previouslyAttemptedBaudRate)
        {
            var skipAutoDetection = false;
            if (previouslyAttemptedBaudRate == 115200)
                return await Create(ioconf, name, 9600, skipAutoDetection);
            if (previouslyAttemptedBaudRate == 9600)
                return await Create(ioconf, name, 115200, skipAutoDetection);
            var board = await Create(ioconf, name, 115200, skipAutoDetection);
            return board != null && board.InitialConnectionSucceeded ? board : await Create(ioconf, name, 9600, skipAutoDetection);
        }

        /// <returns><c>true</c> if we were able to read</returns>
        private async Task<bool> ReadSerialNumber(PipeReader pipeReader)
        {
            var detectedInfo = await DetectThirdPartyProtocol(pipeReader);
            if (detectedInfo != default)
            {
                SerialNumber = detectedInfo.SerialNumber;
                ProductType = detectedInfo.ProductType;
                TryReadLine = detectedInfo.LineParser;
                return true;
            }

            port.WriteLine("Serial");
            var header = new Header();
            var ableToRead = await header.DetectBoardHeader(pipeReader, TryReadLine, () => port.WriteLine("Serial"), $"{PortName} ({port.BaudRate})");
            header.CopyTo(this);
            return ableToRead;
        }

        /// <summary>detects if we are talking with a supported third party device</summary>
        private Task<BoardInfo?> DetectThirdPartyProtocol(PipeReader pipeReader) =>
            DetectThirdPartyProtocol(port.BaudRate, PortName, pipeReader);

        public static void AddCustomProtocol(CustomProtocolDetectionDelegate detector) => customProtocolDetectors.Add(detector ?? throw new ArgumentNullException(nameof(detector)));

        /// <summary>detects if we are talking with a supported third party device</summary>
        public static async Task<BoardInfo?> DetectThirdPartyProtocol(
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
            ReadLine(pipeReader ?? throw new ObjectDisposedException("Closed connection detected (null pipeReader)"), PortName, ConfigSettings.MaxMillisecondsWithoutNewValues, TryReadLine);
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
                        throw new TimeoutException("Timed out (soft)");
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
                            return line ?? throw new InvalidOperationException("Unexpected null line returned by TryReadLineDelegate (probably from a custom read implementation)"); //we return a single line for now to keep a similar API before introducing the pipe reader, but would be fine to explore changing the API shape in the future
                        }

                        if (res.IsCompleted)
                            throw new ObjectDisposedException(portName, "Closed connection detected");
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
                    throw new TimeoutException("Timed out (while reading)");
                throw;
            }

            throw new TimeoutException("Timed out (idle)");
        }

        private bool TryReadAsciiLine(ref ReadOnlySequence<byte> buffer, [NotNullWhen(true)] out string? line) => TryReadAsciiLine(ref buffer, out line, ConfigSettings.ValuesEndOfLineChar);
        public static bool TryReadAsciiLine(ref ReadOnlySequence<byte> buffer, [NotNullWhen(true)] out string? line, char endOfLineChar)
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

        Task<string> SkipEmptyLines(int timeoutMilliseconds)
        {
            return ReadLine(pipeReader ?? throw new ObjectDisposedException("Closed connection detected (null pipeReader)"), PortName, timeoutMilliseconds, TrySkipEmptyLines);

            bool TrySkipEmptyLines(ref ReadOnlySequence<byte> buffer, [NotNullWhen(true)] out string? line)
            {//TryPeekNextNonEmptyLine updates the ref buffer to skip any empty line it finds before the next non empty line
                var readLine = TryPeekNextNonEmptyLine(ref buffer, out _, out _, TryReadLine);
                line = string.Empty; //we don't really read/advance the returned line, so just return string.Empty to meet the not null requirement of ReadLine
                return readLine;
            }
        }

        ///<returns>true if a non empty line was found, or false if more data is needed</returns>
        static bool TryPeekNextNonEmptyLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> bufferAfterLine, out string? line, TryReadLineDelegate tryReadLine)
        {
            bufferAfterLine = buffer;
            bool readLine = tryReadLine(ref bufferAfterLine, out line);

            //skip empty lines advancing the caller's buffer so it does not read the empty line again.
            while (readLine && string.IsNullOrWhiteSpace(line))
            {
                buffer = bufferAfterLine;
                readLine = tryReadLine(ref bufferAfterLine, out line);
            }

            if (readLine)
                return true;

            //we did not get a line, more data is needed, advance the buffer to ensure the caller fetches more data
            buffer = bufferAfterLine;
            return false;
        }

        private async Task<TResult> RunWaitingForAnyOngoingReconnect<TResult>(Func<TResult> action, CancellationToken token)
        {
            using var _ = await _reconnectionLock.AcquireReaderLock(token);
            return action(); // the built actions like ReadLine, etc are documented to throw InvalidOperationException if the connection is not opened.
        }

        private async Task<TResult> RunWaitingForAnyOngoingReconnect<TResult>(Func<Task<TResult>> action, CancellationToken token)
        {
            using var _ = await _reconnectionLock.AcquireReaderLock(token);
            return await action(); // the built actions like ReadLine, etc are documented to throw InvalidOperationException if the connection is not opened.
        }

        private Task RunWaitingForAnyOngoingReconnect(Action action, CancellationToken token) =>
            RunWaitingForAnyOngoingReconnect(() => { action(); return true; }, token);

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

        public class Header
        {
            public string? SerialNumber { get; private set; }
            public string? ProductType { get; private set; }
            public string? PcbVersion { get; private set; }
            public string? SoftwareCompileDate { get; private set; }
            public string? SoftwareVersion { get; private set; }
            public string? McuFamily { get; private set; }
            public string? SubProductType { get; private set; }
            public string? GitSha { get; private set; }
            public string? Calibration { get; private set; }
            public string? UpdatedCalibration { get; private set; }

            private readonly HeaderDependencies Dependencies;

            public Header() : this(HeaderDependencies.Default) { }
            public Header(HeaderDependencies dependencies)
            {
                Dependencies = dependencies;
            }

            public async Task<bool> DetectBoardHeader(PipeReader pipeReader, TryReadLineDelegate tryReadLine, Action resendSerial, string port)
            {
                var (sentSerialCommandTwice, ableToRead, finishedReadingHeader) = (false, false, false);
                var (readSerialNumber, readProductType, readSoftwareVersion, readPcbVersion) = (false, false, false, false);
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(5000), Dependencies.TimeProvider); // we use a cancellation token for the timeout as ReadAsync can otherwise hang if a device never sends a line
                var token = cts.Token;
                var linesRead = new StringBuilder(200);

                while (!finishedReadingHeader)
                {
                    try
                    {
                        // we use the regular reads as only 1 thread uses the board during instance initialization
                        var res = await pipeReader.ReadAsync(token);

                        if (res.IsCanceled)
                            return ableToRead;

                        var buffer = res.Buffer;

                        while (!ReadFullHeader() && tryReadLine(ref buffer, out var input))
                        {
                            linesRead.AppendLine(input);
                            ableToRead |= input.Length >= 2;
                            input = input.Trim();
                            if (input.StartsWith(serialNumberHeader))
                                (readSerialNumber, SerialNumber) = (true, input[(input.IndexOf(serialNumberHeader) + serialNumberHeader.Length)..].Trim());
                            else if (input.StartsWith(boardFamilyHeader))
                                (readProductType, ProductType) = (true, input[(input.IndexOf(boardFamilyHeader) + boardFamilyHeader.Length)..].Trim());
                            else if (input.StartsWith(productTypeHeader))
                                (readProductType, ProductType) = (true, input[(input.IndexOf(productTypeHeader) + productTypeHeader.Length)..].Trim());
                            else if (input.StartsWith(boardVersionHeader))
                                (readPcbVersion, PcbVersion) = (true, input[(input.IndexOf(boardVersionHeader) + boardVersionHeader.Length)..].Trim());
                            else if (input.StartsWith(pcbVersionHeader, StringComparison.InvariantCultureIgnoreCase))
                                (readPcbVersion, PcbVersion) = (true, input[(input.IndexOf(pcbVersionHeader, StringComparison.InvariantCultureIgnoreCase) + pcbVersionHeader.Length)..].Trim());
                            else if (input.StartsWith(boardSoftwareHeader))
                                SoftwareCompileDate = input[(input.IndexOf(boardSoftwareHeader) + boardSoftwareHeader.Length)..].Trim();
                            else if (input.StartsWith(softwareCompileDateHeader))
                                SoftwareCompileDate = input[(input.IndexOf(softwareCompileDateHeader) + softwareCompileDateHeader.Length)..].Trim();
                            else if (input.StartsWith(compileDateHeader))
                                SoftwareCompileDate = input[(input.IndexOf(compileDateHeader) + compileDateHeader.Length)..].Trim();
                            else if (input.StartsWith(boardSoftwareHeader))
                                (readSoftwareVersion, SoftwareVersion) = (true, input[(input.IndexOf(boardSoftwareHeader) + boardSoftwareHeader.Length)..].Trim());
                            else if (input.StartsWith(softwareVersionHeader))
                                (readSoftwareVersion, SoftwareVersion) = (true, input[(input.IndexOf(softwareVersionHeader) + softwareVersionHeader.Length)..].Trim());
                            else if (input.StartsWith(mcuFamilyHeader))
                                McuFamily = input[(input.IndexOf(mcuFamilyHeader) + mcuFamilyHeader.Length)..].Trim();
                            else if (input.StartsWith(subProductTypeHeader))
                                SubProductType = input[(input.IndexOf(subProductTypeHeader) + subProductTypeHeader.Length)..].Trim();
                            else if (input.StartsWith(GitShaHeader))
                                GitSha = input[(input.IndexOf(GitShaHeader) + GitShaHeader.Length)..].Trim();
                            else if (input.StartsWith(GitShaHeader2))
                                GitSha = input[(input.IndexOf(GitShaHeader2) + GitShaHeader2.Length)..].Trim();

                            else if (input.Contains("MISREAD") && !sentSerialCommandTwice && SerialNumber == null)
                            {
                                resendSerial();
                                Dependencies.LogInfo(LogID.A, $"Received misread without any serial on port {port} - re-sending serial command");
                                sentSerialCommandTwice = true;
                            }
                        }

                        if (ReadFullHeader() && TryReadOptionalCalibration(ref buffer, out var calibration))
                        {
                            finishedReadingHeader = true;
                            Calibration = UpdatedCalibration = calibration;
                        }

                        // Tell the PipeReader how much of the buffer has been consumed.
                        pipeReader.AdvanceTo(buffer.Start, buffer.End);

                        if (res.IsCompleted)
                        {
                            Dependencies.LogError(LogID.A, $"Unable to read from {port}: pipe reader was closed");
                            break; // typically means the connection was closed.
                        }
                    }
                    catch (TimeoutException ex)
                    {
                        Dependencies.LogException(LogID.A, $"Unable to read from {port}: " + ex.Message, ex);
                        break;
                    }
                    catch (OperationCanceledException ex) when (ex.CancellationToken == token)
                    {
                        Dependencies.LogError(LogID.A, $"Unable to read from {port}: timed out");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Dependencies.LogException(LogID.A, $"Unable to read from {port}: " + ex.Message, ex);
                    }
                }

                if (!finishedReadingHeader && linesRead.Length > 0)
                    Dependencies.LogError(LogID.A, $"Partial board header detected from {port}{Environment.NewLine}{linesRead}");

                return ableToRead;

                // pcbVersion is included in this list because at the time of writting is the last value in the readEEPROM header, which avoids the rest of the header being treated as "values".
                bool ReadFullHeader() => readSerialNumber && readProductType && readSoftwareVersion && readPcbVersion;
                ///<returns>true if it finished attempting to read the optional calibration, or false if more data is needed</returns>
                bool TryReadOptionalCalibration(ref ReadOnlySequence<byte> buffer, out string? calibration)
                {
                    calibration = default;
                    var readLine = TryPeekNextNonEmptyLine(ref buffer, out var bufferAfterLine, out var line, tryReadLine);
                    if (!readLine || line == null) return false;
                    if (!line.Contains(CalibrationHeader, StringComparison.InvariantCultureIgnoreCase))
                        return readLine; //note this returns true if we peeked at a non empty non calibration line.

                    buffer = bufferAfterLine;//advance the caller's buffer to avoid the calibration line from being read again.
                    calibration = line[(line.IndexOf(CalibrationHeader, StringComparison.InvariantCultureIgnoreCase) + CalibrationHeader.Length)..].Trim();
                    return true;
                }
            }

            internal void CopyTo(MCUBoard mcuBoard)
            {
                mcuBoard.SerialNumber = SerialNumber;
                mcuBoard.ProductType = ProductType;
                mcuBoard.SubProductType = SubProductType;
                mcuBoard.McuFamily = McuFamily;
                mcuBoard.SoftwareVersion = SoftwareVersion;
                mcuBoard.SoftwareCompileDate = SoftwareCompileDate;
                mcuBoard.GitSha = GitSha;
                mcuBoard.PcbVersion = PcbVersion;
                mcuBoard.Calibration = Calibration;
                mcuBoard.UpdatedCalibration = UpdatedCalibration;
            }
        }

        public class HeaderDependencies(TimeProvider timeProvider, Action<LogID, string> logInfo, Action<LogID, string> logError, Action<LogID, string, Exception> logException)
        {
            internal static readonly HeaderDependencies Default = new(TimeProvider.System, CALog.LogInfoAndConsoleLn, CALog.LogErrorAndConsoleLn, CALog.LogErrorAndConsoleLn);

            public TimeProvider TimeProvider { get; } = timeProvider;

            internal void LogError(LogID logId, string message) => logError(logId, message);
            internal void LogException(LogID logId, string message, Exception exception) => logException(logId, message, exception);
            internal void LogInfo(LogID logId, string message) => logInfo(logId, message);
        }
    }
}
