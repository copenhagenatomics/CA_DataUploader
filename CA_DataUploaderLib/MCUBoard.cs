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
        private readonly IConnection port;
        private readonly Dependencies dependencies;
        private PipeReader? pipeReader;
        public delegate bool TryReadLineDelegate(ref ReadOnlySequence<byte> buffer, [NotNullWhen(true)]out string? line);
        private TryReadLineDelegate TryReadLine;
        private static readonly string[] _newLineCharacters = ["\r\n", "\r", "\n"];
        public delegate (bool finishedDetection, BoardInfo? info) CustomProtocolDetectionDelegate(ReadOnlySequence<byte> buffer, string portName);
        private static readonly List<CustomProtocolDetectionDelegate> customProtocolDetectors = [];

        public bool Closed { get; private set; }

        private MCUBoard(IConnection port, string portName, Dependencies dependencies) : base(portName, null)
        {
            this.port = port;
            this.dependencies = dependencies;
            TryReadLine = TryReadAsciiLine; //this is the default which can be changed in ReadSerial based on the ThirdPartyProtocolDetection
        }

        private async static Task<MCUBoard?> Create(Dependencies dependencies, IIOconf? ioconf, string name, int baudrate, bool skipBoardAutoDetection, bool enableDtrRts = true)
        {
            MCUBoard? board = null;
            try
            {
                var port = dependencies.ConnectionManager.NewConnection(name, baudrate, enableDtrRts);
                board = new MCUBoard(port, name, dependencies);
                board.pipeReader = port.GetPipeReader();
                board.ProductType = "NA";
                board.InitialConnectionSucceeded = skipBoardAutoDetection || await board.ReadSerialNumber(board.pipeReader);

                if (ioconf is not null)
                { // note that unlike the discovery at MCUBoard.OpenDeviceConnection that only considers the usb port, in here we can find the boards by the serial number too.
                    var didMap = false;
                    foreach (var ioconfMap in ioconf.GetMap())
                    {
                        if (board.TrySetMap(ioconfMap))
                        {
                            board.ConfigSettings = ioconfMap.BoardSettings;
                            port.ReadTimeout = ioconfMap.BoardSettings.MaxMillisecondsWithoutNewValues;
                            await board.UpdateCalibration(board.ConfigSettings);
                            didMap = true;
                        }
                    }
                    if (!didMap)
                        await board.SafeClose(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                dependencies.LogException(LogID.A, $"Unexpected error connecting to {name}({baudrate})", ex);
            }

            if (board != null && !board.InitialConnectionSucceeded)
            {
                board.pipeReader?.Complete();
                board.port.Close();
                Thread.Sleep(100);
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
                dependencies.LogInfo(LogID.A, $"Skipped detected board without calibration support - {ToShortDescription()}");
                return default;
            }

            var calibrationMessage = $"Replaced board calibration '{Calibration}' with '{newCalibration}";
            dependencies.LogInfo(LogID.A, $"{calibrationMessage}' - {ToShortDescription()}");
            await SafeWriteLine(newCalibration, CancellationToken.None);
            UpdatedCalibration = newCalibration;
            return calibrationMessage;
        }

        public async Task<string> SafeReadLine(CancellationToken token) => await RunWaitingForAnyOngoingReconnect(ReadLine, token);
        public Task SafeWriteLine(string msg, CancellationToken token) => RunWaitingForAnyOngoingReconnect(() => { if (port.IsOpen) port.WriteLine(msg); else throw new ObjectDisposedException("Closed connection detected (port is closed)"); } , token);

        public async Task SafeClose(CancellationToken token)
        {
            using var _ = await _reconnectionLock.AcquireWriterLock(token);
            if (pipeReader != null)
                await pipeReader.CompleteAsync();
            pipeReader = null;
            if (port.IsOpen)
                port.Close();
            Closed = true;
        }

        public string ToDebugString(string separator) =>
            $"{BoxNameHeader}{BoxName}{separator}Port name: {PortName}{separator}Baud rate: {port.BaudRate}{separator}{serialNumberHeader} {SerialNumber}{separator}{productTypeHeader} {ProductType}{separator}{pcbVersionHeader}{PcbVersion}{separator}{softwareVersionHeader} {SoftwareVersion}{separator}{CalibrationHeader} {Calibration}{separator}";
        public override string ToString() => $"{productTypeHeader} {ProductType,-20} {serialNumberHeader} {SerialNumber,-12} Port name: {PortName,-18} {pcbVersionHeader} {PcbVersion}";

        /// <summary>
        /// Reopens the connection.
        /// </summary>
        /// <returns><c>false</c> if the reconnect attempt failed.</returns>
        /// <remarks>
        /// The reconnection is only considered succesfull after the board returns the first non empty line within <see cref="BoardSettings.MaxMillisecondsWithoutNewValues"/>.
        /// Any initial empty lines returned by the board are skipped.
        /// 
        /// Log entries about te attempt to reopen the connection are added to <see cref="LogID.B"/>, but not to the console / event log.
        /// </remarks>
        public async Task<(bool, string)> SafeReopen(CancellationToken token)
        {
            string reconnectedLine;
            try
            {
                using var _ = await _reconnectionLock.AcquireWriterLock(token);
                if (pipeReader != null)
                    await pipeReader.CompleteAsync();
                if (port.IsOpen)
                {
                    dependencies.LogData(LogID.B, $"(Reopen) Closing port {PortName} {ProductType} {SerialNumber}");
                    port.Close();
                    await Task.Delay(TimeSpan.FromMilliseconds(500), dependencies.TimeProvider, token);
                }

                dependencies.LogData(LogID.B, $"(Reopen) opening port {PortName} {ProductType} {SerialNumber}");
                pipeReader = port.GetPipeReader();
                reconnectedLine = await ReadOptionalNonEmptyLine(
                    dependencies, pipeReader, PortName, ConfigSettings.MaxMillisecondsWithoutNewValues, TryReadLine, l => l.StartsWith("reconnected", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                dependencies.LogException(LogID.B, $"Failure reopening port {PortName} {ProductType} {SerialNumber}.",ex);
                return (false, string.Empty);
            }

            dependencies.LogData(LogID.B, $"Reopened port {PortName} {ProductType} {SerialNumber}.");
            return (true, reconnectedLine);
        }

        public static Task<MCUBoard?> OpenDeviceConnection(IIOconf? ioconf, string name) => OpenDeviceConnection(Dependencies.Default, SerialPortConnectionManager.Default, ioconf, name, true);
        public async static Task<MCUBoard?> OpenDeviceConnection(Dependencies dependencies, IConnectionManager connectionManager, IIOconf? ioconf, string name, bool useDmsgIfTypeUnknown)
        {
            // note this map is only found by usb, for map entries configured by serial we use auto detection with standard baud rates instead.
            var map = ioconf?.GetMap().SingleOrDefault(m => m.IsLocalBoard && m.USBPort == name);
            var initialBaudrate = map != null && map.BaudRate != 0 ? map.BaudRate : 115200;
            var isVport = IOconfMap.IsVirtualPortName(name);
            bool skipAutoDetection = isVport || (map?.BoardSettings ?? BoardSettings.Default).SkipBoardAutoDetection;
            if (skipAutoDetection)
                dependencies.LogInfo(LogID.A, $"Device detection disabled for {name}({map})");
            var mcu = await Create(dependencies, ioconf, name, initialBaudrate, skipAutoDetection, !isVport);
            if (mcu == null || !mcu.InitialConnectionSucceeded)
                mcu = await OpenWithAutoDetection(dependencies, ioconf, name, initialBaudrate);
            if (mcu == null)
                return null; //we normally only get here if the SerialPort(usbport) constructor failed, which might be due to a wrong port
            if (mcu.SerialNumber.IsNullOrEmpty())
                mcu.SerialNumber = "unknown" + Interlocked.Increment(ref _detectedUnknownBoards);
            if (mcu.InitialConnectionSucceeded && map != null && map.BaudRate != 0 && mcu.port.BaudRate != map.BaudRate)
                dependencies.LogError(LogID.A, $"Unexpected baud rate for {map}. Board info {mcu}");
            mcu.ProductType ??= useDmsgIfTypeUnknown ? GetStringFromDmesg(mcu.PortName) : mcu.ProductType;
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
            var result = DULutil.ExecuteShellCommand($"dmesg | grep {portName}").Split(_newLineCharacters, StringSplitOptions.None);
            return result.FirstOrDefault(x => x.EndsWith(portName))?.StringBetween(": ", " to ttyUSB");
        }

        private async static Task<MCUBoard?> OpenWithAutoDetection(Dependencies dependencies, IIOconf? ioconf, string name, int previouslyAttemptedBaudRate)
        {
            var skipAutoDetection = false;
            if (previouslyAttemptedBaudRate == 115200)
                return await Create(dependencies, ioconf, name, 9600, skipAutoDetection);
            if (previouslyAttemptedBaudRate == 9600)
                return await Create(dependencies, ioconf, name, 115200, skipAutoDetection);
            var board = await Create(dependencies, ioconf, name, 115200, skipAutoDetection);
            return board != null && board.InitialConnectionSucceeded ? board : await Create(dependencies, ioconf, name, 9600, skipAutoDetection);
        }

        /// <returns><c>true</c> if we were able to read</returns>
        private async Task<bool> ReadSerialNumber(PipeReader pipeReader)
        {
            var detectedInfo = await DetectThirdPartyProtocol(pipeReader, dependencies);
            if (detectedInfo != default)
            {
                SerialNumber = detectedInfo.SerialNumber;
                ProductType = detectedInfo.ProductType;
                TryReadLine = detectedInfo.LineParser;
                return true;
            }

            port.WriteLine("Serial");
            var header = new Header(dependencies);
            var ableToRead = await header.DetectBoardHeader(pipeReader, TryReadLine, () => port.WriteLine("Serial"), $"{PortName} ({port.BaudRate})");
            header.CopyTo(this);
            return ableToRead;
        }

        /// <summary>detects if we are talking with a supported third party device</summary>
        private Task<BoardInfo?> DetectThirdPartyProtocol(PipeReader pipeReader, Dependencies dependencies) =>
            DetectThirdPartyProtocol(port.BaudRate, PortName, pipeReader, dependencies);

        public static void AddCustomProtocol(CustomProtocolDetectionDelegate detector) => customProtocolDetectors.Add(detector ?? throw new ArgumentNullException(nameof(detector)));

        /// <summary>detects if we are talking with a supported third party device</summary>
        public static async Task<BoardInfo?> DetectThirdPartyProtocol(
            int baudRate, string portName, PipeReader pipeReader, Dependencies? dependencies = null)
        {
            if (baudRate != 9600)
                return default; //all the third party devices currently supported are running at 9600<

            // A cancellation token is made here rather than a simple timer since the ReadAsync function can hang
            // if a device never sends a line for it to read.
            int millisecondsTimeout = 3000;
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(millisecondsTimeout), (dependencies ?? Dependencies.Default).TimeProvider);
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
            ReadLine(dependencies, pipeReader ?? throw new ObjectDisposedException("Closed connection detected (null pipeReader)"), PortName, ConfigSettings.MaxMillisecondsWithoutNewValues, TryReadLine);
        //exposing this one for testing purposes
        public static async Task<string> ReadLine(
            Dependencies dependencies, PipeReader pipeReader, string portName, int millisecondsTimeout, TryReadLineDelegate tryReadLine)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(millisecondsTimeout), dependencies.TimeProvider);
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

        /// <summary>advances the reader past any empty lines and returns + advances the next line if it <paramref name="matches"/> returns <c>true</c>.</summary>
        /// <remarks>returns <c>string.Empty</c> if <paramref name="matches"/> returns <c>false</c>.</remarks>
        static Task<string> ReadOptionalNonEmptyLine(Dependencies dependencies, PipeReader? pipeReader, string portName, int timeoutMilliseconds, TryReadLineDelegate tryReadLine, Func<string, bool> matches)
        {
            return ReadLine(dependencies, pipeReader ?? throw new ObjectDisposedException("Closed connection detected (null pipeReader)"), portName, timeoutMilliseconds, TryOptionalRead);

            bool TryOptionalRead(ref ReadOnlySequence<byte> buffer, out string matchingLine) => TryReadOptionalNonEmptyLine(ref buffer, tryReadLine, matches, out matchingLine);
        }
        static bool TryReadOptionalNonEmptyLine(ref ReadOnlySequence<byte> buffer, TryReadLineDelegate tryReadLine, Func<string, bool> matches, out string matchingLine)
        {
            matchingLine = string.Empty;
            var bufferAfterLine = buffer;
            string? line;
            do
            {
                buffer = bufferAfterLine; //advance the buffer to avoid empty lines from being read again.
                if (!tryReadLine(ref bufferAfterLine, out line))
                {//we did not get a line, more data is needed, advance the buffer to ensure the caller fetches more data
                    buffer = bufferAfterLine;
                    return false;
                }
            }
            while (string.IsNullOrWhiteSpace(line));

            if (!matches(line))
                return true;

            buffer = bufferAfterLine;//advance the caller's buffer to avoid the calibration line from being read again.
            matchingLine = line;
            return true;
        }

        static Task<string> SkipEmptyLines(Dependencies dependencies, PipeReader? pipeReader, string portName, int timeoutMilliseconds, TryReadLineDelegate tryReadLine) => ReadOptionalNonEmptyLine(dependencies, pipeReader, portName, timeoutMilliseconds, tryReadLine, _ => false);
        ///<returns>true if a non empty line was found, or false if more data is needed</returns>

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

            private readonly Dependencies Dependencies;

            public Header() : this(Dependencies.Default) { }
            public Header(Dependencies dependencies)
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
                        {
                            LogMissingHeader("pipe canceled");
                            return ableToRead;
                        }

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
                            buffer = buffer.Slice(0, 0);//don't advance the reader beyond the optional calibration line, so the next read does not unnecesarily fetches more data
                        }

                        // Tell the PipeReader how much of the buffer has been consumed.
                        pipeReader.AdvanceTo(buffer.Start, buffer.End);

                        if (res.IsCompleted)
                        { // typically means the connection was closed.
                            LogMissingHeader("pipe closed");
                            return ableToRead;
                        }
                    }
                    catch (OperationCanceledException ex) when (ex.CancellationToken == token)
                    {
                        LogMissingHeader("timed out");
                        return ableToRead;
                    }
                    catch (Exception ex)
                    {
                        Dependencies.LogException(LogID.A, $"Unable to read from {port}: " + ex.Message, ex);
                    }
                }

                await SkipEmptyLines(Dependencies, pipeReader, port, 2000, tryReadLine);//avoid any extra empty lines just after the full header from getting read by callers
                return true;

                void LogMissingHeader(string reason) => 
                    Dependencies.LogError(LogID.A, linesRead.Length > 0 ? $"Partial board header detected from {port}: {reason}{Environment.NewLine}{linesRead}" : $"Unable to read from {port}: {reason}");
                // pcbVersion is included in this list because at the time of writting is the last value in the readEEPROM header, which avoids the rest of the header being treated as "values".
                bool ReadFullHeader() => readSerialNumber && readProductType && readSoftwareVersion && readPcbVersion;
                ///<returns>true if it finished attempting to read the optional calibration, or false if more data is needed</returns>
                bool TryReadOptionalCalibration(ref ReadOnlySequence<byte> buffer, out string? calibration)
                {
                    calibration = default;
                    var readLine = TryReadOptionalNonEmptyLine(ref buffer, tryReadLine, l => l.Contains(CalibrationHeader, StringComparison.InvariantCultureIgnoreCase), out var line);
                    if (readLine && !string.IsNullOrEmpty(line))
                        calibration = line[(line.IndexOf(CalibrationHeader, StringComparison.InvariantCultureIgnoreCase) + CalibrationHeader.Length)..].Trim();
                    return readLine;
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

        public class Dependencies(
            TimeProvider timeProvider, Action<LogID, string> logInfo, Action<LogID, string> logError, 
            Action<LogID, string, Exception> logException, Action<LogID, string> logData,
            IConnectionManager connectionManager)
        {
            public Dependencies(TimeProvider timeProvider, Action<LogID, string> log, IConnectionManager connectionManager) :
                this(timeProvider, log, log, (id, msg, ex) => log(id, $"{msg}{Environment.NewLine}{ex}"), log, connectionManager)
            { 
            }
            internal static Dependencies Default { get; } = new(
                TimeProvider.System, CALog.LogInfoAndConsoleLn, CALog.LogErrorAndConsoleLn, CALog.LogErrorAndConsoleLn, CALog.LogData,
                SerialPortConnectionManager.Default);

            public TimeProvider TimeProvider { get; } = timeProvider;
            public IConnectionManager ConnectionManager { get; } = connectionManager;

            internal void LogError(LogID logId, string message) => logError(logId, message);
            internal void LogException(LogID logId, string message, Exception exception) => logException(logId, message, exception);
            internal void LogInfo(LogID logId, string message) => logInfo(logId, message);
            internal void LogData(LogID logId, string message) => logData(logId, message);
        }

        public interface IConnectionManager
        {
            public IConnection NewConnection(string name, int baudRate, bool enableDtrRts);
        }

        public interface IConnection
        {
            int ReadTimeout { get; set; }
            bool IsOpen { get; }
            int BaudRate { get; }

            void Close();
            PipeReader GetPipeReader();
            void WriteLine(string msg);
        }

        private class SerialPortConnectionManager() : IConnectionManager
        {
            public static IConnectionManager Default { get; } = new SerialPortConnectionManager();

            public IConnection NewConnection(string name, int baudRate, bool enableDtrRts) => new SerialPortConnection(name, baudRate, enableDtrRts);

            private class SerialPortConnection : IConnection
            {
                private readonly SerialPort port;

                public SerialPortConnection(string name, int baudRate, bool enableDtrRts)
                {
                    port = new(name);
                    port.BaudRate = 1;
                    port.DtrEnable = enableDtrRts;
                    port.RtsEnable = enableDtrRts;
                    port.BaudRate = baudRate;
                    port.ReadTimeout = 2000;
                    port.WriteTimeout = 2000;
                }

                public int ReadTimeout { get => port.ReadTimeout; set => port.ReadTimeout = value; }
                public bool IsOpen => port.IsOpen;
                public int BaudRate => port.BaudRate;

                public void Close() => port.Close();
                public PipeReader GetPipeReader()
                {
                    port.Open();
                    var reader = PipeReader.Create(port.BaseStream);
                    Thread.Sleep(100); // it needs to wait that the board registers that the COM port has been opened before sending commands (work arounds issue when first opening the connection and sending serial).
                    return reader;
                }

                public void WriteLine(string msg) => port.WriteLine(msg);
            }
        }
    }
}
