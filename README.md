# ZeroAllocJobScheduler
[![Maintenance](https://img.shields.io/badge/Maintained%3F-yes-green.svg?style=for-the-badge)](https://GitHub.com/Naereen/StrapDown.js/graphs/commit-activity)
[![Nuget](https://img.shields.io/nuget/v/ZeroAllocJobScheduler?style=for-the-badge)](https://www.nuget.org/packages/ZeroAllocJobScheduler/)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg?style=for-the-badge)](https://opensource.org/licenses/Apache-2.0)
![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white)

A high-performance alloc-free C# job scheduler.  
Schedules and executes jobs on a set of worker threads with automatic pooling of internal handles. 

# Code sample

```csharp

public class HeavyCalculation : IJob
{
  public void Execute()
  {
    Thread.Sleep(50);  // Simulate heavy work
    Console.WriteLine("Done");
  }
}

// Automatically chooses threads based on your processor count
// Creates a global singleton instance
var scheduler = new JobScheduler("MyThreads");

// You need to pool/create jobs by yourself
var firstJob = new HeavyCalculation();  

var firstHandle = firstJob.Schedule(false); // Schedules job locally; false = user must manually wait for Complete() and and manually Return() the handle to the pool
scheduler.Flush();                          // Dispatches all scheduled jobs to the worker threads                      

firstHandle.Complete();                     // Blocks the thread until the job is complete
firstHandle.Return();                       // Returns job to pool

// Dispose at program exit
scheduler.Dispose();                
```

# Fire-and-forget sample

```csharp
var scheduler = new JobScheduler("MyThreads"); 

var firstJob = new HeavyCalculation();    
var firstHandle = firstJob.Schedule(true);  // Schedules job locally; true = user can't wait for Complete(), and it will automatically Return() on completion
scheduler.Flush();                          // Dispatches all scheduled jobs to the worker threads   

scheduler.Dispose();                
```

# Syntactic sugar

```csharp
IJob.Schedule(IList<IJob> jobs, IList<JobHandle> handles);   // Schedules a bunch of jobs at once, and adds their handles to the passed-in list, which is cleared

JobHandle.Complete(JobHandle[] handles);                     // Waits for all JobHandles to finish, and blocks the main thread until they complete
JobHandle.Complete(IList<JobHandle> handles);

JobHandle.Return(JobHandle[] handles);                       // Returns all handles to the pool
JobHandle.Return(IList<JobHandle> handles);
```
