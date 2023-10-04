# ZeroAllocJobScheduler
[![Maintenance](https://img.shields.io/badge/Maintained%3F-yes-green.svg?style=for-the-badge)](https://GitHub.com/Naereen/StrapDown.js/graphs/commit-activity)
[![Nuget](https://img.shields.io/nuget/v/ZeroAllocJobScheduler?style=for-the-badge)](https://www.nuget.org/packages/ZeroAllocJobScheduler/)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg?style=for-the-badge)](https://opensource.org/licenses/Apache-2.0)
![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white)

A high-performance alloc-free C# job scheduler.  
Schedules and executes jobs on a set of worker threads with automatic pooling of internal handles. 

# Usage

```csharp

public class HeavyCalculation : IJob
{
  public void Execute()
  {
    Thread.Sleep(50);  // Simulate heavy work
    Console.WriteLine("Done");
  }
}

// Automatically chooses threads based on your processor count, and names the process "MyThreads0", "MyThreads1", etc.
// Should last the lifetime of your program!
var scheduler = new JobScheduler("MyThreads");

// You need to pool/create jobs by yourself
var firstJob = new HeavyCalculation();  

var firstHandle = scheduler.Schedule(firstJob); // Schedules job locally: this might allocate if JobScheduler needs more memory to hold all the concurrent tasks.
                                                // But once those jobs are complete, the memory will be reused.

scheduler.Flush();                              // Dispatches all scheduled jobs to the worker threads

firstHandle.Complete();                         // Blocks the thread until the job is complete.

// Dispose at program exit
scheduler.Dispose();                
```

# Dependencies

To set a sequential dependency on a job, simply pass a created `JobHandle` to `JobScheduler.Schedule(job, dependency)`.

```csharp
var handle1 = scheduler.Schedule(job1);
var handle2 = scheduler.Schedule(job2, handle1);    // job2 will only begin execution once job1 is complete!
scheduler.Flush();
```

# Multiple dependencies

Use `Scheduler.CombineDependencies(JobHandle[] handles)` to get a new handle that depends on the handles in parallel. That handle can then be passed into future `Schedule` call as a dependency itself!

```csharp
// You must create the array of handles, and handle caching/storage yourself.
JobHandle[] handles = new JobHandle[2];

handles[0] = Scheduler.Schedule(job1);
handles[1] = Scheduler.Schedule(job2);
JobHandle combinedHandle = Scheduler.CombineDependencies(handles);          // Combines all handles into the array into one

var dependantHandle = Scheduler.Schedule(job3, combinedHandle);             // job3 now depends on job1 and job2.
                                                                            // job1 and job2 can Complete() in parallel, but job3 can only run once both are complete.

dependantHandle.Complete();                                                 // Blocks the main thread until all three tasks are complete.
```


# Bulk complete

Rather than using `CombineDependencies()`, if you just need to block the main thread until a list of handles are complete, you can use this syntax:

```csharp
JobHandle.CompleteAll(JobHandle[] handles);                     // Waits for all JobHandles to finish, and blocks the main thread until they each complete (in any order)
JobHandle.CompleteAll(IList<JobHandle> handles);
```

Or, if you don't want to maintain a list or array, you can just call `handle.Complete()` on all your handles, in any order.