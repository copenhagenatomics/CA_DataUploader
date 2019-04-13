using CA_DataUploaderLib.Extensions;
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
        private FormUrlEncodedContent _accountInfo;
        private int _plotID;
        private int _vectorLen;
        private DateTime _lastTimestamp;
        private string _keyFilename;
        private string _loopName;
        private string _loginToken;
        private bool _running;

        public int MillisecondsBetweenUpload { get; set; }

        public ServerUploader(VectorDescription vectorDescription)
        {
            try
            {
                var connectionInfo = IOconf.GetConnectionInfo();
                var values = new Dictionary<string, string>
                {
                    { "email", connectionInfo.email },
                    { "password", connectionInfo.password },
                    { "fullname", connectionInfo.Fullname }
                };

                _accountInfo = new FormUrlEncodedContent(values);
                MillisecondsBetweenUpload = 900;
                string server = connectionInfo.Server;
                _client.BaseAddress = new Uri(server);
                _client.DefaultRequestHeaders.Accept.Clear();
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _loopName = IOconf.GetLoopName();
                _keyFilename = "Key" + _loopName + ".bin";
                CALog.LogInfoAndConsoleLn(LogID.A, _loopName);

                if (File.Exists(_keyFilename))
                    _rsaWriter.ImportCspBlob(File.ReadAllBytes(_keyFilename));
                else
                    File.WriteAllBytes(_keyFilename, _rsaWriter.ExportCspBlob(true));

                GetLoginToken();

                _plotID = GetPlotIDAsync(_rsaWriter.ExportCspBlob(false), GetBytes(vectorDescription)).Result;
                _vectorLen = vectorDescription.Length;
                new Thread(() => this.LoopForever()).Start();
            }
            catch (Exception ex)
            {
                LogHttpException(ex);
                throw;
            }
        }

        public void SendVector(List<double> vector, DateTime timestamp)
        {
            if (vector.Count() != _vectorLen)
                throw new ArgumentException($"wrong vector length (input, expected): {vector.Count()} <> {_vectorLen}");

            if (_queue.Count < 1000)  // if problems then drop packages. 
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
                    while (_queue.Any())  // dequeue all. 
                    {
                        list.Add(_queue.Dequeue());
                    }

                    if (list.Any())
                    {
                        byte[] listLen = BitConverter.GetBytes((ushort)list.Count());
                        var theData = list.SelectMany(a => a.buffer).ToArray();
                        var buffer = Compress(listLen.Concat(theData).ToArray());
                        PostVectorAsync(buffer, list.First().timestamp);
                    }

                    Thread.Sleep(MillisecondsBetweenUpload);  // only send approx. one time per second. 
                }
                catch (Exception ex)
                {
                    CALog.LogErrorAndConsole(LogID.A, "ServerUploader.LoopForever() exception: " + ex.Message);
                }
            }
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
                string query = $"api/LoopApi?LoopName={_loopName}&ticks={DateTime.Now.Ticks}&loginToken={_loginToken}";
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
                CALog.LogInfoAndConsoleLn(LogID.A, "Unable to upload vector to server: " + timestamp.ToString("HH:mm:ss"));
                LogHttpException(ex);
            }
        }

        private void GetLoginToken()
        {
            var response = _client.PostAsync("Login", _accountInfo);
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
            var response = _client.PostAsync("Login/CreateAccount", _accountInfo);
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
