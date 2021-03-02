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
    public class ServerUploader : IDisposable
    {
        private HttpClient _client = new HttpClient();
        private RSACryptoServiceProvider _rsaWriter = new RSACryptoServiceProvider(1024);
        private Queue<DataVector> _queue = new Queue<DataVector>();
        private Queue<string> _alertQueue = new Queue<string>();
        private List<DateTime> _badPackages = new List<DateTime>();
        private List<IOconfAlert> _alerts;
        private Dictionary<string, string> _accountInfo;
        private int _plotID;
        private string _plotname;
        private DateTime _lastTimestamp;
        private DateTime _waitTimestamp = DateTime.Now;
        private string _keyFilename;
        private string _loopName;
        private string _loginToken;
        private bool _running;
        private VectorDescription _vectorDescription;
        private readonly CommandHandler _cmd;

        public ServerUploader(VectorDescription vectorDescription) : this(vectorDescription, null)
        {
        }

        public ServerUploader(VectorDescription vectorDescription, CommandHandler cmd)
        {
            try
            {
                CheckInputData(vectorDescription);
                var connectionInfo = GetAccountInfo();

                string server = connectionInfo.Server;
                _client.BaseAddress = new Uri(server);
                _client.DefaultRequestHeaders.Accept.Clear();
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _loopName = IOconfFile.GetLoopName();
                _alerts = GetAlerts(vectorDescription, cmd);
                _keyFilename = "Key" + _loopName + ".bin";
                CALog.LogInfoAndConsoleLn(LogID.A, _loopName);

                if (File.Exists(_keyFilename))
                    _rsaWriter.ImportCspBlob(File.ReadAllBytes(_keyFilename));
                else
                    File.WriteAllBytes(_keyFilename, _rsaWriter.ExportCspBlob(true));

                vectorDescription.IOconf = IOconfFile.RawFile;
                _vectorDescription = vectorDescription;

                GetLoginTokenWithRetries().Wait();
                GetPlotIDAsync(_rsaWriter.ExportCspBlob(false), GetBytes(vectorDescription));

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

        private static List<IOconfAlert> GetAlerts(VectorDescription vectorDesc, CommandHandler cmd)
        {
            var alerts = IOconfFile.GetAlerts().ToList();
            var alertsWithoutItem = alerts.Where(a => !vectorDesc.HasItem(a.Sensor)).ToList();
            foreach (var alert in alertsWithoutItem)
                CALog.LogErrorAndConsoleLn(LogID.A, $"ERROR in {Directory.GetCurrentDirectory()}\\IO.conf:{Environment.NewLine} Alert: {alert.Name} points to missing sensor: {alert.Sensor}");
            if (alertsWithoutItem.Count > 0)
                throw new InvalidOperationException("Misconfigured alerts detected");
            if (alerts.Any(a => a.TriggersEmergencyShutdown) && cmd == null)
                throw new InvalidOperationException("Alert with emergency shutdown is configured, but command handler is not available to trigger it");
            return alerts;
        }

        private static void CheckInputData(VectorDescription vectorDescription)
        {
            var dublicates = vectorDescription._items.GroupBy(x => x.Descriptor).Where(x => x.Count() > 1).Select(x => x.Key);
            if (dublicates.Any())
                throw new Exception("Title of datapoint in vector was listed twice: " + string.Join(", ", dublicates));
        }

        private ConnectionInfo GetAccountInfo()
        {
            var connectionInfo = IOconfFile.GetConnectionInfo();
            _accountInfo = new Dictionary<string, string>
                {
                    { "email", connectionInfo.email },
                    { "password", connectionInfo.password },
                    { "fullname", connectionInfo.Fullname }
                };
            return connectionInfo;
        }

        public void SendVector(List<double> vector, DateTime timestamp)
        {
            if (vector.Count() != _vectorDescription.Length)
                throw new ArgumentException($"wrong vector length (input, expected): {vector.Count} <> {_vectorDescription.Length}");

            foreach (var a in _alerts)
                CheckAndTriggerAlert(vector, timestamp, a);

            lock (_queue)
                if (_queue.Count < 10000 && _lastTimestamp < timestamp)  // if problems then drop packages. 
                {
                    _queue.Enqueue(new DataVector
                    {
                        timestamp = timestamp,
                        vector = vector
                    });

                    _lastTimestamp = timestamp;
                }
        }

        private void CheckAndTriggerAlert(List<double> vector, DateTime timestamp, IOconfAlert a)
        {
            if (!a.CheckValue(GetVectorValue(vector, a.Sensor))) return;

            CALog.LogErrorAndConsoleLn(LogID.A, timestamp.ToString("yyyy.MM.dd HH:mm:ss") + a.Message);
            if (a.TriggersEmergencyShutdown)
                _cmd.Execute("emergencyshutdown");

            lock (_alertQueue)
                if (_alertQueue.Count < 10000)  // if problems then drop packages. 
                    _alertQueue.Enqueue(timestamp.ToString("yyyy.MM.dd HH:mm:ss") + a.Message);
        }

        // service method, that makes it easy to control the duration of each loop in (milliseconds)
        public int Wait(int milliseconds)
        {
            int wait = milliseconds - (int)DateTime.Now.Subtract(_waitTimestamp).TotalMilliseconds;
            Thread.Sleep(Math.Max(0, wait));
            _waitTimestamp = DateTime.Now;
            return wait;
        }

        public async void UploadSensorMatch(string newDescription)
        {
            try
            {
                string query = $"api/LoopApi?plotnameID={_plotID}";
                HttpResponseMessage response = await _client.PutAsJsonAsync(query, SignedMessage(newDescription));
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                OnError("failed uploading sensor match", ex);
                throw;
            }
        }

        public string GetPlotUrl()
        {
            return "https://www.copenhagenatomics.com/Plots/TemperaturePlot.php?" + _plotname;
        }

        public string PrintMyPlots()
        {
            var sb = new StringBuilder(Environment.NewLine);
            foreach (var x in ListMyPlots())
                sb.AppendLine(x.Value);

            sb.AppendLine();
            return sb.ToString();
        }

        public Dictionary<string, string> ListMyPlots()
        {
            try
            {
                string query = $"plots/ListMyPlots?token={_loginToken}";
                var result = _client.GetStringAsync(query).Result;
                var startPos = result.IndexOf("Table\":[{") + 9;
                result = result.Substring(startPos); // remove LoggedIn + Table "header"
                result = result.Substring(0, result.Length - 2); // remove squar brackets. 
                List<string> list = FormatPlotList(result);
                return list.ToDictionary(x => x.StringBetween("\"PlotNameId\":\"", "\",\""), x => x);
            }
            catch (Exception ex)
            {
                OnError("failed listing plots", ex);
                throw;
            }
        }

        private static List<string> FormatPlotList(string result)
        {
            var list = result.Split("{".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).ToList();
            var maxPos = list.Max(x => x.IndexOf("PlotName"));
            list = list.Select(x => FormatPlotList(x, maxPos, "\"PlotName")).ToList();
            maxPos = list.Max(x => x.IndexOf("VectorLength"));
            list = list.Select(x => FormatPlotList(x, maxPos, "\"VectorLength")).ToList();
            return list;
        }

        private static string FormatPlotList(string str, int maxPos, string padBefore)
        {
            if (str.EndsWith("},"))
                str = str.Substring(0, str.Length - 2);
            if(str.EndsWith("}"))
                str = str.Substring(0, str.Length - 1);

            while (str.IndexOf(padBefore) < maxPos)
                str = str.Replace(padBefore, " " + padBefore);

            return str;
        }

        private void LoopForever()
        {
            _running = true;
            while (_running)
            {
                try
                {
                    List<DataVector> list = new List<DataVector>();
                    lock (_queue)
                    {
                        while (_queue.Any())  // dequeue all. 
                        {
                            list.Add(_queue.Dequeue());
                        }
                    }

                    if (list.Any())
                    {
                        byte[] listLen = BitConverter.GetBytes((ushort)list.Count());
                        var theData = list.SelectMany(a => a.buffer).ToArray();
                        var buffer = Compress(listLen.Concat(theData).ToArray());
                        PostVectorAsync(buffer, list.First().timestamp);
                    }

                    lock (_alertQueue)
                    {
                        while (_alertQueue.Any())  // dequeue all. 
                        {
                            PostAlertAsync(_alertQueue.Dequeue());
                        }
                    }

                    PrintBadPackagesMessage(false);
                    Thread.Sleep(IOconfFile.GetVectorUploadDelay());  
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, "ServerUploader.LoopForever() exception: " + ex.Message, ex);
                }
            }

            PrintBadPackagesMessage(true);
        }

        private byte[] Compress(byte[] buffer)
        {
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionMode.Compress))
                {
                    gzip.Write(buffer, 0, buffer.Length);
                }

                // create and prepend the signature. 
                byte[] signature = _rsaWriter.SignData(buffer, new SHA1CryptoServiceProvider());
                return signature.Concat(memory.ToArray()).ToArray();
            }
        }

        private byte[] GetBytes(VectorDescription vectorDescription)
        {
            var xmlserializer = new XmlSerializer(typeof(VectorDescription));

            using (var msXml = new MemoryStream())
            using (var msZip = new MemoryStream())
            {
                xmlserializer.Serialize(msXml, vectorDescription);
                var buffer = msXml.ToArray();
                using (var gzip = new GZipStream(msZip, CompressionMode.Compress))
                {
                    gzip.Write(buffer, 0, buffer.Length);
                }                

                // create and prepend the signature. 
                byte[] signature = _rsaWriter.SignData(buffer, new SHA1CryptoServiceProvider());
                return signature.Concat(msZip.ToArray()).ToArray();
            }
        }

        private byte[] SignedMessage(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            // create and prepend the signature. 
            byte[] signature = _rsaWriter.SignData(buffer, new SHA1CryptoServiceProvider());
            return signature.Concat(buffer).ToArray();
        }


        private void GetPlotIDAsync(byte[] publicKey, byte[] vectorDescription)
        {
            HttpResponseMessage response = null;
            try
            {
                string query = $"api/LoopApi?LoopName={_loopName}&ticks={DateTime.UtcNow.Ticks}&loginToken={_loginToken}";
                response = _client.PutAsJsonAsync(query, publicKey.Concat(vectorDescription)).Result;
                response.EnsureSuccessStatusCode();
                var result = response.Content.ReadAsAsync<string>().Result;
                _plotID = result.StringBefore(" ").ToInt();
                _plotname = result.StringAfter(" ");
            }
            catch (Exception ex)
            {
                if (ex.InnerException?.Message == "The remote name could not be resolved: 'www.theng.dk'" || ex.InnerException?.InnerException?.Message == "The remote name could not be resolved: 'www.theng.dk'")
                    throw new HttpRequestException("Check your internet connection", ex);

                var error = response?.Content?.ReadAsStringAsync()?.Result;
                if (!string.IsNullOrEmpty(error))
                    throw new Exception(error);

                OnError("failed getting plot id", ex);
                throw;
            }
        }

        private static void OnError(string message, Exception ex) => CALog.LogErrorAndConsoleLn(LogID.A, message, ex);

        private async void PostVectorAsync(byte[] buffer, DateTime timestamp)
        {
            try
            {
                string query = $"api/LoopApi?plotnameID={_plotID}&Ticks={timestamp.Ticks}&loginToken={_loginToken}";
                HttpResponseMessage response = await _client.PutAsJsonAsync(query, buffer);
                response.EnsureSuccessStatusCode();
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
                string query = $"api/LoopApi/AlertMessage?plotnameID={_plotID}";

                HttpResponseMessage response = await _client.PutAsJsonAsync(query, SignedMessage(message));
                response.EnsureSuccessStatusCode();
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

        private async Task GetLoginTokenWithRetries()
        {
            int failureCount = 0;
            while (true)
            {
                try
                {
                    await GetLoginToken();
                    if (failureCount > 0)
                        CALog.LogInfoAndConsoleLn(LogID.A, "Reconnected.");
                    return;
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

        private async Task GetLoginToken()
        {
            var response = await _client.PostAsync("Login", new FormUrlEncodedContent(_accountInfo));
            if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content != null)
            {
                var dic = response.Content.ReadAsAsync<Dictionary<string, string>>().Result;
                if (dic["status"] == "success")
                {
                    _loginToken = dic["message"];
                    return;
                }

                if (dic["message"] == "email or password does not match")
                {
                    CreateAccount();
                    return;
                }

                throw new Exception(dic["message"]);
            }

            throw new Exception(response.ReasonPhrase);                
        }

        private void CreateAccount()
        {
            var response = _client.PostAsync("Login/CreateAccount", new FormUrlEncodedContent(_accountInfo));
            if (response.Result.StatusCode == System.Net.HttpStatusCode.OK && response.Result.Content != null)
            {
                var dic = response.Result.Content.ReadAsAsync<Dictionary<string, string>>().Result;
                if (dic["status"] == "success")
                {
                    _loginToken = dic["message"];
                    return;
                }

                throw new Exception(dic["message"]);
            }
           
            throw new Exception(response.Result.ReasonPhrase);
        }

        private double GetVectorValue(List<double> values, string name)
        {
            var index = _vectorDescription._items.IndexOf(_vectorDescription._items.Single(x => x.Descriptor == name));
            return values[index];
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _running = false;
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}
