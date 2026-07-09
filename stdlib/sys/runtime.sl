namespace sys.runtime

print value: Text -> Unit = intrinsic

println value: Text -> Unit = intrinsic

readInt prompt: Text -> Int = intrinsic

seedRandom value: Int -> Unit = intrinsic

randomBelow maxExclusive: Int -> Int = intrinsic

openIntWriter path: Text -> Unit = intrinsic

writeInt value: Int -> Unit = intrinsic

closeIntWriter: -> Unit = intrinsic

openIntReader path: Text -> Unit = intrinsic

closestInt target: Int -> Int = intrinsic

closeIntReader: -> Unit = intrinsic

nowMillis: -> Int = intrinsic
