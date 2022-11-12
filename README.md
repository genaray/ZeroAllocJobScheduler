# ZeroAllocJobScheduler
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
var secondJob = new HeavyCalculation();

// Scheduling jobs, not being executed instantly. They wait for a flush
var firstHandle = firsJob.Schedule();   
var secondHandle = secondJob.Schedule();

// Flushes all scheduled jobs to the worker threads
scheduler.Flush();                       

// Blocks till job/handle completed
firstHandle.Complete();                 
secondHandle.Complete();

// Pool internal handles
firstHandle.Return();                 
secondHandle.Return();

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
