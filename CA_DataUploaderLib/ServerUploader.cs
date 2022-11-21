#nullable enable
using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace CA_DataUploaderLib
{
    public sealed class ServerUploader : IDisposable
    {
        private readonly PlotConnection _plot;
        private readonly Signing _signing;
        private readonly Queue<DataVector> _queue = new();
        private readonly Queue<EventFiredArgs> _eventsQueue = new();
        private readonly Dictionary<string, int> _duplicateEventsDetection = new();
        private readonly Queue<(DateTime expirationTime, string @event)> _duplicateEventsExpirationTimes = new();
        private DateTime _lastTimestamp;
        private readonly CancellationTokenSource _stopTokenSource;
        private readonly int _localVectorLength;
        private readonly int _uploadVectorLength;
        private readonly bool[] _fieldUploadMap;
        private readonly CommandHandler? _cmd;
        private readonly Channel<UploadState> _executedActionChannel = Channel.CreateBounded<UploadState>(10000);

        public ServerUploader(VectorDescription vectorDescription) : this(vectorDescription, null)
        {
        }

        public ServerUploader(VectorDescription vectorDescription, CommandHandler? cmd)
        {
            try
            {
                var duplicates = vectorDescription._items.GroupBy(x => x.Descriptor).Where(x => x.Count() > 1).Select(x => x.Key);
                if (duplicates.Any())
                    throw new Exception("Title of datapoint in vector was listed twice: " + string.Join(", ", duplicates));
                var loopName = IOconfFile.GetLoopName();
                _signing = new Signing(loopName);
                _localVectorLength = vectorDescription.Length;
                _fieldUploadMap = vectorDescription._items.Select(v => v.Upload).ToArray();
                var uploadDescription = new VectorDescription(vectorDescription._items.Where(v => v.Upload).ToList(), vectorDescription.Hardware, vectorDescription.Software) { IOconf = IOconfFile.GetRawFile() };
                _uploadVectorLength = uploadDescription.Length;
                _plot = PlotConnection.Establish(loopName, _signing.GetPublicKey(), GetSignedVectorDescription(uploadDescription)).GetAwaiter().GetResult();
                _stopTokenSource = cmd != null ? CancellationTokenSource.CreateLinkedTokenSource(cmd.StopToken) : new();
                _ = Task.Run(() => LoopForever(_stopTokenSource.Token));
                _cmd = cmd;

            }
            catch (Exception ex)
            {
                OnError("failed initializing uploader", ex);
                throw;
            }
        }

        public void SendEvent(object? sender, EventFiredArgs e)
        {
            lock (_eventsQueue)
            {
                if (_eventsQueue.Count >= 10000) return;  // if sending thread can't catch up, then drop packages.
                if (_duplicateEventsDetection.TryGetValue(e.Data, out var repeatCount))
                {
                    _duplicateEventsDetection[e.Data] = repeatCount + 1;
                    return;
                }

                _eventsQueue.Enqueue(e);
                _duplicateEventsDetection[e.Data] = 1;
                _duplicateEventsExpirationTimes.Enqueue((DateTime.UtcNow.AddMinutes(5), e.Data));
            }
        }

        void RemoveExpireduplicateEvents()
        {
            lock (_eventsQueue)
            {
                var now = DateTime.UtcNow;
                while (_duplicateEventsExpirationTimes.TryPeek(out var e) && now > e.expirationTime)
                {
                    e = _duplicateEventsExpirationTimes.Dequeue();
                    if (_duplicateEventsDetection.TryGetValue(e.@event, out var repeatCount) && repeatCount > 1)
                        _eventsQueue.Enqueue(new EventFiredArgs($"Skipped {repeatCount} duplicate messages detected within 5 minute: {e.@event}", EventType.LogError, DateTime.UtcNow));
                    _duplicateEventsDetection.Remove(e.@event);
                }
            }
        }

        public void SendVector(DataVector vector)
        {
            if (vector.Count() != _localVectorLength)
                throw new ArgumentException($"wrong vector length (input, expected): {vector.Count} <> {_localVectorLength}");
            if (vector.timestamp <= _lastTimestamp)
            {
                CALog.LogData(LogID.B, $"non changing or out of order timestamp received - vector ignored: last recorded {_lastTimestamp} vs received {vector.timestamp}");
                return;
            }

            _executedActionChannel.Writer.TryWrite(UploadState.LocalClusterRunning);

            lock (_queue)
                if (_queue.Count < 10000)  // if sending thread can't catch up, then drop packages.
                {
                    _queue.Enqueue(WithOnlyUploadFields(vector));
                    _lastTimestamp = vector.timestamp;
                }

            DataVector WithOnlyUploadFields(DataVector fullVector)
            {
                var fullVectorData = fullVector.vector;
                var uploadData = new double[_uploadVectorLength];
                int j = 0;
                for (int i = 0; i < fullVector.vector.Count; i++)
                {
                    if (_fieldUploadMap[i])
                        uploadData[j++] = fullVectorData[i];
                }

                return new(new(uploadData), fullVector.timestamp);
            }
        }

        public string GetPlotUrl() => "https://www.copenhagenatomics.com/Plots/TemperaturePlot.php?" + _plot.PlotName;

        private async Task LoopForever(CancellationToken token)
        {
            try
            {
                var stateTracker = WithExceptionLogging(TrackUploadState(token), "upload state tracker");
                var vectorsSender = WithExceptionLogging(VectorsSender(token), "upload vector sender");
                var eventsSender = WithExceptionLogging(EventsSender(token), "upload events sender");

                await Task.WhenAll(stateTracker, vectorsSender, eventsSender);
            }
            catch (Exception ex)
            {//we don't normally expect to reach here, as the individual tasks above are expected to capture and logs any exceptions
                CALog.LogErrorAndConsoleLn(LogID.A, "ServerUploader.LoopForever() exception: " + ex.Message, ex);
            }
            finally
            {
                _executedActionChannel.Writer.Complete();
            }

            async Task VectorsSender(CancellationToken token)
            {
                var throttle = new PeriodicTimer(TimeSpan.FromMilliseconds(IOconfFile.GetVectorUploadDelay()));
                var stateWriter = _executedActionChannel.Writer;
                var badVectors = new List<DateTime>();

                try
                {
                    while (await throttle.WaitForNextTickAsync(token))
                    {
                        stateWriter.TryWrite(UploadState.UploadVectorsCycle);
                        if (!await PostQueuedVectorAsync(stateWriter))
                            badVectors.Add(DateTime.UtcNow);
                    }
                }
                catch (OperationCanceledException) { }

                CALog.LogInfoAndConsoleLn(LogID.A, "uploader is stopping, trying to send remaining queued vectors");
                if (!await PostQueuedVectorAsync(stateWriter))
                    badVectors.Add(DateTime.UtcNow);
                PrintBadPackagesMessage(badVectors, "Vector", true);
            }

            async Task EventsSender(CancellationToken token)
            {
                //note this uses this uses the vector upload delay for the frequency for no special reason, it was just an easy value to use for this
                var throttle = new PeriodicTimer(TimeSpan.FromMilliseconds(IOconfFile.GetVectorUploadDelay()));
                var stateWriter = _executedActionChannel.Writer;
                var badEvents = new List<DateTime>();

                try
                {
                    while (await throttle.WaitForNextTickAsync(token))
                    {
                        stateWriter.TryWrite(UploadState.UploadEventsCycle);
                        await PostQueuedEventsAsync(stateWriter, badEvents);
                    }
                }
                catch (OperationCanceledException) { }

                await PostEventAsync(new EventFiredArgs("uploader is stopping", EventType.Log, DateTime.UtcNow));
                CALog.LogInfoAndConsoleLn(LogID.A, "uploader is stopping, trying to send remaining queued events");

                await Task.Delay(200, CancellationToken.None); //we give an extra 200ms to let any remaining shutdown events come in
                await PostQueuedEventsAsync(stateWriter, badEvents);
                PrintBadPackagesMessage(badEvents, "Events", true);
            }

            async Task TrackUploadState(CancellationToken token)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _ = Task.Run(() => SignalCheckStateEvery200ms(_executedActionChannel.Writer, cts), token);
                try
                {
                    await Task.Delay(5000, token); //give some time for initialization + other nodes starting in multipi.
                    var vectorsCycleCheckArgs = (Stopwatch.StartNew(), 2500, UploadState.UploadVectorsCycle);
                    var eventsCycleCheckArgs = (Stopwatch.StartNew(), 2500, UploadState.UploadEventsCycle);
                    var receivedVectorCheckArgs = (Stopwatch.StartNew(), 1000, UploadState.LocalClusterRunning);
                    await foreach (var state in _executedActionChannel.Reader.ReadAllAsync(token))
                    {
                        switch (state)
                        {//we only process together the group them by state
                            case UploadState.UploadVectorsCycle or UploadState.PostingVector or UploadState.PostedVector:
                                DetectSlowActionOnNewAction(ref vectorsCycleCheckArgs, state);
                                break;
                            case UploadState.LocalClusterRunning:
                                DetectSlowActionOnNewAction(ref receivedVectorCheckArgs, state);
                                break;
                            case UploadState.UploadEventsCycle or UploadState.PostingEvent or UploadState.PostedEvent:
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

                static void DetectSlowActionOnNewAction(ref (Stopwatch timeSinceLastAction, int nextTargetToReportSlowAction, UploadState lastState) stateCheckArgs, UploadState newState)
                {
                    if (stateCheckArgs.timeSinceLastAction.ElapsedMilliseconds > stateCheckArgs.nextTargetToReportSlowAction)
                        OnError($"detected slow {stateCheckArgs.lastState} - time passed: {stateCheckArgs.timeSinceLastAction.Elapsed} - new state {newState}", null);

                    stateCheckArgs.nextTargetToReportSlowAction = 2500;
                    stateCheckArgs.timeSinceLastAction.Restart();
                    stateCheckArgs.lastState = newState;
                }

                static void DetectSlowAction(ref (Stopwatch timeSinceLastAction, int nextTargetToReportSlowAction, UploadState lastState) stateCheckArgs)
                {
                    if (stateCheckArgs.timeSinceLastAction.ElapsedMilliseconds > stateCheckArgs.nextTargetToReportSlowAction)
                    {
                        OnError($"detected slow {stateCheckArgs.lastState} - time passed: {stateCheckArgs.timeSinceLastAction.Elapsed}", null);
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

        static List<T>? DequeueAllEntries<T>(Queue<T> queue)
        {
            List<T>? list = null; // delayed initialization to avoid creating lists when there is no data.
            lock (queue)
                while (queue.Any())  // dequeue all. 
                {
                    list ??= new List<T>(); // ensure initialized
                    list.Add(queue.Dequeue());
                }

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

            return _signing.GetSignature(buffer).Concat(memory.ToArray()).ToArray();
        }

        private byte[] GetSignedVectorDescription(VectorDescription vectorDescription)
        {
            var xmlserializer = new XmlSerializer(typeof(VectorDescription));
            using var msXml = new MemoryStream();
            xmlserializer.Serialize(msXml, vectorDescription);
            var buffer = msXml.ToArray();
            return SignAndCompress(buffer);
        }

        private byte[] GetSignedVectors(List<DataVector> vectors)
        {
            byte[] listLen = BitConverter.GetBytes((ushort)vectors.Count);
            var theData = vectors.SelectMany(a => a.buffer).ToArray();
            return SignAndCompress(listLen.Concat(theData).ToArray());
        }

        private byte[] GetSignedEvent(EventFiredArgs @event)
        { //this can be made more efficient to avoid extra allocations and/or use a memory pool, but these are for low frequency events so postponing looking at that.
            var bytes = @event.ToByteArray();
            return _signing.GetSignature(bytes).Concat(bytes).ToArray();
        }

        private static void OnError(string message, Exception? ex)
        {
            if (ex != null)
                CALog.LogError(LogID.A, message, ex); //note we don't use LogErrorAndConsoleLn variation, as CALog.LoggerForUserOutput may be set to generate events that are only visible on the event log.
            else
                CALog.LogData(LogID.A, message);
            Console.WriteLine(message);
        }

        private ValueTask<bool> PostQueuedVectorAsync(ChannelWriter<UploadState> stateWriter)
        {
            var list = DequeueAllEntries(_queue);
            if (list == null) return ValueTask.FromResult(true); //no vectors, return success

            stateWriter.TryWrite(UploadState.PostingVector);
            var buffer = GetSignedVectors(list);
            var timestamp = list.First().timestamp;
            return new ValueTask<bool>(Post());

            async Task<bool> Post()
            {
                try
                {
                    await _plot.PostVectorAsync(buffer, timestamp);
                    stateWriter.TryWrite(UploadState.PostedVector);
                    return true;
                }
                catch (Exception ex)
                {
                    OnError("failed posting vector", ex);
                    return false;
                }
            }
        }

        private async ValueTask PostQueuedEventsAsync(ChannelWriter<UploadState> stateWriter, List<DateTime> badEvents)
        {
            RemoveExpireduplicateEvents();
            var events = DequeueAllEntries(_eventsQueue);
            if (events == null) return;

            foreach (var @event in events)
            {
                stateWriter.TryWrite(UploadState.PostingEvent);
                if (!await PostEventAsync(@event))
                    badEvents.Add(DateTime.UtcNow);
                stateWriter.TryWrite(UploadState.PostedEvent);
            }
        }

        private async Task<bool> PostEventAsync(EventFiredArgs args)
        {
            try
            {
                if (args.EventType == (byte)EventType.SystemChangeNotification)
                    await PostSystemChangeNotificationAsync(args);//this special event turns into reported boards serial info + still an event with a shorter description
                else
                    await Post(args);
                return true;
            }
            catch (Exception ex)
            {
                OnError($"failed posting event: {args.EventType} - {args.Data} - {args.TimeSpan}", ex);
                return false;
            }

            Task Post(EventFiredArgs args) => _plot.PostEventAsync(GetSignedEvent(args));
            Task PostBoardsSerialInfo(SystemChangeNotificationData data, DateTime timeSpan) =>
                _plot.PostBoardsSerialInfo(SignAndCompress(data.ToBoardsSerialInfoJsonUtf8Bytes(timeSpan)));
            Task PostSystemChangeNotificationAsync(EventFiredArgs args)
            {
                var data = SystemChangeNotificationData.ParseJson(args.Data);
                if (data == null)
                    throw new FormatException($"failed to parse SystemChangeNotificationData: {args.Data}");
                return Task.WhenAll(
                    PostBoardsSerialInfo(data, args.TimeSpan),
                    Post(new EventFiredArgs(ToShortEventData(data), args.EventType, args.TimeSpan)));
            }
            static string ToShortEventData(SystemChangeNotificationData data)
            {
                var sb = new StringBuilder(data.Boards.Count * 100);//allocate more than enough space to avoid slow unnecesary resizes
                sb.Append("Detected devices for ");
                sb.AppendLine(data.NodeName);
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
            if (!badPackages.Any())
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

        public void Dispose()
        { // class is sealed so don't need full blown IDisposable pattern.
            _stopTokenSource.Cancel();
        }

        private class PlotConnection
        {
            readonly HttpClient _client;
            readonly int _plotID;
            private PlotConnection(HttpClient client, int plotID, string plotname)
            {
                _client = client;
                _plotID = plotID;
                PlotName = plotname;
            }

            public string PlotName { get; private set; }
            public async Task PostVectorAsync(byte[] buffer, DateTime timestamp)
            {
                //ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                string query = $"/api/v2/Timeserie/UploadVectorRetroAsync?plotNameId={_plotID}&ticks={timestamp.Ticks}";
                var response = await _client.PutAsJsonAsync(query, buffer);
                response.EnsureSuccessStatusCode();
            }

            public async Task PostEventAsync(byte[] signedMessage)
            {
                //ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                string query = $"/api/v2/Event?plotNameId={_plotID}";
                var response = await _client.PutAsJsonAsync(query, signedMessage);
                response.EnsureSuccessStatusCode();
            }

            public async Task PostBoardsSerialInfo(byte[] signedMessage)
            {
                //ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                string query = $"/api/v1/McuSerialnumber?plotNameId={_plotID}";
                var response = await _client.PutAsJsonAsync(query, signedMessage);
                response.EnsureSuccessStatusCode();
            }

            public static async Task<PlotConnection> Establish(string loopName, byte[] publicKey, byte[] signedVectorDescription)
            {
                var connectionInfo = IOconfFile.GetConnectionInfo();
                var client = NewClient(connectionInfo.Server);
                var token = await GetLoginTokenWithRetries(client, connectionInfo); // retries here are important as its the first connection so running after a restart can run into the connection not being initialized
                var (plotId, plotName) = await GetPlotIDAsync(loopName, client, token, publicKey, signedVectorDescription);
                return new PlotConnection(client, plotId, plotName);
            }

            private static async Task<(int plotId, string plotName)> GetPlotIDAsync(string loopName, HttpClient client, string loginToken, byte[] publicKey, byte[] signedVectorDescription)
            {
                //ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                HttpResponseMessage? response = null;
                try
                {
                    string query = $"/api/v1/Plotdata/GetPlotnameId?loopname={loopName}&ticks={DateTime.UtcNow.Ticks}&logintoken={loginToken}";
                    var signedValue = publicKey.Concat(signedVectorDescription).ToArray(); // Note that it will only work if converted to array and not IEnummerable
                    response = await client.PutAsJsonAsync(query, signedValue);
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadFromJsonAsync<string>() ?? throw new InvalidOperationException("unexpected null result when getting plotid");
                    return (result.StringBefore(" ").ToInt(), result.StringAfter(" "));
                }
                catch (Exception ex)
                {
                    if (ex.InnerException?.Message == "The remote name could not be resolved: 'www.theng.dk'" || ex.InnerException?.InnerException?.Message == "The remote name could not be resolved: 'www.theng.dk'")
                        throw new HttpRequestException("Check your internet connection", ex);

                    var contentTask = response?.Content?.ReadAsStringAsync();
                    var error = contentTask != null ? await contentTask : null;
                    if (!string.IsNullOrEmpty(error))
                        throw new Exception(error);

                    OnError("failed getting plot id", ex);
                    throw;
                }
            }

            private static async Task<string> GetLoginTokenWithRetries(HttpClient client, ConnectionInfo info)
            {
                int failureCount = 0;
                while (true)
                {
                    try
                    {
                        var token = await GetLoginToken(client, info);
                        if (failureCount > 0)
                            CALog.LogInfoAndConsoleLn(LogID.A, $"Reconnected after {failureCount} failed attempts.");
                        return token;
                    }
                    catch (HttpRequestException ex)
                    {
                        if (failureCount++ > 10)
                            throw;

                        OnError("Failed to connect while starting, attempting to reconnect in 5 seconds.", ex);
                        await Task.Delay(5000);
                    }
                }
            }

            private static async Task<string> GetLoginToken(HttpClient client, ConnectionInfo accountInfo)
            {
                string? token = await Post(client, $"/api/v1/user/login?user={accountInfo.email}&password={accountInfo.password}");
                if (string.IsNullOrEmpty(token))
                    token = await Post(client, $"/api/v1/user/CreateAccount?user={accountInfo.email}&password={accountInfo.password}&fullName={accountInfo.Fullname}"); // attempt to create account assuming it did not exist
                if (!string.IsNullOrEmpty(token))
                    return token;

                throw new Exception("Unable to login or create token");
            }

            private static async Task<string?> Post(HttpClient client, string requestUri)
            {
                var response = await client.PostAsync(requestUri, null);
                if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content != null)
                    return (await response.Content.ReadAsStringAsync()).Replace("\"","");
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return null;

                throw new Exception(response.ReasonPhrase);
            }

            private static HttpClient NewClient(string server)
            {
                var client = new HttpClient();
                client.BaseAddress = new Uri(server);
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
            UploadVectorsCycle,
            PostedVector,
            LocalClusterRunning,
            PostedEvent,
            UploadEventsCycle,
            PostingVector,
            CheckState,
            PostingEvent
        }
    }
}
