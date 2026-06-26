using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MusicTag.States;

internal sealed class LimitedConcurrencyTaskScheduler : TaskScheduler
{
	[ThreadStatic]
	private static bool isProcessingQueuedTasks;

	private readonly LinkedList<Task> queuedTasks = new LinkedList<Task>();

	private readonly int maximumConcurrencyLevel;

	private int runningWorkerCount;

	public sealed override int MaximumConcurrencyLevel => maximumConcurrencyLevel;

	public LimitedConcurrencyTaskScheduler(int maximumConcurrencyLevel)
	{
		if (maximumConcurrencyLevel < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(maximumConcurrencyLevel));
		}
		this.maximumConcurrencyLevel = maximumConcurrencyLevel;
	}

	protected sealed override void QueueTask(Task task)
	{
		lock (queuedTasks)
		{
			queuedTasks.AddLast(task);
			if (runningWorkerCount < maximumConcurrencyLevel)
			{
				runningWorkerCount++;
				QueueWorker();
			}
		}
	}

	protected sealed override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
	{
		if (!isProcessingQueuedTasks)
		{
			return false;
		}

		if (taskWasPreviouslyQueued && !TryDequeue(task))
		{
			return false;
		}

		return TryExecuteTask(task);
	}

	protected sealed override bool TryDequeue(Task task)
	{
		lock (queuedTasks)
		{
			return queuedTasks.Remove(task);
		}
	}

		protected sealed override IEnumerable<Task> GetScheduledTasks()
		{
			bool lockTaken = false;
			try
			{
				Monitor.TryEnter(queuedTasks, ref lockTaken);
				if (!lockTaken)
				{
					throw new NotSupportedException();
				}

				Task[] scheduledTasks = new Task[queuedTasks.Count];
				queuedTasks.CopyTo(scheduledTasks, 0);
				return scheduledTasks;
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(queuedTasks);
				}
			}
		}

	private void QueueWorker()
	{
		ThreadPool.UnsafeQueueUserWorkItem(ProcessQueuedTasks, null);
	}

	private void ProcessQueuedTasks(object state)
	{
		isProcessingQueuedTasks = true;
		try
		{
			while (true)
			{
				Task task;
				lock (queuedTasks)
				{
					if (queuedTasks.Count == 0)
					{
						runningWorkerCount--;
						break;
					}

					task = queuedTasks.First.Value;
					queuedTasks.RemoveFirst();
				}

				TryExecuteTask(task);
			}
		}
		finally
		{
			isProcessingQueuedTasks = false;
		}
	}
}
