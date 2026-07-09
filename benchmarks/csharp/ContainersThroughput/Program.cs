using System.Diagnostics;

const long ArrayN = 50_000_000;
const int ArrayScanRepeats = 20;
const long DictN = 8_000_000;

Warmup();
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

var stopwatch = Stopwatch.StartNew();
var arrayBuildAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
var values = new List<long>();
for (long i = 1; i <= ArrayN; i++)
{
    values.Add(i);
}

stopwatch.Stop();
var arrayBuildAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - arrayBuildAllocatedBefore;
var arrayBuildMillis = stopwatch.ElapsedMilliseconds;

stopwatch.Restart();
var arrayScanAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
long arrayChecksum = 0;
for (var repeat = 1; repeat <= ArrayScanRepeats; repeat++)
{
    long scanChecksum = 0;
    for (var i = 0; i < values.Count; i++)
    {
        scanChecksum += values[i];
    }

    arrayChecksum += scanChecksum;
}

stopwatch.Stop();
var arrayScanAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - arrayScanAllocatedBefore;
var arrayScanMillis = stopwatch.ElapsedMilliseconds;

stopwatch.Restart();
var dictBuildAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
var scores = new Dictionary<long, long>
{
    [0] = 0
};
for (long i = 1; i <= DictN; i++)
{
    scores[i] = i * 3;
}

stopwatch.Stop();
var dictBuildAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - dictBuildAllocatedBefore;
var dictBuildMillis = stopwatch.ElapsedMilliseconds;

stopwatch.Restart();
var dictLookupAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
long dictChecksum = 0;
for (long i = 1; i <= DictN; i++)
{
    dictChecksum += scores[i];
}

stopwatch.Stop();
var dictLookupAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - dictLookupAllocatedBefore;
var dictLookupMillis = stopwatch.ElapsedMilliseconds;

var arrayLength = values.Count;
var arrayCapacity = values.Capacity;
var arrayBackingBytes = (long)arrayCapacity * 8;
var dictLength = scores.Count;
var dictCapacity = scores.EnsureCapacity(0);
var arrayScanOperations = ArrayN * ArrayScanRepeats;

Console.WriteLine("benchmark = containers-throughput");
Console.WriteLine("language = csharp");
Console.WriteLine($"arrayN = {ArrayN}");
Console.WriteLine($"arrayScanRepeats = {ArrayScanRepeats}");
Console.WriteLine($"dictN = {DictN}");
Console.WriteLine($"arrayLength = {arrayLength}");
Console.WriteLine($"arrayCapacity = {arrayCapacity}");
Console.WriteLine($"arrayBackingBytes = {arrayBackingBytes}");
Console.WriteLine($"arrayChecksum = {arrayChecksum}");
Console.WriteLine($"arrayBuildMillis = {arrayBuildMillis}");
Console.WriteLine($"arrayBuildOpsPerSecond = {OpsPerSecond(ArrayN, arrayBuildMillis)}");
Console.WriteLine($"arrayBuildAllocatedBytes = {arrayBuildAllocatedBytes}");
Console.WriteLine($"arrayScanMillis = {arrayScanMillis}");
Console.WriteLine($"arrayScanOpsPerSecond = {OpsPerSecond(arrayScanOperations, arrayScanMillis)}");
Console.WriteLine($"arrayScanAllocatedBytes = {arrayScanAllocatedBytes}");
Console.WriteLine($"dictLength = {dictLength}");
Console.WriteLine($"dictCapacity = {dictCapacity}");
Console.WriteLine($"dictChecksum = {dictChecksum}");
Console.WriteLine($"dictBuildMillis = {dictBuildMillis}");
Console.WriteLine($"dictBuildOpsPerSecond = {OpsPerSecond(DictN, dictBuildMillis)}");
Console.WriteLine($"dictBuildAllocatedBytes = {dictBuildAllocatedBytes}");
Console.WriteLine($"dictLookupMillis = {dictLookupMillis}");
Console.WriteLine($"dictLookupOpsPerSecond = {OpsPerSecond(DictN, dictLookupMillis)}");
Console.WriteLine($"dictLookupAllocatedBytes = {dictLookupAllocatedBytes}");

static long OpsPerSecond(long operations, long millis)
{
    return millis > 0 ? operations * 1000 / millis : 0;
}

static void Warmup()
{
    var values = new List<long>();
    for (long i = 1; i <= 1024; i++)
    {
        values.Add(i);
    }

    long checksum = 0;
    for (var i = 0; i < values.Count; i++)
    {
        checksum += values[i];
    }

    var scores = new Dictionary<long, long>
    {
        [0] = 0
    };
    for (long i = 1; i <= 1024; i++)
    {
        scores[i] = i * 3;
    }

    for (long i = 1; i <= 1024; i++)
    {
        checksum += scores[i];
    }

    GC.KeepAlive(checksum);
}
