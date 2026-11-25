using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace UnitTests
{
    [TestClass]
    public class UploadRecoveryTests
    {
        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(1, 0)]
        [DataRow(2, 0)]
        [DataRow(3, 1)]
        [DataRow(5, 1)]
        public void Recovery_LogsOnceOnlyAfterThreeOrMoreConsecutiveFailures(int failures, int expectedLogs)
        {
            var count = 0;
            var logs = new List<string>();
            for (var i = 0; i < failures; i++)
                ServerUploader.TrackUploadRecovery(false, ref count, "Vector", logs.Add);

            Assert.HasCount(0, logs);
            ServerUploader.TrackUploadRecovery(true, ref count, "Vector", logs.Add);
            ServerUploader.TrackUploadRecovery(true, ref count, "Vector", logs.Add);

            Assert.AreEqual(0, count);
            Assert.HasCount(expectedLogs, logs);
            if (expectedLogs > 0)
                Assert.AreEqual($"Vector uploads working again after {failures} consecutive failures.", logs[0]);
        }

        [TestMethod]
        public void Recovery_SuccessResetsFailuresBelowThreshold()
        {
            var count = 0;
            var logs = new List<string>();
            for (var burst = 0; burst < 3; burst++)
            {
                ServerUploader.TrackUploadRecovery(false, ref count, "Vector", logs.Add);
                ServerUploader.TrackUploadRecovery(false, ref count, "Vector", logs.Add);
                ServerUploader.TrackUploadRecovery(true, ref count, "Vector", logs.Add);
            }

            Assert.AreEqual(0, count);
            Assert.HasCount(0, logs);
        }

        [TestMethod]
        public void Recovery_TracksUploadTypesIndependentlyAndReportsNewFailureSeries()
        {
            var vectors = 0;
            var events = 0;
            var logs = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                ServerUploader.TrackUploadRecovery(false, ref vectors, "Vector", logs.Add);
                ServerUploader.TrackUploadRecovery(false, ref events, "Event", logs.Add);
            }

            ServerUploader.TrackUploadRecovery(true, ref vectors, "Vector", logs.Add);
            Assert.AreEqual(3, events);
            ServerUploader.TrackUploadRecovery(true, ref events, "Event", logs.Add);
            for (var i = 0; i < 3; i++)
                ServerUploader.TrackUploadRecovery(false, ref vectors, "Vector", logs.Add);
            ServerUploader.TrackUploadRecovery(true, ref vectors, "Vector", logs.Add);

            CollectionAssert.AreEqual(new[]
            {
                "Vector uploads working again after 3 consecutive failures.",
                "Event uploads working again after 3 consecutive failures.",
                "Vector uploads working again after 3 consecutive failures."
            }, logs);
        }
    }
}
