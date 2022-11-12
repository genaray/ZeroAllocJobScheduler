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
