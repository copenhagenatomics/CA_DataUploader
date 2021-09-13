using CA_DataUploaderLib.Extensions;
using System;
using System.Threading;
using System.IO.Ports;
using System.Diagnostics;
using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using System.Buffers;
using System.Text;
using System.Globalization;

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
        public bool InitialConnectionSucceeded {get; private set;} = false;

        // "O 0213.1 T +21.0 P 1019 % 020.92 e 0000"
        private static readonly Regex _luminoxRegex = new Regex(
            "O (([0-9]*[.])?[0-9]+) T ([+-]?([0-9]*[.])?[0-9]+) P (([0-9]*[.])?[0-9]+) % (([0-9]*[.])?[0-9]+) e ([0-9]*)");
        private static int _oxygenSensorsDetected;
        private static readonly Regex _scaleRegex = new Regex("[+-](([0-9]*[.])?[0-9]+) kg"); // "+0000.00 kg"
        private static int _detectedScaleBoards;
        private static int _detectedUnknownBoards;
        // the "writer" for this lock are operations that close/reopens the connection, while the readers are any other operation including SafeWriteLine.
        // This prevents operations being ran when the connections are being closed/reopened.
        private AsyncReaderWriterLock _reconnectionLock = new AsyncReaderWriterLock();

        public BoardSettings ConfigSettings { get; set; } = BoardSettings.Default;
        public string PortName => port.PortName;
        private SerialPort port;
        private PipeReader pipeReader;
        public delegate bool TryReadLineDelegate(ref ReadOnlySequence<byte> buffer, out string line);
        private TryReadLineDelegate TryReadLine;

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

            return board;
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
            $"{BoxNameHeader}{BoxName}{seperator}Port name: {PortName}{seperator}Baud rate: {port.BaudRate}{seperator}{serialNumberHeader}{serialNumber}{seperator}{productTypeHeader}{productType}{seperator}{pcbVersionHeader}{pcbVersion}{seperator}{softwareVersionHeader}{softwareVersion}{seperator}";
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
            var mcu = await Create(name, 115200, skipAutoDetection);
            return mcu.InitialConnectionSucceeded ? mcu : await Create(name, 9600, skipAutoDetection);
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
            var stop = DateTime.Now.AddSeconds(5);
            bool sentSerialCommandTwice = false;
            bool ableToRead = false;
            while (IsEmpty() && DateTime.Now < stop)
            {
                try
                {
                    // we use the regular reads as only 1 thread uses the board during instance initialization
                    var res = await pipeReader.ReadAsync();
                    var buffer = res.Buffer;

                    while (TryReadLine(ref buffer, out var input))
                    {
                        if (Debugger.IsAttached && input.Length > 0)
                            CALog.LogColor(LogID.A, ConsoleColor.Green, input);

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
                        else if (input.Contains("MISREAD") && !sentSerialCommandTwice && serialNumber == null)
                        {
                            port.WriteLine("Serial");
                            CALog.LogInfoAndConsoleLn(LogID.A, $"Received misread without any serial on port {PortName} - re-sending serial command");
                            sentSerialCommandTwice = true;
                        }
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
                catch (Exception ex)
                {
                    CALog.LogColor(LogID.A, ConsoleColor.Red, $"Unable to read from {PortName} ({port.BaudRate}): " + ex.Message);
                }
            }

            return ableToRead;
        }

        /// <summary>detects if we are talking with a supported third party device</summary>
        private Task<BoardInfo> DetectThirdPartyProtocol() => 
            DetectThirdPartyProtocol(port.BaudRate, PortName, pipeReader);

        /// <summary>detects if we are talking with a supported third party device</summary>
        public static async Task<BoardInfo> DetectThirdPartyProtocol(
            int baudRate, string portName, PipeReader pipeReader)
        {
            if (baudRate != 9600) 
                return default; //all the third party devices currently supported are running at 9600<

            var watch = Stopwatch.StartNew();
            var thirdPartyDetectors = new List<Func<ReadOnlySequence<byte>, string, (bool finishedDetection, BoardInfo)>>()
            {
                TryDetectZE03Protocol,
                TryDetectLuminoxProtocol,
                TryDetectAscaleProtocol
            };
            foreach (var detector in thirdPartyDetectors)
            {
                while (watch.ElapsedMilliseconds < 3000) //only allow up to 3 seconds to detect a third party device
                {
                    var result = await pipeReader.ReadAsync();
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
            }

            return default;
        }

        private static (bool finishedDetection, BoardInfo detectedInfo) TryDetectLuminoxProtocol(ReadOnlySequence<byte> buffer, string _)
        {
            var pos = buffer.PositionOf((byte)'\n');
            if (pos == null) return (false, default);
            if (buffer.FirstSpan[0] != (byte)'O')
                return (true, default); //just a quick check to abort the string conversion if the first character does not match.
            buffer = buffer.Slice(0, pos.Value);
            return (true, DetectLuminoxSensor(EncodingExtensions.GetString(Encoding.ASCII, buffer)));
        }

        private static (bool finishedDetection, BoardInfo detectedInfo) TryDetectAscaleProtocol(ReadOnlySequence<byte> buffer, string _)
        {
            var pos = buffer.PositionOf((byte)'\n');
            if (pos == null) return (false, default);
            buffer = buffer.Slice(0, pos.Value);
            if (buffer.PositionOf((byte)'k') == null)
                return (true, default); //just a quick check to abort the string conversion if the k from kg is not present.
            return (true, DetectAscale(EncodingExtensions.GetString(Encoding.ASCII, buffer)));
        }

        /// <summary>detects the ze03 by checking the first 2 bytes are 0xFF 0x86 and verifying the checksum matches</summary>
        /// <returns>true if detection is finished, otherwise it means we need to receive more data to complete detection</returns>
        private static (bool finishedDetection, BoardInfo detectedInfo) TryDetectZE03Protocol(ReadOnlySequence<byte> buffer, string portName)
        {
            if (buffer.Length < 9)
                return (false, default); // waiting for enough data to detect a full valid frame.
            if (TryReadZE03FrameAtCurrentPosition(buffer, out _, out var checksumFailure))
            {
                var info =  new BoardInfo("Oxygen" + Interlocked.Increment(ref _oxygenSensorsDetected), "ze03", TryReadLineZE03);
                return (true, info);
            }

            if (checksumFailure)
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Data received in port {portName} seems o2 (ze03) sensor but checksum did not match");
                LogSkippedZE03Data(buffer);
            }

            return (true, default);
        }

        private async Task<string> ReadLine()
        {
            using var cts = new CancellationTokenSource(ConfigSettings.MaxMillisecondsWithoutNewValues);
            var token = cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var res = await pipeReader.ReadAsync(token);
                    if (res.IsCanceled)
                        throw new TimeoutException("timed out (soft)");
                    var buffer = res.Buffer;
                    var readLine = TryReadLine(ref buffer, out var line);
                    pipeReader.AdvanceTo(buffer.Start, buffer.End); // Tell the PipeReader how much of the buffer has been consumed.
                    if (readLine)
                        return line; //we return a single line for now to keep a similar API before introducing the pipe reader, but would be fine to explore changing the API shape in the future

                    if (res.IsCompleted)
                        throw new ObjectDisposedException(PortName, "closed connection detected");
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

        private static BoardInfo DetectLuminoxSensor(string line)
        {
            if (!_luminoxRegex.IsMatch(line)) return default;
            return new BoardInfo(
                "Oxygen" + Interlocked.Increment(ref _oxygenSensorsDetected),
                "Luminox O2",
                TryReadLineLuminox);
        }

        private static BoardInfo DetectAscale(string line)
        {
            if (!_scaleRegex.IsMatch(line)) return default;
            return new BoardInfo(
                "Scale" + Interlocked.Increment(ref _detectedScaleBoards),
                "Scale",
                TryReadLineScale);
        }

        private bool TryReadAsciiLine(ref ReadOnlySequence<byte> buffer, out string line) => TryReadAsciiLine(ref buffer, out line, ConfigSettings.ValuesEndOfLineChar);
        private static bool TryReadAsciiLine(ref ReadOnlySequence<byte> buffer, out string line, char endOfLineChar) 
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

        private static bool TryReadLineLuminox(ref ReadOnlySequence<byte> buffer, out string line) => TryReadAsciiLine(ref buffer, out line, '\n'); //just return the raw lines to keep current config based implementation for now
        private static bool TryReadLineScale(ref ReadOnlySequence<byte> buffer, out string line) => TryReadAsciiLine(ref buffer, out line, '\n'); //just return the raw lines to keep current config based implementation for now
        private static bool TryReadLineZE03(ref ReadOnlySequence<byte> buffer, out string line)
        {
            var origBuffer = buffer;
            var isValid = TryReadNextValidZE03Frame(ref buffer, out line);
            var skippedData = origBuffer.Slice(0, buffer.Start);
            if (isValid)
                skippedData = skippedData.Slice(0, skippedData.Length - 9);
            if (skippedData.Length > 0)
                LogSkippedZE03Data(skippedData);
            return isValid;
        }

        private static void LogSkippedZE03Data(ReadOnlySequence<byte> skippedData)
        {//could make sense to improve the performance of this in the future, like the byte[] allocation.
            var sequence = new SequenceReader<byte>(skippedData);
            var bytesReceived = new byte[skippedData.Length]; 
            var bytesCopied = sequence.TryCopyTo(bytesReceived);
            var bytesStr = bytesCopied ? Convert.ToHexString(bytesReceived) : "";
            CALog.LogData(LogID.B, $"invalid data detected for o2 (ze03) sensor: {bytesStr}");
        }

        private static bool TryReadNextValidZE03Frame(ref ReadOnlySequence<byte> buffer, out string line)
        {
            line = default;
            while (true)
            {
                var pos = buffer.PositionOf((byte)0xFF); //start of frame
                if (pos == null)
                {//no start frame found
                    buffer = buffer.Length > 50 
                        ? buffer.Slice(buffer.End) //skip to the end to avoid accumulating unprocessed data
                        : buffer; //lets wait to have a bit more data before reporting the invalid data (so we get better log info about it)
                    return false;
                }

                buffer = buffer.Slice(pos.Value); //skip to the detected start of frame
                if (buffer.Length < 9)
                    return false; //waiting for more data to complete a full frame

                if (TryReadZE03FrameAtCurrentPosition(buffer, out line, out _))
                {
                    buffer = buffer.Slice(9); //skip the processed frame
                    return true;
                }
                
                pos = buffer.PositionOf((byte)0xFF) ?? buffer.End; //we skip to the end if we don't find a new start of frame
            }
        }

        /// <returns>true if the frame was valid, otherwise false</returns>
        /// <remarks>caller should validate there are at least 9 bytes, as this method also returns false if there are less than 9 bytes</remarks>
        private static bool TryReadZE03FrameAtCurrentPosition(ReadOnlySequence<byte> buffer, out string line, out bool checksumFailure)
        {
            line = default;
            checksumFailure = false;
            var sequence = new SequenceReader<byte>(buffer);
            if (!sequence.TryRead(out byte first))
                return false;
            if (first != 0xFF)
                return false;
            if (!sequence.TryPeek(out var second))
                return false;
            if (second != 0x86)
                return false;
            byte checksum = 0;
            byte concentrationHighByte = 0, concentrationLowByte = 0;
            for (int i = 0; i < 7; i++)
            {
                if (!sequence.TryRead(out var val))
                    return false;
                if (i == 1) //this is the third byte in the frame
                    concentrationHighByte = val;
                if (i == 2) //this is the fourth byte in the frame
                    concentrationLowByte = val;
                checksum += val;
            }

            checksum ^= 0xff; //uses xor to negate all bits
            checksum += 1;
            if (!sequence.TryRead(out var receivedChecksum))
                return false;
            if (checksum == receivedChecksum)
            {
                var concentration = Math.Round(((concentrationHighByte * 256) + concentrationLowByte) * 0.1, 2);
                line = concentration.ToString(CultureInfo.InvariantCulture); 
                return true;
            }
            
            checksumFailure = true;
            return false;
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
