namespace sys.io

import sys.runtime as rt

print value: Text -> Unit {
    value -> rt.print
}

println value: Text -> Unit {
    value -> rt.println
}

readInt prompt: Text -> Int {
    prompt -> rt.readInt
}
