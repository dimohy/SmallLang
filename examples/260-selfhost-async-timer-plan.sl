import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.semantic.calls as calls

main {
    [
        """
        namespace sys.time

        public struct Duration {
            millis: Long
        }

        public milliseconds value: Long -> Duration {
            Duration { millis: value }
        }

        public sleep duration: Duration -> async Unit = intrinsic
        """,
        """
        namespace sample.timer

        import sys.time as time

        delayed value: Int -> async Int {
            time.milliseconds(5) => duration
            time.sleep(duration) => timer
            timer -> await
            value
        }

        main { }
        """,
        ~
    ] => sources!

    sources! -> calls.resolveModules => resolved!
    sources! -> typedIr.suspensions => points!
    0 => importedCalls!
    resolved! -> each call {
        (call.sourceModule == 1 and call.origin == 1 and call.status == 0) -> if {
            importedCalls! + 1 => importedCalls!
        }
    }
    "imports=$(importedCalls!),suspensions=$(points! -> len),state=$(points![0].state),kind=$(points![0].kind)" -> println
}
