using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
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
        private readonly Queue<DataVector> _queue = new Queue<DataVector>();
        private readonly Queue<EventFiredArgs> _eventsQueue = new Queue<EventFiredArgs>();
        private readonly List<DateTime> _badPackages = new List<DateTime>();
        private DateTime _lastTimestamp;
        private bool _running;
        private readonly VectorDescription _vectorDescription;
        private readonly CommandHandler _cmd;
        private readonly Channel<UploadState> _executedActionChannel = Channel.CreateBounded<UploadState>(10000);

        public ServerUploader(VectorDescription vectorDescription) : this(vectorDescription, null)
        {
        }

        public ServerUploader(VectorDescription vectorDescription, CommandHandler cmd)
        {
            try
            {
                var duplicates = vectorDescription._items.GroupBy(x => x.Descriptor).Where(x => x.Count() > 1).Select(x => x.Key);
                if (duplicates.Any())
                    throw new Exception("Title of datapoint in vector was listed twice: " + string.Join(", ", duplicates));
                var loopName = IOconfFile.GetLoopName();
                _signing = new Signing(loopName);
                vectorDescription.IOconf = IOconfFile.GetRawFile();
                _vectorDescription = vectorDescription;
                _plot = PlotConnection.Establish(loopName, _signing.GetPublicKey(), GetSignedVectorDescription(vectorDescription)).GetAwaiter().GetResult();
                new Thread(() => this.LoopForever()).Start();
                _cmd = cmd;
                cmd?.AddCommand("escape", Stop);

            }
            catch (Exception ex)
            {
                OnError("failed initializing uploader", ex);
                throw;
            }
        }

        public void SendEvent(object sender, EventFiredArgs e)
        {
            lock (_eventsQueue)
                if (_eventsQueue.Count < 10000)  // if sending thread can't catch up, then drop packages.
                    _eventsQueue.Enqueue(e);
        }

        public void SendVector(DataVector vector)
        {
            if (vector.Count() != _vectorDescription.Length)
                throw new ArgumentException($"wrong vector length (input, expected): {vector.Count} <> {_vectorDescription.Length}");
            if (vector.timestamp <= _lastTimestamp)
            {
                CALog.LogData(LogID.B, $"non changing or out of order timestamp received - vector ignored: last recorded {_lastTimestamp} vs received {vector.timestamp}");
                return;
            }

            lock (_queue)
                if (_queue.Count < 10000)  // if sending thread can't catch up, then drop packages.
                {
                    _queue.Enqueue(vector);
                    _lastTimestamp = vector.timestamp;
                }
        }

        public string GetPlotUrl() => "https://www.copenhagenatomics.com/Plots/TemperaturePlot.php?" + _plot.PlotName;

        private void LoopForever()
        {
            _running = true;
            _ = Task.Run(TrackUploadState);

            var vectorUploadDelay = IOconfFile.GetVectorUploadDelay();
            var throttle = new TimeThrottle(vectorUploadDelay);
            var writer = _executedActionChannel.Writer;
            while (_running)
            {
                throttle.Wait();
                writer.TryWrite(UploadState.StartingUploadCycle);
                SendQueuedData(false, writer);
            }

            CALog.LogInfoAndConsoleLn(LogID.A, "uploader is stopping, trying to send remaining queued messages");
            SendQueuedData(true, writer);

            async void SendQueuedData(bool stopping, ChannelWriter<UploadState> writer)
            {
                try
                {
                    var list = DequeueAllEntries(_queue);
                    if (list != null)
                    {
                        var task = PostVectorAsync(GetSignedVectors(list), list.First().timestamp, writer);
                        if (stopping) await task;
                    }

                    var events = DequeueAllEntries(_eventsQueue);
                    if (events != null)
                    {
                        foreach (var @event in events)
                        {
                            var task = PostEventAsync(@event);
                            if (stopping) await task;
                        }
                    }

                    if (stopping)
                    {
                        await PostEventAsync(new EventFiredArgs("uploader has stopped", EventType.Log, DateTime.UtcNow));
                        writer.Complete();
                    }

                    PrintBadPackagesMessage(stopping);
                }
                catch (Exception ex)
                {//we don't normally expect to reach here, as the post method above capture and log any exceptions
                    CALog.LogErrorAndConsoleLn(LogID.A, "ServerUploader.LoopForever() exception: " + ex.Message, ex);
                }
            }
        }

        private async Task TrackUploadState()
        {
            var token = _cmd.StopToken;
            try
            {
                await Task.Delay(5000, token); //give some time for initialization + other nodes starting in multipi.
                var startingUploadCheckArgs = (Stopwatch.StartNew(), 2500);
                var postingVectorCheckArgs = (Stopwatch.StartNew(), 2500);
                var postedVectorCheckArgs = (Stopwatch.StartNew(), 2500);
                await foreach (var state in _executedActionChannel.Reader.ReadAllAsync(token))
                {
                    DetectSlowActionOnNewAction(ref startingUploadCheckArgs, UploadState.StartingUploadCycle, state);
                    DetectSlowActionOnNewAction(ref postingVectorCheckArgs, UploadState.PostingVector, state);
                    DetectSlowActionOnNewAction(ref postedVectorCheckArgs, UploadState.PostedVector, state);
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == token) { }
            catch (Exception ex)
            {
                OnError("Unexpected error tracking upload state", ex);
            }

            static void DetectSlowActionOnNewAction(ref (Stopwatch timeSinceLastAction, int nextTargetToReportSlowAction) stateCheckArgs, UploadState expectedState, UploadState state)
            {
                if (stateCheckArgs.timeSinceLastAction.ElapsedMilliseconds > stateCheckArgs.nextTargetToReportSlowAction)
                {
                    OnError($"detected slow {expectedState} - time passed: {stateCheckArgs.timeSinceLastAction.Elapsed}", null);
                    stateCheckArgs.nextTargetToReportSlowAction *= 2;
                }

                if (state != expectedState) return;

                stateCheckArgs.timeSinceLastAction.Restart();
                stateCheckArgs.nextTargetToReportSlowAction = 2500;
            }
        }

        List<T> DequeueAllEntries<T>(Queue<T> queue)
        {
            List<T> list = null; // delayed initialization to avoid creating lists when there is no data.
            lock (queue)
                while (queue.Any())  // dequeue all. 
                {
                    list = list ?? new List<T>(); // ensure initialized
                    list.Add(queue.Dequeue());
                }

            return list;
        }

        /// <returns>a signature of the original data followed by the data compressed with gzip.</returns>
        private byte[] SignAndCompress(byte[] buffer)
        {
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionMode.Compress))
                {
                    gzip.Write(buffer, 0, buffer.Length);
                }

                return _signing.GetSignature(buffer).Concat(memory.ToArray()).ToArray();
            }
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
            byte[] listLen = BitConverter.GetBytes((ushort)vectors.Count());
            var theData = vectors.SelectMany(a => a.buffer).ToArray();
            return SignAndCompress(listLen.Concat(theData).ToArray());
        }

        private byte[] GetSignedEvent(EventFiredArgs @event)
        { //this can be made more efficient to avoid extra allocations and/or use a memory pool, but these are for low frequency events so postponing looking at that.
            var bytes = @event.ToByteArray();
            return _signing.GetSignature(bytes).Concat(bytes).ToArray();
        }

        private static void OnError(string message, Exception ex)
        {
            if (ex != null)
                CALog.LogError(LogID.A, message, ex); //note we don't use LogErrorAndConsoleLn variation, as CALog.LoggerForUserOutput may be set to generate events that are only visible on the event log.
            else
                CALog.LogData(LogID.A, $"{DateTime.Now:MM.dd HH:mm:ss} - {message}");
            Console.WriteLine(message);
        }

        private async Task PostVectorAsync(byte[] buffer, DateTime timestamp, ChannelWriter<UploadState> writer)
        {
            try
            {
                writer.TryWrite(UploadState.PostingVector);
                await _plot.PostVectorAsync(buffer, timestamp);
                writer.TryWrite(UploadState.PostedVector);
            }
            catch (Exception ex)
            {
                lock (_badPackages)
                {
                    _badPackages.Add(DateTime.UtcNow);
                }
                OnError("failed posting vector", ex);
            }
        }

        private async Task PostEventAsync(EventFiredArgs args)
        {
            try
            {
                if (args.EventType == (byte)EventType.SystemChangeNotification)
                    await PostSystemChangeNotificationAsync(args);//this special event turns into reported boards serial info + still an event with a shorter description
                else
                    await Post(args);
            }
            catch (Exception ex)
            {
                lock (_badPackages)
                {
                    _badPackages.Add(DateTime.UtcNow);
                }

                OnError($"failed posting event: {args.EventType} - {args.Data} - {args.TimeSpan}", ex);
            }

            Task Post(EventFiredArgs args) => _plot.PostEventAsync(GetSignedEvent(args));
            Task PostBoardsSerialInfo(SystemChangeNotificationData data, DateTime timeSpan) =>
                _plot.PostBoardsSerialInfo(SignAndCompress(data.ToBoardsSerialInfoJsonUtf8Bytes(timeSpan)));
            Task PostSystemChangeNotificationAsync(EventFiredArgs args)
            {
                var data = SystemChangeNotificationData.ParseJson(args.Data);
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

        private void PrintBadPackagesMessage(bool force)
        {
            if (!_badPackages.Any())
                return;

            lock (_badPackages)
            {
                if (force || _badPackages.First().AddHours(1) < DateTime.UtcNow)
                {
                    var msg = GetBadPackagesErrorMessage(_badPackages);
                    CALog.LogInfoAndConsoleLn(LogID.A, msg);
                    _badPackages.Clear();
                }
            }

            static string GetBadPackagesErrorMessage(List<DateTime> times)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Vector upload errors within the last hour:");
                foreach (var minutes in times.GroupBy(x => x.ToString("MMM dd HH:mm")))
                    sb.AppendLine($"{minutes.Key} = {minutes.Count()}");
                return sb.ToString();
            }
        }

        private bool Stop(List<string> args)
        {
            _running = false;
            return true;
        }

        public void Dispose()
        { // class is sealed so don't need full blown IDisposable pattern.
            _running = false;
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
                HttpResponseMessage response = null;
                try
                {
                    string query = $"/api/v1/Plotdata/GetPlotnameId?loopname={loopName}&ticks={DateTime.UtcNow.Ticks}&logintoken={loginToken}";
                    var signedValue = publicKey.Concat(signedVectorDescription).ToArray(); // Note that it will only work if converted to array and not IEnummerable
                    response = await client.PutAsJsonAsync(query, signedValue);
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadFromJsonAsync<string>();
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
                var accountInfo = new Dictionary<string, string> { { "email", info.email }, { "password", info.password }, { "fullname", info.Fullname } };
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
                string token = await Post(client, $"/api/v1/user/login?user={accountInfo.email}&password={accountInfo.password}");
                if (string.IsNullOrEmpty(token))
                    token = await Post(client, $"/api/v1/user/CreateAccount?user={accountInfo.email}&password={accountInfo.password}&fullName={accountInfo.Fullname}"); // attempt to create account assuming it did not exist
                if (!string.IsNullOrEmpty(token))
                    return token;

                throw new Exception("Unable to login or create token");
            }

            private static async Task<string> Post(HttpClient client, string requestUri)
            {
                //ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                var response = await client.PostAsync(requestUri, null);
                if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content != null)
                {
                    return (await response.Content.ReadAsStringAsync())?.Replace("\"","");

                }
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
            private RSACryptoServiceProvider _rsaWriter = new RSACryptoServiceProvider(1024);

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
            StartingUploadCycle,
            PostingVector,
            PostedVector
        }
    }
}
