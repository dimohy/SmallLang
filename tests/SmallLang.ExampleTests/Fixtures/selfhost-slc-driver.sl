import smalllang.compiler.llvm.text as llvm
import sys.process as process

collectPaths: -> [Text; ~] uses Process {
    process.arguments => arguments
    arguments -> len => argumentCount
    [Text; ~] => paths!
    UIntSize(2) => index!
    index! < argumentCount -> while {
        arguments[index!] => path
        paths! -> push(path)
        index! + UIntSize(1) => index!
    }
    paths!
}

collectBuildPaths: -> [Text; ~] uses Process {
    process.arguments => arguments
    arguments -> len => argumentCount
    [Text; ~] => paths!
    UIntSize(5) => index!
    index! < argumentCount -> while {
        arguments[index!] => path
        paths! -> push(path)
        index! + UIntSize(1) => index!
    }
    paths!
}

emitLinuxFiles: -> Unit uses Console, File, Process {
    collectPaths => paths!
    paths! -> llvm.emitLinuxFiles
}

emitWasmFiles: -> Unit uses Console, File, Process {
    collectPaths => paths!
    paths! -> llvm.emitWasmFiles
}

emitWindowsFiles: -> Unit uses Console, File, Process {
    collectPaths => paths!
    paths! -> llvm.emitFiles
}

buildWindows: -> Unit uses Console, File, Process {
    process.arguments => arguments
    collectBuildPaths => sourcePaths!
    [arguments[0], "windows", ~] => emitArgv!
    sourcePaths! -> each sourcePath { emitArgv! -> push(sourcePath) }
    process.RunToFileRequest {
        argv: emitArgv!
        output: arguments[2]
    } -> process.runToFile => emitted

    emitted -> when {
        Ok(emitCode) {
            emitCode == 0 -> if {
                [arguments[4], arguments[2], "-O1", "-Wno-override-module", "-o", arguments[3], ~] => clangArgv!
                clangArgv! -> process.run => compiled
                compiled -> when {
                    Ok(clangCode) => "native build = $(clangCode)" -> println
                    Err(error) => "native compiler invocation failed" -> println
                }
            } else {
                "emit failed" -> println
            }
        }
        Err(error) => "compiler emission failed" -> println
    }
}

main {
    process.arguments => arguments
    arguments[1] -> len => targetLength
    targetLength == UIntSize(13) -> if {
        buildWindows
    } else {
    targetLength == UIntSize(5) -> if {
        emitLinuxFiles
    } else {
        targetLength == UIntSize(4) -> if {
            emitWasmFiles
        } else {
            emitWindowsFiles
        }
    }
    }
}
