using System.Globalization;
using System.Numerics;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.Semantics;

internal sealed class SemanticCompiler
{
    private readonly SmallLangProgram _program;
    private readonly TypeDefinitionTable _types;
    private readonly IReadOnlyDictionary<string, BoundTraitDefinition> _traits;
    private readonly Dictionary<object, BoundFunction> _resolvedGenericCalls = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BoundFunction> _validatingGenericSpecializations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundFunction, IReadOnlyDictionary<string, BoundType>> _functionBindings =
        new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, BoundFunction>? _boundFunctions;
    private string _currentModuleName = "";
    private string? _currentTypeScopeName;
    private BoundType? _currentFunctionReturnType;
    private string? _currentMoveInputName;
    private IReadOnlyDictionary<string, BoundType>? _currentFunctionOuterBindings;
    private bool _currentFunctionAllowsEarlyReturn;
    private bool _currentFunctionIsAsync;
    private readonly int _pointerBitWidth;
    private int _loopDepth;

    public SemanticCompiler(SmallLangProgram program, int pointerBitWidth)
    {
        _program = program;
        _pointerBitWidth = pointerBitWidth;
        _types = BuildTypeDefinitions(program.Structs, program.Enums);
        _traits = BindTraits(program.Traits);
    }

    public BoundProgram Compile()
    {
        var functions = BindFunctions();
        var mainBindings = BindMain(functions);
        var storagePlacement = StoragePlacementAnalyzer.Analyze(_program, functions);
        return new BoundProgram(
            _types,
            _traits,
            functions,
            _resolvedGenericCalls,
            _program.Statements,
            mainBindings,
            _functionBindings,
            storagePlacement.MainFrame,
            storagePlacement.FunctionFrames);
    }

    private IReadOnlyDictionary<string, BoundTraitDefinition> BindTraits(
        IReadOnlyList<TraitDeclaration> declarations)
    {
        var traits = new Dictionary<string, BoundTraitDefinition>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            _currentModuleName = declaration.ModuleName;
            if (IsReservedName(declaration.Name))
            {
                throw Error(declaration.Line, declaration.Column, $"trait name '{declaration.Name}' is reserved");
            }
            if (_types.TryResolve(declaration.Name, out _) || traits.ContainsKey(declaration.Name))
            {
                throw Error(declaration.Line, declaration.Column, $"trait '{declaration.Name}' already exists");
            }

            var methods = new List<BoundTraitMethod>(declaration.Methods.Count);
            var associatedTypes = declaration.AssociatedTypes
                .Select(type => new BoundTraitAssociatedType(type.Name, type.Line, type.Column))
                .ToArray();
            var methodNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in declaration.Methods)
            {
                if (!methodNames.Add(method.Name))
                {
                    throw Error(
                        method.Line,
                        method.Column,
                        $"trait method '{declaration.Name}.{method.Name}' already exists");
                }

                var returnAssociatedType = declaration.AssociatedTypes
                    .FirstOrDefault(type => type.Name == method.ReturnType);
                methods.Add(new BoundTraitMethod(
                    method.Name,
                    method.SelfOwnership switch
                    {
                        FunctionInputOwnership.Default => BoundFunctionInputOwnership.Default,
                        FunctionInputOwnership.Move => BoundFunctionInputOwnership.Move,
                        FunctionInputOwnership.MutableBorrow => BoundFunctionInputOwnership.MutableBorrow,
                        _ => throw new InvalidOperationException("unsupported trait receiver ownership")
                    },
                    returnAssociatedType is null
                        ? ParseType(method.ReturnType, method.Line, method.Column)
                        : null,
                    returnAssociatedType?.Name,
                    method.Line,
                    method.Column));
            }

