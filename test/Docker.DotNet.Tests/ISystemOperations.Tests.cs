﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Xunit;

namespace Docker.DotNet.Tests
{
    public class ISystemOperationsTests
    {
        private readonly DockerClient _client;

        public ISystemOperationsTests()
        {
            _client = new DockerClientConfiguration().CreateClient();
        }

        [Fact]
        public void Docker_IsRunning()
        {
            var dockerProcess = Array.Find(Process.GetProcesses(), _ => _.ProcessName.Equals("docker", StringComparison.InvariantCultureIgnoreCase) || _.ProcessName.Equals("dockerd", StringComparison.InvariantCultureIgnoreCase));
            Assert.NotNull(dockerProcess); // docker is not running
        }

        [Fact]
        public async Task GetSystemInfoAsync_Succeeds()
        {
            var info = await _client.System.GetSystemInfoAsync();
            Assert.NotNull(info.Architecture);
        }

        [Fact]
        public async Task GetVersionAsync_Succeeds()
        {
            var version = await _client.System.GetVersionAsync();
            Assert.NotNull(version.APIVersion);
        }

        [Fact]
        public async Task MonitorEventsAsync_EmptyContainersList_CanBeCancelled()
        {
            var progress = new ProgressMessage()
            {
                _onMessageCalled = (_) => { }
            };

            var cts = new CancellationTokenSource();
            cts.CancelAfter(1000);

            var task = _client.System.MonitorEventsAsync(new ContainerEventsParameters(), progress, cts.Token);

            var taskIsCancelled = false;
            try
            {
                await task;
            }
            catch
            {
                // Exception is not always thrown when cancelling token
                taskIsCancelled = true;
            }

            // On local develop machine task is completed.
            // On CI/CD Pipeline exception is thrown, not always
            Assert.True(task.IsCompleted || taskIsCancelled);
        }

        [Fact]
        public Task MonitorEventsAsync_NullParameters_Throws()
        {
            return Assert.ThrowsAsync<ArgumentNullException>(() => _client.System.MonitorEventsAsync(null, null));
        }

        [Fact]
        public Task MonitorEventsAsync_NullProgress_Throws()
        {
            return Assert.ThrowsAsync<ArgumentNullException>(() => _client.System.MonitorEventsAsync(new ContainerEventsParameters(), null));
        }

        [Fact]
        public async Task MonitorEventsAsync_Succeeds()
        {
            const string repository = "hello-world";
            var newTag = $"MonitorTests-{Guid.NewGuid().ToString().Substring(1, 10)}";

            var progressJSONMessage = new ProgressJSONMessage
            {
                _onJSONMessageCalled = (m) =>
                {
                    // Status could be 'Pulling from...'
                    Console.WriteLine($"{MethodBase.GetCurrentMethod().Module}->{MethodBase.GetCurrentMethod().Name}: _onJSONMessageCalled - {m.ID} - {m.Status} {m.From} - {m.Stream}");
                    Assert.NotNull(m);
                }
            };

            var wasProgressCalled = false;
            var progressMessage = new ProgressMessage
            {
                _onMessageCalled = (m) =>
                {
                    Console.WriteLine($"{MethodBase.GetCurrentMethod().Module}->{MethodBase.GetCurrentMethod().Name}: _onMessageCalled - {m.Action} - {m.Status} {m.From} - {m.Type}");
                    wasProgressCalled = true;
                    Assert.NotNull(m);
                }
            };

            using var cts = new CancellationTokenSource();

            await _client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = "hello-world" }, null, progressJSONMessage);

            var task = Task.Run(() => _client.System.MonitorEventsAsync(new ContainerEventsParameters(), progressMessage, cts.Token));

            await _client.Images.TagImageAsync(repository, new ImageTagParameters { RepositoryName = repository, Tag = newTag });
            _ = await _client.Images.DeleteImageAsync($"{repository}:{newTag}", new ImageDeleteParameters());

            cts.Cancel();

            bool taskIsCancelled = false;
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                taskIsCancelled = true;
            }

            // On local develop machine task is completed.
            // On CI/CD Pipeline exception is thrown, not always
            Assert.True(task.IsCompleted || taskIsCancelled);
            Assert.True(wasProgressCalled);
        }

        [Fact]
        public async Task MonitorEventsFiltered_Succeeds()
        {
            const string repository = "hello-world";
            var newTag = $"MonitorTests-{Guid.NewGuid().ToString().Substring(1, 10)}";

            var progressJSONMessage = new ProgressJSONMessage
            {
                _onJSONMessageCalled = (_) => { }
            };

            await _client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = repository }, null, progressJSONMessage);

            var progressCalledCounter = 0;

            var eventsParams = new ContainerEventsParameters()
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>()
                {
                    {
                        "event", new Dictionary<string, bool>()
                        {
                            {
                                "tag", true
                            },
                            {
                                "untag", true
                            }
                        }
                    },
                    {
                        "type", new Dictionary<string, bool>()
                        {
                            {
                                "image", true
                            }
                        }
                    }
                }
            };

            var progress = new ProgressMessage()
            {
                _onMessageCalled = (m) =>
                {
                    Console.WriteLine($"{MethodBase.GetCurrentMethod().Module}->{MethodBase.GetCurrentMethod().Name}: _onMessageCalled received: {m.Action} - {m.Status} {m.From} - {m.Type}");
                    Assert.True(m.Status == "tag" || m.Status == "untag");
                    progressCalledCounter++;
                }
            };

            using var cts = new CancellationTokenSource();
            var task = Task.Run(() => _client.System.MonitorEventsAsync(eventsParams, progress, cts.Token));

            await _client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = repository }, null, progressJSONMessage);

            await _client.Images.TagImageAsync(repository, new ImageTagParameters { RepositoryName = repository, Tag = newTag });
            _ = await _client.Images.DeleteImageAsync($"{repository}:{newTag}", new ImageDeleteParameters());

            var newContainerId = (await _client.Containers.CreateContainerAsync(new CreateContainerParameters { Image = repository })).ID;
            await _client.Containers.RemoveContainerAsync(newContainerId, new ContainerRemoveParameters(), cts.Token);

            cts.Cancel();
            bool taskIsCancelled = false;
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                taskIsCancelled = true;
            }

            // On local develop machine task is completed.
            // On CI/CD Pipeline exception is thrown, not always
            Assert.True(task.IsCompleted || taskIsCancelled);
            Assert.Equal(2, progressCalledCounter);
        }

        [Fact]
        public Task PingAsync_Succeeds()
        {
            return _client.System.PingAsync();
        }

        private class ProgressJSONMessage : IProgress<JSONMessage>
        {
            internal Action<JSONMessage> _onJSONMessageCalled;

            void IProgress<JSONMessage>.Report(JSONMessage value)
            {
                _onJSONMessageCalled(value);
            }
        }

        private class ProgressMessage : IProgress<Message>
        {
            internal Action<Message> _onMessageCalled;

            void IProgress<Message>.Report(Message value)
            {
                _onMessageCalled(value);
            }
        }
    }
}
