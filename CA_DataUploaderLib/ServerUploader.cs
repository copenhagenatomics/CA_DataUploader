#nullable enable
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Serialization;
using UdpToHttpGateway.Client;

namespace CA_DataUploaderLib
{
    public sealed class ServerUploader : ISubsystemWithVectorData
    {
        private const int maxDuplicateMessagesPerMinuteRate = 3;
        private readonly string _loopname;
        private PlotConnection? _plot;
        private PlotConnection Plot => _plot ?? throw new InvalidOperationException("Usage of plot connection before vector description initialization");
        private readonly Signing? _signInfo;
        private Signing SignInfo => _signInfo ?? throw new NotSupportedException("Signing is only supported in the uploader");
        private DateTime _lastTimestamp;
        private (bool[] uploadMap, VectorDescription uploadDesc)? _desc;
        private (bool[] uploadMap, VectorDescription uploadDesc) Desc => _desc ?? throw new InvalidOperationException("Usage of desc before vector description initialization");
        private static readonly BoundedChannelOptions BoundedOptions = new(10000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true };
        private readonly Channel<UploadState> _executedActionChannel = Channel.CreateBounded<UploadState>(BoundedOptions);
        private readonly Channel<DataVector> _vectorsChannel = Channel.CreateBounded<DataVector>(BoundedOptions);
        private readonly Channel<EventFiredArgs> _eventsChannel = Channel.CreateBounded<EventFiredArgs>(BoundedOptions);
        private readonly IIOconf _ioconf;
        private readonly CommandHandler _cmd;
        private readonly IDictionary<byte, string>? _nodeIdToName;
        private readonly TaskCompletionSource<PlotConnection> _connectionEstablishedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Stopwatch _timeSinceLastInvalidValueEvent = new();
        private int _invalidValueEventsSkipped = 0;
        private IReadOnlyList<Func<EventFiredArgs, bool>> _eventFilters = [];

        public string Title => nameof(ServerUploader);
        public bool IsEnabled { get; }

        public ServerUploader(IIOconf ioconf, CommandHandler cmd)
        {
            _ioconf = ioconf;
            _cmd = cmd;
            _loopname = ioconf.GetLoopName();
            var nodes = ioconf.GetEntries<IOconfNode>().ToList();
            IsEnabled = IOconfNode.IsCurrentSystemAnUploader(nodes);
            if (!IsEnabled)
                return;
            _nodeIdToName = nodes.ToDictionary(n => n.NodeIndex, n => n.Name);
            _signInfo = new Signing(_loopname);
            cmd.FullVectorDescriptionCreated += DescriptionCreated;
            var reader = cmd.GetReceivedVectorsReader();
            _ = Task.Run(async () =>
            {
                await foreach (var vector in reader.ReadAllAsync(cmd.StopToken))
                    SendVector(vector);
            });
            cmd.AddSubsystem(this);

            void DescriptionCreated(object? sender, VectorDescription desc)
            {
                var fieldUploadMap = desc._items.Select(v => v.Upload).ToArray();
                var uploadDescription = new VectorDescription(desc._items.Where(v => v.Upload).ToList(), desc.Hardware, desc.Software) { IOconf = _ioconf.GetRawFile() };
                _desc = (fieldUploadMap, uploadDescription);
            }
        }

        public SubsystemDescriptionItems GetVectorDescriptionItems() => new([]);
        public IEnumerable<SensorSample> GetInputValues() => [];
        public void SendEvent(object? sender, EventFiredArgs e) => _eventsChannel.Writer.TryWrite(e);
        public void SendInvalidValueEvent(EventFiredArgs e)
        {
            if (_timeSinceLastInvalidValueEvent.IsRunning && _timeSinceLastInvalidValueEvent.ElapsedMilliseconds < 5 * 60000)
            {
                if (_invalidValueEventsSkipped++ == 0)
                    CALog.LogData(LogID.B, $"{nameof(ServerUploader)} - skipping further invalid value events (max 1 message every 5 minutes)");
                return;
            }

            _timeSinceLastInvalidValueEvent.Restart();
            _eventsChannel.Writer.TryWrite(e);
            _invalidValueEventsSkipped = 0;
        }
        /// <summary>
        /// Add filters that prevent events from getting uploaded.
        /// A filter gets an event as input and returns true if the event should be ignored (i.e. not uploaded).
        /// </summary>
        public void AddEventFilters(params Func<EventFiredArgs, bool>[] filters)
        {
            _eventFilters = [.._eventFilters, ..filters];
        }