            traits.Add(
                declaration.Name,
                new BoundTraitDefinition(
                    declaration.Name,
                    associatedTypes,
                    methods,
                    declaration.Line,
                    declaration.Column,
                    declaration.ModuleName,
                    declaration.IsPublic));
        }

        return traits;
    }

    private void ValidateTraitImplementations(IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var implementations = new Dictionary<(string Trait, BoundType Type), HashSet<string>>();
        foreach (var function in functions.Values)
        {
            _currentModuleName = function.ModuleName;
            if (function.TraitName is null)
            {
                continue;
            }

            if (!_traits.TryGetValue(function.TraitName, out var trait))
            {
                throw Error(function.Line, function.Column, $"unknown trait '{function.TraitName}'");
            }
            EnsureTraitVisible(trait, function.Line, function.Column);
            if (function.InputType is not { } inputType
                || (!_types.IsStruct(inputType) && !_types.IsEnum(inputType)))
            {
                throw Error(function.Line, function.Column, "trait methods require a user type self receiver");
            }

            var associatedBindings = function.ImplAssociatedTypes
                ?? new Dictionary<string, TypeId>(StringComparer.Ordinal);
            var unknownAssociatedType = associatedBindings.Keys
                .FirstOrDefault(name => !trait.AssociatedTypes.Any(type => type.Name == name));
            if (unknownAssociatedType is not null)
            {
                throw Error(
                    function.Line,
                    function.Column,
                    $"trait '{trait.Name}' has no associated type '{unknownAssociatedType}'");
            }
            var missingAssociatedType = trait.AssociatedTypes
                .FirstOrDefault(type => !associatedBindings.ContainsKey(type.Name));
            if (missingAssociatedType is not null)
            {
                throw Error(
                    function.Line,
                    function.Column,
                    $"impl {trait.Name} requires associated type '{missingAssociatedType.Name}'");
            }

            var methodName = function.Name[(function.Name.LastIndexOf('.') + 1)..];
            var requirement = trait.Methods.FirstOrDefault(method => method.Name == methodName)
                ?? throw Error(
                    function.Line,
                    function.Column,
                    $"trait '{trait.Name}' has no method '{methodName}'");
            var requiredReturnType = requirement.ReturnType
                ?? associatedBindings[requirement.ReturnAssociatedTypeName!];
            if (function.InputOwnership != requirement.SelfOwnership
                || function.ReturnType != requiredReturnType)
            {
                throw Error(
                    function.Line,
                    function.Column,
                    $"trait method '{trait.Name}.{methodName}' signature does not match its declaration");
            }

            var key = (trait.Name, inputType);
            if (!implementations.TryGetValue(key, out var methods))
            {
                methods = new HashSet<string>(StringComparer.Ordinal);
                implementations.Add(key, methods);
            }
            methods.Add(methodName);
        }

        foreach (var ((traitName, type), methods) in implementations)
        {
            var trait = _traits[traitName];
            var missing = trait.Methods.FirstOrDefault(method => !methods.Contains(method.Name));
            if (missing is not null)
            {
                var typeName = _types.IsStruct(type) ? _types.GetStruct(type).Name : _types.GetEnum(type).Name;
                throw Error(
                    trait.Line,
                    trait.Column,
                    $"impl {traitName} for {typeName} is missing method '{missing.Name}'");
            }
        }
    }

    private IReadOnlyDictionary<string, BoundFunction> BindFunctions()
    {
        var functions = new Dictionary<string, BoundFunction>(StringComparer.Ordinal);
        _boundFunctions = functions;
        foreach (var declaration in _program.Functions)
        {
            if (functions.ContainsKey(declaration.Name))
            {
                throw Error(declaration.Line, declaration.Column, $"function '{declaration.Name}' already exists");
            }

            var function = BindFunctionDeclaration(declaration, isLocal: false);
            functions.Add(function.Name, function);
        }

        ValidateTraitImplementations(functions);
        AddGlobalAliases(functions);
        ValidateMemberNameCollisions(functions);

        var checkedFunctions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in functions.Values.ToArray())
        {
            if (function.Kind is not (BoundFunctionKind.User or BoundFunctionKind.UserBlock)
                || (function.GenericParameterName is not null
                    && function.SpecializedType is null
                    && function.SpecializedValue is null)
                || !checkedFunctions.Add(function.Name))
            {
                continue;
            }

            ValidateUserFunction(
                function,
                functions,
                new Dictionary<string, BoundType>(StringComparer.Ordinal));
        }

        return functions;
    }

    private void ValidateMemberNameCollisions(IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var seen = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        foreach (var function in functions.Values)
        {
            if (!seen.Add(function))
            {
                continue;
            }

            var definition = function.InputType is { } inputType && _types.IsStruct(inputType)
                ? _types.GetStruct(inputType)
                : _types.Structs.FirstOrDefault(structure =>
                    function.Name.StartsWith(structure.Name + ".", StringComparison.Ordinal));
            if (definition is null)
            {
                continue;
            }

            var memberName = function.Name[(function.Name.LastIndexOf('.') + 1)..];
            if (definition.Fields.Any(field => field.Name == memberName))
            {
                throw Error(
                    function.Line,
                    function.Column,
                    $"member '{definition.Name}.{memberName}' conflicts with a stored field of the same name");
            }
        }
    }

    private BoundFunction BindFunctionDeclaration(FunctionDeclaration function, bool isLocal)
    {
        _currentModuleName = function.ModuleName;
        _currentTypeScopeName = ResolveFunctionTypeScope(function.Name);
        ValidateFunctionDeclaration(function, isLocal);

        if (function.GenericTraitBound is not null
            && !function.IsValueGeneric
            && !_traits.ContainsKey(function.GenericTraitBound))
        {
            throw Error(function.Line, function.Column, $"unknown trait bound '{function.GenericTraitBound}'");
        }

        if (function.GenericAssociatedTypeName is not null && function.GenericTraitBound is null)
        {
            throw Error(function.Line, function.Column, "associated type equality requires a trait bound");
        }
        if (function.GenericTraitBound is { } constrainedTraitName
            && function.GenericAssociatedTypeName is { } constrainedAssociatedTypeName)
        {
            var constrainedTrait = _traits[constrainedTraitName];
            EnsureTraitVisible(constrainedTrait, function.Line, function.Column);
            if (!constrainedTrait.AssociatedTypes.Any(type => type.Name == constrainedAssociatedTypeName))
            {
                throw Error(
                    function.Line,
                    function.Column,
                    $"trait '{constrainedTrait.Name}' has no associated type '{constrainedAssociatedTypeName}'");
            }
        }

        var inputType = function.InputType is null
            ? (BoundType?)null
            : ParseFunctionType(
                function.InputType,
                function.GenericParameterName,
                function.SecondaryGenericParameterName,
                function.Line,
                function.Column);
        if (function.InputOwnership == FunctionInputOwnership.Default
            && inputType == BoundType.IntDictionary)
        {
            inputType = BoundType.IntDictionaryView;
        }

        var returnType = ParseFunctionType(
            function.ReturnType,
            function.GenericParameterName,
            function.SecondaryGenericParameterName,
            function.Line,
            function.Column);
        if (returnType == BoundType.IntSlice)
        {
            throw Error(function.Line, function.Column, "readonly Int view returns are not implemented yet");
        }

        var blockInputType = function.BlockInputType is null
            ? (BoundType?)null
            : ParseType(function.BlockInputType, function.Line, function.Column);
        var genericAssociatedTypeConstraint = function.GenericAssociatedTypeConstraint is null
            ? (TypeId?)null
            : ParseFunctionType(
                function.GenericAssociatedTypeConstraint,
                function.GenericParameterName,
                function.SecondaryGenericParameterName,
                function.Line,
                function.Column);
        IReadOnlyDictionary<string, TypeId>? implAssociatedTypes = function.ImplAssociatedTypes is null
            ? null
            : function.ImplAssociatedTypes.ToDictionary(
                static pair => pair.Key,
                pair => (TypeId)ParseType(pair.Value, function.Line, function.Column),
                StringComparer.Ordinal);
        if (function.TraitName is null && implAssociatedTypes is { Count: > 0 })
        {
            throw Error(function.Line, function.Column, "associated type bindings require a trait impl");
        }
        var inputOwnership = BindFunctionInputOwnership(function, inputType);
        if (function.IsAsync
            && (!IsAsyncResultTypeSupported(returnType)
                || !IsAsyncInputTypeSupported(inputType, inputOwnership)
                || isLocal
                || function.IsStandardLibrary
                || function.IsIntrinsic))
        {
            throw Error(
                function.Line,
                function.Column,
                "async functions require a transferable result, a sendable input, and a non-local user declaration; owned inputs must use move");
        }
        var kind = BindFunctionKind(function, inputType, returnType, isLocal);
        var localFunctions = BindLocalFunctions(function);

        return new BoundFunction(
            function.Name,
            function.InputName,
            inputType,
            inputOwnership,
            returnType,
            function.BlockInputName,
            blockInputType,
            localFunctions,
            function.Body,
            function.BlockBody,
            function.Line,
            function.Column,
            kind,
            function.IsStandardLibrary,
            isLocal,
            function.TraitName,
            function.GenericParameterName,
            function.SecondaryGenericParameterName,
            function.GenericTraitBound,
            function.GenericAssociatedTypeName,
            genericAssociatedTypeConstraint,
            implAssociatedTypes,
            IsValueGeneric: function.IsValueGeneric,
            HasValueGenericFixedArrayInput: function.HasValueGenericFixedArrayInput,
            ModuleName: function.ModuleName,
            IsPublic: function.IsPublic || function.IsStandardLibrary,
            IsAsync: function.IsAsync);
    }

    private BoundFunctionInputOwnership BindFunctionInputOwnership(
        FunctionDeclaration function,
        BoundType? inputType)
    {
        if (function.InputOwnership == FunctionInputOwnership.Move)
        {
            if (inputType is null)
            {
                throw Error(function.Line, function.Column, "move input requires an input type");
            }

            if (!IsOwnedHeapType(inputType.Value)
                && !_types.IsStruct(inputType.Value)
                && !_types.IsEnum(inputType.Value))
            {
                throw Error(function.Line, function.Column, "move input expects an owned container or user value type");
            }

            return BoundFunctionInputOwnership.Move;
        }

        if (function.InputOwnership == FunctionInputOwnership.MutableBorrow)
        {
            if (inputType is null)
            {
                throw Error(function.Line, function.Column, "mut input requires an input type");
            }

            if (inputType.Value is not (BoundType.DynamicIntArray or BoundType.IntDictionary or BoundType.Arena)
                && !_types.IsDynamicArray(inputType.Value)
                && !_types.IsDictionary(inputType.Value)
                && !_types.IsStruct(inputType.Value))
            {
                throw Error(function.Line, function.Column, "mut input expects an owned container or struct value");
            }

            return BoundFunctionInputOwnership.MutableBorrow;
        }

        return BoundFunctionInputOwnership.Default;
    }

    private void ValidateFunctionDeclaration(FunctionDeclaration function, bool isLocal)
    {
        if (function.GenericParameterName is not null)
        {
            if (isLocal || function.TraitName is not null)
            {
                throw Error(function.Line, function.Column, "generic local and impl functions are not implemented yet");
            }
            if (!function.IsValueGeneric
                && function.InputType is not null
                && function.InputType != function.GenericParameterName)
            {
                throw Error(
                    function.Line,
                    function.Column,
                    $"generic function input must use its type parameter '{function.GenericParameterName}' in this slice");
            }
            if (function.InputOwnership != FunctionInputOwnership.Default)
            {
                throw Error(function.Line, function.Column, "generic function inputs are readonly in this slice");
            }
        }

        if (isLocal && function.Name.Contains('.', StringComparison.Ordinal))
        {
            throw Error(function.Line, function.Column, "local function names cannot be path-qualified");
        }

        if (IsReservedName(function.Name))
        {
            throw Error(function.Line, function.Column, $"function name '{function.Name}' is reserved");
        }

        if (!function.IsStandardLibrary && function.Name.StartsWith("sys.", StringComparison.Ordinal))
        {
            throw Error(function.Line, function.Column, "the sys namespace is reserved for the standard library");
        }

        if (function.InputName is not null && function.InputType is null)
        {
            throw Error(function.Line, function.Column, "function input name requires an input type");
        }

        if (function.InputName is not null
            && !(function.InputName == "self" && function.Name.Contains('.', StringComparison.Ordinal)))
        {
            ValidateBindingName(function.InputName, function.Line, function.Column);
        }

        if (function.BlockInputName is not null && function.BlockInputType is null)
        {
            throw Error(function.Line, function.Column, "block input name requires a block input type");
        }

        if (function.BlockInputName is not null)
        {
            ValidateBindingName(function.BlockInputName, function.Line, function.Column);
        }

        if (function.BlockInputName is not null && function.ReturnType != "Unit")
        {
            throw Error(function.Line, function.Column, "block functions must declare Unit return type");
        }

    }

    private IReadOnlyDictionary<string, BoundFunction> BindLocalFunctions(FunctionDeclaration owner)
    {
        var functions = new Dictionary<string, BoundFunction>(StringComparer.Ordinal);
        foreach (var localDeclaration in owner.LocalFunctions)
        {
            if (functions.ContainsKey(localDeclaration.Name))
            {
                throw Error(
                    localDeclaration.Line,
                    localDeclaration.Column,
                    $"local function '{localDeclaration.Name}' already exists in function '{owner.Name}'");
            }

            var localFunction = BindFunctionDeclaration(localDeclaration, isLocal: true);
            functions.Add(localFunction.Name, localFunction);
        }

        return functions;
    }

    private void ValidateUserFunction(
        BoundFunction function,
        IReadOnlyDictionary<string, BoundFunction> parentFunctions,
        IReadOnlyDictionary<string, BoundType> capturedBindings)
    {
        _currentModuleName = function.ModuleName;
        _currentTypeScopeName = ResolveFunctionTypeScope(function.Name);
        if (function.Kind == BoundFunctionKind.UserBlock)
        {
            ValidateUserBlockFunction(function, parentFunctions, capturedBindings);
            return;
        }

        var bodyBindings = new Dictionary<string, BoundType>(capturedBindings, StringComparer.Ordinal);
        if (function.InputType is { } inputType)
        {
            bodyBindings[function.InputName ?? "it"] = inputType;
        }
        if (function.IsValueGeneric
            && function.SpecializedValue is not null
            && function.GenericParameterName is { } valueParameterName)
        {
            bodyBindings[valueParameterName] = BoundType.Int;
        }

        var returnOuterBindings = new Dictionary<string, BoundType>(bodyBindings, StringComparer.Ordinal);

        var scopedFunctions = CreateFunctionScope(parentFunctions, function.LocalFunctions);
        foreach (var localFunction in function.LocalFunctions.Values)
        {
            ValidateUserFunction(localFunction, scopedFunctions, bodyBindings);
        }
        _currentFunctionReturnType = function.ReturnType;
        _currentMoveInputName = FunctionMovesOwnedHeapInput(function)
            ? function.InputName ?? "it"
            : null;
        _currentFunctionOuterBindings = returnOuterBindings;
        _currentFunctionAllowsEarlyReturn = !function.IsLocal;
        _currentFunctionIsAsync = function.IsAsync;

        var mutableBindings = new HashSet<string>(StringComparer.Ordinal);
        if (FunctionMutablyBorrowsInput(function))
        {
            mutableBindings.Add(function.InputName ?? "it");
        }

        var returnedMoveInputName = FunctionMovesOwnedHeapInput(function)
            && function.InputType == function.ReturnType
                ? function.InputName ?? "it"
                : null;

        BindStatements(function.BlockBody, scopedFunctions, bodyBindings, mutableBindings, allowContainerBindings: true);
        var bodyType = function.Body is null
            ? BoundType.Unit
            : InferExpression(
                function.Body,
                scopedFunctions,
                bodyBindings,
                allowPrintCall: false,
                allowReadIntCall: function.IsStandardLibrary,
                allowFlowBindingTarget: false,
                yieldInputType: null,
                mutableBindings: mutableBindings,
                allowedOwnedOuterResultName: returnedMoveInputName);
        if (bodyType != function.ReturnType)
        {
            throw Error(
                function.Line,
                function.Column,
                $"function '{function.Name}' returns {FormatType(bodyType)} but declares {FormatType(function.ReturnType)}");
        }

        if (function.Body is not null && IsContainerType(bodyType))
        {
            EnsureOwnedContainerCanLeaveBlock(
                function.Body,
                returnOuterBindings,
                bodyBindings,
                returnedMoveInputName);

            if (returnedMoveInputName is not null)
            {
                EnsureMoveInputReturnCoverage(
                    function.Body,
                    returnedMoveInputName,
                    scopedFunctions);
            }
        }

        _functionBindings[function] = new Dictionary<string, BoundType>(
            bodyBindings,
            StringComparer.Ordinal);

    }

    private void ValidateUserBlockFunction(
        BoundFunction function,
        IReadOnlyDictionary<string, BoundFunction> parentFunctions,
        IReadOnlyDictionary<string, BoundType> capturedBindings)
    {
        _currentFunctionIsAsync = false;
        if (function.InputType is null)
        {
            throw Error(function.Line, function.Column, $"block function '{function.Name}' requires an input");
        }

        if (function.BlockInputType is null)
        {
            throw Error(function.Line, function.Column, $"block function '{function.Name}' requires a block input");
        }

        var bodyBindings = new Dictionary<string, BoundType>(capturedBindings, StringComparer.Ordinal)
        {
            [function.InputName ?? "it"] = function.InputType.Value
        };

        var scopedFunctions = CreateFunctionScope(parentFunctions, function.LocalFunctions);
        foreach (var localFunction in function.LocalFunctions.Values)
        {
            ValidateUserFunction(localFunction, scopedFunctions, bodyBindings);
        }

        BindStatements(function.BlockBody, scopedFunctions, bodyBindings, yieldInputType: function.BlockInputType.Value);
    }

    private IReadOnlyDictionary<string, BoundFunction> CreateFunctionScope(
        IReadOnlyDictionary<string, BoundFunction> parentFunctions,
        IReadOnlyDictionary<string, BoundFunction> localFunctions)
    {
        if (localFunctions.Count == 0)
        {
            return parentFunctions;
        }

        var functions = new Dictionary<string, BoundFunction>(parentFunctions, StringComparer.Ordinal);
        foreach (var (name, function) in localFunctions)
        {
            functions[name] = function;
        }

        return functions;
    }

    private BoundFunctionKind BindFunctionKind(
        FunctionDeclaration function,
        BoundType? inputType,
        BoundType returnType,
        bool isLocal)
    {
        if (!function.IsIntrinsic)
        {
            return function.BlockInputName is null
                ? BoundFunctionKind.User
                : BoundFunctionKind.UserBlock;
        }

        if (isLocal)
        {
            throw Error(function.Line, function.Column, "local intrinsic functions are not supported");
        }

        if (!function.IsStandardLibrary)
        {
            throw Error(function.Line, function.Column, "intrinsic functions are reserved for the standard library");
        }

        if (function.Body is not null)
        {
            throw Error(function.Line, function.Column, $"intrinsic function '{function.Name}' cannot have a body");
        }

        return function.Name switch
        {
            "sys.runtime.print" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                BoundType.Text,
                BoundType.Unit,
                BoundFunctionKind.RuntimePrint),
            "sys.runtime.println" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                BoundType.Text,
                BoundType.Unit,
                BoundFunctionKind.RuntimePrintLine),
            "sys.runtime.readInt" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                BoundType.Text,
                BoundType.Int,
                BoundFunctionKind.RuntimeReadInt),
            "sys.runtime.seedRandom" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                BoundType.Int,
                BoundType.Unit,
                BoundFunctionKind.RuntimeSeedRandom),
            "sys.runtime.randomBelow" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                BoundType.Int,
                BoundType.Int,
                BoundFunctionKind.RuntimeRandomBelow),
            "sys.runtime.openIntWriter" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                BoundType.Text,
                BoundType.Unit,
                BoundFunctionKind.RuntimeOpenIntWriter),
            "sys.runtime.writeInt" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                BoundType.Int,
                BoundType.Unit,
                BoundFunctionKind.RuntimeWriteInt),
            "sys.runtime.closeIntWriter" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                expectedInputType: null,
                BoundType.Unit,
                BoundFunctionKind.RuntimeCloseIntWriter),
            "sys.runtime.openIntReader" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                BoundType.Text,
                BoundType.Unit,
                BoundFunctionKind.RuntimeOpenIntReader),
            "sys.runtime.closestInt" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                BoundType.Int,
                BoundType.Int,
                BoundFunctionKind.RuntimeClosestInt),
            "sys.runtime.closeIntReader" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                expectedInputType: null,
                BoundType.Unit,
                BoundFunctionKind.RuntimeCloseIntReader),
            "sys.runtime.nowMillis" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                expectedInputType: null,
                BoundType.Int64,
                BoundFunctionKind.RuntimeNowMillis),
            "sys.process.arguments" => RequireIntrinsicSignature(
                function,
                inputType,
                returnType,
                expectedInputType: null,
                BoundType.Arguments,
                BoundFunctionKind.RuntimeArguments),
            "sys.process.environment" => RequireEnvironmentIntrinsicSignature(
                function,
                inputType,
                returnType),
            "sys.process.run" => RequireProcessRunIntrinsicSignature(
                function,
                inputType,
                returnType),
            "sys.file.write" => RequireGenericScalarWriteSignature(
                function,
                inputType,
                returnType),
            "sys.file.read" => RequireGenericScalarReadSignature(
                function,
                inputType,
                returnType),
            "sys.file.openWriter" => RequireIntrinsicSignature(
                function, inputType, returnType, BoundType.Text, BoundType.Unit,
                BoundFunctionKind.RuntimeOpenIntWriter),
            "sys.file.closeWriter" => RequireIntrinsicSignature(
                function, inputType, returnType, expectedInputType: null, BoundType.Unit,
                BoundFunctionKind.RuntimeCloseIntWriter),
            "sys.file.openReader" => RequireIntrinsicSignature(
                function, inputType, returnType, BoundType.Text, BoundType.Unit,
                BoundFunctionKind.RuntimeOpenIntReader),
            "sys.file.closeReader" => RequireIntrinsicSignature(
                function, inputType, returnType, expectedInputType: null, BoundType.Unit,
                BoundFunctionKind.RuntimeCloseIntReader),
            _ => throw Error(function.Line, function.Column, $"unknown intrinsic function '{function.Name}'")
        };
    }

    private BoundFunctionKind RequireEnvironmentIntrinsicSignature(
        FunctionDeclaration function,
        BoundType? inputType,
        BoundType returnType)
    {
        if (inputType != BoundType.Text
            || !_types.TryGetOptionValue(returnType, out var valueType)
            || valueType != BoundType.Text)
        {
            throw Error(function.Line, function.Column,
                $"intrinsic '{function.Name}' must have signature Text -> Option<Text>");
        }
        return BoundFunctionKind.RuntimeEnvironment;
    }

    private BoundFunctionKind RequireProcessRunIntrinsicSignature(
        FunctionDeclaration function,
        BoundType? inputType,
        BoundType returnType)
    {
        if (inputType is not { } argvType
            || !_types.IsDynamicArray(argvType)
            || _types.GetDynamicArray(argvType).ElementType != BoundType.Text
            || !_types.TryGetResultTypes(returnType, out var resultTypes)
            || resultTypes.Ok != BoundType.Int
            || resultTypes.Error != BoundType.Text)
        {
            throw Error(function.Line, function.Column,
                $"intrinsic '{function.Name}' must have signature [Text; ~] -> Result<Int, Text>");
        }
        return BoundFunctionKind.RuntimeRunProcess;
    }

    private BoundFunctionKind RequireGenericScalarWriteSignature(
        FunctionDeclaration function,
        BoundType? inputType,
        BoundType returnType)
    {
        if (function.GenericParameterName is null
            || inputType != BoundType.GenericParameter
            || returnType != BoundType.Unit)
        {
            throw Error(function.Line, function.Column,
                $"intrinsic '{function.Name}' must have signature write<T>: T -> Unit");
        }
        return BoundFunctionKind.RuntimeWriteScalar;
    }

    private BoundFunctionKind RequireGenericScalarReadSignature(
        FunctionDeclaration function,
        BoundType? inputType,
        BoundType returnType)
    {
        if (function.GenericParameterName is null
            || inputType is not null
            || !_types.TryGetResultTypes(returnType, out var resultTypes)
            || !_types.TryGetOptionValue(resultTypes.Ok, out var valueType)
            || valueType != BoundType.GenericParameter
            || resultTypes.Error != BoundType.Text)
        {
            throw Error(function.Line, function.Column,
                $"intrinsic '{function.Name}' must have signature read<T>: -> Result<Option<T>, Text>");
        }
        return BoundFunctionKind.RuntimeReadScalar;
    }

    private BoundFunctionKind RequireIntrinsicSignature(
        FunctionDeclaration function,
        BoundType? inputType,
        BoundType returnType,
        BoundType? expectedInputType,
        BoundType expectedReturnType,
        BoundFunctionKind kind)
    {
        if (inputType != expectedInputType || returnType != expectedReturnType)
        {
            var expectedSignature = expectedInputType is null
                ? "-> " + FormatType(expectedReturnType)
                : FormatType(expectedInputType.Value) + " -> " + FormatType(expectedReturnType);
            throw Error(
                function.Line,
                function.Column,
                $"intrinsic function '{function.Name}' must be {expectedSignature}");
        }

        return kind;
    }

    private void AddGlobalAliases(Dictionary<string, BoundFunction> functions)
    {
        AddGlobalAlias(functions, "print", "sys.io.print");
        AddGlobalAlias(functions, "println", "sys.io.println");
        AddGlobalAlias(functions, "readInt", "sys.io.readInt");
        AddGlobalAlias(functions, "seedRandom", "sys.random.seed");
        AddGlobalAlias(functions, "randomBelow", "sys.random.below");
        AddGlobalAlias(functions, "openIntWriter", "sys.file.openIntWriter");
        AddGlobalAlias(functions, "writeInt", "sys.file.writeInt");
        AddGlobalAlias(functions, "closeIntWriter", "sys.file.closeIntWriter");
        AddGlobalAlias(functions, "openIntReader", "sys.file.openIntReader");
        AddGlobalAlias(functions, "closestInt", "sys.file.closestInt");
        AddGlobalAlias(functions, "closeIntReader", "sys.file.closeIntReader");
        AddGlobalAlias(functions, "nowMillis", "sys.time.nowMillis");
    }

    private void AddGlobalAlias(
        Dictionary<string, BoundFunction> functions,
        string alias,
        string target)
    {
        if (!functions.TryGetValue(target, out var function))
        {
            throw Error(0, 0, $"standard library function '{target}' was not loaded");
        }

        if (functions.ContainsKey(alias))
        {
            throw Error(function.Line, function.Column, $"global import alias '{alias}' conflicts with an existing function");
        }

        functions.Add(alias, function);
    }

    private IReadOnlyDictionary<string, BoundType> BindMain(IReadOnlyDictionary<string, BoundFunction> functions)
    {
        _currentModuleName = string.Join('.', _program.NamespacePath);
        _currentTypeScopeName = null;
        _currentFunctionReturnType = null;
        _currentMoveInputName = null;
        _currentFunctionOuterBindings = null;
        _currentFunctionAllowsEarlyReturn = false;
        _currentFunctionIsAsync = true;
        var bindings = new Dictionary<string, BoundType>(StringComparer.Ordinal);
        var mutableBindings = new HashSet<string>(StringComparer.Ordinal);
        BindStatements(_program.Statements, functions, bindings, mutableBindings);
        return bindings;
    }

    private void BindStatements(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings,
        HashSet<string>? mutableBindings = null,
        BoundType? yieldInputType = null,
        bool allowContainerBindings = true)
    {
        mutableBindings ??= new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BindingStatement binding:
                    ValidateBindingName(binding.Name, binding.Line, binding.Column);
                    var reboundType = default(BoundType);
                    var isMutableRebind = binding.IsMutable
                        && bindings.TryGetValue(binding.Name, out reboundType)
                        && mutableBindings.Contains(binding.Name)
                        && !IsContainerType(reboundType);
                    var movedSourceName = GetMoveConsumingContainerSourceName(binding.Value);
                    var movedFieldOwnerName = GetMoveConsumingOwnedFieldOwnerName(binding.Value, bindings);
                    var consumedSourceNames = GetOwnedParameterConsumedSourceNames(binding.Value, functions, bindings);
                    var structFieldSourceNames = GetOwnedStructFieldSourceNames(binding.Value, bindings);
                    if (bindings.ContainsKey(binding.Name)
                        && !isMutableRebind
                        && !string.Equals(binding.Name, movedSourceName, StringComparison.Ordinal)
                        && !string.Equals(binding.Name, movedFieldOwnerName, StringComparison.Ordinal)
                        && !consumedSourceNames.Contains(binding.Name, StringComparer.Ordinal)
                        && !structFieldSourceNames.Contains(binding.Name, StringComparer.Ordinal))
                    {
                        throw Error(binding.Line, binding.Column, $"binding '{binding.Name}' already exists in this scope");
                    }

                    var valueType = InferExpression(
                        binding.Value,
                        functions,
                        bindings,
                        allowPrintCall: false,
                        allowReadIntCall: true,
                        allowFlowBindingTarget: false,
                        yieldInputType: yieldInputType,
                        mutableBindings: mutableBindings);
                    if (valueType == BoundType.Unit)
                    {
                        throw Error(binding.Line, binding.Column, "cannot bind a unit value");
                    }
                    if (isMutableRebind && valueType != reboundType)
                    {
                        throw Error(binding.Line, binding.Column,
                            $"mutable rebind of '{binding.Name}' requires {FormatType(reboundType)} but received {FormatType(valueType)}");
                    }
                    if (valueType == BoundType.MutableMappedBytes && !binding.IsMutable)
                    {
                        throw Error(binding.Line, binding.Column,
                            "map write requires a mutable owner binding; use '=> name!'");
                    }

                    if (IsContainerType(valueType))
                    {
                        if (!allowContainerBindings)
                        {
                            throw Error(
                                binding.Line,
                                binding.Column,
                                "owned containers can only be bound in a scope where the compiler can insert deterministic drops");
                        }

                        if (!IsContainerCreationExpression(binding.Value)
                            && movedFieldOwnerName is null)
                        {
                            throw Error(
                                binding.Line,
                                binding.Column,
                                "owned container values must be created directly at their binding site in the current slice");
                        }
                    }

                    if (movedSourceName is not null)
                    {
                        bindings.Remove(movedSourceName);
                        mutableBindings.Remove(movedSourceName);
                    }
                    if (movedFieldOwnerName is not null)
                    {
                        bindings.Remove(movedFieldOwnerName);
                        mutableBindings.Remove(movedFieldOwnerName);
                    }

                    ValidateOwnedParameterConsumptionExpression(binding.Value, functions);
                    foreach (var consumedName in consumedSourceNames)
                    {
                        bindings.Remove(consumedName);
                        mutableBindings.Remove(consumedName);
                    }
                    foreach (var transferredName in structFieldSourceNames)
                    {
                        bindings.Remove(transferredName);
                        mutableBindings.Remove(transferredName);
                    }

                    if (!isMutableRebind)
                    {
                        bindings.Add(binding.Name, valueType);
                    }
                    if (binding.IsMutable)
                    {
                        mutableBindings.Add(binding.Name);
                    }

                    break;
                case IndexAssignmentStatement assignment:
                    BindIndexAssignment(assignment, functions, bindings, mutableBindings, yieldInputType);
                    break;
                case FieldAssignmentStatement assignment:
                    BindFieldAssignment(assignment, functions, bindings, mutableBindings, yieldInputType);
                    break;
                case BlockFunctionCallStatement blockFunctionCall:
                    BindBlockFunctionCall(blockFunctionCall, functions, bindings, mutableBindings, yieldInputType);
                    break;
                case LoopControlStatement loopControl:
                    if (_loopDepth == 0)
                    {
                        throw Error(
                            loopControl.Line,
                            loopControl.Column,
                            $"'{loopControl.Kind.ToString().ToLowerInvariant()}' is only valid inside a loop");
                    }
                    break;
                case GuardLoopControlStatement guardLoopControl:
                    if (_loopDepth == 0)
                    {
                        throw Error(
                            guardLoopControl.Line,
                            guardLoopControl.Column,
                            $"'{guardLoopControl.Kind.ToString().ToLowerInvariant()}' guard is only valid inside a loop");
                    }
                    var guardType = InferExpression(
                        guardLoopControl.Condition,
                        functions,
                        bindings,
                        allowPrintCall: false,
                        allowReadIntCall: true,
                        allowFlowBindingTarget: false,
                        yieldInputType: yieldInputType,
                        mutableBindings: mutableBindings);
                    if (guardType != BoundType.Bool)
                    {
                        throw Error(
                            guardLoopControl.Condition.Line,
                            guardLoopControl.Condition.Column,
                            $"loop-control guard requires Bool but received {FormatType(guardType)}");
                    }
                    ValidateOwnedParameterConsumptionExpression(guardLoopControl.Condition, functions);
                    break;
                case ReturnStatement returnStatement:
                    if (_currentFunctionReturnType is null)
                    {
                        throw Error(
                            returnStatement.Line,
                            returnStatement.Column,
                            "'return' is only valid inside a value or Unit function");
                    }
                    if (_currentFunctionIsAsync)
                    {
                        throw Error(
                            returnStatement.Line,
                            returnStatement.Column,
                            "explicit return in an async function is not supported in the first runtime slice");
                    }
                    if (!_currentFunctionAllowsEarlyReturn)
                    {
                        throw Error(
                            returnStatement.Line,
                            returnStatement.Column,
                            "explicit return from an inline local or block function is not supported yet");
                    }

                    var returnType = returnStatement.Value is null
                        ? BoundType.Unit
                        : InferExpression(
                            returnStatement.Value,
                            functions,
                            bindings,
                            allowPrintCall: false,
                            allowReadIntCall: true,
                            allowFlowBindingTarget: false,
                            yieldInputType: yieldInputType,
                            mutableBindings: mutableBindings,
                            allowedOwnedOuterResultName: _currentMoveInputName);
                    if (returnType != _currentFunctionReturnType.Value)
                    {
                        throw Error(
                            returnStatement.Line,
                            returnStatement.Column,
                            $"return requires {FormatType(_currentFunctionReturnType.Value)} but received {FormatType(returnType)}");
                    }

                    if (returnStatement.Value is not null)
                    {
                        ValidateOwnedParameterConsumptionExpression(returnStatement.Value, functions);
                        if (IsContainerType(returnType))
                        {
                            EnsureOwnedContainerCanLeaveBlock(
                                returnStatement.Value,
                                _currentFunctionOuterBindings
                                    ?? throw new SmallLangException("missing function return ownership scope"),
                                bindings,
                                _currentMoveInputName);
                        }
                    }
                    return;
                case ExpressionStatement expressionStatement:
                    var movedExpressionSourceName = GetMoveConsumingContainerSourceName(
                        expressionStatement.Expression);
                    var effect = InferExpressionStatement(expressionStatement.Expression, functions, bindings, mutableBindings, yieldInputType);
                    if (effect is FlowBindingEffect bindingEffect)
                    {
                        ValidateBindingName(
                            bindingEffect.Name,
                            expressionStatement.Expression.Line,
                            expressionStatement.Expression.Column);
                        if (bindings.ContainsKey(bindingEffect.Name))
                        {
                            throw Error(
                                expressionStatement.Expression.Line,
                                expressionStatement.Expression.Column,
                                $"binding '{bindingEffect.Name}' already exists in this scope");
                        }

                        bindings.Add(bindingEffect.Name, bindingEffect.Type);
                    }

                    ValidateOwnedParameterConsumptionExpression(expressionStatement.Expression, functions);
                    foreach (var consumedName in GetOwnedParameterConsumedSourceNames(
                        expressionStatement.Expression,
                        functions,
                        bindings))
                    {
                        bindings.Remove(consumedName);
                        mutableBindings.Remove(consumedName);
                    }
                    if (movedExpressionSourceName is not null)
                    {
                        bindings.Remove(movedExpressionSourceName);
                        mutableBindings.Remove(movedExpressionSourceName);
                    }

                    break;
                default:
                    throw new SmallLangException($"unsupported statement {statement.GetType().Name}");
            }
        }
    }

    private void BindFieldAssignment(
        FieldAssignmentStatement assignment,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings,
        HashSet<string> mutableBindings,
        BoundType? yieldInputType)
    {
        if (!bindings.TryGetValue(assignment.Name, out var targetType))
        {
            throw Error(assignment.Line, assignment.Column, $"unknown binding '{assignment.Name}'");
        }

        if (!_types.IsStruct(targetType))
        {
            throw Error(assignment.Line, assignment.Column, "field assignment expects a struct owner");
        }

        if (!mutableBindings.Contains(assignment.Name))
        {
            throw Error(
                assignment.Line,
                assignment.Column,
                $"field assignment requires a mutable owner binding; use '{assignment.Name.TrimEnd('!')}!'");
        }

        var definition = _types.GetStruct(targetType);
        var field = definition.Fields.FirstOrDefault(candidate => candidate.Name == assignment.FieldName)
            ?? throw Error(
                assignment.Line,
                assignment.Column,
                $"struct '{definition.Name}' has no field '{assignment.FieldName}'");
        var valueType = InferExpression(
            assignment.Value,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall: true,
            allowFlowBindingTarget: false,
            yieldInputType: yieldInputType,
            mutableBindings: mutableBindings);
        if (valueType != field.Type)
        {
            throw Error(
                assignment.Value.Line,
                assignment.Value.Column,
                $"field '{definition.Name}.{field.Name}' expects {FormatType(field.Type)}, got {FormatType(valueType)}");
        }

        var transferred = new HashSet<string>(StringComparer.Ordinal);
        var movedSourceName = GetMoveConsumingContainerSourceName(assignment.Value);
        if (movedSourceName is not null)
        {
            transferred.Add(movedSourceName);
        }
        foreach (var consumedName in GetOwnedParameterConsumedSourceNames(
                     assignment.Value, functions, bindings))
        {
            transferred.Add(consumedName);
        }
        foreach (var fieldSourceName in GetOwnedStructFieldSourceNames(
                     assignment.Value, bindings))
        {
            transferred.Add(fieldSourceName);
        }

        if (_types.ContainsOwnedStorage(field.Type)
            && assignment.Value is NameExpression sourceName)
        {
            if (string.Equals(sourceName.Name, assignment.Name, StringComparison.Ordinal))
            {
                throw Error(
                    assignment.Line,
                    assignment.Column,
                    "an owned field cannot be replaced from its containing owner");
            }
            transferred.Add(sourceName.Name);
        }

        if (_types.ContainsOwnedStorage(field.Type)
            && transferred.Count == 0
            && !IsContainerCreationExpression(assignment.Value))
        {
            throw Error(
                assignment.Value.Line,
                assignment.Value.Column,
                $"owned field '{definition.Name}.{field.Name}' requires a fresh value or a named owner transfer");
        }

        foreach (var transferredName in transferred)
        {
            bindings.Remove(transferredName);
            mutableBindings.Remove(transferredName);
        }
    }

    private void BindIndexAssignment(
        IndexAssignmentStatement assignment,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        IReadOnlySet<string> mutableBindings,
        BoundType? yieldInputType)
    {
        if (!bindings.TryGetValue(assignment.Name, out var targetType))
        {
            throw Error(assignment.Line, assignment.Column, $"unknown binding '{assignment.Name}'");
        }

        var isDynamicArray = _types.IsDynamicArray(targetType);
        if (targetType is not (BoundType.StaticIntArray or BoundType.DynamicIntArray or BoundType.IntDictionary
            or BoundType.MutableMappedBytes) && !isDynamicArray)
        {
            throw Error(assignment.Line, assignment.Column, "indexed assignment expects an array or dictionary owner");
        }

        if (!mutableBindings.Contains(assignment.Name))
        {
            throw Error(
                assignment.Line,
                assignment.Column,
                $"indexed assignment requires a mutable owner binding; use '=> {assignment.Name.TrimEnd('!')}!'");
        }

        var indexType = assignment.Index is NumberExpression && targetType == BoundType.MutableMappedBytes
            ? BoundType.UIntSize
            : InferExpression(
            assignment.Index,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall: true,
            allowFlowBindingTarget: false,
            yieldInputType: yieldInputType,
            mutableBindings: mutableBindings);
        var expectedIndexType = targetType == BoundType.MutableMappedBytes ? BoundType.UIntSize : BoundType.Int;
        if (indexType != expectedIndexType)
        {
            throw Error(assignment.Index.Line, assignment.Index.Column,
                $"indexed assignment index must be {FormatType(expectedIndexType)}");
        }

        var valueType = InferExpression(
            assignment.Value,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall: true,
            allowFlowBindingTarget: false,
            yieldInputType: yieldInputType,
            mutableBindings: mutableBindings);
        var expectedValueType = targetType == BoundType.MutableMappedBytes
            ? BoundType.UInt8
            : isDynamicArray
                ? _types.GetDynamicArray(targetType).ElementType
                : BoundType.Int;
        if (isDynamicArray && _types.ContainsOwnedStorage(expectedValueType))
        {
            throw Error(assignment.Line, assignment.Column,
                "indexed replacement of owned array elements is not supported yet; move-aware replacement must drop the previous element");
        }
        if (valueType != expectedValueType)
        {
            throw Error(assignment.Value.Line, assignment.Value.Column,
                $"indexed assignment value must be {FormatType(expectedValueType)}");
        }
    }

    private void BindBlockFunctionCall(
        BlockFunctionCallStatement call,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings,
        HashSet<string> mutableBindings,
        BoundType? yieldInputType)
    {
        var target = string.Join('.', call.Target);
        switch (target)
        {
            case "each":
                BindEachBlockFunctionCall(call, functions, bindings, mutableBindings, yieldInputType);
                return;
            case "eachKey":
                BindDictionaryEachBlockFunctionCall(call, functions, bindings, mutableBindings, yieldInputType, bindKey: true);
                return;
            case "eachValue":
                BindDictionaryEachBlockFunctionCall(call, functions, bindings, mutableBindings, yieldInputType, bindKey: false);
                return;
            case "repeat":
                BindRepeatBlockFunctionCall(call, functions, bindings, mutableBindings, yieldInputType);
                return;
            case "while":
                BindWhileBlockFunctionCall(call, functions, bindings, mutableBindings, yieldInputType);
                return;
            default:
                if (functions.TryGetValue(target, out var function)
                    && function.Kind == BoundFunctionKind.UserBlock)
                {
                    BindUserBlockFunctionCall(call, function, functions, bindings, mutableBindings, target);
                    return;
                }

                throw Error(call.Line, call.Column, $"unknown block function '{target}'");
        }
    }

    private void BindWhileBlockFunctionCall(
        BlockFunctionCallStatement call,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings,
        HashSet<string> mutableBindings,
        BoundType? yieldInputType)
    {
        if (!call.UsesDefaultItemName)
        {
            throw Error(call.Line, call.Column, "while does not bind an iteration item");
        }
        var conditionType = InferExpression(call.Source, functions, bindings,
            allowPrintCall: false, allowReadIntCall: true, allowFlowBindingTarget: false,
            mutableBindings: mutableBindings);
        if (conditionType != BoundType.Bool)
        {
            throw Error(call.Source.Line, call.Source.Column, "while condition must be Bool");
        }
        BindLoopStatements(call.Body, functions,
            new Dictionary<string, BoundType>(bindings, StringComparer.Ordinal),
            new HashSet<string>(mutableBindings, StringComparer.Ordinal),
            yieldInputType,
            allowContainerBindings: true);
    }

    private void BindEachBlockFunctionCall(
        BlockFunctionCallStatement call,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings,
        HashSet<string> mutableBindings,
        BoundType? yieldInputType)
    {
        if (!call.UsesDefaultItemName)
        {
            ValidateBindingName(call.ItemName, call.Line, call.Column);
        }

        if (bindings.ContainsKey(call.ItemName))
        {
            throw Error(call.Line, call.Column, $"binding '{call.ItemName}' already exists in this scope");
        }

        var itemType = BoundType.Int;
        if (call.Source is RangeExpression range)
        {
            var startType = InferExpression(
                range.Start,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall: true,
                allowFlowBindingTarget: false,
                mutableBindings: mutableBindings);
            if (startType != BoundType.Int)
            {
                throw Error(range.Start.Line, range.Start.Column, "range start must be an integer");
            }

            var endType = InferExpression(
                range.End,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall: true,
                allowFlowBindingTarget: false,
                mutableBindings: mutableBindings);
            if (endType != BoundType.Int)
            {
                throw Error(range.End.Line, range.End.Column, "range end must be an integer");
            }
        }
        else
        {
            var sourceType = InferExpression(
                call.Source,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall: true,
                allowFlowBindingTarget: false,
                mutableBindings: mutableBindings);
            itemType = sourceType switch
            {
                BoundType.IntSlice or BoundType.StaticIntArray or BoundType.DynamicIntArray => BoundType.Int,
                BoundType.StaticTextArray => BoundType.Text,
                BoundType.Text => BoundType.CodePoint,
                BoundType.Arguments => BoundType.Text,
                BoundType.MappedBytes or BoundType.MutableMappedBytes => BoundType.UInt8,
                _ when _types.IsStaticArray(sourceType) => _types.GetStaticArray(sourceType).ElementType,
                _ when _types.IsDynamicArray(sourceType) => _types.GetDynamicArray(sourceType).ElementType,
                _ => BoundType.Unit
            };
            if (itemType == BoundType.Unit)
            {
                throw Error(call.Source.Line, call.Source.Column,
                    "each expects a range, Text, array, Arguments, or mapped byte view");
            }
        }

        var bodyBindings = new Dictionary<string, BoundType>(bindings, StringComparer.Ordinal)
        {
            [call.ItemName] = itemType
        };
        BindLoopStatements(
            call.Body,
            functions,
            bodyBindings,
            new HashSet<string>(mutableBindings, StringComparer.Ordinal),
            yieldInputType,
            allowContainerBindings: true);
    }

    private void BindUserBlockFunctionCall(
        BlockFunctionCallStatement call,
        BoundFunction function,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings,
        HashSet<string> mutableBindings,
        string target)
    {
        if (function.InputType is null || function.BlockInputType is null)
        {
            throw Error(call.Line, call.Column, $"block function '{target}' is not callable");
        }

        if (!call.UsesDefaultItemName)
        {
            ValidateBindingName(call.ItemName, call.Line, call.Column);
        }

        if (bindings.ContainsKey(call.ItemName))
        {
            throw Error(call.Line, call.Column, $"binding '{call.ItemName}' already exists in this scope");
        }

        var inputType = InferExpression(
            call.Source,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall: true,
            allowFlowBindingTarget: false,
            mutableBindings: mutableBindings);
        if (inputType != function.InputType.Value)
        {
            throw Error(
                call.Source.Line,
                call.Source.Column,
                $"block function '{target}' expects {FormatType(function.InputType.Value)} but received {FormatType(inputType)}");
        }

        var bodyBindings = new Dictionary<string, BoundType>(bindings, StringComparer.Ordinal)
        {
            [call.ItemName] = function.BlockInputType.Value
        };
        BindStatements(
            call.Body,
            functions,
            bodyBindings,
            new HashSet<string>(mutableBindings, StringComparer.Ordinal),
            allowContainerBindings: true);
    }

    private void BindDictionaryEachBlockFunctionCall(
        BlockFunctionCallStatement call,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings,
        HashSet<string> mutableBindings,
        BoundType? yieldInputType,
        bool bindKey)
    {
        if (!call.UsesDefaultItemName)
        {
            ValidateBindingName(call.ItemName, call.Line, call.Column);
        }
        if (bindings.ContainsKey(call.ItemName))
        {
            throw Error(call.Line, call.Column, $"binding '{call.ItemName}' already exists in this scope");
        }

        var sourceType = InferExpression(
            call.Source,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall: true,
            allowFlowBindingTarget: false,
            mutableBindings: mutableBindings);
        BoundType itemType;
        if (sourceType is BoundType.IntDictionary or BoundType.IntDictionaryView)
        {
            itemType = BoundType.Int;
        }
        else if (_types.IsDictionary(sourceType))
        {
            var dictionary = _types.GetDictionary(sourceType);
            itemType = bindKey ? dictionary.KeyType : dictionary.ValueType;
        }
        else
        {
            throw Error(call.Source.Line, call.Source.Column,
                $"{(bindKey ? "eachKey" : "eachValue")} expects a dictionary input");
        }

        var bodyBindings = new Dictionary<string, BoundType>(bindings, StringComparer.Ordinal)
        {
            [call.ItemName] = itemType
        };
        BindLoopStatements(
            call.Body,
            functions,
            bodyBindings,
            new HashSet<string>(mutableBindings, StringComparer.Ordinal),
            yieldInputType,
            allowContainerBindings: true);
    }

    private void BindLoopStatements(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings,
        HashSet<string> mutableBindings,
        BoundType? yieldInputType = null,
        bool allowContainerBindings = true)
    {
        _loopDepth++;
        try
        {
            BindStatements(
                statements,
                functions,
                bindings,
                mutableBindings,
                yieldInputType,
                allowContainerBindings);
        }
        finally
        {
            _loopDepth--;
        }
    }

    private void BindRepeatBlockFunctionCall(
        BlockFunctionCallStatement call,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings,
        HashSet<string> mutableBindings,
        BoundType? yieldInputType)
    {
        if (!call.UsesDefaultItemName)
        {
            ValidateBindingName(call.ItemName, call.Line, call.Column);
        }

        if (bindings.ContainsKey(call.ItemName))
        {
            throw Error(call.Line, call.Column, $"binding '{call.ItemName}' already exists in this scope");
        }

        var countType = InferExpression(
            call.Source,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall: true,
            allowFlowBindingTarget: false,
            mutableBindings: mutableBindings);
        if (countType != BoundType.Int)
        {
            throw Error(call.Source.Line, call.Source.Column, "repeat expects an integer input");
        }

        var bodyBindings = new Dictionary<string, BoundType>(bindings, StringComparer.Ordinal)
        {
            [call.ItemName] = BoundType.Int
        };
        BindLoopStatements(
            call.Body,
            functions,
            bodyBindings,
            new HashSet<string>(mutableBindings, StringComparer.Ordinal),
            yieldInputType,
            allowContainerBindings: true);
    }

    private FlowEffect InferExpressionStatement(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        IReadOnlySet<string>? mutableBindings = null,
        BoundType? yieldInputType = null)
    {
        if (expression is FlowExpression flow)
        {
            var result = InferFlowExpression(
                flow,
                functions,
                bindings,
                allowReadIntCall: true,
                allowFlowBindingTarget: false,
                yieldInputType: yieldInputType,
                mutableBindings: mutableBindings);
            if (result.Type != BoundType.Unit && result.Effect is NoFlowEffect)
            {
                throw Error(
                    expression.Line,
                    expression.Column,
                    "value-flow expression statements must end in a unit-producing call or bind their result with '=>'");
            }

            return result.Effect;
        }

        var expressionType = InferExpression(
            expression,
            functions,
            bindings,
            allowPrintCall: true,
            allowReadIntCall: true,
            allowFlowBindingTarget: false,
            yieldInputType: yieldInputType,
            mutableBindings: mutableBindings);
        if (expressionType != BoundType.Unit)
        {
            throw Error(
                expression.Line,
                expression.Column,
                "only function calls with side effects are valid expression statements");
        }

        return FlowEffect.None;
    }

    private BoundType InferExpression(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowPrintCall,
        bool allowReadIntCall,
        bool allowFlowBindingTarget,
        BoundType? yieldInputType = null,
        IReadOnlySet<string>? mutableBindings = null,
        string? allowedOwnedOuterResultName = null)
    {
        return expression switch
        {
            StringExpression str => InferStringExpression(str, functions, bindings, allowReadIntCall),
            NumberExpression number => number.Text.Contains('.', StringComparison.Ordinal)
                || number.Text.Contains('e', StringComparison.OrdinalIgnoreCase)
                ? BoundType.Float32
                : BoundType.Int,
            BoolExpression => BoundType.Bool,
            NameExpression name => InferNameExpression(name, functions, bindings, allowReadIntCall),
            ArrayLiteralExpression array => InferArrayLiteralExpression(array, functions, bindings, allowReadIntCall),
            TypeApplicationExpression application => InferTypeApplicationExpression(application, functions, allowReadIntCall),
            ArrayRepeatExpression repeat => InferArrayRepeatExpression(repeat, functions, bindings, allowReadIntCall),
            TypedEmptyArrayExpression typedArray => InferTypedEmptyArrayExpression(typedArray),
            DictionaryLiteralExpression dictionary => InferDictionaryLiteralExpression(dictionary, functions, bindings, allowReadIntCall),
            TypedEmptyDictionaryExpression typedDictionary => InferTypedEmptyDictionaryExpression(typedDictionary),
            IndexExpression index => InferIndexExpression(index, functions, bindings, allowReadIntCall),
            StructLiteralExpression literal => InferStructLiteralExpression(literal, functions, bindings, allowReadIntCall),
            FieldAccessExpression field => InferFieldAccessExpression(field, functions, bindings, allowReadIntCall),
            TryExpression attempt => InferTryExpression(attempt, functions, bindings, allowReadIntCall),
            BoxExpression box => InferBoxExpression(box, functions, bindings, allowReadIntCall),
            MapExpression mapping => InferMapExpression(mapping, functions, bindings, allowReadIntCall),
            AddExpression add => InferAddExpression(add, functions, bindings, allowReadIntCall),
            SubtractExpression subtract => InferSubtractExpression(subtract, functions, bindings, allowReadIntCall),
            MultiplyExpression multiply => InferMultiplyExpression(multiply, functions, bindings, allowReadIntCall),
            DivideExpression divide => InferDivideExpression(divide, functions, bindings, allowReadIntCall),
            ModuloExpression modulo => InferModuloExpression(modulo, functions, bindings, allowReadIntCall),
            NegateExpression negate => InferNegateExpression(negate, functions, bindings, allowReadIntCall),
            CompareExpression compare => InferCompareExpression(compare, functions, bindings, allowReadIntCall),
            AndExpression and => InferLogicalExpression(and.Left, and.Right, functions, bindings, allowReadIntCall, "and"),
            OrExpression or => InferLogicalExpression(or.Left, or.Right, functions, bindings, allowReadIntCall, "or"),
            NotExpression not => InferNotExpression(not, functions, bindings, allowReadIntCall),
            IfExpression conditional => InferIfExpression(
                conditional,
                functions,
                bindings,
                allowReadIntCall,
                allowedOwnedOuterResultName,
                mutableBindings),
            WhenExpression whenExpression => InferWhenExpression(
                whenExpression,
                functions,
                bindings,
                allowReadIntCall,
                allowedOwnedOuterResultName),
            EnumMatchExpression enumMatch => InferEnumMatchExpression(
                enumMatch,
                functions,
                bindings,
                allowReadIntCall,
                allowedOwnedOuterResultName),
            EnumPatternExpression => throw Error(
                expression.Line,
                expression.Column,
                "enum patterns are only valid in a subject when arm"),
            SubjectCompareExpression => throw Error(
                expression.Line,
                expression.Column,
                "subject comparison is only valid inside value-flow when"),
            SubjectRangeExpression => throw Error(
                expression.Line,
                expression.Column,
                "subject range is only valid inside value-flow when"),
            FoldExpression fold => InferFoldExpression(fold, functions, bindings, allowReadIntCall),
            RangeExpression => throw Error(expression.Line, expression.Column, "range values are only valid as block-function input"),
            CallExpression call => InferCallExpression(call, functions, bindings, allowPrintCall, allowReadIntCall, mutableBindings),
            FlowExpression flow => InferFlowExpression(
                flow,
                functions,
                bindings,
                allowReadIntCall,
                allowFlowBindingTarget,
                yieldInputType,
                mutableBindings).Type,
            _ => throw Error(expression.Line, expression.Column, "expected an expression value")
        };
    }

    private BoundType InferStringExpression(
        StringExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        foreach (var segment in expression.Segments)
        {
            if (segment is not InterpolationSegment interpolation)
            {
                continue;
            }

            var interpolationType = InferExpression(
                interpolation.Expression,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            EnsureDisplayable(interpolationType, expression.Line, expression.Column, "string interpolation");
        }

        return BoundType.Text;
    }

    private BoundType InferArrayLiteralExpression(
        ArrayLiteralExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        BoundType? inferredElementType = expression.ElementType is null
            ? null
            : ParseType(expression.ElementType, expression.Line, expression.Column);
        foreach (var element in expression.Elements)
        {
            var elementType = element is DictionaryLiteralExpression contextual
                && inferredElementType is { } contextualElementType
                && _types.IsStruct(contextualElementType)
                    ? InferContextualStructLiteral(
                        contextual,
                        contextualElementType,
                        functions,
                        bindings,
                        allowReadIntCall)
                    : InferExpression(
                        element,
                        functions,
                        bindings,
                        allowPrintCall: false,
                        allowReadIntCall,
                        allowFlowBindingTarget: false);
            if (elementType == BoundType.Unit
                || elementType is BoundType.IntSlice
                    or BoundType.StaticIntArray
                    or BoundType.StaticTextArray
                    or BoundType.DynamicIntArray
                    or BoundType.IntDictionaryView
                    or BoundType.IntDictionary
                || _types.IsStaticArray(elementType))
            {
                throw Error(element.Line, element.Column, "fixed array elements must be inline scalar or user values");
            }
            if (inferredElementType is not null && inferredElementType != elementType)
            {
                throw Error(
                    element.Line,
                    element.Column,
                    $"array elements must have one type; expected {FormatType(inferredElementType.Value)}, got {FormatType(elementType)}");
            }
            inferredElementType = elementType;
        }

        if (expression.IsDynamic)
        {
            return inferredElementType switch
            {
                null or BoundType.Int => BoundType.DynamicIntArray,
                var elementType => _types.GetOrAddDynamicArray(elementType.Value)
            };
        }

        return inferredElementType switch
        {
            null or BoundType.Int => BoundType.StaticIntArray,
            BoundType.Text => BoundType.StaticTextArray,
            _ => _types.GetOrAddStaticArray(inferredElementType.Value)
        };
    }

    private BoundType InferArrayRepeatExpression(
        ArrayRepeatExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        var valueType = InferExpression(
            expression.Value,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (valueType != BoundType.Int)
        {
            throw Error(expression.Value.Line, expression.Value.Column, "array repeat value must be Int");
        }

        if (expression.CountParameterName is { } countParameterName
            && (!bindings.TryGetValue(countParameterName, out var countType) || countType != BoundType.Int))
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"unknown compile-time Int value parameter '{countParameterName}'");
        }

        return BoundType.StaticIntArray;
    }

    private BoundType InferTypedEmptyArrayExpression(TypedEmptyArrayExpression expression)
    {
        if (expression.ElementType == "Int")
        {
            return BoundType.DynamicIntArray;
        }
        var elementType = ParseType(expression.ElementType, expression.Line, expression.Column);
        if (elementType == BoundType.Unit
            || IsNestedContainerElementType(elementType))
        {
            throw Error(expression.Line, expression.Column, "growable array elements must be inline scalar or user values");
        }
        return _types.GetOrAddDynamicArray(elementType);
    }

    private BoundType InferDictionaryLiteralExpression(
        DictionaryLiteralExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        BoundType? inferredKeyType = expression.KeyType is null
            ? null
            : ParseType(expression.KeyType, expression.Line, expression.Column);
        BoundType? inferredValueType = expression.ValueType is null
            ? null
            : ParseType(expression.ValueType, expression.Line, expression.Column);
        if (inferredKeyType is { } declaredKey && !IsSupportedDictionaryKeyType(declaredKey))
        {
            throw Error(expression.Line, expression.Column,
                $"dictionary key type {FormatType(declaredKey)} must implement Hash.hash: self -> Int and Eq.eq: self -> Int");
        }
        foreach (var entry in expression.Entries)
        {
            var keyType = entry.Key is DictionaryLiteralExpression contextual
                && inferredKeyType is { } contextualKeyType
                && _types.IsStruct(contextualKeyType)
                    ? InferContextualStructLiteral(
                        contextual,
                        contextualKeyType,
                        functions,
                        bindings,
                        allowReadIntCall)
                    : InferExpression(
                        entry.Key,
                        functions,
                        bindings,
                        allowPrintCall: false,
                        allowReadIntCall,
                        allowFlowBindingTarget: false);
            if (!IsSupportedDictionaryKeyType(keyType))
            {
                throw Error(entry.Key.Line, entry.Key.Column,
                    $"dictionary key type {FormatType(keyType)} must implement Hash.hash: self -> Int and Eq.eq: self -> Int");
            }
            if (inferredKeyType is { } expectedKey && keyType != expectedKey)
            {
                throw Error(entry.Key.Line, entry.Key.Column,
                    $"dictionary keys must have one type; expected {FormatType(expectedKey)}, got {FormatType(keyType)}");
            }
            inferredKeyType ??= keyType;

            var valueType = entry.Value is DictionaryLiteralExpression contextualValue
                && inferredValueType is { } contextualValueType
                && _types.IsStruct(contextualValueType)
                    ? InferContextualStructLiteral(
                        contextualValue,
                        contextualValueType,
                        functions,
                        bindings,
                        allowReadIntCall)
                    : InferExpression(
                        entry.Value,
                        functions,
                        bindings,
                        allowPrintCall: false,
                        allowReadIntCall,
                        allowFlowBindingTarget: false);
            if (valueType == BoundType.Unit)
            {
                throw Error(entry.Value.Line, entry.Value.Column, "dictionary values cannot be Unit");
            }
            if (inferredValueType is { } expectedValue && valueType != expectedValue)
            {
                throw Error(entry.Value.Line, entry.Value.Column,
                    $"dictionary values must have one type; expected {FormatType(expectedValue)}, got {FormatType(valueType)}");
            }
            inferredValueType ??= valueType;
        }

        var key = inferredKeyType ?? throw Error(expression.Line, expression.Column, "dictionary literal requires at least one entry");
        var value = inferredValueType!.Value;
        return key == BoundType.Int && value == BoundType.Int
            ? BoundType.IntDictionary
            : _types.GetOrAddDictionary(key, value);
    }

    private BoundType InferTypedEmptyDictionaryExpression(TypedEmptyDictionaryExpression expression)
    {
        var keyType = ParseType(expression.KeyType, expression.Line, expression.Column);
        var valueType = ParseType(expression.ValueType, expression.Line, expression.Column);
        if (!IsSupportedDictionaryKeyType(keyType))
        {
            throw Error(expression.Line, expression.Column,
                $"dictionary key type {FormatType(keyType)} must implement Hash.hash: self -> Int and Eq.eq: self -> Int");
        }
        return keyType == BoundType.Int && valueType == BoundType.Int
            ? BoundType.IntDictionary
            : _types.GetOrAddDictionary(keyType, valueType);
    }

    private BoundType InferIndexExpression(
        IndexExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        var sourceType = InferExpression(
            expression.Source,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (sourceType is not (BoundType.IntSlice
            or BoundType.StaticIntArray
            or BoundType.StaticTextArray
            or BoundType.DynamicIntArray
            or BoundType.IntDictionaryView
            or BoundType.IntDictionary
            or BoundType.Arguments
            or BoundType.MappedBytes
            or BoundType.MutableMappedBytes)
            && !_types.IsStaticArray(sourceType)
            && !_types.IsDynamicArray(sourceType)
            && !_types.IsDictionary(sourceType))
        {
            throw Error(expression.Source.Line, expression.Source.Column,
                "indexing expects an array, dictionary, or mapped byte view");
        }

        var expectedIndexType = sourceType is BoundType.MappedBytes or BoundType.MutableMappedBytes or BoundType.Arguments
            ? BoundType.UIntSize
            : _types.IsDictionary(sourceType)
            ? _types.GetDictionary(sourceType).KeyType
            : BoundType.Int;
        var indexType = expression.Index is NumberExpression
                && sourceType is BoundType.MappedBytes or BoundType.MutableMappedBytes or BoundType.Arguments
            ? BoundType.UIntSize
            : expression.Index is DictionaryLiteralExpression contextual
            && _types.IsStruct(expectedIndexType)
                ? InferContextualStructLiteral(contextual, expectedIndexType, functions, bindings, allowReadIntCall)
                : InferExpression(
                    expression.Index,
                    functions,
                    bindings,
                    allowPrintCall: false,
                    allowReadIntCall,
                    allowFlowBindingTarget: false);
        if (indexType != expectedIndexType)
        {
            throw Error(expression.Index.Line, expression.Index.Column,
                $"index must be {FormatType(expectedIndexType)}");
        }

        if (_types.IsStaticArray(sourceType))
        {
            var elementType = _types.GetStaticArray(sourceType).ElementType;
            if (_types.ContainsOwnedStorage(elementType))
            {
                throw Error(
                    expression.Line,
                    expression.Column,
                    $"indexing owned array element type {FormatType(elementType)} requires move extraction, which is not implemented yet");
            }
            return elementType;
        }
        if (sourceType is BoundType.MappedBytes or BoundType.MutableMappedBytes)
        {
            return BoundType.UInt8;
        }
        if (sourceType == BoundType.Arguments)
        {
            return BoundType.Text;
        }
        if (_types.IsDynamicArray(sourceType))
        {
            var elementType = _types.GetDynamicArray(sourceType).ElementType;
            if (_types.ContainsOwnedStorage(elementType))
            {
                throw Error(
                    expression.Line,
                    expression.Column,
                    $"indexing owned array element type {FormatType(elementType)} requires move extraction, which is not implemented yet");
            }
            return elementType;
        }
        if (_types.IsDictionary(sourceType))
        {
            var valueType = _types.GetDictionary(sourceType).ValueType;
            if (_types.ContainsOwnedStorage(valueType))
            {
                throw Error(expression.Line, expression.Column,
                    $"indexing owned dictionary value type {FormatType(valueType)} requires move extraction, which is not implemented yet");
            }
            return valueType;
        }
        return sourceType == BoundType.StaticTextArray ? BoundType.Text : BoundType.Int;
    }

    private BoundType InferContextualStructLiteral(
        DictionaryLiteralExpression expression,
        BoundType expectedType,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        var definition = _types.GetStruct(expectedType);
        var initializers = new Dictionary<string, DictionaryEntryExpression>(StringComparer.Ordinal);
        foreach (var entry in expression.Entries)
        {
            if (entry.Key is not NameExpression name)
            {
                throw Error(entry.Key.Line, entry.Key.Column,
                    $"contextual {definition.Name} literal field name must be an identifier");
            }
            if (!initializers.TryAdd(name.Name, entry))
            {
                throw Error(name.Line, name.Column,
                    $"field '{name.Name}' is initialized more than once");
            }
        }

        foreach (var field in definition.Fields)
        {
            if (!initializers.TryGetValue(field.Name, out var initializer))
            {
                throw Error(expression.Line, expression.Column,
                    $"contextual {definition.Name} literal is missing field '{field.Name}'");
            }
            var actualType = InferExpression(
                initializer.Value,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            if (actualType != field.Type)
            {
                throw Error(initializer.Value.Line, initializer.Value.Column,
                    $"field '{definition.Name}.{field.Name}' expects {FormatType(field.Type)}, got {FormatType(actualType)}");
            }
        }

        var unknown = initializers.Keys.FirstOrDefault(name =>
            !definition.Fields.Any(field => field.Name == name));
        if (unknown is not null)
        {
            var entry = initializers[unknown];
            throw Error(entry.Key.Line, entry.Key.Column,
                $"struct '{definition.Name}' has no field '{unknown}'");
        }
        return expectedType;
    }

    private BoundType InferStructLiteralExpression(
        StructLiteralExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        if (!_types.TryResolve(expression.TypeName, out var type) || !_types.IsStruct(type))
        {
            throw Error(expression.Line, expression.Column, $"unknown struct type '{expression.TypeName}'");
        }
        EnsureTypeVisible(type, expression.Line, expression.Column);

        var definition = _types.GetStruct(type);
        var initializers = new Dictionary<string, StructFieldInitializer>(StringComparer.Ordinal);
        foreach (var initializer in expression.Fields)
        {
            if (!initializers.TryAdd(initializer.Name, initializer))
            {
                throw Error(
                    initializer.Line,
                    initializer.Column,
                    $"field '{initializer.Name}' is initialized more than once in '{definition.Name}'");
            }
        }

        foreach (var initializer in expression.Fields)
        {
            var field = definition.Fields.FirstOrDefault(candidate => candidate.Name == initializer.Name);
            if (field is null)
            {
                throw Error(
                    initializer.Line,
                    initializer.Column,
                    $"struct '{definition.Name}' has no field '{initializer.Name}'");
            }

            var actualType = InferExpression(
                initializer.Value,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            if (actualType != field.Type)
            {
                throw Error(
                    initializer.Value.Line,
                    initializer.Value.Column,
                    $"field '{field.Name}' expects {FormatType(field.Type)}, got {FormatType(actualType)}");
            }
        }

        var missing = definition.Fields.FirstOrDefault(field => !initializers.ContainsKey(field.Name));
        if (missing is not null)
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"struct '{definition.Name}' requires field '{missing.Name}' to be initialized");
        }

        return type;
    }

    private BoundType InferFieldAccessExpression(
        FieldAccessExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        if (expression.Source is NameExpression functionOwner
            && !bindings.ContainsKey(functionOwner.Name)
            && functions.TryGetValue(functionOwner.Name + "." + expression.FieldName, out var zeroArgumentFunction)
            && zeroArgumentFunction.InputType is null)
        {
            EnsureFunctionVisible(zeroArgumentFunction, expression.Line, expression.Column);
            if (IsMainOnlyRuntimeWrapper(zeroArgumentFunction) && !allowReadIntCall)
            {
                throw Error(expression.Line, expression.Column,
                    $"{zeroArgumentFunction.Name} is only valid in main for the current runtime slice");
            }
            return zeroArgumentFunction.ReturnType;
        }

        if (expression.Source is NameExpression genericTypeName
            && !bindings.ContainsKey(genericTypeName.Name)
            && (genericTypeName.Name.StartsWith("Option<", StringComparison.Ordinal)
                || genericTypeName.Name.StartsWith("Result<", StringComparison.Ordinal)))
        {
            var genericEnumType = ParseType(genericTypeName.Name, expression.Line, expression.Column);
            var enumeration = _types.GetEnum(genericEnumType);
            var variant = enumeration.Variants.FirstOrDefault(candidate => candidate.Name == expression.FieldName)
                ?? throw Error(expression.Line, expression.Column,
                    $"enum '{FormatType(genericEnumType)}' has no variant '{expression.FieldName}'");
            if (variant.PayloadType is not null)
            {
                throw Error(expression.Line, expression.Column,
                    $"variant '{FormatType(genericEnumType)}.{variant.Name}' requires a payload argument");
            }
            return genericEnumType;
        }
        if (expression.Source is NameExpression typeName
            && !bindings.ContainsKey(typeName.Name)
            && _types.TryResolve(typeName.Name, out var enumType)
            && _types.IsEnum(enumType))
        {
            EnsureTypeVisible(enumType, expression.Line, expression.Column);
            var enumeration = _types.GetEnum(enumType);
            var variant = enumeration.Variants.FirstOrDefault(candidate => candidate.Name == expression.FieldName)
                ?? throw Error(
                    expression.Line,
                    expression.Column,
                    $"enum '{enumeration.Name}' has no variant '{expression.FieldName}'");
            if (variant.PayloadType is not null)
            {
                throw Error(
                    expression.Line,
                    expression.Column,
                    $"variant '{enumeration.Name}.{variant.Name}' requires a payload argument");
            }

            return enumType;
        }

        if (expression.Source is NameExpression staticTypeName
            && !bindings.ContainsKey(staticTypeName.Name)
            && _types.TryResolve(staticTypeName.Name, out var staticType)
            && _types.IsStruct(staticType))
        {
            EnsureTypeVisible(staticType, expression.Line, expression.Column);
            var memberPath = staticTypeName.Name + "." + expression.FieldName;
            if (functions.TryGetValue(memberPath, out var associated) && associated.InputType is null)
            {
                return associated.ReturnType;
            }

            throw Error(
                expression.Line,
                expression.Column,
                $"type '{staticTypeName.Name}' has no zero-argument associated member '{expression.FieldName}'");
        }

        var sourceType = InferExpression(
            expression.Source,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (_types.IsBox(sourceType))
        {
            sourceType = _types.GetBox(sourceType).ElementType;
        }
        if (!_types.IsStruct(sourceType))
        {
            throw Error(expression.Line, expression.Column, "field access expects a struct value");
        }

        var definition = _types.GetStruct(sourceType);
        var field = definition.Fields.FirstOrDefault(candidate => candidate.Name == expression.FieldName);
        if (field is not null)
        {
            return field.Type;
        }

        if (TryResolveInstanceMethod(sourceType, expression.FieldName, functions, out var method)
            && method.InputOwnership == BoundFunctionInputOwnership.Default)
        {
            return method.ReturnType;
        }

        throw Error(
            expression.Line,
            expression.Column,
            $"struct '{definition.Name}' has no field or readonly computed member '{expression.FieldName}'");
    }

    private BoundType InferTryExpression(
        TryExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        var operandType = InferExpression(
            expression.Value,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (!_types.TryGetResultTypes(operandType, out var operandResult))
        {
            throw Error(expression.Line, expression.Column,
                $"'?' expects Result<T, E> but received {FormatType(operandType)}");
        }
        if (_currentFunctionReturnType is null
            || !_types.TryGetResultTypes(_currentFunctionReturnType.Value, out var outerResult))
        {
            throw Error(expression.Line, expression.Column,
                "'?' can only be used inside a function returning Result<T, E>");
        }
        if (operandResult.Error != outerResult.Error)
        {
            throw Error(expression.Line, expression.Column,
                $"'?' error type {FormatType(operandResult.Error)} does not match function error type {FormatType(outerResult.Error)}");
        }
        if ((_types.ContainsOwnedStorage(operandResult.Ok)
                || _types.ContainsOwnedStorage(operandResult.Error))
            && !IsConsumableOwnedResultExpression(expression.Value, functions, bindings))
        {
            throw Error(expression.Line, expression.Column,
                "owned Result '?' must consume a temporary Result or the function's explicit move input");
        }
        return operandResult.Ok;
    }

    private bool IsConsumableOwnedResultExpression(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings)
    {
        if (expression is CallExpression)
        {
            return true;
        }
        if (expression is NameExpression name)
        {
            return !bindings.ContainsKey(name.Name)
                || string.Equals(name.Name, _currentMoveInputName, StringComparison.Ordinal);
        }
        if (expression is FieldAccessExpression field
            && field.Source is NameExpression owner
            && functions.TryGetValue(owner.Name + "." + field.FieldName, out var function)
            && function.InputType is null)
        {
            return true;
        }
        return false;
    }

    private BoundType InferBoxExpression(
        BoxExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        var elementType = InferExpression(
            expression.Value,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        var box = _types.Boxes.FirstOrDefault(candidate => candidate.ElementType == elementType)
            ?? throw Error(
                expression.Line,
                expression.Column,
                $"type {FormatType(elementType)} cannot be boxed in this slice");
        return box.Id;
    }

    private BoundType InferMapExpression(
        MapExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        var pathType = InferExpression(expression.Path, functions, bindings,
            allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
        if (pathType != BoundType.Text)
        {
            throw Error(expression.Path.Line, expression.Path.Column, "map path must be Text");
        }
        ValidateMapContextValue(expression.Offset, BoundType.UInt64, "map offset", functions, bindings, allowReadIntCall);
        ValidateMapContextValue(expression.Length, BoundType.UIntSize, "map length", functions, bindings, allowReadIntCall);
        ValidateMapContextValue(expression.FileSize, BoundType.UInt64, "mapped file size", functions, bindings, allowReadIntCall);
        if (expression.Mode == MapAccessMode.Read && expression.FileSize is not null)
        {
            throw Error(expression.FileSize.Line, expression.FileSize.Column,
                "map read does not accept a file size");
        }
        return expression.Mode == MapAccessMode.Write
            ? BoundType.MutableMappedBytes
            : BoundType.MappedBytes;
    }

    private void ValidateMapContextValue(
        Expression? expression,
        BoundType expectedType,
        string role,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        if (expression is null || expression is NumberExpression)
        {
            return;
        }
        var actualType = InferExpression(expression, functions, bindings,
            allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
        if (actualType != expectedType)
        {
            throw Error(expression.Line, expression.Column,
                $"{role} must be {FormatType(expectedType)} or an integer literal");
        }
    }

    private BoundType InferAddExpression(
        AddExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        return InferIntegerBinaryExpression(expression.Left, expression.Right, functions, bindings, allowReadIntCall, "+");
    }

    private BoundType InferMultiplyExpression(
        MultiplyExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        return InferIntegerBinaryExpression(expression.Left, expression.Right, functions, bindings, allowReadIntCall, "*");
    }

    private BoundType InferSubtractExpression(
        SubtractExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        return InferIntegerBinaryExpression(expression.Left, expression.Right, functions, bindings, allowReadIntCall, "-");
    }

    private BoundType InferDivideExpression(
        DivideExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        return InferIntegerBinaryExpression(expression.Left, expression.Right, functions, bindings, allowReadIntCall, "/");
    }

    private BoundType InferModuloExpression(
        ModuloExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        return InferIntegerBinaryExpression(expression.Left, expression.Right, functions, bindings, allowReadIntCall, "%");
    }

    private BoundType InferNegateExpression(
        NegateExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        var value = InferExpression(
            expression.Value,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (!IsSignedIntegerType(value) && !IsFloatType(value))
        {
            throw Error(expression.Value.Line, expression.Value.Column, "operand of unary '-' must be a signed numeric value");
        }

        return value;
    }

    private BoundType InferIntegerBinaryExpression(
        Expression leftExpression,
        Expression rightExpression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        string operatorText)
    {
        var left = InferExpression(
            leftExpression,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        var right = InferExpression(
            rightExpression,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (left == BoundType.CodePoint)
        {
            throw Error(leftExpression.Line, leftExpression.Column,
                $"CodePoint does not support '{operatorText}'; convert it to UInt32 before arithmetic");
        }
        if (!IsNumericType(left) || (operatorText == "%" && !IsIntegerType(left)))
        {
            throw Error(leftExpression.Line, leftExpression.Column, $"left operand of '{operatorText}' must be a compatible numeric value");
        }

        if (right != left)
        {
            throw Error(rightExpression.Line, rightExpression.Column,
                $"operands of '{operatorText}' must have the same numeric type; left is {FormatType(left)}, right is {FormatType(right)}");
        }

        return left;
    }

    private BoundType InferCompareExpression(
        CompareExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        var left = InferExpression(
            expression.Left,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        var right = InferExpression(
            expression.Right,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);

        if (!IsNumericType(left))
        {
            throw Error(expression.Left.Line, expression.Left.Column, "left operand of comparison must be numeric");
        }

        if (right != left)
        {
            throw Error(expression.Right.Line, expression.Right.Column,
                $"comparison operands must have the same numeric type; left is {FormatType(left)}, right is {FormatType(right)}");
        }

        return BoundType.Bool;
    }

    private BoundType InferLogicalExpression(
        Expression leftExpression,
        Expression rightExpression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        string operatorText)
    {
        var left = InferExpression(
            leftExpression,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        var right = InferExpression(
            rightExpression,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);

        if (left != BoundType.Bool)
        {
            throw Error(leftExpression.Line, leftExpression.Column, $"left operand of '{operatorText}' must be Bool");
        }

        if (right != BoundType.Bool)
        {
            throw Error(rightExpression.Line, rightExpression.Column, $"right operand of '{operatorText}' must be Bool");
        }

        return BoundType.Bool;
    }

    private BoundType InferNotExpression(
        NotExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        var value = InferExpression(
            expression.Value,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (value != BoundType.Bool)
        {
            throw Error(expression.Value.Line, expression.Value.Column, "'not' expects Bool");
        }

        return BoundType.Bool;
    }

    private BoundType InferIfExpression(
        IfExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        string? allowedOwnedOuterResultName = null,
        IReadOnlySet<string>? mutableBindings = null)
    {
        var condition = InferExpression(
            expression.Condition,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (condition != BoundType.Bool)
        {
            throw Error(expression.Condition.Line, expression.Condition.Column, "if expects Bool input");
        }

        var thenType = InferBlockBody(
            expression.Then,
            functions,
            bindings,
            allowReadIntCall,
            allowedOwnedOuterResultName,
            mutableBindings);
        if (expression.Else is null)
        {
            if (thenType != BoundType.Unit)
            {
                throw Error(expression.Line, expression.Column, "if used as a value requires an else block");
            }

            return BoundType.Unit;
        }

        var elseType = InferBlockBody(
            expression.Else,
            functions,
            bindings,
            allowReadIntCall,
            allowedOwnedOuterResultName,
            mutableBindings);
        if (thenType != elseType)
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"if branches must return the same type, got {FormatType(thenType)} and {FormatType(elseType)}");
        }

        return thenType;
    }

    private BoundType InferWhenExpression(
        WhenExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        string? allowedOwnedOuterResultName = null)
    {
        var hasSubjectConditions = expression.Arms.Any(arm => IsSubjectWhenCondition(arm.Condition));
        if (expression.Subject is not null)
        {
            var subjectType = InferExpression(
                expression.Subject,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            if (subjectType != BoundType.Int)
            {
                throw Error(expression.Subject.Line, expression.Subject.Column, "value-flow when subject must be an integer");
            }
        }
        else if (hasSubjectConditions)
        {
            if (!bindings.TryGetValue("it", out var implicitSubject) || implicitSubject != BoundType.Int)
            {
                throw Error(
                    expression.Line,
                    expression.Column,
                    "subject-style when without an explicit subject requires the default integer input 'it'");
            }
        }

        BoundType? resultType = null;
        foreach (var arm in expression.Arms)
        {
            var condition = expression.Subject is null && !hasSubjectConditions
                ? InferExpression(
                    arm.Condition,
                    functions,
                    bindings,
                    allowPrintCall: false,
                    allowReadIntCall,
                    allowFlowBindingTarget: false)
                : InferSubjectWhenCondition(
                    arm.Condition,
                    functions,
                    bindings,
                    allowReadIntCall);
            if (condition != BoundType.Bool)
            {
                throw Error(arm.Condition.Line, arm.Condition.Column, "when arm condition must be Bool");
            }

            var armType = InferBlockBody(
                arm.Body,
                functions,
                bindings,
                allowReadIntCall,
                allowedOwnedOuterResultName);
            resultType ??= armType;
            if (armType != resultType)
            {
                throw Error(
                    arm.Line,
                    arm.Column,
                    $"when arms must return the same type, got {FormatType(resultType.Value)} and {FormatType(armType)}");
            }
        }

        var elseType = InferBlockBody(
            expression.Else,
            functions,
            bindings,
            allowReadIntCall,
            allowedOwnedOuterResultName);
        resultType ??= elseType;
        if (elseType != resultType)
        {
            throw Error(
                expression.Else.Line,
                expression.Else.Column,
                $"when else must return {FormatType(resultType.Value)} but returns {FormatType(elseType)}");
        }

        return resultType.Value;
    }

    private bool IsSubjectWhenCondition(Expression condition)
    {
        return condition is SubjectCompareExpression or SubjectRangeExpression;
    }

    private BoundType InferEnumMatchExpression(
        EnumMatchExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        string? allowedOwnedOuterResultName)
    {
        var subjectType = InferExpression(
            expression.Subject,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (!_types.IsEnum(subjectType))
        {
            throw Error(expression.Subject.Line, expression.Subject.Column, "enum pattern matching expects an enum subject");
        }

        var definition = _types.GetEnum(subjectType);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        BoundType? resultType = null;
        foreach (var arm in expression.Arms)
        {
            var pattern = (EnumPatternExpression)arm.Condition;
            if (!_types.TryResolve(pattern.TypeName, out var patternType)
                && (pattern.TypeName.StartsWith("Option<", StringComparison.Ordinal)
                    || pattern.TypeName.StartsWith("Result<", StringComparison.Ordinal)))
            {
                patternType = ParseType(pattern.TypeName, pattern.Line, pattern.Column);
            }
            if (patternType != subjectType)
            {
                throw Error(
                    pattern.Line,
                    pattern.Column,
                    $"pattern type '{pattern.TypeName}' does not match enum '{definition.Name}'");
            }

            var variant = definition.Variants.FirstOrDefault(candidate => candidate.Name == pattern.VariantName)
                ?? throw Error(
                    pattern.Line,
                    pattern.Column,
                    $"enum '{definition.Name}' has no variant '{pattern.VariantName}'");
            if (!covered.Add(variant.Name))
            {
                throw Error(pattern.Line, pattern.Column, $"variant '{definition.Name}.{variant.Name}' is matched more than once");
            }

            var armBindings = new Dictionary<string, BoundType>(bindings, StringComparer.Ordinal);
            if (variant.PayloadType is { } payloadType)
            {
                if (pattern.BindingName is null)
                {
                    throw Error(
                        pattern.Line,
                        pattern.Column,
                        $"variant '{definition.Name}.{variant.Name}' requires a payload binding");
                }

                ValidateBindingName(pattern.BindingName, pattern.Line, pattern.Column);
                armBindings[pattern.BindingName] = payloadType;
            }
            else if (pattern.BindingName is not null)
            {
                throw Error(
                    pattern.Line,
                    pattern.Column,
                    $"variant '{definition.Name}.{variant.Name}' has no payload to bind");
            }

            var armType = InferBlockBody(
                arm.Body,
                functions,
                armBindings,
                allowReadIntCall,
                allowedOwnedOuterResultName);
            resultType ??= armType;
            if (armType != resultType)
            {
                throw Error(
                    arm.Line,
                    arm.Column,
                    $"enum when arms must return the same type, got {FormatType(resultType.Value)} and {FormatType(armType)}");
            }
        }

        if (expression.Else is null)
        {
            var missing = definition.Variants.Where(variant => !covered.Contains(variant.Name)).ToArray();
            if (missing.Length > 0)
            {
                throw Error(
                    expression.Line,
                    expression.Column,
                    $"non-exhaustive enum when; missing {string.Join(", ", missing.Select(variant => definition.Name + "." + variant.Name))}");
            }
        }
        else
        {
            if (covered.Count == definition.Variants.Count)
            {
                throw Error(expression.Else.Line, expression.Else.Column, "enum when else arm is unreachable because all variants are covered");
            }

            var elseType = InferBlockBody(
                expression.Else,
                functions,
                bindings,
                allowReadIntCall,
                allowedOwnedOuterResultName);
            resultType ??= elseType;
            if (elseType != resultType)
            {
                throw Error(
                    expression.Else.Line,
                    expression.Else.Column,
                    $"enum when else must return {FormatType(resultType.Value)} but returns {FormatType(elseType)}");
            }
        }

        return resultType ?? BoundType.Unit;
    }

    private BoundType InferSubjectWhenCondition(
        Expression condition,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        if (condition is SubjectCompareExpression compare)
        {
            var right = InferExpression(
                compare.Right,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            if (right != BoundType.Int)
            {
                throw Error(compare.Right.Line, compare.Right.Column, "right operand of value-flow when comparison must be an integer");
            }

            return BoundType.Bool;
        }

        if (condition is not SubjectRangeExpression range)
        {
            throw Error(condition.Line, condition.Column, "value-flow when arm must start with a comparison operator or range");
        }

        var start = InferExpression(
            range.Start,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (start != BoundType.Int)
        {
            throw Error(range.Start.Line, range.Start.Column, "range start of value-flow when arm must be an integer");
        }

        var end = InferExpression(
            range.End,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (end != BoundType.Int)
        {
            throw Error(range.End.Line, range.End.Column, "range end of value-flow when arm must be an integer");
        }

        return BoundType.Bool;
    }

    private BoundType InferFoldExpression(
        FoldExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        ValidateBindingName(expression.AccumulatorName, expression.Line, expression.Column);
        ValidateBindingName(expression.ItemName, expression.Line, expression.Column);
        if (expression.AccumulatorName == expression.ItemName)
        {
            throw Error(expression.Line, expression.Column, "fold accumulator and item names must be different");
        }

        if (bindings.ContainsKey(expression.AccumulatorName))
        {
            throw Error(expression.Line, expression.Column, $"binding '{expression.AccumulatorName}' already exists in this scope");
        }

        if (bindings.ContainsKey(expression.ItemName))
        {
            throw Error(expression.Line, expression.Column, $"binding '{expression.ItemName}' already exists in this scope");
        }

        if (expression.Source is RangeExpression range)
        {
            var startType = InferExpression(
                range.Start,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            if (startType != BoundType.Int)
            {
                throw Error(range.Start.Line, range.Start.Column, "range start must be an integer");
            }

            var endType = InferExpression(
                range.End,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            if (endType != BoundType.Int)
            {
                throw Error(range.End.Line, range.End.Column, "range end must be an integer");
            }
        }
        else
        {
            var sourceType = InferExpression(
                expression.Source,
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            if (!IsReadonlyIntViewCompatible(sourceType))
            {
                throw Error(expression.Source.Line, expression.Source.Column, "fold expects a range or Int array input");
            }
        }

        var initialType = InferExpression(
            expression.Initial,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (initialType != BoundType.Int)
        {
            throw Error(expression.Initial.Line, expression.Initial.Column, "fold initial value must be an integer");
        }

        var bodyBindings = new Dictionary<string, BoundType>(bindings, StringComparer.Ordinal)
        {
            [expression.AccumulatorName] = BoundType.Int,
            [expression.ItemName] = BoundType.Int
        };
        var bodyType = InferBlockBody(expression.Body, functions, bodyBindings, allowReadIntCall);
        if (bodyType != BoundType.Int)
        {
            throw Error(expression.Body.Line, expression.Body.Column, "fold body must return the next integer accumulator value");
        }

        return BoundType.Int;
    }

    private BoundType InferBlockBody(
        BlockBody body,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        string? allowedOwnedOuterResultName = null,
        IReadOnlySet<string>? mutableBindings = null)
    {
        var bodyBindings = new Dictionary<string, BoundType>(bindings, StringComparer.Ordinal);
        var bodyMutableBindings = mutableBindings is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(mutableBindings, StringComparer.Ordinal);
        BindStatements(body.Statements, functions, bodyBindings, bodyMutableBindings, allowContainerBindings: true);
        if (body.Value is null)
        {
            return BoundType.Unit;
        }

        var resultType = InferExpression(
            body.Value,
            functions,
            bodyBindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false,
            mutableBindings: bodyMutableBindings,
            allowedOwnedOuterResultName: allowedOwnedOuterResultName);
        if (resultType == BoundType.StaticIntArray)
        {
            throw Error(
                body.Value.Line,
                body.Value.Column,
                "static array block results are not supported yet; use a growable array or keep the static array inside the block");
        }

        if (IsContainerType(resultType))
        {
            EnsureOwnedContainerCanLeaveBlock(
                body.Value,
                bindings,
                bodyBindings,
                allowedOwnedOuterResultName);
        }

        return resultType;
    }

    private FlowResult InferFlowExpression(
        FlowExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        bool allowFlowBindingTarget,
        BoundType? yieldInputType = null,
        IReadOnlySet<string>? mutableBindings = null)
    {
        var currentType = InferFlowSource(expression.Source, functions, bindings, allowReadIntCall);
        if (IsOwnedHeapType(currentType) && IsAnonymousOwnedHeapContainerExpression(expression.Source))
        {
            throw Error(
                expression.Source.Line,
                expression.Source.Column,
                "owned heap containers must be bound directly so the compiler can prove and insert their drop");
        }

        for (var i = 0; i < expression.Targets.Count; i++)
        {
            var target = expression.Targets[i];
            var isLast = i == expression.Targets.Count - 1;
            var path = string.Join('.', target.Path);

            if (TryInferContainerFlowTarget(
                expression,
                target,
                path,
                currentType,
                functions,
                bindings,
                mutableBindings,
                allowReadIntCall,
                isLast,
                out var containerFlowResult))
            {
                if (containerFlowResult.Type == BoundType.Unit)
                {
                    return containerFlowResult;
                }

                currentType = containerFlowResult.Type;
                continue;
            }

            if (path == "yield")
            {
                if (target.Arguments.Count != 0)
                {
                    throw Error(target.Line, target.Column, "yield does not accept arguments");
                }

                if (yieldInputType is null)
                {
                    throw Error(target.Line, target.Column, "yield() is only valid inside a block function");
                }

                if (!isLast)
                {
                    throw Error(target.Line, target.Column, "yield must be the final value-flow target");
                }

                if (currentType != yieldInputType.Value)
                {
                    throw Error(
                        expression.Line,
                        expression.Column,
                        $"yield expects {FormatType(yieldInputType.Value)} but received {FormatType(currentType)}");
                }

                return new FlowResult(BoundType.Unit, FlowEffect.None);
            }

            if (TryGetFunction(path, functions, out var function)
                || TryResolveInstanceMethod(currentType, path, functions, out function))
            {
                EnsureFunctionVisible(function, target.Line, target.Column);
                EnsureAsyncRuntimeCallable(function, target.Line, target.Column, path);
                if (target.Arguments.Count != 0)
                {
                    throw Error(
                        target.Line,
                        target.Column,
                        $"function value-flow target '{path}' does not accept additional arguments in this slice");
                }

                switch (function.Kind)
                {
                    case BoundFunctionKind.RuntimePrint:
                    case BoundFunctionKind.RuntimePrintLine:
                        if (!isLast)
                        {
                            throw Error(expression.Line, expression.Column, $"{path} must be the final value-flow target");
                        }

                        EnsureDisplayable(currentType, expression.Line, expression.Column, path);
                        return new FlowResult(BoundType.Unit, FlowEffect.None);
                    case BoundFunctionKind.RuntimeReadInt:
                        if (!allowReadIntCall)
                        {
                            throw Error(expression.Line, expression.Column, $"{path} is only valid in main for the current runtime slice");
                        }

                        if (currentType != BoundType.Text)
                        {
                            throw Error(
                                expression.Line,
                                expression.Column,
                                $"{path} expects Text but received {FormatType(currentType)}");
                        }

                        currentType = BoundType.Int;
                        continue;
                    case BoundFunctionKind.RuntimeSeedRandom:
                    case BoundFunctionKind.RuntimeWriteInt:
                        EnsureRuntimeIntrinsicAllowed(function, allowReadIntCall, expression.Line, expression.Column, path);
                        EnsureRuntimeInput(currentType, function, expression.Line, expression.Column, path);
                        if (!isLast)
                        {
                            throw Error(expression.Line, expression.Column, $"{path} must be the final value-flow target");
                        }

                        return new FlowResult(BoundType.Unit, FlowEffect.None);
                    case BoundFunctionKind.RuntimeWriteScalar:
                        function = ResolveGenericSpecialization(function, currentType, functions, target);
                        if (!isLast)
                        {
                            throw Error(expression.Line, expression.Column, $"{path} must be the final value-flow target");
                        }
                        return new FlowResult(BoundType.Unit, FlowEffect.None);
                    case BoundFunctionKind.RuntimeOpenIntWriter:
                    case BoundFunctionKind.RuntimeOpenIntReader:
                        EnsureRuntimeIntrinsicAllowed(function, allowReadIntCall, expression.Line, expression.Column, path);
                        EnsureRuntimeInput(currentType, function, expression.Line, expression.Column, path);
                        if (!isLast)
                        {
                            throw Error(expression.Line, expression.Column, $"{path} must be the final value-flow target");
                        }

                        return new FlowResult(BoundType.Unit, FlowEffect.None);
                    case BoundFunctionKind.RuntimeRandomBelow:
                    case BoundFunctionKind.RuntimeClosestInt:
                        EnsureRuntimeIntrinsicAllowed(function, allowReadIntCall, expression.Line, expression.Column, path);
                        EnsureRuntimeInput(currentType, function, expression.Line, expression.Column, path);
                        currentType = function.ReturnType;
                        continue;
                    case BoundFunctionKind.RuntimeCloseIntWriter:
                    case BoundFunctionKind.RuntimeCloseIntReader:
                        throw Error(expression.Line, expression.Column, $"{path} does not accept a flowed input");
                    case BoundFunctionKind.RuntimeEnvironment:
                        if (currentType != BoundType.Text)
                        {
                            throw Error(expression.Line, expression.Column,
                                $"{path} expects Text but received {FormatType(currentType)}");
                        }
                        currentType = function.ReturnType;
                        continue;
                    case BoundFunctionKind.RuntimeRunProcess:
                        EnsureRuntimeInput(currentType, function, expression.Line, expression.Column, path);
                        currentType = function.ReturnType;
                        continue;
                    case BoundFunctionKind.User:
                        if (function.GenericParameterName is not null
                            && function.SpecializedType is null
                            && function.SpecializedValue is null)
                        {
                            function = function.IsValueGeneric
                                ? ResolveValueGenericSpecialization(function, currentType, target.CompileTimeValueArgument, target)
                                : ResolveGenericSpecialization(function, currentType, functions, target);
                        }
                        else if (target.CompileTimeValueArgument is not null)
                        {
                            throw Error(
                                target.Line,
                                target.Column,
                                $"function '{path}' does not declare a compile-time value parameter");
                        }

                        if (IsMainOnlyRuntimeWrapper(function) && !allowReadIntCall)
                        {
                            throw Error(expression.Line, expression.Column, $"{path} is only valid in main for the current runtime slice");
                        }

                        if (function.InputType is null)
                        {
                            throw Error(expression.Line, expression.Column, $"function '{path}' does not accept a flowed input");
                        }

                if (currentType != function.InputType)
                {
                            if (!CanPassFunctionArgument(currentType, function.InputType.Value))
                            {
                                throw Error(
                                expression.Line,
                                expression.Column,
                                $"function '{path}' expects {FormatType(function.InputType.Value)} but received {FormatType(currentType)}");
                            }
                        }

                        if (FunctionMovesOwnedHeapInput(function))
                        {
                            EnsureOwnedParameterFlowSource(expression.Source, path);
                        }

                        if (FunctionMutablyBorrowsInput(function))
                        {
                            EnsureMutableBorrowFlowSource(expression.Source, path, mutableBindings);
                        }

                        currentType = AsyncCallType(function);
                        continue;
                    default:
                        throw Error(expression.Line, expression.Column, $"unsupported function kind '{function.Kind}'");
                }
            }

            throw Error(target.Line, target.Column, $"unknown value-flow target '{path}'");
        }

        return new FlowResult(currentType, FlowEffect.None);
    }

    private bool TryInferContainerFlowTarget(
        FlowExpression expression,
        FlowTarget target,
        string path,
        BoundType currentType,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        IReadOnlySet<string>? mutableBindings,
        bool allowReadIntCall,
        bool isLast,
        out FlowResult result)
    {
        result = new FlowResult(BoundType.Unit, FlowEffect.None);
        switch (path)
        {
            case "await" when _types.TryGetTaskValue(currentType, out var awaitedType):
                if (!_currentFunctionIsAsync)
                {
                    throw Error(target.Line, target.Column, "await is only valid in async functions or main");
                }
                if (target.Arguments.Count != 0)
                {
                    throw Error(target.Line, target.Column, "await does not accept arguments");
                }
                result = new FlowResult(awaitedType, FlowEffect.None);
                return true;
            case "flush" when currentType == BoundType.MutableMappedBytes:
                if (!isLast || target.Arguments.Count != 0)
                {
                    throw Error(target.Line, target.Column, "flush must be final and takes no arguments");
                }
                EnsureMutableContainerSource(expression.Source, "flush", mutableBindings);
                result = new FlowResult(BoundType.Unit, FlowEffect.None);
                return true;
            case "used" when currentType == BoundType.Arena:
                if (target.Arguments.Count != 0)
                {
                    throw Error(target.Line, target.Column, "used does not accept arguments");
                }
                result = new FlowResult(BoundType.UIntSize, FlowEffect.None);
                return true;
            case "alloc" when currentType == BoundType.Arena:
                if (!isLast || target.Arguments.Count != 2)
                {
                    throw Error(target.Line, target.Column,
                        "arena alloc must be final and expects byte-count and alignment arguments");
                }
                EnsureMutableContainerSource(expression.Source, "alloc", mutableBindings);
                foreach (var argument in target.Arguments)
                {
                    var argumentType = InferExpression(argument, functions, bindings,
                        allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
                    if (argumentType is not (BoundType.Int or BoundType.UIntSize))
                    {
                        throw Error(argument.Line, argument.Column,
                            "arena alloc byte-count and alignment must be Int or UIntSize");
                    }
                }
                if (target.Arguments[1] is NumberExpression alignmentLiteral
                    && long.TryParse(alignmentLiteral.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var literalAlignment)
                    && (literalAlignment <= 0 || (literalAlignment & (literalAlignment - 1)) != 0))
                {
                    throw Error(alignmentLiteral.Line, alignmentLiteral.Column,
                        "arena alignment must be a nonzero power of two");
                }
                result = new FlowResult(BoundType.UIntSize, FlowEffect.None);
                return true;
            case "store" when currentType == BoundType.Arena:
                if (!isLast || target.Arguments.Count != 2)
                {
                    throw Error(target.Line, target.Column,
                        "arena store must be final and expects offset and UInt8 value arguments");
                }
                EnsureMutableContainerSource(expression.Source, "store", mutableBindings);
                var storeOffsetType = InferExpression(target.Arguments[0], functions, bindings,
                    allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
                var storeValueType = InferExpression(target.Arguments[1], functions, bindings,
                    allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
                if (storeOffsetType != BoundType.UIntSize || storeValueType != BoundType.UInt8)
                {
                    throw Error(target.Line, target.Column, "arena store expects UIntSize offset and UInt8 value");
                }
                result = new FlowResult(BoundType.Unit, FlowEffect.None);
                return true;
            case "load" when currentType == BoundType.Arena:
                if (target.Arguments.Count != 1)
                {
                    throw Error(target.Line, target.Column, "arena load expects one UIntSize offset");
                }
                var loadOffsetType = InferExpression(target.Arguments[0], functions, bindings,
                    allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
                if (loadOffsetType != BoundType.UIntSize)
                {
                    throw Error(target.Arguments[0].Line, target.Arguments[0].Column,
                        "arena load offset must be UIntSize");
                }
                result = new FlowResult(BoundType.UInt8, FlowEffect.None);
                return true;
            case "reset" when currentType == BoundType.Arena:
                if (!isLast || target.Arguments.Count != 0)
                {
                    throw Error(target.Line, target.Column, "arena reset must be final and takes no arguments");
                }
                EnsureMutableContainerSource(expression.Source, "reset", mutableBindings);
                result = new FlowResult(BoundType.Unit, FlowEffect.None);
                return true;
            case "len":
                if (target.Arguments.Count != 0)
                {
                    throw Error(target.Line, target.Column, "len does not accept arguments");
                }

                if (currentType is not (BoundType.Text
                    or BoundType.IntSlice
                    or BoundType.StaticIntArray
                    or BoundType.StaticTextArray
                    or BoundType.DynamicIntArray
                    or BoundType.IntDictionaryView
                    or BoundType.IntDictionary
                    or BoundType.Arguments
                    or BoundType.MappedBytes
                    or BoundType.MutableMappedBytes)
                    && !_types.IsStaticArray(currentType)
                    && !_types.IsDynamicArray(currentType)
                    && !_types.IsDictionary(currentType))
                {
                    return false;
                }

                result = new FlowResult(
                    currentType is BoundType.Text or BoundType.MappedBytes or BoundType.MutableMappedBytes or BoundType.Arguments
                        ? BoundType.UIntSize
                        : BoundType.Int,
                    FlowEffect.None);
                return true;
            case "byte" when currentType == BoundType.Text:
                if (target.Arguments.Count != 1)
                {
                    throw Error(target.Line, target.Column, "Text byte expects one UIntSize index");
                }
                var byteIndexType = target.Arguments[0] is NumberExpression
                    ? BoundType.UIntSize
                    : InferExpression(target.Arguments[0], functions, bindings,
                        allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
                if (byteIndexType != BoundType.UIntSize)
                {
                    throw Error(target.Arguments[0].Line, target.Arguments[0].Column,
                        "Text byte index must be UIntSize");
                }
                result = new FlowResult(BoundType.UInt8, FlowEffect.None);
                return true;
            case "slice" when currentType == BoundType.Text:
                if (target.Arguments.Count != 2)
                {
                    throw Error(target.Line, target.Column,
                        "Text slice expects UIntSize start and byte length");
                }
                foreach (var argument in target.Arguments)
                {
                    var argumentType = argument is NumberExpression
                        ? BoundType.UIntSize
                        : InferExpression(argument, functions, bindings,
                            allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
                    if (argumentType != BoundType.UIntSize)
                    {
                        throw Error(argument.Line, argument.Column,
                            "Text slice start and length must be UIntSize");
                    }
                }
                result = new FlowResult(BoundType.Text, FlowEffect.None);
                return true;
            case "capacity":
                if (target.Arguments.Count != 0)
                {
                    throw Error(target.Line, target.Column, "capacity does not accept arguments");
                }

                if (currentType is not (BoundType.DynamicIntArray
                    or BoundType.IntDictionaryView
                    or BoundType.IntDictionary
                    or BoundType.Arena)
                    && !_types.IsDynamicArray(currentType)
                    && !_types.IsDictionary(currentType))
                {
                    return false;
                }

                result = new FlowResult(
                    currentType is BoundType.Arena or BoundType.MappedBytes or BoundType.MutableMappedBytes
                        ? BoundType.UIntSize
                        : BoundType.Int,
                    FlowEffect.None);
                return true;
            case "push":
                if (currentType != BoundType.DynamicIntArray && !_types.IsDynamicArray(currentType))
                {
                    return false;
                }

                if (!isLast)
                {
                    throw Error(target.Line, target.Column, "push must be the final value-flow target");
                }

                EnsureMutableContainerSource(expression.Source, "push", mutableBindings);

                if (target.Arguments.Count != 1)
                {
                    throw Error(target.Line, target.Column, "push expects exactly one argument");
                }

                var expectedPushedType = currentType == BoundType.DynamicIntArray
                    ? BoundType.Int
                    : _types.GetDynamicArray(currentType).ElementType;
                var pushedArgument = target.Arguments[0];
                var pushedType = pushedArgument is DictionaryLiteralExpression contextualPushed
                    && _types.IsStruct(expectedPushedType)
                        ? InferContextualStructLiteral(
                            contextualPushed,
                            expectedPushedType,
                            functions,
                            bindings,
                            allowReadIntCall)
                        : InferExpression(
                            pushedArgument,
                            functions,
                            bindings,
                            allowPrintCall: false,
                            allowReadIntCall,
                            allowFlowBindingTarget: false);
                if (pushedType != expectedPushedType)
                {
                    throw Error(
                        target.Arguments[0].Line,
                        target.Arguments[0].Column,
                        $"push expects {FormatType(expectedPushedType)}, got {FormatType(pushedType)}");
                }
                if (_types.ContainsOwnedStorage(expectedPushedType)
                    && target.Arguments[0] is NameExpression)
                {
                    throw Error(
                        target.Arguments[0].Line,
                        target.Arguments[0].Column,
                        "pushing an owned element requires a freshly created value until explicit move arguments are implemented");
                }

                result = new FlowResult(BoundType.Unit, FlowEffect.None);
                return true;
            case "append":
                if (currentType != BoundType.DynamicIntArray)
                {
                    return false;
                }

                if (!isLast)
                {
                    throw Error(target.Line, target.Column, "append must be bound directly with '=>'");
                }

                EnsureMoveContainerSource(expression.Source, "append");

                if (target.Arguments.Count != 1)
                {
                    throw Error(target.Line, target.Column, "append expects exactly one Int argument");
                }

                var appendedType = InferExpression(
                    target.Arguments[0],
                    functions,
                    bindings,
                    allowPrintCall: false,
                    allowReadIntCall,
                    allowFlowBindingTarget: false);
                if (appendedType != BoundType.Int)
                {
                    throw Error(target.Arguments[0].Line, target.Arguments[0].Column, "append expects an Int value");
                }

                result = new FlowResult(BoundType.DynamicIntArray, FlowEffect.None);
                return true;
            case "put":
                if (currentType == BoundType.IntDictionaryView)
                {
                    throw Error(
                        target.Line,
                        target.Column,
                        "put is not available on a readonly dictionary parameter; use 'mut {Int: Int}'");
                }

                if (currentType != BoundType.IntDictionary && !_types.IsDictionary(currentType))
                {
                    return false;
                }

                if (!isLast)
                {
                    throw Error(target.Line, target.Column, "put must be the final value-flow target");
                }

                EnsureMutableContainerSource(expression.Source, "put", mutableBindings);

                if (target.Arguments.Count != 2)
                {
                    throw Error(target.Line, target.Column, "put expects key and value arguments");
                }

                var putKeyType = currentType == BoundType.IntDictionary
                    ? BoundType.Int
                    : _types.GetDictionary(currentType).KeyType;
                var putValueType = currentType == BoundType.IntDictionary
                    ? BoundType.Int
                    : _types.GetDictionary(currentType).ValueType;
                var expectedPutTypes = new[] { putKeyType, putValueType };
                for (var argumentIndex = 0; argumentIndex < target.Arguments.Count; argumentIndex++)
                {
                    var argument = target.Arguments[argumentIndex];
                    var expectedArgumentType = expectedPutTypes[argumentIndex];
                    var argumentType = argument is DictionaryLiteralExpression contextualArgument
                        && _types.IsStruct(expectedArgumentType)
                            ? InferContextualStructLiteral(
                                contextualArgument,
                                expectedArgumentType,
                                functions,
                                bindings,
                                allowReadIntCall)
                            : InferExpression(
                                argument,
                                functions,
                                bindings,
                                allowPrintCall: false,
                                allowReadIntCall,
                                allowFlowBindingTarget: false);
                    if (argumentType != expectedPutTypes[argumentIndex])
                    {
                        throw Error(argument.Line, argument.Column,
                            $"put expects {FormatType(putKeyType)} key and {FormatType(putValueType)} value arguments");
                    }
                }

                result = new FlowResult(BoundType.Unit, FlowEffect.None);
                return true;
            case "updated":
                if (currentType == BoundType.IntDictionaryView)
                {
                    throw Error(
                        target.Line,
                        target.Column,
                        "updated consumes a dictionary owner and is not available on a readonly dictionary parameter; use 'move {Int: Int}'");
                }

                if (currentType is not (BoundType.DynamicIntArray or BoundType.IntDictionary))
                {
                    return false;
                }

                if (!isLast)
                {
                    throw Error(target.Line, target.Column, "updated must be bound directly with '=>'");
                }

                EnsureMoveContainerSource(expression.Source, "updated");

                if (target.Arguments.Count != 2)
                {
                    throw Error(target.Line, target.Column, "updated expects two Int arguments");
                }

                foreach (var argument in target.Arguments)
                {
                    var argumentType = InferExpression(
                        argument,
                        functions,
                        bindings,
                        allowPrintCall: false,
                        allowReadIntCall,
                        allowFlowBindingTarget: false);
                    if (argumentType != BoundType.Int)
                    {
                        throw Error(argument.Line, argument.Column, "updated expects Int arguments");
                    }
                }

                result = new FlowResult(currentType, FlowEffect.None);
                return true;
            default:
                if (path == "await")
                {
                    throw Error(
                        target.Line,
                        target.Column,
                        $"await expects Task<T> but received {FormatType(currentType)}");
                }
                return false;
        }
    }

    private BoundType InferFlowSource(
        Expression source,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        return InferExpression(
            source,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
    }

    private BoundType InferCallExpression(
        CallExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowPrintCall,
        bool allowReadIntCall,
        IReadOnlySet<string>? mutableBindings)
    {
        if (TryInferEnumConstructor(expression, functions, bindings, allowReadIntCall, out var enumType))
        {
            return enumType;
        }

        if (TryInferArenaConstructor(expression, functions, bindings, allowReadIntCall, out var arenaType))
        {
            return arenaType;
        }

        if (TryInferNumericConversion(expression, functions, bindings, allowReadIntCall, out var numericType))
        {
            return numericType;
        }

        var path = string.Join('.', expression.Path);
        string? receiverName = null;
        BoundType? receiverType = null;
        if (!TryGetFunction(path, functions, out var function))
        {
            if (!TryResolveInstanceMethodCall(
                expression.Path,
                functions,
                bindings,
                out function,
                out receiverName,
                out receiverType))
            {
                throw Error(expression.Line, expression.Column, $"unknown function or method '{path}'");
            }
        }

        EnsureFunctionVisible(function, expression.Line, expression.Column);
        EnsureAsyncRuntimeCallable(function, expression.Line, expression.Column, path);

        if (function.InputType is null
            && expression.Arguments.Count == 0
            && !(expression.Path.Count == 2
                && _types.TryResolve(expression.Path[0], out var zeroArgumentOwnerType)
                && _types.IsStruct(zeroArgumentOwnerType)))
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"zero-argument function '{path}' uses property syntax without parentheses: '{path}'");
        }

        if (expression.Path.Count == 2
            && function.InputType is null
            && _types.TryResolve(expression.Path[0], out var associatedType)
            && _types.IsStruct(associatedType))
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"zero-argument associated member '{path}' uses property syntax without parentheses: '{path}'");
        }

        switch (function.Kind)
        {
            case BoundFunctionKind.RuntimePrint:
            case BoundFunctionKind.RuntimePrintLine:
                if (!allowPrintCall)
                {
                    throw Error(expression.Line, expression.Column, $"{path} is only valid as an expression statement");
                }

                if (expression.Arguments.Count != 1)
                {
                    throw Error(expression.Line, expression.Column, $"{path} expects exactly one argument");
                }

                var valueType = InferExpression(
                    expression.Arguments[0],
                    functions,
                    bindings,
                    allowPrintCall: false,
                    allowReadIntCall,
                    allowFlowBindingTarget: false);
                EnsureDisplayable(valueType, expression.Arguments[0].Line, expression.Arguments[0].Column, path);
                return BoundType.Unit;
            case BoundFunctionKind.RuntimeReadInt:
                if (!allowReadIntCall)
                {
                    throw Error(expression.Line, expression.Column, $"{path} is only valid in main for the current runtime slice");
                }

                if (expression.Arguments.Count != 1)
                {
                    throw Error(expression.Line, expression.Column, $"{path} expects exactly one Text prompt");
                }

                var promptType = InferExpression(
                    expression.Arguments[0],
                    functions,
                    bindings,
                    allowPrintCall: false,
                    allowReadIntCall,
                    allowFlowBindingTarget: false);
                if (promptType != BoundType.Text)
                {
                    throw Error(
                        expression.Arguments[0].Line,
                        expression.Arguments[0].Column,
                        $"{path} expects Text but received {FormatType(promptType)}");
                }

                return function.ReturnType;
            case BoundFunctionKind.RuntimeEnvironment:
                if (expression.Arguments.Count != 1)
                {
                    throw Error(expression.Line, expression.Column, $"{path} expects exactly one Text name");
                }
                var environmentNameType = InferExpression(
                    expression.Arguments[0], functions, bindings,
                    allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
                if (environmentNameType != BoundType.Text)
                {
                    throw Error(expression.Arguments[0].Line, expression.Arguments[0].Column,
                        $"{path} expects Text but received {FormatType(environmentNameType)}");
                }
                return function.ReturnType;
            case BoundFunctionKind.RuntimeRunProcess:
                if (expression.Arguments.Count != 1)
                {
                    throw Error(expression.Line, expression.Column, $"{path} expects one dynamic Text argv array");
                }
                var argvType = InferExpression(
                    expression.Arguments[0], functions, bindings,
                    allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
                EnsureRuntimeInput(argvType, function, expression.Arguments[0].Line, expression.Arguments[0].Column, path);
                return function.ReturnType;
            case BoundFunctionKind.RuntimeSeedRandom:
            case BoundFunctionKind.RuntimeRandomBelow:
            case BoundFunctionKind.RuntimeOpenIntWriter:
            case BoundFunctionKind.RuntimeWriteInt:
            case BoundFunctionKind.RuntimeOpenIntReader:
            case BoundFunctionKind.RuntimeClosestInt:
                EnsureRuntimeIntrinsicAllowed(function, allowReadIntCall, expression.Line, expression.Column, path);
                if (expression.Arguments.Count != 1)
                {
                    throw Error(expression.Line, expression.Column, $"{path} expects exactly one argument");
                }

                var argumentType = InferExpression(
                    expression.Arguments[0],
                    functions,
                    bindings,
                    allowPrintCall: false,
                    allowReadIntCall,
                    allowFlowBindingTarget: false);
                EnsureRuntimeInput(argumentType, function, expression.Arguments[0].Line, expression.Arguments[0].Column, path);
                return function.ReturnType;
            case BoundFunctionKind.RuntimeCloseIntWriter:
            case BoundFunctionKind.RuntimeCloseIntReader:
                EnsureRuntimeIntrinsicAllowed(function, allowReadIntCall, expression.Line, expression.Column, path);
                if (expression.Arguments.Count != 0)
                {
                    throw Error(expression.Line, expression.Column, $"{path} does not accept arguments");
                }

                return BoundType.Unit;
            case BoundFunctionKind.RuntimeNowMillis:
            case BoundFunctionKind.RuntimeArguments:
                EnsureRuntimeIntrinsicAllowed(function, allowReadIntCall, expression.Line, expression.Column, path);
                if (expression.Arguments.Count != 0)
                {
                    throw Error(expression.Line, expression.Column, $"{path} does not accept arguments");
                }

                return function.ReturnType;
            case BoundFunctionKind.RuntimeWriteScalar:
                return InferGenericCallExpression(
                    expression, function, functions, bindings, allowReadIntCall);
            case BoundFunctionKind.User:
                if (function.GenericParameterName is not null
                    && function.SpecializedType is null
                    && function.SpecializedValue is null)
                {
                    if (function.IsValueGeneric)
                    {
                        throw Error(
                            expression.Line,
                            expression.Column,
                            $"value-generic function '{function.Name}' requires fluent syntax with an explicit value argument, for example 'value -> {function.Name}[4]'");
                    }
                    return InferGenericCallExpression(
                        expression,
                        function,
                        functions,
                        bindings,
                        allowReadIntCall);
                }

                if (receiverName is not null && receiverType is not null)
                {
                    throw Error(
                        expression.Line,
                        expression.Column,
                        $"zero-argument method '{path}' uses property syntax without parentheses: '{path}'");
                }

                return InferUserCallExpression(expression, function, functions, bindings, allowReadIntCall, mutableBindings, path);
            default:
                throw Error(expression.Line, expression.Column, $"unsupported function kind '{function.Kind}'");
        }
    }

    private BoundType InferGenericCallExpression(
        CallExpression expression,
        BoundFunction template,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        if (expression.Arguments.Count != 1)
        {
            throw Error(expression.Line, expression.Column, $"generic function '{template.Name}' expects one argument");
        }

        var actualType = InferExpression(
            expression.Arguments[0],
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        var specialization = ResolveGenericSpecialization(template, actualType, functions, expression);
        return specialization.ReturnType;
    }

    private BoundFunction ResolveGenericSpecialization(
        BoundFunction template,
        BoundType actualType,
        IReadOnlyDictionary<string, BoundFunction> functions,
        object callSite)
    {
        if (template.Kind is BoundFunctionKind.RuntimeWriteScalar or BoundFunctionKind.RuntimeReadScalar
            && actualType != BoundType.Bool
            && !IsNumericType(actualType)
            && actualType != BoundType.CodePoint)
        {
            var operation = template.Kind == BoundFunctionKind.RuntimeReadScalar ? "read" : "write";
            throw new SmallLangException(
                $"generic file {operation} supports Bool, CodePoint, and fixed-width numeric scalars; got {FormatType(actualType)}");
        }
        if (actualType is BoundType.Unit
            or BoundType.IntSlice
            or BoundType.StaticIntArray
            or BoundType.DynamicIntArray
            or BoundType.IntDictionaryView
            or BoundType.IntDictionary)
        {
            throw new SmallLangException(
                $"generic function '{template.Name}' does not yet support {FormatType(actualType)} specialization");
        }

        BoundFunction? traitImplementation = null;
        if (template.GenericTraitBound is { } traitBound
            && !TryFindTraitImplementation(functions, traitBound, actualType, out traitImplementation))
        {
            throw new SmallLangException(
                $"type {FormatType(actualType)} does not implement trait '{traitBound}' required by '{template.Name}'");
        }
        BoundType? inferredSecondaryType = null;
        if (template.GenericTraitBound is { } constrainedTrait
            && template.GenericAssociatedTypeName is { } associatedTypeName
            && template.GenericAssociatedTypeConstraint is { } associatedTypeConstraint)
        {
            var actualAssociatedType = default(BoundType);
            var hasAssociatedType = traitImplementation?.ImplAssociatedTypes is { } associatedTypes
                && associatedTypes.TryGetValue(associatedTypeName, out actualAssociatedType);
            var satisfiesConstraint = hasAssociatedType
                && (associatedTypeConstraint == BoundType.SecondaryGenericParameter
                    || actualAssociatedType == associatedTypeConstraint);
            if (!satisfiesConstraint)
            {
                throw new SmallLangException(
                    $"type {FormatType(actualType)} does not satisfy associated type constraint "
                    + $"'{constrainedTrait}<{associatedTypeName} = {FormatType(associatedTypeConstraint)}>' required by '{template.Name}'");
            }
            if (associatedTypeConstraint == BoundType.SecondaryGenericParameter)
            {
                inferredSecondaryType = actualAssociatedType;
            }
        }
        if (template.SecondaryGenericParameterName is not null && inferredSecondaryType is null)
        {
            throw new SmallLangException(
                $"generic function '{template.Name}' cannot infer type parameter '{template.SecondaryGenericParameterName}'");
        }

        var specializedName = template.Name + "$" + ((int)actualType).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (inferredSecondaryType is { } secondaryType)
        {
            specializedName += "_" + ((int)secondaryType).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (_boundFunctions is null)
        {
            throw new InvalidOperationException("generic specialization requires bound functions");
        }

        if (!_boundFunctions.TryGetValue(specializedName, out var specialization))
        {
            specialization = template with
            {
                Name = specializedName,
                InputType = template.InputType is null ? null : actualType,
                ReturnType = SubstituteGenericType(template.ReturnType, actualType, inferredSecondaryType),
                SpecializedType = actualType,
                SpecializedSecondaryType = inferredSecondaryType
            };
            _boundFunctions.Add(specializedName, specialization);
            if (specialization.Kind is BoundFunctionKind.User or BoundFunctionKind.UserBlock
                && _validatingGenericSpecializations.Add(specialization))
            {
                ValidateUserFunction(
                    specialization,
                    _boundFunctions,
                    new Dictionary<string, BoundType>(StringComparer.Ordinal));
            }
        }

        _resolvedGenericCalls[callSite] = specialization;
        return specialization;
    }

    private BoundType SubstituteGenericType(
        BoundType type,
        BoundType primaryType,
        BoundType? secondaryType)
    {
        if (type == BoundType.GenericParameter) return primaryType;
        if (type == BoundType.SecondaryGenericParameter) return secondaryType!.Value;
        if (_types.TryGetOptionValue(type, out var optionValue))
        {
            var value = SubstituteGenericType(optionValue, primaryType, secondaryType);
            return _types.GetOrAddOption(value, $"Option<{FormatType(value)}>");
        }
        if (_types.TryGetResultTypes(type, out var resultTypes))
        {
            var ok = SubstituteGenericType(resultTypes.Ok, primaryType, secondaryType);
            var error = SubstituteGenericType(resultTypes.Error, primaryType, secondaryType);
            return _types.GetOrAddResult(ok, error, $"Result<{FormatType(ok)}, {FormatType(error)}>");
        }
        if (_types.TryGetTaskValue(type, out var taskValue))
        {
            var value = SubstituteGenericType(taskValue, primaryType, secondaryType);
            return _types.GetOrAddTask(value);
        }
        return type;
    }

    private BoundType InferTypeApplicationExpression(
        TypeApplicationExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        bool allowRuntimeCall)
    {
        var path = string.Join('.', expression.Path);
        if (!TryGetFunction(path, functions, out var template))
        {
            throw Error(expression.Line, expression.Column, $"unknown generic function '{path}'");
        }
        EnsureFunctionVisible(template, expression.Line, expression.Column);
        if (template.GenericParameterName is null || template.SpecializedType is not null)
        {
            throw Error(expression.Line, expression.Column, $"function '{path}' is not generic");
        }
        if (template.InputType is not null)
        {
            throw Error(expression.Line, expression.Column,
                $"generic function '{path}' expects an argument and must use call or flow syntax");
        }
        if (IsMainOnlyRuntimeWrapper(template) && !allowRuntimeCall)
        {
            throw Error(expression.Line, expression.Column,
                $"{path} is only valid in main for the current runtime slice");
        }
        var actualType = ParseType(expression.TypeArgument, expression.Line, expression.Column);
        return ResolveGenericSpecialization(template, actualType, functions, expression).ReturnType;
    }

    private BoundFunction ResolveValueGenericSpecialization(
        BoundFunction template,
        BoundType actualType,
        int? valueArgument,
        object callSite)
    {
        if (valueArgument is null)
        {
            throw new SmallLangException(
                $"value-generic function '{template.Name}' requires an explicit compile-time Int argument");
        }
        if (template.HasValueGenericFixedArrayInput && actualType != BoundType.StaticIntArray)
        {
            throw new SmallLangException(
                $"value-generic function '{template.Name}' requires a fixed Int array input");
        }
        if (!template.HasValueGenericFixedArrayInput && template.InputType != actualType)
        {
            throw new SmallLangException(
                $"function '{template.Name}' expects {FormatType(template.InputType!.Value)} but received {FormatType(actualType)}");
        }
        if (_boundFunctions is null)
        {
            throw new InvalidOperationException("generic specialization requires bound functions");
        }

        var specializedName = template.Name
            + "$v"
            + valueArgument.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!_boundFunctions.TryGetValue(specializedName, out var specialization))
        {
            specialization = template with
            {
                Name = specializedName,
                SpecializedValue = valueArgument.Value
            };
            _boundFunctions.Add(specializedName, specialization);
            if (_validatingGenericSpecializations.Add(specialization))
            {
                ValidateUserFunction(
                    specialization,
                    _boundFunctions,
                    new Dictionary<string, BoundType>(StringComparer.Ordinal));
            }
        }

        _resolvedGenericCalls[callSite] = specialization;
        return specialization;
    }

    private bool TryInferArenaConstructor(
        CallExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        out BoundType type)
    {
        type = default;
        if (expression.Path.Count != 1 || expression.Path[0] != "Arena")
        {
            return false;
        }
        if (expression.Arguments.Count != 1)
        {
            throw Error(expression.Line, expression.Column, "Arena expects one initial byte-capacity argument");
        }
        var capacityType = InferExpression(
            expression.Arguments[0], functions, bindings,
            allowPrintCall: false, allowReadIntCall, allowFlowBindingTarget: false);
        if (capacityType is not (BoundType.Int or BoundType.UIntSize))
        {
            throw Error(expression.Arguments[0].Line, expression.Arguments[0].Column,
                $"Arena capacity must be Int or UIntSize, got {FormatType(capacityType)}");
        }
        type = BoundType.Arena;
        return true;
    }

    private bool TryInferNumericConversion(
        CallExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        out BoundType type)
    {
        type = default;
        if (expression.Path.Count != 1
            || !_types.TryResolve(expression.Path[0], out var targetType)
            || !IsNumericType(targetType))
        {
            return false;
        }
        if (expression.Arguments.Count != 1)
        {
            throw Error(expression.Line, expression.Column,
                $"numeric conversion '{expression.Path[0]}' expects exactly one argument");
        }
        var sourceType = InferExpression(
            expression.Arguments[0],
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (!IsNumericType(sourceType))
        {
            throw Error(expression.Arguments[0].Line, expression.Arguments[0].Column,
                $"numeric conversion '{expression.Path[0]}' expects a numeric value, got {FormatType(sourceType)}");
        }
        if (targetType == BoundType.CodePoint && IsFloatType(sourceType))
        {
            throw Error(expression.Arguments[0].Line, expression.Arguments[0].Column,
                "CodePoint conversion requires an integer Unicode scalar value");
        }
        ValidateNumericLiteralConversion(expression.Arguments[0], targetType, expression.Path[0]);
        type = targetType;
        return true;
    }

    private void ValidateNumericLiteralConversion(Expression argument, BoundType targetType, string targetName)
    {
        var negative = argument is NegateExpression;
        var number = argument switch
        {
            NumberExpression direct => direct,
            NegateExpression { Value: NumberExpression negated } => negated,
            _ => null
        };
        if (number is null)
        {
            return;
        }
        var text = negative ? "-" + number.Text : number.Text;
        if (IsIntegerType(targetType) && !number.Text.Contains('.', StringComparison.Ordinal)
            && !number.Text.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            var value = BigInteger.Parse(text, CultureInfo.InvariantCulture);
            var bits = targetType switch
            {
                BoundType.Int8 or BoundType.UInt8 => 8,
                BoundType.Int16 or BoundType.UInt16 => 16,
                BoundType.Int or BoundType.UInt32 => 32,
                BoundType.Size or BoundType.UIntSize => _pointerBitWidth,
                _ => 64
            };
            var signed = IsSignedIntegerType(targetType);
            var minimum = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
            var maximum = targetType == BoundType.CodePoint
                ? new BigInteger(0x10FFFF)
                : signed ? (BigInteger.One << (bits - 1)) - 1 : (BigInteger.One << bits) - 1;
            if (value < minimum || value > maximum)
            {
                throw Error(argument.Line, argument.Column,
                    $"numeric literal {text} is out of range for {targetName} ({minimum}..{maximum})");
            }
            if (targetType == BoundType.CodePoint && value >= 0xD800 && value <= 0xDFFF)
            {
                throw Error(argument.Line, argument.Column,
                    $"numeric literal {text} is a Unicode surrogate and cannot be a CodePoint");
            }
            return;
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating)
            || double.IsInfinity(floating)
            || (targetType == BoundType.Float32 && Math.Abs(floating) > float.MaxValue))
        {
            throw Error(argument.Line, argument.Column, $"numeric literal {text} is out of range for {targetName}");
        }
    }

    private static bool IsIntegerType(BoundType type) => type is
        BoundType.Int or BoundType.Int8 or BoundType.Int16 or BoundType.Int64
        or BoundType.UInt8 or BoundType.UInt16 or BoundType.UInt32 or BoundType.UInt64
        or BoundType.Size or BoundType.UIntSize or BoundType.CodePoint;

    private static bool IsSignedIntegerType(BoundType type) => type is
        BoundType.Int or BoundType.Int8 or BoundType.Int16 or BoundType.Int64 or BoundType.Size;

    private static bool IsFloatType(BoundType type) => type is BoundType.Float32 or BoundType.Float64;

    private static bool IsNumericType(BoundType type) => IsIntegerType(type) || IsFloatType(type);

    private bool TryInferEnumConstructor(
        CallExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        out BoundType type)
    {
        type = default;
        if (expression.Path.Count < 2)
        {
            return false;
        }
        var typeName = string.Join('.', expression.Path.Take(expression.Path.Count - 1));
        if (!_types.TryResolve(typeName, out type))
        {
            if (!typeName.StartsWith("Option<", StringComparison.Ordinal)
                && !typeName.StartsWith("Result<", StringComparison.Ordinal))
            {
                return false;
            }
            type = ParseType(typeName, expression.Line, expression.Column);
        }
        if (!_types.IsEnum(type))
        {
            return false;
        }
        EnsureTypeVisible(type, expression.Line, expression.Column);

        var definition = _types.GetEnum(type);
        var variantName = expression.Path[^1];
        var variant = definition.Variants.FirstOrDefault(candidate => candidate.Name == variantName)
            ?? throw Error(
                expression.Line,
                expression.Column,
                $"enum '{definition.Name}' has no variant '{variantName}'");
        if (variant.PayloadType is null)
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"payload-free variant '{definition.Name}.{variant.Name}' uses member syntax without parentheses");
        }

        var expectedCount = variant.PayloadType is null ? 0 : 1;
        if (expression.Arguments.Count != expectedCount)
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"variant '{definition.Name}.{variant.Name}' expects {expectedCount} payload argument(s)");
        }

        if (variant.PayloadType is { } payloadType)
        {
            var actualType = InferExpression(
                expression.Arguments[0],
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            if (actualType != payloadType)
            {
                throw Error(
                    expression.Arguments[0].Line,
                    expression.Arguments[0].Column,
                    $"variant '{definition.Name}.{variant.Name}' expects {FormatType(payloadType)}, got {FormatType(actualType)}");
            }
        }

        return true;
    }

    private BoundType InferUserMethodCallExpression(
        CallExpression expression,
        BoundFunction function,
        string receiverName,
        BoundType receiverType,
        string path)
    {
        if (expression.Arguments.Count != 0)
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"method '{path}' does not accept additional arguments in this slice");
        }

        if (function.InputType != receiverType)
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"method '{path}' cannot be called on {FormatType(receiverType)} value '{receiverName}'");
        }

        return function.ReturnType;
    }

    private bool TryResolveInstanceMethodCall(
        IReadOnlyList<string> path,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        out BoundFunction function,
        out string? receiverName,
        out BoundType? receiverType)
    {
        function = null!;
        receiverName = null;
        receiverType = null;
        if (path.Count != 2 || !bindings.TryGetValue(path[0], out var type))
        {
            return false;
        }

        if (!TryResolveInstanceMethod(type, path[1], functions, out function))
        {
            return false;
        }

        receiverName = path[0];
        receiverType = type;
        return true;
    }

    private bool TryResolveInstanceMethod(
        BoundType receiverType,
        string methodName,
        IReadOnlyDictionary<string, BoundFunction> functions,
        out BoundFunction function)
    {
        function = null!;
        if (!_types.IsStruct(receiverType))
        {
            return false;
        }

        var typeName = _types.GetStruct(receiverType).Name;
        if (methodName.Contains('.', StringComparison.Ordinal))
        {
            var separator = methodName.LastIndexOf('.');
            var traitName = methodName[..separator];
            var memberName = methodName[(separator + 1)..];
            return functions.TryGetValue(traitName + "." + typeName + "." + memberName, out function!)
                && function.InputType == receiverType;
        }

        var inherentName = typeName + "." + methodName;
        if (functions.TryGetValue(inherentName, out function!) && function.InputType == receiverType)
        {
            return true;
        }

        var candidates = functions.Values
            .Where(candidate => candidate.TraitName is not null
                && candidate.InputType == receiverType
                && candidate.Name.EndsWith("." + methodName, StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        if (candidates.Length > 1)
        {
            throw new SmallLangException(
                $"ambiguous trait member '{typeName}.{methodName}'; use 'value -> Trait.{methodName}'");
        }
        if (candidates.Length == 1)
        {
            function = candidates[0];
            return true;
        }

        return false;
    }

    private static bool TryFindTraitImplementation(
        IReadOnlyDictionary<string, BoundFunction> functions,
        string traitName,
        BoundType receiverType,
        out BoundFunction? implementation)
    {
        implementation = functions.Values
            .Where(candidate => candidate.TraitName == traitName
                && candidate.InputType == receiverType)
            .Distinct()
            .FirstOrDefault();
        return implementation is not null;
    }

    private BoundType InferUserCallExpression(
        CallExpression expression,
        BoundFunction function,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        IReadOnlySet<string>? mutableBindings,
        string path)
    {
        if (IsMainOnlyRuntimeWrapper(function) && !allowReadIntCall)
        {
            throw Error(expression.Line, expression.Column, $"{path} is only valid in main for the current runtime slice");
        }

        if (function.InputType is null)
        {
            if (expression.Arguments.Count != 0)
            {
                throw Error(expression.Line, expression.Column, $"function '{path}' does not accept arguments");
            }

            return AsyncCallType(function);
        }

        if (expression.Arguments.Count != 1)
        {
            throw Error(expression.Line, expression.Column, $"function '{path}' expects exactly one argument");
        }

        var argumentType = InferExpression(
            expression.Arguments[0],
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (!CanPassFunctionArgument(argumentType, function.InputType.Value))
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"function '{path}' expects {FormatType(function.InputType.Value)} but received {FormatType(argumentType)}");
        }

        if (FunctionMovesOwnedHeapInput(function))
        {
            EnsureOwnedParameterCallArgument(expression.Arguments[0], path);
        }

        if (FunctionMutablyBorrowsInput(function))
        {
            EnsureMutableBorrowCallArgument(expression.Arguments[0], path, mutableBindings);
        }

        if (FunctionReadonlyBorrowsHeapInput(function, argumentType))
        {
            EnsureReadonlyBorrowCallArgument(expression.Arguments[0], path);
        }

        return AsyncCallType(function);
    }

    private BoundType AsyncCallType(BoundFunction function) =>
        function.IsAsync ? _types.GetOrAddTask(function.ReturnType) : function.ReturnType;

    private void EnsureDisplayable(BoundType type, int line, int column, string path)
    {
        if (type != BoundType.Text && !IsIntegerType(type))
        {
            throw Error(line, column, $"{path} expects Text or Int but received {FormatType(type)}");
        }
    }

    private bool IsMainOnlyRuntimeWrapper(BoundFunction function)
    {
        return function.Name is "sys.io.readInt"
            or "sys.random.seed"
            or "sys.random.below"
            or "sys.file.openIntWriter"
            or "sys.file.writeInt"
            or "sys.file.closeIntWriter"
            or "sys.file.openIntReader"
            or "sys.file.closestInt"
            or "sys.file.closeIntReader"
            or "sys.time.nowMillis"
            or "sys.process.arguments";
    }

    private void EnsureRuntimeIntrinsicAllowed(
        BoundFunction function,
        bool allowRuntimeCall,
        int line,
        int column,
        string path)
    {
        if (!allowRuntimeCall)
        {
            throw Error(line, column, $"{path} is only valid in main for the current runtime slice");
        }
    }

    private void EnsureRuntimeInput(
        BoundType actualType,
        BoundFunction function,
        int line,
        int column,
        string path)
    {
        if (function.InputType is null)
        {
            throw Error(line, column, $"{path} does not accept an input");
        }

        if (actualType != function.InputType)
        {
            throw Error(
                line,
                column,
                $"{path} expects {FormatType(function.InputType.Value)} but received {FormatType(actualType)}");
        }
    }

    private BoundType ResolveBindingType(
        string name,
        IReadOnlyDictionary<string, BoundType> bindings,
        int line,
        int column)
    {
        return bindings.TryGetValue(name, out var type)
            ? type
            : throw Error(line, column, $"unknown binding '{name}'");
    }

    private BoundType InferNameExpression(
        NameExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowRuntimeCall)
    {
        if (bindings.TryGetValue(expression.Name, out var type))
        {
            return type;
        }

        if (TryGetFunction(expression.Name, functions, out var function))
        {
            EnsureFunctionVisible(function, expression.Line, expression.Column);
            EnsureAsyncRuntimeCallable(function, expression.Line, expression.Column, expression.Name);
            if (function.InputType is not null)
            {
                throw Error(
                    expression.Line,
                    expression.Column,
                    $"function '{expression.Name}' expects an argument and must use call or flow syntax");
            }
            if (IsMainOnlyRuntimeWrapper(function) && !allowRuntimeCall)
            {
                throw Error(expression.Line, expression.Column,
                    $"{expression.Name} is only valid in main for the current runtime slice");
            }
            return AsyncCallType(function);
        }

        throw Error(expression.Line, expression.Column, $"unknown binding '{expression.Name}'");
    }

    private void ValidateBindingName(string name, int line, int column)
    {
        if (IsReservedName(name))
        {
            throw Error(line, column, $"binding name '{name}' is reserved");
        }
    }

    private void EnsureAsyncRuntimeCallable(BoundFunction function, int line, int column, string path)
    {
        if (!_currentFunctionIsAsync || _currentFunctionReturnType is null)
        {
            return;
        }

        if (function.Kind == BoundFunctionKind.User && function.IsAsync)
        {
            return;
        }

        throw Error(
            line,
            column,
            $"async function '{path}' is outside the CPU-pure first runtime slice");
    }

    private bool IsReservedName(string name)
    {
        return name is "main"
            or "sys"
            or "print"
            or "println"
            or "readInt"
            or "seedRandom"
            or "randomBelow"
            or "openIntWriter"
            or "writeInt"
            or "closeIntWriter"
            or "openIntReader"
            or "closestInt"
            or "closeIntReader"
            or "nowMillis"
            or "each"
            or "fold"
            or "if"
            or "else"
            or "when"
            or "and"
            or "or"
            or "not"
            or "true"
            or "false"
            or "in"
            or "it"
            or "repeat"
            or "block"
            or "yield"
            or "namespace"
            or "import"
            or "public"
            or "struct"
            or "enum"
            or "trait"
            or "impl"
            or "for"
            or "self"
            or "as"
            or "move"
            or "mut";
    }

    private TypeDefinitionTable BuildTypeDefinitions(
        IReadOnlyList<StructDeclaration> structDeclarations,
        IReadOnlyList<EnumDeclaration> enumDeclarations)
    {
        var names = new Dictionary<string, TypeId>(StringComparer.Ordinal)
        {
            ["Unit"] = BoundType.Unit,
            ["Text"] = BoundType.Text,
            ["Int"] = BoundType.Int,
            ["Int8"] = BoundType.Int8,
            ["Int16"] = BoundType.Int16,
            ["Int32"] = BoundType.Int,
            ["Int64"] = BoundType.Int64,
            ["Long"] = BoundType.Int64,
            ["UInt8"] = BoundType.UInt8,
            ["UInt16"] = BoundType.UInt16,
            ["UInt32"] = BoundType.UInt32,
            ["UInt64"] = BoundType.UInt64,
            ["Size"] = BoundType.Size,
            ["UIntSize"] = BoundType.UIntSize,
            ["CodePoint"] = BoundType.CodePoint,
            ["Arena"] = BoundType.Arena,
            ["Arguments"] = BoundType.Arguments,
            ["MappedBytes"] = BoundType.MappedBytes,
            ["MutableMappedBytes"] = BoundType.MutableMappedBytes,
            ["Float"] = BoundType.Float32,
            ["Float32"] = BoundType.Float32,
            ["Float64"] = BoundType.Float64,
            ["Double"] = BoundType.Float64,
            ["Bool"] = BoundType.Bool,
            ["[Int]"] = BoundType.IntSlice,
            ["[Int; ~]"] = BoundType.DynamicIntArray,
            ["{Int: Int}"] = BoundType.IntDictionary
        };
        var structTypes = new Dictionary<StructDeclaration, TypeId>(ReferenceEqualityComparer.Instance);
        var enumTypes = new Dictionary<EnumDeclaration, TypeId>(ReferenceEqualityComparer.Instance);
        var nextTypeId = (int)TypeId.FirstUserDefined;

        foreach (var declaration in structDeclarations)
        {
            _currentModuleName = declaration.ModuleName;
            if (IsReservedName(declaration.Name))
            {
                throw Error(declaration.Line, declaration.Column, $"type name '{declaration.Name}' is reserved");
            }

            var id = (TypeId)nextTypeId++;
            if (!names.TryAdd(declaration.Name, id))
            {
                throw Error(declaration.Line, declaration.Column, $"type '{declaration.Name}' already exists");
            }

            structTypes.Add(declaration, id);
        }

        foreach (var declaration in enumDeclarations)
        {
            _currentModuleName = declaration.ModuleName;
            if (IsReservedName(declaration.Name))
            {
                throw Error(declaration.Line, declaration.Column, $"type name '{declaration.Name}' is reserved");
            }

            var id = (TypeId)nextTypeId++;
            if (!names.TryAdd(declaration.Name, id))
            {
                throw Error(declaration.Line, declaration.Column, $"type '{declaration.Name}' already exists");
            }

            enumTypes.Add(declaration, id);
        }

        var boxes = new Dictionary<TypeId, BoundBoxDefinition>();
        var predeclaredDynamicArrays = new Dictionary<TypeId, TypeId>();
        var predeclaredDynamicArraysByElement = new Dictionary<TypeId, TypeId>();
        var boxableTypes = names
            .Where(item => item.Value is TypeId.Int or TypeId.Bool or TypeId.Text
                || structTypes.Values.Contains(item.Value)
                || enumTypes.Values.Contains(item.Value))
            .OrderBy(item => (int)item.Value)
            .ToArray();
        foreach (var (name, elementType) in boxableTypes)
        {
            var id = (TypeId)nextTypeId++;
            names.Add("box " + name, id);
            boxes.Add(id, new BoundBoxDefinition(id, elementType, Size: 0, Alignment: 1));
        }

        var structs = new Dictionary<TypeId, BoundStructDefinition>();
        foreach (var declaration in structDeclarations)
        {
            _currentModuleName = declaration.ModuleName;
            var fields = new List<BoundStructField>(declaration.Fields.Count);
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < declaration.Fields.Count; index++)
            {
                var field = declaration.Fields[index];
                ValidateBindingName(field.Name, field.Line, field.Column);
                if (!fieldNames.Add(field.Name))
                {
                    throw Error(field.Line, field.Column, $"field '{field.Name}' already exists in struct '{declaration.Name}'");
                }

                if (!names.TryGetValue(field.TypeName, out var fieldType)
                    && !TryResolveDefinitionDynamicArray(field.TypeName, out fieldType))
                {
                    throw Error(field.Line, field.Column, $"unknown type '{field.TypeName}'");
                }

                if (fieldType is TypeId.Unit
                    or TypeId.IntSlice
                    or TypeId.StaticIntArray
                    or TypeId.IntDictionaryView)
                {
                    throw Error(
                        field.Line,
                        field.Column,
                        $"struct field '{field.Name}' must be an inline value type");
                }

                fields.Add(new BoundStructField(field.Name, fieldType, index, field.Line, field.Column));
            }

            var id = structTypes[declaration];
            structs.Add(id, new BoundStructDefinition(
                id,
                declaration.Name,
                fields,
                declaration.Line,
                declaration.Column,
                declaration.ModuleName,
                declaration.IsPublic,
                declaration.DeclaringTypeName));
        }

        var enums = new Dictionary<TypeId, BoundEnumDefinition>();
        foreach (var declaration in enumDeclarations)
        {
            _currentModuleName = declaration.ModuleName;
            if (declaration.Variants.Count == 0)
            {
                throw Error(declaration.Line, declaration.Column, $"enum '{declaration.Name}' requires at least one variant");
            }

            var variants = new List<BoundEnumVariant>(declaration.Variants.Count);
            var variantNames = new HashSet<string>(StringComparer.Ordinal);
            for (var tag = 0; tag < declaration.Variants.Count; tag++)
            {
                var variant = declaration.Variants[tag];
                if (!variantNames.Add(variant.Name))
                {
                    throw Error(
                        variant.Line,
                        variant.Column,
                        $"variant '{variant.Name}' already exists in enum '{declaration.Name}'");
                }

                TypeId? payloadType = null;
                if (variant.PayloadType is not null)
                {
                    if (!names.TryGetValue(variant.PayloadType, out var resolvedPayloadType))
                    {
                        throw Error(variant.Line, variant.Column, $"unknown type '{variant.PayloadType}'");
                    }

                    if (resolvedPayloadType is TypeId.Unit
                        or TypeId.IntSlice
                        or TypeId.StaticIntArray
                        or TypeId.DynamicIntArray
                        or TypeId.IntDictionaryView
                        or TypeId.IntDictionary)
                    {
                        throw Error(
                            variant.Line,
                            variant.Column,
                            $"enum variant '{variant.Name}' payload must be an inline value type");
                    }

                    payloadType = resolvedPayloadType;
                }

                variants.Add(new BoundEnumVariant(
                    variant.Name,
                    payloadType,
                    tag,
                    variant.Line,
                    variant.Column));
            }

            var id = enumTypes[declaration];
            enums.Add(id, new BoundEnumDefinition(
                id,
                declaration.Name,
                variants,
                PayloadWords: 0,
                declaration.Line,
                declaration.Column,
                declaration.ModuleName,
                declaration.IsPublic));
        }

        ValidateAcyclicValueTypes(structs, enums);
        foreach (var (id, definition) in enums.ToArray())
        {
            var payloadBytes = definition.Variants
                .Where(static variant => variant.PayloadType is not null)
                .Select(variant => InlineSize(variant.PayloadType!.Value, structs, enums, boxes, predeclaredDynamicArrays))
                .DefaultIfEmpty(0)
                .Max();
            enums[id] = definition with { PayloadWords = (payloadBytes + 7) / 8 };
        }

        foreach (var (id, definition) in boxes.ToArray())
        {
            var size = InlineSize(definition.ElementType, structs, enums, boxes, predeclaredDynamicArrays);
            boxes[id] = definition with
            {
                Size = size,
                Alignment = Math.Min(Math.Max(size, 1), 8)
            };
        }

        var result = new TypeDefinitionTable(names, structs, enums, boxes, _pointerBitWidth / 8);
        foreach (var (id, elementType) in predeclaredDynamicArrays)
        {
            result.RegisterDynamicArray(id, elementType);
        }
        return result;

        bool TryResolveDefinitionDynamicArray(string typeName, out TypeId type)
        {
            type = default;
            if (!typeName.StartsWith('[', StringComparison.Ordinal)
                || !typeName.EndsWith("; ~]", StringComparison.Ordinal))
            {
                return false;
            }

            var elementName = typeName[1..^4].Trim();
            if (!names.TryGetValue(elementName, out var elementType)
                || elementType == BoundType.Unit)
            {
                return false;
            }
            if (elementType == BoundType.Int)
            {
                type = BoundType.DynamicIntArray;
                return true;
            }
            if (predeclaredDynamicArraysByElement.TryGetValue(elementType, out type))
            {
                return true;
            }

            type = (TypeId)nextTypeId++;
            predeclaredDynamicArrays.Add(type, elementType);
            predeclaredDynamicArraysByElement.Add(elementType, type);
            names.TryAdd(typeName, type);
            return true;
        }
    }

    private void ValidateAcyclicValueTypes(
        IReadOnlyDictionary<TypeId, BoundStructDefinition> structs,
        IReadOnlyDictionary<TypeId, BoundEnumDefinition> enums)
    {
        var states = new Dictionary<TypeId, int>();
        foreach (var definition in structs.Values)
        {
            Visit(definition.Id, definition.Name, definition.Line, definition.Column);
        }
        foreach (var definition in enums.Values)
        {
            Visit(definition.Id, definition.Name, definition.Line, definition.Column);
        }

        void Visit(TypeId id, string name, int line, int column)
        {
            if (states.TryGetValue(id, out var state))
            {
                if (state == 1)
                {
                    throw Error(
                        line,
                        column,
                        $"type '{name}' recursively contains itself; recursive values require an explicit heap reference type");
                }

                return;
            }

            states[id] = 1;
            if (structs.TryGetValue(id, out var structure))
            {
                foreach (var field in structure.Fields)
                {
                    VisitDependency(field.Type);
                }
            }
            else if (enums.TryGetValue(id, out var enumeration))
            {
                foreach (var variant in enumeration.Variants)
                {
                    if (variant.PayloadType is { } payloadType)
                    {
                        VisitDependency(payloadType);
                    }
                }
            }

            states[id] = 2;

            void VisitDependency(TypeId dependency)
            {
                if (structs.TryGetValue(dependency, out var nestedStruct))
                {
                    Visit(nestedStruct.Id, nestedStruct.Name, nestedStruct.Line, nestedStruct.Column);
                }
                else if (enums.TryGetValue(dependency, out var nestedEnum))
                {
                    Visit(nestedEnum.Id, nestedEnum.Name, nestedEnum.Line, nestedEnum.Column);
                }
            }
        }
    }

    private int InlineSize(
        TypeId type,
        IReadOnlyDictionary<TypeId, BoundStructDefinition> structs,
        IReadOnlyDictionary<TypeId, BoundEnumDefinition> enums,
        IReadOnlyDictionary<TypeId, BoundBoxDefinition> boxes,
        IReadOnlyDictionary<TypeId, TypeId> dynamicArrays)
    {
        if (boxes.ContainsKey(type))
        {
            return 8;
        }

        if (structs.TryGetValue(type, out var structure))
        {
            var offset = 0;
            var maxAlignment = 1;
            foreach (var field in structure.Fields)
            {
                var size = InlineSize(field.Type, structs, enums, boxes, dynamicArrays);
                var alignment = Math.Min(Math.Max(size, 1), 8);
                offset = AlignUp(offset, alignment);
                offset += size;
                maxAlignment = Math.Max(maxAlignment, alignment);
            }

            return AlignUp(offset, maxAlignment);
        }

        if (enums.TryGetValue(type, out var enumeration))
        {
            var payloadBytes = enumeration.Variants
                .Where(static variant => variant.PayloadType is not null)
                .Select(variant => InlineSize(variant.PayloadType!.Value, structs, enums, boxes, dynamicArrays))
                .DefaultIfEmpty(0)
                .Max();
            return 8 + AlignUp(payloadBytes, 8);
        }

        if (dynamicArrays.ContainsKey(type))
        {
            return 3 * (_pointerBitWidth / 8);
        }

        return type switch
        {
            TypeId.Bool => 1,
            TypeId.Int8 or TypeId.UInt8 => 1,
            TypeId.Int16 or TypeId.UInt16 => 2,
            TypeId.Int or TypeId.UInt32 or TypeId.Float32 => 4,
            TypeId.CodePoint => 4,
            TypeId.Int64 or TypeId.UInt64 or TypeId.Float64 => 8,
            TypeId.Size or TypeId.UIntSize => _pointerBitWidth / 8,
            TypeId.Text => 16,
            TypeId.Arguments => 8,
            TypeId.Arena => 24,
            TypeId.MappedBytes or TypeId.MutableMappedBytes => 40,
            TypeId.DynamicIntArray or TypeId.IntDictionary => 24,
            _ => throw new InvalidOperationException($"type {type} has no inline size")
        };
    }

    private int AlignUp(int value, int alignment)
    {
        return checked((value + alignment - 1) / alignment * alignment);
    }

    private BoundType ParseType(string typeName, int line, int column)
    {
        if (typeName.StartsWith("Option<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            var valueName = typeName[7..^1].Trim();
            var valueType = ParseType(valueName, line, column);
            var option = _types.GetOrAddOption(valueType, $"Option<{FormatType(valueType)}>");
            _types.AddAlias(typeName, option);
            return option;
        }
        if (typeName.StartsWith("Result<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            var arguments = typeName[7..^1];
            var separator = FindTopLevelTypeComma(arguments);
            if (separator < 0)
            {
                throw Error(line, column, "Result requires success and error types");
            }
            var okType = ParseType(arguments[..separator].Trim(), line, column);
            var errorType = ParseType(arguments[(separator + 1)..].Trim(), line, column);
            var result = _types.GetOrAddResult(
                okType,
                errorType,
                $"Result<{FormatType(okType)}, {FormatType(errorType)}>");
            _types.AddAlias(typeName, result);
            return result;
        }
        if (typeName.StartsWith('[', StringComparison.Ordinal)
            && typeName.EndsWith("; ~]", StringComparison.Ordinal))
        {
            var elementName = typeName[1..^4].Trim();
            var elementType = ParseType(elementName, line, column);
            if (elementType == BoundType.Unit || IsNestedContainerElementType(elementType))
            {
                throw Error(line, column, "growable array elements must be inline scalar or user values");
            }
            return elementType == BoundType.Int
                ? BoundType.DynamicIntArray
                : _types.GetOrAddDynamicArray(elementType);
        }
        if (typeName.Length >= 5 && typeName[0] == '{' && typeName[^1] == '}')
        {
            var separator = typeName.IndexOf(':', StringComparison.Ordinal);
            if (separator > 1)
            {
                var keyName = typeName[1..separator].Trim();
                var valueName = typeName[(separator + 1)..^1].Trim();
                var keyType = ParseType(keyName, line, column);
                var valueType = ParseType(valueName, line, column);
                if (!IsSupportedDictionaryKeyType(keyType))
                {
                    throw Error(line, column,
                        $"dictionary key type {FormatType(keyType)} must implement Hash.hash: self -> Int and Eq.eq: self -> Int");
                }
                return keyType == BoundType.Int && valueType == BoundType.Int
                    ? BoundType.IntDictionary
                    : _types.GetOrAddDictionary(keyType, valueType);
            }
        }
        if (!_types.TryResolve(typeName, out var type))
        {
            throw Error(line, column, $"unknown type '{typeName}'");
        }

        EnsureTypeVisible(type, line, column);
        return type;
    }

    private BoundType ParseFunctionType(
        string typeName,
        string? genericParameterName,
        string? secondaryGenericParameterName,
        int line,
        int column)
    {
        if (genericParameterName is not null && typeName == genericParameterName)
        {
            return BoundType.GenericParameter;
        }
        if (secondaryGenericParameterName is not null && typeName == secondaryGenericParameterName)
        {
            return BoundType.SecondaryGenericParameter;
        }
        if (genericParameterName is not null && typeName == $"[Int; {genericParameterName}]")
        {
            return BoundType.IntSlice;
        }
        if (typeName.StartsWith("Option<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            var value = ParseFunctionType(typeName[7..^1].Trim(), genericParameterName,
                secondaryGenericParameterName, line, column);
            return _types.GetOrAddOption(value, $"Option<{FormatType(value)}>");
        }
        if (typeName.StartsWith("Result<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            var arguments = typeName[7..^1];
            var separator = FindTopLevelTypeComma(arguments);
            if (separator < 0)
            {
                throw Error(line, column, "Result requires success and error types");
            }
            var ok = ParseFunctionType(arguments[..separator].Trim(), genericParameterName,
                secondaryGenericParameterName, line, column);
            var error = ParseFunctionType(arguments[(separator + 1)..].Trim(), genericParameterName,
                secondaryGenericParameterName, line, column);
            return _types.GetOrAddResult(ok, error, $"Result<{FormatType(ok)}, {FormatType(error)}>");
        }

        return ParseType(typeName, line, column);
    }

    private static int FindTopLevelTypeComma(string text)
    {
        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            depth += text[index] switch
            {
                '[' or '{' or '<' => 1,
                ']' or '}' or '>' => -1,
                _ => 0
            };
            if (text[index] == ',' && depth == 0)
            {
                return index;
            }
        }
        return -1;
    }

    private string FormatType(BoundType type)
    {
        if (_types.TryGetOptionValue(type, out var optionValue))
        {
            return $"Option<{FormatType(optionValue)}>";
        }
        if (_types.TryGetResultTypes(type, out var resultTypes))
        {
            return $"Result<{FormatType(resultTypes.Ok)}, {FormatType(resultTypes.Error)}>";
        }
        if (_types.IsStruct(type))
        {
            return _types.GetStruct(type).Name;
        }

        if (_types.IsEnum(type))
        {
            return _types.GetEnum(type).Name;
        }
        if (_types.IsBox(type))
        {
            return "box " + FormatType(_types.GetBox(type).ElementType);
        }
        if (_types.IsStaticArray(type))
        {
            return $"[{FormatType(_types.GetStaticArray(type).ElementType)}; N]";
        }
        if (_types.IsDynamicArray(type))
        {
            return $"[{FormatType(_types.GetDynamicArray(type).ElementType)}; ~]";
        }
        if (_types.IsDictionary(type))
        {
            var dictionary = _types.GetDictionary(type);
            return $"{{{FormatType(dictionary.KeyType)}: {FormatType(dictionary.ValueType)}}}";
        }
        if (_types.TryGetTaskValue(type, out var taskValue))
        {
            return $"Task<{FormatType(taskValue)}>";
        }

        return type switch
        {
            BoundType.Unit => "Unit",
            BoundType.Text => "Text",
            BoundType.Int => "Int",
            BoundType.Int8 => "Int8",
            BoundType.Int16 => "Int16",
            BoundType.Int64 => "Long",
            BoundType.UInt8 => "UInt8",
            BoundType.UInt16 => "UInt16",
            BoundType.UInt32 => "UInt32",
            BoundType.UInt64 => "UInt64",
            BoundType.Size => "Size",
            BoundType.UIntSize => "UIntSize",
            BoundType.CodePoint => "CodePoint",
            BoundType.Arena => "Arena",
            BoundType.Arguments => "Arguments",
            BoundType.MappedBytes => "MappedBytes",
            BoundType.MutableMappedBytes => "MutableMappedBytes",
            BoundType.Float32 => "Float",
            BoundType.Float64 => "Double",
            BoundType.Bool => "Bool",
            BoundType.IntSlice => "[Int]",
            BoundType.StaticIntArray => "[Int; N]",
            BoundType.StaticTextArray => "[Text; N]",
            BoundType.DynamicIntArray => "[Int; ~]",
            BoundType.IntDictionaryView => "{Int: Int}",
            BoundType.IntDictionary => "{Int: Int}",
            BoundType.GenericParameter => "generic parameter",
            BoundType.SecondaryGenericParameter => "secondary generic parameter",
            _ => type.ToString()
        };
    }

    private bool IsContainerType(BoundType type)
    {
        return type is BoundType.StaticIntArray or BoundType.StaticTextArray or BoundType.DynamicIntArray or BoundType.IntDictionary
            || _types.IsStaticArray(type)
            || _types.IsDynamicArray(type)
            || _types.IsDictionary(type)
            || _types.ContainsOwnedStorage(type);
    }

    private bool IsNestedContainerElementType(BoundType type)
    {
        return type is BoundType.IntSlice
            or BoundType.StaticIntArray
            or BoundType.StaticTextArray
            or BoundType.DynamicIntArray
            or BoundType.IntDictionaryView
            or BoundType.IntDictionary
            || _types.IsStaticArray(type)
            || _types.IsDynamicArray(type)
            || _types.IsDictionary(type);
    }

    private bool IsSupportedDictionaryKeyType(BoundType type)
    {
        if (type is BoundType.Int or BoundType.Text)
        {
            return true;
        }
        if (!_types.IsStruct(type) && !_types.IsEnum(type))
        {
            return false;
        }
        if (_types.ContainsOwnedStorage(type))
        {
            return false;
        }

        return HasDictionaryKeyTrait(type, "Hash", "hash")
            && HasDictionaryKeyTrait(type, "Eq", "eq");
    }

    private bool HasDictionaryKeyTrait(BoundType type, string traitName, string methodName)
    {
        var typeName = _types.IsStruct(type)
            ? _types.GetStruct(type).Name
            : _types.GetEnum(type).Name;
        return _program.Functions.Any(function =>
            function.TraitName == traitName
            && function.InputType == typeName
            && function.Name.EndsWith('.' + methodName, StringComparison.Ordinal)
            && function.ReturnType == "Int"
            && function.InputOwnership == FunctionInputOwnership.Default);
    }

    private bool IsReadonlyIntViewCompatible(BoundType type)
    {
        return type is BoundType.IntSlice or BoundType.StaticIntArray or BoundType.DynamicIntArray;
    }

    private bool CanPassFunctionArgument(BoundType actualType, BoundType expectedType)
    {
        return actualType == expectedType
            || (expectedType == BoundType.IntSlice && IsReadonlyIntViewCompatible(actualType))
            || (expectedType == BoundType.IntDictionaryView && actualType == BoundType.IntDictionary);
    }

    private bool IsOwnedHeapType(BoundType type)
    {
        return _types.ContainsOwnedStorage(type);
    }

    private bool IsAsyncResultTypeSupported(BoundType type)
    {
        return type == BoundType.Unit || IsAsyncValueTypeSupported(type);
    }

    private bool IsAsyncInputTypeSupported(
        BoundType? type,
        BoundFunctionInputOwnership ownership)
    {
        if (type is null)
        {
            return true;
        }

        if (ownership == BoundFunctionInputOwnership.MutableBorrow
            || !IsAsyncValueTypeSupported(type.Value))
        {
            return false;
        }

        return !_types.ContainsOwnedStorage(type.Value)
            || ownership == BoundFunctionInputOwnership.Move;
    }

    private bool IsAsyncValueTypeSupported(BoundType type)
    {
        return IsAsyncValueTypeSupported(type, []);
    }

    private bool IsAsyncValueTypeSupported(BoundType type, HashSet<BoundType> visiting)
    {
        if (type is BoundType.Unit or BoundType.Text or BoundType.Bool
            or BoundType.DynamicIntArray or BoundType.IntDictionary
            || IsNumericType(type))
        {
            return true;
        }

        if (!visiting.Add(type))
        {
            return true;
        }

        try
        {
            if (_types.IsStruct(type))
            {
                return _types.GetStruct(type).Fields.All(
                    field => IsAsyncValueTypeSupported(field.Type, visiting));
            }
            if (_types.IsEnum(type))
            {
                return _types.GetEnum(type).Variants.All(
                    variant => variant.PayloadType is null
                        || IsAsyncValueTypeSupported(variant.PayloadType.Value, visiting));
            }
            if (_types.IsBox(type))
            {
                return IsAsyncValueTypeSupported(_types.GetBox(type).ElementType, visiting);
            }
            if (_types.IsStaticArray(type))
            {
                return IsAsyncValueTypeSupported(_types.GetStaticArray(type).ElementType, visiting);
            }
            if (_types.IsDynamicArray(type))
            {
                return IsAsyncValueTypeSupported(_types.GetDynamicArray(type).ElementType, visiting);
            }
            if (_types.IsDictionary(type))
            {
                var dictionary = _types.GetDictionary(type);
                return IsAsyncValueTypeSupported(dictionary.KeyType, visiting)
                    && IsAsyncValueTypeSupported(dictionary.ValueType, visiting);
            }

            return false;
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private bool IsContainerCreationExpression(Expression expression)
    {
        return expression is ArrayLiteralExpression
            or ArrayRepeatExpression
            or TypedEmptyArrayExpression
            or DictionaryLiteralExpression
            or TypedEmptyDictionaryExpression
            or BoxExpression
            or MapExpression
            or StructLiteralExpression
            or CallExpression
            or TryExpression
            or FlowExpression
            or IfExpression
            or WhenExpression
            || IsZeroArgumentFunctionCreationExpression(expression)
            || IsAssociatedOwnedCreationExpression(expression)
            || IsMoveConsumingContainerTransformExpression(expression);
    }

    private bool IsZeroArgumentFunctionCreationExpression(Expression expression)
    {
        if (_boundFunctions is null) return false;
        var functionName = expression switch
        {
            NameExpression name => name.Name,
            FieldAccessExpression { Source: NameExpression owner } field
                => owner.Name + "." + field.FieldName,
            _ => null
        };
        return functionName is not null
            && _boundFunctions.TryGetValue(functionName, out var function)
            && function.InputType is null
            && (IsOwnedHeapType(function.ReturnType) || function.IsAsync);
    }

    private bool IsAssociatedOwnedCreationExpression(Expression expression)
    {
        return expression is FieldAccessExpression
        {
            Source: NameExpression typeName
        } field
            && _types.TryResolve(typeName.Name, out var ownerType)
            && _types.IsStruct(ownerType)
            && _boundFunctions is not null
            && _boundFunctions.TryGetValue(typeName.Name + "." + field.FieldName, out var function)
            && function.InputType is null;
    }

    private bool IsOwnedHeapContainerCreationExpression(Expression expression)
    {
        return expression is ArrayLiteralExpression { IsDynamic: true }
            or TypedEmptyArrayExpression
            or DictionaryLiteralExpression
            or TypedEmptyDictionaryExpression
            or BoxExpression;
    }

    private bool IsAnonymousOwnedHeapContainerExpression(Expression expression)
    {
        return expression switch
        {
            NameExpression => false,
            FieldAccessExpression field => IsAnonymousOwnedHeapContainerExpression(field.Source),
            _ => true
        };
    }

    private bool IsMoveConsumingContainerTransformExpression(Expression expression)
    {
        if (expression is not FlowExpression flow || flow.Targets.Count == 0)
        {
            return false;
        }

        var lastTarget = flow.Targets[^1];
        if (lastTarget.Path.Count != 1)
        {
            return false;
        }

        return lastTarget.Path[0] is "append" or "updated" or "await";
    }

    private string? GetMoveConsumingContainerSourceName(Expression expression)
    {
        if (!IsMoveConsumingContainerTransformExpression(expression)
            || expression is not FlowExpression flow
            || flow.Source is not NameExpression name)
        {
            return null;
        }

        return name.Name;
    }

    private IReadOnlyList<string> GetOwnedStructFieldSourceNames(
        Expression expression,
        IReadOnlyDictionary<string, BoundType> bindings)
    {
        if (expression is not StructLiteralExpression literal
            || !_types.TryResolve(literal.TypeName, out var type)
            || !_types.IsStruct(type))
        {
            return [];
        }

        var definition = _types.GetStruct(type);
        var transferred = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var initializer in literal.Fields)
        {
            var field = definition.Fields.FirstOrDefault(candidate => candidate.Name == initializer.Name);
            if (field is null
                || !_types.ContainsOwnedStorage(field.Type)
                || initializer.Value is not NameExpression name
                || !bindings.TryGetValue(name.Name, out var sourceType)
                || sourceType != field.Type)
            {
                continue;
            }

            if (!unique.Add(name.Name))
            {
                throw Error(
                    initializer.Line,
                    initializer.Column,
                    $"owned binding '{name.Name}' cannot initialize more than one struct field");
            }
            transferred.Add(name.Name);
        }
        return transferred;
    }

    private string? GetMoveConsumingOwnedFieldOwnerName(
        Expression expression,
        IReadOnlyDictionary<string, BoundType> bindings)
    {
        if (expression is not FieldAccessExpression { Source: NameExpression owner } field
            || !string.Equals(owner.Name, _currentMoveInputName, StringComparison.Ordinal)
            || !bindings.TryGetValue(owner.Name, out var ownerType)
            || !_types.IsStruct(ownerType))
        {
            return null;
        }

        var fieldDefinition = _types.GetStruct(ownerType).Fields
            .FirstOrDefault(candidate => candidate.Name == field.FieldName);
        return fieldDefinition is not null && _types.ContainsOwnedStorage(fieldDefinition.Type)
            ? owner.Name
            : null;
    }

    private void EnsureOwnedContainerCanLeaveBlock(
        Expression expression,
        IReadOnlyDictionary<string, BoundType> outerBindings,
        IReadOnlyDictionary<string, BoundType> bodyBindings,
        string? allowedOwnedOuterResultName = null)
    {
        if (expression is NameExpression name)
        {
            EnsureBlockLocalOwner(
                name.Name,
                name.Line,
                name.Column,
                outerBindings,
                bodyBindings,
                allowedOwnedOuterResultName);
            return;
        }

        var movedSourceName = GetMoveConsumingContainerSourceName(expression);
        if (movedSourceName is not null)
        {
            EnsureBlockLocalOwner(
                movedSourceName,
                expression.Line,
                expression.Column,
                outerBindings,
                bodyBindings,
                allowedOwnedOuterResultName);
            return;
        }

        if (expression is ArrayLiteralExpression { IsDynamic: true }
            or TypedEmptyArrayExpression
            or DictionaryLiteralExpression
            or TypedEmptyDictionaryExpression
            or BoxExpression
            or CallExpression
            or FlowExpression
            or IfExpression
            or WhenExpression)
        {
            return;
        }

        throw Error(
            expression.Line,
            expression.Column,
            "owned container block results must be created in that block or moved from a block-local owner");
    }

    private void EnsureBlockLocalOwner(
        string name,
        int line,
        int column,
        IReadOnlyDictionary<string, BoundType> outerBindings,
        IReadOnlyDictionary<string, BoundType> bodyBindings,
        string? allowedOwnedOuterResultName)
    {
        if (!bodyBindings.TryGetValue(name, out var type) || !IsContainerType(type))
        {
            throw Error(line, column, $"unknown owned container '{name}'");
        }

        if (outerBindings.ContainsKey(name)
            && !string.Equals(name, allowedOwnedOuterResultName, StringComparison.Ordinal))
        {
            throw Error(
                line,
                column,
                "owned container block results must move a block-local owner, not an owner from an outer scope");
        }
    }

    private void EnsureMutableContainerSource(
        Expression source,
        string operation,
        IReadOnlySet<string>? mutableBindings)
    {
        if (source is not NameExpression name)
        {
            throw Error(
                source.Line,
                source.Column,
                $"{operation} requires a named mutable container binding");
        }

        if (mutableBindings is null || !mutableBindings.Contains(name.Name))
        {
            throw Error(
                source.Line,
                source.Column,
                $"{operation} requires a mutable owner binding; use '=> {name.Name.TrimEnd('!')}!'");
        }
    }

    private void EnsureMoveContainerSource(Expression source, string operation)
    {
        if (source is not NameExpression)
        {
            throw Error(
                source.Line,
                source.Column,
                $"{operation} requires a named container owner so ownership can move");
        }
    }

    private void EnsureOwnedParameterFlowSource(Expression source, string functionName)
    {
        if (source is not NameExpression)
        {
            throw Error(
                source.Line,
                source.Column,
                $"function '{functionName}' consumes an owned container, so the flowed input must be a named owner");
        }
    }

    private void EnsureOwnedParameterCallArgument(Expression argument, string functionName)
    {
        if (argument is not NameExpression)
        {
            throw Error(
                argument.Line,
                argument.Column,
                $"function '{functionName}' consumes an owned container, so the argument must be a named owner");
        }
    }

    private void EnsureMutableBorrowFlowSource(
        Expression source,
        string functionName,
        IReadOnlySet<string>? mutableBindings)
    {
        if (source is not NameExpression name)
        {
            throw Error(
                source.Line,
                source.Column,
                $"function '{functionName}' mutably borrows a container, so the flowed input must be a named mutable owner");
        }

        EnsureMutableBorrowName(name, functionName, mutableBindings);
    }

    private void EnsureMutableBorrowCallArgument(
        Expression argument,
        string functionName,
        IReadOnlySet<string>? mutableBindings)
    {
        if (argument is not NameExpression name)
        {
            throw Error(
                argument.Line,
                argument.Column,
                $"function '{functionName}' mutably borrows a container, so the argument must be a named mutable owner");
        }

        EnsureMutableBorrowName(name, functionName, mutableBindings);
    }

    private void EnsureMutableBorrowName(
        NameExpression name,
        string functionName,
        IReadOnlySet<string>? mutableBindings)
    {
        if (mutableBindings is null || !mutableBindings.Contains(name.Name))
        {
            throw Error(
                name.Line,
                name.Column,
                $"function '{functionName}' mutably borrows a container; use a mutable owner binding such as '{name.Name.TrimEnd('!')}!'");
        }
    }

    private void EnsureReadonlyBorrowCallArgument(Expression argument, string functionName)
    {
        if (argument is not NameExpression)
        {
            throw Error(
                argument.Line,
                argument.Column,
                $"function '{functionName}' borrows a heap container for the call, so the argument must be a named owner");
        }
    }

    private IReadOnlyList<string> GetOwnedParameterConsumedSourceNames(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings)
    {
        if (expression is TryExpression { Value: NameExpression attemptedName }
            && string.Equals(attemptedName.Name, _currentMoveInputName, StringComparison.Ordinal))
        {
            return [attemptedName.Name];
        }

        if (expression is TryExpression attempt)
        {
            return GetOwnedParameterConsumedSourceNames(attempt.Value, functions, bindings);
        }

        if (expression is CallExpression enumCall
            && TryGetOwnedEnumConstructorSourceName(enumCall, out var enumSourceName))
        {
            return [enumSourceName];
        }

        if (expression is CallExpression call
            && TryGetFunction(call.Path, functions, out var callFunction)
            && FunctionMovesOwnedHeapInput(callFunction)
            && call.Arguments.Count == 1
            && call.Arguments[0] is NameExpression argumentName)
        {
            return [argumentName.Name];
        }

        if (expression is FlowExpression flow)
        {
            var consumed = new List<string>();
            var sourceType = flow.Source is NameExpression typedSource
                && bindings.TryGetValue(typedSource.Name, out var resolvedSourceType)
                    ? resolvedSourceType
                    : (BoundType?)null;
            foreach (var target in flow.Targets)
            {
                var path = string.Join('.', target.Path);
                if ((TryGetFunction(target.Path, functions, out var targetFunction)
                        || (sourceType is { } receiverType
                            && TryResolveInstanceMethod(receiverType, path, functions, out targetFunction)))
                    && FunctionMovesOwnedHeapInput(targetFunction)
                    && flow.Source is NameExpression sourceName)
                {
                    consumed.Add(sourceName.Name);
                }
            }

            return consumed;
        }

        return [];
    }

    private bool TryGetOwnedEnumConstructorSourceName(CallExpression expression, out string sourceName)
    {
        sourceName = "";
        if (expression.Path.Count < 2
            || expression.Arguments.Count != 1
            || expression.Arguments[0] is not NameExpression name)
        {
            return false;
        }
        var typeName = string.Join('.', expression.Path.Take(expression.Path.Count - 1));
        if (!_types.TryResolve(typeName, out var type) || !_types.IsEnum(type))
        {
            return false;
        }
        var variant = _types.GetEnum(type).Variants
            .FirstOrDefault(candidate => candidate.Name == expression.Path[^1]);
        if (variant?.PayloadType is not { } payloadType || !_types.ContainsOwnedStorage(payloadType))
        {
            return false;
        }
        sourceName = name.Name;
        return true;
    }

    private void EnsureMoveInputReturnCoverage(
        Expression expression,
        string inputName,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        if (GetMoveInputDisposition(expression, inputName, functions, isResult: true)
            == MoveInputDisposition.Mixed)
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"move input '{inputName}' must be transferred on every return branch or on none of them");
        }
    }

    private MoveInputDisposition GetMoveInputDisposition(
        Expression expression,
        string inputName,
        IReadOnlyDictionary<string, BoundFunction> functions,
        bool isResult)
    {
        if (isResult && expression is NameExpression name && name.Name == inputName)
        {
            return MoveInputDisposition.Transferred;
        }

        if (string.Equals(
            GetMoveConsumingContainerSourceName(expression),
            inputName,
            StringComparison.Ordinal))
        {
            return MoveInputDisposition.Transferred;
        }

        if (expression is CallExpression call
            && TryGetFunction(call.Path, functions, out var callFunction)
            && FunctionMovesOwnedHeapInput(callFunction)
            && call.Arguments.Count == 1
            && call.Arguments[0] is NameExpression argumentName
            && argumentName.Name == inputName)
        {
            return MoveInputDisposition.Transferred;
        }

        if (expression is FlowExpression flow
            && flow.Source is NameExpression sourceName
            && sourceName.Name == inputName
            && flow.Targets.Any(target =>
                TryGetFunction(target.Path, functions, out var targetFunction)
                && FunctionMovesOwnedHeapInput(targetFunction)))
        {
            return MoveInputDisposition.Transferred;
        }

        if (expression is IfExpression conditional && conditional.Else is not null)
        {
            return CombineAlternativeMoveInputDispositions(
                GetMoveInputDisposition(conditional.Then, inputName, functions),
                GetMoveInputDisposition(conditional.Else, inputName, functions));
        }

        if (expression is WhenExpression whenExpression)
        {
            var disposition = GetMoveInputDisposition(
                whenExpression.Else,
                inputName,
                functions);
            foreach (var arm in whenExpression.Arms)
            {
                disposition = CombineAlternativeMoveInputDispositions(
                    disposition,
                    GetMoveInputDisposition(arm.Body, inputName, functions));
            }

            return disposition;
        }

        return MoveInputDisposition.Retained;
    }

    private MoveInputDisposition GetMoveInputDisposition(
        BlockBody body,
        string inputName,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var disposition = MoveInputDisposition.Retained;
        foreach (var statement in body.Statements)
        {
            var expression = statement switch
            {
                BindingStatement binding => binding.Value,
                ReturnStatement { Value: { } value } => value,
                ExpressionStatement expressionStatement => expressionStatement.Expression,
                GuardLoopControlStatement guard => guard.Condition,
                _ => null
            };
            if (expression is null)
            {
                continue;
            }

            var statementDisposition = GetMoveInputDisposition(
                expression,
                inputName,
                functions,
                isResult: false);
            if (statementDisposition != MoveInputDisposition.Retained)
            {
                disposition = statementDisposition;
                break;
            }
        }

        if (disposition != MoveInputDisposition.Retained || body.Value is null)
        {
            return disposition;
        }

        return GetMoveInputDisposition(body.Value, inputName, functions, isResult: true);
    }

    private MoveInputDisposition CombineAlternativeMoveInputDispositions(
        MoveInputDisposition left,
        MoveInputDisposition right)
    {
        return left == right ? left : MoveInputDisposition.Mixed;
    }

    private void ValidateOwnedParameterConsumptionExpression(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        if (expression is CallExpression call && IsOwnedParameterCall(call, functions))
        {
            return;
        }

        if (expression is FlowExpression flow)
        {
            if (ContainsOwnedParameterCall(flow.Source, functions)
                || flow.Targets.Any(target => target.Arguments.Any(argument => ContainsOwnedParameterCall(argument, functions))))
            {
                throw Error(
                    expression.Line,
                    expression.Column,
                    "owned container parameter calls must be direct calls or direct value-flow from a named owner");
            }

            if (flow.Targets.Any(target => IsOwnedParameterFlowTarget(target, functions)))
            {
                return;
            }
        }

        if (ContainsOwnedParameterCall(expression, functions))
        {
            throw Error(
                expression.Line,
                expression.Column,
                "owned container parameter calls must be direct calls or direct value-flow from a named owner");
        }
    }

    private bool ContainsOwnedParameterCall(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        return expression switch
        {
            CallExpression call => IsOwnedParameterCall(call, functions)
                || call.Arguments.Any(argument => ContainsOwnedParameterCall(argument, functions)),
            FlowExpression flow => ContainsOwnedParameterCall(flow.Source, functions)
                || flow.Targets.Any(target =>
                    IsOwnedParameterFlowTarget(target, functions)
                    || target.Arguments.Any(argument => ContainsOwnedParameterCall(argument, functions))),
            StringExpression text => text.Segments
                .OfType<InterpolationSegment>()
                .Any(segment => ContainsOwnedParameterCall(segment.Expression, functions)),
            AddExpression add => ContainsOwnedParameterCall(add.Left, functions)
                || ContainsOwnedParameterCall(add.Right, functions),
            SubtractExpression subtract => ContainsOwnedParameterCall(subtract.Left, functions)
                || ContainsOwnedParameterCall(subtract.Right, functions),
            MultiplyExpression multiply => ContainsOwnedParameterCall(multiply.Left, functions)
                || ContainsOwnedParameterCall(multiply.Right, functions),
            DivideExpression divide => ContainsOwnedParameterCall(divide.Left, functions)
                || ContainsOwnedParameterCall(divide.Right, functions),
            ModuloExpression modulo => ContainsOwnedParameterCall(modulo.Left, functions)
                || ContainsOwnedParameterCall(modulo.Right, functions),
            NegateExpression negate => ContainsOwnedParameterCall(negate.Value, functions),
            CompareExpression compare => ContainsOwnedParameterCall(compare.Left, functions)
                || ContainsOwnedParameterCall(compare.Right, functions),
            AndExpression logicalAnd => ContainsOwnedParameterCall(logicalAnd.Left, functions)
                || ContainsOwnedParameterCall(logicalAnd.Right, functions),
            OrExpression logicalOr => ContainsOwnedParameterCall(logicalOr.Left, functions)
                || ContainsOwnedParameterCall(logicalOr.Right, functions),
            NotExpression logicalNot => ContainsOwnedParameterCall(logicalNot.Value, functions),
            TryExpression attempt => ContainsOwnedParameterCall(attempt.Value, functions),
            RangeExpression range => ContainsOwnedParameterCall(range.Start, functions)
                || ContainsOwnedParameterCall(range.End, functions),
            FoldExpression fold => ContainsOwnedParameterCall(fold.Source, functions)
                || ContainsOwnedParameterCall(fold.Initial, functions)
                || ContainsOwnedParameterCall(fold.Body, functions),
            IfExpression conditional => ContainsOwnedParameterCall(conditional.Condition, functions)
                || ContainsOwnedParameterCall(conditional.Then, functions)
                || (conditional.Else is not null && ContainsOwnedParameterCall(conditional.Else, functions)),
            WhenExpression whenExpression => (whenExpression.Subject is not null && ContainsOwnedParameterCall(whenExpression.Subject, functions))
                || whenExpression.Arms.Any(arm => ContainsOwnedParameterCall(arm.Condition, functions) || ContainsOwnedParameterCall(arm.Body, functions))
                || ContainsOwnedParameterCall(whenExpression.Else, functions),
            ArrayLiteralExpression array => array.Elements.Any(element => ContainsOwnedParameterCall(element, functions)),
            ArrayRepeatExpression repeat => ContainsOwnedParameterCall(repeat.Value, functions),
            DictionaryLiteralExpression dictionary => dictionary.Entries.Any(entry =>
                ContainsOwnedParameterCall(entry.Key, functions)
                || ContainsOwnedParameterCall(entry.Value, functions)),
            IndexExpression index => ContainsOwnedParameterCall(index.Source, functions)
                || ContainsOwnedParameterCall(index.Index, functions),
            SubjectCompareExpression compare => ContainsOwnedParameterCall(compare.Right, functions),
            SubjectRangeExpression range => ContainsOwnedParameterCall(range.Start, functions)
                || ContainsOwnedParameterCall(range.End, functions),
            _ => false
        };
    }

    private bool ContainsOwnedParameterCall(
        BlockBody body,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        return body.Statements.Any(statement => statement switch
            {
                BindingStatement binding => ContainsOwnedParameterCall(binding.Value, functions),
                ReturnStatement { Value: { } value } => ContainsOwnedParameterCall(value, functions),
                IndexAssignmentStatement assignment => ContainsOwnedParameterCall(assignment.Value, functions)
                    || ContainsOwnedParameterCall(assignment.Index, functions),
                ExpressionStatement expression => ContainsOwnedParameterCall(expression.Expression, functions),
                GuardLoopControlStatement guard => ContainsOwnedParameterCall(guard.Condition, functions),
                BlockFunctionCallStatement blockFunctionCall => ContainsOwnedParameterCall(blockFunctionCall.Source, functions)
                    || blockFunctionCall.Body.Any(nested => nested is ExpressionStatement expression
                        && ContainsOwnedParameterCall(expression.Expression, functions)),
                _ => false
            })
            || (body.Value is not null && ContainsOwnedParameterCall(body.Value, functions));
    }

    private bool IsOwnedParameterCall(
        CallExpression call,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        return TryGetFunction(call.Path, functions, out var function)
            && FunctionMovesOwnedHeapInput(function);
    }

    private bool IsOwnedParameterFlowTarget(
        FlowTarget target,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        return TryGetFunction(target.Path, functions, out var function)
            && FunctionMovesOwnedHeapInput(function);
    }

    private bool FunctionMovesOwnedHeapInput(BoundFunction function)
    {
        return function.InputOwnership == BoundFunctionInputOwnership.Move
            && function.InputType is not null;
    }

    private bool FunctionMutablyBorrowsInput(BoundFunction function)
    {
        return function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow
            && function.InputType is { } inputType
            && (inputType is BoundType.DynamicIntArray or BoundType.IntDictionary or BoundType.Arena
                || _types.IsDynamicArray(inputType)
                || _types.IsDictionary(inputType)
                || _types.IsStruct(inputType));
    }

    private bool FunctionReadonlyBorrowsHeapInput(BoundFunction function, BoundType actualType)
    {
        return (function.InputType == BoundType.IntDictionaryView
                && actualType == BoundType.IntDictionary)
            || (function.InputType == actualType && _types.IsDictionary(actualType))
            || (function.InputType == actualType && _types.IsDynamicArray(actualType))
            || (function.InputType == BoundType.IntSlice
                && actualType == BoundType.DynamicIntArray);
    }

    private bool TryGetFunction(
        IReadOnlyList<string> path,
        IReadOnlyDictionary<string, BoundFunction> functions,
        out BoundFunction function)
    {
        return TryGetFunction(string.Join('.', path), functions, out function);
    }

    private bool TryGetFunction(
        string path,
        IReadOnlyDictionary<string, BoundFunction> functions,
        out BoundFunction function)
    {
        if (functions.TryGetValue(path, out function!))
        {
            return true;
        }

        return !path.Contains('.', StringComparison.Ordinal)
            && _currentModuleName.Length > 0
            && functions.TryGetValue(_currentModuleName + "." + path, out function!);
    }

    private void EnsureFunctionVisible(BoundFunction function, int line, int column)
    {
        if (function.IsStandardLibrary
            || function.IsLocal
            || function.IsPublic
            || function.ModuleName == _currentModuleName)
        {
            return;
        }

        throw Error(
            line,
            column,
            $"function '{function.Name}' is internal to module '{function.ModuleName}'");
    }

    private void EnsureTypeVisible(TypeId type, int line, int column)
    {
        string? name = null;
        string? moduleName = null;
        var isPublic = true;
        if (_types.IsStruct(type))
        {
            var definition = _types.GetStruct(type);
            if (definition.DeclaringTypeName is not null
                && !definition.IsPublic
                && (_currentTypeScopeName is null
                    || !(_currentTypeScopeName == definition.DeclaringTypeName
                        || _currentTypeScopeName.StartsWith(definition.DeclaringTypeName + ".", StringComparison.Ordinal))))
            {
                throw Error(line, column,
                    $"nested type '{definition.Name}' is private to struct '{definition.DeclaringTypeName}'");
            }
            name = definition.Name;
            moduleName = definition.ModuleName;
            isPublic = definition.IsPublic;
        }
        else if (_types.IsEnum(type))
        {
            var definition = _types.GetEnum(type);
            name = definition.Name;
            moduleName = definition.ModuleName;
            isPublic = definition.IsPublic;
        }
        else if (_types.IsBox(type))
        {
            EnsureTypeVisible(_types.GetBox(type).ElementType, line, column);
            return;
        }

        if (name is null || isPublic || moduleName == _currentModuleName)
        {
            return;
        }

        throw Error(line, column, $"type '{name}' is internal to module '{moduleName}'");
    }

    private string? ResolveFunctionTypeScope(string functionName)
    {
        return _types.Structs
            .Where(type => functionName.StartsWith(type.Name + ".", StringComparison.Ordinal))
            .OrderByDescending(type => type.Name.Length)
            .Select(type => type.Name)
            .FirstOrDefault();
    }

    private void EnsureTraitVisible(BoundTraitDefinition trait, int line, int column)
    {
        if (trait.IsPublic || trait.ModuleName == _currentModuleName)
        {
            return;
        }

        throw Error(line, column, $"trait '{trait.Name}' is internal to module '{trait.ModuleName}'");
    }

    private bool IsPlainStringLiteral(Expression expression)
    {
        return expression is StringExpression str
            && str.Segments.All(static segment => segment is TextSegment);
    }

    private SmallLangException Error(int line, int column, string message)
    {
        return new SmallLangException($"semantic error at {line}:{column}: {message}");
    }

    private sealed record FlowResult(BoundType Type, FlowEffect Effect);

    private abstract record FlowEffect
    {
        public static FlowEffect None { get; } = new NoFlowEffect();
    }

    private sealed record NoFlowEffect : FlowEffect;

    private sealed record FlowBindingEffect(string Name, BoundType Type) : FlowEffect;

    private enum MoveInputDisposition
    {
        Retained,
        Transferred,
        Mixed
    }
}
