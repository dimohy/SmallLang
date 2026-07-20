using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.Semantics;

internal sealed partial class SemanticCompiler
{
    private sealed record BorrowOriginState(
        IReadOnlyDictionary<string, IReadOnlySet<string>> Origins,
        IReadOnlySet<string> ReadonlyReferenceBindings);

    [Flags]
    private enum BorrowControlExit
    {
        None = 0,
        Fallthrough = 1,
        Break = 2,
        Continue = 4,
        Return = 8
    }

    private void DiscoverReadonlyReferenceReturnOrigins(
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var candidates = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        foreach (var function in functions.Values)
        {
            CollectFunctionTree(function, candidates);
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var function in candidates)
            {
                if (_readonlyReferenceReturnOrigins.ContainsKey(function)
                    || !TypeContainsReadonlyReference(function.ReturnType)
                    || !ReadonlyReferenceOriginParameterNames(function).Any())
                {
                    continue;
                }

                if (TryInferFunctionReadonlyReferenceOrigins(function, functions, out var origins))
                {
                    _readonlyReferenceReturnOrigins.Add(function, origins);
                    changed = true;
                }
            }
        }
    }

    private IEnumerable<string> ReadonlyReferenceOriginParameterNames(BoundFunction function)
    {
        if (function.InputType is { } inputType && TypeContainsReadonlyReference(inputType))
        {
            yield return function.InputName ?? "it";
        }
        foreach (var parameter in function.AdditionalParameters ?? [])
        {
            if (TypeContainsReadonlyReference(parameter.Type))
            {
                yield return parameter.Name;
            }
        }
    }

    private bool TryInferFunctionReadonlyReferenceOrigins(
        BoundFunction function,
        IReadOnlyDictionary<string, BoundFunction> functions,
        out IReadOnlySet<string> origins)
    {
        var locals = ReadonlyReferenceOriginParameterNames(function).ToDictionary(
            static name => name,
            static name => (IReadOnlySet<string>)new HashSet<string>([name], StringComparer.Ordinal),
            StringComparer.Ordinal);
        var union = new HashSet<string>(StringComparer.Ordinal);
        CollectReadonlyReferenceReturnOrigins(function.BlockBody, functions, locals, union);
        if (function.Body is not null
            && TryInferReadonlyReferenceOrigins(function.Body, functions, locals, out var bodyOrigins))
        {
            union.UnionWith(bodyOrigins);
        }
        origins = union;
        return union.Count > 0;
    }

    private void CollectReadonlyReferenceReturnOrigins(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, IReadOnlySet<string>> locals,
        HashSet<string> union)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BindingStatement binding:
                    CollectNestedReadonlyReferenceReturnOrigins(binding.Value, functions, locals, union);
                    if (TryInferReadonlyReferenceOrigins(binding.Value, functions, locals, out var bindingOrigins))
                    {
                        locals[binding.Name] = bindingOrigins;
                    }
                    break;
                case ReturnStatement { Value: { } value }:
                    if (TryInferReadonlyReferenceOrigins(value, functions, locals, out var returnOrigins))
                    {
                        union.UnionWith(returnOrigins);
                    }
                    CollectNestedReadonlyReferenceReturnOrigins(value, functions, locals, union);
                    break;
                case ExpressionStatement expression:
                    CollectNestedReadonlyReferenceReturnOrigins(expression.Expression, functions, locals, union);
                    break;
                case BlockFunctionCallStatement block:
                    CollectNestedReadonlyReferenceReturnOrigins(block.Source, functions, locals, union);
                    var blockLocals = new Dictionary<string, IReadOnlySet<string>>(locals, StringComparer.Ordinal);
                    CollectReadonlyReferenceReturnOrigins(block.Body, functions, blockLocals, union);
                    break;
            }
        }
    }

    private void CollectNestedReadonlyReferenceReturnOrigins(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> locals,
        HashSet<string> union)
    {
        switch (expression)
        {
            case IfExpression conditional:
                CollectBlockReadonlyReferenceReturnOrigins(conditional.Then, functions, locals, union);
                if (conditional.Else is not null)
                {
                    CollectBlockReadonlyReferenceReturnOrigins(conditional.Else, functions, locals, union);
                }
                break;
            case WhenExpression match:
                foreach (var arm in match.Arms)
                {
                    CollectBlockReadonlyReferenceReturnOrigins(arm.Body, functions, locals, union);
                }
                CollectBlockReadonlyReferenceReturnOrigins(match.Else, functions, locals, union);
                break;
        }
    }

    private void CollectBlockReadonlyReferenceReturnOrigins(
        BlockBody block,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> parentLocals,
        HashSet<string> union)
    {
        var locals = new Dictionary<string, IReadOnlySet<string>>(parentLocals, StringComparer.Ordinal);
        CollectReadonlyReferenceReturnOrigins(block.Statements, functions, locals, union);
        if (block.Value is not null
            && TryInferReadonlyReferenceOrigins(block.Value, functions, locals, out var origins))
        {
            union.UnionWith(origins);
        }
    }

    private bool TryInferReadonlyReferenceOrigins(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> locals,
        out IReadOnlySet<string> origins)
    {
        if (expression is NameExpression name && locals.TryGetValue(name.Name, out origins!))
        {
            return true;
        }
        if (expression is FieldAccessExpression field)
        {
            return TryInferReadonlyReferenceOrigins(field.Source, functions, locals, out origins);
        }
        if (expression is IndexExpression index)
        {
            return TryInferReadonlyReferenceOrigins(index.Source, functions, locals, out origins);
        }
        if (expression is StructLiteralExpression structure)
        {
            return TryUnionInferredReadonlyReferenceOrigins(
                structure.Fields.Select(static field => field.Value),
                functions,
                locals,
                out origins);
        }
        if (expression is ArrayLiteralExpression array)
        {
            return TryUnionInferredReadonlyReferenceOrigins(
                array.Elements,
                functions,
                locals,
                out origins);
        }
        if (expression is DictionaryLiteralExpression dictionary)
        {
            return TryUnionInferredReadonlyReferenceOrigins(
                dictionary.Entries.SelectMany(static entry => new[] { entry.Key, entry.Value }),
                functions,
                locals,
                out origins);
        }
        if (expression is CallExpression enumConstructor
            && TryGetReadonlyReferenceEnumConstructor(
                enumConstructor,
                out _,
                out var enumVariant,
                out var enumPayload)
            && enumVariant.PayloadType is { } enumPayloadType
            && TypeContainsReadonlyReference(enumPayloadType))
        {
            return TryInferReadonlyReferenceOrigins(
                enumPayload,
                functions,
                locals,
                out origins);
        }
        if (expression is CallExpression call
            && TryGetFunction(call.Path, functions, out var called)
            && _readonlyReferenceReturnOrigins.TryGetValue(called, out var returnOrigins))
        {
            return TryMapReadonlyReferenceReturnOrigins(
                called, returnOrigins, call.Arguments, functions, locals, out origins);
        }
        if (expression is FlowExpression flow && flow.Targets.Count > 0)
        {
            var target = flow.Targets[^1];
            if (TryGetFunction(target.Path, functions, out var flowed)
                && _readonlyReferenceReturnOrigins.TryGetValue(flowed, out var flowedReturnOrigins))
            {
                var arguments = new Expression[] { flow.Source }.Concat(target.Arguments).ToArray();
                return TryMapReadonlyReferenceReturnOrigins(
                    flowed, flowedReturnOrigins, arguments, functions, locals, out origins);
            }
        }
        if (expression is IfExpression conditional && conditional.Else is not null)
        {
            var union = new HashSet<string>(StringComparer.Ordinal);
            CollectBlockReadonlyReferenceOrigins(conditional.Then, functions, locals, union);
            CollectBlockReadonlyReferenceOrigins(conditional.Else, functions, locals, union);
            origins = union;
            return union.Count > 0;
        }
        if (expression is WhenExpression match)
        {
            var union = new HashSet<string>(StringComparer.Ordinal);
            foreach (var arm in match.Arms)
            {
                CollectBlockReadonlyReferenceOrigins(arm.Body, functions, locals, union);
            }
            CollectBlockReadonlyReferenceOrigins(match.Else, functions, locals, union);
            origins = union;
            return union.Count > 0;
        }

        origins = EmptyBorrowOrigins();
        return false;
    }

    private void CollectBlockReadonlyReferenceOrigins(
        BlockBody block,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> parentLocals,
        HashSet<string> union)
    {
        var locals = new Dictionary<string, IReadOnlySet<string>>(parentLocals, StringComparer.Ordinal);
        foreach (var statement in block.Statements)
        {
            if (statement is BindingStatement binding
                && TryInferReadonlyReferenceOrigins(binding.Value, functions, locals, out var bindingOrigins))
            {
                locals[binding.Name] = bindingOrigins;
            }
        }
        if (block.Value is not null
            && TryInferReadonlyReferenceOrigins(block.Value, functions, locals, out var origins))
        {
            union.UnionWith(origins);
        }
    }

    private bool TryMapReadonlyReferenceReturnOrigins(
        BoundFunction called,
        IReadOnlySet<string> returnOrigins,
        IReadOnlyList<Expression> arguments,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> locals,
        out IReadOnlySet<string> origins)
    {
        var parameters = FunctionParameters(called);
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var returnOrigin in returnOrigins)
        {
            var ordinal = parameters.FindIndex(parameter =>
                StringComparer.Ordinal.Equals(parameter, returnOrigin));
            if (ordinal < 0 || ordinal >= arguments.Count
                || !TryInferReadonlyReferenceOrigins(arguments[ordinal], functions, locals, out var argumentOrigins))
            {
                origins = EmptyBorrowOrigins();
                return false;
            }
            union.UnionWith(argumentOrigins);
        }
        origins = union;
        return union.Count > 0;
    }

    private bool TryUnionInferredReadonlyReferenceOrigins(
        IEnumerable<Expression> expressions,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> locals,
        out IReadOnlySet<string> origins)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expression in expressions)
        {
            if (TryInferReadonlyReferenceOrigins(expression, functions, locals, out var nested))
            {
                union.UnionWith(nested);
            }
        }
        origins = union;
        return union.Count > 0;
    }

    private void DiscoverBorrowedTextReturnOrigins(
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var candidates = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        foreach (var function in functions.Values)
        {
            CollectFunctionTree(function, candidates);
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var function in candidates)
            {
                if (_borrowedTextReturnOrigins.ContainsKey(function)
                    || !TypeContains(function.ReturnType, BoundType.Text)
                    || !BorrowedTextOriginParameterNames(function).Any())
                {
                    continue;
                }

                if (TryInferFunctionBorrowedTextOrigins(function, functions, out var origins))
                {
                    _borrowedTextReturnOrigins.Add(function, origins);
                    changed = true;
                }
            }
        }
    }

    private static void CollectFunctionTree(
        BoundFunction function,
        HashSet<BoundFunction> functions)
    {
        if (!functions.Add(function))
        {
            return;
        }
        foreach (var local in function.LocalFunctions.Values)
        {
            CollectFunctionTree(local, functions);
        }
    }

    private IEnumerable<string> BorrowedTextOriginParameterNames(BoundFunction function)
    {
        if (function.InputType is { } inputType
            && ((inputType == BoundType.SourceText
                    && function.InputOwnership == BoundFunctionInputOwnership.Default)
                || TypeContains(inputType, BoundType.Text)))
        {
            yield return function.InputName ?? "it";
        }
        foreach (var parameter in function.AdditionalParameters ?? [])
        {
            if ((parameter.Type == BoundType.SourceText
                    && parameter.Ownership == BoundFunctionInputOwnership.Default)
                || TypeContains(parameter.Type, BoundType.Text))
            {
                yield return parameter.Name;
            }
        }
    }

    private bool TryInferFunctionBorrowedTextOrigins(
        BoundFunction function,
        IReadOnlyDictionary<string, BoundFunction> functions,
        out IReadOnlySet<string> origins)
    {
        var locals = BorrowedTextOriginParameterNames(function).ToDictionary(
            static name => name,
            static name => (IReadOnlySet<string>)new HashSet<string>([name], StringComparer.Ordinal),
            StringComparer.Ordinal);
        var union = new HashSet<string>(StringComparer.Ordinal);
        CollectBorrowedTextReturnOrigins(
            function.BlockBody,
            functions,
            locals,
            union);

        if (function.Body is not null
            && TryInferBorrowedTextOrigins(function.Body, functions, locals, out var bodyOrigins))
        {
            union.UnionWith(bodyOrigins);
        }
        origins = union;
        return union.Count > 0;
    }

    private void CollectBorrowedTextReturnOrigins(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, IReadOnlySet<string>> locals,
        HashSet<string> union)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BindingStatement binding:
                    CollectNestedBorrowedTextReturnOrigins(binding.Value, functions, locals, union);
                    if (TryInferBorrowedTextOrigins(
                            binding.Value,
                            functions,
                            locals,
                            out var bindingOrigins))
                    {
                        locals[binding.Name] = bindingOrigins;
                    }
                    break;
                case ReturnStatement { Value: { } value }:
                    if (TryInferBorrowedTextOrigins(value, functions, locals, out var returnOrigins))
                    {
                        union.UnionWith(returnOrigins);
                    }
                    CollectNestedBorrowedTextReturnOrigins(value, functions, locals, union);
                    break;
                case ExpressionStatement expression:
                    CollectNestedBorrowedTextReturnOrigins(
                        expression.Expression,
                        functions,
                        locals,
                        union);
                    break;
                case BlockFunctionCallStatement block:
                    CollectNestedBorrowedTextReturnOrigins(block.Source, functions, locals, union);
                    var blockLocals = new Dictionary<string, IReadOnlySet<string>>(
                        locals,
                        StringComparer.Ordinal);
                    CollectBorrowedTextReturnOrigins(block.Body, functions, blockLocals, union);
                    break;
            }
        }
    }

    private void CollectNestedBorrowedTextReturnOrigins(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> locals,
        HashSet<string> union)
    {
        switch (expression)
        {
            case IfExpression conditional:
                CollectBlockBorrowedTextReturnOrigins(conditional.Then, functions, locals, union);
                if (conditional.Else is not null)
                {
                    CollectBlockBorrowedTextReturnOrigins(conditional.Else, functions, locals, union);
                }
                break;
            case WhenExpression match:
                foreach (var arm in match.Arms)
                {
                    CollectBlockBorrowedTextReturnOrigins(arm.Body, functions, locals, union);
                }
                CollectBlockBorrowedTextReturnOrigins(match.Else, functions, locals, union);
                break;
            case FoldExpression fold:
                CollectBlockBorrowedTextReturnOrigins(fold.Body, functions, locals, union);
                break;
        }
    }

    private bool TryInferBorrowedTextOrigins(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> locals,
        out IReadOnlySet<string> origins)
    {
        if (expression is NameExpression name && locals.TryGetValue(name.Name, out origins!))
        {
            return true;
        }

        if (expression is StructLiteralExpression structure)
        {
            return TryUnionBorrowedTextOrigins(
                structure.Fields.Select(static field => field.Value),
                functions,
                locals,
                out origins);
        }

        if (expression is ArrayLiteralExpression array)
        {
            return TryUnionBorrowedTextOrigins(array.Elements, functions, locals, out origins);
        }

        if (expression is ArrayRepeatExpression repeat)
        {
            return TryInferBorrowedTextOrigins(repeat.Value, functions, locals, out origins);
        }

        if (expression is DictionaryLiteralExpression dictionary)
        {
            return TryUnionBorrowedTextOrigins(
                dictionary.Entries.SelectMany(static entry => new[] { entry.Key, entry.Value }),
                functions,
                locals,
                out origins);
        }

        if (expression is FieldAccessExpression field)
        {
            return TryInferBorrowedTextOrigins(field.Source, functions, locals, out origins);
        }

        if (expression is IndexExpression index)
        {
            return TryInferBorrowedTextOrigins(index.Source, functions, locals, out origins);
        }

        if (expression is TryExpression attempted)
        {
            return TryInferBorrowedTextOrigins(attempted.Value, functions, locals, out origins);
        }

        if (expression is BoxExpression boxed)
        {
            return TryInferBorrowedTextOrigins(boxed.Value, functions, locals, out origins);
        }

        if (expression is CallExpression call
            && TryGetFunction(call.Path, functions, out var called)
            && _borrowedTextReturnOrigins.TryGetValue(called, out var returnOrigins))
        {
            return TryMapBorrowedReturnOrigins(
                called,
                returnOrigins,
                call.Arguments,
                functions,
                locals,
                out origins);
        }

        if (expression is FlowExpression flow)
        {
            Expression current = flow.Source;
            for (var targetIndex = 0; targetIndex < flow.Targets.Count; targetIndex++)
            {
                var target = flow.Targets[targetIndex];
                var path = string.Join('.', target.Path);
                if (path == "slice")
                {
                    if (targetIndex != flow.Targets.Count - 1)
                    {
                        origins = EmptyBorrowOrigins();
                        return false;
                    }
                    if (!TryInferBorrowedTextOrigins(current, functions, locals, out origins))
                    {
                        origins = EmptyBorrowOrigins();
                        return false;
                    }
                    continue;
                }
                if (TryGetFunction(target.Path, functions, out var flowedFunction)
                    && _borrowedTextReturnOrigins.TryGetValue(flowedFunction, out var flowedReturnOrigins))
                {
                    if (targetIndex != flow.Targets.Count - 1)
                    {
                        origins = EmptyBorrowOrigins();
                        return false;
                    }
                    var arguments = new Expression[] { current }.Concat(target.Arguments).ToArray();
                    return TryMapBorrowedReturnOrigins(
                        flowedFunction,
                        flowedReturnOrigins,
                        arguments,
                        functions,
                        locals,
                        out origins);
                }

                origins = EmptyBorrowOrigins();
                return false;
            }
            return TryInferBorrowedTextOrigins(current, functions, locals, out origins);
        }

        if (expression is IfExpression conditional && conditional.Else is not null)
        {
            var union = new HashSet<string>(StringComparer.Ordinal);
            CollectBlockBorrowedTextOrigins(conditional.Then, functions, locals, union);
            CollectBlockBorrowedTextOrigins(conditional.Else, functions, locals, union);
            origins = union;
            return union.Count > 0;
        }

        if (expression is WhenExpression when)
        {
            var union = new HashSet<string>(StringComparer.Ordinal);
            foreach (var arm in when.Arms)
            {
                CollectBlockBorrowedTextOrigins(arm.Body, functions, locals, union);
            }
            CollectBlockBorrowedTextOrigins(when.Else, functions, locals, union);
            origins = union;
            return union.Count > 0;
        }

        origins = EmptyBorrowOrigins();
        return false;
    }

    private bool TryUnionBorrowedTextOrigins(
        IEnumerable<Expression> expressions,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> locals,
        out IReadOnlySet<string> origins)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expression in expressions)
        {
            if (TryInferBorrowedTextOrigins(expression, functions, locals, out var nested))
            {
                union.UnionWith(nested);
            }
        }
        origins = union;
        return union.Count > 0;
    }

    private void CollectBlockBorrowedTextOrigins(
        BlockBody block,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> parentLocals,
        HashSet<string> union)
    {
        var locals = new Dictionary<string, IReadOnlySet<string>>(parentLocals, StringComparer.Ordinal);
        foreach (var statement in block.Statements)
        {
            if (statement is BindingStatement binding
                && TryInferBorrowedTextOrigins(binding.Value, functions, locals, out var bindingOrigins))
            {
                locals[binding.Name] = bindingOrigins;
            }
        }
        if (block.Value is not null
            && TryInferBorrowedTextOrigins(block.Value, functions, locals, out var origins))
        {
            union.UnionWith(origins);
        }
    }

    private void CollectBlockBorrowedTextReturnOrigins(
        BlockBody block,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> parentLocals,
        HashSet<string> union)
    {
        var locals = new Dictionary<string, IReadOnlySet<string>>(parentLocals, StringComparer.Ordinal);
        CollectBorrowedTextReturnOrigins(block.Statements, functions, locals, union);
        if (block.Value is not null)
        {
            CollectNestedBorrowedTextReturnOrigins(block.Value, functions, locals, union);
        }
    }

    private bool TryMapBorrowedReturnOrigins(
        BoundFunction called,
        IReadOnlySet<string> returnOrigins,
        IReadOnlyList<Expression> arguments,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, IReadOnlySet<string>> locals,
        out IReadOnlySet<string> origins)
    {
        var parameters = FunctionParameters(called);
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var returnOrigin in returnOrigins)
        {
            var ordinal = parameters.FindIndex(parameter =>
                StringComparer.Ordinal.Equals(parameter, returnOrigin));
            if (ordinal < 0 || ordinal >= arguments.Count
                || !TryInferBorrowedTextOrigins(arguments[ordinal], functions, locals, out var argumentOrigins))
            {
                origins = EmptyBorrowOrigins();
                return false;
            }
            union.UnionWith(argumentOrigins);
        }
        origins = union;
        return union.Count > 0;
    }

    private static List<string> FunctionParameters(BoundFunction function)
    {
        var parameters = new List<string>();
        if (function.InputType is not null)
        {
            parameters.Add(function.InputName ?? "it");
        }
        parameters.AddRange((function.AdditionalParameters ?? []).Select(static parameter => parameter.Name));
        return parameters;
    }

    private bool TryGetBorrowedTextCallOrigins(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        out IReadOnlySet<string> origins)
    {
        if (expression is NameExpression name
            && _activeBorrowedTextOrigins.TryGetValue(name.Name, out origins!))
        {
            return true;
        }

        if (expression is StructLiteralExpression structure)
        {
            return TryUnionCallSiteBorrowedOrigins(
                structure.Fields.Select(static field => field.Value),
                functions,
                bindings,
                out origins);
        }

        if (expression is ArrayLiteralExpression array)
        {
            return TryUnionCallSiteBorrowedOrigins(array.Elements, functions, bindings, out origins);
        }

        if (expression is ArrayRepeatExpression repeat)
        {
            return TryGetBorrowedTextCallOrigins(repeat.Value, functions, bindings, out origins);
        }

        if (expression is DictionaryLiteralExpression dictionary)
        {
            return TryUnionCallSiteBorrowedOrigins(
                dictionary.Entries.SelectMany(static entry => new[] { entry.Key, entry.Value }),
                functions,
                bindings,
                out origins);
        }

        if (expression is FieldAccessExpression field)
        {
            return TryGetBorrowedTextCallOrigins(field.Source, functions, bindings, out origins);
        }

        if (expression is IndexExpression index)
        {
            return TryGetBorrowedTextCallOrigins(index.Source, functions, bindings, out origins);
        }

        if (expression is TryExpression attempted)
        {
            return TryGetBorrowedTextCallOrigins(attempted.Value, functions, bindings, out origins);
        }

        if (expression is BoxExpression boxed)
        {
            return TryGetBorrowedTextCallOrigins(boxed.Value, functions, bindings, out origins);
        }

        if (expression is CallExpression call
            && TryGetFunction(call.Path, functions, out var called)
            && _borrowedTextReturnOrigins.TryGetValue(called, out var returnOrigins))
        {
            return TryMapCallSiteBorrowedOrigins(
                called, returnOrigins, call.Arguments, functions, bindings, out origins);
        }

        if (expression is FlowExpression flow)
        {
            Expression current = flow.Source;
            for (var targetIndex = 0; targetIndex < flow.Targets.Count; targetIndex++)
            {
                var target = flow.Targets[targetIndex];
                if (target.Path.Count == 1 && target.Path[0] == "slice")
                {
                    if (targetIndex == flow.Targets.Count - 1
                        && TryGetConcreteBorrowOrigins(current, bindings, out origins))
                    {
                        return true;
                    }
                    continue;
                }
                if (TryGetFunction(target.Path, functions, out var flowedFunction)
                    && _borrowedTextReturnOrigins.TryGetValue(flowedFunction, out var flowedReturnOrigins))
                {
                    if (targetIndex != flow.Targets.Count - 1)
                    {
                        break;
                    }
                    var arguments = new Expression[] { current }.Concat(target.Arguments).ToArray();
                    return TryMapCallSiteBorrowedOrigins(
                        flowedFunction, flowedReturnOrigins, arguments, functions, bindings, out origins);
                }
                break;
            }
        }

        origins = EmptyBorrowOrigins();
        return false;
    }

    private bool TryGetReadonlyReferenceCallOrigins(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        out IReadOnlySet<string> origins)
    {
        if (TryGetActiveReadonlyReferenceCarrierOrigins(expression, out origins))
        {
            return true;
        }

        if (expression is StructLiteralExpression structure)
        {
            return TryUnionReadonlyReferenceCallOrigins(
                structure.Fields.Select(static field => field.Value),
                functions,
                bindings,
                out origins);
        }

        if (expression is ArrayLiteralExpression array)
        {
            return TryUnionReadonlyReferenceCallOrigins(
                array.Elements,
                functions,
                bindings,
                out origins);
        }

        if (expression is DictionaryLiteralExpression dictionary)
        {
            return TryUnionReadonlyReferenceCallOrigins(
                dictionary.Entries.SelectMany(static entry => new[] { entry.Key, entry.Value }),
                functions,
                bindings,
                out origins);
        }

        if (expression is CallExpression enumConstructor
            && TryGetReadonlyReferenceEnumConstructor(
                enumConstructor,
                out _,
                out var enumVariant,
                out var enumPayload)
            && enumVariant.PayloadType is { } enumPayloadType
            && TypeContainsReadonlyReference(enumPayloadType))
        {
            return TryGetReadonlyReferenceCallOrigins(
                enumPayload,
                functions,
                bindings,
                out origins);
        }

        if (expression is CallExpression call
            && TryGetFunction(call.Path, functions, out var called)
            && TypeContainsReadonlyReference(called.ReturnType))
        {
            return TryMapReadonlyReferenceCallOrigins(
                called,
                call.Arguments,
                functions,
                bindings,
                out origins);
        }

        if (expression is FlowExpression flow
            && flow.Targets.Count > 0)
        {
            var target = flow.Targets[^1];
            if (TryGetFunction(target.Path, functions, out var flowed)
                && TypeContainsReadonlyReference(flowed.ReturnType))
            {
                var arguments = new Expression[] { flow.Source }
                    .Concat(target.Arguments)
                    .ToArray();
                return TryMapReadonlyReferenceCallOrigins(
                    flowed,
                    arguments,
                    functions,
                    bindings,
                    out origins);
            }
        }

        origins = EmptyBorrowOrigins();
        return false;
    }

    private bool TryGetActiveReadonlyReferenceCarrierOrigins(
        Expression expression,
        out IReadOnlySet<string> origins)
    {
        if (!TryGetBorrowPlace(expression, out var place))
        {
            origins = EmptyBorrowOrigins();
            return false;
        }

        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var carrier in _activeReadonlyReferenceBindings)
        {
            if ((StringComparer.Ordinal.Equals(carrier, place)
                    || carrier.StartsWith(place + ".", StringComparison.Ordinal)
                    || carrier.StartsWith(place + "[", StringComparison.Ordinal))
                && _activeBorrowedTextOrigins.TryGetValue(carrier, out var carrierOrigins))
            {
                union.UnionWith(carrierOrigins);
            }
        }
        origins = union;
        return union.Count > 0;
    }

    private IReadOnlyDictionary<string, IReadOnlySet<string>> GetReadonlyReferenceCarrierOrigins(
        Expression expression,
        BoundType type,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings)
    {
        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        Collect(expression, type, "");
        return result;

        void Collect(Expression value, BoundType valueType, string path)
        {
            if (_types.IsReference(valueType))
            {
                if (TryGetReadonlyReferenceCallOrigins(value, functions, bindings, out var origins))
                {
                    result[path] = origins;
                }
                return;
            }

            if (_types.IsStruct(valueType)
                && value is StructLiteralExpression structure)
            {
                var initializers = structure.Fields.ToDictionary(
                    static field => field.Name,
                    StringComparer.Ordinal);
                foreach (var field in _types.GetStruct(valueType).Fields)
                {
                    if (TypeContainsReadonlyReference(field.Type)
                        && initializers.TryGetValue(field.Name, out var initializer))
                    {
                        Collect(initializer.Value, field.Type, path + "." + field.Name);
                    }
                }
                return;
            }

            if (_types.IsEnum(valueType)
                && value is CallExpression enumConstructor
                && TryGetReadonlyReferenceEnumConstructor(
                    enumConstructor,
                    out var enumType,
                    out var enumVariant,
                    out var enumPayload)
                && enumType == valueType
                && enumVariant.PayloadType is { } enumPayloadType
                && TypeContainsReadonlyReference(enumPayloadType))
            {
                Collect(
                    enumPayload,
                    enumPayloadType,
                    path + "[" + enumVariant.Name + "]");
                return;
            }

            if (value is NameExpression name)
            {
                var prefix = CanonicalBorrowOriginName(name.Name);
                foreach (var carrier in _activeReadonlyReferenceBindings)
                {
                    if (!carrier.StartsWith(prefix + ".", StringComparison.Ordinal)
                        && !carrier.StartsWith(prefix + "[", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (_activeBorrowedTextOrigins.TryGetValue(carrier, out var origins))
                    {
                        result[path + carrier[prefix.Length..]] = origins;
                    }
                }
                if (result.Count > 0)
                {
                    return;
                }
            }

            if (!TryGetReadonlyReferenceCallOrigins(value, functions, bindings, out var aggregateOrigins))
            {
                return;
            }
            foreach (var leaf in ReadonlyReferenceLeafPaths(valueType))
            {
                result[path + leaf] = aggregateOrigins;
            }
        }
    }

    private bool TryGetReadonlyReferenceEnumConstructor(
        CallExpression expression,
        out BoundType enumType,
        out BoundEnumVariant variant,
        out Expression payload)
    {
        enumType = default;
        variant = null!;
        payload = null!;
        if (expression.Path.Count < 2 || expression.Arguments.Count != 1)
        {
            return false;
        }

        var typeName = string.Join('.', expression.Path.Take(expression.Path.Count - 1));
        if (!_types.TryResolve(typeName, out enumType))
        {
            if (!typeName.StartsWith("Option<", StringComparison.Ordinal)
                && !typeName.StartsWith("Result<", StringComparison.Ordinal))
            {
                return false;
            }
            enumType = ParseType(typeName, expression.Line, expression.Column);
        }
        if (!_types.IsEnum(enumType))
        {
            return false;
        }

        variant = _types.GetEnum(enumType).Variants.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Name, expression.Path[^1]))!;
        if (variant?.PayloadType is null)
        {
            return false;
        }
        payload = expression.Arguments[0];
        return true;
    }

    private IReadOnlyList<string> InstallReadonlyReferenceEnumPatternOrigins(
        Expression subject,
        BoundEnumVariant variant,
        string bindingName)
    {
        if (!TryGetBorrowPlace(subject, out var subjectPlace))
        {
            return [];
        }

        var variantPlace = subjectPlace + "[" + variant.Name + "]";
        var installed = new List<string>();
        foreach (var carrier in _activeReadonlyReferenceBindings.ToArray())
        {
            if (!TryGetBorrowPlaceRelativeSuffix(variantPlace, carrier, out var suffix))
            {
                continue;
            }
            if (!_activeBorrowedTextOrigins.TryGetValue(carrier, out var origins))
            {
                continue;
            }

            var projected = bindingName + suffix;
            _activeBorrowedTextOrigins[projected] = origins;
            _activeReadonlyReferenceBindings.Add(projected);
            installed.Add(projected);
        }
        return installed;
    }

    private void RemoveReadonlyReferencePatternOrigins(IEnumerable<string> bindings)
    {
        foreach (var binding in bindings)
        {
            _activeBorrowedTextOrigins.Remove(binding);
            _activeReadonlyReferenceBindings.Remove(binding);
        }
    }

    private IEnumerable<string> ReadonlyReferenceLeafPaths(BoundType type)
    {
        if (_types.IsReference(type))
        {
            yield return "";
            yield break;
        }
        if (_types.IsStruct(type))
        {
            foreach (var field in _types.GetStruct(type).Fields)
            {
                foreach (var nested in ReadonlyReferenceLeafPaths(field.Type))
                {
                    yield return "." + field.Name + nested;
                }
            }
            yield break;
        }
        if (_types.IsEnum(type))
        {
            foreach (var variant in _types.GetEnum(type).Variants)
            {
                if (variant.PayloadType is not { } payload)
                {
                    continue;
                }
                foreach (var nested in ReadonlyReferenceLeafPaths(payload))
                {
                    yield return "[" + variant.Name + "]" + nested;
                }
            }
            yield break;
        }
        if (_types.IsStaticArray(type))
        {
            foreach (var nested in ReadonlyReferenceLeafPaths(_types.GetStaticArray(type).ElementType))
            {
                yield return "[*]" + nested;
            }
            yield break;
        }
        if (_types.IsDynamicArray(type))
        {
            foreach (var nested in ReadonlyReferenceLeafPaths(_types.GetDynamicArray(type).ElementType))
            {
                yield return "[*]" + nested;
            }
        }
    }

    private static bool TryGetBorrowPlace(Expression expression, out string place)
    {
        switch (expression)
        {
            case NameExpression name:
                place = CanonicalBorrowOriginName(name.Name);
                return true;
            case FieldAccessExpression field when TryGetBorrowPlace(field.Source, out var source):
                place = source + "." + field.FieldName;
                return true;
            case IndexExpression index when TryGetBorrowPlace(index.Source, out var source):
                place = source + BorrowOriginIndexProjection(index.Index);
                return true;
            default:
                place = "";
                return false;
        }
    }

    private static bool TryGetBorrowPlaceRelativeSuffix(
        string prefix,
        string candidate,
        out string suffix)
    {
        var prefixParts = SplitBorrowPlace(prefix);
        var candidateParts = SplitBorrowPlace(candidate);
        if (candidateParts.Count < prefixParts.Count
            || !StringComparer.Ordinal.Equals(candidateParts[0], prefixParts[0]))
        {
            suffix = "";
            return false;
        }

        for (var index = 1; index < prefixParts.Count; index++)
        {
            var expected = prefixParts[index];
            var actual = candidateParts[index];
            if (StringComparer.Ordinal.Equals(expected, actual))
            {
                continue;
            }
            if (expected.Length > 1 && actual.Length > 1
                && expected[0] == '[' && actual[0] == '['
                && (expected == "[*]" || actual == "[*]"))
            {
                continue;
            }

            suffix = "";
            return false;
        }

        suffix = string.Concat(candidateParts.Skip(prefixParts.Count));
        return true;
    }

    private bool TryUnionReadonlyReferenceCallOrigins(
        IEnumerable<Expression> expressions,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        out IReadOnlySet<string> origins)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expression in expressions)
        {
            if (TryGetReadonlyReferenceCallOrigins(expression, functions, bindings, out var nested))
            {
                union.UnionWith(nested);
            }
        }
        origins = union;
        return union.Count > 0;
    }

    private bool TypeContainsReadonlyReference(BoundType type) =>
        TypeContainsReadonlyReference(type, new HashSet<BoundType>());

    private bool TypeContainsReadonlyReference(BoundType type, HashSet<BoundType> visiting)
    {
        if (_types.IsReference(type))
        {
            return true;
        }
        if (!visiting.Add(type))
        {
            return false;
        }
        try
        {
            if (_types.IsStruct(type))
            {
                return _types.GetStruct(type).Fields.Any(field =>
                    TypeContainsReadonlyReference(field.Type, visiting));
            }
            if (_types.IsEnum(type))
            {
                return _types.GetEnum(type).Variants.Any(variant =>
                    variant.PayloadType is { } payload
                    && TypeContainsReadonlyReference(payload, visiting));
            }
            if (_types.IsStaticArray(type))
            {
                return TypeContainsReadonlyReference(
                    _types.GetStaticArray(type).ElementType,
                    visiting);
            }
            if (_types.IsDynamicArray(type))
            {
                return TypeContainsReadonlyReference(
                    _types.GetDynamicArray(type).ElementType,
                    visiting);
            }
            if (_types.IsDictionary(type))
            {
                var dictionary = _types.GetDictionary(type);
                return TypeContainsReadonlyReference(dictionary.KeyType, visiting)
                    || TypeContainsReadonlyReference(dictionary.ValueType, visiting);
            }
            if (_types.IsBox(type))
            {
                return TypeContainsReadonlyReference(_types.GetBox(type).ElementType, visiting);
            }
            return false;
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private bool TryMapReadonlyReferenceCallOrigins(
        BoundFunction called,
        IReadOnlyList<Expression> arguments,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        out IReadOnlySet<string> origins)
    {
        if (!_readonlyReferenceReturnOrigins.TryGetValue(called, out var returnOrigins))
        {
            origins = EmptyBorrowOrigins();
            return false;
        }

        var parameters = FunctionParameters(called);
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var returnOrigin in returnOrigins)
        {
            var index = parameters.FindIndex(parameter =>
                StringComparer.Ordinal.Equals(parameter, returnOrigin));
            if (index < 0 || index >= arguments.Count)
            {
                continue;
            }

            if (TryGetReadonlyReferenceCallOrigins(
                    arguments[index],
                    functions,
                    bindings,
                    out var propagated))
            {
                union.UnionWith(propagated);
            }
            else if (TryGetConcreteBorrowOrigins(arguments[index], bindings, out var concrete))
            {
                union.UnionWith(concrete);
            }
        }

        origins = union;
        return union.Count > 0;
    }

    private bool TryUnionCallSiteBorrowedOrigins(
        IEnumerable<Expression> expressions,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        out IReadOnlySet<string> origins)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expression in expressions)
        {
            if (TryGetBorrowedTextCallOrigins(expression, functions, bindings, out var nested))
            {
                union.UnionWith(nested);
            }
        }
        origins = union;
        return union.Count > 0;
    }

    private bool TryMapCallSiteBorrowedOrigins(
        BoundFunction called,
        IReadOnlySet<string> returnOrigins,
        IReadOnlyList<Expression> arguments,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        out IReadOnlySet<string> origins)
    {
        var parameters = FunctionParameters(called);
        var parameterTypes = FunctionParameterTypes(called);
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var returnOrigin in returnOrigins)
        {
            var ordinal = parameters.FindIndex(parameter =>
                StringComparer.Ordinal.Equals(parameter, returnOrigin));
            if (ordinal < 0 || ordinal >= arguments.Count || ordinal >= parameterTypes.Count)
            {
                origins = EmptyBorrowOrigins();
                return false;
            }

            if (parameterTypes[ordinal] == BoundType.SourceText)
            {
                if (!TryGetConcreteBorrowOrigins(arguments[ordinal], bindings, out var sourceOrigins))
                {
                    origins = EmptyBorrowOrigins();
                    return false;
                }
                union.UnionWith(sourceOrigins);
            }
            else if (TryGetBorrowedTextCallOrigins(
                         arguments[ordinal],
                         functions,
                         bindings,
                         out var propagatedOrigins))
            {
                union.UnionWith(propagatedOrigins);
            }
        }
        origins = union;
        return union.Count > 0;
    }

    private static List<BoundType> FunctionParameterTypes(BoundFunction function)
    {
        var parameters = new List<BoundType>();
        if (function.InputType is { } inputType)
        {
            parameters.Add(inputType);
        }
        parameters.AddRange((function.AdditionalParameters ?? []).Select(static parameter => parameter.Type));
        return parameters;
    }

    private bool TryGetConcreteBorrowOrigins(
        Expression expression,
        IReadOnlyDictionary<string, BoundType> bindings,
        out IReadOnlySet<string> origins)
    {
        return TryGetConcreteBorrowOrigins(expression, bindings, out origins, out _);
    }

    private bool TryGetConcreteBorrowOrigins(
        Expression expression,
        IReadOnlyDictionary<string, BoundType> bindings,
        out IReadOnlySet<string> origins,
        out bool isOwnedPlace)
    {
        if (expression is NameExpression name)
        {
            if (_activeBorrowedTextOrigins.TryGetValue(name.Name, out origins!))
            {
                isOwnedPlace = false;
                return true;
            }
            if (bindings.ContainsKey(name.Name))
            {
                origins = new HashSet<string>(
                    [CanonicalBorrowOriginName(name.Name)],
                    StringComparer.Ordinal);
                isOwnedPlace = true;
                return true;
            }
        }

        if (expression is FieldAccessExpression field
            && TryGetConcreteBorrowOrigins(field.Source, bindings, out var fieldOrigins, out isOwnedPlace))
        {
            origins = isOwnedPlace
                ? AppendBorrowProjection(fieldOrigins, $".{field.FieldName}")
                : fieldOrigins;
            return true;
        }

        if (expression is IndexExpression index
            && TryGetConcreteBorrowOrigins(index.Source, bindings, out var indexOrigins, out isOwnedPlace))
        {
            origins = isOwnedPlace
                ? AppendBorrowProjection(indexOrigins, BorrowOriginIndexProjection(index.Index))
                : indexOrigins;
            return true;
        }

        origins = EmptyBorrowOrigins();
        isOwnedPlace = false;
        return false;
    }

    private static IReadOnlySet<string> AppendBorrowProjection(
        IReadOnlySet<string> origins,
        string projection) => origins
        .Select(origin => origin + projection)
        .ToHashSet(StringComparer.Ordinal);

    private void RejectBorrowedTextOriginInvalidation(
        string? first,
        string? second,
        IReadOnlyList<string> consumed,
        IReadOnlyList<string> transferred,
        string? rebound,
        bool isRebind,
        int line,
        int column)
    {
        if (first is not null)
        {
            RejectBorrowedTextOriginMutation(first, line, column);
        }
        if (second is not null)
        {
            RejectBorrowedTextOriginMutation(second, line, column);
        }
        foreach (var name in consumed)
        {
            RejectBorrowedTextOriginMutation(name, line, column);
        }
        foreach (var name in transferred)
        {
            RejectBorrowedTextOriginMutation(name, line, column);
        }
        if (isRebind && rebound is not null)
        {
            RejectBorrowedTextOriginMutation(rebound, line, column);
        }
    }

    private void ExpireBorrowedTextOriginsBeforeStatement(
        IReadOnlyList<Statement> statements,
        int statementIndex,
        Expression? result,
        IReadOnlySet<string>? continuationNames)
    {
        foreach (var binding in _activeBorrowedTextOrigins.Keys.ToArray())
        {
            var isLive = continuationNames?.Contains(binding) == true;
            for (var index = statementIndex; index < statements.Count; index++)
            {
                if (ReferencesTrackedBorrow(statements[index], binding))
                {
                    isLive = true;
                    break;
                }
            }

            if (!isLive
                && result is not null
                && ReferencesTrackedBorrow(result, binding))
            {
                isLive = true;
            }

            if (!isLive)
            {
                _activeBorrowedTextOrigins.Remove(binding);
                _activeReadonlyReferenceBindings.Remove(binding);
            }
        }
    }

    private HashSet<string> BorrowedTextContinuationAfterStatement(
        IReadOnlyList<Statement> statements,
        int statementIndex,
        Expression? result,
        IReadOnlySet<string>? continuationNames)
    {
        var live = continuationNames is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(continuationNames, StringComparer.Ordinal);
        foreach (var binding in _activeBorrowedTextOrigins.Keys)
        {
            for (var index = statementIndex + 1; index < statements.Count; index++)
            {
                if (ReferencesTrackedBorrow(statements[index], binding))
                {
                    live.Add(binding);
                    break;
                }
            }

            if (result is not null && ReferencesTrackedBorrow(result, binding))
            {
                live.Add(binding);
            }
        }
        return live;
    }

    private bool ReferencesTrackedBorrow(Statement statement, string binding) =>
        _activeReadonlyReferenceBindings.Contains(binding)
            ? ReferencesBorrowCarrier(statement, binding)
            : StoragePlacementAnalyzer.ReferencesName(statement, binding);

    private bool ReferencesTrackedBorrow(Expression expression, string binding) =>
        _activeReadonlyReferenceBindings.Contains(binding)
            ? ReferencesBorrowCarrier(expression, binding)
            : StoragePlacementAnalyzer.ReferencesName(expression, binding);

    private static bool ReferencesBorrowCarrier(Statement statement, string carrier) => statement switch
    {
        BindingStatement binding => ReferencesBorrowCarrier(binding.Value, carrier),
        IndexAssignmentStatement assignment =>
            BorrowPlacesConflict(BorrowOriginIndexedPlace(assignment.Name, assignment.Index), carrier)
            || ReferencesBorrowCarrier(assignment.Index, carrier)
            || ReferencesBorrowCarrier(assignment.Value, carrier),
        FieldAssignmentStatement assignment =>
            BorrowPlacesConflict(
                CanonicalBorrowOriginName(assignment.Name) + "." + assignment.FieldName,
                carrier)
            || ReferencesBorrowCarrier(assignment.Value, carrier),
        BlockFunctionCallStatement block => ReferencesBorrowCarrier(block.Source, carrier)
            || ReferencesBorrowCarrier(block.Body, carrier),
        ExpressionStatement expression => ReferencesBorrowCarrier(expression.Expression, carrier),
        ReturnStatement { Value: { } value } => ReferencesBorrowCarrier(value, carrier),
        GuardLoopControlStatement guard => ReferencesBorrowCarrier(guard.Condition, carrier),
        _ => false
    };

    private static bool ReferencesBorrowCarrier(BlockBody body, string carrier) =>
        body.Statements.Any(statement => ReferencesBorrowCarrier(statement, carrier))
        || (body.Value is not null && ReferencesBorrowCarrier(body.Value, carrier));

    private static bool ReferencesBorrowCarrier(
        IReadOnlyList<Statement> statements,
        string carrier) => statements.Any(statement =>
            ReferencesBorrowCarrier(statement, carrier));

    private static bool ReferencesBorrowCarrier(Expression expression, string carrier)
    {
        if (expression is FieldAccessExpression field)
        {
            return (TryGetBorrowPlace(field, out var place) && BorrowPlacesConflict(place, carrier));
        }
        if (expression is IndexExpression index)
        {
            return (TryGetBorrowPlace(index, out var place) && BorrowPlacesConflict(place, carrier))
                || ReferencesBorrowCarrier(index.Index, carrier);
        }
        if (expression is NameExpression name)
        {
            return BorrowPlacesConflict(CanonicalBorrowOriginName(name.Name), carrier);
        }

        return expression switch
        {
            StringExpression text => text.Segments.OfType<InterpolationSegment>()
                .Any(segment => ReferencesBorrowCarrier(segment.Expression, carrier)),
            AddExpression add => ReferencesBorrowCarrier(add.Left, carrier)
                || ReferencesBorrowCarrier(add.Right, carrier),
            SubtractExpression subtract => ReferencesBorrowCarrier(subtract.Left, carrier)
                || ReferencesBorrowCarrier(subtract.Right, carrier),
            MultiplyExpression multiply => ReferencesBorrowCarrier(multiply.Left, carrier)
                || ReferencesBorrowCarrier(multiply.Right, carrier),
            DivideExpression divide => ReferencesBorrowCarrier(divide.Left, carrier)
                || ReferencesBorrowCarrier(divide.Right, carrier),
            ModuloExpression modulo => ReferencesBorrowCarrier(modulo.Left, carrier)
                || ReferencesBorrowCarrier(modulo.Right, carrier),
            NegateExpression negate => ReferencesBorrowCarrier(negate.Value, carrier),
            CompareExpression compare => ReferencesBorrowCarrier(compare.Left, carrier)
                || ReferencesBorrowCarrier(compare.Right, carrier),
            AndExpression and => ReferencesBorrowCarrier(and.Left, carrier)
                || ReferencesBorrowCarrier(and.Right, carrier),
            OrExpression or => ReferencesBorrowCarrier(or.Left, carrier)
                || ReferencesBorrowCarrier(or.Right, carrier),
            NotExpression not => ReferencesBorrowCarrier(not.Value, carrier),
            RangeExpression range => ReferencesBorrowCarrier(range.Start, carrier)
                || ReferencesBorrowCarrier(range.End, carrier),
            FoldExpression fold => ReferencesBorrowCarrier(fold.Source, carrier)
                || ReferencesBorrowCarrier(fold.Initial, carrier)
                || ReferencesBorrowCarrier(fold.Body, carrier),
            IfExpression conditional => ReferencesBorrowCarrier(conditional.Condition, carrier)
                || ReferencesBorrowCarrier(conditional.Then, carrier)
                || (conditional.Else is not null
                    && ReferencesBorrowCarrier(conditional.Else, carrier)),
            WhenExpression selection =>
                (selection.Subject is not null
                    && ReferencesBorrowCarrier(selection.Subject, carrier))
                || selection.Arms.Any(arm =>
                    ReferencesBorrowCarrier(arm.Condition, carrier)
                    || ReferencesBorrowCarrier(arm.Body, carrier))
                || ReferencesBorrowCarrier(selection.Else, carrier),
            EnumMatchExpression match => ReferencesBorrowCarrier(match.Subject, carrier)
                || match.Arms.Any(arm => ReferencesBorrowCarrier(arm.Body, carrier))
                || (match.Else is not null && ReferencesBorrowCarrier(match.Else, carrier)),
            SubjectCompareExpression subject => ReferencesBorrowCarrier(subject.Right, carrier),
            SubjectRangeExpression subject => ReferencesBorrowCarrier(subject.Start, carrier)
                || ReferencesBorrowCarrier(subject.End, carrier),
            FlowExpression flow => ReferencesBorrowCarrier(flow.Source, carrier)
                || flow.Targets.Any(target => target.Arguments.Any(argument =>
                    ReferencesBorrowCarrier(argument, carrier))),
            CallExpression call => call.Arguments.Any(argument =>
                ReferencesBorrowCarrier(argument, carrier)),
            ArrayLiteralExpression array => array.Elements.Any(element =>
                ReferencesBorrowCarrier(element, carrier)),
            ArrayRepeatExpression repeat => ReferencesBorrowCarrier(repeat.Value, carrier),
            StructLiteralExpression structure => structure.Fields.Any(field =>
                ReferencesBorrowCarrier(field.Value, carrier)),
            BoxExpression box => ReferencesBorrowCarrier(box.Value, carrier),
            DictionaryLiteralExpression dictionary => dictionary.Entries.Any(entry =>
                ReferencesBorrowCarrier(entry.Key, carrier)
                || ReferencesBorrowCarrier(entry.Value, carrier)),
            TryExpression attempt => ReferencesBorrowCarrier(attempt.Value, carrier),
            MapExpression map => ReferencesBorrowCarrier(map.Path, carrier)
                || (map.Offset is not null && ReferencesBorrowCarrier(map.Offset, carrier))
                || (map.Length is not null && ReferencesBorrowCarrier(map.Length, carrier))
                || (map.FileSize is not null && ReferencesBorrowCarrier(map.FileSize, carrier)),
            _ => false
        };
    }

    private BorrowOriginState CaptureBorrowedTextOriginState() => new(
        new Dictionary<string, IReadOnlySet<string>>(
            _activeBorrowedTextOrigins,
            StringComparer.Ordinal),
        new HashSet<string>(
            _activeReadonlyReferenceBindings,
            StringComparer.Ordinal));

    private void RestoreBorrowedTextOriginState(BorrowOriginState state)
    {
        _activeBorrowedTextOrigins.Clear();
        foreach (var pair in state.Origins)
        {
            _activeBorrowedTextOrigins.Add(pair.Key, pair.Value);
        }
        _activeReadonlyReferenceBindings.Clear();
        _activeReadonlyReferenceBindings.UnionWith(state.ReadonlyReferenceBindings);
    }

    private static BorrowOriginState MergeBorrowedTextOriginStates(
        IEnumerable<BorrowOriginState> states)
    {
        var merged = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var readonlyReferenceBindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            foreach (var pair in state.Origins)
            {
                if (!merged.TryGetValue(pair.Key, out var origins))
                {
                    origins = new HashSet<string>(StringComparer.Ordinal);
                    merged.Add(pair.Key, origins);
                }
                origins.UnionWith(pair.Value);
            }
            readonlyReferenceBindings.UnionWith(state.ReadonlyReferenceBindings);
        }
        return new BorrowOriginState(
            merged.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlySet<string>)pair.Value,
                StringComparer.Ordinal),
            readonlyReferenceBindings);
    }

    private static bool BorrowBlockMayReachContinuation(BlockBody body) =>
        (BorrowControlExits(body) & BorrowControlExit.Fallthrough) != 0;

    private static bool BorrowLoopMayReachBackEdge(IReadOnlyList<Statement> statements)
    {
        var exits = BorrowControlExits(statements, result: null);
        return (exits & (BorrowControlExit.Fallthrough | BorrowControlExit.Continue)) != 0;
    }

    private static BorrowControlExit BorrowControlExits(BlockBody body) =>
        BorrowControlExits(body.Statements, body.Value);

    private static BorrowControlExit BorrowControlExits(
        IReadOnlyList<Statement> statements,
        Expression? result)
    {
        var exits = BorrowControlExit.Fallthrough;
        foreach (var statement in statements)
        {
            if ((exits & BorrowControlExit.Fallthrough) == 0)
            {
                break;
            }
            exits = (exits & ~BorrowControlExit.Fallthrough) | BorrowControlExits(statement);
        }

        if ((exits & BorrowControlExit.Fallthrough) != 0 && result is not null)
        {
            exits = (exits & ~BorrowControlExit.Fallthrough) | BorrowControlExits(result);
        }
        return exits;
    }

    private static BorrowControlExit BorrowControlExits(Statement statement) => statement switch
    {
        ReturnStatement => BorrowControlExit.Return,
        LoopControlStatement { Kind: LoopControlKind.Break } => BorrowControlExit.Break,
        LoopControlStatement { Kind: LoopControlKind.Continue } => BorrowControlExit.Continue,
        GuardLoopControlStatement { Kind: LoopControlKind.Break } =>
            BorrowControlExit.Fallthrough | BorrowControlExit.Break,
        GuardLoopControlStatement { Kind: LoopControlKind.Continue } =>
            BorrowControlExit.Fallthrough | BorrowControlExit.Continue,
        BindingStatement binding => BorrowControlExits(binding.Value),
        IndexAssignmentStatement assignment =>
            BorrowControlExits(assignment.Index) | BorrowControlExits(assignment.Value),
        FieldAssignmentStatement assignment => BorrowControlExits(assignment.Value),
        ExpressionStatement expression => BorrowControlExits(expression.Expression),
        BlockFunctionCallStatement block =>
            BorrowControlExit.Fallthrough | (BorrowControlExits(block.Body, result: null) & BorrowControlExit.Return),
        _ => BorrowControlExit.Fallthrough
    };

    private static BorrowControlExit BorrowControlExits(Expression expression) => expression switch
    {
        IfExpression conditional => BorrowControlExits(conditional.Then)
            | (conditional.Else is null
                ? BorrowControlExit.Fallthrough
                : BorrowControlExits(conditional.Else)),
        WhenExpression whenExpression => whenExpression.Arms
            .Select(arm => BorrowControlExits(arm.Body))
            .Aggregate(BorrowControlExit.None, static (left, right) => left | right)
            | BorrowControlExits(whenExpression.Else),
        EnumMatchExpression match => match.Arms
            .Select(arm => BorrowControlExits(arm.Body))
            .Aggregate(BorrowControlExit.None, static (left, right) => left | right)
            | (match.Else is null
                ? BorrowControlExit.None
                : BorrowControlExits(match.Else)),
        _ => BorrowControlExit.Fallthrough
    };

    private void RejectBorrowedTextOriginMutation(string name, int line, int column)
    {
        name = CanonicalBorrowOriginName(name);
        var borrowed = _activeBorrowedTextOrigins
            .FirstOrDefault(pair => pair.Value.Any(origin => BorrowPlacesConflict(origin, name)));
        if (borrowed.Value is null)
        {
            return;
        }

        var origin = borrowed.Value.First(candidate => BorrowPlacesConflict(candidate, name));

        if (_activeReadonlyReferenceBindings.Contains(borrowed.Key))
        {
            throw Error(
                line,
                column,
                $"cannot move or mutate owner '{origin}' while readonly reference '{borrowed.Key}' is live in this scope");
        }

        throw Error(
            line,
            column,
            $"cannot move or mutate origin '{origin}' while borrowed Text view '{borrowed.Key}' is live in this scope");
    }

    private static bool BorrowPlacesConflict(string left, string right)
    {
        var leftParts = SplitBorrowPlace(left);
        var rightParts = SplitBorrowPlace(right);
        if (!StringComparer.Ordinal.Equals(leftParts[0], rightParts[0]))
        {
            return false;
        }

        var shared = Math.Min(leftParts.Count, rightParts.Count);
        for (var index = 1; index < shared; index++)
        {
            if (StringComparer.Ordinal.Equals(leftParts[index], rightParts[index]))
            {
                continue;
            }

            if (leftParts[index][0] == '.' && rightParts[index][0] == '.')
            {
                return false;
            }

            if (leftParts[index][0] == '['
                && rightParts[index][0] == '['
                && leftParts[index] != "[*]"
                && rightParts[index] != "[*]")
            {
                return false;
            }

            return true;
        }

        return true;
    }

    private static List<string> SplitBorrowPlace(string place)
    {
        var parts = new List<string>();
        var projectionStart = place.IndexOfAny(['.', '[']);
        if (projectionStart < 0)
        {
            parts.Add(place);
            return parts;
        }

        parts.Add(place[..projectionStart]);
        var index = projectionStart;
        while (index < place.Length)
        {
            var next = index + 1;
            if (place[index] == '[')
            {
                next = place.IndexOf(']', next);
                if (next < 0)
                {
                    next = place.Length - 1;
                }
                next++;
            }
            else
            {
                while (next < place.Length && place[next] is not ('.' or '['))
                {
                    next++;
                }
            }
            parts.Add(place[index..next]);
            index = next;
        }
        return parts;
    }

    private static string BorrowOriginIndexedPlace(string name, Expression index) =>
        CanonicalBorrowOriginName(name) + BorrowOriginIndexProjection(index);

    private static string BorrowOriginIndexProjection(Expression index) => index switch
    {
        NumberExpression number when IsIntegerLiteralExpression(number) =>
            $"[{number.Text.Replace("_", string.Empty, StringComparison.Ordinal)}]",
        NegateExpression { Value: NumberExpression number } when IsIntegerLiteralExpression(index) =>
            $"[-{number.Text.Replace("_", string.Empty, StringComparison.Ordinal)}]",
        _ => "[*]"
    };

    private static IReadOnlySet<string> EmptyBorrowOrigins() =>
        new HashSet<string>(StringComparer.Ordinal);

    private static string CanonicalBorrowOriginName(string name) => name.TrimEnd('!');
}
