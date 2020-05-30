using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib;
using System.Collections.Generic;
using System.Threading;

namespace UnitTests
{
    [TestClass]
    public class ServerUploaderTests
    {
        [TestMethod]
        public void CreateAccountIntegrationTest()
        {
            using (var cloud = new ServerUploader(GetVectorDescription(), new CommandHandler()))
            {
                var vector = new List<double> { 1, 2, 3, 4 };
                cloud.SendVector(vector, DateTime.UtcNow);
                Thread.Sleep(2); // wait for data to be uploaded. 
            }
        }

        private VectorDescription GetVectorDescription()
        {
            var list = new List<VectorDescriptionItem>();
            for (int i = 0; i < 4; i++)
                list.Add(new VectorDescriptionItem("double", "x" + i.ToString(), DataTypeEnum.Input));
            return new VectorDescription(list, RpiVersion.GetHardware(), RpiVersion.GetSoftware());
        }
    }
}
