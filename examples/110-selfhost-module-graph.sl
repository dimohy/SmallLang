import smalllang.compiler.lexer
import smalllang.compiler.semantic.module_resolve as moduleResolve
import smalllang.compiler.semantic.modules

main {
    [
        """
        namespace sample.math
        """,
        """
        import sample.math
        """,
        """
        import missing.pkg as missing
        """,
        ~
    ] => sources!
    sources! -> modules.identities => identities!
    sources! -> modules.imports => imports!
    sources! -> moduleResolve.resolve => resolved!
    identities! -> each module {
        "module = $(module.sourceIndex),$(module.pathHash),$(module.importCount)" -> println
    }
    imports! -> each edge {
        sources![edge.sourceModule] => source
        source -> lexer.lex => tokens!
        tokens![edge.aliasToken] => aliasToken
        source -> slice(aliasToken.span.start, aliasToken.span.length) => alias
        "import = $(edge.sourceModule),$(edge.targetHash),$alias" -> println
    }
    resolved! -> each item {
        "resolved = $(item.sourceModule),$(item.targetModule),$(item.status)" -> println
    }
}
