using CA_DataUploaderLib.Extensions;
using CA_DataUploaderLib.IOconf;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace CA_DataUploaderLib
{
    public sealed class ServerUploader : IDisposable
    {
        private readonly PlotConnection _plot;
        private readonly Signing _signing;
        private readonly Queue<DataVector> _queue = new Queue<DataVector>();
        private readonly Queue<string> _alertQueue = new Queue<string>();
        private readonly List<DateTime> _badPackages = new List<DateTime>();
        private DateTime _lastTimestamp;
        private DateTime _waitTimestamp = DateTime.Now;
        private DateTime _waitLoopForeverTimestamp = DateTime.Now;
        private bool _running;
        private readonly VectorDescription _vectorDescription;
        private readonly CommandHandler _cmd;

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
                vectorDescription.IOconf = IOconfFile.RawFile;
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

        internal void SendAlert(string message)
        {
            lock (_alertQueue)
                if (_alertQueue.Count < 10000)  // if sending thread can't catch up, then drop packages.
                    _alertQueue.Enqueue(message);
        }

        public void SendVector(List<double> vector, DateTime timestamp)
        {
            if (vector.Count() != _vectorDescription.Length)
                throw new ArgumentException($"wrong vector length (input, expected): {vector.Count} <> {_vectorDescription.Length}");
            if (_lastTimestamp < timestamp)
            {
                CALog.LogData(LogID.B, $"non changing or out of order timestamp received - vector ignored: last recorded {_lastTimestamp} vs received {timestamp}");
                return;
            }

            lock (_queue)
                if (_queue.Count < 10000)  // if sending thread can't catch up, then drop packages.
                {
                    _queue.Enqueue(new DataVector
                    {
                        timestamp = timestamp,
                        vector = vector
                    });

                    _lastTimestamp = timestamp;
                }
        }

        // service method, that makes it easy to control the duration of each SendVector related loop in (milliseconds)
        public int Wait(int milliseconds) => Wait(milliseconds, ref _waitTimestamp);

        // service method, that makes it easy to control the duration of each loop in (milliseconds)
        private static int Wait(int milliseconds, ref DateTime lastWaitDate)
        {
            int wait = milliseconds - (int)DateTime.Now.Subtract(lastWaitDate).TotalMilliseconds;
            Thread.Sleep(Math.Max(0, wait));
            lastWaitDate = DateTime.Now;
            return wait;
        }

        public string GetPlotUrl() => "https://www.copenhagenatomics.com/Plots/TemperaturePlot.php?" + _plot.PlotName;

        private void LoopForever()
        {
            _running = true;
            var vectorUploadDelay = IOconfFile.GetVectorUploadDelay();
            while (_running)
            {
                Wait(vectorUploadDelay, ref _waitLoopForeverTimestamp);
                try
                {
                    var list = DequeueAllEntries(_queue);
                    if (list != null)
                        PostVectorAsync(GetSignedVectors(list), list.First().timestamp);

                    var alerts = DequeueAllEntries(_alertQueue);
                    foreach (var alert in alerts)
                        PostAlertAsync(alert);

                    PrintBadPackagesMessage(false);
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, "ServerUploader.LoopForever() exception: " + ex.Message, ex);
                }
            }

            PrintBadPackagesMessage(true);
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

        private byte[] SignedMessage(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            return _signing.GetSignature(buffer).Concat(buffer).ToArray();
        }

        private static void OnError(string message, Exception ex) => CALog.LogErrorAndConsoleLn(LogID.A, message, ex);

        private async void PostVectorAsync(byte[] buffer, DateTime timestamp)
        {
            try
            {
                await _plot.PostVectorAsync(buffer, timestamp);
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

        private async void PostAlertAsync(string message)
        {
            try
            {
                await _plot.PostAlertAsync(SignedMessage(message));
            }
            catch (Exception ex)
            {
                lock (_badPackages)
                {
                    _badPackages.Add(DateTime.UtcNow);
                }
                OnError("failed posting alert: " + message, ex);
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
                    CALog.LogInfoAndConsoleLn(LogID.A, $"Vector upload errors within the last hour:");
                    foreach (var minutes in _badPackages.GroupBy(x => x.ToString("MMM dd HH:mm")))
                        CALog.LogInfoAndConsoleLn(LogID.A, $"{minutes.Key} = {minutes.Count()}");

                    _badPackages.Clear();
                }
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
                string query = $"api/LoopApi?plotnameID={_plotID}&Ticks={timestamp.Ticks}";
                var response = await _client.PutAsJsonAsync(query, buffer);
                response.EnsureSuccessStatusCode();
            }

            public async Task PostAlertAsync(byte[] signedMessage)
            {
                string query = $"api/LoopApi/AlertMessage?plotnameID={_plotID}";
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
                HttpResponseMessage response = null;
                try
                {
                    string query = $"api/LoopApi?LoopName={loopName}&ticks={DateTime.UtcNow.Ticks}&loginToken={loginToken}";
                    response = await client.PutAsJsonAsync(query, publicKey.Concat(signedVectorDescription));
                    response.EnsureSuccessStatusCode();
                    var result = response.Content.ReadAsAsync<string>().Result;
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
                        var token = await GetLoginToken(client, accountInfo);
                        if (failureCount > 0)
                            CALog.LogInfoAndConsoleLn(LogID.A, "Reconnected.");
                        return token;
                    }
                    catch (HttpRequestException ex)
                    {
                        if (failureCount++ > 10)
                            throw;

                        CALog.LogErrorAndConsoleLn(LogID.A, "Failed to connect while starting, attempting to reconnect in 5 seconds.", ex);
                        await Task.Delay(5000);
                    }
                }
            }

            private static async Task<string> GetLoginToken(HttpClient client, Dictionary<string, string> accountInfo)
            {
                var (status, message) = await Post(client, accountInfo, "Login");
                if (message == "email or password does not match")
                    (status, message) = await Post(client, accountInfo, "Login/CreateAccount"); // attempt to create account assuming it did not exist
                if (status == "success")
                    return message;

                throw new Exception(message);
            }
            
            private static async Task<(string status, string message)> Post(HttpClient client, Dictionary<string, string> accountInfo, string requestUri)
            {
                var response = client.PostAsync(requestUri, new FormUrlEncodedContent(accountInfo));
                if (response.Result.StatusCode == System.Net.HttpStatusCode.OK && response.Result.Content != null)
                {
                    var dic = await response.Result.Content.ReadAsAsync<Dictionary<string, string>>();
                    return (dic["status"], dic["message"]);
                }
            
                throw new Exception(response.Result.ReasonPhrase);
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
                CALog.LogInfoAndConsoleLn(LogID.A, loopName);

                if (File.Exists(keyFilename))
                    _rsaWriter.ImportCspBlob(File.ReadAllBytes(keyFilename));
                else
                    File.WriteAllBytes(keyFilename, _rsaWriter.ExportCspBlob(true));
            }

            public byte[] GetPublicKey() => _rsaWriter.ExportCspBlob(false);

            public byte[] GetSignature(byte[] data) => _rsaWriter.SignData(data, new SHA1CryptoServiceProvider());
        }
    }
}
