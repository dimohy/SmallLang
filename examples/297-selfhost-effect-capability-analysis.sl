import smalllang.compiler.semantic.effects as effects
import smalllang.compiler.semantic.context as semanticContext

main {
    [
        """
        namespace app

        import lib

        relay text: Text -> Unit {
            text -> lib.announce
        }

        broken value: Int -> Int uses Network, File, File {
            value
        }

        outer text: Text -> Unit {
            show value: Text -> Unit uses Console {
                value -> println
            }
            text -> show
        }

        clocked: -> Long {
            nowMillis
        }

        draw max: Int -> Int {
            max -> randomBelow
        }

        store value: Int -> Unit {
            value -> writeInt
        }

        ask prompt: Text -> Int {
            prompt -> readInt
        }

        inspect value: Int -> Int {
            value -> lib.inspect
        }

        mappedLength: -> UIntSize {
            map read "data.bin" at 0 for 16 => data
            data -> len
        }

        sync data: MutableMappedBytes -> Unit {
            data -> flush
        }

        mappedSafe: -> UIntSize uses File {
            map write "safe.bin" size 16 => data!
            data! -> flush
            data! -> len
        }

        main {
        }
        """,
        """
        namespace lib

        public announce text: Text -> Unit uses Console {
            text -> println
        }

        public inspect value: Int -> Int uses Process, Environment {
            value
        }
        """,
        ~
    ] => sources!
    sources! -> semanticContext.prepare => prepared!
    prepared! -> effects.analyzeContext => result!
    "effect functions = $(result!.functions -> len)" -> println
    result!.functions -> each fact {
        "effect = $(fact.sourceModule),$(fact.functionSymbol),$(fact.mask)" -> println
    }
    "effect diagnostics = $(result!.diagnostics -> len)" -> println
    result!.diagnostics -> each diagnostic {
        "effect diagnostic = $(diagnostic.code),$(diagnostic.sourceModule),$(diagnostic.functionSymbol),$(diagnostic.targetSourceModule),$(diagnostic.targetFunctionSymbol),$(diagnostic.effectMask)" -> println
    }
}
