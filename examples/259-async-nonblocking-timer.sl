delayed value: Int -> async Int {
    25 -> milliseconds -> sleep -> await
    value
}

quick value: Int -> async Int {
    1 -> milliseconds -> sleep -> await
    value
}

veryDelayed value: Int -> async Int {
    1000 -> milliseconds -> sleep -> await
    value
}

main {
    nowMillis => started
    1 -> delayed => slow
    2 -> quick => fast

    1000 -> milliseconds -> sleep => cancelled
    3 -> veryDelayed => cancelledParent
    0 -> milliseconds -> sleep -> await
    cancelled -> cancel
    cancelledParent -> cancel

    fast -> await => fastValue
    slow -> await => slowValue
    -1 -> seconds -> sleep -> await
    milliseconds(0) -> sleep -> await
    nowMillis - started => elapsed

    elapsed >= Long(20) -> if {
        "fast=$fastValue,slow=$slowValue,cancelled=true,parent=true,elapsed=true" -> println
    } else {
        "fast=$fastValue,slow=$slowValue,cancelled=true,parent=true,elapsed=false" -> println
    }
}