        private void SendVector(DataVector vector)
        {
            var (uploadMap, uploadDesc) = Desc;
            if (vector.Count != uploadMap.Length)
                throw new ArgumentException($"Wrong vector length (input, expected): {vector.Count} <> {uploadMap.Length}");
            if (vector.Timestamp <= _lastTimestamp)
            {
                CALog.LogData(LogID.B, $"Non changing or out of order timestamp received - vector ignored: last recorded {_lastTimestamp:O} vs received {vector.Timestamp:O}");
                if (vector.Events.Count > 0)
                    CALog.LogData(LogID.B, $"Events in vector ignored: {vector.Events.Select(e => e.Data)}");
                return;
            }
            _lastTimestamp = vector.Timestamp;

            _executedActionChannel.Writer.TryWrite(UploadState.LocalCluster);

            //first queue all events
            //note slightly changing the event time below is a workaround to ensure they come in order in the event log
            int @eventIndex = 0;
            foreach (var e in vector.Events)
                SendEvent(this, new EventFiredArgs($"{((e.EventType == (byte) EventType.Log || e.EventType == (byte)EventType.LogError) ? NodeIdToName(e.NodeId) : "")}{e.Data}", e.EventType, e.TimeSpan.AddTicks(eventIndex++)));

            //now queue vectors
            _vectorsChannel.Writer.TryWrite(FilterOnlyUploadFieldsAndCheckInvalidValues(vector, out var invalidValueMessage));

            //Possibly send event due to invalid values detected in the vector
            if (!string.IsNullOrWhiteSpace(invalidValueMessage))
                SendInvalidValueEvent(new EventFiredArgs("Invalid values detected: " + invalidValueMessage[2..], EventType.LogError, DateTime.UtcNow));

            string NodeIdToName(byte nodeId) => nodeId != byte.MaxValue ? $"[{_nodeIdToName?[nodeId]}] " : "";

            DataVector FilterOnlyUploadFieldsAndCheckInvalidValues(DataVector fullVector, out string invalidValueMessage)
            {
                var fullVectorData = fullVector.Data;
                var uploadData = new double[uploadDesc.Length];
                int j = 0;
                StringBuilder? invalidValues = null;
                for (int i = 0; i < fullVector.Data.Length; i++)
                {
                    if (!uploadMap[i]) continue;

                    if (double.IsNaN(fullVectorData[i]))
                        (invalidValues ??= new()).Append($", {uploadDesc._items[j].Descriptor}=NaN");
                    if (double.IsInfinity(fullVectorData[i]))
                        (invalidValues ??= new()).Append($", {uploadDesc._items[j].Descriptor}=Inf");

                    uploadData[j++] = fullVectorData[i];
                }
                invalidValueMessage = invalidValues?.ToString() ?? string.Empty;
                return new(uploadData, fullVector.Timestamp, []);//emptyEvents as we already queued them
            }
        }

        /// <remarks>
        /// This method waits until a connection has been established and if there is a connection failure it throws <see cref="InvalidOperationException"/>.
        /// </remarks>
        public async Task<(int plotId, bool supports2WayCommunication)> GetPlotId(CancellationToken token)
        {
            if (!IsEnabled) throw new NotSupportedException("GetPlotId is only supported in uploader nodes");
            var plot = await GetPlot(token);
            return (plot.PlotId, plot.Supports2WayCommunication);
        }

        /// <remarks>
        /// This method waits until a connection has been established and if there is a connection failure it throws <see cref="InvalidOperationException"/>.
        /// </remarks>
        public async Task<string> GetPlotName(CancellationToken token)
        {
            if (!IsEnabled) throw new NotSupportedException("GetPlotName is only supported in uploader nodes");
            var plot = await GetPlot(token);
            if (!plot.Supports2WayCommunication) throw new NotSupportedException("GetPlotName is only supported when using 2 way communication");
            return plot.PlotName;
        }

        private async Task<PlotConnection> GetPlot(CancellationToken token)
        {
            if (!IsEnabled) throw new NotSupportedException("The uploader is not enabled in this node");
            var task = _connectionEstablishedSource.Task;
            var cancellationSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationRegistration = token.Register(() => cancellationSource.TrySetCanceled(token));
            await Task.WhenAny(task, cancellationSource.Task);
            token.ThrowIfCancellationRequested();
            return await task;
        }

        public async Task<HttpResponseMessage> DirectRequest(HttpRequestMessage request, CancellationToken token)
        {
            var plot = await GetPlot(token);
            return await plot.SendAsync(request, token);
        }
        public async Task<HttpResponseMessage> DirectPost(string requestUri, byte[] bytes, CancellationToken token)
        {
            var plot = await GetPlot(token);
            return await plot.PostAsync(requestUri, Sign(bytes), token);
        }
        public async Task<HttpResponseMessage> DirectPut(string requestUri, byte[] bytes, CancellationToken token)
        {
            var plot = await GetPlot(token);
            return await plot.PutAsync(requestUri, Sign(bytes), token);
        }

        public byte[] Sign(byte[] bytes) => [.. SignInfo.GetSignature(bytes), .. bytes];

