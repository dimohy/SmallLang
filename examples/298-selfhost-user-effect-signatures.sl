import smalllang.compiler.semantic.context as semanticContext
import smalllang.compiler.semantic.effects as effects
import smalllang.compiler.semantic.user_effects as userEffects

main {
    [
        """
        namespace app

        import fx

        parse text: Text -> Int uses fx.Failure {
            text -> fail
        }

        select max: Int -> Int uses fx.Choice {
            max -> choose
        }

        unknown value: Int -> Int uses fx.Missing {
            value
        }

        hidden value: Int -> Int uses fx.Hidden {
            value
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

        effect Hidden {
            stop value: Int -> Int
        }

        public effect Broken {
            repeat value: Int -> Int
            repeat text: Text -> Int
        }

        unhandled message: Text -> Int {
            message -> Failure.fail
        }
        """,
        ~
    ] => sources!
    sources! -> semanticContext.prepare => prepared!
    prepared! -> effects.analyzeContext => builtin!
    prepared! -> userEffects.analyzeContext => result!

    "builtin diagnostics = $(builtin!.diagnostics -> len)" -> println
    "user effects = $(result!.signatures -> len), operations = $(result!.operations -> len)" -> println
    result!.operations -> each operation {
        "operation = $(operation.sourceModule),$(operation.effectSymbol),$(operation.operationSymbol),$(operation.inputTypeNode),$(operation.returnTypeNode)" -> println
    }
    "requirements = $(result!.requirements -> len), calls = $(result!.calls -> len)" -> println
    result!.requirements -> each requirement {
        "requirement = $(requirement.sourceModule),$(requirement.functionSymbol),$(requirement.effectSourceModule),$(requirement.effectSymbol),$(requirement.status)" -> println
    }
    result!.calls -> each call {
        "user call = $(call.sourceModule),$(call.functionSymbol),$(call.effectSourceModule),$(call.effectSymbol),$(call.operationSymbol),$(call.status)" -> println
    }
    "user diagnostics = $(result!.diagnostics -> len)" -> println
    result!.diagnostics -> each diagnostic {
        "user diagnostic = $(diagnostic.code),$(diagnostic.sourceModule),$(diagnostic.functionSymbol),$(diagnostic.effectSourceModule),$(diagnostic.effectSymbol),$(diagnostic.operationSymbol)" -> println
    }
}
