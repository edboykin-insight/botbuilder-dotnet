﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Bot.Schema;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Bot.Builder.Azure.Tests
{
    [TestClass]
    public class AzureBlobStorageTests
    {
        private const string ConnectionString = @"AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;DefaultEndpointsProtocol=http;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";
        private const string ContainerName = "test";

        // These tests require Azure Storage Emulator v5.7
        [TestInitialize]
        public void TestInit()
        {
            StorageEmulatorHelper.StartStorageEmulator();
        }

        // These tests require Azure Storage Emulator v5.7
        [TestCleanup]
        public void TestCleanUp()
        {
            StorageEmulatorHelper.StopStorageEmulator();
        }

        [TestMethod]
        public void BlobParamTest()
        {
            Assert.ThrowsException<FormatException>(() => new AzureBlobStorage("123", ContainerName));

            Assert.ThrowsException<ArgumentNullException>(() =>
                new AzureBlobStorage((CloudStorageAccount)null, ContainerName));

            Assert.ThrowsException<ArgumentNullException>(() =>
                new AzureBlobStorage((string)null, ContainerName));

            Assert.ThrowsException<ArgumentNullException>(() =>
                new AzureBlobStorage((CloudStorageAccount)null, null));

            Assert.ThrowsException<ArgumentNullException>(() => new AzureBlobStorage((string)null, null));
        }

        [TestMethod]
        public async Task TestBlobStorageWriteRead()
        {
            // Arrange
            var storageAccount = CloudStorageAccount.Parse(ConnectionString);
            var storage = new AzureBlobStorage(storageAccount, ContainerName);

            var changes = new Dictionary<string, object>
            {
                { "x", "hello" },
                { "y", "world" },
            };

            // Act
            await storage.WriteAsync(changes);
            var result = await storage.ReadAsync(new[] { "x", "y" });

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("hello", result["x"]);
            Assert.AreEqual("world", result["y"]);
        }

        [TestMethod]
        public async Task TestBlobStorageWriteDeleteRead()
        {
            // Arrange
            var storageAccount = CloudStorageAccount.Parse(ConnectionString);
            var storage = new AzureBlobStorage(storageAccount, ContainerName);

            var changes = new Dictionary<string, object>
            {
                { "x", "hello" },
                { "y", "world" },
            };

            // Act
            await storage.WriteAsync(changes);
            await storage.DeleteAsync(new[] { "x" });
            var result = await storage.ReadAsync(new[] { "x", "y" });

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("world", result["y"]);
        }

        [TestMethod]
        public async Task TestBlobStorageChanges()
        {
            // Arrange
            var storageAccount = CloudStorageAccount.Parse(ConnectionString);
            var storage = new AzureBlobStorage(storageAccount, ContainerName);

            // Act
            await storage.WriteAsync(new Dictionary<string, object> { { "a", "1.0" }, { "b", "2.0" } });
            await storage.WriteAsync(new Dictionary<string, object> { { "c", "3.0" } });
            await storage.DeleteAsync(new[] { "b" });
            await storage.WriteAsync(new Dictionary<string, object> { { "a", "1.1" } });
            var result = await storage.ReadAsync(new[] { "a", "b", "c", "d", "e" });

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("1.1", result["a"]);
            Assert.AreEqual("3.0", result["c"]);
        }

        [TestMethod]
        public async Task TestConversationStateBlobStorage()
        {
            // Arrange
            var storageAccount = CloudStorageAccount.Parse(ConnectionString);
            var storage = new AzureBlobStorage(storageAccount, ContainerName);
            var conversationState = new ConversationState(storage);
            var propAccessor = conversationState.CreateProperty<Prop>("prop");
            var adapter = new TestStorageAdapter();
            var activity = new Activity
            {
                ChannelId = "123",
                Conversation = new ConversationAccount { Id = "abc" },
            };

            // Act
            var turnContext1 = new TurnContext(adapter, activity);
            var propValue1 = await propAccessor.GetAsync(turnContext1, () => new Prop());
            propValue1.X = "hello";
            propValue1.Y = "world";
            await conversationState.SaveChangesAsync(turnContext1, force: true);

            var turnContext2 = new TurnContext(adapter, activity);
            var propValue2 = await propAccessor.GetAsync(turnContext2);

            // Assert
            Assert.AreEqual("hello", propValue2.X);
            Assert.AreEqual("world", propValue2.Y);
        }

        private class TestStorageAdapter : BotAdapter
        {
            public override Task DeleteActivityAsync(ITurnContext turnContext, ConversationReference reference, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public override Task<ResourceResponse[]> SendActivitiesAsync(ITurnContext turnContext, Activity[] activities, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public override Task<ResourceResponse> UpdateActivityAsync(ITurnContext turnContext, Activity activity, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }

        private class Prop : IStoreItem
        {
            public string X { get; set; }

            public string Y { get; set; }

            string IStoreItem.ETag { get; set; }
        }
    }
}
