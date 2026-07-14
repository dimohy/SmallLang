import smalllang.compiler.semantic.module_resolve as moduleResolve

main {
    [
        """
        namespace alpha.tools
        """,
        """
        namespace beta.tools
        """,
        """
        namespace app.main
        import alpha.tools
        import beta.tools
        """,
        ~
    ] => sources!
    sources! -> moduleResolve.resolve => resolved!
    0 => duplicateAliases!
    resolved! -> each item {
        item.status == 3 -> if { duplicateAliases! + 1 => duplicateAliases! }
    }
    (resolved! -> len) == 2 and duplicateAliases! == 1 -> if {
        "default import alias collision = valid"
    } else {
        "default import alias collision = invalid"
    } -> println
}
