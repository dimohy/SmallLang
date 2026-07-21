using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.Semantics;

internal static class SemanticStableIdentity
{
    public static byte[] DeclarationFingerprint(
        TypeDefinitionTable types,
        IEnumerable<BoundTraitDefinition> traits,
        IEnumerable<BoundFunction> functions)
    {
        var fields = new List<string>();
        foreach (var structure in types.Structs.Where(static value => value.IsPublic)
                     .OrderBy(static value => value.Name, StringComparer.Ordinal))
        {
            fields.Add("struct");
            fields.Add(structure.ModuleName);
            fields.Add(structure.Name);
            fields.Add(structure.DeclaringTypeName ?? "");
            fields.Add(structure.IsPublic ? "public" : "private");
            foreach (var field in structure.Fields)
            {
                fields.Add(field.Name);
                fields.Add(Type(types, field.Type));
            }
        }
        foreach (var enumeration in types.Enums.Where(static value => value.IsPublic)
                     .OrderBy(static value => value.Name, StringComparer.Ordinal))
        {
            fields.Add("enum");
            fields.Add(enumeration.ModuleName);
            fields.Add(enumeration.Name);
            fields.Add(enumeration.IsPublic ? "public" : "private");
            foreach (var variant in enumeration.Variants)
            {
                fields.Add(variant.Name);
                fields.Add(variant.PayloadType is { } payload ? Type(types, payload) : "-");
            }
        }
        foreach (var trait in traits.Where(static value => value.IsPublic)
                     .OrderBy(static value => value.Name, StringComparer.Ordinal))
        {
            fields.Add("trait");
            fields.Add(trait.ModuleName);
            fields.Add(trait.Name);
            fields.Add(trait.IsPublic ? "public" : "private");
            foreach (var associated in trait.AssociatedTypes)
                fields.Add(associated.Name);
            foreach (var method in trait.Methods)
            {
                fields.Add(method.Name);
                fields.Add(((int)method.SelfOwnership).ToString(CultureInfo.InvariantCulture));
                fields.Add(method.ReturnType is { } result ? Type(types, result) : "-");
                fields.Add(method.ReturnAssociatedTypeName ?? "");
            }
        }
        var seen = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        foreach (var function in functions.Where(seen.Add).Where(static value => value.IsPublic)
                     .OrderBy(static value => value.ModuleName, StringComparer.Ordinal)
                     .ThenBy(static value => value.Name, StringComparer.Ordinal))
        {
            fields.Add(Function(types, function));
        }

        var builder = new StringBuilder();
        foreach (var field in fields)
            Append(builder, field);
        return SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    public static IReadOnlyDictionary<object, string> IndexSyntaxCallSites(
        IEnumerable<BoundFunction> roots,
        IReadOnlyList<Statement> mainStatements,
        IReadOnlyDictionary<BoundFunction, string> functionIdentities)
    {
        var result = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        var visitedFunctions = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        foreach (var root in roots
                     .OrderBy(static function => function.ModuleName, StringComparer.Ordinal)
                     .ThenBy(static function => function.Name, StringComparer.Ordinal))
        {
            IndexFunctionCallSites(
                root,
                functionIdentities,
                visitedFunctions,
                result);
        }

        var mainOrdinal = 0;
        VisitStatements(mainStatements, "main", result, ref mainOrdinal);
        return result;
    }

    public static IReadOnlyDictionary<BoundFunction, string> IndexFunctions(
        TypeDefinitionTable types,
        IEnumerable<BoundFunction> roots,
        IEnumerable<BoundFunction> resolvedTargets)
    {
        var result = new Dictionary<BoundFunction, string>(ReferenceEqualityComparer.Instance);
        foreach (var root in roots
                     .OrderBy(static function => function.ModuleName, StringComparer.Ordinal)
                     .ThenBy(static function => function.Name, StringComparer.Ordinal))
        {
            AddFunction(types, root, parentIdentity: null, result);
        }
        foreach (var target in resolvedTargets)
        {
            if (!result.ContainsKey(target))
            {
                result.Add(target, Function(types, target, parentIdentity: null));
            }
        }
        return result;
    }

    public static string Function(
        TypeDefinitionTable types,
        BoundFunction function,
        string? parentIdentity = null)
    {
        var builder = new StringBuilder(256);
        Append(builder, parentIdentity is null ? "function" : "local");
        Append(builder, parentIdentity ?? "");
        Append(builder, function.ModuleName);
        Append(builder, function.Name);
        Append(builder, ((int)function.Kind).ToString(CultureInfo.InvariantCulture));
        Append(builder, function.InputType is { } input ? Type(types, input) : "-");
        Append(builder, ((int)function.InputOwnership).ToString(CultureInfo.InvariantCulture));
        Append(builder, Type(types, function.ReturnType));
        Append(builder, function.BlockInputType is { } blockInput ? Type(types, blockInput) : "-");
        Append(builder, function.BlockResultType is { } blockResult ? Type(types, blockResult) : "-");
        Append(builder, function.IsAsync ? "async" : "sync");
        Append(builder, function.GenericParameterName ?? "");
        Append(builder, function.SecondaryGenericParameterName ?? "");
        Append(builder, function.TertiaryGenericParameterName ?? "");
        Append(builder, function.GenericTraitBound ?? "");
        Append(builder, function.GenericAssociatedTypeName ?? "");
        Append(builder, function.GenericAssociatedTypeConstraint is { } constraint
            ? Type(types, constraint)
            : "-");
        Append(builder, function.SpecializedType is { } specialized
            ? Type(types, specialized)
            : "-");
        Append(builder, function.SpecializedSecondaryType is { } secondary
            ? Type(types, secondary)
            : "-");
        Append(builder, function.SpecializedTertiaryType is { } tertiary
            ? Type(types, tertiary)
            : "-");
        Append(builder, function.SpecializedValue?.ToString(CultureInfo.InvariantCulture) ?? "-");
        Append(builder, function.InputTypeTemplate ?? "");
        Append(builder, function.ReturnTypeTemplate ?? "");
        Append(builder, function.BlockInputTypeTemplate ?? "");
        Append(builder, function.BlockResultTypeTemplate ?? "");
        foreach (var parameter in function.AdditionalParameters ?? [])
        {
            Append(builder, parameter.Name);
            Append(builder, Type(types, parameter.Type));
            Append(builder, ((int)parameter.Ownership).ToString(CultureInfo.InvariantCulture));
        }
        foreach (var associated in (function.ImplAssociatedTypes
                     ?? new Dictionary<string, TypeId>(StringComparer.Ordinal))
                 .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            Append(builder, associated.Key);
            Append(builder, Type(types, associated.Value));
        }
        foreach (var effect in (function.Effects ?? new HashSet<string>(StringComparer.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            Append(builder, effect);
        }
        return builder.ToString();
    }

    public static string Type(TypeDefinitionTable types, TypeId type)
    {
        if (types.TryGetOptionValue(type, out var optionValue))
        {
            return "Option<" + Type(types, optionValue) + ">";
        }
        if (types.TryGetResultTypes(type, out var result))
        {
            return "Result<" + Type(types, result.Ok) + "," + Type(types, result.Error) + ">";
        }
        if (types.TryGetTaskValue(type, out var taskValue))
        {
            return "Task<" + Type(types, taskValue) + ">";
        }
        if (types.IsStaticArray(type))
        {
            return "StaticArray<" + Type(types, types.GetStaticArray(type).ElementType) + ">";
        }
        if (types.IsDynamicArray(type))
        {
            return "Array<" + Type(types, types.GetDynamicArray(type).ElementType) + ">";
        }
        if (types.IsDictionary(type))
        {
            var dictionary = types.GetDictionary(type);
            return "Dictionary<" + Type(types, dictionary.KeyType) + ","
                + Type(types, dictionary.ValueType) + ">";
        }
        if (types.IsBox(type))
        {
            return "Box<" + Type(types, types.GetBox(type).ElementType) + ">";
        }
        if (types.IsReference(type))
        {
            return "Ref<" + Type(types, types.GetReference(type).ElementType) + ">";
        }
        if (types.IsDynTrait(type))
        {
            return "Dyn<" + types.GetDynTrait(type).TraitName + ">";
        }
        if (types.IsStruct(type))
        {
            return "struct:" + types.GetStruct(type).Name;
        }
        if (types.IsEnum(type))
        {
            return "enum:" + types.GetEnum(type).Name;
        }
        if ((int)type < (int)TypeId.FirstUserDefined)
        {
            return "builtin:" + type;
        }
        throw new InvalidOperationException(
            $"type id '{(int)type}' has no stable structural identity");
    }

    public static TypeId ResolveType(TypeDefinitionTable types, string identity)
    {
        var parser = new StableTypeParser(types, identity);
        var result = parser.Parse();
        parser.RequireEnd();
        return result;
    }

    private static void AddFunction(
        TypeDefinitionTable types,
        BoundFunction function,
        string? parentIdentity,
        IDictionary<BoundFunction, string> result)
    {
        if (result.ContainsKey(function))
        {
            return;
        }
        var identity = Function(types, function, parentIdentity);
        result.Add(function, identity);
        foreach (var local in function.LocalFunctions.Values
                     .OrderBy(static value => value.Name, StringComparer.Ordinal))
        {
            AddFunction(types, local, identity, result);
        }
    }

    private static void IndexFunctionCallSites(
        BoundFunction function,
        IReadOnlyDictionary<BoundFunction, string> functionIdentities,
        ISet<BoundFunction> visitedFunctions,
        IDictionary<object, string> result)
    {
        if (!visitedFunctions.Add(function))
        {
            return;
        }
        var owner = functionIdentities[function];
        var ordinal = 0;
        if (function.Body is not null)
        {
            VisitExpression(function.Body, owner, result, ref ordinal);
        }
        VisitStatements(function.BlockBody, owner, result, ref ordinal);
        foreach (var local in function.LocalFunctions.Values
                     .OrderBy(static value => value.Name, StringComparer.Ordinal))
        {
            IndexFunctionCallSites(
                local,
                functionIdentities,
                visitedFunctions,
                result);
        }
    }

    private static void VisitStatements(
        IReadOnlyList<Statement> statements,
        string owner,
        IDictionary<object, string> result,
        ref int ordinal)
    {
        foreach (var statement in statements)
        {
            RegisterSyntaxCall(statement, owner, result, ref ordinal);
            switch (statement)
            {
                case BindingStatement binding:
                    VisitExpression(binding.Value, owner, result, ref ordinal);
                    break;
                case IndexAssignmentStatement assignment:
                    VisitExpression(assignment.Index, owner, result, ref ordinal);
                    VisitExpression(assignment.Value, owner, result, ref ordinal);
                    break;
                case FieldAssignmentStatement assignment:
                    VisitExpression(assignment.Value, owner, result, ref ordinal);
                    break;
                case BlockFunctionCallStatement block:
                    VisitExpression(block.Source, owner, result, ref ordinal);
                    VisitStatements(block.Body, owner, result, ref ordinal);
                    break;
                case ExpressionStatement expression:
                    VisitExpression(expression.Expression, owner, result, ref ordinal);
                    break;
                case GuardLoopControlStatement guard:
                    VisitExpression(guard.Condition, owner, result, ref ordinal);
                    break;
                case ReturnStatement { Value: { } value }:
                    VisitExpression(value, owner, result, ref ordinal);
                    break;
            }
        }
    }

    private static void VisitExpression(
        Expression expression,
        string owner,
        IDictionary<object, string> result,
        ref int ordinal)
    {
        RegisterSyntaxCall(expression, owner, result, ref ordinal);
        switch (expression)
        {
            case StringExpression text:
                foreach (var segment in text.Segments.OfType<InterpolationSegment>())
                    VisitExpression(segment.Expression, owner, result, ref ordinal);
                break;
            case AddExpression binary:
                VisitBinary(binary.Left, binary.Right, owner, result, ref ordinal);
                break;
            case SubtractExpression binary:
                VisitBinary(binary.Left, binary.Right, owner, result, ref ordinal);
                break;
            case MultiplyExpression binary:
                VisitBinary(binary.Left, binary.Right, owner, result, ref ordinal);
                break;
            case DivideExpression binary:
                VisitBinary(binary.Left, binary.Right, owner, result, ref ordinal);
                break;
            case ModuloExpression binary:
                VisitBinary(binary.Left, binary.Right, owner, result, ref ordinal);
                break;
            case CompareExpression binary:
                VisitBinary(binary.Left, binary.Right, owner, result, ref ordinal);
                break;
            case AndExpression binary:
                VisitBinary(binary.Left, binary.Right, owner, result, ref ordinal);
                break;
            case OrExpression binary:
                VisitBinary(binary.Left, binary.Right, owner, result, ref ordinal);
                break;
            case RangeExpression binary:
                VisitBinary(binary.Start, binary.End, owner, result, ref ordinal);
                break;
            case NegateExpression unary:
                VisitExpression(unary.Value, owner, result, ref ordinal);
                break;
            case NotExpression unary:
                VisitExpression(unary.Value, owner, result, ref ordinal);
                break;
            case TryExpression unary:
                VisitExpression(unary.Value, owner, result, ref ordinal);
                break;
            case BoxExpression unary:
                VisitExpression(unary.Value, owner, result, ref ordinal);
                break;
            case CompileTimeEachExpression each:
                VisitExpression(each.Source, owner, result, ref ordinal);
                VisitExpression(each.Selector, owner, result, ref ordinal);
                if (each.DictionaryValueSelector is not null)
                    VisitExpression(each.DictionaryValueSelector, owner, result, ref ordinal);
                break;
            case FoldExpression fold:
                VisitExpression(fold.Source, owner, result, ref ordinal);
                VisitExpression(fold.Initial, owner, result, ref ordinal);
                VisitBlock(fold.Body, owner, result, ref ordinal);
                break;
            case IfExpression conditional:
                VisitExpression(conditional.Condition, owner, result, ref ordinal);
                VisitBlock(conditional.Then, owner, result, ref ordinal);
                if (conditional.Else is not null)
                    VisitBlock(conditional.Else, owner, result, ref ordinal);
                break;
            case WhenExpression selection:
                if (selection.Subject is not null)
                    VisitExpression(selection.Subject, owner, result, ref ordinal);
                VisitArms(selection.Arms, owner, result, ref ordinal);
                VisitBlock(selection.Else, owner, result, ref ordinal);
                break;
            case FlowExpression flow:
                VisitExpression(flow.Source, owner, result, ref ordinal);
                foreach (var target in flow.Targets)
                {
                    RegisterSyntaxCall(target, owner, result, ref ordinal);
                    foreach (var argument in target.Arguments)
                        VisitExpression(argument, owner, result, ref ordinal);
                }
                break;
            case CallExpression call:
                foreach (var argument in call.Arguments)
                    VisitExpression(argument, owner, result, ref ordinal);
                break;
            case ArrayLiteralExpression array:
                foreach (var element in array.Elements)
                    VisitExpression(element, owner, result, ref ordinal);
                break;
            case ArrayRepeatExpression repeat:
                VisitExpression(repeat.Value, owner, result, ref ordinal);
                break;
            case DictionaryLiteralExpression dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    VisitExpression(entry.Key, owner, result, ref ordinal);
                    VisitExpression(entry.Value, owner, result, ref ordinal);
                }
                break;
            case IndexExpression index:
                VisitBinary(index.Source, index.Index, owner, result, ref ordinal);
                break;
            case StructLiteralExpression structure:
                foreach (var field in structure.Fields)
                    VisitExpression(field.Value, owner, result, ref ordinal);
                break;
            case FieldAccessExpression field:
                VisitExpression(field.Source, owner, result, ref ordinal);
                break;
            case MapExpression map:
                VisitExpression(map.Path, owner, result, ref ordinal);
                if (map.Offset is not null) VisitExpression(map.Offset, owner, result, ref ordinal);
                if (map.Length is not null) VisitExpression(map.Length, owner, result, ref ordinal);
                if (map.FileSize is not null) VisitExpression(map.FileSize, owner, result, ref ordinal);
                break;
            case EnumMatchExpression match:
                VisitExpression(match.Subject, owner, result, ref ordinal);
                VisitArms(match.Arms, owner, result, ref ordinal);
                if (match.Else is not null) VisitBlock(match.Else, owner, result, ref ordinal);
                break;
            case SubjectCompareExpression compare:
                VisitExpression(compare.Right, owner, result, ref ordinal);
                break;
            case SubjectRangeExpression range:
                VisitBinary(range.Start, range.End, owner, result, ref ordinal);
                break;
        }
    }

    private static void VisitBinary(
        Expression left,
        Expression right,
        string owner,
        IDictionary<object, string> result,
        ref int ordinal)
    {
        VisitExpression(left, owner, result, ref ordinal);
        VisitExpression(right, owner, result, ref ordinal);
    }

    private static void VisitBlock(
        BlockBody block,
        string owner,
        IDictionary<object, string> result,
        ref int ordinal)
    {
        VisitStatements(block.Statements, owner, result, ref ordinal);
        if (block.Value is not null)
            VisitExpression(block.Value, owner, result, ref ordinal);
    }

    private static void VisitArms(
        IReadOnlyList<WhenArm> arms,
        string owner,
        IDictionary<object, string> result,
        ref int ordinal)
    {
        foreach (var arm in arms)
        {
            VisitExpression(arm.Condition, owner, result, ref ordinal);
            VisitBlock(arm.Body, owner, result, ref ordinal);
        }
    }

    private static void RegisterSyntaxCall(
        object node,
        string owner,
        IDictionary<object, string> result,
        ref int ordinal)
    {
        if (node is not (CallExpression
            or TypeApplicationExpression
            or FlowTarget
            or BlockFunctionCallStatement))
        {
            return;
        }
        if (!result.TryAdd(node, owner + "/call:" + ordinal.ToString(CultureInfo.InvariantCulture)))
        {
            throw new InvalidOperationException("syntax call site occurs more than once in syntax traversal");
        }
        ordinal++;
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }

    private sealed class StableTypeParser(TypeDefinitionTable types, string text)
    {
        private int _position;

        public TypeId Parse()
        {
            if (Take("Option<"))
                return Close(types.GetOrAddOption(Parse(), "Option"));
            if (Take("Task<"))
                return Close(types.GetOrAddTask(Parse()));
            if (Take("StaticArray<"))
                return Close(types.GetOrAddStaticArray(Parse()));
            if (Take("Array<"))
                return Close(types.GetOrAddDynamicArray(Parse()));
            if (Take("Box<"))
            {
                var element = Parse();
                Expect('>');
                if (types.Boxes.FirstOrDefault(box => box.ElementType == element) is { } box)
                    return box.Id;
                throw Invalid("unknown box type");
            }
            if (Take("Ref<"))
                return Close(types.GetOrAddReference(Parse()));
            if (Take("Dyn<"))
            {
                var traitName = Rest();
                Expect('>');
                return types.GetOrAddDynTrait(traitName);
            }
            if (Take("Result<"))
            {
                var ok = Parse();
                Expect(',');
                var error = Parse();
                Expect('>');
                return types.GetOrAddResult(ok, error, "Result");
            }
            if (Take("Dictionary<"))
            {
                var key = Parse();
                Expect(',');
                var value = Parse();
                Expect('>');
                return types.GetOrAddDictionary(key, value);
            }
            if (Take("builtin:"))
            {
                var name = Rest();
                if (Enum.TryParse<TypeId>(name, out var builtin)
                    && (int)builtin < (int)TypeId.FirstUserDefined)
                    return builtin;
                throw Invalid("unknown builtin type");
            }
            if (Take("struct:") || Take("enum:"))
            {
                var name = Rest();
                if (types.TryResolve(name, out var nominal))
                    return nominal;
                throw Invalid("unknown nominal type");
            }
            throw Invalid("unknown stable type identity");
        }

        public void RequireEnd()
        {
            if (_position != text.Length)
                throw Invalid("trailing stable type data");
        }

        private TypeId Close(TypeId type)
        {
            Expect('>');
            return type;
        }

        private bool Take(string value)
        {
            if (!text.AsSpan(_position).StartsWith(value, StringComparison.Ordinal))
                return false;
            _position += value.Length;
            return true;
        }

        private string Rest()
        {
            var start = _position;
            while (_position < text.Length && text[_position] is not ('>' or ','))
                _position++;
            return text[start.._position];
        }

        private void Expect(char value)
        {
            if (_position >= text.Length || text[_position] != value)
                throw Invalid($"expected '{value}'");
            _position++;
        }

        private InvalidDataException Invalid(string message) =>
            new($"{message} at stable type offset {_position}");
    }
}
