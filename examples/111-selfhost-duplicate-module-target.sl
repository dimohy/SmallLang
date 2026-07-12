import smalllang.compiler.semantic.module_resolve as moduleResolve

main {
    [
        """
        namespace sample.math
        """,
        """
        namespace sample.math
        """,
        """
        import sample.math as math
        """,
        ~
    ] => sources!
    sources! -> moduleResolve.resolve => resolved!
    resolved![0] => item
    "duplicate target = $(item.sourceModule),$(item.targetModule),$(item.status)" -> println
}
