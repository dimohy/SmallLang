namespace sys.file

import sys.runtime as rt

# File is an affine native resource. Its private token cannot be constructed or
# copied by user code, and leaving the owner scope closes it deterministically.
public struct File {
    token: UInt64
}

# FileWriter is a separate affine capability, so read-only and write-only
# handles cannot be mixed accidentally. openWrite creates or truncates a file.
# Its syncAsync flow member is a durable-data barrier, not a user-buffer flush.
public struct FileWriter {
    token: UInt64
}

openRead path: Text -> Result<File, Text> = intrinsic

openWrite path: Text -> Result<FileWriter, Text> = intrinsic

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

# Asynchronous scalar reads use the shared native file worker. The operation
# returns an affine Task and therefore composes with the ordinary await and
# cancellation rules without exposing callbacks or executor handles.
readAsync<T>: -> async Result<Option<T>, Text> = intrinsic

closeReader: -> Unit = intrinsic
