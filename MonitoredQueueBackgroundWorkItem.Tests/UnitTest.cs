using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MonitoredQueueBackgroundWorkItem;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;

namespace Tests
{
    public class UnitTest
    {
        private IBackgroundTaskQueue _backgroundQueue;
        private IServiceCollection _services;
        private QueuedHostedService _hostedService;

        [SetUp]
        public void Setup()
        {
            this.InitContext();
        }

        [Test]
        public void TestMethod1()
        {
            System.Collections.Concurrent.ConcurrentBag<BaseTask> list = new System.Collections.Concurrent.ConcurrentBag<BaseTask>();
            for (int i = 0; i < 120; i++)
            {
                BaseTask baseTask = new SomeTaskType01(i);
                this._backgroundQueue.QueueBackgroundWorkItem(baseTask);
                list.Add(baseTask);
            }

            while (!list.All(bs => bs.IsCompleted == true))
            {
                Console.WriteLine("Wait to all tasks end. - Sleeping");
                Thread.Sleep(1000);
            }

            foreach (var t in list)
            {
                Assert.IsNotNull(t.GetProduct() as string);
            }
        }

        private void InitContext()
        {
            this._services = new ServiceCollection();
            this._services.AddHostedService<QueuedHostedService>();
            this._services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
            this._services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            var serviceProvider = this._services.BuildServiceProvider();

            this._hostedService = serviceProvider.GetService<IHostedService>() as QueuedHostedService;

            this._backgroundQueue = serviceProvider.GetService<IBackgroundTaskQueue>();

            this._hostedService.StartAsync(CancellationToken.None);
        }
    }
}