﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Speckle.Core.Serialisation
{
  internal enum WorkerThreadTaskType
  {
    Deserialize,
  }

  internal class DeserializationWorkerThreads : IDisposable
  {
    public int ThreadCount { get; set; } = Environment.ProcessorCount;

    private List<Thread> Threads = new List<Thread>();
    private BaseObjectDeserializerV2 Serializer;

    private object LockFreeThreads = new object();
    private int FreeThreadCount = 0;
    private BlockingCollection<(WorkerThreadTaskType, object, TaskCompletionSource<object>)> Tasks = new BlockingCollection<(WorkerThreadTaskType, object, TaskCompletionSource<object>)>();

    public DeserializationWorkerThreads(BaseObjectDeserializerV2 serializer)
    {
      Serializer = serializer;
    }

    public void Start()
    {
      for (int i = 0; i < ThreadCount; i++)
      {
        Thread t = new Thread(new ThreadStart(ThreadMain));
        t.IsBackground = true;
        Threads.Add(t);
        t.Start();
      }

    }

    private void ThreadMain()
    {
      while (true)
      {
        lock (LockFreeThreads)
        {
          FreeThreadCount++;
        }
        (WorkerThreadTaskType taskType, object inputValue, TaskCompletionSource<object> tcs) = Tasks.Take();
        if (tcs == null)
        {
          return;
        }

        try
        {
          object converted = null;
          if (taskType == WorkerThreadTaskType.Deserialize)
            converted = Serializer.DeserializeTransportObject(inputValue as String);
          tcs.SetResult(converted);
        }
        catch (Exception e)
        {
          tcs.SetException(e);
        }
      }
    }

    public void Dispose()
    {
      lock (LockFreeThreads)
      {
        FreeThreadCount -= ThreadCount;
      }
      foreach (Thread t in Threads)
        Tasks.Add((WorkerThreadTaskType.Deserialize, null, null));
      foreach (Thread t in Threads)
        t.Join();
      Threads = null;
      Tasks.Dispose();
    }

    internal Task<object> TryStartTask(WorkerThreadTaskType taskType, object inputValue)
    {
      bool canStartTask = false;
      lock (LockFreeThreads)
      {
        if (FreeThreadCount > 0)
        {
          canStartTask = true;
          FreeThreadCount--;
        }
      }

      if (canStartTask)
      {
        TaskCompletionSource<object> tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Tasks.Add((taskType, inputValue, tcs));
        return tcs.Task;
      }
      else
      {
        return null;
      }

    }
  }
}