        public static async Task<bool> ConnectionTest(string loopServer, CancellationToken token)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(loopServer),
                Timeout = TimeSpan.FromSeconds(5)
            };
            var response = await client.GetAsync("/api/v1/LoopControl/PingTest", token);
            return response.IsSuccessStatusCode;
        }

        public async Task Run(CancellationToken token)
        {
            try
            {
                var (_, uploadDesc) = Desc;
                _plot = await PlotConnection.Establish(_ioconf.GetConnectionInfo(), SignInfo.GetPublicKey(), GetSignedVectorDescription(uploadDesc), _connectionEstablishedSource, token);

                var stateTracker = WithExceptionLogging(TrackUploadState(token), "upload state tracker");
                var vectorsSender = WithExceptionLogging(VectorsSender(token), "upload vector sender");
                var eventsSender = WithExceptionLogging(EventsSender(token), "upload events sender");

                await Task.WhenAll(stateTracker, vectorsSender, eventsSender);
            }
            catch (UnauthorizedAccessException ex)
            {
                OnError($"Uploading stopped: {ex.Message}{Environment.NewLine}Check the configuration, correct any issues and restart to resume uploads.", ex);
            }
            catch (HttpRequestException ex)
            {
                OnError($"ServerUploader: an error occurred (HttpStatusCode={ex.StatusCode}, HttpRequestError={ex.HttpRequestError}).", ex);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == token) { }
            catch (Exception ex)
            {//this will usually be a hard error to establish a plot connection 
                OnError("ServerUploader.LoopForever() exception: " + ex.Message, ex);
            }
            finally
            {
                _executedActionChannel.Writer.Complete();
            }

            async Task VectorsSender(CancellationToken token)
            {
                var throttle = new PeriodicTimer(TimeSpan.FromMilliseconds(_ioconf.GetVectorUploadDelay()));
                var stateWriter = _executedActionChannel.Writer;
                var badVectors = new List<DateTime>();

                try
                {
                    while (await throttle.WaitForNextTickAsync(token))
                    {
                        stateWriter.TryWrite(UploadState.VectorUploader);
                        if (!await PostQueuedVectorAsync(stateWriter))
                            badVectors.Add(DateTime.UtcNow);
                    }
                }
                catch (OperationCanceledException) { }

                CALog.LogInfoAndConsoleLn(LogID.A, "Uploader is stopping, trying to send remaining queued vectors");
                if (!await PostQueuedVectorAsync(stateWriter))
                    badVectors.Add(DateTime.UtcNow);
                PrintBadPackagesMessage(badVectors, "Vector", true);
            }

            async Task EventsSender(CancellationToken token)
            {
                //note this uses this uses the vector upload delay for the frequency for no special reason, it was just an easy value to use for this
                var throttle = new PeriodicTimer(TimeSpan.FromMilliseconds(_ioconf.GetVectorUploadDelay()));
                var stateWriter = _executedActionChannel.Writer;
                var badEvents = new List<DateTime>();
                Dictionary<string, int> duplicateEventsDetection = [];
                Queue<(DateTime expirationTime, string @event)> duplicateEventsExpirationTimes = new();

                try
                {
                    while (await throttle.WaitForNextTickAsync(token))
                    {
                        stateWriter.TryWrite(UploadState.EventUploader);
                        await PostQueuedEventsAsync(stateWriter, badEvents, duplicateEventsDetection, duplicateEventsExpirationTimes);
                    }
                }
                catch (OperationCanceledException) { }

                await PostEventAsync(new EventFiredArgs("Uploader is stopping", EventType.Log, DateTime.UtcNow));
                CALog.LogInfoAndConsoleLn(LogID.A, "Uploader is stopping, trying to send remaining queued events");

                await Task.Delay(200, CancellationToken.None); //we give an extra 200ms to let any remaining shutdown events come in
                await PostQueuedEventsAsync(stateWriter, badEvents, duplicateEventsDetection, duplicateEventsExpirationTimes);
                PrintBadPackagesMessage(badEvents, "Events", true);
            }

            async Task TrackUploadState(CancellationToken token)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _ = Task.Run(() => SignalCheckStateEvery200ms(_executedActionChannel.Writer, cts), token);
                try
                {
                    //note failures to upload events are not reported to the event log, as that event would often fail as well
                    await Task.Delay(5000, token); //give some time for initialization + other nodes starting in multipi.
                    var vectorsCycleCheckArgs = (Stopwatch.StartNew(), 15000, 15000, UploadState.VectorUploader, addToEventLog: true);
                    var eventsCycleCheckArgs = (Stopwatch.StartNew(), 15000, 15000, UploadState.EventUploader, addToEventLog: false);
                    var receivedVectorCheckArgs = (Stopwatch.StartNew(), 1000, 1000, UploadState.LocalCluster, addToEventLog: true);
                    await foreach (var state in _executedActionChannel.Reader.ReadAllAsync(token))
                    {
                        switch (state)
                        {//we only process together the group them by state
                            case UploadState.VectorUploader or UploadState.VectorUpload or UploadState.UploadedVector:
                                DetectSlowActionOnNewAction(ref vectorsCycleCheckArgs, state);
                                break;
                            case UploadState.LocalCluster:
                                DetectSlowActionOnNewAction(ref receivedVectorCheckArgs, state);
                                break;
                            case UploadState.EventUploader or UploadState.EventUpload or UploadState.UploadedEvent:
                                DetectSlowActionOnNewAction(ref eventsCycleCheckArgs, state);
                                break;
                            default:
                                DetectSlowAction(ref vectorsCycleCheckArgs);
                                DetectSlowAction(ref receivedVectorCheckArgs);
                                DetectSlowAction(ref eventsCycleCheckArgs);
                                break;
                        }
                    }
                }
                finally
                {
                    cts.Cancel();
                }

                void DetectSlowActionOnNewAction(ref (Stopwatch timeSinceLastAction, int firstTargetToReportSlowAction, int nextTargetToReportSlowAction, UploadState lastState, bool addToEventLog) stateCheckArgs, UploadState newState)
                {
                    if (stateCheckArgs.timeSinceLastAction.ElapsedMilliseconds > stateCheckArgs.nextTargetToReportSlowAction)
                        OnError($"Detected slow {stateCheckArgs.lastState} - target: {stateCheckArgs.nextTargetToReportSlowAction} - new state {newState}", stateCheckArgs.addToEventLog, null);

                    stateCheckArgs.nextTargetToReportSlowAction = stateCheckArgs.firstTargetToReportSlowAction;
                    stateCheckArgs.timeSinceLastAction.Restart();
                    stateCheckArgs.lastState = newState;
                }

                void DetectSlowAction(ref (Stopwatch timeSinceLastAction, int firstTargetToReportSlowAction, int nextTargetToReportSlowAction, UploadState lastState, bool addToEventLog) stateCheckArgs)
                {
                    if (stateCheckArgs.timeSinceLastAction.ElapsedMilliseconds > stateCheckArgs.nextTargetToReportSlowAction)
                    {
                        OnError($"Detected slow {stateCheckArgs.lastState} - target: {stateCheckArgs.nextTargetToReportSlowAction}", stateCheckArgs.addToEventLog, null);
                        stateCheckArgs.nextTargetToReportSlowAction *= 2;
                    }
                }

                static async Task SignalCheckStateEvery200ms(ChannelWriter<UploadState> writer, CancellationTokenSource cts)
                {
                    var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
                    while (await timer.WaitForNextTickAsync(cts.Token))
                        writer.TryWrite(UploadState.CheckState);
                }
            }

            async Task WithExceptionLogging(Task task, string subsystem)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                { //normally the upload subsystems should handle errors so they can continue operating, so seeing this error means we missed handling some case
                    CALog.LogErrorAndConsoleLn(LogID.A, $"{subsystem} - unexpected error detected", ex);
                }
            }
        }

        static IReadOnlyList<T> DequeueAllEntries<T>(ChannelReader<T> reader)
        {
            if (!reader.TryRead(out var value))
                return [];

            var list = new List<T>();
            do
                list.Add(value);
            while (reader.TryRead(out value));

            return list;
        }

        /// <returns>a signature of the original data followed by the data compressed with gzip.</returns>
        private byte[] SignAndCompress(byte[] buffer)
        {
            using var memory = new MemoryStream();
            using (var gzip = new GZipStream(memory, CompressionMode.Compress))
            {
                gzip.Write(buffer, 0, buffer.Length);
            }

            return [.. SignInfo.GetSignature(buffer), .. memory.ToArray()];
        }

        private byte[] GetSignedVectorDescription(VectorDescription vectorDescription)
        {
            var xmlserializer = new XmlSerializer(typeof(VectorDescription));
            using var msXml = new MemoryStream();
            xmlserializer.Serialize(msXml, vectorDescription);
            var buffer = msXml.ToArray();
            return SignAndCompress(buffer);
        }

        private byte[] GetSignedVectors(IReadOnlyList<DataVector> vectors)
        {
            byte[] listLen = BitConverter.GetBytes((ushort)vectors.Count);
            var theData = vectors.SelectMany(a => a.Buffer).ToArray();
            return SignAndCompress([.. listLen, .. theData]);
        }

        private byte[] GetSignedEvent(EventFiredArgs @event)
        { //this can be made more efficient to avoid extra allocations and/or use a memory pool, but these are for low frequency events so postponing looking at that.
            return Sign(@event.ToByteArray());
        }

        private void OnError(string message, bool addToEventLog, Exception? ex = null)
        {
            if (!addToEventLog)
            {
                OnError(message, ex);
                return;
            }

            Console.WriteLine(message);
            message = ex != null ? $"{message}{Environment.NewLine}{ex}" : message;
            SendEvent(this, new(message, EventType.LogError, DateTime.UtcNow));
            return;
        }

        private static void OnError(string message, Exception? ex = null)
        {
            if (ex != null)
                CALog.LogError(LogID.A, message, ex); //note we don't use LogErrorAndConsoleLn variation, as CALog.LoggerForUserOutput may be set to generate events that are only visible on the event log.
            else
                CALog.LogData(LogID.A, message);
            Console.WriteLine(message);
        }

        private static void OnError(string message, string detailsForLog, Exception? ex = null)
        {
            var logMsg = message + Environment.NewLine + detailsForLog;
            if (ex != null)
                CALog.LogError(LogID.A, logMsg, ex); //note we don't use LogErrorAndConsoleLn variation, as CALog.LoggerForUserOutput may be set to generate events that are only visible on the event log.
            else
                CALog.LogData(LogID.A, logMsg);
            Console.WriteLine(message);
        }

        private ValueTask<bool> PostQueuedVectorAsync(ChannelWriter<UploadState> stateWriter)
        {
            var list = DequeueAllEntries(_vectorsChannel.Reader);
            if (list.Count == 0) return ValueTask.FromResult(true); //no vectors, return success

            stateWriter.TryWrite(UploadState.VectorUpload);
            var buffer = GetSignedVectors(list);
            var timestamp = list[0].Timestamp;
            return new ValueTask<bool>(Post());

            async Task<bool> Post()
            {
                try
                {
                    using var response = await Plot.PostVectorAsync(buffer, timestamp);
                    var success = CheckAndLogFailures(response, "Failed posting vector");
                    stateWriter.TryWrite(UploadState.UploadedVector);
                    return success;
                }
                catch (HttpRequestException ex)
                {
                    OnError($"Failed posting vector (HttpStatusCode={ex.StatusCode}, HttpRequestError={ex.HttpRequestError}).", ex);
                    return false;
                }
                catch (Exception ex)
                {
                    OnError("Failed posting vector", ex);
                    return false;
                }
            }
        }

        private IEnumerable<EventFiredArgs> FilterEvents(IEnumerable<EventFiredArgs> events)
        {
            var filters = _eventFilters;
            foreach (var e in events)
            {
                if (filters.Any(eval => eval(e)))
                    continue;
                yield return e;
            }
        }

        private async ValueTask PostQueuedEventsAsync(
            ChannelWriter<UploadState> stateWriter, List<DateTime> badEvents, Dictionary<string, int> duplicateEventsDetection, 
            Queue<(DateTime expirationTime, string @event)> duplicateEventsExpirationTimes)
        {
            RemoveExpiredDuplicateEvents();

            var events = DequeueAllEntries(_eventsChannel.Reader);
            if (events.Count == 0) return;

            foreach (var e in FilterEvents(events))
            {
                if (TrackDuplicate(e.Data)) continue;

                stateWriter.TryWrite(UploadState.EventUpload);
                if (!await PostEventAsync(e))
                    badEvents.Add(DateTime.UtcNow);
                stateWriter.TryWrite(UploadState.UploadedEvent);
            }

            bool TrackDuplicate(string e)
            {
                var duplicate = duplicateEventsDetection.TryGetValue(e, out var oldRepeatCount);
                duplicateEventsDetection[e] = duplicate ? oldRepeatCount + 1 : 1;
                if (duplicate && oldRepeatCount >= maxDuplicateMessagesPerMinuteRate)
                    return true;
                if (!duplicate)
                    duplicateEventsExpirationTimes.Enqueue((DateTime.UtcNow.AddMinutes(1), e));
                return false;
            }
            void RemoveExpiredDuplicateEvents()
            {
                var now = DateTime.UtcNow;
                while (duplicateEventsExpirationTimes.TryPeek(out var e) && now > e.expirationTime)
                {
                    e = duplicateEventsExpirationTimes.Dequeue();
                    if (duplicateEventsDetection.TryGetValue(e.@event, out var repeatCount) && repeatCount > maxDuplicateMessagesPerMinuteRate)
                        _eventsChannel.Writer.TryWrite(new EventFiredArgs($"Skipped {repeatCount - maxDuplicateMessagesPerMinuteRate} duplicate messages detected within the last minute: {e.@event}", EventType.LogError, DateTime.UtcNow));
                    duplicateEventsDetection.Remove(e.@event);
                }
            }
        }

        private async Task<bool> PostEventAsync(EventFiredArgs args)
        {
            try
            {
                if (args.EventType == (byte)EventType.SystemChangeNotification)
                    return await PostSystemChangeNotificationAsync(args);//this special event turns into reported boards serial info + still an event with a shorter description
                else
                    return await Post(args);
            }
            catch (HttpRequestException ex)
            {
                OnError($"Failed posting event (HttpStatusCode={ex.StatusCode}, HttpRequestError={ex.HttpRequestError}): {args.EventType} - {args.Data} - {args.TimeSpan}", ex);
                return false;
            }
            catch (Exception ex)
            {
                OnError($"Failed posting event: {args.EventType} - {args.Data} - {args.TimeSpan}", ex);
                return false;
            }

            Task<bool> Post(EventFiredArgs args) => CheckAndLogEvent(Plot.PostEventAsync(GetSignedEvent(args)), "event");
            Task<bool> PostBoards(byte[] message) => CheckAndLogEvent(Plot.PostBoardsAsync(SignAndCompress(message)), "board");
            async Task<bool> PostSystemChangeNotificationAsync(EventFiredArgs args)
            {
                var data = SystemChangeNotificationData.ParseJson(args.Data) ?? 
                    throw new FormatException($"Failed to parse SystemChangeNotificationData: {args.Data}");
                var results = await Task.WhenAll(
                    PostBoards(data.ToBoardsSerialInfoJsonUtf8Bytes(args.TimeSpan)),
                    Post(new EventFiredArgs(ToShortEventData(data), args.EventType, args.TimeSpan)));
                return Array.TrueForAll(results, r => r);
            }
            async Task<bool> CheckAndLogEvent(Task<HttpResponseMessage> responseTask, string type)
            {
                using var response = await responseTask;
                return CheckAndLogFailures(response, (type, args), a => $"Failed posting ({a.type}): {a.args.EventType} - {a.args.Data} - {a.args.TimeSpan}");
            }
            static string ToShortEventData(SystemChangeNotificationData data)
            {
                var sb = new StringBuilder(data.Boards.Count * 100);//allocate more than enough space to avoid slow unnecessary resizes
                sb.AppendLine($"[{data.NodeName}] Detected devices:");
                foreach (var board in data.Boards)
                {
                    sb.AppendFormat("{0} {1} {2} {3}", board.MappedBoardName, board.ProductType, board.SerialNumber, board.Port);
                    if (board.Calibration != null && board.Calibration != board.UpdatedCalibration)
                        sb.AppendFormat(" cal:{0}->{1}", board.Calibration, board.UpdatedCalibration);
                    else if (board.Calibration != null)
                        sb.AppendFormat(" cal:{0}", board.Calibration);
                    sb.AppendLine();
                }
                return sb.ToString();
            }
        }

        private static void PrintBadPackagesMessage(List<DateTime> badPackages, string type, bool force)
        {
            if (badPackages.Count == 0)
                return;

            if (force || badPackages.First().AddHours(1) < DateTime.UtcNow)
            {
                var msg = GetBadPackagesErrorMessage(type, badPackages);
                CALog.LogInfoAndConsoleLn(LogID.A, msg);
                badPackages.Clear();
            }

            static string GetBadPackagesErrorMessage(string type, List< DateTime> times)
            {
                var sb = new StringBuilder();
                sb.Append(type);
                sb.AppendLine(" upload errors within the last hour:");
                foreach (var minutes in times.GroupBy(x => x.ToString("MMM dd HH:mm")))
                    sb.AppendLine($"{minutes.Key} = {minutes.Count()}");
                return sb.ToString();
            }
        }
        private static bool CheckAndLogFailures(HttpResponseMessage response, string message) => CheckAndLogFailures(response, message, m => m);
        private static bool CheckAndLogFailures<T>(HttpResponseMessage response, T messageArgs, Func<T, string> getMessage)
        {
            var success = response.IsSuccessStatusCode;
            if (!success)
                OnError($"{getMessage(messageArgs)}",
                    $"Response: {Environment.NewLine}{response}{Environment.NewLine}" +
                    $"Response Content: {Environment.NewLine}{ReadStringFromStream(response.Content?.ReadAsStream())}{Environment.NewLine}" +
                    $"Request Message: {Environment.NewLine}{response.RequestMessage}{Environment.NewLine}");

            return success;
        }

        public static string ReadStringFromStream(Stream? input)
        {
            if (input is null)
                return string.Empty;

            using var stream = new StreamReader(input);
            stream.BaseStream.Seek(0, SeekOrigin.Begin);
            return stream.ReadToEnd();
        }

        private class PlotConnection
        {
            readonly HttpClient _client;
            private PlotConnection(HttpClient client, int plotID, string? plotName, bool supports2WayCommunication)
            {
                if (supports2WayCommunication)
                    ArgumentNullException.ThrowIfNull(PlotName);
                _client = client;
                PlotId = plotID;
                PlotName = plotName;
                Supports2WayCommunication = supports2WayCommunication;
            }

            public string? PlotName { get; set; }
            public int PlotId { get; }
            [MemberNotNullWhen(true, nameof(PlotName))]
            public bool Supports2WayCommunication { get; }
            public Task<HttpResponseMessage> PostVectorAsync(byte[] buffer, DateTime timestamp) => PutAsync($"/api/v2/Timeserie/UploadVectorRetroAsync?plotNameId={PlotId}&ticks={timestamp.Ticks}", buffer);
            public Task<HttpResponseMessage> PostEventAsync(byte[] signedMessage) => PutAsync($"/api/v2/Event?plotNameId={PlotId}", signedMessage);
            public Task<HttpResponseMessage> PostBoardsAsync(byte[] signedMessage) => PutAsync($"/api/v1/McuSerialnumber?plotNameId={PlotId}", signedMessage);
            public Task<HttpResponseMessage> PostAsync(string requestUri, byte[] message, CancellationToken token = default) 
                => _client.PostAsJsonAsync(requestUri, message, token);
            public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token = default) => _client.SendAsync(request, token);
            internal Task<HttpResponseMessage> PutAsync(string requestUri, byte[] message, CancellationToken token = default)
                => _client.PutAsJsonAsync(requestUri, message, token);
            public static async Task<PlotConnection> Establish(ConnectionInfo connectionInfo, byte[] publicKey, byte[] signedVectorDescription, TaskCompletionSource<PlotConnection> connectionEstablishedSource, CancellationToken cancellationToken)
            {
                try
                {
                    PlotConnection connection = 
                        await TryGetConnectionViaUdpToHttpGateway(connectionInfo, cancellationToken) ?? 
                        await GetDirectConnection(connectionInfo, publicKey, signedVectorDescription, cancellationToken);
                    connectionEstablishedSource.TrySetResult(connection);
                    return connection;
                }
                catch (OperationCanceledException ex)
                {
                    connectionEstablishedSource.TrySetCanceled(ex.CancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    connectionEstablishedSource.TrySetException(ex);
                    throw;
                }
            }

            private static async Task<PlotConnection> GetDirectConnection(ConnectionInfo connectionInfo, byte[] publicKey, byte[] signedVectorDescription, CancellationToken cancellationToken)
            {
                //PooledConnectionLifetime allows the system to notice dns changes as recommended at https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines#recommended-use
                var client = NewClient(connectionInfo.Server, new SocketsHttpHandler() { PooledConnectionLifetime = TimeSpan.FromMinutes(15) });
                var token = await GetLoginTokenWithRetries(client, connectionInfo, cancellationToken); // retries here are important as its the first connection so running after a restart can run into the connection not being initialized
                var (plotId, plotName) = await GetPlotIDAsyncWithRetries(connectionInfo.LoopName, client, token, publicKey, signedVectorDescription, cancellationToken);
                return new PlotConnection(client, plotId, plotName, true);
            }

            record struct DiodeConf(int PlotId, string IPEndpoint);
            private static async Task<PlotConnection?> TryGetConnectionViaUdpToHttpGateway(ConnectionInfo connectionInfo, CancellationToken token)
            {
                const string file = "diode.json";
                if (!File.Exists(file))
                    return null;
                using FileStream openStream = File.OpenRead(file);
                var conf = await JsonSerializer.DeserializeAsync<DiodeConf>(openStream, cancellationToken: token);

                //for now we just assume the conf matches, but we might want to lock the related config,
                //so that new changes to come into effect require a new plot id must be given
                //(which could need us to post the proposed config to the server first).
                var plotId = conf.PlotId;
                CALog.LogData(LogID.A, $"Using diode.conf plotId {plotId}, IpEndPoint {conf.IPEndpoint}");
                var gatewayIp = IPEndPoint.Parse(conf.IPEndpoint);
                return new(NewClient(connectionInfo.Server, new SendViaUdpGatewayMessageHandler(gatewayIp)), plotId, default, false);
            }

            private static async Task<(int plotId, string plotName)> GetPlotIDAsyncWithRetries(string loopName, HttpClient client, string loginToken, byte[] publicKey, byte[] signedVectorDescription, CancellationToken cancellationToken)
            {
                return await RunWithRetries(GetPlotIDAsync, (loopName, client, loginToken, publicKey, signedVectorDescription), "obtain/create plot for the system", cancellationToken);

                static async Task<(int plotId, string plotName)> GetPlotIDAsync((string loopName, HttpClient client, string loginToken, byte[] publicKey, byte[] signedVectorDescription) args, CancellationToken cancellationToken)
                {
                    var (loopName, client, loginToken, publicKey, signedVectorDescription) = args;
                    HttpResponseMessage? response = null;
                    try
                    {
                        string query = $"/api/v1/Plotdata/GetPlotnameId?loopname={loopName}&ticks={DateTime.UtcNow.Ticks}&logintoken={loginToken}";
                        var signedValue = publicKey.Concat(signedVectorDescription).ToArray(); // Note that it will only work if converted to array and not IEnumerable
                        response = await client.PutAsJsonAsync(query, signedValue, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        var result = await response.Content.ReadFromJsonAsync<string>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Unexpected null result when getting plotid");
                        return (result.StringBefore(" ").ToInt(), result.StringAfter(" "));
                    }
                    catch (Exception ex)
                    {
                        var contentTask = response?.Content?.ReadAsStringAsync(cancellationToken);
                        var error = contentTask != null ? await contentTask : null;
                        if (!string.IsNullOrEmpty(error))
                            OnError($"Failure getting plot id: {error} {ex.Message}");

                        if (ex is HttpRequestException rex && (rex.StatusCode == HttpStatusCode.Unauthorized || rex.StatusCode == HttpStatusCode.BadRequest))
                            throw new UnauthorizedAccessException($"System private key or verified account token was rejected ({rex.StatusCode}).", ex);//UnauthorizedAccessException skips retries

                        throw;
                    }
                }
            }

            private static Task<string> GetLoginTokenWithRetries(HttpClient client, ConnectionInfo info, CancellationToken cancellationToken)
            {
                return RunWithRetries(GetLoginToken, (client, info), "verify system identity with the plot server", cancellationToken);

                static async Task<string> GetLoginToken((HttpClient client, ConnectionInfo accountInfo) args, CancellationToken cancellationToken)
                {
                    var (client, accountInfo) = args;
                    string? token = await Post(client, $"/api/v1/user/login?user={accountInfo.Email}&password={accountInfo.Password}", cancellationToken);
                    if (string.IsNullOrEmpty(token))
                        token = await Post(client, $"/api/v1/user/CreateAccount?user={accountInfo.Email}&password={accountInfo.Password}&fullName={accountInfo.Fullname}", cancellationToken); // attempt to create account assuming it did not exist

                    return !string.IsNullOrEmpty(token) ? 
                        token : 
                        throw new UnauthorizedAccessException("Unable to login with the configured account.");//UnauthorizedAccessException skips retries
                }

                static async Task<string?> Post(HttpClient client, string requestUri, CancellationToken cancellationToken)
                {
                    var response = await client.PostAsync(requestUri, null, cancellationToken);
                    if (response.StatusCode == HttpStatusCode.OK && response.Content != null)
                        return (await response.Content.ReadAsStringAsync(cancellationToken)).Replace("\"", "");
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                        return null;

                    throw new HttpRequestException($"Failed server response: {response.StatusCode} - {response.ReasonPhrase}", null, response.StatusCode);
                }
            }

            private static async Task<T> RunWithRetries<TArgs, T>(Func<TArgs, CancellationToken, Task<T>> func, TArgs args, string actionMsg, CancellationToken cancellationToken)
            {
                int connectionAttempts = 0;
                while (true)
                {
                    try
                    {
                        connectionAttempts++;
                        var res = await func(args, cancellationToken);
                        CALog.LogInfoAndConsoleLn(LogID.A, $"Succeeded to {actionMsg} after {connectionAttempts} attempt(s).");
                        return res;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        OnError($"Failed to {actionMsg}.");
                        throw;
                    }
                    catch (OperationCanceledException ex) when (ex.CancellationToken != cancellationToken && !cancellationToken.IsCancellationRequested)
                    {//in some cases this could happen when hitting timeouts - https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.postasync?view=net-6.0
                        OnError($"Failed to {actionMsg}, attempting to reconnect in 5 seconds.", ex);
                        await Task.Delay(5000, cancellationToken);
                    }
                    catch (HttpRequestException ex)
                    {
                        OnError($"Failed to {actionMsg} (HttpStatusCode={ex.StatusCode}, HttpRequestError={ex.HttpRequestError}), attempting to reconnect in 5 seconds.", ex);
                        await Task.Delay(5000, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        //Based on documented behavior it should only be HttpRequestException, but doing retries for other exceptions due to info out there on Socket and IO exceptions.
                        //Above is expected to be improved in .net 8, but it was unclear while writing this which exception set is guaranteed to really include all types of failures doing the request and fetching the corresponding response.
                        //Failures should usually be a network or potentially a temporary error returned by the server, we continue retrying - https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.postasync?view=net-6.0
                        //however, it is also some error codes the server returns that can be shared by temporary and permanent rejection errors
                        OnError($"Failed to {actionMsg}, attempting to reconnect in 5 seconds.", ex);
                        await Task.Delay(5000, cancellationToken);
                    }
                }
            }

            private static HttpClient NewClient(string server, HttpMessageHandler handler)
            {
                var client = new HttpClient(handler) { BaseAddress = new Uri(server) };
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return client;
            }
        }

        private class Signing
        {
            private readonly RSACryptoServiceProvider _rsaWriter = new(1024);

            public Signing(string loopName)
            {
                var keyFilename = "Key" + loopName + ".bin";
                if (File.Exists(keyFilename))
                    _rsaWriter.ImportCspBlob(File.ReadAllBytes(keyFilename));
                else
                    File.WriteAllBytes(keyFilename, _rsaWriter.ExportCspBlob(true));
            }

            public byte[] GetPublicKey() => _rsaWriter.ExportCspBlob(false);

            public byte[] GetSignature(byte[] data) => _rsaWriter.SignData(data, SHA1.Create());
        }

        private enum UploadState
        {
            VectorUploader,
            UploadedVector,
            LocalCluster,
            UploadedEvent,
            EventUploader,
            VectorUpload,
            CheckState,
            EventUpload
        }
    }
}
