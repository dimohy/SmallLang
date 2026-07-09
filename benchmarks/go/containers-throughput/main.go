package main

import (
	"fmt"
	"runtime"
	"time"
)

const arrayN int64 = 50_000_000
const arrayScanRepeats = 20
const dictN int64 = 8_000_000

func main() {
	warmup()
	runtime.GC()

	arrayBuildAllocatedBefore := totalAllocatedBytes()
	arrayBuildStart := time.Now()
	var values []int64
	for i := int64(1); i <= arrayN; i++ {
		values = append(values, i)
	}
	arrayBuildMillis := elapsedMillis(arrayBuildStart)
	arrayBuildAllocatedBytes := totalAllocatedBytes() - arrayBuildAllocatedBefore

	arrayScanAllocatedBefore := totalAllocatedBytes()
	arrayScanStart := time.Now()
	var arrayChecksum int64
	for repeat := 1; repeat <= arrayScanRepeats; repeat++ {
		var scanChecksum int64
		for i := 0; i < len(values); i++ {
			scanChecksum += values[i]
		}
		arrayChecksum += scanChecksum
	}
	arrayScanMillis := elapsedMillis(arrayScanStart)
	arrayScanAllocatedBytes := totalAllocatedBytes() - arrayScanAllocatedBefore

	dictBuildAllocatedBefore := totalAllocatedBytes()
	dictBuildStart := time.Now()
	scores := map[int64]int64{0: 0}
	for i := int64(1); i <= dictN; i++ {
		scores[i] = i * 3
	}
	dictBuildMillis := elapsedMillis(dictBuildStart)
	dictBuildAllocatedBytes := totalAllocatedBytes() - dictBuildAllocatedBefore

	dictLookupAllocatedBefore := totalAllocatedBytes()
	dictLookupStart := time.Now()
	var dictChecksum int64
	for i := int64(1); i <= dictN; i++ {
		dictChecksum += scores[i]
	}
	dictLookupMillis := elapsedMillis(dictLookupStart)
	dictLookupAllocatedBytes := totalAllocatedBytes() - dictLookupAllocatedBefore

	arrayLength := len(values)
	arrayCapacity := cap(values)
	arrayBackingBytes := int64(arrayCapacity) * 8
	dictLength := len(scores)
	arrayScanOperations := arrayN * arrayScanRepeats

	fmt.Println("benchmark = containers-throughput")
	fmt.Println("language = go")
	fmt.Printf("arrayN = %d\n", arrayN)
	fmt.Printf("arrayScanRepeats = %d\n", arrayScanRepeats)
	fmt.Printf("dictN = %d\n", dictN)
	fmt.Printf("arrayLength = %d\n", arrayLength)
	fmt.Printf("arrayCapacity = %d\n", arrayCapacity)
	fmt.Printf("arrayBackingBytes = %d\n", arrayBackingBytes)
	fmt.Printf("arrayChecksum = %d\n", arrayChecksum)
	fmt.Printf("arrayBuildMillis = %d\n", arrayBuildMillis)
	fmt.Printf("arrayBuildOpsPerSecond = %d\n", opsPerSecond(arrayN, arrayBuildMillis))
	fmt.Printf("arrayBuildAllocatedBytes = %d\n", arrayBuildAllocatedBytes)
	fmt.Printf("arrayScanMillis = %d\n", arrayScanMillis)
	fmt.Printf("arrayScanOpsPerSecond = %d\n", opsPerSecond(arrayScanOperations, arrayScanMillis))
	fmt.Printf("arrayScanAllocatedBytes = %d\n", arrayScanAllocatedBytes)
	fmt.Printf("dictLength = %d\n", dictLength)
	fmt.Println("dictCapacity = 0")
	fmt.Println("dictBackingBytes = 0")
	fmt.Printf("dictChecksum = %d\n", dictChecksum)
	fmt.Printf("dictBuildMillis = %d\n", dictBuildMillis)
	fmt.Printf("dictBuildOpsPerSecond = %d\n", opsPerSecond(dictN, dictBuildMillis))
	fmt.Printf("dictBuildAllocatedBytes = %d\n", dictBuildAllocatedBytes)
	fmt.Printf("dictLookupMillis = %d\n", dictLookupMillis)
	fmt.Printf("dictLookupOpsPerSecond = %d\n", opsPerSecond(dictN, dictLookupMillis))
	fmt.Printf("dictLookupAllocatedBytes = %d\n", dictLookupAllocatedBytes)

	runtime.KeepAlive(values)
	runtime.KeepAlive(scores)
}

func elapsedMillis(start time.Time) int64 {
	return time.Since(start).Milliseconds()
}

func opsPerSecond(operations int64, millis int64) int64 {
	if millis <= 0 {
		return 0
	}
	return operations * 1000 / millis
}

func totalAllocatedBytes() uint64 {
	var stats runtime.MemStats
	runtime.ReadMemStats(&stats)
	return stats.TotalAlloc
}

func warmup() {
	var values []int64
	for i := int64(1); i <= 1024; i++ {
		values = append(values, i)
	}

	var checksum int64
	for i := 0; i < len(values); i++ {
		checksum += values[i]
	}

	scores := map[int64]int64{0: 0}
	for i := int64(1); i <= 1024; i++ {
		scores[i] = i * 3
	}

	for i := int64(1); i <= 1024; i++ {
		checksum += scores[i]
	}

	runtime.KeepAlive(checksum)
}
