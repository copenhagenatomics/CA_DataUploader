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
        private CommandHandler _cmd;
        private VectorDescription _vectorDescription;
        private Dictionary<string, string> _accountInfo;
        private int _plotID;
        private int _vectorLen;
        private DateTime _lastTimestamp;
        private DateTime _waitTimestamp = DateTime.Now;
        private string _keyFilename;
        private string _loopName;
        private string _loginToken;
        private bool _running;

        public int MillisecondsBetweenUpload { get; set; }

        public ServerUploader(VectorDescription vectorDescription, CommandHandler cmd)
        {
            try
            {
                var connectionInfo = IOconfFile.GetConnectionInfo();
                _accountInfo = new Dictionary<string, string>
                {
                    { "email", connectionInfo.email },
                    { "password", connectionInfo.password },
                    { "fullname", connectionInfo.Fullname }
                };

                MillisecondsBetweenUpload = 900;
                string server = connectionInfo.Server;
                _cmd = cmd;
                _client.BaseAddress = new Uri(server);
                _client.DefaultRequestHeaders.Accept.Clear();
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _loopName = IOconfFile.GetLoopName();
                _alerts = IOconfFile.GetAlerts().ToList();
                _keyFilename = "Key" + _loopName + ".bin";
                CALog.LogInfoAndConsoleLn(LogID.A, _loopName);

                if (File.Exists(_keyFilename))
                    _rsaWriter.ImportCspBlob(File.ReadAllBytes(_keyFilename));
                else
                    File.WriteAllBytes(_keyFilename, _rsaWriter.ExportCspBlob(true));

                foreach (var math in IOconfFile.GetMath())
                {
                    vectorDescription._items.Add(new VectorDescriptionItem("double", math.Name, DataTypeEnum.State));
                }

                _cmd.SetVectorDescription(vectorDescription);
                _vectorDescription = vectorDescription;
                _vectorLen = vectorDescription.Length;

                GetLoginToken();
                _plotID = GetPlotIDAsync(_rsaWriter.ExportCspBlob(false), GetBytes(vectorDescription)).Result;

                new Thread(() => this.LoopForever()).Start();
                _cmd.AddCommand("escape", Stop);
            }
            catch (Exception ex)
            {
                LogHttpException(ex);
                throw;
            }
        }

        public void SendVector(List<double> vector, DateTime timestamp)
        {
            var dic = GetVectorDictionary(vector);
            IOconfFile.GetMath().ToList().ForEach(x => vector.Add(x.Calculate(dic)));

            if (vector.Count() != _vectorLen)
                throw new ArgumentException($"wrong vector length (input, expected): {vector.Count()} <> {_vectorLen}, Math: {IOconfFile.GetMath().Count()}");

            _cmd.NewData(vector);

            foreach (var a in _alerts)
            {
                if (a.CheckValue(_cmd.GetVectorValue(a.Name)))
                {
                    lock (_alertQueue)
                    {
                        if (_alertQueue.Count < 10000)  // if problems then drop packages. 
                        {
                            _alertQueue.Enqueue(timestamp.ToString("YYYY.MM.dd HH:mm:ss") + a.Message);
                        }
                    }
                }
            }

            lock (_queue)
            {
                if (_queue.Count < 10000)  // if problems then drop packages. 
                {
                    if (_lastTimestamp < timestamp)
                    {
                        _queue.Enqueue(new DataVector
                        {
                            timestamp = timestamp,
                            vector = vector
                        });

                        _lastTimestamp = timestamp;
                    }
                }
            }
        }

        private Dictionary<string, object> GetVectorDictionary(List<double> vector)
        {
            var dic = new Dictionary<string, object>();
            int i = 0;
            foreach(var item in _vectorDescription._items)
            {
                dic.Add(item.Descriptor, vector[i++]);
            }

            return dic;
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
                LogHttpException(ex);
                throw;
            }
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
                return list.ToDictionary(x => x.StringBetween("\"PlotName\":", "\",\""), x => x);
            }
            catch (Exception ex)
            {
                LogHttpException(ex);
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
                    Thread.Sleep(MillisecondsBetweenUpload);  // only send approx. one time per second. 
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, "ServerUploader.LoopForever() exception: " + ex.Message);
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


        private async Task<int> GetPlotIDAsync(byte[] publicKey, byte[] vectorDescription)
        {
            try
            {
                string query = $"api/LoopApi?LoopName={_loopName}&ticks={DateTime.UtcNow.Ticks}&loginToken={_loginToken}";
                HttpResponseMessage response = await _client.PutAsJsonAsync(query, publicKey.Concat(vectorDescription));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsAsync<int>();
            }
            catch (Exception ex)
            {
                if (ex.InnerException?.Message == "The remote name could not be resolved: 'www.theng.dk'" || ex.InnerException?.InnerException?.Message == "The remote name could not be resolved: 'www.theng.dk'")
                    throw new HttpRequestException("Check your internet connection", ex);
                LogHttpException(ex);
                throw;
            }
        }

        private static void LogHttpException(Exception ex)
        {
            if (ex.InnerException == null)
                CALog.LogErrorAndConsole(LogID.A, ex.Message);
            else
                CALog.LogErrorAndConsole(LogID.A, ex.InnerException.Message);
        }

        private static void LogData(Exception ex)
        {
            if (ex.InnerException == null)
                CALog.LogData(LogID.A, ex.Message);
            else
                CALog.LogData(LogID.A, ex.InnerException.Message);
        }

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
                LogData(ex);
            }
        }

        private async void PostAlertAsync(string message)
        {
            try
            {
                string query = $"api/LoopApi?message={message}&loginToken={_loginToken}";
                HttpResponseMessage response = await _client.GetAsync(query);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                lock (_badPackages)
                {
                    _badPackages.Add(DateTime.UtcNow);
                }
                LogData(ex);
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
                    foreach (var minutes in _badPackages.GroupBy(x => x.ToString("dd-MMM-yyyy HH:mm")))
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

        private void GetLoginToken()
        {
            var response = _client.PostAsync("Login", new FormUrlEncodedContent(_accountInfo));
            if (response.Result.StatusCode == System.Net.HttpStatusCode.OK && response.Result.Content != null)
            {
                var dic = response.Result.Content.ReadAsAsync<Dictionary<string, string>>().Result;
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

            throw new Exception(response.Result.ReasonPhrase);                
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
