busy value: Int -> Int {
    0 => index!
    value => total!
    index! < 1000000 -> while {
        total! + (index! % 17) => total!
        index! + 1 => index!
    }
    total!
}

main {
    1 -> limitParallelWorkers => workerCount
    [1, 2, 3, 4, 5, 6, 7, 8, ~] -> parallel value {
        value -> busy
    } => mapped!
    parallelPeakWorkers => peakCount
    peakCount > workerCount -> if { "true" } else { "false" } => parentHelped
    "workers=$workerCount, parent-helped=$parentHelped, results=$(mapped! -> len)" -> println
}
