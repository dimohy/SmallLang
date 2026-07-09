namespace sys.random

import sys.runtime as rt

seed value: Int -> Unit {
    value -> rt.seedRandom
}

below maxExclusive: Int -> Int {
    maxExclusive -> rt.randomBelow
}
