import smalllang.compiler.semantic.context as semanticContext
import smalllang.compiler.semantic.user_effects as userEffects

main {
    [
        """
        namespace app

        import fx

        bare message: Text -> Int uses fx.Failure {
            message -> fail
        }

        explicit message: Text -> Int uses fx.Failure {
            message -> fx.Failure.fail
        }

        unhandled message: Text -> Int {
            message -> fx.Failure.fail
        }

        wrong message: Text -> Int uses fx.Choice {
            message -> choose
        }

        missing: -> Int uses fx.Choice {
            choose
        }

        main {
        }
        """,
        """
        namespace fx

        public effect Failure {
            fail message: Text -> Int
        }

        public effect Choice {
            choose max: Int -> Int
        }
        """,
        ~
    ] => sources!
    sources! -> semanticContext.prepare => prepared!
    prepared! -> userEffects.analyzeContext => result!

    "effects = $(result!.signatures -> len), operations = $(result!.operations -> len)" -> println
    result!.operations -> each operation {
        "operation = $(operation.sourceModule),$(operation.effectSymbol),$(operation.operationSymbol),$(operation.inputTypeId),$(operation.returnTypeId)" -> println
    }
    "requirements = $(result!.requirements -> len), calls = $(result!.calls -> len)" -> println
    result!.calls -> each call {
        "call = $(call.sourceModule),$(call.functionSymbol),$(call.effectSourceModule),$(call.effectSymbol),$(call.operationSymbol),$(call.status),$(call.argumentTypeId),$(call.returnTypeId)" -> println
    }
    "diagnostics = $(result!.diagnostics -> len)" -> println
    result!.diagnostics -> each diagnostic {
        "diagnostic = $(diagnostic.code),$(diagnostic.sourceModule),$(diagnostic.functionSymbol),$(diagnostic.effectSourceModule),$(diagnostic.effectSymbol),$(diagnostic.operationSymbol),$(diagnostic.expectedTypeId),$(diagnostic.actualTypeId)" -> println
    }
}
