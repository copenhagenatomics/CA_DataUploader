using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Configuration;
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
        private int _plotID;
        private int _vectorLen;
        private string _keyFilename;
        private string _loopName;
        private string _loginToken;
        private bool _running;

        public ServerUploader(VectorDescription vectorDescription)
        {
            try
            {
                string server = ConfigurationManager.AppSettings["server"];
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
                throw new ArgumentException($"wrong vector length: {vector.Count()} <> {_vectorLen}");

            if (_queue.Count < 1000)  // if problems then drop packages. 
            {
                _queue.Enqueue(new DataVector
                {
                    timestamp = timestamp,
                    vector = vector
                });
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

        public Dictionary<string, string> ListMyPlots()
        {
            try
            {
                string query = $"plots/ListMyPlots?token={_loginToken}";
                var result = _client.GetStringAsync(query).Result;
                result = result.Substring(1, result.Length - 2); // remove squar brackets. 
                var list = result.Split("{".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).ToList();

                return list.ToDictionary(x => x.StringBetween("\"PlotName\":", "\",\""), x => x.Substring(0, x.Length-2));
            }
            catch (Exception ex)
            {
                LogHttpException(ex);
                throw;
            }
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

                    Thread.Sleep(900);  // only send approx. one time per second. 
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
            var values = new Dictionary<string, string>
            {
                { "email", ConfigurationManager.AppSettings["email"] },
                { "password", ConfigurationManager.AppSettings["password"] }
            };

            var content = new FormUrlEncodedContent(values);
            var response = _client.PostAsync("Login", content);
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
            var values = new Dictionary<string, string>
            {
                { "email", ConfigurationManager.AppSettings["email"] },
                { "password", ConfigurationManager.AppSettings["password"] },
                { "fullname", ConfigurationManager.AppSettings["fullname"] }
            };

            var content = new FormUrlEncodedContent(values);
            var response = _client.PostAsync("Login/CreateAccount", content);
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
