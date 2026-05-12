using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace UnitTests
{
    [TestClass]
    public class BoundedUploadBufferTests
    {
        [TestMethod]
        public void Add_WhenCapacityExceeded_DropsOldestItems()
        {
            // Arrange
            var buffer = new ServerUploader.BoundedUploadBuffer<int>(3);

            // Act
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4);

            // Assert
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, buffer.Snapshot().Items.ToList());
        }

        [TestMethod]
        public void RemoveThrough_WhenUploadSucceeds_RemovesOnlySnapshotItems()
        {
            // Arrange
            var buffer = new ServerUploader.BoundedUploadBuffer<int>(5);
            buffer.Add(1);
            buffer.Add(2);
            var snapshot = buffer.Snapshot();

            // Act
            buffer.Add(3);
            buffer.RemoveThrough(snapshot.LastSequence);

            // Assert
            CollectionAssert.AreEqual(new[] { 3 }, buffer.Snapshot().Items.ToList());
        }

        [TestMethod]
        public void Snapshot_WhenUploadFailsAndNewItemsArrive_ReturnsOldAndNewItems()
        {
            // Arrange
            var buffer = new ServerUploader.BoundedUploadBuffer<int>(5);
            buffer.Add(1);
            buffer.Add(2);

            // Act
            _ = buffer.Snapshot();
            buffer.Add(3);

            // Assert
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, buffer.Snapshot().Items.ToList());
        }

        [TestMethod]
        public void Add_WhenCapacityExceededDuringOutage_KeepsNewestItems()
        {
            // Arrange
            var buffer = new ServerUploader.BoundedUploadBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);

            // Act
            _ = buffer.Snapshot();
            buffer.Add(3);
            buffer.Add(4);
            buffer.Add(5);

            // Assert
            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, buffer.Snapshot().Items.ToList());
        }

        [TestMethod]
        public void Snapshot_DoesNotCloneItems()
        {
            // Arrange
            var buffer = new ServerUploader.BoundedUploadBuffer<object>(3);
            var item = new object();
            buffer.Add(item);

            // Act
            var snapshot = buffer.Snapshot();

            // Assert
            Assert.AreSame(item, snapshot.Items.Single());
        }
    }
}
