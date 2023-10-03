namespace JobScheduler.Test.Utils;

[TestFixture]
[NonParallelizable]
internal class AllocationTestFixture : SchedulerTestFixture
{
    protected struct AllocHandle
    {
        public long HeapSize { get; set; } = -1;

        public AllocHandle() { }
    }
    /// <summary>
    /// Starts allocation testing, marking a region in which zero allocations should occur.
    /// </summary>
    protected static AllocHandle StartAllocationTesting() => new AllocHandle()
    {
        HeapSize = GC.GetAllocatedBytesForCurrentThread()
    };

    protected static bool NoAllocationsOccured(AllocHandle handle) => handle.HeapSize == GC.GetAllocatedBytesForCurrentThread();

    protected static bool AllocationsOccured(AllocHandle handle) => !NoAllocationsOccured(handle);
}
