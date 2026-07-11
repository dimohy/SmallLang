namespace sys.file

import sys.runtime as rt

openIntWriter path: Text -> Unit {
    path -> rt.openIntWriter
}

writeInt value: Int -> Unit {
    value -> rt.writeInt
}

closeIntWriter: -> Unit {
    rt.closeIntWriter
}

openIntReader path: Text -> Unit {
    path -> rt.openIntReader
}

closestInt target: Int -> Int {
    target -> rt.closestInt
}

closeIntReader: -> Unit {
    rt.closeIntReader
}

# Generic canonical binary writer. Existing Int-specific names remain for the
# sorted Int64 demo format.
openWriter path: Text -> Unit = intrinsic

write<T> value: T -> Unit = intrinsic

closeWriter: -> Unit = intrinsic
