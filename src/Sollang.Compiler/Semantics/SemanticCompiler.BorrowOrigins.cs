using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.Semantics;

internal sealed partial class SemanticCompiler
{
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
                    || !_types.IsReference(function.ReturnType)
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
        if (function.InputType is { } inputType && _types.IsReference(inputType))
        {
            yield return function.InputName ?? "it";
        }
        foreach (var parameter in function.AdditionalParameters ?? [])
        {
            if (_types.IsReference(parameter.Type))
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
        if (expression is NameExpression name
            && _activeReadonlyReferenceBindings.Contains(name.Name)
            && _activeBorrowedTextOrigins.TryGetValue(name.Name, out origins!))
        {
            return true;
        }

        if (expression is CallExpression call
            && TryGetFunction(call.Path, functions, out var called)
            && _types.IsReference(called.ReturnType))
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
                && _types.IsReference(flowed.ReturnType))
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
                if (StoragePlacementAnalyzer.ReferencesName(statements[index], binding))
                {
                    isLive = true;
                    break;
                }
            }

            if (!isLive
                && result is not null
                && StoragePlacementAnalyzer.ReferencesName(result, binding))
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
                if (StoragePlacementAnalyzer.ReferencesName(statements[index], binding))
                {
                    live.Add(binding);
                    break;
                }
            }

            if (result is not null && StoragePlacementAnalyzer.ReferencesName(result, binding))
            {
                live.Add(binding);
            }
        }
        return live;
    }

    private Dictionary<string, IReadOnlySet<string>> CaptureBorrowedTextOriginState() =>
        new(_activeBorrowedTextOrigins, StringComparer.Ordinal);

    private void RestoreBorrowedTextOriginState(IReadOnlyDictionary<string, IReadOnlySet<string>> state)
    {
        _activeBorrowedTextOrigins.Clear();
        foreach (var pair in state)
        {
            _activeBorrowedTextOrigins.Add(pair.Key, pair.Value);
        }
    }

    private static Dictionary<string, IReadOnlySet<string>> MergeBorrowedTextOriginStates(
        IEnumerable<IReadOnlyDictionary<string, IReadOnlySet<string>>> states)
    {
        var merged = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            foreach (var pair in state)
            {
                if (!merged.TryGetValue(pair.Key, out var origins))
                {
                    origins = new HashSet<string>(StringComparer.Ordinal);
                    merged.Add(pair.Key, origins);
                }
                origins.UnionWith(pair.Value);
            }
        }
        return merged.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlySet<string>)pair.Value,
            StringComparer.Ordinal);
    }

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
