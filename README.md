# ZeroAllocJobScheduler
[![Maintenance](https://img.shields.io/badge/Maintained%3F-yes-green.svg?style=for-the-badge)](https://GitHub.com/Naereen/StrapDown.js/graphs/commit-activity)
[![Nuget](https://img.shields.io/nuget/v/Arch?style=for-the-badge)](https://www.nuget.org/packages/ZeroAllocJobScheduler/)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg?style=for-the-badge)](https://opensource.org/licenses/Apache-2.0)
![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white)

A highperformance alloc free c# Jobscheduler.  
Schedules and executes jobs on a set of worker threads with automatic pooling of internal handles. 

# Code sample

```csharp

public class HeavyCalculation : IJob{
  public void Execute(){
    Thread.Sleep(50);  // Simulate heavy work...
    Console.WriteLine("Done");
  }
}

// Automatically chooses threads based on your processor count
var scheduler = new JobScheduler("MyThreads"); 

// You need to pool/create jobs still by yourself
var firsJob = new HeavyCalculation();    

var firstHandle = firsJob.Schedule(false); // Schedules job locally, false = user needs to wait for complete and return to pool
scheduler.Flush();  // Flushes all scheduled jobs to the worker threads                      

firstHandle.Complete(); // Blocks till job was completed            
firstHandle.Return();   // Returns job to pool

// Dispose
scheduler.Dispose();                
```

# Fire and forget sample

```csharp
// Automatically chooses threads based on your processor count
var scheduler = new JobScheduler("MyThreads"); 

// You need to pool/create jobs still by yourself
var firsJob = new HeavyCalculation();    
var firstHandle = firsJob.Schedule(true); // Schedules job locally, true = user cant wait for it or return, its fire & forget

// Dispose
scheduler.Dispose();                
```

# Advanced API

```csharp
IJob.Schedule(IList<IJob> jobs, IList<JobHandle> handles);   // Schedules a bunch of jobs at once, syntax sugar... handles written into passed array

JobHandle.Complete(JobHandle[] handles);                     // Waits for all jobhandles to finish, blocks till they are     
JobHandle.Return(JobHandle[] handles);                       // Returns all handles to the pool

JobHandle.Complete(IList<JobHandle> handles);                // Waits for all jobhandles to finish, blocks till they are     
JobHandle.Return(IList<JobHandle> handles);                  // Returns all handles to the pool
```
