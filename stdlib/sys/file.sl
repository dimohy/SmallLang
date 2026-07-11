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

# Generic canonical binary reader. Ok(None) is clean EOF; partial scalars,
# invalid Bool/CodePoint encodings, and operating-system failures return
# "truncated", "invalid", and "io" errors.
openReader path: Text -> Unit = intrinsic

read<T>: -> Result<Option<T>, Text> = intrinsic

closeReader: -> Unit = intrinsic
