using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace CA_DataUploaderLib
{
    public class ServerUploader
    {
        private HttpClient _client = new HttpClient();
        private RSACryptoServiceProvider _rsaWriter = new RSACryptoServiceProvider(1024);
        private Queue<DataVector> _queue = new Queue<DataVector>();
        private int _plotID;
        private int _vectorLen;
        private string _keyFilename = "keyBlob.bin";
        private string _loopName;

        public ServerUploader(string server, VectorDescription vectorDescription)
        {
            try
            {
                _client.BaseAddress = new Uri(server + "/api/");
                _client.DefaultRequestHeaders.Accept.Clear();
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _loopName = IOconf.GetLoopName();
                Console.WriteLine(_loopName);

                if (File.Exists(_keyFilename))
                    _rsaWriter.ImportCspBlob(File.ReadAllBytes(_keyFilename));
                else
                    File.WriteAllBytes(_keyFilename, _rsaWriter.ExportCspBlob(true));

                _plotID = GetPlotIDAsync(_rsaWriter.ExportCspBlob(false), GetBytes(vectorDescription)).Result;
                _vectorLen = vectorDescription.Length;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
        }

        public void SendVector(List<double> vector, DateTime timestamp)
        {
            if (vector.Count() != _vectorLen)
                throw new ArgumentException($"wrong vector length: {vector.Count()} <> {_vectorLen}");

            if (_queue.Count < 100)  // if problems then drop packages. 
            {
                _queue.Enqueue(new DataVector
                {
                    timestamp = timestamp,
                    vector = vector
                });
            }

            Task.Run(() => {
                while (_queue.Any())
                {
                    PostVectorAsync(_queue.Dequeue());
                }
            });
        }

        private byte[] GetBytes(List<double> vector)
        {
            var raw = new byte[vector.Count() * sizeof(double)];
            Buffer.BlockCopy(vector.ToArray(), 0, raw, 0, raw.Length);
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionMode.Compress))
                {
                    gzip.Write(raw, 0, raw.Length);
                }

                // create and prepend the signature. 
                byte[] signature = _rsaWriter.SignData(raw, new SHA1CryptoServiceProvider());
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
                string query = $"LoopApi?LoopName={_loopName}&Ticks={DateTime.Now.Ticks}";
                HttpResponseMessage response = await _client.PutAsJsonAsync(query, publicKey.Concat(vectorDescription));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsAsync<int>();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
        }

        private async void PostVectorAsync(DataVector dv)
        {
            try
            {
                var buffer = GetBytes(dv.vector);
                string query = $"LoopApi?plotnameID={_plotID}&Ticks={dv.timestamp.Ticks}";
                HttpResponseMessage response = await _client.PutAsJsonAsync(query, buffer);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable to upload vector to server");
                Console.WriteLine(ex.Message);
            }
        }

        public async void UploadSensorMatch(string newDescription)
        {
            try
            {
                string query = $"LoopApi?plotnameID={_plotID}";
                HttpResponseMessage response = await _client.PutAsJsonAsync(query, SignedMessage(newDescription));
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
        }
    }
}
