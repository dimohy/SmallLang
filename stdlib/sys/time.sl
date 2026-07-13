namespace sys.time

import sys.runtime as rt

public struct Duration {
    millis: Long
}

public milliseconds value: Long -> Duration {
    Duration { millis: value }
}

public seconds value: Long -> Duration {
    Duration { millis: value * Long(1000) }
}

public sleep duration: Duration -> async Unit = intrinsic

nowMillis: -> Long {
    rt.nowMillis
}
