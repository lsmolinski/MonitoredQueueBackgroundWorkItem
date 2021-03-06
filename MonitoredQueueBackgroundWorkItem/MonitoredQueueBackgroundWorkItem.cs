﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace MonitoredQueueBackgroundWorkItem
{
    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private ConcurrentQueue<BaseTask> _workItems =
            new ConcurrentQueue<BaseTask>();

        private SemaphoreSlim _signal = new SemaphoreSlim(0);

        public int Count()
        {
            return _workItems.Count;
        }

        public void QueueBackgroundWorkItem(BaseTask workItem)
        {
            if (workItem == null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }

            Console.WriteLine("WorkItem Enqueue. TaskId: {0}", workItem.TaskID);
            _workItems.Enqueue(workItem);
            _signal.Release();
        }

        public async Task<BaseTask> Dequeue()
        {
            await _signal.WaitAsync();
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }
    }

    public class QueuedHostedService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly ILogger _logger;
        private System.Timers.Timer aTimer;
        private readonly object _countLocker = new object();
        private const int _maxDequeueCountPerMin = 80;

        public QueuedHostedService(
            IBackgroundTaskQueue taskQueue,
            ILoggerFactory loggerFactory)
        {
            TaskQueue = taskQueue;
            _logger = loggerFactory.CreateLogger<QueuedHostedService>();
            _counter = 0;
            SetTimer();
        }

        private long _counter;

        private long GetValueCounter()
        {
            long copy = 0;
            lock (_countLocker)
            {
                copy = _counter;
                return copy;
            }
        }

        private void IncreaseCounter(long value)
        {
            lock (_countLocker)
            {
                _counter = _counter + value;
            }
        }

        private void ResetCounter()
        {
            lock (_countLocker)
            {
                _counter = 0;
            }
        }

        private void SetTimer()
        {
            aTimer = new System.Timers.Timer(1000 * 60);
            aTimer.Elapsed += OnTimedEvent;
            aTimer.AutoReset = true;
            aTimer.Enabled = true;
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            this.ResetCounter();
            string msg = string.Format("RESET Queued action counter: {0}. In Queue: {1}. MaxCounter: {2}.", _counter, TaskQueue.Count(), _maxDequeueCountPerMin);
            _logger.LogInformation(msg);
            Console.WriteLine(msg);
        }

        public IBackgroundTaskQueue TaskQueue { get; }

        protected async override Task ExecuteAsync(
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Queued Hosted Service is starting.");

            BaseTask workItem = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (this.GetValueCounter() > _maxDequeueCountPerMin) // allow start 80 action per minute
                {
                    string msg = string.Format("SKIPPED queued action counter: {0}. In Queue: {1}. MaxCounter: {2}.", _counter, TaskQueue.Count(), _maxDequeueCountPerMin);
                    _logger.LogInformation(msg);
                    Console.WriteLine(msg);
                    Thread.Sleep(1000 * 10);
                }
                else
                {
                    var t = await TaskQueue.Dequeue();
                    t.Start();
                    this.IncreaseCounter(1);
                    string msg = string.Format("DEQUEUE Queued Hosted Service  count: {0}. In Queue: {1}.  MaxCounter: {2}.", _counter, TaskQueue.Count(), _maxDequeueCountPerMin);
                    _logger.LogInformation(msg);
                    Console.WriteLine(msg);
                }
            }

            _logger.LogInformation("Queued Hosted Service is stopping.");
        }
    }

    public abstract class BaseTask
    {
        protected Action _action;
        private Task _task;

        private object _taskLocker = new object();
        private object _isStartedInitLocker = new object();


        private long _owner;

        public int TaskID
        {
            get
            {
                int toReturn = 0;
                lock (_taskLocker)
                {
                    toReturn = _task.Id;
                }
                return toReturn;
            }
        }

        public BaseTask()
        {
            Init();
        }

        protected void Init()
        {
            SetAction();
            lock (_isStartedInitLocker)
            {
                this._isStartInicialized = false;
            }
            _task = new Task(_action);
        }

        protected abstract void SetAction();

        private bool _isStartInicialized;

        public bool IsStartInicialized
        {
            // TODO: use lock
            get
            {
                bool toReturn = false;

                lock (_isStartedInitLocker)
                {
                    toReturn = this._isStartInicialized;
                }

                return toReturn;
            }
        }

        public bool IsCompleted
        {
            get
            {
                bool toReturn = false;
                lock (_taskLocker)
                {
                    toReturn = this._task.IsCompleted;
                }
                return toReturn;
            }
        }

        internal protected void Start()
        {
            lock (this._isStartedInitLocker)
            {
                this._isStartInicialized = true;
            }
            lock (_taskLocker)
            {
                this._task.Start();
            }
        }

        public void Wait()
        {
            lock (_taskLocker)
            {
                this._task.Wait();
            }
        }

        public abstract object GetProduct();
    }

    public class SomeTaskType01 : BaseTask
    {
        private long _someProduct;

        private long _inputData;


        public SomeTaskType01(long inputData)
        {
            _inputData = inputData;
        }

        public override object GetProduct()
        {
            return this._someProduct;
        }

        protected override void SetAction()
        {
            this._action = this.BuildAction;
        }

        private void BuildAction()
        {
            var start = DateTime.Now;

            Console.WriteLine("SomeTaskType01 {0} startedAt: {1}.", this.TaskID, start);

            Random rnd = new Random();

            var sleepTime = rnd.Next(500, 1500);

            Thread.Sleep(sleepTime);

            var end = DateTime.Now;

            this._someProduct = string.Format("InputData: {4}. Start: {0}, End: {1}, Rnd: {2}, TaskId {3}", start, end, sleepTime, this.TaskID, this._inputData).Length;

            Console.WriteLine("SomeTaskType01 {0} EndAt: {1}.", this.TaskID, end);
        }
    }

    public interface IBackgroundTaskQueue
    {
        void QueueBackgroundWorkItem(BaseTask workItem);

        Task<BaseTask> Dequeue();

        int Count();
    }
}



