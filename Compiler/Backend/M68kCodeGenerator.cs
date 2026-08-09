/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly CompilationModule _module;
	private readonly M68kCompilationRequest _request;
	private readonly M68kAssembler _assembler = new();
	private readonly Dictionary<CilTypeIdentity, CilTypeLayout> _usedTypeLayouts = new();
	private readonly Dictionary<string, (CilType Type, CilTypeLayout Layout)>
		_constructedTypeDescriptors = new(StringComparer.Ordinal);
	private readonly Dictionary<CilFieldIdentity, CilField> _staticFields = new();
	private readonly Dictionary<CilUserStringIdentity, string> _stringLiterals = new();
	private readonly Dictionary<CilUserStringIdentity, string> _cStringLiterals = new();
	private bool _usesDynamicStrings;
	private bool _usesRuntimeEmptyString;
	private CilType? _runtimeEmptyCharArrayElementType;
	private readonly Dictionary<string, CilType> _arrayTypes = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CilRuntimeTypeTarget> _arrayElementRuntimeTypes =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, CilType> _boxedTypes = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CilTypeLayout> _boxedStructLayouts = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CilType> _delegateTypes = new(StringComparer.Ordinal);
	private readonly Dictionary<CilTypeIdentity, CilInterfaceDefinition> _usedInterfaces = new();
	private readonly Dictionary<string, GeneratedPlatformBase> _usedPlatformBases = new(StringComparer.Ordinal);
	private readonly M68kMemoryManagement _memoryManagement;
	private int _uniqueLabel;
	private int _currentStackDepth = 0;
	private bool _usesExceptionRuntime;
	private bool _usesArithmeticExceptionFault;
	private bool _hasExceptionFrames;
	private ImmutableArray<CilStackValueKind> _currentStackTypes = ImmutableArray<CilStackValueKind>.Empty;
	private ImmutableArray<CilStackValueKind> _nextStackTypes = ImmutableArray<CilStackValueKind>.Empty;
	private FrameLayout? _currentFrameLayout;
	private GeneratedPlatformBase? _loadedPlatformBase;
	private readonly Dictionary<CilMethodIdentity, string?>
		_platformBaseMethodEntries = new();
	private readonly ManagedPoolRuntimeModule? _managedPoolRuntime;
	private readonly IReadOnlyList<ManagedLifecycleModule> _managedLifecycles;
	private readonly Dictionary<CilMethodIdentity, M68kMethodAllocationStatistics>
		_allocationStatistics = new();
	private readonly Dictionary<CilMethodIdentity, M68kTerminalDeadStoreStatistics>
		_terminalDeadStoreStatistics = new();
	private readonly List<M68kLoopLayout> _loopLayouts = new();
	private readonly HashSet<CilMethodIdentity> _rootOnlyMethods = new();
	private readonly HashSet<CilMethodIdentity> _terminatingEntryMethods = new();
	private readonly HashSet<CilMethodIdentity> _callerBorrowedByrefMethods = new();
	private readonly HashSet<CilFieldIdentity> _escapedStaticFields = new();
	private readonly Dictionary<CilMethodIdentity, CilMethod> _typeInitializers = new();
	private readonly Dictionary<CilMethodIdentity, CilMethod> _foldedMethodAliases = new();

	private enum InlineCandidateKind
	{
		SimpleWrapperConstructor,
		IdentityReturn,
		MaskedAddReturn
	}

	private readonly record struct InlineCandidate(
		InlineCandidateKind Kind,
		int InlineBytes,
		int CallBytes,
		int InlineCycles,
		int CallCycles)
	{
		public int SavedBytes => CallBytes - InlineBytes;

		public int SavedCycles => CallCycles - InlineCycles;
	}

	private readonly record struct InternalArgumentLocation(
		int Index,
		int SlotLongs,
		M68kRegister? Register,
		M68kRegister? LowRegister,
		int StackOffset,
		bool IsGcReference)
	{
		public bool IsStack => Register is null;
	}

	private sealed record InternalCallAbi(
		ImmutableArray<InternalArgumentLocation> Arguments,
		int StackBytes,
		int? ReturnBufferStackOffset = null)
	{
		public bool IsRegisterOnly => StackBytes == 0;

		public IReadOnlyList<M68kRegister>? RegisterOnlyLocations =>
			IsRegisterOnly
				? Arguments.Select(static argument => argument.Register!.Value).ToArray()
				: null;
	}

	private bool UsesBuiltInManagedPool =>
		_memoryManagement == M68kMemoryManagement.ManagedPoolMarkSweepGc;

	private bool UsesAmigaManagedPoolArena =>
		UsesBuiltInManagedPool &&
		_request.Imports.ContainsKey(M68kRuntimeImports.AmigaManagedPoolArena);

	private bool UsesManagedExceptionRuntime =>
		_usesExceptionRuntime;

	private bool UseClr => _request.ClrPolicy switch
	{
		M68kClrPolicy.Always => true,
		_ => _request.Cpu != M68kCpuTarget.M68000
	};

	public M68kCodeGenerator(
		CompilationModule module,
		M68kCompilationRequest request,
		ManagedPoolRuntimeModule? managedPoolRuntime = null,
		IReadOnlyList<ManagedLifecycleModule>? managedLifecycles = null)
	{
		_module = module;
		_request = request;
		_memoryManagement = M68kCompiler.GetEffectiveMemoryManagement(request);
		_managedPoolRuntime = managedPoolRuntime;
		_managedLifecycles = managedLifecycles ?? Array.Empty<ManagedLifecycleModule>();
	}

	public GeneratedProgram Generate(CilMethod entry)
	{
		if (_managedPoolRuntime is not null)
		{
			foreach (var field in _managedPoolRuntime.Fields)
			{
				_staticFields.TryAdd(field.Identity, field);
			}
		}
		ValidateMethodSignature(entry, isEntry: true, isExport: false);
		var exports = _module.GetExports();
		var methods = PruneAlwaysInlinedMethods(DiscoverReachableMethods(entry, exports), entry, exports);
		PlanIdenticalMethodFolds(methods, entry, exports);
		var exportedMethods = exports
			.Select(static export => export.Method.Identity)
			.ToHashSet();
		foreach (var method in methods)
		{
			if (method.Identity != entry.Identity &&
				!exportedMethods.Contains(method.Identity))
			{
				_callerBorrowedByrefMethods.Add(method.Identity);
			}
		}
		PreRegisterPlatformBases(methods);
		EnsureManagedPoolExecBase();
		var initializesPlatformBase = _usedPlatformBases.Values.Any(RequiresPlatformBaseInitialization);
		var usesManagedRuntime = M68kCompiler.IsManagedRuntime(_request);
		var usesManagedLifecycle = _managedLifecycles.Count != 0;
		var usesAmigaStartupArguments = HasAmigaStartupArguments(entry);
		_usesExceptionRuntime = _request.ExceptionMode == M68kExceptionMode.Full &&
			methods.Any(MethodMayRaiseException);
		_hasExceptionFrames = _usesExceptionRuntime &&
			methods.Any(static method => method.ExceptionRegions.Count != 0);
		var entryHasOtherInvocation =
			exports.Any(export => export.Method.Identity == entry.Identity) ||
			methods.Any(method =>
				method.Identity != entry.Identity &&
				method.Instructions.Any(instruction =>
					IsCallInstruction(instruction) &&
					_module.ResolveMethodToken(
						(int)instruction.Operand!,
						method,
						instruction.Offset).Definition?.Identity ==
							entry.Identity));
		var hasTerminatingTargetLifetime =
			_request.OutputFormat == M68kOutputFormat.Hunk ||
			(_request.OutputFormat == M68kOutputFormat.Assembly &&
			 _request.RuntimeProfile == M68kRuntimeProfile.Application);
		if (!entryHasOtherInvocation &&
			hasTerminatingTargetLifetime &&
			(!usesManagedRuntime || _managedPoolRuntime is not null))
		{
			_terminatingEntryMethods.Add(entry.Identity);
		}
		foreach (var method in methods)
		{
			foreach (var instruction in method.Instructions.Where(static instruction =>
				instruction.OpCode == OpCodes.Ldsflda))
			{
				_escapedStaticFields.Add(_module.ResolveFieldToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset).Identity);
			}
		}
		var entryLabel = MethodLabel(entry);
		if (initializesPlatformBase ||
			usesManagedRuntime ||
			usesManagedLifecycle ||
			usesAmigaStartupArguments ||
			_hasExceptionFrames)
		{
			entryLabel = EmitEntryAdapter(
				entry,
				usesManagedRuntime,
				usesAmigaStartupArguments,
				_hasExceptionFrames);
			if (!entryHasOtherInvocation &&
				!usesManagedRuntime &&
				!usesManagedLifecycle)
			{
				// This adapter only initializes process/exception state before entering
				// the sole managed root. The Amiga process boundary has no caller-owned
				// callee-saved registers, so keep the entry allocation root-only even
				// when the adapter calls it to establish an exception return boundary.
				_rootOnlyMethods.Add(entry.Identity);
			}
		}
		else if (!entryHasOtherInvocation)
		{
			_rootOnlyMethods.Add(entry.Identity);
		}
		AnalyzePlatformBaseMethodEntries(methods, entry, exports);
		foreach (var method in methods)
		{
			if (_foldedMethodAliases.ContainsKey(method.Identity))
			{
				continue;
			}
			CompileMethod(method);
		}
		foreach (var export in exports)
		{
			EmitExportAdapter(export, initializesPlatformBase);
		}
		EmitTypeInitializationFailureThunks();
		EmitBoxedInterfaceThunks();
		if (_usesExceptionRuntime &&
			!M68kCompiler.IsManagedRuntime(_request) &&
			!_assembler.ReferencesTargetPrefix("__c68k_exception_"))
		{
			// Method-level analysis is conservative. If final reachable code emitted
			// no exception-runtime entry reference, the runtime and metadata are dead.
			_usesExceptionRuntime = false;
		}
		EmitExceptionRuntime();
		if (!_usesExceptionRuntime &&
			_assembler.ReferencesTarget(RuntimeInitialStackLabel))
		{
			// The entry adapter may have conservatively published A7 before final
			// reachability proved the exception runtime dead. Retain only its private
			// storage target; no runtime code or exception metadata is emitted.
			_assembler.Mark(RuntimeInitialStackLabel);
			_assembler.EmitLong(0);
		}
		EmitManagedPoolRuntime();
		VerifyAllocatedPrePeepholeOutput();
		_assembler.ApplyRequestedAlignments();
		var sizeFirstLoops = _request.Cpu == M68kCpuTarget.M68020
			? M68kLoopFootprintAnalysis.SelectSizeFirstLayouts(
				_loopLayouts,
				_assembler.Labels,
				_assembler.AnalysisAnchors)
			: Array.Empty<M68kLoopLayout>();
		_assembler.OptimizeForCpu(
			_request.Cpu,
			_request.ClrPolicy,
			sizeFirstLoops);
		_assembler.ApplyRequestedAlignments();
		_assembler.MarkDataStart();
		EmitData(methods);
		EmitExceptionMetadata(methods);

		return new GeneratedProgram(
			_assembler,
			methods,
			exports,
			_usedPlatformBases.Values.OrderBy(item => item.Binding.Identity, StringComparer.Ordinal).ToArray(),
			entryLabel,
			methods.ToDictionary(method => method.Identity, MethodLabel),
			_allocationStatistics,
			_terminalDeadStoreStatistics,
			_loopLayouts);
	}

	private void VerifyAllocatedPrePeepholeOutput()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			var opcode = instruction.Opcode;
			var sizeFamily = opcode & 0xF000;
			var selfDataMove =
				sizeFamily is 0x1000 or 0x2000 or 0x3000 &&
				((opcode >> 3) & 7) == 0 &&
				((opcode >> 6) & 7) == 0 &&
				(opcode & 7) == ((opcode >> 9) & 7);
			var selfAddressMove =
				(opcode & 0xF1F8) == 0x2048 &&
				(opcode & 7) == ((opcode >> 9) & 7);
			if (selfDataMove || selfAddressMove)
			{
				throw new InvalidOperationException(
					$"Allocated emission produced a self-move at byte offset " +
					$"{instruction.Offset} before peephole optimization.");
			}
			if (index + 1 >= instructions.Count ||
				!IsPrePeepholeLongStackPush(opcode))
			{
				continue;
			}
			var next = instructions[index + 1].Opcode;
			if (IsPrePeepholeLongStackPop(next) || next == 0x588F)
			{
				throw new InvalidOperationException(
					$"Allocated emission produced an immediate stack round trip " +
					$"at byte offset {instruction.Offset} before peephole optimization " +
					$"(opcodes ${opcode:X4}, ${next:X4}).");
			}
		}
	}

	private static bool IsPrePeepholeLongStackPush(ushort opcode) =>
		(opcode & 0xF000) == 0x2000 &&
		((opcode >> 6) & 7) == 4 &&
		((opcode >> 9) & 7) == 7;

	private static bool IsPrePeepholeLongStackPop(ushort opcode) =>
		(opcode & 0xF000) == 0x2000 &&
		((opcode >> 3) & 7) == 3 &&
		(opcode & 7) == 7;

	private IReadOnlyList<CilMethod> DiscoverReachableMethods(
		CilMethod entry,
		IReadOnlyList<CilExport> exports)
	{
		var result = new List<CilMethod>();
		var visited = new HashSet<CilMethodIdentity>();
		var exportedMethods = exports
			.Select(static export => export.Method.Identity)
			.ToHashSet();
		var queue = new Queue<CilMethod>();
		var reachableDispatchLayouts =
			new Dictionary<CilTypeIdentity, CilTypeLayout>();
		var usedVirtualDeclarations =
			new Dictionary<CilMethodIdentity, CilMethod>();
		queue.Enqueue(entry);
		foreach (var export in exports)
		{
			queue.Enqueue(export.Method);
		}
		if (_managedPoolRuntime is not null)
		{
			foreach (var method in _managedPoolRuntime.CoreMethods)
			{
				queue.Enqueue(method);
			}
		}
		foreach (var lifecycle in _managedLifecycles)
		{
			foreach (var method in lifecycle.Methods)
			{
				queue.Enqueue(method);
			}
		}

		var requiresExtendedRootWalk = false;
		var extendedRootWalkQueued = false;
	ProcessQueue:
		while (queue.Count != 0)
		{
			var method = queue.Dequeue();
			if (!visited.Add(method.Identity))
			{
				continue;
			}

			ValidateMethodSignature(
				method,
				isEntry: method == entry,
				isExport: exportedMethods.Contains(method.Identity));
			if (method.IsImport)
			{
				continue;
			}

			result.Add(method);
			if (ContainsDynamicLocalloc(method))
			{
				requiresExtendedRootWalk = true;
			}
			foreach (var instruction in method.Instructions)
			{
				if (_module.GetTriggeredTypeInitializer(method, instruction) is { } initializer)
				{
					queue.Enqueue(initializer);
					if (TypeInitializerRequiresExceptionCleanup(initializer))
					{
						requiresExtendedRootWalk = true;
					}
				}
				if (instruction.OpCode == OpCodes.Ldftn ||
					instruction.OpCode == OpCodes.Ldvirtftn)
				{
					var delegateTarget = _module.ResolveMethodToken(
						(int)instruction.Operand!,
						method,
						instruction.Offset);
					if (instruction.OpCode == OpCodes.Ldvirtftn &&
						delegateTarget.Definition is
							{ IsImport: false, DeclaringTypeIsInterface: true } interfaceTarget)
					{
						var interfaceDefinition = _module.GetInterfaceDefinition(interfaceTarget);
						_usedInterfaces.TryAdd(interfaceDefinition.Identity, interfaceDefinition);
						foreach (var implementation in _module.GetInterfaceTableImplementations(interfaceTarget))
						{
							queue.Enqueue(implementation);
						}
					}
					else if (instruction.OpCode == OpCodes.Ldvirtftn &&
						delegateTarget.Definition is { IsImport: false } virtualTarget &&
						virtualTarget.IsVirtual &&
						!virtualTarget.IsFinal &&
						!virtualTarget.DeclaringTypeIsSealed)
					{
						usedVirtualDeclarations.TryAdd(
							virtualTarget.Identity,
							virtualTarget);
						foreach (var implementation in _module.GetVirtualImplementations(virtualTarget))
						{
							queue.Enqueue(implementation);
						}
					}
					else if (delegateTarget.Definition is { IsImport: false } targetDefinition)
					{
						queue.Enqueue(targetDefinition);
					}
					continue;
				}
				if (instruction.OpCode != OpCodes.Call &&
					instruction.OpCode != OpCodes.Callvirt &&
					instruction.OpCode != OpCodes.Newobj)
				{
					continue;
				}

				var target = _module.ResolveMethodToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset);
				if (instruction.OpCode == OpCodes.Newobj &&
					target.Definition is { IsImport: false } constructor)
				{
					var layout = _module.GetTypeLayout(constructor);
					reachableDispatchLayouts.TryAdd(layout.Identity, layout);
				}
				if (target.Definition is { IsImport: false, DeclaringTypeIsInterface: true } interfaceMethod)
				{
					var interfaceDefinition = _module.GetInterfaceDefinition(interfaceMethod);
					_usedInterfaces.TryAdd(interfaceDefinition.Identity, interfaceDefinition);
					foreach (var implementation in _module.GetInterfaceTableImplementations(interfaceMethod))
					{
						queue.Enqueue(implementation);
					}
				}
				else if (target.Definition is { IsImport: false } definition &&
					RequiresVirtualDispatch(instruction, definition))
				{
					usedVirtualDeclarations.TryAdd(definition.Identity, definition);
					foreach (var implementation in _module.GetVirtualImplementations(definition))
					{
						queue.Enqueue(implementation);
					}
				}
				else if (target.Definition is { IsImport: false } directDefinition)
				{
					if (!IsNativeShadowMathLeaf(directDefinition))
					{
						queue.Enqueue(directDefinition);
					}
				}
			}
		}
		var queuedClosedDispatchMethod = false;
		foreach (var layout in reachableDispatchLayouts.Values)
		{
			foreach (var interfaceDefinition in _usedInterfaces.Values)
			{
				var implementation = _module.TryGetInterfaceImplementation(
					layout,
					interfaceDefinition);
				if (implementation is null)
				{
					continue;
				}
				foreach (var implementationMethod in implementation.Methods)
				{
					if (visited.Contains(implementationMethod.Identity))
					{
						continue;
					}
					queue.Enqueue(implementationMethod);
					queuedClosedDispatchMethod = true;
				}
			}
			foreach (var declaration in usedVirtualDeclarations.Values)
			{
				var implementation = _module.TryGetVirtualImplementation(
					layout,
					declaration);
				if (implementation is null ||
					visited.Contains(implementation.Identity))
				{
					continue;
				}
				queue.Enqueue(implementation);
				queuedClosedDispatchMethod = true;
			}
		}
		if (queuedClosedDispatchMethod)
		{
			goto ProcessQueue;
		}
		if (requiresExtendedRootWalk &&
			!extendedRootWalkQueued &&
			_managedPoolRuntime is not null)
		{
			extendedRootWalkQueued = true;
			foreach (var method in _managedPoolRuntime.ExtendedRootWalkMethods)
			{
				queue.Enqueue(method);
			}
			goto ProcessQueue;
		}

		return result;

		bool ContainsDynamicLocalloc(CilMethod method)
		{
			if (!method.Instructions.Any(static instruction =>
				instruction.OpCode == OpCodes.Localloc))
			{
				return false;
			}
			return CilMachineIrBuilder.Build(
				method,
				_module,
				_request.Cpu).HasDynamicStackAllocation;
		}
	}

	private void PreRegisterPlatformBases(IEnumerable<CilMethod> methods)
	{
		foreach (var method in methods)
		{
			foreach (var instruction in method.Instructions)
			{
				if (!IsCallInstruction(instruction))
				{
					continue;
				}

				var target = _module.ResolveMethodToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset);
				if (target.Definition?.ExternalCall is { } externalCall)
				{
					GetOrAddPlatformBase(externalCall.Convention, method);
				}
			}
		}
	}

	private IReadOnlyList<CilMethod> PruneAlwaysInlinedMethods(
		IReadOnlyList<CilMethod> methods,
		CilMethod entry,
		IReadOnlyList<CilExport> exports)
	{
		var roots = new HashSet<CilMethodIdentity>(exports.Select(export => export.Method.Identity))
		{
			entry.Identity
		};
		if (_managedPoolRuntime is not null)
		{
			roots.UnionWith(_managedPoolRuntime.Methods.Select(method => method.Identity));
		}
		var referencedByNonInlinedCall = new HashSet<CilMethodIdentity>();
		foreach (var method in methods)
		{
			foreach (var instruction in method.Instructions)
			{
				if (!IsCallInstruction(instruction))
				{
					continue;
				}

				var target = _module.ResolveMethodToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset);
				if (target.Definition is { IsImport: false, DeclaringTypeIsInterface: true } interfaceMethod)
				{
					referencedByNonInlinedCall.UnionWith(
						_module.GetInterfaceTableImplementations(interfaceMethod)
							.Select(implementation => implementation.Identity));
					continue;
				}
				if (target.Definition is { IsImport: false } virtualDefinition &&
					RequiresVirtualDispatch(instruction, virtualDefinition))
				{
					referencedByNonInlinedCall.UnionWith(
						_module.GetVirtualImplementations(virtualDefinition)
							.Select(implementation => implementation.Identity));
					continue;
				}
				if (target.Definition is not { IsImport: false } definition ||
					IsAlwaysInlinedMethod(definition))
				{
					continue;
				}

				referencedByNonInlinedCall.Add(definition.Identity);
			}
		}

		return methods
			.Where(method =>
				roots.Contains(method.Identity) ||
				!IsAlwaysInlinedMethod(method) ||
				referencedByNonInlinedCall.Contains(method.Identity))
			.ToArray();
	}

	private static bool IsCallInstruction(CilInstruction instruction) =>
		instruction.OpCode == OpCodes.Call ||
		instruction.OpCode == OpCodes.Callvirt ||
		instruction.OpCode == OpCodes.Newobj;

	private void PlanIdenticalMethodFolds(
		IReadOnlyList<CilMethod> methods,
		CilMethod entry,
		IReadOnlyList<CilExport> exports)
	{
		var protectedMethods = exports
			.Select(static export => export.Method.Identity)
			.ToHashSet();
		protectedMethods.Add(entry.Identity);
		if (_managedPoolRuntime is not null)
		{
			protectedMethods.UnionWith(
				_managedPoolRuntime.Methods.Select(static method => method.Identity));
		}
		foreach (var lifecycle in _managedLifecycles)
		{
			protectedMethods.UnionWith(
				lifecycle.Methods.Select(static method => method.Identity));
		}

		var addressTaken = new HashSet<CilMethodIdentity>();
		foreach (var caller in methods)
		{
			foreach (var instruction in caller.Instructions)
			{
				if (instruction.OpCode != OpCodes.Ldftn &&
					instruction.OpCode != OpCodes.Ldvirtftn)
				{
					continue;
				}

				var target = _module.ResolveMethodToken(
					(int)instruction.Operand!,
					caller,
					instruction.Offset);
				if (target.Definition is { } definition)
				{
					addressTaken.Add(definition.Identity);
				}
			}
		}

		var canonicalMethods = new List<CilMethod>();
		foreach (var method in methods)
		{
			if (!CanFoldIdenticalMethod(method, protectedMethods, addressTaken))
			{
				continue;
			}

			var canonical = canonicalMethods.FirstOrDefault(candidate =>
				HaveIdenticalFoldableBodies(candidate, method));
			if (canonical is null)
			{
				canonicalMethods.Add(method);
				continue;
			}

			_foldedMethodAliases.Add(method.Identity, canonical);
		}
	}

	private static bool CanFoldIdenticalMethod(
		CilMethod method,
		IReadOnlySet<CilMethodIdentity> protectedMethods,
		IReadOnlySet<CilMethodIdentity> addressTaken) =>
		!method.IsImport &&
		!method.IsAbstract &&
		!method.IsVirtual &&
		!method.IsTypeInitializer &&
		!method.DeclaringTypeIsInterface &&
		method.Construction.Length == 0 &&
		method.Instructions.Count != 0 &&
		method.ExceptionRegions.Count == 0 &&
		!protectedMethods.Contains(method.Identity) &&
		!addressTaken.Contains(method.Identity);

	private bool HaveIdenticalFoldableBodies(CilMethod left, CilMethod right)
	{
		if (left.ModuleName != right.ModuleName ||
			left.Attributes != right.Attributes ||
			left.DeclaringTypeAttributes != right.DeclaringTypeAttributes ||
			left.InitializeLocals != right.InitializeLocals ||
			(left.Name == ".ctor") != (right.Name == ".ctor") ||
			IsTransparentScalarDeclaringType(left) !=
				IsTransparentScalarDeclaringType(right) ||
			!HaveSameSignature(left, right) ||
			!HaveSameTypes(left.Locals, right.Locals) ||
			!HaveSameParameterFlags(left.ParameterFlags, right.ParameterFlags) ||
			!HaveSameInternalCallAbi(
				GetInternalCallAbi(left),
				GetInternalCallAbi(right)) ||
			left.Instructions.Count != right.Instructions.Count)
		{
			return false;
		}

		for (var index = 0; index < left.Instructions.Count; index++)
		{
			var leftInstruction = left.Instructions[index];
			var rightInstruction = right.Instructions[index];
			if (leftInstruction.Offset != rightInstruction.Offset ||
				leftInstruction.OpCode != rightInstruction.OpCode ||
				leftInstruction.NextOffset != rightInstruction.NextOffset ||
				leftInstruction.ConstrainedTypeToken !=
					rightInstruction.ConstrainedTypeToken ||
				!HaveSameInstructionOperand(
					leftInstruction.Operand,
					rightInstruction.Operand))
			{
				return false;
			}
		}

		return true;
	}

	private static bool HaveSameSignature(CilMethod left, CilMethod right) =>
		left.Signature.Header.Equals(right.Signature.Header) &&
		left.Signature.GenericParameterCount == right.Signature.GenericParameterCount &&
		left.Signature.RequiredParameterCount == right.Signature.RequiredParameterCount &&
		HaveSameType(left.Signature.ReturnType, right.Signature.ReturnType) &&
		HaveSameTypes(left.Signature.ParameterTypes, right.Signature.ParameterTypes);

	private static bool HaveSameTypes(
		IReadOnlyList<CilType> left,
		IReadOnlyList<CilType> right)
	{
		if (left.Count != right.Count)
		{
			return false;
		}

		for (var index = 0; index < left.Count; index++)
		{
			if (!HaveSameType(left[index], right[index]))
			{
				return false;
			}
		}

		return true;
	}

	private static bool HaveSameType(CilType left, CilType right) =>
		left.Kind == right.Kind &&
		left.Size == right.Size &&
		left.DisplayName == right.DisplayName &&
		left.IsReadOnly == right.IsReadOnly &&
		left.IsEnum == right.IsEnum &&
		(left.ElementType is null
			? right.ElementType is null
			: right.ElementType is not null &&
				HaveSameType(left.ElementType, right.ElementType)) &&
		HaveSameGenericArguments(left.GenericArguments, right.GenericArguments);

	private static bool HaveSameGenericArguments(
		ImmutableArray<CilType> left,
		ImmutableArray<CilType> right) =>
		left.IsDefaultOrEmpty && right.IsDefaultOrEmpty ||
		!left.IsDefault && !right.IsDefault && HaveSameTypes(left, right);

	private static bool HaveSameParameterFlags(
		ImmutableArray<ParameterAttributes> left,
		ImmutableArray<ParameterAttributes> right) =>
		left.IsDefaultOrEmpty && right.IsDefaultOrEmpty ||
		!left.IsDefault && !right.IsDefault && left.SequenceEqual(right);

	private static bool HaveSameInternalCallAbi(
		InternalCallAbi left,
		InternalCallAbi right) =>
		left.StackBytes == right.StackBytes &&
		left.ReturnBufferStackOffset == right.ReturnBufferStackOffset &&
		left.Arguments.SequenceEqual(right.Arguments);

	private static bool HaveSameInstructionOperand(object? left, object? right) =>
		(left, right) switch
		{
			(null, null) => true,
			(int[] leftTargets, int[] rightTargets) =>
				leftTargets.AsSpan().SequenceEqual(rightTargets),
			(float leftValue, float rightValue) =>
				BitConverter.SingleToInt32Bits(leftValue) ==
					BitConverter.SingleToInt32Bits(rightValue),
			(double leftValue, double rightValue) =>
				BitConverter.DoubleToInt64Bits(leftValue) ==
					BitConverter.DoubleToInt64Bits(rightValue),
			_ => Equals(left, right)
		};

	private static bool RequiresVirtualDispatch(CilInstruction instruction, CilMethod method) =>
		instruction.OpCode == OpCodes.Callvirt &&
		method.IsVirtual &&
		!method.IsFinal &&
		!method.DeclaringTypeIsSealed;

	private void ValidateMethodSignature(
		CilMethod method,
		bool isEntry,
		bool isExport)
	{
		if (isEntry && method.Signature.Header.IsInstance)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"The image entry point must be static.",
				method.DisplayName);
		}

		if (isEntry &&
			method.ParameterCount != 0 &&
			!HasAmigaStartupArguments(method))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"The image entry point must either have no parameters or use the Amiga startup signature Main(int argLength, CONST_STRPTR argText).",
				method.DisplayName);
		}

		ValidateType(
			method.Signature.ReturnType,
			method,
			method.Signature.ReturnType.IsNullable &&
				(method.IsImport || method.ExternalCall is not null)
				? "nullable platform return type"
				: "return type",
			admitsSpanReturn:
				!isEntry &&
				!isExport &&
				!method.IsImport &&
				method.ExternalCall is null &&
				!method.IsVirtual &&
				!method.DeclaringTypeIsInterface);
		foreach (var parameter in method.Signature.ParameterTypes)
		{
			ValidateType(
				parameter,
				method,
				"parameter",
				admitsSpanParameter:
					(!isEntry &&
					 !isExport &&
					 !method.IsImport &&
					 !method.IsVirtual &&
					 !method.DeclaringTypeIsInterface) ||
					IsEqualityComparerShadowNullableMethod(method));
		}

		foreach (var local in method.Locals)
		{
			ValidateType(local, method, "local");
		}
	}

	private bool IsEqualityComparerShadowNullableMethod(CilMethod method) =>
		// This is the one admitted virtual aggregate ABI: both private comparer
		// shapes pass the proven reference-free nullable value intact on the stack.
		method.Name is "Equals" or "GetHashCode" &&
		method.ConstructedDeclaringType is
		{
			GenericArguments: [var element]
		} declaringType &&
		_module.IsSupportedNullableType(element) &&
		(declaringType.DisplayName.StartsWith(
			"CopperSharp.Runtime.ShadowEqualityComparer`1<",
			StringComparison.Ordinal) ||
		 declaringType.DisplayName.StartsWith(
			"CopperSharp.Runtime.IShadowEqualityComparer`1<",
			StringComparison.Ordinal));

	private void ValidateType(
		CilType type,
		CilMethod method,
		string role,
		bool admitsSpanParameter = false,
		bool admitsSpanReturn = false)
	{
		if (type.IsVoid)
		{
			if (role == "return type")
			{
				return;
			}

			throw UnsupportedType(type, method, role);
		}

		if (type.IsFloatingPoint)
		{
			if (_request.FloatingPoint == M68kFloatingPointMode.Disabled)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"Floating-point {role} '{type.DisplayName}' is disabled; select an FPU or SoftFloat mode.",
					method.DisplayName);
			}
			return;
		}

		if (type.IsNullable)
		{
			if ((role == "local" ||
				 role == "nullable platform return type" ||
				 role == "parameter" && admitsSpanParameter ||
				 role == "return type" && admitsSpanReturn) &&
				_module.IsSupportedNullableType(type))
			{
				return;
			}

			throw UnsupportedType(type, method, role);
		}

		if (CompilationModule.IsSupportedSpanLikeType(type))
		{
			if (role == "local" ||
				role == "parameter" && admitsSpanParameter ||
				role == "return type" && admitsSpanReturn)
			{
				return;
			}

			throw UnsupportedType(type, method, role);
		}

		if (CompilationModule.IsDefaultInterpolatedStringHandler(type))
		{
			if (role == "local")
			{
				return;
			}
			throw UnsupportedType(type, method, role);
		}

		if ((!type.IsSupportedScalar || type.Size > 4) &&
			!Is64BitScalar(type) &&
			!_module.IsTransparentScalarType(type) &&
			!_module.IsSupportedStructType(type))
		{
			throw UnsupportedType(type, method, role);
		}
	}

	private static bool Is64BitScalar(CilType type) =>
		type.IsSupportedScalar && type.Size == 8;

	private static bool HasAmigaStartupArguments(CilMethod method) =>
		method.Signature.ParameterTypes.Length == 2 &&
		method.Signature.ParameterTypes[0].DisplayName == "int" &&
		method.Signature.ParameterTypes[1].DisplayName == "Amiga.CONST_STRPTR";

	private static M68kCompilationException UnsupportedType(
		CilType type,
		CilMethod method,
		string role) =>
		new(
			M68kDiagnosticIds.UnsupportedSignature,
			$"Unsupported {role} '{type.DisplayName}'. This compiler version accepts 32-bit scalar values.",
			method.DisplayName);

	private void CompileMethod(CilMethod method)
	{
		_loadedPlatformBase =
			_platformBaseMethodEntries.TryGetValue(method.Identity, out var entryIdentity) &&
			entryIdentity is not null &&
			_usedPlatformBases.TryGetValue(entryIdentity, out var entryPlatformBase)
				? entryPlatformBase
				: null;
		var ilOptimizations = CilOptimizer.Optimize(method, _module);
		var internalAbi = GetInternalCallAbi(method);
		var machineFunction = CilMachineIrBuilder.Build(
			method,
			_module,
			_request.Cpu,
			RequiresRuntimeFrame(method),
			ilOptimizations,
			internalAbi.Arguments
				.Select(static argument => argument.Register)
				.ToArray());
		machineFunction.PreserveCalleeSavedRegisters =
			!_rootOnlyMethods.Contains(method.Identity);
		_terminalDeadStoreStatistics[method.Identity] =
			M68kTerminalDeadStoreEliminator.Run(
			machineFunction,
			method,
			_module,
			_terminatingEntryMethods.Contains(method.Identity),
			_escapedStaticFields);
		var allocatedFunction = M68kRegisterAllocatorPipeline.Run(
			machineFunction,
			!M68kCompiler.IsManagedRuntime(_request) ||
			(_managedPoolRuntime is { Initialize: var runtimeEntry } &&
			 method.ModuleName == runtimeEntry.ModuleName &&
			 method.DeclaringType == runtimeEntry.DeclaringType),
			_callerBorrowedByrefMethods.Contains(method.Identity),
			method.Signature.ReturnType.Kind == CilTypeKind.ManagedPointer &&
			!CilManagedByrefSummary.TryGetBorrowedParameterReturn(method, out _));
		var reachableStackStates = CilStackAnalyzer.AnalyzeTypes(method, _module);
		var branchTargets = GetBranchTargets(method.Instructions);
		var reachableOffsets = reachableStackStates.Keys.ToHashSet();
		var platformBaseBlockEntries = AnalyzePlatformBaseBlockEntries(
			method,
			reachableOffsets,
			entryIdentity);
		var exceptionStateBlockEntries = method.ExceptionRegions.Count == 0
			? null
			: branchTargets
				.Concat(method.ExceptionRegions.Select(static region => region.HandlerOffset))
				.ToHashSet();
		_currentFrameLayout = CreateFrameLayout(
			method,
			internalAbi,
			branchTargets,
			reachableOffsets,
			reachableStackStates);
		RecordRuntimeFrameLayout(method);
		_assembler.AlignWord();
		var emittedStart = _assembler.Offset;
		if (ManagedRuntimeAlias(method) is { } runtimeAlias)
		{
			_assembler.Mark(runtimeAlias);
		}
		_assembler.Mark(MethodLabel(method));
		EmitAllocatedMethod(
			method,
			internalAbi,
			allocatedFunction,
			platformBaseBlockEntries,
			exceptionStateBlockEntries);
		_assembler.Mark(MethodEndLabel(method));
		var emittedEnd = _assembler.Offset;
		var stackMemoryInstructions = _assembler
			.GetInstructionStream(emittedStart)
			.Count(
				instruction =>
				{
					var effects =
						M68kInstructionDataflow.GetEffects(instruction);
					return instruction.Offset < emittedEnd &&
						((effects.ReadsMemory | effects.WritesMemory) &
							M68kMemorySet.Stack) != 0;
				});
		_allocationStatistics[method.Identity] =
			allocatedFunction.Statistics with
			{
				CodeBytes = emittedEnd - emittedStart,
				StackMemoryInstructions = stackMemoryInstructions
			};
	}

	private bool IsIdentityReturnBody(CilMethod method)
	{
		var body = method.Instructions
			.Where(static instruction => instruction.OpCode != OpCodes.Nop)
			.ToArray();
		if (body.Length == 2 &&
			body[1].OpCode == OpCodes.Ret &&
			TryGetArgumentIndex(body[0], out var argumentIndex) &&
			argumentIndex == 0 &&
			method.Signature.ParameterTypes.Length == 1 &&
			method.Signature.ParameterTypes[0].DisplayName == method.Signature.ReturnType.DisplayName)
		{
			return true;
		}

		if (body.Length == 3 &&
			body[2].OpCode == OpCodes.Ret &&
			(body[1].OpCode == OpCodes.Call || body[1].OpCode == OpCodes.Callvirt) &&
			TryGetLoadArgumentAddressIndex(body[0], out var addressArgumentIndex) &&
			addressArgumentIndex == 0 &&
			method.Signature.ParameterTypes.Length == 1 &&
			method.Signature.ReturnType.DisplayName == "uint" &&
			_module.IsTransparentScalarType(method.Signature.ParameterTypes[0]))
		{
			var target = _module.ResolveMethodToken(
				(int)body[1].Operand!,
				method,
				body[1].Offset);
			return IsTransparentScalarRawGetter(target);
		}

		return false;
	}

	private bool IsAlwaysInlinedMethod(CilMethod method) =>
		GetInternalRegisterAbi(method) switch
		{
			[M68kRegister.D0] => IsIdentityReturnBody(method),
			[] => TryGetConstantReturnBody(method, out _),
			_ => false
		};

	private static bool TryGetConstantReturnBody(
		CilMethod method,
		out int constant)
	{
		var body = method.Instructions
			.Where(static instruction => instruction.OpCode != OpCodes.Nop)
			.ToArray();
		if (!method.Signature.Header.IsInstance &&
			method.Signature.ParameterTypes.Length == 0 &&
			method.Signature.ReturnType.IsSupportedScalar &&
			!method.Signature.ReturnType.IsVoid &&
			body is [var load, var ret] &&
			ret.OpCode == OpCodes.Ret &&
			TryGetConstant(load, out constant))
		{
			return true;
		}

		constant = 0;
		return false;
	}

	private bool TryEmitLoadBranch(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 1 >= instructions.Count)
		{
			return false;
		}

		var branch = instructions[startIndex + 1];
		var branchOp = branch.OpCode;
		if ((branchOp != OpCodes.Brtrue && branchOp != OpCodes.Brtrue_S &&
			 branchOp != OpCodes.Brfalse && branchOp != OpCodes.Brfalse_S) ||
			branchTargets.Contains(branch.Offset) ||
			!TryEmitBranchLoadValue(method, instructions[startIndex]))
		{
			return false;
		}

		_assembler.EmitBranch(
			branchOp == OpCodes.Brtrue || branchOp == OpCodes.Brtrue_S
				? M68kCondition.NotEqual
				: M68kCondition.Equal,
			IlLabel(method, (int)branch.Operand!));
		consumed = 2;
		return true;
	}

	private bool TryEmitBranchLoadValue(CilMethod method, CilInstruction instruction)
	{
		if (instruction.OpCode == OpCodes.Ldsfld)
		{
			EmitLoadStaticFieldToRegister(method, instruction, M68kRegister.D0);
			return true;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(method, instruction, argumentIndex);
			var argumentKind = StackKindForType(TypeForArgument(method, argumentIndex));
			if (ArgumentRegister(argumentIndex) is { } register)
			{
				EmitMoveRegisterToD0(register);
				if (CilStackValueLayout.IsByte(argumentKind))
				{
					_assembler.EmitWord(0x4A00); // TST.B D0
				}
				else if (register == M68kRegister.D0)
				{
					_assembler.EmitWord(0x4A80); // TST.L D0
				}
				return true;
			}

			var displacement = FrameDisplacement(
				ArgumentOffset(method, argumentIndex),
				_currentStackDepth);
			if (CilStackValueLayout.IsByte(argumentKind))
			{
				EmitLoadByteFromFrame(M68kRegister.D0, displacement);
				_assembler.EmitWord(0x4A00); // TST.B D0
			}
			else
			{
				EmitLoadRegisterFromStack(M68kRegister.D0, displacement);
			}
			return true;
		}

		if (TryGetLoadLocalIndex(instruction, out var localIndex))
		{
			ValidateLocal(method, instruction, localIndex);
			var localKind = StackKindForType(method.Locals[localIndex]);
			if (LocalRegister(localIndex) is { } register)
			{
				EmitMoveRegisterToD0(register);
				if (CilStackValueLayout.IsByte(localKind))
				{
					_assembler.EmitWord(0x4A00); // TST.B D0
				}
				else if (register == M68kRegister.D0)
				{
					_assembler.EmitWord(0x4A80); // TST.L D0
				}
				return true;
			}

			var displacement = FrameDisplacement(
				LocalOffset(method, localIndex),
				_currentStackDepth);
			if (CilStackValueLayout.IsByte(localKind))
			{
				EmitLoadByteFromFrame(M68kRegister.D0, displacement);
				_assembler.EmitWord(0x4A00); // TST.B D0
			}
			else
			{
				EmitLoadRegisterFromStack(M68kRegister.D0, displacement);
			}
			return true;
		}

		return false;
	}

	private bool TryEmitAddressNullBranch(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex >= instructions.Count ||
			instructions[startIndex].OpCode != OpCodes.Call)
		{
			return false;
		}

		var call = instructions[startIndex];
		var target = _module.ResolveMethodToken(
			(int)call.Operand!,
			method,
			call.Offset);
		var isNull = target.ImportName == "intrinsic:aptr-is-null";
		if (!isNull &&
			target.ImportName != "intrinsic:aptr-is-not-null")
		{
			return false;
		}

		var branchIndex = startIndex + 1;
		var storedLocalIndex = -1;
		if (branchIndex + 2 < instructions.Count &&
			TryGetStoreLocalIndex(instructions[branchIndex], out storedLocalIndex) &&
			TryGetLoadLocalIndex(instructions[branchIndex + 1], out var loadedLocalIndex) &&
			loadedLocalIndex == storedLocalIndex)
		{
			branchIndex += 2;
		}

		if (branchIndex >= instructions.Count ||
			!TryGetBooleanBranchCondition(
				instructions[branchIndex].OpCode,
				out var branchCondition) ||
			instructions[branchIndex].Operand is not int targetOffset ||
			(storedLocalIndex >= 0 &&
				IsLocalReadAfter(instructions, branchIndex, storedLocalIndex)))
		{
			return false;
		}

		var activeExceptionGroups = GetActiveExceptionGroups(method, call.Offset);
		for (var index = startIndex + 1; index <= branchIndex; index++)
		{
			var instruction = instructions[index];
			if (branchTargets.Contains(instruction.Offset) ||
				method.ExceptionRegions.Any(region =>
					region.HandlerOffset == instruction.Offset) ||
				!activeExceptionGroups.SequenceEqual(
					GetActiveExceptionGroups(method, instruction.Offset)))
			{
				return false;
			}
		}

		EmitPopRegister(M68kRegister.A0);
		_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		_assembler.EmitBranch(
			isNull ? InvertCondition(branchCondition) : branchCondition,
			IlLabel(method, targetOffset));
		consumed = branchIndex - startIndex + 1;
		return true;
	}

	private bool TryEmitNullableHasValueBranch(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 2 >= instructions.Count ||
			!TryGetLoadLocalAddressIndex(instructions[startIndex], out var localIndex))
		{
			return false;
		}

		var call = instructions[startIndex + 1];
		if ((call.OpCode != OpCodes.Call && call.OpCode != OpCodes.Callvirt) ||
			branchTargets.Contains(call.Offset))
		{
			return false;
		}

		var branch = instructions[startIndex + 2];
		if ((branch.OpCode != OpCodes.Brtrue && branch.OpCode != OpCodes.Brtrue_S &&
			 branch.OpCode != OpCodes.Brfalse && branch.OpCode != OpCodes.Brfalse_S) ||
			branchTargets.Contains(branch.Offset))
		{
			return false;
		}

		ValidateLocal(method, instructions[startIndex], localIndex);
		var localType = method.Locals[localIndex];
		if (!localType.IsNullable ||
			!_module.IsSupportedNullableType(localType))
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)call.Operand!,
			method,
			call.Offset);
		if (target.ImportName?.StartsWith("intrinsic:nullable-has-value:", StringComparison.Ordinal) != true)
		{
			return false;
		}

		EmitNullableHasValueTestFromLocal(method, localIndex);
		_assembler.EmitBranch(
			branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S
				? M68kCondition.NotEqual
				: M68kCondition.Equal,
			IlLabel(method, (int)branch.Operand!));
		consumed = 3;
		return true;
	}

	private bool TryEmitNullableHasValueComparisonStoreBranch(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 5 >= instructions.Count ||
			!TryGetLoadLocalAddressIndex(instructions[startIndex], out var nullableLocalIndex))
		{
			return false;
		}

		var call = instructions[startIndex + 1];
		if ((call.OpCode != OpCodes.Call && call.OpCode != OpCodes.Callvirt) ||
			branchTargets.Contains(call.Offset) ||
			!TryGetConstant(instructions[startIndex + 2], out var zero) ||
			zero != 0 ||
			instructions[startIndex + 3].OpCode != OpCodes.Ceq)
		{
			return false;
		}

		var store = instructions[startIndex + 4];
		var load = instructions[startIndex + 5];
		if (startIndex + 6 >= instructions.Count ||
			branchTargets.Contains(instructions[startIndex + 2].Offset) ||
			branchTargets.Contains(instructions[startIndex + 3].Offset) ||
			branchTargets.Contains(store.Offset) ||
			branchTargets.Contains(load.Offset) ||
			!TryGetStoreLocalIndex(store, out var storeLocalIndex) ||
			!TryGetLoadLocalIndex(load, out var loadLocalIndex) ||
			loadLocalIndex != storeLocalIndex ||
			!TryGetBooleanBranchCondition(
				instructions[startIndex + 6].OpCode,
				out var branchCondition) ||
			instructions[startIndex + 6].Operand is not int targetOffset ||
			branchTargets.Contains(instructions[startIndex + 6].Offset) ||
			IsLocalReadAfter(instructions, startIndex + 6, storeLocalIndex))
		{
			return false;
		}

		ValidateLocal(method, instructions[startIndex], nullableLocalIndex);
		var localType = method.Locals[nullableLocalIndex];
		if (!localType.IsNullable ||
			!_module.IsSupportedNullableType(localType))
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)call.Operand!,
			method,
			call.Offset);
		if (target.ImportName?.StartsWith("intrinsic:nullable-has-value:", StringComparison.Ordinal) != true)
		{
			return false;
		}

		var condition = branchCondition == M68kCondition.NotEqual
			? M68kCondition.Equal
			: M68kCondition.NotEqual;
		EmitNullableHasValueTestFromLocal(method, nullableLocalIndex);
		_assembler.EmitBranch(condition, IlLabel(method, targetOffset));
		consumed = 7;
		return true;
	}

	private void EmitNullableHasValueTestFromLocal(CilMethod method, int localIndex)
	{
		var localType = method.Locals[localIndex];
		if (IsCompactNullableType(localType) &&
			LocalRegister(localIndex) is { } register)
		{
			EmitMoveRegisterToD0(register);
			if (register == M68kRegister.D0)
			{
				_assembler.EmitWord(0x4A80); // TST.L D0
			}
			return;
		}

		var displacement = LocalOffset(method, localIndex);
		EmitLoadRegisterFromStack(
			M68kRegister.D0,
			FrameDisplacement(
				IsCompactNullableType(localType)
					? displacement
					: checked((short)(displacement + 4)),
				_currentStackDepth));
		_assembler.EmitWord(0x4A80); // TST.L D0
	}

	private bool TryEmitStoredCompactNullableHasValueBranch(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int storeIndex,
		int localIndex,
		IReadOnlySet<int> branchTargets,
		out int branchIndex)
	{
		branchIndex = 0;
		ValidateLocal(method, instructions[storeIndex], localIndex);
		var localType = method.Locals[localIndex];
		if (!localType.IsNullable ||
			!_module.IsSupportedNullableType(localType) ||
			!IsCompactNullableType(localType))
		{
			return false;
		}

		var addressIndex = storeIndex + 1;
		if (!TrySkipNonTargetNops(instructions, branchTargets, ref addressIndex) ||
			addressIndex >= instructions.Count ||
			branchTargets.Contains(instructions[addressIndex].Offset) ||
			!TryGetLoadLocalAddressIndex(instructions[addressIndex], out var addressLocalIndex) ||
			addressLocalIndex != localIndex)
		{
			return false;
		}

		var callIndex = addressIndex + 1;
		if (!TrySkipNonTargetNops(instructions, branchTargets, ref callIndex) ||
			callIndex >= instructions.Count ||
			branchTargets.Contains(instructions[callIndex].Offset))
		{
			return false;
		}

		var call = instructions[callIndex];
		if (call.OpCode != OpCodes.Call &&
			call.OpCode != OpCodes.Callvirt)
		{
			return false;
		}

		var target = _module.ResolveMethodToken((int)call.Operand!, method, call.Offset);
		if (target.ImportName?.StartsWith("intrinsic:nullable-has-value:", StringComparison.Ordinal) != true)
		{
			return false;
		}

		var nextIndex = callIndex + 1;
		if (!TrySkipNonTargetNops(instructions, branchTargets, ref nextIndex) ||
			nextIndex >= instructions.Count ||
			branchTargets.Contains(instructions[nextIndex].Offset))
		{
			return false;
		}

		var branch = instructions[nextIndex];
		if ((branch.OpCode != OpCodes.Brtrue && branch.OpCode != OpCodes.Brtrue_S &&
			 branch.OpCode != OpCodes.Brfalse && branch.OpCode != OpCodes.Brfalse_S) ||
			branch.Operand is not int targetOffset)
		{
			return TryEmitStoredCompactNullableHasValueComparisonStoreBranch(
				method,
				instructions,
				nextIndex,
				branchTargets,
				out branchIndex);
		}

		branchIndex = nextIndex;
		_assembler.EmitBranch(
			branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S
				? M68kCondition.NotEqual
				: M68kCondition.Equal,
			IlLabel(method, targetOffset));
		return true;
	}

	private bool TryEmitStoredCompactNullableHasValueComparisonStoreBranch(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int zeroIndex,
		IReadOnlySet<int> branchTargets,
		out int branchIndex)
	{
		branchIndex = 0;
		if (zeroIndex + 4 >= instructions.Count ||
			!TryGetConstant(instructions[zeroIndex], out var zero) ||
			zero != 0)
		{
			return false;
		}

		var compareIndex = zeroIndex + 1;
		var storeIndex = zeroIndex + 2;
		var loadIndex = zeroIndex + 3;
		branchIndex = zeroIndex + 4;
		if (branchTargets.Contains(instructions[compareIndex].Offset) ||
			branchTargets.Contains(instructions[storeIndex].Offset) ||
			branchTargets.Contains(instructions[loadIndex].Offset) ||
			branchTargets.Contains(instructions[branchIndex].Offset) ||
			instructions[compareIndex].OpCode != OpCodes.Ceq ||
			!TryGetStoreLocalIndex(instructions[storeIndex], out var storeLocalIndex) ||
			!TryGetLoadLocalIndex(instructions[loadIndex], out var loadLocalIndex) ||
			loadLocalIndex != storeLocalIndex ||
			!TryGetBooleanBranchCondition(
				instructions[branchIndex].OpCode,
				out var branchCondition) ||
			instructions[branchIndex].Operand is not int targetOffset ||
			IsLocalReadAfter(instructions, branchIndex, storeLocalIndex))
		{
			return false;
		}

		_assembler.EmitBranch(
			branchCondition == M68kCondition.NotEqual
				? M68kCondition.Equal
				: M68kCondition.NotEqual,
			IlLabel(method, targetOffset));
		return true;
	}

	private static bool TrySkipNonTargetNops(
		IReadOnlyList<CilInstruction> instructions,
		IReadOnlySet<int> branchTargets,
		ref int index)
	{
		while (index < instructions.Count &&
			instructions[index].OpCode == OpCodes.Nop)
		{
			if (branchTargets.Contains(instructions[index].Offset))
			{
				return false;
			}

			index++;
		}

		return true;
	}

	private bool TryEmitDirectFieldLoad(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (!TryGetArgumentValueExpression(
			method,
			instructions,
			startIndex,
			out var objectValue,
			out var valueConsumed))
		{
			return false;
		}

		var fieldIndex = startIndex + valueConsumed;
		if (fieldIndex >= instructions.Count ||
			instructions[fieldIndex].OpCode != OpCodes.Ldfld ||
			branchTargets.Contains(instructions[fieldIndex].Offset) ||
			(!objectValue.AllowsInternalBranchTargets &&
				HasBranchTarget(
					branchTargets,
					instructions,
					startIndex,
					startIndex + 1,
					fieldIndex - 1)))
		{
			return false;
		}

		var field = _module.ResolveFieldToken(
			(int)instructions[fieldIndex].Operand!,
			method,
			instructions[fieldIndex].Offset);
		if (field.IsStatic)
		{
			return false;
		}

		ValidateType(field.Type, method, "field");
		var returnsDirectly = TryGetDirectReturnIndex(
			instructions,
			fieldIndex,
			branchTargets,
			out var returnIndex);
		EmitArgumentValueToRegister(method, objectValue, _currentStackDepth, M68kRegister.A0);
		EmitFieldLoadFromA0(method, instructions[fieldIndex], field, pushResult: !returnsDirectly);
		if (returnsDirectly)
		{
			EmitFrameTeardown(method);
			_assembler.EmitWord(0x4E75); // RTS
		}

		consumed = (returnsDirectly ? returnIndex : fieldIndex) - startIndex + 1;
		return true;
	}

	private bool TryEmitDirectRegisterCall(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (!TryCollectDirectCallArguments(
			caller,
			instructions,
			startIndex,
			branchTargets,
			out var values,
			out var index))
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[index].Operand!,
			caller,
			instructions[index].Offset);
		if (target.Definition is not { } definition)
		{
			return false;
		}
		if (definition.Signature.ReturnType.IsNullable)
		{
			return false;
		}

		var returnsDirectly = TryGetDirectReturnIndex(
			instructions,
			index,
			branchTargets,
			out var returnIndex);
		var discardIndex = 0;
		var discardsResult =
			!returnsDirectly &&
			!definition.Signature.ReturnType.IsVoid &&
			TryGetDiscardedResultIndex(
				instructions,
				index,
				branchTargets,
				out discardIndex);
		if (definition.ExternalCall is { } externalCall)
		{
			if (Is64BitScalar(definition.Signature.ReturnType) ||
				definition.Signature.ParameterTypes.Any(Is64BitScalar))
			{
				return false;
			}

			return TryEmitDirectExternalRegisterCall(
				caller,
				definition,
				externalCall,
				values,
				branchTargets,
				returnsDirectly,
				returnIndex,
				discardsResult,
				discardIndex,
				startIndex,
				index,
				out consumed);
		}

		if (definition.IsImport)
		{
			if (Is64BitScalar(definition.Signature.ReturnType) ||
				definition.Signature.ParameterTypes.Any(Is64BitScalar))
			{
				return false;
			}

			return TryEmitDirectRegisterImportCall(
				caller,
				definition,
				values,
				returnsDirectly,
				returnIndex,
				discardsResult,
				discardIndex,
				startIndex,
				index,
				out consumed);
		}

		if (returnsDirectly)
		{
			return false;
		}

		if (GetInternalRegisterAbi(definition) is not { } registerAbi ||
			registerAbi.Count != values.Count)
		{
			return false;
		}

		for (var argument = 0; argument < values.Count; argument++)
		{
			EmitInternalCallArgumentValueToRegister(
				caller,
				definition,
				values[argument],
				argument,
				_currentStackDepth,
				registerAbi[argument]);
		}

		if (TryGetInlineCandidate(definition, registerAbi, out var inlineCandidate) &&
			ShouldInline(inlineCandidate))
		{
			EmitInlineCandidate(inlineCandidate);
			consumed = index - startIndex + 1;
			return true;
		}

		_assembler.EmitBsr(MethodLabel(definition));
		_loadedPlatformBase = null;
		if (!definition.Signature.ReturnType.IsVoid && !discardsResult)
		{
			if (IsInternalAddressReturn(definition.Signature.ReturnType))
			{
				if (!returnsDirectly)
				{
					_assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
				}
			}
			else if (!returnsDirectly)
			{
				EmitPushD0();
			}
		}

		if (returnsDirectly)
		{
			EmitFrameTeardown(caller);
			_assembler.EmitWord(0x4E75); // RTS
		}

		consumed = (returnsDirectly ? returnIndex : discardsResult ? discardIndex : index) - startIndex + 1;
		return true;
	}

	private bool TryCollectDirectCallArguments(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out IReadOnlyList<ArgumentValue> values,
		out int callIndex)
	{
		var collected = new List<ArgumentValue>();
		callIndex = startIndex;
		var callMayBeBranchTarget = false;
		while (callIndex < instructions.Count)
		{
			var instruction = instructions[callIndex];
			if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
			{
				break;
			}

			if (callIndex != startIndex && branchTargets.Contains(instruction.Offset))
			{
				values = Array.Empty<ArgumentValue>();
				return false;
			}

			if (!TryGetArgumentValueExpression(
				caller,
				instructions,
				callIndex,
				out var value,
				out var valueConsumed))
			{
				values = Array.Empty<ArgumentValue>();
				return false;
			}

			if (!value.AllowsInternalBranchTargets &&
				HasBranchTarget(
					branchTargets,
					instructions,
					startIndex,
					callIndex + 1,
					callIndex + valueConsumed - 1))
			{
				values = Array.Empty<ArgumentValue>();
				return false;
			}

			callMayBeBranchTarget = value.AllowsInternalBranchTargets;
			collected.Add(value);
			callIndex += valueConsumed;
		}

		if (callIndex >= instructions.Count ||
			(branchTargets.Contains(instructions[callIndex].Offset) && !callMayBeBranchTarget))
		{
			values = Array.Empty<ArgumentValue>();
			return false;
		}

		var call = instructions[callIndex];
		var target = _module.ResolveMethodToken(
			(int)call.Operand!,
			caller,
			call.Offset);
		if (call.OpCode == OpCodes.Callvirt &&
			target.Definition is { Signature.Header.IsInstance: true })
		{
			values = Array.Empty<ArgumentValue>();
			return false;
		}

		values = collected;
		return true;
	}

	private void EmitInternalCallArgumentValueToRegister(
		CilMethod caller,
		CilMethod callee,
		ArgumentValue value,
		int argumentIndex,
		int stackDepth,
		M68kRegister register)
	{
		if (argumentIndex == 0 &&
			callee.Signature.Header.IsInstance &&
			callee.Name != ".ctor" &&
			IsTransparentScalarDeclaringType(callee) &&
			value.Instruction is { } instruction)
		{
			if (TryGetLoadLocalAddressIndex(instruction, out var localAddressIndex))
			{
				ValidateLocal(caller, instruction, localAddressIndex);
				EmitLoadRegisterFromStack(
					register,
					FrameDisplacement(
						LocalOffset(caller, localAddressIndex),
						stackDepth));
				return;
			}

			if (TryGetLoadArgumentAddressIndex(instruction, out var argumentAddressIndex))
			{
				ValidateArgument(caller, instruction, argumentAddressIndex);
				if (ArgumentRegister(argumentAddressIndex) is { } argumentRegister)
				{
					EmitMoveRegisterToRegister(argumentRegister, register);
					return;
				}

				EmitLoadRegisterFromStack(
					register,
					FrameDisplacement(
						ArgumentOffset(caller, argumentAddressIndex),
						stackDepth));
				return;
			}
		}

		EmitArgumentValueToRegister(caller, value, stackDepth, register);
	}

	private bool TryEmitDirectExternalRegisterCall(
		CilMethod caller,
		CilMethod definition,
		CilExternalCall externalCall,
		IReadOnlyList<ArgumentValue> values,
		IReadOnlySet<int> branchTargets,
		bool returnsDirectly,
		int returnIndex,
		bool discardsResult,
		int discardIndex,
		int startIndex,
		int callIndex,
		out int consumed)
	{
		consumed = 0;
		if (externalCall.Abi.ParameterRegisters.Count != values.Count)
		{
			return false;
		}

		if (returnsDirectly &&
			CanEmitExternalTailCall(caller, definition, externalCall))
		{
			EmitDirectExternalRegisterTailCall(caller, definition, externalCall, values);
			consumed = returnIndex - startIndex + 1;
			return true;
		}

		EmitDirectExternalRegisterCall(caller, definition, externalCall, values);
		if (!definition.Signature.ReturnType.IsVoid && !returnsDirectly && !discardsResult)
		{
			EmitPushD0();
		}

		if (returnsDirectly)
		{
			EmitFrameTeardown(caller);
			_assembler.EmitWord(0x4E75); // RTS
		}

		consumed = (returnsDirectly ? returnIndex : discardsResult ? discardIndex : callIndex) - startIndex + 1;
		return true;
	}

	private bool TryEmitDirectRegisterImportCall(
		CilMethod caller,
		CilMethod definition,
		IReadOnlyList<ArgumentValue> values,
		bool returnsDirectly,
		int returnIndex,
		bool discardsResult,
		int discardIndex,
		int startIndex,
		int callIndex,
		out int consumed)
	{
		consumed = 0;
		if (definition.ImportAbi is not { } importAbi ||
			importAbi.ParameterRegisters.Count != values.Count)
		{
			return false;
		}

		EmitDirectRegisterImportCall(caller, definition, importAbi, values);
		if (!definition.Signature.ReturnType.IsVoid && !returnsDirectly && !discardsResult)
		{
			EmitPushD0();
		}

		if (returnsDirectly)
		{
			EmitFrameTeardown(caller);
			_assembler.EmitWord(0x4E75); // RTS
		}

		consumed = (returnsDirectly ? returnIndex : discardsResult ? discardIndex : callIndex) - startIndex + 1;
		return true;
	}

	private void EmitDirectRegisterImportCall(
		CilMethod caller,
		CilMethod definition,
		CilRegisterAbi importAbi,
		IReadOnlyList<ArgumentValue> values)
	{
		for (var argument = 0; argument < values.Count; argument++)
		{
			EmitArgumentValueToRegister(
				caller,
				values[argument],
				_currentStackDepth,
				importAbi.ParameterRegisters[argument]);
		}

		_assembler.EmitJsr(definition.ImportName!, external: true);
		_loadedPlatformBase = null;
		if (!definition.Signature.ReturnType.IsVoid)
		{
			EmitMoveRegisterToD0(importAbi.ReturnRegister);
		}
	}

	private bool TryGetInlineCandidate(
		CilMethod target,
		IReadOnlyList<M68kRegister> registerAbi,
		out InlineCandidate candidate)
	{
		if (target.Name == ".ctor" &&
			registerAbi is [M68kRegister.A0, M68kRegister.D0] &&
			IsSimpleWrapperConstructorBody(target))
		{
			// 68000 estimates: inline MOVE.L D0,(A0) vs BSR.W plus callee MOVE.L/RTS.
			candidate = new InlineCandidate(
				InlineCandidateKind.SimpleWrapperConstructor,
				InlineBytes: 2,
				CallBytes: 4,
				InlineCycles: 12,
				CallCycles: 46);
			return true;
		}

		if (registerAbi is [M68kRegister.D0] &&
			IsIdentityReturnBody(target))
		{
			candidate = new InlineCandidate(
				InlineCandidateKind.IdentityReturn,
				InlineBytes: 0,
				CallBytes: 4,
				InlineCycles: 0,
				CallCycles: 34);
			return true;
		}

		if (registerAbi is [M68kRegister.D0] &&
			IsAlignLongReturnBody(target))
		{
			candidate = new InlineCandidate(
				InlineCandidateKind.MaskedAddReturn,
				InlineBytes: 8,
				CallBytes: 4,
				InlineCycles: 20,
				CallCycles: 34);
			return true;
		}

		candidate = default;
		return false;
	}

	private static bool ShouldInline(InlineCandidate candidate) =>
		candidate.Kind == InlineCandidateKind.IdentityReturn ||
		candidate.SavedBytes > 0 || candidate.SavedCycles >= 16;

	private bool IsAlignLongReturnBody(CilMethod method)
	{
		if (!TryGetArgumentValueExpression(
				method,
				method.Instructions,
				0,
				out var value,
				out var valueConsumed) ||
				value.Instruction is null ||
				valueConsumed + 4 >= method.Instructions.Count ||
				!TryGetConstant(method.Instructions[valueConsumed], out var addend) ||
				addend != 3 ||
				method.Instructions[valueConsumed + 1].OpCode != OpCodes.Add ||
				!TryGetConstant(method.Instructions[valueConsumed + 2], out var mask) ||
				mask != -4 ||
				method.Instructions[valueConsumed + 3].OpCode != OpCodes.And ||
				method.Instructions[valueConsumed + 4].OpCode != OpCodes.Ret)
		{
			return false;
		}

		return true;
	}

	private void EmitInlineCandidate(InlineCandidate candidate)
	{
		switch (candidate.Kind)
		{
			case InlineCandidateKind.SimpleWrapperConstructor:
				_assembler.EmitWord(0x2080); // MOVE.L D0,(A0)
				return;
			case InlineCandidateKind.IdentityReturn:
				return;
			case InlineCandidateKind.MaskedAddReturn:
				_assembler.EmitWord(0x5680); // ADDQ.L #3,D0
				EmitAndImmediateToDataRegister(M68kRegister.D0, -4);
				return;
			default:
				throw new InvalidOperationException($"Unsupported inline candidate '{candidate.Kind}'.");
		}
	}

	private void EmitDirectExternalRegisterCall(
		CilMethod caller,
		CilMethod definition,
		CilExternalCall externalCall,
		IReadOnlyList<ArgumentValue> values)
	{
		EmitEnsurePlatformBase(externalCall.Convention, definition);
		var cacheRegister = externalCall.Convention.CacheRegister;
		var preservePlatformCache = cacheRegister is not null &&
			(externalCall.Abi.ReturnRegister == cacheRegister ||
			 externalCall.Abi.ParameterRegisters.Contains(cacheRegister.Value));
		if (preservePlatformCache)
		{
			EmitPushRegister(cacheRegister!.Value);
		}

		for (var argument = 0; argument < values.Count; argument++)
		{
			EmitArgumentValueToRegister(
				caller,
				values[argument],
				_currentStackDepth + (preservePlatformCache ? 1 : 0),
				externalCall.Abi.ParameterRegisters[argument]);
		}

		EmitBaseRelativeJsr(
			externalCall.Convention.BaseRegister,
			externalCall.Convention.Displacement);
		EmitExternalExceptionStatusCheck(externalCall.Convention);
		if (!definition.Signature.ReturnType.IsVoid)
		{
			EmitMoveRegisterToD0(externalCall.Abi.ReturnRegister);
		}
		if (preservePlatformCache)
		{
			EmitPopRegister(cacheRegister!.Value);
		}
	}

	private void EmitDirectExternalRegisterTailCall(
		CilMethod caller,
		CilMethod definition,
		CilExternalCall externalCall,
		IReadOnlyList<ArgumentValue> values)
	{
		for (var argument = 0; argument < values.Count; argument++)
		{
			EmitArgumentValueToRegister(
				caller,
				values[argument],
				_currentStackDepth,
				externalCall.Abi.ParameterRegisters[argument]);
		}

		EmitEnsurePlatformBase(externalCall.Convention, definition);
		EmitFrameTeardown(caller);
		EmitBaseRelativeJmp(
			externalCall.Convention.BaseRegister,
			externalCall.Convention.Displacement);
		_loadedPlatformBase = null;
	}

	private bool TryEmitDirectRegisterCallResultStore(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		IReadOnlyDictionary<int, ImmutableArray<CilStackValueKind>> reachableStackStates,
		out int consumed)
	{
		consumed = 0;
		if (!TryCollectDirectCallArguments(
			caller,
			instructions,
			startIndex,
			branchTargets,
			out var values,
			out var index))
		{
			return false;
		}

		if (!TryGetNextStoreIndex(
				instructions,
				index,
				branchTargets,
				out var storeIndex,
				out var destination) ||
			!reachableStackStates.TryGetValue(
				instructions[storeIndex].Offset,
				out var storeStackTypes) ||
			storeStackTypes.Length == 0)
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[index].Operand!,
			caller,
			instructions[index].Offset);
		if (target.Definition is not { } definition ||
			definition.Signature.ReturnType.IsVoid ||
			(!definition.Signature.ReturnType.IsNullable &&
				Is64BitScalar(definition.Signature.ReturnType)) ||
			definition.Signature.ParameterTypes.Any(Is64BitScalar))
		{
			return false;
		}

		if (definition.ExternalCall is { } externalCall)
		{
			if (externalCall.Abi.ParameterRegisters.Count != values.Count)
			{
				return false;
			}

			EmitDirectExternalRegisterCall(caller, definition, externalCall, values);
		}
		else if (definition.IsImport)
		{
			if (definition.ImportAbi is not { } importAbi ||
				importAbi.ParameterRegisters.Count != values.Count)
			{
				return false;
			}

			EmitDirectRegisterImportCall(caller, definition, importAbi, values);
		}
		else
		{
			if (definition.Signature.ReturnType.IsNullable ||
				GetInternalRegisterAbi(definition) is not { } registerAbi ||
				registerAbi.Count != values.Count)
			{
				return false;
			}

			for (var argument = 0; argument < values.Count; argument++)
			{
				EmitInternalCallArgumentValueToRegister(
					caller,
					definition,
					values[argument],
					argument,
					_currentStackDepth,
					registerAbi[argument]);
			}

			_assembler.EmitBsr(MethodLabel(definition));
			_loadedPlatformBase = null;
		}

		EmitStoreReturnToDestination(
			caller,
			definition,
			destination,
			stackDepth: storeStackTypes.Length - EvaluationSlotLongs(definition.Signature.ReturnType));
		if (definition.Signature.ReturnType.IsNullable &&
			destination.IsLocal &&
			TryEmitStoredCompactNullableHasValueBranch(
				caller,
				instructions,
				storeIndex,
				destination.Index,
				branchTargets,
				out var branchIndex))
		{
			consumed = branchIndex - startIndex + 1;
			return true;
		}

		consumed = storeIndex - startIndex + 1;
		return true;
	}

	private readonly record struct StoreDestination(bool IsLocal, int Index);

	private bool TryEmitCallResultStore(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		IReadOnlyDictionary<int, ImmutableArray<CilStackValueKind>> reachableStackStates,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 1 >= instructions.Count)
		{
			return false;
		}

		var callOp = instructions[startIndex].OpCode;
		if ((callOp != OpCodes.Call && callOp != OpCodes.Callvirt) ||
			!TryGetNextStoreIndex(
				instructions,
				startIndex,
				branchTargets,
				out var storeIndex,
				out var destination) ||
			!reachableStackStates.TryGetValue(
				instructions[storeIndex].Offset,
				out var storeStackTypes) ||
			storeStackTypes.Length == 0)
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[startIndex].Operand!,
			caller,
			instructions[startIndex].Offset);
		if (target.Definition is not { } definition ||
			target.Signature.ReturnType.IsVoid)
		{
			return false;
		}

		EmitCall(caller, instructions[startIndex], pushResult: false);
		EmitStoreReturnToDestination(
			caller,
			definition,
			destination,
			stackDepth: storeStackTypes.Length - EvaluationSlotLongs(definition.Signature.ReturnType));
		if (definition.Signature.ReturnType.IsNullable &&
			destination.IsLocal &&
			TryEmitStoredCompactNullableHasValueBranch(
				caller,
				instructions,
				storeIndex,
				destination.Index,
				branchTargets,
				out var branchIndex))
		{
			consumed = branchIndex - startIndex + 1;
			return true;
		}

		consumed = storeIndex - startIndex + 1;
		return true;
	}

	private bool TryEmitCallResultDiscard(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 1 >= instructions.Count)
		{
			return false;
		}

		var callOp = instructions[startIndex].OpCode;
		if (callOp != OpCodes.Call && callOp != OpCodes.Callvirt)
		{
			return false;
		}

		var popIndex = startIndex + 1;
		while (popIndex < instructions.Count &&
			instructions[popIndex].OpCode == OpCodes.Nop &&
			!branchTargets.Contains(instructions[popIndex].Offset))
		{
			popIndex++;
		}

		if (popIndex >= instructions.Count ||
			instructions[popIndex].OpCode != OpCodes.Pop ||
			branchTargets.Contains(instructions[popIndex].Offset))
		{
			return false;
		}

		if (caller.ExceptionRegions.Count != 0)
		{
			var activeExceptionGroups = GetActiveExceptionGroups(
				caller,
				instructions[startIndex].Offset);
			for (var index = startIndex + 1; index <= popIndex; index++)
			{
				var instruction = instructions[index];
				if (caller.ExceptionRegions.Any(region =>
						region.HandlerOffset == instruction.Offset) ||
					!activeExceptionGroups.SequenceEqual(
						GetActiveExceptionGroups(caller, instruction.Offset)))
				{
					return false;
				}
			}
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[startIndex].Operand!,
			caller,
			instructions[startIndex].Offset);
		if (target.Definition is null ||
			target.Signature.ReturnType.IsVoid)
		{
			return false;
		}

		EmitCall(caller, instructions[startIndex], pushResult: false);
		consumed = popIndex - startIndex + 1;
		return true;
	}

	private bool TryEmitValueStore(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (!TryGetArgumentValueExpression(
				caller,
				instructions,
				startIndex,
				out var value,
				out var valueInstructionCount))
		{
			return false;
		}

		var storeIndex = startIndex + valueInstructionCount;
		if (storeIndex >= instructions.Count ||
			branchTargets.Contains(instructions[storeIndex].Offset) ||
			!TryGetStoreDestination(instructions[storeIndex], out var destination))
		{
			return false;
		}

		for (var index = startIndex; index < storeIndex; index++)
		{
			if (branchTargets.Contains(instructions[index].Offset))
			{
				return false;
			}

			if (IsComparisonOp(instructions[index].OpCode))
			{
				// Comparison results have an explicit stack representation. Let
				// the normal instruction path handle stores so byte/long widening
				// follows the declared destination type.
				return false;
			}
		}

		if (!TryEmitArgumentValueToDestination(caller, value, _currentStackDepth, destination))
		{
			return false;
		}

		consumed = valueInstructionCount + 1;
		return true;
	}

	private bool TryEmitDirectLocalQuickUpdate(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 3 >= instructions.Count ||
			!TryGetLoadLocalIndex(instructions[startIndex], out var localIndex) ||
			!TryGetConstant(instructions[startIndex + 1], out var constant) ||
			!CanEmitQuickLocalUpdate(caller, localIndex, constant, instructions[startIndex + 2].OpCode) ||
			!TryGetStoreLocalIndex(instructions[startIndex + 3], out var storeLocalIndex) ||
			storeLocalIndex != localIndex)
		{
			return false;
		}

		for (var index = startIndex + 1; index <= startIndex + 3; index++)
		{
			if (branchTargets.Contains(instructions[index].Offset))
			{
				return false;
			}
		}

		EmitQuickLocalUpdate(caller, localIndex, constant, instructions[startIndex + 2].OpCode);
		consumed = 4;
		return true;
	}

	private bool TryEmitDirectNullableLocalAccessor(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 1 >= instructions.Count ||
			!TryGetLoadLocalAddressIndex(instructions[startIndex], out var localIndex) ||
			branchTargets.Contains(instructions[startIndex + 1].Offset))
		{
			return false;
		}

		var callOp = instructions[startIndex + 1].OpCode;
		if (callOp != OpCodes.Call && callOp != OpCodes.Callvirt)
		{
			return false;
		}

		ValidateLocal(caller, instructions[startIndex], localIndex);
		var localType = caller.Locals[localIndex];
		if (!localType.IsNullable ||
			!_module.IsSupportedNullableType(localType))
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[startIndex + 1].Operand!,
			caller,
			instructions[startIndex + 1].Offset);
		var importName = target.ImportName;
		if (importName?.StartsWith("intrinsic:nullable-has-value:", StringComparison.Ordinal) == true)
		{
			if (IsCompactNullableType(localType))
			{
				if (LocalRegister(localIndex) is { } register)
				{
					EmitCompactNullableHasValueFromRegister(register);
				}
				else
				{
					EmitCompactNullableHasValueFromFrame(LocalOffset(caller, localIndex));
				}
				consumed = 2;
				return true;
			}

			var baseDisplacement = LocalOffset(caller, localIndex);
			EmitPushFrameSlot(checked((short)(baseDisplacement + 4)));
			consumed = 2;
			return true;
		}

		if (importName?.StartsWith("intrinsic:nullable-get-value:", StringComparison.Ordinal) == true)
		{
			if (IsCompactNullableType(localType) &&
				LocalRegister(localIndex) is { } register)
			{
				EmitPushRegister(register);
			}
			else
			{
				EmitPushFrameSlot(LocalOffset(caller, localIndex));
			}
			consumed = 2;
			return true;
		}

		return false;
	}

	private bool TryEmitDirectTransparentScalarRawGetter(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 1 >= instructions.Count ||
			branchTargets.Contains(instructions[startIndex + 1].Offset))
		{
			return false;
		}

		var call = instructions[startIndex + 1];
		if (call.OpCode != OpCodes.Call &&
			call.OpCode != OpCodes.Callvirt)
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)call.Operand!,
			caller,
			call.Offset);
		if (!IsTransparentScalarRawGetter(target))
		{
			return false;
		}

		var instruction = instructions[startIndex];
		if (startIndex + 3 < instructions.Count &&
			(instructions[startIndex + 2].OpCode == OpCodes.Call ||
			 instructions[startIndex + 2].OpCode == OpCodes.Callvirt) &&
			!branchTargets.Contains(instructions[startIndex + 2].Offset) &&
			!branchTargets.Contains(instructions[startIndex + 3].Offset) &&
			TryGetStoreDestination(instructions[startIndex + 3], out var destination))
		{
			var wrapper = _module.ResolveMethodToken(
				(int)instructions[startIndex + 2].Operand!,
				caller,
				instructions[startIndex + 2].Offset);
			if (wrapper.ImportName == "intrinsic:cstring-from-pointer" &&
				TryEmitArgumentValueToDestination(
					caller,
					new ArgumentValue(instruction, IsTransparentScalarRawGetter: true),
					_currentStackDepth,
					destination))
			{
				consumed = 4;
				return true;
			}
		}

		if (TryGetLoadLocalAddressIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } localRegister)
			{
				EmitPushRegister(localRegister);
				consumed = 2;
				return true;
			}

			EmitPushFrameSlot(FrameDisplacement(
				LocalOffset(caller, localIndex),
				_currentStackDepth));
			consumed = 2;
			return true;
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (ArgumentRegister(argumentIndex) is { } argumentRegister)
			{
				EmitPushRegister(argumentRegister);
				consumed = 2;
				return true;
			}

			EmitPushFrameSlot(FrameDisplacement(
				ArgumentOffset(caller, argumentIndex),
				_currentStackDepth));
			consumed = 2;
			return true;
		}

		return false;
	}

	private bool TryEmitNullableGetValueLibraryBaseSet(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 2 >= instructions.Count ||
			!TryGetLoadLocalAddressIndex(instructions[startIndex], out var localIndex))
		{
			return false;
		}

		var getter = instructions[startIndex + 1];
		var setter = instructions[startIndex + 2];
		if ((getter.OpCode != OpCodes.Call && getter.OpCode != OpCodes.Callvirt) ||
			(setter.OpCode != OpCodes.Call && setter.OpCode != OpCodes.Callvirt) ||
			branchTargets.Contains(getter.Offset) ||
			branchTargets.Contains(setter.Offset))
		{
			return false;
		}

		ValidateLocal(caller, instructions[startIndex], localIndex);
		var localType = caller.Locals[localIndex];
		if (!localType.IsNullable ||
			!_module.IsSupportedNullableType(localType))
		{
			return false;
		}

		var getterTarget = _module.ResolveMethodToken(
			(int)getter.Operand!,
			caller,
			getter.Offset);
		if (getterTarget.ImportName?.StartsWith("intrinsic:nullable-get-value:", StringComparison.Ordinal) != true)
		{
			return false;
		}

		var setterTarget = _module.ResolveMethodToken(
			(int)setter.Operand!,
			caller,
			setter.Offset);
		if (setterTarget.ImportName?.StartsWith(
				"intrinsic:amiga-library-base-set:",
				StringComparison.Ordinal) != true)
		{
			return false;
		}

		var libraryTypeName = setterTarget.ImportName["intrinsic:amiga-library-base-set:".Length..];
		EnsureAmigaLibraryBaseSlot(caller, libraryTypeName);
		var slotSymbol = AmigaLibraryBaseSlotSymbol(libraryTypeName);
		if (LocalRegister(localIndex) is { } localRegister)
		{
			EmitStoreRegisterDirectToLabel(localRegister, slotSymbol);
		}
		else
		{
			EmitStoreFrameLongDirectToLabel(
				FrameDisplacement(LocalOffset(caller, localIndex), _currentStackDepth),
				slotSymbol);
		}
		_loadedPlatformBase = null;
		consumed = 3;
		return true;
	}

	private bool TryEmitZeroLibraryBaseSet(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (!TryGetZeroValueProducer(
				caller,
				instructions,
				startIndex,
				out var valueConsumed) ||
			valueConsumed == 0)
		{
			return false;
		}

		var setterIndex = startIndex + valueConsumed;
		if (setterIndex >= instructions.Count ||
			branchTargets.Contains(instructions[setterIndex].Offset) ||
			HasBranchTarget(
				branchTargets,
				instructions,
				startIndex,
				startIndex + 1,
				setterIndex - 1))
		{
			return false;
		}

		var setter = instructions[setterIndex];
		if (setter.OpCode != OpCodes.Call &&
			setter.OpCode != OpCodes.Callvirt)
		{
			return false;
		}

		var setterTarget = _module.ResolveMethodToken(
			(int)setter.Operand!,
			caller,
			setter.Offset);
		if (setterTarget.ImportName?.StartsWith(
				"intrinsic:amiga-library-base-set:",
				StringComparison.Ordinal) != true)
		{
			return false;
		}

		var libraryTypeName = setterTarget.ImportName["intrinsic:amiga-library-base-set:".Length..];
		EnsureAmigaLibraryBaseSlot(caller, libraryTypeName);
		EmitClearLabel(AmigaLibraryBaseSlotSymbol(libraryTypeName));
		_loadedPlatformBase = null;
		consumed = valueConsumed + 1;
		return true;
	}

	private bool TryGetZeroValueProducer(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		out int consumed)
	{
		consumed = 0;
		if (TryGetArgumentValueExpression(
				caller,
				instructions,
				startIndex,
				out var value,
				out consumed) &&
			value.Instruction is { } valueInstruction &&
			IsZeroArgumentValue(valueInstruction))
		{
			return true;
		}

		var instruction = instructions[startIndex];
		if (instruction.OpCode != OpCodes.Call &&
			instruction.OpCode != OpCodes.Callvirt)
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset);
		if (target.ImportName != "intrinsic:aptr-null")
		{
			return false;
		}

		consumed = 1;
		return true;
	}

	private static bool IsZeroArgumentValue(CilInstruction instruction) =>
		instruction.OpCode == OpCodes.Ldnull ||
		TryGetConstant(instruction, out var constant) &&
		constant == 0;

	private bool TryEmitDirectValueReturn(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (caller.Signature.ReturnType.IsVoid ||
			Is64BitScalar(caller.Signature.ReturnType) ||
			IsInternalAddressReturn(caller.Signature.ReturnType) ||
			!TryGetArgumentValueExpression(
				caller,
				instructions,
				startIndex,
				out var value,
				out var valueInstructionCount))
		{
			return false;
		}

		var returnIndex = startIndex + valueInstructionCount;
		if (returnIndex >= instructions.Count ||
			instructions[returnIndex].OpCode != OpCodes.Ret ||
			branchTargets.Contains(instructions[returnIndex].Offset))
		{
			return false;
		}

		for (var index = startIndex + 1; index < returnIndex; index++)
		{
			if (branchTargets.Contains(instructions[index].Offset))
			{
				return false;
			}
		}

		// The direct-return path loads values with long-width frame helpers. Keep
		// narrow locals on the typed stack path so their declared width is widened
		// correctly before returning through a 32-bit managed signature.
		if (caller.Instructions.Any(static item =>
			item.OpCode == OpCodes.Conv_I1 || item.OpCode == OpCodes.Conv_U1 ||
			item.OpCode == OpCodes.Conv_I2 || item.OpCode == OpCodes.Conv_U2))
		{
			return false;
		}

		EmitArgumentValueToRegister(caller, value, _currentStackDepth, M68kRegister.D0);
		EmitFrameTeardown(caller);
		_assembler.EmitWord(0x4E75); // RTS
		consumed = valueInstructionCount + 1;
		return true;
	}

	private bool TryEmitDirectMaskedAddReturn(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex != 0 ||
			instructions.Count != 6 ||
			!TryGetArgumentValueExpression(
				caller,
				instructions,
				startIndex,
				out var value,
				out var valueConsumed) ||
			value.Instruction is null ||
			!TryGetConstant(instructions[valueConsumed], out var addend) ||
			instructions[valueConsumed + 1].OpCode != OpCodes.Add ||
			!TryGetConstant(instructions[valueConsumed + 2], out var mask) ||
			instructions[valueConsumed + 3].OpCode != OpCodes.And ||
			instructions[valueConsumed + 4].OpCode != OpCodes.Ret ||
			caller.Signature.ReturnType.IsVoid ||
			Is64BitScalar(caller.Signature.ReturnType))
		{
			return false;
		}

		for (var index = 1; index < instructions.Count; index++)
		{
			if (branchTargets.Contains(instructions[index].Offset))
			{
				return false;
			}
		}

		if (addend is < 1 or > 8)
		{
			return false;
		}

		EmitArgumentValueToRegister(caller, value, _currentStackDepth, M68kRegister.D0);
		_assembler.EmitWord((ushort)(0x5080 | (addend << 9)));
		EmitAndImmediateToDataRegister(M68kRegister.D0, mask);
		EmitFrameTeardown(caller);
		_assembler.EmitWord(0x4E75); // RTS
		consumed = instructions.Count;
		return true;
	}

	private void EmitAndImmediateToDataRegister(M68kRegister register, int mask)
	{
		var dataRegister = (int)register;
		var unsignedMask = unchecked((uint)mask);
		_assembler.EmitWord((ushort)(0x0280 | dataRegister)); // ANDI.L
		_assembler.EmitLong(unsignedMask);
	}

	private static bool TryGetStoreDestination(
		CilInstruction instruction,
		out StoreDestination destination)
	{
		if (TryGetStoreLocalIndex(instruction, out var localIndex))
		{
			destination = new StoreDestination(IsLocal: true, localIndex);
			return true;
		}

		if (instruction.OpCode == OpCodes.Starg || instruction.OpCode == OpCodes.Starg_S)
		{
			destination = new StoreDestination(IsLocal: false, Convert.ToInt32(instruction.Operand));
			return true;
		}

		destination = default;
		return false;
	}

	private static bool TryGetAddressStoreDestination(
		CilInstruction instruction,
		out StoreDestination destination)
	{
		if (TryGetLoadLocalAddressIndex(instruction, out var localIndex))
		{
			destination = new StoreDestination(IsLocal: true, localIndex);
			return true;
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var argumentIndex))
		{
			destination = new StoreDestination(IsLocal: false, argumentIndex);
			return true;
		}

		destination = default;
		return false;
	}

	private static bool TryGetNextStoreIndex(
		IReadOnlyList<CilInstruction> instructions,
		int producerIndex,
		IReadOnlySet<int> branchTargets,
		out int storeIndex,
		out StoreDestination destination)
	{
		storeIndex = producerIndex + 1;
		while (storeIndex < instructions.Count &&
			instructions[storeIndex].OpCode == OpCodes.Nop &&
			!branchTargets.Contains(instructions[storeIndex].Offset))
		{
			storeIndex++;
		}

		if (storeIndex < instructions.Count &&
			!branchTargets.Contains(instructions[storeIndex].Offset) &&
			TryGetStoreDestination(instructions[storeIndex], out destination))
		{
			return true;
		}

		destination = default;
		return false;
	}

	private bool TryGetNextSimpleWrapperConstructorIndex(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int producerIndex,
		IReadOnlySet<int> branchTargets,
		out int constructorIndex,
		out CilMethod constructor)
	{
		constructorIndex = producerIndex + 1;
		while (constructorIndex < instructions.Count &&
			instructions[constructorIndex].OpCode == OpCodes.Nop &&
			!branchTargets.Contains(instructions[constructorIndex].Offset))
		{
			constructorIndex++;
		}

		constructor = null!;
		if (constructorIndex >= instructions.Count ||
			branchTargets.Contains(instructions[constructorIndex].Offset))
		{
			return false;
		}

		var op = instructions[constructorIndex].OpCode;
		if (op != OpCodes.Call && op != OpCodes.Callvirt)
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[constructorIndex].Operand!,
			caller,
			instructions[constructorIndex].Offset).Definition;
		if (target is null ||
			target.Name != ".ctor" ||
			!target.Signature.Header.IsInstance ||
			!target.Signature.ReturnType.IsVoid ||
			target.Signature.ParameterTypes.Length != 1 ||
			target.Signature.ParameterTypes[0].DisplayName != "uint" ||
			!IsSimpleWrapperConstructorBody(target))
		{
			return false;
		}

		constructor = target;
		return true;
	}

	private bool IsSimpleWrapperConstructorBody(CilMethod constructor)
	{
		var body = constructor.Instructions
			.Where(static instruction => instruction.OpCode != OpCodes.Nop)
			.ToArray();
		if (body.Length != 4 ||
			!TryGetArgumentIndex(body[0], out var thisIndex) ||
			thisIndex != 0 ||
			!TryGetArgumentIndex(body[1], out var valueIndex) ||
			valueIndex != 1 ||
			body[2].OpCode != OpCodes.Stfld ||
			body[3].OpCode != OpCodes.Ret)
		{
			return false;
		}

		var field = _module.ResolveFieldToken(
			(int)body[2].Operand!,
			constructor,
			body[2].Offset);
		return !field.IsStatic &&
			field.Type.DisplayName == "uint";
	}

	private bool TryEmitArgumentValueToDestination(
		CilMethod caller,
		ArgumentValue value,
		int stackDepth,
		StoreDestination destination)
	{
		if (destination.IsLocal)
		{
			ValidateLocal(caller, value.Instruction ?? caller.Instructions[0], destination.Index);
			if (LocalRegister(destination.Index) is { } localRegister)
			{
				EmitArgumentValueToRegister(caller, value, stackDepth, localRegister);
				return true;
			}

			return TryEmitArgumentValueToFrame(
				caller,
				value,
				stackDepth,
				FrameDisplacement(
					LocalOffset(caller, destination.Index),
					stackDepth));
		}

		ValidateArgument(caller, value.Instruction ?? caller.Instructions[0], destination.Index);
		return TryEmitArgumentValueToFrame(
			caller,
			value,
			stackDepth,
			FrameDisplacement(
				ArgumentOffset(caller, destination.Index),
				stackDepth));
	}

	private void EmitStoreReturnToDestination(
		CilMethod caller,
		CilMethod definition,
		StoreDestination destination,
		int stackDepth)
	{
		if (definition.Signature.ReturnType.IsNullable)
		{
			EmitStoreNullableReturnToDestination(caller, destination, stackDepth);
			return;
		}

		if (Is64BitScalar(definition.Signature.ReturnType))
		{
			EmitStore64BitReturnToDestination(caller, definition, destination, stackDepth);
			return;
		}

		var sourceRegister = !definition.IsImport && IsInternalAddressReturn(definition.Signature.ReturnType)
			? M68kRegister.A0
			: M68kRegister.D0;
		if (destination.IsLocal)
		{
			if (LocalRegister(destination.Index) is { } localRegister)
			{
				EmitMoveRegisterToRegister(sourceRegister, localRegister);
				return;
			}

			EmitStoreRegisterToFrame(
				sourceRegister,
				FrameDisplacement(
					LocalOffset(caller, destination.Index),
					stackDepth));
			return;
		}

		EmitStoreRegisterToFrame(
			sourceRegister,
			FrameDisplacement(
				ArgumentOffset(caller, destination.Index),
				stackDepth));
	}

	private void EmitStore64BitReturnToDestination(
		CilMethod caller,
		CilMethod definition,
		StoreDestination destination,
		int stackDepth)
	{
		var highRegister = definition.ExternalCall?.Abi.ReturnRegister ??
			definition.ImportAbi?.ReturnRegister ??
			M68kRegister.D0;
		var lowRegister = NextDataRegister(highRegister, definition.DisplayName);
		if (destination.IsLocal)
		{
			var displacement = FrameDisplacement(
				LocalOffset(caller, destination.Index),
				stackDepth);
			EmitStoreRegisterToFrame(highRegister, displacement);
			EmitStoreRegisterToFrame(lowRegister, checked((short)(displacement + 4)));
			return;
		}

		var argumentDisplacement = FrameDisplacement(
			ArgumentOffset(caller, destination.Index),
			stackDepth);
		EmitStoreRegisterToFrame(highRegister, argumentDisplacement);
		EmitStoreRegisterToFrame(lowRegister, checked((short)(argumentDisplacement + 4)));
	}

	private void EmitStoreNullableReturnToDestination(
		CilMethod caller,
		StoreDestination destination,
		int stackDepth)
	{
		if (!destination.IsLocal)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Storing nullable call results to arguments is not implemented.",
				caller.DisplayName);
		}

		var destinationType = caller.Locals[destination.Index];
		if (!destinationType.IsNullable ||
			!_module.IsSupportedNullableType(destinationType))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Nullable call results must be stored directly to a supported nullable local.",
				caller.DisplayName);
		}

		if (IsCompactNullableType(destinationType) &&
			LocalRegister(destination.Index) is { } register)
		{
			EmitMoveRegisterToRegister(M68kRegister.D0, register);
			return;
		}

		var displacement = FrameDisplacement(
			LocalOffset(caller, destination.Index),
			stackDepth);
		if (IsCompactNullableType(destinationType))
		{
			EmitStoreRegisterToFrame(M68kRegister.D0, displacement);
			return;
		}

		EmitImmediateToRegister(M68kRegister.D1, 0);
		EmitStoreRegisterToFrame(M68kRegister.D0, displacement);
		_assembler.EmitWord(0x56C1); // SNE D1
		_assembler.EmitWord(0x4401); // NEG.B D1, FF -> 1
		EmitStoreRegisterToFrame(M68kRegister.D1, checked((short)(displacement + 4)));
	}

	private static bool TryGetDirectReturnIndex(
		IReadOnlyList<CilInstruction> instructions,
		int callIndex,
		IReadOnlySet<int> branchTargets,
		out int returnIndex)
	{
		returnIndex = callIndex + 1;
		while (returnIndex < instructions.Count &&
			instructions[returnIndex].OpCode == OpCodes.Nop &&
			!branchTargets.Contains(instructions[returnIndex].Offset))
		{
			returnIndex++;
		}

		return returnIndex < instructions.Count &&
			instructions[returnIndex].OpCode == OpCodes.Ret &&
			!branchTargets.Contains(instructions[returnIndex].Offset);
	}

	private static bool TryGetDiscardedResultIndex(
		IReadOnlyList<CilInstruction> instructions,
		int callIndex,
		IReadOnlySet<int> branchTargets,
		out int popIndex)
	{
		popIndex = callIndex + 1;
		while (popIndex < instructions.Count &&
			instructions[popIndex].OpCode == OpCodes.Nop &&
			!branchTargets.Contains(instructions[popIndex].Offset))
		{
			popIndex++;
		}

		return popIndex < instructions.Count &&
			instructions[popIndex].OpCode == OpCodes.Pop &&
			!branchTargets.Contains(instructions[popIndex].Offset);
	}

	private bool IsStackVarargsNewArray(CilMethod caller, CilInstruction instruction)
	{
		var elementType = _module.ResolveTypeToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset);
		return elementType.DisplayName == "uint" ||
			elementType.DisplayName == "Amiga.AmigaVarArg" ||
			_module.IsTransparentScalarType(elementType);
	}

	private static bool HasBranchTarget(
		IReadOnlySet<int> branchTargets,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		int firstIndex,
		int lastIndex)
	{
		for (var index = firstIndex; index <= lastIndex; index++)
		{
			if (index != startIndex &&
				branchTargets.Contains(instructions[index].Offset))
			{
				return true;
			}
		}

		return false;
	}



	private bool TryEmitDirectExternalCall(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		var constants = new List<int>();
		var callIndex = startIndex;
		while (callIndex < instructions.Count &&
			TryGetConstant(instructions[callIndex], out var constant))
		{
			if (callIndex != startIndex &&
				branchTargets.Contains(instructions[callIndex].Offset))
			{
				return false;
			}
			constants.Add(constant);
			callIndex++;
		}
		if (constants.Count == 0 ||
			callIndex >= instructions.Count ||
			(instructions[callIndex].OpCode != OpCodes.Call &&
			 instructions[callIndex].OpCode != OpCodes.Callvirt) ||
			branchTargets.Contains(instructions[callIndex].Offset))
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[callIndex].Operand!,
			caller,
			instructions[callIndex].Offset);
		if (target.Definition?.ExternalCall is not { } externalCall ||
			constants.Count != externalCall.Abi.ParameterRegisters.Count ||
			target.Signature.ReturnType.IsNullable ||
			target.Signature.ReturnType.Size == 8 ||
			target.Signature.ParameterTypes.Any(static type => type.Size == 8))
		{
			return false;
		}

		EmitEnsurePlatformBase(externalCall.Convention, target.Definition);
		var cacheRegister = externalCall.Convention.CacheRegister;
		var preservePlatformCache = cacheRegister is not null &&
			(externalCall.Abi.ReturnRegister == cacheRegister ||
			 externalCall.Abi.ParameterRegisters.Contains(cacheRegister.Value));
		if (preservePlatformCache)
		{
			EmitPushRegister(cacheRegister!.Value);
		}
		for (var index = 0; index < constants.Count; index++)
		{
			EmitImmediateToRegister(
				externalCall.Abi.ParameterRegisters[index],
				constants[index]);
		}
		EmitBaseRelativeJsr(
			externalCall.Convention.BaseRegister,
			externalCall.Convention.Displacement);
		if (!target.Signature.ReturnType.IsVoid)
		{
			EmitMoveRegisterToD0(externalCall.Abi.ReturnRegister);
		}
		if (preservePlatformCache)
		{
			EmitPopRegister(cacheRegister!.Value);
		}

		var returnsDirectly = TryGetDirectReturnIndex(
			instructions,
			callIndex,
			branchTargets,
			out var returnIndex) &&
			!Is64BitScalar(target.Signature.ReturnType);
		if (returnsDirectly)
		{
			EmitFrameTeardown(caller);
			_assembler.EmitWord(0x4E75); // RTS
			consumed = returnIndex - startIndex + 1;
			return true;
		}
		if (!target.Signature.ReturnType.IsVoid)
		{
			EmitPushD0();
		}
		consumed = callIndex - startIndex + 1;
		return true;
	}

	private bool TryEmitQuickBinary(int constant, OpCode operation)
	{
		if (operation != OpCodes.Add && operation != OpCodes.Sub)
		{
			return false;
		}

		var subtract = operation == OpCodes.Sub;
		if (constant < 0)
		{
			if (constant < -8)
			{
				return false;
			}
			constant = -constant;
			subtract = !subtract;
		}
		else if (constant > 8)
		{
			return false;
		}

		if (constant == 0)
		{
			return true;
		}

		var encodedCount = constant == 8 ? 0 : constant;
		var opcode = 0x5080 | (encodedCount << 9) | 0x0017;
		if (subtract)
		{
			opcode |= 0x0100;
		}
		_assembler.EmitWord((ushort)opcode);
		return true;
	}

	private bool CanEmitQuickLocalUpdate(
		CilMethod caller,
		int localIndex,
		int constant,
		OpCode operation) =>
		TryNormalizeQuickUpdate(
			constant,
			operation,
			out _,
			out _) &&
		(uint)localIndex < (uint)caller.Locals.Length &&
		SlotLongs(caller.Locals[localIndex]) == 1 &&
		(LocalRegister(localIndex) is not { } register || register <= M68kRegister.D7);

	private void EmitQuickLocalUpdate(
		CilMethod caller,
		int localIndex,
		int constant,
		OpCode operation)
	{
		ValidateLocal(caller, caller.Instructions[0], localIndex);
		if (!TryNormalizeQuickUpdate(constant, operation, out var quickCount, out var subtract))
		{
			throw new InvalidOperationException("Invalid quick local update.");
		}

		if (quickCount == 0)
		{
			return;
		}

		if (LocalRegister(localIndex) is { } register)
		{
			EmitQuickRegisterUpdate(register, quickCount, subtract);
			return;
		}

		EmitQuickFrameUpdate(LocalOffset(caller, localIndex), quickCount, subtract);
	}

	private static bool TryNormalizeQuickUpdate(
		int constant,
		OpCode operation,
		out int quickCount,
		out bool subtract)
	{
		quickCount = 0;
		subtract = operation == OpCodes.Sub;
		if (operation != OpCodes.Add && operation != OpCodes.Sub)
		{
			return false;
		}

		if (constant < 0)
		{
			if (constant < -8)
			{
				return false;
			}
			quickCount = -constant;
			subtract = !subtract;
			return true;
		}

		if (constant > 8)
		{
			return false;
		}

		quickCount = constant;
		return true;
	}

	private void EmitQuickRegisterUpdate(M68kRegister register, int quickCount, bool subtract)
	{
		if (quickCount == 0)
		{
			return;
		}

		var encodedCount = quickCount == 8 ? 0 : quickCount;
		var opcode = 0x5080 | (encodedCount << 9) | (int)register;
		if (subtract)
		{
			opcode |= 0x0100;
		}
		_assembler.EmitWord((ushort)opcode);
	}

	private void EmitQuickFrameUpdate(short displacement, int quickCount, bool subtract)
	{
		if (quickCount == 0)
		{
			return;
		}

		var encodedCount = quickCount == 8 ? 0 : quickCount;
		var opcode = 0x50AF | (encodedCount << 9);
		if (subtract)
		{
			opcode |= 0x0100;
		}
		_assembler.EmitWord((ushort)opcode);
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private static HashSet<int> GetBranchTargets(IReadOnlyList<CilInstruction> instructions)
	{
		var result = new HashSet<int>();
		foreach (var instruction in instructions)
		{
			if (instruction.OpCode == OpCodes.Switch)
			{
				result.UnionWith((int[])instruction.Operand!);
			}
			else if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch &&
				instruction.Operand is int target)
			{
				result.Add(target);
			}
		}
		return result;
	}

	private void EmitInstruction(CilMethod method, CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Nop)
		{
			return;
		}

		if (TryGetConstant(instruction, out var constant))
		{
			EmitPushConstant(constant);
			return;
		}

		if (instruction.OpCode == OpCodes.Ldc_I8)
		{
			EmitPushLongConstant((long)instruction.Operand!);
			return;
		}

		if (op == OpCodes.Ldnull)
		{
			EmitPushConstant(0);
			return;
		}

		if (op == OpCodes.Ldstr)
		{
			if (IsNextCStringFromLiteralCall(method, instruction))
			{
				return;
			}

			if (IsNextExportAddressCall(method, instruction))
			{
				return;
			}

			var token = (int)instruction.Operand!;
			var identity = new CilUserStringIdentity(method.ModuleName, token);
			_stringLiterals.TryAdd(identity, _module.GetUserString(token, method, instruction.Offset));
			_assembler.EmitWord(0x2F3C); // MOVE.L #string,-(A7)
			_assembler.EmitAddress(StringLabel(identity));
			return;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			var argumentKind = StackKindForType(TypeForArgument(method, argumentIndex));
			if (ArgumentRegister(argumentIndex) is { } register)
			{
				EmitPushStackValue(register, argumentKind);
				return;
			}

			var displacement = FrameDisplacement(
				ArgumentOffset(method, argumentIndex),
				_currentStackDepth);
			if (CilStackValueLayout.IsByte(argumentKind))
			{
				EmitPushByteFrameSlot(displacement);
			}
			else
			{
				EmitPushFrameValue(displacement, SlotLongs(TypeForArgument(method, argumentIndex)));
			}
			return;
		}

		if (TryGetLoadLocalIndex(instruction, out var loadLocal))
		{
			ValidateLocal(method, instruction, loadLocal);
			var localKind = StackKindForType(method.Locals[loadLocal]);
			if (LocalRegister(loadLocal) is { } register)
			{
				EmitPushStackValue(register, localKind);
				return;
			}

			var displacement = FrameDisplacement(
				LocalOffset(method, loadLocal),
				_currentStackDepth);
			if (CilStackValueLayout.IsByte(localKind))
			{
				EmitPushByteFrameSlot(displacement);
			}
			else
			{
				EmitPushFrameValue(displacement, SlotLongs(method.Locals[loadLocal]));
			}
			return;
		}

		if (TryGetLoadLocalAddressIndex(instruction, out var loadLocalAddress))
		{
			ValidateLocal(method, instruction, loadLocalAddress);
			if (_module.IsTransparentScalarType(method.Locals[loadLocalAddress]) ||
				_module.IsSupportedNullableType(method.Locals[loadLocalAddress]) ||
				_module.IsSupportedStructType(method.Locals[loadLocalAddress]))
			{
				if (_module.RequiresLongAlignedStackAddress(method.Locals[loadLocalAddress]))
				{
					EmitPushAlignedLocalAddress(
						FrameDisplacement(LocalOffset(method, loadLocalAddress), _currentStackDepth));
				}
				else
				{
					EmitPushFrameAddress(FrameDisplacement(
						LocalOffset(method, loadLocalAddress),
						_currentStackDepth));
				}
				return;
			}
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var loadArgumentAddress))
		{
			ValidateArgument(method, instruction, loadArgumentAddress);
			if (IsTransparentScalarArgument(method, loadArgumentAddress) ||
				IsSupportedNullableArgument(method, loadArgumentAddress))
			{
				EmitPushFrameAddress(FrameDisplacement(
					ArgumentOffset(method, loadArgumentAddress),
					_currentStackDepth));
				return;
			}
		}

		if (TryGetStoreLocalIndex(instruction, out var storeLocal))
		{
			ValidateLocal(method, instruction, storeLocal);
			var localKind = StackKindForType(method.Locals[storeLocal]);
			var storedKind = CurrentStackKindOrLong();
			if (LocalRegister(storeLocal) is { } register)
			{
				EmitPopStackValue(register, CilStackValueLayout.IsByte(storedKind) ? localKind : storedKind);
				return;
			}

			var displacement = FrameDisplacement(
				LocalOffset(method, storeLocal),
				_currentStackDepth - (CilStackValueLayout.IsByte(storedKind) ? 1 : SlotLongs(method.Locals[storeLocal])));
			if (CilStackValueLayout.IsByte(localKind))
			{
				EmitPopStackValue(M68kRegister.D0, storedKind, widen: false);
				EmitStoreByteToFrame(M68kRegister.D0, displacement);
			}
			else if (CilStackValueLayout.IsByte(storedKind))
			{
				EmitPopStackValue(M68kRegister.D0, storedKind, widen: true);
				EmitStoreRegisterToFrame(M68kRegister.D0, displacement);
			}
			else
			{
				EmitPopFrameValue(displacement, SlotLongs(method.Locals[storeLocal]));
			}
			return;
		}

		if (op == OpCodes.Starg || op == OpCodes.Starg_S)
		{
			var index = Convert.ToInt32(instruction.Operand);
			ValidateArgument(method, instruction, index);
			var argumentType = TypeForArgument(method, index);
			var argumentKind = StackKindForType(argumentType);
			var storedKind = CurrentStackKindOrLong();
			var displacement = FrameDisplacement(
				ArgumentOffset(method, index),
				_currentStackDepth - (CilStackValueLayout.IsByte(storedKind)
					? 1
					: SlotLongs(argumentType)));
			if (CilStackValueLayout.IsByte(argumentKind))
			{
				EmitPopStackValue(M68kRegister.D0, storedKind, widen: false);
				EmitStoreByteToFrame(M68kRegister.D0, displacement);
				return;
			}

			if (CilStackValueLayout.IsByte(storedKind))
			{
				EmitPopStackValue(M68kRegister.D0, storedKind, widen: true);
				EmitStoreRegisterToFrame(M68kRegister.D0, displacement);
				return;
			}

			var slotLongs = SlotLongs(TypeForArgument(method, index));
			EmitPopFrameValue(
				displacement,
				slotLongs);
			return;
		}

		if (op == OpCodes.Dup)
		{
			var kind = CurrentStackKind();
			if (CilStackValueLayout.IsByte(kind))
			{
				_assembler.EmitWord(0x1017); // MOVE.B (A7),D0
				EmitPushByteRegister(M68kRegister.D0);
			}
			else
			{
				_assembler.EmitWord(0x2017); // MOVE.L (A7),D0
				_assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
			}
			return;
		}

		if (op == OpCodes.Pop)
		{
			EmitReleaseStackBytes(CilStackValueLayout.SlotBytes(CurrentStackKindOrLong()));
			return;
		}

		if (op == OpCodes.Add || op == OpCodes.Add_Ovf || op == OpCodes.Sub ||
			op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor)
		{
			EmitBinary(op, CurrentArithmeticResultKind(2));
			return;
		}

		if (op == OpCodes.Mul)
		{
			var resultKind = CurrentArithmeticResultKind(2);
			EmitPopBinaryOperands(widen: true);
			EmitMultiply(resultKind);
			EmitNormalizeArithmeticResult(resultKind);
			EmitPushArithmeticResult(resultKind);
			return;
		}

		if (op == OpCodes.Div || op == OpCodes.Div_Un ||
			op == OpCodes.Rem || op == OpCodes.Rem_Un)
		{
			var resultKind = CurrentArithmeticResultKind(2);
			EmitPopBinaryOperands(widen: true);
			EmitDivide(
				signed: op == OpCodes.Div || op == OpCodes.Rem,
				remainder: op == OpCodes.Rem || op == OpCodes.Rem_Un);
			EmitNormalizeArithmeticResult(resultKind);
			EmitPushArithmeticResult(resultKind);
			return;
		}

		if (op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un)
		{
			var resultKind = CurrentArithmeticResultKind(2);
			EmitPopBinaryOperands(widen: true);
			EmitShift(op, resultKind);
			EmitNormalizeArithmeticResult(resultKind);
			EmitPushArithmeticResult(resultKind);
			return;
		}

		if (op == OpCodes.Neg || op == OpCodes.Not)
		{
			var resultKind = CurrentArithmeticResultKind(1);
			EmitPopStackValue(M68kRegister.D0, CurrentStackKindOrLong(), widen: true);
			EmitUnary(op, resultKind);
			EmitNormalizeArithmeticResult(resultKind);
			EmitPushArithmeticResult(resultKind);
			return;
		}

		if (op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un ||
			op == OpCodes.Clt || op == OpCodes.Clt_Un)
		{
			EmitComparison(op);
			return;
		}

		if (IsUnconditionalBranch(op))
		{
			if (instruction.Operand is not int target)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"Branch instruction is missing its target.",
					method.DisplayName,
					instruction.Offset);
			}

			if (op == OpCodes.Leave || op == OpCodes.Leave_S)
			{
				if (TryEmitNormalLeave(method, instruction.Offset, target))
				{
					return;
				}
			}

			if (target != instruction.NextOffset)
			{
				_assembler.EmitBranch(M68kCondition.True, ControlFlowTargetLabel(method, target));
			}
			return;
		}

		if (op == OpCodes.Throw)
		{
			EmitPopRegister(M68kRegister.A0);
			EmitExceptionRaise(reason: 0, hasException: true);
			return;
		}

		if (op == OpCodes.Rethrow)
		{
			EmitLoadRuntimeFrameRegister(
				M68kRegister.A0,
				RuntimeFrameActiveExceptionOffset);
			EmitImmediateToRegister(M68kRegister.D0, 0);
			_assembler.EmitJmp(RuntimeExceptionRaiseLabel, external: false);
			return;
		}

		if (op == OpCodes.Endfinally)
		{
			_assembler.EmitJmp(RuntimeExceptionEndFinallyLabel, external: false);
			return;
		}

		if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S ||
			op == OpCodes.Brfalse || op == OpCodes.Brfalse_S)
		{
			var kind = CurrentStackKind();
			if (CilStackValueLayout.IsByte(kind))
			{
				EmitPopByteRegister(M68kRegister.D0, kind, widen: false);
				_assembler.EmitWord(0x4A00); // TST.B D0
			}
			else if (CilStackValueLayout.IsWord(kind))
			{
				EmitPopD0();
				_assembler.EmitWord(0x4A40); // TST.W D0
			}
			else
			{
				EmitPopD0();
				_assembler.EmitWord(0x4A80); // TST.L D0
			}
			_assembler.EmitBranch(
				op == OpCodes.Brtrue || op == OpCodes.Brtrue_S
					? M68kCondition.NotEqual
					: M68kCondition.Equal,
				ControlFlowTargetLabel(method, (int)instruction.Operand!));
			return;
		}

		if (TryGetRelationalBranch(op, out var branchCondition))
		{
			EmitPopBinaryOperands();
			_assembler.EmitWord(0xB081); // CMP.L D1,D0
			_assembler.EmitBranch(branchCondition, ControlFlowTargetLabel(method, (int)instruction.Operand!));
			return;
		}

		if (op == OpCodes.Switch)
		{
			EmitSwitch(method, instruction);
			return;
		}

		if (op == OpCodes.Call || op == OpCodes.Callvirt)
		{
			EmitCall(method, instruction);
			return;
		}

		if (op == OpCodes.Initobj)
		{
			EmitInitObj(method, instruction);
			return;
		}

		if (op == OpCodes.Newobj)
		{
			EmitNewObject(method, instruction);
			return;
		}

		if (op == OpCodes.Newarr)
		{
			EmitNewArray(method, instruction);
			return;
		}

		if (op == OpCodes.Ldlen)
		{
			EmitPopD0();
			EmitRequireNonNull();
			_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
			_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0
			_assembler.EmitWord(0x0008);
			EmitPushD0();
			return;
		}

		if (IsArrayAccess(op))
		{
			EmitArrayAccess(method, instruction);
			return;
		}

		if (IsIndirectLoad(op))
		{
			EmitIndirectLoad(op);
			return;
		}

		if (IsIndirectStore(op))
		{
			if (op == OpCodes.Stobj)
			{
				var type = _module.ResolveTypeToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset);
				EmitIndirectStore(type.Size);
			}
			else
			{
				EmitIndirectStore(op);
			}
			return;
		}

		if (op == OpCodes.Ldfld || op == OpCodes.Ldflda ||
			op == OpCodes.Stfld || op == OpCodes.Ldsfld ||
			op == OpCodes.Ldsflda || op == OpCodes.Stsfld)
		{
			EmitFieldAccess(method, instruction);
			return;
		}

		if (op == OpCodes.Ret)
		{
			if (!method.Signature.ReturnType.IsVoid)
			{
				if (Is64BitScalar(method.Signature.ReturnType))
				{
					EmitPopRegister(M68kRegister.D1);
					EmitPopRegister(M68kRegister.D0);
				}
				else if (IsInternalAddressReturn(method.Signature.ReturnType))
				{
					_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0
				}
				else if (method.Signature.ReturnType.Size == 1)
				{
					EmitPopByteRegister(
						M68kRegister.D0,
						StackKindForType(method.Signature.ReturnType));
				}
				else if (CilStackValueLayout.IsByte(CurrentStackKindOrLong()))
				{
					// A byte-producing CIL operation returned through a 32-bit
					// managed signature must be widened before RTS.
					EmitPopStackValue(M68kRegister.D0, CurrentStackKind());
				}
				else
				{
					EmitPopD0();
				}
			}

			EmitFrameTeardown(method);
			_assembler.EmitWord(0x4E75); // RTS
			return;
		}

		if (TryEmitConversion(op))
		{
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"CIL opcode '{op.Name}' is not implemented.",
			method.DisplayName,
			instruction.Offset);
	}

	private void EmitBinary(OpCode op, CilStackValueKind resultKind)
	{
		var width = CilStackValueLayout.ArithmeticWidth(resultKind);
		EmitPopBinaryOperands(widen: width == 4);
		ushort opcode = op.Value switch
		{
			var value when value == OpCodes.Add.Value || value == OpCodes.Add_Ovf.Value =>
				BinaryOpcode(0xD001, width),
			var value when value == OpCodes.Sub.Value => BinaryOpcode(0x9001, width),
			var value when value == OpCodes.And.Value => BinaryOpcode(0xC001, width),
			var value when value == OpCodes.Or.Value => BinaryOpcode(0x8001, width),
			var value when value == OpCodes.Xor.Value => BinaryOpcode(0xB300, width),
			_ => throw new InvalidOperationException()
		};
		_assembler.EmitWord(opcode);
		if (op == OpCodes.Add_Ovf)
		{
			var noOverflow = UniqueLabel("checked-add-no-overflow");
			_assembler.EmitBranch(M68kCondition.OverflowClear, noOverflow);
			EmitExceptionRaise(reason: 4, hasException: false);
			_assembler.Mark(noOverflow);
		}
		EmitNormalizeArithmeticResult(resultKind);
		EmitPushArithmeticResult(resultKind);
	}

	private static ushort BinaryOpcode(ushort byteOpcode, int width) =>
		width switch
		{
			1 => byteOpcode,
			2 => (ushort)(byteOpcode + 0x40),
			4 => (ushort)(byteOpcode + 0x80),
			_ => throw new ArgumentOutOfRangeException(nameof(width))
		};

	private void EmitMultiply(CilStackValueKind resultKind)
	{
		if (CilStackValueLayout.IsSmall(resultKind))
		{
			_assembler.EmitWord(
				CilStackValueLayout.IsSignedWord(resultKind) ||
				CilStackValueLayout.IsSignedByte(resultKind)
					? (ushort)0xC1C1 // MULS.W D1,D0
					: (ushort)0xC0C1); // MULU.W D1,D0
			return;
		}

		if (_request.Cpu != M68kCpuTarget.M68000)
		{
			_assembler.EmitWord(0x4C01); // MULS.L D1,D0
			_assembler.EmitWord(0x0800);
			return;
		}

		var loop = UniqueLabel("mul_loop");
		var skip = UniqueLabel("mul_skip");
		_assembler.EmitWord(0x7400); // MOVEQ #0,D2
		_assembler.EmitWord(0x761F); // MOVEQ #31,D3
		_assembler.Mark(loop);
		_assembler.EmitWord(0xE289); // LSR.L #1,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, skip);
		_assembler.EmitWord(0xD480); // ADD.L D0,D2
		_assembler.Mark(skip);
		_assembler.EmitWord(0xD080); // ADD.L D0,D0
		_assembler.EmitDbra(3, loop);
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
	}

	private void EmitDivide(bool signed, bool remainder)
	{
		var divisorReady = UniqueLabel("div_nonzero");
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.NotEqual, divisorReady);
		EmitExceptionRaise(reason: 3, hasException: false);
		_assembler.Mark(divisorReady);

		if (_request.Cpu != M68kCpuTarget.M68000)
		{
			_assembler.EmitWord(0x4C41); // DIV[SU].L D1,D2:D0
			_assembler.EmitWord((ushort)((signed ? 0x0800 : 0) | 0x0002));
			if (remainder)
			{
				_assembler.EmitWord(0x2002); // MOVE.L D2,D0
			}

			return;
		}

		string? dividendPositive = null;
		string? divisorPositive = null;
		if (signed)
		{
			dividendPositive = UniqueLabel("dividend_positive");
			divisorPositive = UniqueLabel("divisor_positive");
			_assembler.EmitWord(0x2C00); // MOVE.L D0,D6 (original dividend)
			_assembler.EmitWord(0x7A00); // MOVEQ #0,D5 (quotient sign)
			_assembler.EmitWord(0x4A80); // TST.L D0
			_assembler.EmitBranch(M68kCondition.Plus, dividendPositive);
			_assembler.EmitWord(0x4480); // NEG.L D0
			_assembler.EmitWord(0x08C5); // BSET #0,D5
			_assembler.EmitWord(0x0000);
			_assembler.Mark(dividendPositive);
			_assembler.EmitWord(0x4A81); // TST.L D1
			_assembler.EmitBranch(M68kCondition.Plus, divisorPositive);
			_assembler.EmitWord(0x4481); // NEG.L D1
			_assembler.EmitWord(0x0845); // BCHG #0,D5
			_assembler.EmitWord(0x0000);
			_assembler.Mark(divisorPositive);
		}

		var loop = UniqueLabel("div_loop");
		var subtract = UniqueLabel("div_subtract");
		var noSubtract = UniqueLabel("div_no_sub");
		// The carry-chain body is 18 bytes on MC68000 versus 22 bytes for the
		// former BTST/ORI/BSET body. It also retains the 33rd remainder bit in C,
		// which is required when doubling a remainder above $7fffffff.
		_assembler.EmitWord(0x7600); // MOVEQ #0,D3 remainder
		_assembler.EmitWord(0x781F); // MOVEQ #31,D4 counter
		_assembler.Mark(loop);
		_assembler.EmitWord(0xD080); // ADD.L D0,D0: shift dividend/quotient, bit into X
		_assembler.EmitWord(0xD783); // ADDX.L D3,D3: shift remainder and consume bit
		_assembler.EmitBranch(M68kCondition.CarrySet, subtract);
		_assembler.EmitWord(0xB681); // CMP.L D1,D3
		_assembler.EmitBranch(M68kCondition.CarrySet, noSubtract);
		_assembler.Mark(subtract);
		_assembler.EmitWord(0x9681); // SUB.L D1,D3
		_assembler.EmitWord(0x5280); // ADDQ.L #1,D0: append quotient bit
		_assembler.Mark(noSubtract);
		_assembler.EmitDbra(4, loop);

		if (signed)
		{
			var quotientPositive = UniqueLabel("quotient_positive");
			var remainderPositive = UniqueLabel("remainder_positive");
			_assembler.EmitWord(0x0805); // BTST #0,D5
			_assembler.EmitWord(0x0000);
			_assembler.EmitBranch(M68kCondition.Equal, quotientPositive);
			_assembler.EmitWord(0x4480); // NEG.L D0
			_assembler.Mark(quotientPositive);
			_assembler.EmitWord(0x4A86); // TST.L D6
			_assembler.EmitBranch(M68kCondition.Plus, remainderPositive);
			_assembler.EmitWord(0x4483); // NEG.L D3
			_assembler.Mark(remainderPositive);
		}

		if (remainder)
		{
			_assembler.EmitWord(0x2003); // MOVE.L D3,D0
		}
	}

	private void EmitShift(OpCode op)
	{
		EmitShift(op, CilStackValueKind.Int32);
	}

	private void EmitShift(
		OpCode op,
		CilStackValueKind resultKind,
		int? immediate = null)
	{
		if (op == OpCodes.Shr && resultKind is
			(CilStackValueKind.BooleanByte or
			 CilStackValueKind.UnsignedByte or
			 CilStackValueKind.UnsignedWord))
		{
			op = OpCodes.Shr_Un;
		}
		var width = CilStackValueLayout.ArithmeticWidth(resultKind);
		if (immediate is { } shiftCount)
		{
			EmitImmediateShift(op, width, shiftCount);
			return;
		}

		_assembler.EmitWord(0x0281); // ANDI.L #31,D1
		_assembler.EmitLong(31);
		_assembler.EmitWord((ushort)(ShiftOpcode(op, width) | 0x0020));
	}

	private void EmitImmediateShift(OpCode op, int width, int count)
	{
		count &= 31;
		while (count != 0)
		{
			var chunk = Math.Min(count, 8);
			var encodedCount = chunk == 8 ? 0 : chunk;
			var opcode = ShiftOpcode(op, width);
			_assembler.EmitWord((ushort)(
				(opcode & ~(7 << 9)) |
				(encodedCount << 9)));
			count -= chunk;
		}
	}

	private void EmitComparison(OpCode op)
	{
		var leftKind = CurrentStackKindOrLong(1);
		var rightKind = CurrentStackKindOrLong();
		var width = CilStackValueLayout.IsSmall(leftKind) &&
			CilStackValueLayout.IsSmall(rightKind)
			? Math.Max(
				CilStackValueLayout.ArithmeticWidth(leftKind),
				CilStackValueLayout.ArithmeticWidth(rightKind))
			: 4;
		EmitPopBinaryOperands(widen: width == 4 || width == 2);
		_assembler.EmitWord(ComparisonOpcode(width));
		var condition = ComparisonCondition(op);
		EmitComparisonResult(condition);
		EmitPushD0();
	}

	private void EmitUnary(OpCode op, CilStackValueKind resultKind)
	{
		var width = CilStackValueLayout.ArithmeticWidth(resultKind);
		var baseOpcode = op == OpCodes.Neg ? 0x4400 : 0x4600;
		_assembler.EmitWord((ushort)(baseOpcode + ((width - 1) * 0x40)));
	}

	private static ushort ShiftOpcode(OpCode op, int width) =>
		(op == OpCodes.Shl ? 0xE308 : op == OpCodes.Shr ? 0xE200 : 0xE208) switch
		{
			var baseOpcode when width == 1 => (ushort)baseOpcode,
			var baseOpcode when width == 2 => (ushort)(baseOpcode + 0x40),
			var baseOpcode when width == 4 => (ushort)(baseOpcode + 0x80),
			_ => throw new ArgumentOutOfRangeException(nameof(width))
		};

	private static ushort ComparisonOpcode(int width) =>
		width switch
		{
			1 => 0xB001, // CMP.B D1,D0
			2 => 0xB041, // CMP.W D1,D0
			4 => 0xB081, // CMP.L D1,D0
			_ => throw new ArgumentOutOfRangeException(nameof(width))
		};

	private CilStackValueKind CurrentArithmeticResultKind(int poppedCount)
	{
		var resultCount = _currentStackTypes.Length - poppedCount + 1;
		return resultCount > 0 && _nextStackTypes.Length == resultCount
			? _nextStackTypes[^1]
			: CilStackValueKind.Int32;
	}

	private void EmitNormalizeArithmeticResult(CilStackValueKind resultKind)
	{
		if (CilStackValueLayout.IsSignedByte(resultKind))
		{
			EmitSignExtendByteToLongD0();
		}
		else if (resultKind == CilStackValueKind.UnsignedByte ||
			resultKind == CilStackValueKind.BooleanByte)
		{
			_assembler.EmitWord(0x0280); // ANDI.L #mask,D0
			_assembler.EmitLong(resultKind == CilStackValueKind.BooleanByte ? 1u : 0xFFu);
		}
		else if (CilStackValueLayout.IsSignedWord(resultKind))
		{
			_assembler.EmitWord(0x48C0); // EXT.L D0
		}
		else if (resultKind == CilStackValueKind.UnsignedWord)
		{
			_assembler.EmitWord(0x0280); // ANDI.L #$FFFF,D0
			_assembler.EmitLong(0x0000FFFF);
		}
	}

	private void EmitPushArithmeticResult(CilStackValueKind resultKind)
	{
		if (CilStackValueLayout.IsByte(resultKind))
		{
			EmitPushByteRegister(M68kRegister.D0);
		}
		else
		{
			EmitPushD0();
		}
	}

	private bool TryGetDirectBinaryArgumentValues(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out ArgumentValue left,
		out ArgumentValue right,
		out int nextIndex)
	{
		left = default;
		right = default;
		nextIndex = 0;
		if (!TryGetArgumentValueExpression(
				caller,
				instructions,
				startIndex,
				out left,
				out var leftConsumed))
		{
			return false;
		}

		var rightIndex = startIndex + leftConsumed;
		if (rightIndex >= instructions.Count ||
			branchTargets.Contains(instructions[rightIndex].Offset) ||
			(!left.AllowsInternalBranchTargets &&
				HasBranchTarget(
					branchTargets,
					instructions,
					startIndex,
					startIndex + 1,
					rightIndex - 1)) ||
			!TryGetArgumentValueExpression(
				caller,
				instructions,
				rightIndex,
				out right,
				out var rightConsumed))
		{
			return false;
		}

		nextIndex = rightIndex + rightConsumed;
		if (!right.AllowsInternalBranchTargets &&
			HasBranchTarget(
				branchTargets,
				instructions,
				rightIndex,
				rightIndex + 1,
				nextIndex - 1))
		{
			return false;
		}

		return true;
	}

	private bool TryEmitDirectComparison(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (!TryGetDirectBinaryArgumentValues(
				caller,
				instructions,
				startIndex,
				branchTargets,
				out var left,
				out var right,
				out var comparisonIndex))
		{
			return false;
		}

		if (comparisonIndex >= instructions.Count ||
			branchTargets.Contains(instructions[comparisonIndex].Offset) ||
			!IsComparisonOp(instructions[comparisonIndex].OpCode))
		{
			return false;
		}

		EmitArgumentValueToRegister(caller, left, _currentStackDepth, M68kRegister.D0);
		if (!TryEmitCompareValueWithD0(
			caller,
			right,
			_currentStackDepth,
			ArgumentValueSetsFlags(caller, left)))
		{
			EmitArgumentValueToRegister(caller, right, _currentStackDepth, M68kRegister.D1);
			_assembler.EmitWord(0xB081); // CMP.L D1,D0
		}

		EmitComparisonResult(ComparisonCondition(instructions[comparisonIndex].OpCode));
		EmitPushD0();
		consumed = comparisonIndex - startIndex + 1;
		return true;
	}

	private bool TryEmitDirectComparisonResultStore(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		IReadOnlyDictionary<int, ImmutableArray<CilStackValueKind>> reachableStackStates,
		out int consumed)
	{
		consumed = 0;
		if (!TryGetDirectBinaryArgumentValues(
				caller,
				instructions,
				startIndex,
				branchTargets,
				out var left,
				out var right,
				out var comparisonIndex) ||
			comparisonIndex + 1 >= instructions.Count ||
			branchTargets.Contains(instructions[comparisonIndex].Offset) ||
			branchTargets.Contains(instructions[comparisonIndex + 1].Offset) ||
			!IsComparisonOp(instructions[comparisonIndex].OpCode) ||
			!TryGetStoreDestination(instructions[comparisonIndex + 1], out var destination) ||
			!reachableStackStates.TryGetValue(
				instructions[comparisonIndex + 1].Offset,
				out var storeStackTypes) ||
			storeStackTypes.Length == 0)
		{
			return false;
		}

		// Local comparison results need the ordinary typed store path. It keeps
		// the full four-byte local home coherent for existing long consumers.
		if (destination.IsLocal)
		{
			return false;
		}

		EmitArgumentValueToRegister(caller, left, _currentStackDepth, M68kRegister.D0);
		if (!TryEmitCompareValueWithD0(
			caller,
			right,
			_currentStackDepth,
			ArgumentValueSetsFlags(caller, left)))
		{
			EmitArgumentValueToRegister(caller, right, _currentStackDepth, M68kRegister.D1);
			_assembler.EmitWord(0xB081); // CMP.L D1,D0
		}

		EmitComparisonResult(ComparisonCondition(instructions[comparisonIndex].OpCode));
		EmitStoreD0ToDestination(
			caller,
			destination,
			stackDepth: storeStackTypes.Length - 1);
		consumed = comparisonIndex - startIndex + 2;
		return true;
	}

	private bool TryEmitDirectComparisonStoreBranch(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (!TryGetDirectBinaryArgumentValues(
				caller,
				instructions,
				startIndex,
				branchTargets,
				out var left,
				out var right,
				out var comparisonIndex) ||
			comparisonIndex + 3 >= instructions.Count ||
			branchTargets.Contains(instructions[comparisonIndex].Offset) ||
			!IsComparisonOp(instructions[comparisonIndex].OpCode))
		{
			return false;
		}

		var condition = ComparisonCondition(instructions[comparisonIndex].OpCode);
		var storeIndex = comparisonIndex + 1;
		if (storeIndex + 2 < instructions.Count &&
			!branchTargets.Contains(instructions[storeIndex].Offset) &&
			TryGetConstant(instructions[storeIndex], out var invertZero) &&
			invertZero == 0 &&
			instructions[storeIndex + 1].OpCode == OpCodes.Ceq)
		{
			if (branchTargets.Contains(instructions[storeIndex + 1].Offset))
			{
				return false;
			}

			condition = InvertCondition(condition);
			storeIndex += 2;
		}

		if (storeIndex + 2 >= instructions.Count ||
			branchTargets.Contains(instructions[storeIndex].Offset) ||
			branchTargets.Contains(instructions[storeIndex + 1].Offset) ||
			branchTargets.Contains(instructions[storeIndex + 2].Offset) ||
			!TryGetStoreLocalIndex(instructions[storeIndex], out var storeLocalIndex) ||
			!TryGetLoadLocalIndex(instructions[storeIndex + 1], out var loadLocalIndex) ||
			loadLocalIndex != storeLocalIndex ||
			!TryGetBooleanBranchCondition(
				instructions[storeIndex + 2].OpCode,
				out var branchCondition) ||
			instructions[storeIndex + 2].Operand is not int targetOffset ||
			IsLocalReadAfter(instructions, storeIndex + 2, storeLocalIndex))
		{
			return false;
		}

		if (!ProtectedInstructionRangeCanBeCombined(
			caller,
			instructions,
			startIndex,
			storeIndex + 2))
		{
			return false;
		}

		if (branchCondition == M68kCondition.Equal)
		{
			condition = InvertCondition(condition);
		}

		if (TryEmitRegisterImmediateRelationalBranch(
				caller,
				left,
				right,
				condition,
				IlLabel(caller, targetOffset)))
		{
			consumed = storeIndex - startIndex + 3;
			return true;
		}

		EmitArgumentValueToRegister(caller, left, _currentStackDepth, M68kRegister.D0);
		if (!TryEmitCompareValueWithD0(
			caller,
			right,
			_currentStackDepth,
			ArgumentValueSetsFlags(caller, left)))
		{
			EmitArgumentValueToRegister(caller, right, _currentStackDepth, M68kRegister.D1);
			_assembler.EmitWord(0xB081); // CMP.L D1,D0
		}

		_assembler.EmitBranch(condition, IlLabel(caller, targetOffset));
		consumed = storeIndex - startIndex + 3;
		return true;
	}

	private bool TryEmitDirectRelationalBranch(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (!TryGetDirectBinaryArgumentValues(
				caller,
				instructions,
				startIndex,
				branchTargets,
				out var left,
				out var right,
				out var branchIndex))
		{
			return false;
		}

		if (branchIndex >= instructions.Count ||
			branchTargets.Contains(instructions[branchIndex].Offset) ||
			!TryGetRelationalBranch(instructions[branchIndex].OpCode, out var condition))
		{
			return false;
		}

		if (TryEmitRegisterImmediateRelationalBranch(
				caller,
				left,
				right,
				condition,
				IlLabel(caller, (int)instructions[branchIndex].Operand!)))
		{
			consumed = branchIndex - startIndex + 1;
			return true;
		}

		EmitArgumentValueToRegister(caller, left, _currentStackDepth, M68kRegister.D0);
		if (!TryEmitCompareValueWithD0(
			caller,
			right,
			_currentStackDepth,
			ArgumentValueSetsFlags(caller, left)))
		{
			EmitArgumentValueToRegister(caller, right, _currentStackDepth, M68kRegister.D1);
			_assembler.EmitWord(0xB081); // CMP.L D1,D0
		}

		_assembler.EmitBranch(condition, IlLabel(caller, (int)instructions[branchIndex].Operand!));
		consumed = branchIndex - startIndex + 1;
		return true;
	}

	private bool TryEmitDirectComparisonBranch(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (!TryGetDirectBinaryArgumentValues(
				caller,
				instructions,
				startIndex,
				branchTargets,
				out var left,
				out var right,
				out var comparisonIndex) ||
			comparisonIndex + 1 >= instructions.Count ||
			branchTargets.Contains(instructions[comparisonIndex].Offset) ||
			!IsComparisonOp(instructions[comparisonIndex].OpCode))
		{
			return false;
		}

		var condition = ComparisonCondition(instructions[comparisonIndex].OpCode);
		var branchIndex = comparisonIndex + 1;
		if (branchIndex + 2 < instructions.Count &&
			!branchTargets.Contains(instructions[branchIndex].Offset) &&
			TryGetConstant(instructions[branchIndex], out var invertZero) &&
			invertZero == 0 &&
			instructions[branchIndex + 1].OpCode == OpCodes.Ceq)
		{
			if (branchTargets.Contains(instructions[branchIndex + 1].Offset))
			{
				return false;
			}

			condition = InvertCondition(condition);
			branchIndex += 2;
		}

		if (branchIndex >= instructions.Count ||
			branchTargets.Contains(instructions[branchIndex].Offset) ||
			!TryGetBooleanBranchCondition(
				instructions[branchIndex].OpCode,
				out var branchCondition) ||
			instructions[branchIndex].Operand is not int targetOffset)
		{
			return false;
		}

		if (branchCondition == M68kCondition.Equal)
		{
			condition = InvertCondition(condition);
		}

		if (TryEmitRegisterImmediateRelationalBranch(
				caller,
				left,
				right,
				condition,
				IlLabel(caller, targetOffset)))
		{
			consumed = branchIndex - startIndex + 1;
			return true;
		}

		EmitArgumentValueToRegister(caller, left, _currentStackDepth, M68kRegister.D0);
		if (!TryEmitCompareValueWithD0(
			caller,
			right,
			_currentStackDepth,
			ArgumentValueSetsFlags(caller, left)))
		{
			EmitArgumentValueToRegister(caller, right, _currentStackDepth, M68kRegister.D1);
			_assembler.EmitWord(0xB081); // CMP.L D1,D0
		}

		_assembler.EmitBranch(condition, IlLabel(caller, targetOffset));
		consumed = branchIndex - startIndex + 1;
		return true;
	}

	private bool TryEmitRegisterImmediateRelationalBranch(
		CilMethod caller,
		ArgumentValue left,
		ArgumentValue right,
		M68kCondition condition,
		string targetLabel)
	{
		if (left.Instruction is not { } leftInstruction ||
			right.Instruction is not { } rightInstruction ||
			!TryGetConstant(rightInstruction, out var constant) ||
			!TryGetArgumentValueDataRegister(caller, leftInstruction, out var register))
		{
			return false;
		}

		if (constant is >= short.MinValue and <= short.MaxValue &&
			TryGetLoadLocalIndex(leftInstruction, out var localIndex) &&
			LocalValueFitsSignedWord(caller, localIndex))
		{
			EmitCompareWordImmediateWithRegister(register, (short)constant);
		}
		else
		{
			EmitCompareImmediateWithRegister(register, constant);
		}
		_assembler.EmitBranch(condition, targetLabel);
		return true;
	}

	private bool TryGetArgumentValueDataRegister(
		CilMethod caller,
		CilInstruction instruction,
		out M68kRegister register)
	{
		if (TryGetLoadLocalIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } localRegister &&
				localRegister <= M68kRegister.D7)
			{
				register = localRegister;
				return true;
			}
		}
		else if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (ArgumentRegister(argumentIndex) is { } argumentRegister &&
				argumentRegister <= M68kRegister.D7)
			{
				register = argumentRegister;
				return true;
			}
		}

		register = default;
		return false;
	}

	private bool LocalValueFitsSignedWord(CilMethod method, int localIndex)
	{
		int? min = null;
		int? max = null;
		for (var index = 0; index < method.Instructions.Count; index++)
		{
			var instruction = method.Instructions[index];
			if (!TryGetStoreLocalIndex(instruction, out var storedLocal) ||
				storedLocal != localIndex)
			{
				continue;
			}

			if (index >= 1 &&
				TryGetConstant(method.Instructions[index - 1], out var storedConstant))
			{
				min = min is null ? storedConstant : Math.Min(min.Value, storedConstant);
				max = max is null ? storedConstant : Math.Max(max.Value, storedConstant);
				continue;
			}

			if (index >= 3 &&
				TryGetLoadLocalIndex(method.Instructions[index - 3], out var loadedLocal) &&
				loadedLocal == localIndex &&
				TryGetConstant(method.Instructions[index - 2], out var updateConstant) &&
				TryNormalizeQuickUpdate(
					updateConstant,
					method.Instructions[index - 1].OpCode,
					out var quickCount,
					out var subtract))
			{
				if (min is null || max is null)
				{
					return false;
				}

				var delta = subtract ? -quickCount : quickCount;
				var updatedMin = min.Value + delta;
				var updatedMax = max.Value + delta;
				min = Math.Min(min.Value, updatedMin);
				max = Math.Max(max.Value, updatedMax);
				continue;
			}

			return false;
		}

		return min is >= short.MinValue &&
			max is <= short.MaxValue;
	}

	private static bool IsComparisonOp(OpCode op) =>
		op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un ||
		op == OpCodes.Clt || op == OpCodes.Clt_Un;

	private static M68kCondition ComparisonCondition(OpCode op) =>
		op == OpCodes.Ceq
			? M68kCondition.Equal
			: op == OpCodes.Cgt
				? M68kCondition.GreaterThan
				: op == OpCodes.Cgt_Un
					? M68kCondition.Higher
					: op == OpCodes.Clt
						? M68kCondition.LessThan
						: M68kCondition.CarrySet;

	private static bool TryGetBooleanBranchCondition(OpCode op, out M68kCondition condition)
	{
		if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S)
		{
			condition = M68kCondition.NotEqual;
			return true;
		}

		if (op == OpCodes.Brfalse || op == OpCodes.Brfalse_S)
		{
			condition = M68kCondition.Equal;
			return true;
		}

		condition = default;
		return false;
	}

	private static M68kCondition InvertCondition(M68kCondition condition) =>
		condition switch
		{
			M68kCondition.Equal => M68kCondition.NotEqual,
			M68kCondition.NotEqual => M68kCondition.Equal,
			M68kCondition.GreaterThan => M68kCondition.LessOrEqual,
			M68kCondition.LessOrEqual => M68kCondition.GreaterThan,
			M68kCondition.GreaterOrEqual => M68kCondition.LessThan,
			M68kCondition.LessThan => M68kCondition.GreaterOrEqual,
			M68kCondition.Higher => M68kCondition.LowerOrSame,
			M68kCondition.LowerOrSame => M68kCondition.Higher,
			M68kCondition.CarrySet => M68kCondition.CarryClear,
			M68kCondition.CarryClear => M68kCondition.CarrySet,
			_ => throw new InvalidOperationException($"Condition '{condition}' cannot be inverted.")
		};

	private static bool IsLocalReadAfter(
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		int localIndex)
	{
		for (var index = startIndex + 1; index < instructions.Count; index++)
		{
			if (TryGetLoadLocalIndex(instructions[index], out var loadLocalIndex) &&
				loadLocalIndex == localIndex)
			{
				return true;
			}
		}

		return false;
	}

	private bool ProtectedInstructionRangeCanBeCombined(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		int endIndex)
	{
		if (method.ExceptionRegions.Count == 0)
		{
			return true;
		}

		var activeExceptionGroups = GetActiveExceptionGroups(
			method,
			instructions[startIndex].Offset);
		for (var index = startIndex + 1; index <= endIndex; index++)
		{
			var instruction = instructions[index];
			if (method.ExceptionRegions.Any(region =>
					region.HandlerOffset == instruction.Offset) ||
				!activeExceptionGroups.SequenceEqual(
					GetActiveExceptionGroups(method, instruction.Offset)))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsLocalAccessAfter(
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		int localIndex)
	{
		for (var index = startIndex + 1; index < instructions.Count; index++)
		{
			if ((TryGetLoadLocalIndex(instructions[index], out var loadLocalIndex) &&
				 loadLocalIndex == localIndex) ||
				(TryGetLoadLocalAddressIndex(instructions[index], out var addressLocalIndex) &&
				 addressLocalIndex == localIndex) ||
				(TryGetStoreLocalIndex(instructions[index], out var storeLocalIndex) &&
				 storeLocalIndex == localIndex))
			{
				return true;
			}
		}

		return false;
	}

	private void EmitComparisonResult(M68kCondition condition)
	{
		_assembler.EmitWord((ushort)(0x50C0 | ((int)condition << 8))); // Scc D0
		EmitSignExtendByteToLongD0();
		_assembler.EmitWord(0x4480); // NEG.L D0, FFFFFFFF -> 1
	}

	private void EmitByteComparisonResult(M68kCondition condition)
	{
		_assembler.EmitWord((ushort)(0x50C0 | ((int)condition << 8))); // Scc D0
		_assembler.EmitWord(0x4400); // NEG.B D0, FF -> 1
	}

	private void EmitSignExtendByteToLongD0()
	{
		if (_request.Cpu == M68kCpuTarget.M68000)
		{
			_assembler.EmitWord(0x4880); // EXT.W D0
			_assembler.EmitWord(0x48C0); // EXT.L D0
			return;
		}

		_assembler.EmitWord(0x49C0); // EXTB.L D0 (68020+)
		_assembler.EmitWord(0x0280); // ANDI.L #$FF,D0
		_assembler.EmitLong(0x000000FF);
		EmitSignExtendByteMask(M68kRegister.D0);
	}

	private void EmitSignExtendByteMask(M68kRegister register)
	{
		var done = UniqueLabel("sign_extend_byte_done");
		_assembler.EmitWord((ushort)(0x0800 | (int)register)); // BTST #7,Dn
		_assembler.EmitWord(0x0007);
		_assembler.EmitBranch(M68kCondition.Equal, done);
		_assembler.EmitWord((ushort)(0x0080 | ((int)register << 9))); // ORI.L #$FFFFFF00,Dn
		_assembler.EmitLong(0xFFFFFF00);
		_assembler.Mark(done);
	}

	private void EmitStoreD0ToDestination(
		CilMethod caller,
		StoreDestination destination,
		int stackDepth)
	{
		if (destination.IsLocal)
		{
			if (LocalRegister(destination.Index) is { } localRegister)
			{
				EmitMoveRegisterToRegister(M68kRegister.D0, localRegister);
				return;
			}

			EmitStoreRegisterToFrame(
				M68kRegister.D0,
				FrameDisplacement(
					LocalOffset(caller, destination.Index),
					stackDepth));
			return;
		}

		EmitStoreRegisterToFrame(
			M68kRegister.D0,
			FrameDisplacement(
				ArgumentOffset(caller, destination.Index),
				stackDepth));
	}

	private bool ArgumentValueSetsFlags(CilMethod caller, ArgumentValue value)
	{
		if (value.Instruction is not { } instruction)
		{
			return true;
		}

		if (TryGetConstant(instruction, out _) ||
			instruction.OpCode == OpCodes.Ldnull ||
			instruction.OpCode == OpCodes.Ldsfld)
		{
			return true;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			return ArgumentRegister(argumentIndex) is not M68kRegister.D0;
		}

		if (TryGetLoadLocalIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			return LocalRegister(localIndex) is not M68kRegister.D0;
		}

		return false;
	}

	private bool TryEmitCompareValueWithD0(
		CilMethod caller,
		ArgumentValue value,
		int stackDepth,
		bool leftSetsFlags)
	{
		if (value.Instruction is not { } instruction)
		{
			EmitCompareImmediateWithD0(0);
			return true;
		}

		if (TryGetConstant(instruction, out var constant))
		{
			if (leftSetsFlags && constant == 0)
			{
				return true;
			}

			EmitCompareImmediateWithD0(constant);
			return true;
		}

		if (instruction.OpCode == OpCodes.Ldnull)
		{
			EmitCompareImmediateWithD0(0);
			return true;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (ArgumentRegister(argumentIndex) is { } argumentRegister)
			{
				return TryEmitCompareRegisterWithD0(argumentRegister);
			}

			EmitCompareStackSlotWithD0(FrameDisplacement(
				ArgumentOffset(caller, argumentIndex),
				stackDepth));
			return true;
		}

		if (TryGetLoadLocalIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } localRegister)
			{
				return TryEmitCompareRegisterWithD0(localRegister);
			}

			EmitCompareStackSlotWithD0(FrameDisplacement(
				LocalOffset(caller, localIndex),
				stackDepth));
			return true;
		}

		return false;
	}

	private bool TryEmitCompareRegisterWithD0(M68kRegister register)
	{
		if (register == M68kRegister.D0)
		{
			return false;
		}

		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0xB080 | (int)register)); // CMP.L Dn,D0
			return true;
		}

		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0xB088 | addressRegister)); // CMP.L An,D0
		return true;
	}

	private void EmitCompareStackSlotWithD0(short displacement)
	{
		if (displacement == 0)
		{
			_assembler.EmitWord(0xB097); // CMP.L (A7),D0
			return;
		}

		_assembler.EmitWord(0xB0AF); // CMP.L d16(A7),D0
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitCompareImmediateWithD0(int value)
	{
		_assembler.EmitWord(0x0C80); // CMPI.L #value,D0
		_assembler.EmitLong(unchecked((uint)value));
	}

	private void EmitCompareImmediateWithRegister(M68kRegister register, int value)
	{
		if (register > M68kRegister.D7)
		{
			throw new ArgumentOutOfRangeException(nameof(register));
		}

		_assembler.EmitWord((ushort)(0x0C80 | (int)register)); // CMPI.L #value,Dn
		_assembler.EmitLong(unchecked((uint)value));
	}

	private void EmitCompareWordImmediateWithRegister(M68kRegister register, short value)
	{
		if (register > M68kRegister.D7)
		{
			throw new ArgumentOutOfRangeException(nameof(register));
		}

		_assembler.EmitWord((ushort)(0x0C40 | (int)register)); // CMPI.W #value,Dn
		_assembler.EmitWord(unchecked((ushort)value));
	}

	private void EmitSwitch(CilMethod method, CilInstruction instruction)
	{
		EmitPopD0();
		var targets = (int[])instruction.Operand!;
		for (var index = 0; index < targets.Length; index++)
		{
			_assembler.EmitWord(0x0C80); // CMPI.L #index,D0
			_assembler.EmitLong((uint)index);
			_assembler.EmitBranch(M68kCondition.Equal, IlLabel(method, targets[index]));
		}
	}

	private bool TryEmitAddressIntrinsicConstantCall(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (startIndex + 2 >= instructions.Count ||
			!IsDirectAddressIntrinsicValue(instructions[startIndex]) ||
			!TryGetConstant(instructions[startIndex + 1], out var offset) ||
			offset < short.MinValue ||
			offset > short.MaxValue ||
			branchTargets.Contains(instructions[startIndex + 1].Offset))
		{
			return false;
		}

		var callIndex = startIndex + 2;
		var call = instructions[callIndex];
		if ((call.OpCode == OpCodes.Call || call.OpCode == OpCodes.Callvirt) &&
			!branchTargets.Contains(call.Offset))
		{
			var target = _module.ResolveMethodToken(
				(int)call.Operand!,
				caller,
				call.Offset);
			if (target.Definition is null &&
				target.ImportName == "intrinsic:aptr-read-uint32")
			{
				var pushResult = true;
				consumed = 3;
				if (callIndex + 1 < instructions.Count &&
					instructions[callIndex + 1].OpCode == OpCodes.Pop &&
					!branchTargets.Contains(instructions[callIndex + 1].Offset))
				{
					pushResult = false;
					consumed++;
				}

				EmitArgumentValueToRegister(
					caller,
					new ArgumentValue(instructions[startIndex]),
					_currentStackDepth,
					M68kRegister.A0);
				EmitLoadD0FromA0Displacement(4, signExtend: false, (short)offset);
				if (pushResult)
				{
					EmitPushD0();
				}
				return true;
			}
		}

		if (startIndex + 3 >= instructions.Count ||
			!IsDirectAddressIntrinsicValue(instructions[startIndex + 2]) ||
			branchTargets.Contains(instructions[startIndex + 2].Offset))
		{
			return false;
		}

		var value = instructions[startIndex + 2];
		var writeCall = instructions[startIndex + 3];
		if ((writeCall.OpCode != OpCodes.Call && writeCall.OpCode != OpCodes.Callvirt) ||
			branchTargets.Contains(writeCall.Offset))
		{
			return false;
		}

		var writeTarget = _module.ResolveMethodToken(
			(int)writeCall.Operand!,
			caller,
			writeCall.Offset);
		if (writeTarget.Definition is not null ||
			writeTarget.ImportName != "intrinsic:aptr-write-uint32")
		{
			return false;
		}

		EmitArgumentValueToRegister(
			caller,
			new ArgumentValue(instructions[startIndex]),
			_currentStackDepth,
			M68kRegister.A0);
		EmitArgumentValueToRegister(
			caller,
			new ArgumentValue(value),
			_currentStackDepth,
			M68kRegister.D0);
		EmitStoreD0ToA0Displacement(4, (short)offset);
		consumed = 4;
		return true;
	}

	private static bool IsDirectAddressIntrinsicValue(CilInstruction instruction) =>
		TryGetConstant(instruction, out _) ||
		instruction.OpCode == OpCodes.Ldnull ||
		instruction.OpCode == OpCodes.Ldsfld ||
		TryGetLoadLocalIndex(instruction, out _) ||
		TryGetArgumentIndex(instruction, out _);

	private void EmitCall(CilMethod caller, CilInstruction instruction, bool pushResult = true)
	{
		var target = _module.ResolveMethodToken((int)instruction.Operand!, caller, instruction.Offset);
		if (target.Definition is null)
		{
			if (target.ImportName == "intrinsic:runtime-throw-overflow")
			{
				EmitExceptionRaise(reason: 4, hasException: false);
				return;
			}
			if (target.ImportName == "intrinsic:runtime-throw-arithmetic")
			{
				_usesArithmeticExceptionFault = true;
				RegisterRuntimeTypeDescriptor("System.ArithmeticException");
				EmitExceptionRaise(reason: 19, hasException: false);
				return;
			}
			if (target.ImportName == "intrinsic:list-enumerator-dispose")
			{
				EmitDiscardStackArguments(target.ParameterCount);
				return;
			}
			if (target.ImportName == "intrinsic:runtime-throw-argument-out-of-range")
			{
				RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
				EmitExceptionRaise(reason: 10, hasException: false);
				return;
			}
			if (target.ImportName == "intrinsic:runtime-throw-invalid-operation")
			{
				RegisterRuntimeTypeDescriptor("System.InvalidOperationException");
				EmitExceptionRaise(reason: 13, hasException: false);
				return;
			}
			if (target.ImportName == "intrinsic:runtime-throw-io")
			{
				RegisterRuntimeTypeDescriptor("System.IO.IOException");
				EmitExceptionRaise(reason: 15, hasException: false);
				return;
			}
			if (target.ImportName == "intrinsic:runtime-throw-directory-not-found")
			{
				RegisterRuntimeTypeDescriptor("System.IO.DirectoryNotFoundException");
				EmitExceptionRaise(reason: 16, hasException: false);
				return;
			}
			if (target.ImportName == "intrinsic:runtime-throw-file-not-found")
			{
				RegisterRuntimeTypeDescriptor("System.IO.FileNotFoundException");
				EmitExceptionRaise(reason: 18, hasException: false);
				return;
			}
			if (target.ImportName == "intrinsic:runtime-throw-unauthorized-access")
			{
				RegisterRuntimeTypeDescriptor("System.UnauthorizedAccessException");
				EmitExceptionRaise(reason: 17, hasException: false);
				return;
			}
			if (target.ImportName == "intrinsic:runtime-throw-key-not-found")
			{
				RegisterRuntimeTypeDescriptor("System.Collections.Generic.KeyNotFoundException");
				EmitExceptionRaise(reason: 14, hasException: false);
				return;
			}
			if (target.ImportName is
					"intrinsic:dictionary-key-is-null:false" or
					"intrinsic:dictionary-key-is-null:reference")
				{
					EmitPopD0();
					if (target.ImportName.EndsWith(":false", StringComparison.Ordinal))
					{
						_assembler.EmitWord(0x7000); // MOVEQ #0,D0
					}
					else
					{
						_assembler.EmitWord(0x4A80); // TST.L D0
						_assembler.EmitWord(0x57C0); // SEQ D0
						_assembler.EmitWord(0x4400); // NEG.B D0, FF -> 1
					}
					if (pushResult)
					{
						EmitPushByteRegister(M68kRegister.D0);
					}
					else
					{
						EmitSignExtendByteToLongD0();
					}
					return;
				}

			if (target.ImportName == "intrinsic:object-ctor")
			{
				EmitDiscardStackArguments(target.ParameterCount);
				return;
			}

			if (target.ImportName == "intrinsic:string-length")
			{
				EmitPopD0();
				EmitRequireNonNull();
				_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
				_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0
				_assembler.EmitWord(0x0008);
				EmitPushD0();
				return;
			}

			if (target.ImportName is
				"intrinsic:cstring-from-literal" or
				"intrinsic:amiga-vararg-from-literal")
			{
				EmitCStringFromLiteral(caller, instruction, target.ImportName);
				return;
			}

			if (target.ImportName?.StartsWith(
				"intrinsic:runtimehelpers-is-reference-or-contains-references:",
				StringComparison.Ordinal) == true)
			{
				EmitPushConstant(
					target.ImportName.EndsWith(":true", StringComparison.Ordinal)
						? 1
						: 0);
				return;
			}

			if (target.ImportName is "intrinsic:cstring-from-pointer" or "intrinsic:cstring-to-uint32")
			{
				return;
			}

			if (target.ImportName == "intrinsic:file-info-block-file-name")
			{
				EmitPopD0();
				_assembler.EmitWord(0x5080); // ADDQ.L #8,D0
				if (pushResult)
				{
					EmitPushD0();
				}
				return;
			}

			if (target.ImportName == "intrinsic:aptr-null")
			{
				if (pushResult)
				{
					EmitPushConstant(0);
				}
				return;
			}

			if (target.ImportName == "intrinsic:aptr-export-address")
			{
				EmitExportAddress(caller, instruction, pushResult);
				return;
			}

			if (target.ImportName == "intrinsic:boopsi-instance-data")
			{
				EmitBoopsiInstanceData(pushResult);
				return;
			}

			if (target.ImportName == "intrinsic:aptr-read-uint32")
			{
				EmitAptrReadUInt32(pushResult);
				return;
			}

			if (target.ImportName == "intrinsic:aptr-read-uint8")
			{
				EmitAptrRead(1, pushResult);
				return;
			}

			if (target.ImportName == "intrinsic:aptr-read-uint16")
			{
				EmitAptrRead(2, pushResult);
				return;
			}

			if (target.ImportName == "intrinsic:aptr-write-uint32")
			{
				EmitAptrWriteUInt32();
				return;
			}

			if (target.ImportName == "intrinsic:aptr-write-uint8")
			{
				EmitAptrWrite(1);
				return;
			}

			if (target.ImportName == "intrinsic:aptr-write-uint16")
			{
				EmitAptrWrite(2);
				return;
			}

			if (target.ImportName is
				"intrinsic:aptr-from-pointer" or
				"intrinsic:aptr-to-uint32" or
				"intrinsic:amiga-vararg-from-value" or
				"intrinsic:address-of-ref" or
				"intrinsic:address-to-ref" or
				"intrinsic:ref-cast" or
				"intrinsic:hook-address-of" or
				"intrinsic:boopsi-message-address-of")
			{
				return;
			}

			if (target.ImportName == "intrinsic:bptr-address")
			{
				if (target.Signature.Header.IsInstance)
				{
					EmitPopRegister(M68kRegister.A0);
					_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
				}
				else
				{
					EmitPopD0();
				}
				_assembler.EmitWord(0xD080); // ADD.L D0,D0
				_assembler.EmitWord(0xD080); // ADD.L D0,D0
				if (pushResult)
				{
					EmitPushD0();
				}
				return;
			}

			if (target.ImportName == "intrinsic:bptr-from-address")
			{
				EmitPopD0();
				_assembler.EmitWord(0xE288); // LSR.L #1,D0
				_assembler.EmitWord(0xE288); // LSR.L #1,D0
				if (pushResult)
				{
					EmitPushD0();
				}
				return;
			}

			if (target.ImportName == "intrinsic:iff-handle-stream")
			{
				EmitPopRegister(M68kRegister.A0);
				_assembler.EmitWord(0x2050); // MOVEA.L (A0),A0
				_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
				if (pushResult)
				{
					EmitPushD0();
				}
				return;
			}

			if (target.ImportName == "intrinsic:iff-handle-set-stream")
			{
				EmitPopD0();
				EmitPopRegister(M68kRegister.A0);
				_assembler.EmitWord(0x2050); // MOVEA.L (A0),A0
				_assembler.EmitWord(0x2080); // MOVE.L D0,(A0)
				return;
			}

			if (target.ImportName == "intrinsic:aptr-raw")
			{
				EmitPopRegister(M68kRegister.A0);
				_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
				if (pushResult)
				{
					EmitPushD0();
				}
				return;
			}

			if (target.ImportName is "intrinsic:aptr-is-null" or "intrinsic:aptr-is-not-null")
			{
				EmitPopRegister(M68kRegister.A0);
				_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
				_assembler.EmitWord(0x4A80); // TST.L D0
				_assembler.EmitWord((ushort)(
					target.ImportName == "intrinsic:aptr-is-null"
						? 0x57C0
						: 0x56C0)); // SEQ/SNE D0
				_assembler.EmitWord(0x4400); // NEG.B D0, FF -> 1
				if (pushResult)
				{
					EmitPushByteRegister(M68kRegister.D0);
				}
				else
				{
					EmitSignExtendByteToLongD0();
				}
				return;
			}

			if (target.ImportName?.StartsWith("intrinsic:nullable-ctor:", StringComparison.Ordinal) == true)
			{
				EmitNullableConstructor(target);
				return;
			}

			if (target.ImportName?.StartsWith("intrinsic:nullable-has-value:", StringComparison.Ordinal) == true)
			{
				EmitNullableHasValue(target, pushResult);
				return;
			}

			if (target.ImportName?.StartsWith("intrinsic:nullable-get-value:", StringComparison.Ordinal) == true ||
				target.ImportName?.StartsWith(
					"intrinsic:nullable-get-value-or-default-no-argument:",
					StringComparison.Ordinal) == true)
			{
				EmitNullableGetValue(pushResult);
				return;
			}

			if (target.ImportName?.StartsWith("intrinsic:nullable-get-value-or-default:", StringComparison.Ordinal) == true)
			{
				EmitNullableGetValueOrDefault(target, pushResult);
				return;
			}

			if (target.ImportName == "intrinsic:boopsi-do-method")
			{
				EmitBoopsiDoMethod(target.ParameterCount);
				_loadedPlatformBase = null;
				return;
			}

			if (target.ImportName?.StartsWith(
				"intrinsic:amiga-library-base-set:",
				StringComparison.Ordinal) == true)
			{
				var libraryTypeName = target.ImportName["intrinsic:amiga-library-base-set:".Length..];
				EmitPopD0();
				EnsureAmigaLibraryBaseSlot(caller, libraryTypeName);
				EmitStoreD0ToLabel(AmigaLibraryBaseSlotSymbol(libraryTypeName));
				_loadedPlatformBase = null;
				return;
			}

			if (target.ImportName?.StartsWith(
				"intrinsic:amiga-library-base-get:",
				StringComparison.Ordinal) == true)
			{
				var libraryTypeName = target.ImportName["intrinsic:amiga-library-base-get:".Length..];
				EnsureAmigaLibraryBaseSlot(caller, libraryTypeName);
				EmitLoadD0FromLabel(AmigaLibraryBaseSlotSymbol(libraryTypeName));
				EmitPushD0();
				return;
			}

			if (target.ImportName == "intrinsic:runtime-dispose")
			{
				EmitPopRegister(M68kRegister.A0);
				EmitRuntimeJsr(RuntimeDisposeLabel, M68kRuntimeImports.Dispose);
				_loadedPlatformBase = null;
				return;
			}

			if (target.ImportName == "intrinsic:runtime-gc-collect")
			{
				EmitManagedCollectWithRoots();
				_loadedPlatformBase = null;
				return;
			}

			if (target.ImportName == "intrinsic:runtime-GetGcStaleBytes")
			{
				EmitRuntimeJsr(RuntimeGetStaleBytesTarget, M68kRuntimeImports.GcGetStaleBytes);
				_loadedPlatformBase = null;
				if (pushResult)
				{
					EmitPushD0();
				}
				return;
			}

			if (target.ImportName == "intrinsic:runtime-GetGcStaleBlocks")
			{
				EmitRuntimeJsr(RuntimeGetStaleBlocksTarget, M68kRuntimeImports.GcGetStaleBlocks);
				_loadedPlatformBase = null;
				if (pushResult)
				{
					EmitPushD0();
				}
				return;
			}

			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Unresolved call target.",
				caller.DisplayName,
				instruction.Offset);
		}

		ValidateMethodSignature(target.Definition, isEntry: false, isExport: false);
		if (TryEmitNativeShadowMathCall(target.Definition, pushResult))
		{
			return;
		}
		if (instruction.OpCode == OpCodes.Callvirt &&
			target.Definition.Signature.Header.IsInstance)
		{
			EmitRequireCallReceiverNonNull(target.Signature);
		}
		if (target.Definition.ExternalCall is { } externalCall)
		{
			EmitExternalCall(target.Definition, externalCall);
		}
		else if (target.Definition.IsImport)
		{
			if (target.Definition.ImportAbi is { } importAbi)
			{
				var stackOffset = 0;
				for (var index = importAbi.ParameterRegisters.Count - 1; index >= 0; index--)
				{
					var type = target.Definition.Signature.ParameterTypes[index];
					if (Is64BitScalar(type))
					{
						var highRegister = importAbi.ParameterRegisters[index];
						var lowRegister = NextDataRegister(highRegister, target.Definition.DisplayName);
						EmitLoadRegisterFromStack(lowRegister, stackOffset);
						EmitLoadRegisterFromStack(highRegister, checked(stackOffset + 4));
						stackOffset += 8;
					}
					else
					{
						EmitLoadRegisterFromStack(importAbi.ParameterRegisters[index], stackOffset);
						stackOffset += 4;
					}
				}
			}

			_assembler.EmitJsr(target.Definition.ImportName!, external: true);
			_loadedPlatformBase = null;
			if (target.Definition.ImportAbi is { } registerAbi &&
				!target.Signature.ReturnType.IsVoid &&
				!Is64BitScalar(target.Signature.ReturnType))
			{
				EmitMoveRegisterToD0(registerAbi.ReturnRegister);
			}
		}
		else
		{
			var internalAbi = GetInternalCallAbi(target.Definition);
			if (target.Definition.DeclaringTypeIsInterface)
			{
				EmitInterfaceCall(target.Definition, internalAbi);
			}
			else if (RequiresVirtualDispatch(instruction, target.Definition))
			{
				EmitVirtualCall(target.Definition, internalAbi);
			}
			else
			{
				EmitPrepareInternalCall(target.Definition, internalAbi);
				if (!IsAlwaysInlinedMethod(target.Definition))
				{
					_assembler.EmitBsr(MethodLabel(target.Definition));
					_loadedPlatformBase = null;
				}
			}
		}

		if (target.Definition.IsImport || target.Definition.ExternalCall is not null)
		{
			EmitDiscardStackArguments(ParameterSlotLongs(target.Definition.Signature.ParameterTypes));
		}
		else
		{
			EmitReleaseStackBytes(GetInternalCallAbi(target.Definition).StackBytes);
		}
		if (pushResult && !target.Signature.ReturnType.IsVoid)
		{
			if (target.Signature.ReturnType.IsNullable)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					"Nullable call results must be stored directly to a supported nullable local before use.",
					caller.DisplayName,
					instruction.Offset);
			}

			if (Is64BitScalar(target.Signature.ReturnType))
			{
				var returnRegister = target.Definition.IsImport
					? target.Definition.ImportAbi?.ReturnRegister ?? M68kRegister.D0
					: target.Definition.ExternalCall?.Abi.ReturnRegister ?? M68kRegister.D0;
				var lowRegister = NextDataRegister(returnRegister, target.Definition.DisplayName);
				EmitPushRegister(returnRegister);
				EmitPushRegister(lowRegister);
			}
			else if (!target.Definition.IsImport &&
				IsInternalAddressReturn(target.Definition.Signature.ReturnType))
			{
				_assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
			}
			else
			{
				EmitPushD0();
			}
		}
	}

	private bool TryEmitNativeShadowMathCall(CilMethod definition, bool pushResult)
	{
		if (!IsNativeShadowMathLeaf(definition))
		{
			return false;
		}

		var operation = definition.DisplayName switch
		{
			"CopperSharp.Runtime.ShadowMath::Sqrt" => M68kFpuOperation.SquareRoot,
			"CopperSharp.Runtime.ShadowMath::Truncate" => M68kFpuOperation.TruncateToInteger,
			_ => throw new InvalidOperationException("Unknown native Math leaf.")
		};

		EmitPopRegister(M68kRegister.D1); // low word
		EmitPopRegister(M68kRegister.D0); // high word
		EmitAllocateFrame(8);
		_assembler.EmitWord(0x2E80); // MOVE.L D0,(A7)
		_assembler.EmitWord(0x2F41); // MOVE.L D1,4(A7)
		_assembler.EmitWord(4);
		_assembler.EmitFpuStackToRegister(0, M68kFpuFormat.Double);
		_assembler.EmitFpuUnaryOperation(0, operation);
		_assembler.EmitFpuRegisterToStack(0, M68kFpuFormat.Double);
		_assembler.EmitWord(0x2017); // MOVE.L (A7),D0
		_assembler.EmitWord(0x222F); // MOVE.L 4(A7),D1
		_assembler.EmitWord(4);
		EmitReleaseStackBytes(8);
		if (pushResult)
		{
			EmitPushRegister(M68kRegister.D0);
			EmitPushRegister(M68kRegister.D1);
		}
		return true;
	}

	private void EmitRequireCallReceiverNonNull(MethodSignature<CilType> signature)
	{
		var receiverOffset = checked(ParameterSlotLongs(signature.ParameterTypes) * 4);
		EmitLoadRegisterFromStack(M68kRegister.D0, receiverOffset);
		EmitRequireNonNull();
	}

	private void EmitVirtualCall(
		CilMethod declaration,
		InternalCallAbi internalAbi)
	{
		var slot = _module.GetVirtualSlot(declaration);
		if (slot > short.MaxValue / 4)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				$"Virtual slot {slot} exceeds the indexed vtable displacement range.",
				declaration.DisplayName);
		}

		var saveOffset = checked((short)(
			FrameDisplacement(CurrentFrameLayout.DirectCallScratchOffset, _currentStackDepth) +
			internalAbi.StackBytes));
		EmitStoreRegisterToFrame(M68kRegister.A2, saveOffset);
		EmitPrepareInternalCall(declaration, internalAbi);
		_assembler.EmitWord(0x2450); // MOVEA.L (A0),A2 descriptor
		_assembler.EmitWord(0x246A); // MOVEA.L descriptor-vtable(A2),A2
		_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.TypeVirtualTableOffset));
		if (slot == 0)
		{
			_assembler.EmitWord(0x2452); // MOVEA.L (A2),A2 target
		}
		else
		{
			_assembler.EmitWord(0x246A); // MOVEA.L d16(A2),A2 target
			_assembler.EmitWord(checked((ushort)(slot * 4)));
		}
		_assembler.EmitWord(0x4E92); // JSR (A2)
		EmitLoadRegisterFromStack(
			M68kRegister.A2,
			checked((short)(
				CurrentFrameLayout.DirectCallScratchOffset +
				(internalAbi.StackBytes * 2))));
		_loadedPlatformBase = null;
	}

	private void EmitInterfaceCall(
		CilMethod declaration,
		InternalCallAbi internalAbi)
	{
		var interfaceDefinition = _module.GetInterfaceDefinition(declaration);
		_usedInterfaces.TryAdd(interfaceDefinition.Identity, interfaceDefinition);
		var slot = _module.GetInterfaceSlot(declaration);
		if (slot > short.MaxValue / 4)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				$"Interface slot {slot} exceeds the indexed method-table displacement range.",
				declaration.DisplayName);
		}

		var saveOffset = checked((short)(
			FrameDisplacement(CurrentFrameLayout.DirectCallScratchOffset, _currentStackDepth) +
			internalAbi.StackBytes));
		EmitStoreRegisterToFrame(M68kRegister.D2, saveOffset);
		EmitStoreRegisterToFrame(M68kRegister.A2, checked((short)(saveOffset + 4)));
		EmitStoreRegisterToFrame(M68kRegister.A3, checked((short)(saveOffset + 8)));
		EmitPrepareInternalCall(declaration, internalAbi);
		_assembler.EmitWord(0x2450); // MOVEA.L (A0),A2 descriptor
		_assembler.EmitWord(0x246A); // MOVEA.L descriptor-interface-map(A2),A2
		_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.TypeInterfaceMapOffset));
		_assembler.EmitWord(0x241A); // MOVE.L (A2)+,D2 entry count
		EmitAddressImmediateToRegister(
			M68kRegister.A3,
			InterfaceIdentityLabel(interfaceDefinition));

		var loop = UniqueLabel("interface_lookup");
		var found = UniqueLabel("interface_found");
		_assembler.Mark(loop);
		_assembler.EmitWord(0xB7DA); // CMPA.L (A2)+,A3 interface identity
		_assembler.EmitBranch(M68kCondition.Equal, found);
		_assembler.EmitWord(0x588A); // ADDQ.L #4,A2 skip method-table pointer
		_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
		_assembler.EmitBranch(M68kCondition.NotEqual, loop);
		_assembler.EmitWord(0x4AFC); // ILLEGAL: invalid object/interface pairing

		_assembler.Mark(found);
		_assembler.EmitWord(0x2452); // MOVEA.L (A2),A2 method table
		if (slot == 0)
		{
			_assembler.EmitWord(0x2452); // MOVEA.L (A2),A2 target
		}
		else
		{
			_assembler.EmitWord(0x246A); // MOVEA.L d16(A2),A2 target
			_assembler.EmitWord(checked((ushort)(slot * 4)));
		}
		_assembler.EmitWord(0x4E92); // JSR (A2)
		var restoreOffset = checked((short)(
			CurrentFrameLayout.DirectCallScratchOffset +
			(internalAbi.StackBytes * 2)));
		EmitLoadRegisterFromStack(M68kRegister.D2, restoreOffset);
		EmitLoadRegisterFromStack(M68kRegister.A2, checked((short)(restoreOffset + 4)));
		EmitLoadRegisterFromStack(M68kRegister.A3, checked((short)(restoreOffset + 8)));
		_loadedPlatformBase = null;
	}

	private void EmitCStringFromLiteral(
		CilMethod caller,
		CilInstruction instruction,
		string intrinsicName = "intrinsic:cstring-from-literal")
	{
		var index = -1;
		for (var candidate = 0; candidate < caller.Instructions.Count; candidate++)
		{
			if (caller.Instructions[candidate].Offset == instruction.Offset)
			{
				index = candidate;
				break;
			}
		}

		if (index <= 0 ||
			caller.Instructions[index - 1] is not { OpCode: var previousOp, Operand: int token } ||
			previousOp != OpCodes.Ldstr)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				intrinsicName == "intrinsic:amiga-vararg-from-literal"
					? "Implicit AmigaVarArg string conversion requires a string literal argument."
					: "CString.FromLiteral requires a string literal argument.",
				caller.DisplayName,
				instruction.Offset);
		}

		var identity = new CilUserStringIdentity(caller.ModuleName, token);
		_cStringLiterals.TryAdd(identity, _module.GetUserString(token, caller, instruction.Offset));
		_assembler.EmitWord(0x2F3C); // MOVE.L #cstring,-(A7)
		_assembler.EmitAddress(CStringLabel(identity));
	}

	private bool IsNextCStringFromLiteralCall(CilMethod caller, CilInstruction instruction)
	{
		for (var index = 0; index + 1 < caller.Instructions.Count; index++)
		{
			if (caller.Instructions[index].Offset != instruction.Offset)
			{
				continue;
			}

			var next = caller.Instructions[index + 1];
			if (next.OpCode != OpCodes.Call && next.OpCode != OpCodes.Callvirt)
			{
				return false;
			}

			var target = _module.ResolveMethodToken(
				(int)next.Operand!,
				caller,
				next.Offset);
			return IsCStringLiteralIntrinsic(target.ImportName);
		}

		return false;
	}

	private void EmitExportAddress(CilMethod caller, CilInstruction instruction, bool pushResult)
	{
		var index = -1;
		for (var candidate = 0; candidate < caller.Instructions.Count; candidate++)
		{
			if (caller.Instructions[candidate].Offset == instruction.Offset)
			{
				index = candidate;
				break;
			}
		}

		if (index <= 0 ||
			caller.Instructions[index - 1] is not { OpCode: var previousOp, Operand: int token } ||
			previousOp != OpCodes.Ldstr)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"APTR.ExportAddress requires an export name string literal.",
				caller.DisplayName,
				instruction.Offset);
		}

		if (!pushResult)
		{
			return;
		}

		var exportName = _module.GetUserString(token, caller, instruction.Offset);
		if (!_module.GetExports().Any(export => export.Name == exportName))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnresolvedImport,
				$"No [M68kExport] method named '{exportName}' exists.",
				caller.DisplayName,
				instruction.Offset);
		}

		_assembler.EmitWord(0x2F3C); // MOVE.L #export,-(A7)
		_assembler.EmitAddress(ExportLabel(exportName));
	}

	private void EmitAptrReadUInt32(bool pushResult)
	{
		EmitPopD0(); // byte offset
		EmitPopRegister(M68kRegister.A0); // guest address
		_assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		if (pushResult)
		{
			EmitPushD0();
		}
	}

	private void EmitAptrRead(int size, bool pushResult)
	{
		EmitPopD0(); // byte offset
		EmitPopRegister(M68kRegister.A0); // guest address
		_assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.EmitWord(size == 1 ? (ushort)0x1010 : (ushort)0x3010); // MOVE.B/W (A0),D0
		if (pushResult)
		{
			EmitPushD0();
		}
	}

	private void EmitBoopsiInstanceData(bool pushResult)
	{
		EmitPopRegister(M68kRegister.A0); // object
		EmitPopRegister(M68kRegister.A1); // class
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.EmitWord(0x3029); // MOVE.W cl_InstOffset(A1),D0
		_assembler.EmitWord(0x0020);
		_assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		if (pushResult)
		{
			EmitPushRegister(M68kRegister.A0);
		}
	}

	private void EmitAptrWriteUInt32()
	{
		EmitPopD0(); // value
		EmitPopRegister(M68kRegister.D1); // byte offset
		EmitPopRegister(M68kRegister.A0); // guest address
		_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
		_assembler.EmitWord(0x2080); // MOVE.L D0,(A0)
	}

	private void EmitAptrWrite(int size)
	{
		EmitPopD0(); // value
		EmitPopRegister(M68kRegister.D1); // byte offset
		EmitPopRegister(M68kRegister.A0); // guest address
		_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
		_assembler.EmitWord(size == 1 ? (ushort)0x1080 : (ushort)0x3080); // MOVE.B/W D0,(A0)
	}

	private bool IsNextExportAddressCall(CilMethod caller, CilInstruction instruction)
	{
		for (var index = 0; index + 1 < caller.Instructions.Count; index++)
		{
			if (caller.Instructions[index].Offset != instruction.Offset)
			{
				continue;
			}

			var next = caller.Instructions[index + 1];
			if (next.OpCode != OpCodes.Call && next.OpCode != OpCodes.Callvirt)
			{
				return false;
			}

			var target = _module.ResolveMethodToken(
				(int)next.Operand!,
				caller,
				next.Offset);
			return target.ImportName == "intrinsic:aptr-export-address";
		}

		return false;
	}

	private void EmitBoopsiDoMethod(int parameterCount)
	{
		var argumentCount = checked(parameterCount - 2);
		if (argumentCount is < 0 or > 6)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"BOOPSI.DoMethod supports zero to six method arguments.");
		}

		var messageLongs = checked(argumentCount + 1);
		EmitLoadRegisterFromStack(
			M68kRegister.A0,
			checked(messageLongs * 4));
		for (var index = 0; index < messageLongs; index++)
		{
			EmitLoadRegisterFromStack(
				(M68kRegister)((int)M68kRegister.D0 + index),
				checked((messageLongs - 1 - index) * 4));
		}

		for (var index = 0; index < messageLongs; index++)
		{
			EmitStoreRegisterToStack(
				(M68kRegister)((int)M68kRegister.D0 + index),
				checked(index * 4));
		}

		_assembler.EmitWord(0x224F); // MOVEA.L A7,A1
		_assembler.EmitJsr("amiga.boopsi.DoMethodA", external: true);
		EmitDiscardStackArguments(parameterCount);
		EmitPushD0();
	}

	private void EmitManagedCollectWithRoots(int additionalStackBytes = 0)
	{
		_assembler.EmitBsr(RuntimeCollectWithRootsLabel);
		RegisterCurrentUnwindSite(
			exception: false,
			gc: true,
			additionalStackBytes: additionalStackBytes);
	}

	private bool IsReferenceParameter(CilMethod method, int index)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (index == 0)
			{
				var declaringType = new CilType(
					CilTypeKind.ValueType,
					4,
					method.DisplayName.Split("::", StringSplitOptions.None)[0]);
				return !_module.IsTransparentScalarType(declaringType);
			}

			index--;
		}

		return method.Signature.ParameterTypes[index].IsReference;
	}

	private bool IsSupportedScalarParameter(CilMethod method, int index)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (index == 0)
			{
				var declaringType = new CilType(
					CilTypeKind.ValueType,
					4,
					method.DisplayName.Split("::", StringSplitOptions.None)[0]);
				return _module.IsTransparentScalarType(declaringType);
			}

			index--;
		}

		var type = method.Signature.ParameterTypes[index];
		return type.IsSupportedScalar ||
			_module.IsTransparentScalarType(type);
	}

	private bool IsTransparentScalarArgument(CilMethod method, int index)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (index == 0)
			{
				var declaringType = new CilType(
					CilTypeKind.ValueType,
					4,
					method.DisplayName.Split("::", StringSplitOptions.None)[0]);
				return _module.IsTransparentScalarType(declaringType);
			}

			index--;
		}

		return _module.IsTransparentScalarType(method.Signature.ParameterTypes[index]);
	}

	private bool IsSupportedNullableArgument(CilMethod method, int index)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (index == 0)
			{
				return false;
			}

			index--;
		}

		return _module.IsSupportedNullableType(method.Signature.ParameterTypes[index]);
	}

	private bool IsCompactNullableType(CilType type) =>
		type.NullableElementType is { } element &&
		_module.IsTransparentScalarType(element);

	private bool IsCompactNullableIntrinsic(MethodReference target) =>
		target.ImportName?.StartsWith("intrinsic:nullable-", StringComparison.Ordinal) == true &&
		target.ImportName.LastIndexOf(':') is var separator &&
		separator >= 0 &&
		_module.IsTransparentScalarType(new CilType(
			CilTypeKind.ValueType,
			4,
			target.ImportName[(separator + 1)..]));

	private int SlotLongs(CilType type) =>
		Is64BitScalar(type) || type.IsNullable && !IsCompactNullableType(type)
			? 2
			: _module.IsSupportedStructType(type)
				? _module.GetStructSlotLongs(type)
				: 1;

	private CilStackValueKind StackKindForType(CilType type) =>
		type.Size == 1 && type.Kind == CilTypeKind.Boolean
			? CilStackValueKind.BooleanByte
			: type.Size == 1 && type.Kind == CilTypeKind.SignedInteger
				? CilStackValueKind.SignedByte
			: type.Size == 1 && type.Kind == CilTypeKind.UnsignedInteger
					? CilStackValueKind.UnsignedByte
				: type.Size == 2 && (type.Kind == CilTypeKind.Character ||
					type.Kind == CilTypeKind.UnsignedInteger)
					? CilStackValueKind.UnsignedWord
				: type.Size == 2 && type.Kind == CilTypeKind.SignedInteger
					? CilStackValueKind.SignedWord
				: type.Kind switch
					{
						CilTypeKind.ManagedReference => CilStackValueKind.Reference,
						CilTypeKind.ManagedPointer => CilStackValueKind.ManagedPointer,
						_ => CilStackValueKind.Int32
					};

	private CilStackValueKind CurrentStackKind(int fromTop = 0)
	{
		if ((uint)fromTop >= (uint)_currentStackTypes.Length)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidEvaluationStack,
				"The generated stack operation has no value of the requested type.");
		}

		return _currentStackTypes[_currentStackTypes.Length - 1 - fromTop];
	}

	private CilStackValueKind CurrentStackKindOrLong(int fromTop = 0) =>
		(uint)fromTop < (uint)_currentStackTypes.Length
			? CurrentStackKind(fromTop)
			: CilStackValueKind.Int32;

	private void EmitPushStackValue(
		M68kRegister register,
		CilStackValueKind kind)
	{
		if (CilStackValueLayout.IsByte(kind))
		{
			EmitPushByteRegister(register);
		}
		else
		{
			EmitPushRegister(register);
		}
	}

	private void EmitPopStackValue(
		M68kRegister register,
		CilStackValueKind kind,
		bool widen = true)
	{
		if (CilStackValueLayout.IsByte(kind))
		{
			EmitPopByteRegister(register, kind, widen);
		}
		else
		{
			EmitPopRegister(register);
		}
	}

	private static int EvaluationSlotLongs(CilType type) =>
		Is64BitScalar(type) ? 2 : type.IsVoid ? 0 : 1;

	private int ParameterSlotLongs(ImmutableArray<CilType> parameterTypes)
	{
		var result = 0;
		foreach (var parameter in parameterTypes)
		{
			result += SlotLongs(parameter);
		}
		return result;
	}

	private bool IsNativeShadowMathLeaf(CilMethod method) =>
		_request.FloatingPoint is
			M68kFloatingPointMode.M68040 or M68kFloatingPointMode.M68882 &&
		method.DisplayName is
			"CopperSharp.Runtime.ShadowMath::Sqrt" or
			"CopperSharp.Runtime.ShadowMath::Truncate" &&
		method.Signature.ParameterTypes is [{ IsFloatingPoint: true, Size: 8 }] &&
		method.Signature.ReturnType is { IsFloatingPoint: true, Size: 8 };

	private int ArgumentSlotLongs(CilMethod method, int argumentIndex) =>
		SlotLongs(TypeForArgument(method, argumentIndex));

	private int InternalStackArgumentSlotLongs(CilMethod method)
	{
		var result = 0;
		for (var index = 0; index < method.ParameterCount; index++)
		{
			result += ArgumentSlotLongs(method, index);
		}
		return result;
	}

	private int InternalStackArgumentBytes(CilMethod method) =>
		checked(InternalStackArgumentSlotLongs(method) * 4);

	private int InternalStackArgumentByteOffset(CilMethod method, int argumentIndex)
	{
		var result = 0;
		for (var index = 0; index < argumentIndex; index++)
		{
			result += ArgumentSlotLongs(method, index) * 4;
		}
		return result;
	}

	private int InternalStackArgumentBytesAfter(CilMethod method, int argumentIndex) =>
		checked(InternalStackArgumentBytes(method) -
			InternalStackArgumentByteOffset(method, argumentIndex) -
			(ArgumentSlotLongs(method, argumentIndex) * 4));

	private static CilType TypeForArgument(CilMethod method, int index)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (index == 0)
			{
				return new CilType(
					CilTypeKind.ManagedReference,
					4,
					method.DisplayName.Split("::", StringSplitOptions.None)[0]);
			}

			index--;
		}

		return method.Signature.ParameterTypes[index];
	}

	private static M68kRegister NextDataRegister(M68kRegister register, string methodName)
	{
		if (register < M68kRegister.D0 || register >= M68kRegister.D7)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"64-bit register-pair values must start in D0-D6.",
				methodName);
		}

		return register + 1;
	}

	private void EmitExternalCall(CilMethod method, CilExternalCall call)
	{
		var binding = call.Convention;
		EmitEnsurePlatformBase(binding, method);
		EmitExternalCallArguments(method, call);
		EmitBaseRelativeJsr(binding.BaseRegister, binding.Displacement);
		EmitExternalExceptionStatusCheck(binding);
		if (!method.Signature.ReturnType.IsVoid && !Is64BitScalar(method.Signature.ReturnType))
		{
			EmitMoveRegisterToD0(call.Abi.ReturnRegister);
		}
	}

	private void EmitExternalExceptionStatusCheck(
		M68kExternalCallConvention binding,
		M68kRegister? capturedStatusRegister = null)
	{
		if (binding.ExceptionPolicy != M68kExternalExceptionPolicy.NonZeroStatus ||
			binding.ExceptionStatusRegister is not { } statusRegister)
		{
			return;
		}

		var success = UniqueLabel("external_call_success");
		var effectiveStatusRegister = capturedStatusRegister ?? statusRegister;
		_assembler.EmitWord((ushort)(0x4A80 | (int)effectiveStatusRegister)); // TST.L Dn
		_assembler.EmitBranch(M68kCondition.Equal, success);
		EmitExceptionRaise(reason: 5, hasException: false);
		_assembler.Mark(success);
	}

	private void EmitExternalCallArguments(
		CilMethod method,
		CilExternalCall call,
		int initialStackOffset = 0)
	{
		var binding = call.Convention;
		var cacheRegister = binding.CacheRegister;
		var preservePlatformCache = cacheRegister is not null &&
			(call.Abi.ReturnRegister == cacheRegister ||
			 call.Abi.ParameterRegisters.Contains(cacheRegister.Value));
		if (preservePlatformCache)
		{
			EmitPushRegister(cacheRegister!.Value);
		}
		var stackOffset = initialStackOffset + (preservePlatformCache ? 4 : 0);
		for (var index = call.Abi.ParameterRegisters.Count - 1; index >= 0; index--)
		{
			var type = method.Signature.ParameterTypes[index];
			if (Is64BitScalar(type))
			{
				var highRegister = call.Abi.ParameterRegisters[index];
				var lowRegister = NextDataRegister(highRegister, method.DisplayName);
				EmitLoadRegisterFromStack(lowRegister, stackOffset);
				EmitLoadRegisterFromStack(highRegister, checked(stackOffset + 4));
				stackOffset += 8;
			}
			else
			{
				EmitLoadRegisterFromStack(call.Abi.ParameterRegisters[index], stackOffset);
				stackOffset += 4;
			}
		}
		if (preservePlatformCache)
		{
			EmitPopRegister(cacheRegister!.Value);
		}
	}

	private void EmitEnsurePlatformBase(
		M68kExternalCallConvention binding,
		CilMethod method)
	{
		if (binding.BaseSource == M68kExternalBaseSource.Argument)
		{
			// Argument setup supplies the dynamic base. It also replaces any base
			// identity previously known to be resident in the register.
			_loadedPlatformBase = null;
			return;
		}

		var platformBase = GetOrAddPlatformBase(binding, method);
		if (_loadedPlatformBase != platformBase)
		{
			switch (binding.BaseSource)
			{
				case M68kExternalBaseSource.CachedPointer:
					EmitMoveRegister(
						binding.CacheRegister ??
							throw new M68kCompilationException(
								M68kDiagnosticIds.InvalidMetadata,
								"Cached platform bases require a cache register.",
								method.DisplayName),
						binding.BaseRegister);
					break;
				case M68kExternalBaseSource.WritableSlot:
					EmitLoadAddressRegisterPcRelative(binding.BaseRegister, platformBase.Label!);
					break;
				case M68kExternalBaseSource.Immediate:
					EmitLoadAddressRegisterImmediate(binding.BaseRegister, binding.InitialValue);
					break;
				default:
					throw new InvalidOperationException(
						$"Unknown platform base source {binding.BaseSource}.");
			}
			_loadedPlatformBase = platformBase;
		}
	}

	private GeneratedPlatformBase GetOrAddPlatformBase(
		M68kExternalCallConvention binding,
		CilMethod method)
	{
		if (_usedPlatformBases.TryGetValue(binding.Identity, out var existing))
		{
			if (!PlatformBasesMatch(existing.Binding, binding))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Platform base '{binding.Identity}' has conflicting declarations.",
					method.DisplayName);
			}
			return existing;
		}

		if (binding.BaseSource == M68kExternalBaseSource.WritableSlot &&
			_request.OutputFormat == M68kOutputFormat.KickstartRom)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				$"Writable platform base storage for '{binding.Identity}' would be placed in read-only ROM.",
				method.DisplayName);
		}

		if (binding.BaseRegister < M68kRegister.A0 ||
			binding.CacheRegister is { } cache && cache < M68kRegister.A0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Platform base and cache registers must be address registers.",
				method.DisplayName);
		}

		var generated = new GeneratedPlatformBase(
			binding,
			binding.BaseSource == M68kExternalBaseSource.WritableSlot
				? binding.SlotSymbol
				: null);
		if (binding.BaseSource == M68kExternalBaseSource.WritableSlot &&
			string.IsNullOrWhiteSpace(binding.SlotSymbol))
		{
			throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidMetadata,
						"Writable platform bases require a slot symbol.",
						method.DisplayName);
		}
		_usedPlatformBases.Add(binding.Identity, generated);
		return generated;
	}

	private void EnsureAmigaLibraryBaseSlot(CilMethod method, string libraryTypeName) =>
		GetOrAddPlatformBase(
			new M68kExternalCallConvention(
				AmigaLibraryName(libraryTypeName),
				M68kExternalBaseSource.WritableSlot,
				M68kRegister.A6,
				0,
				SlotSymbol: AmigaLibraryBaseSlotSymbol(libraryTypeName)),
			method);

	private static string AmigaLibraryName(string libraryTypeName) =>
		libraryTypeName == "TimerDevice"
			? "timer.device"
			: $"{libraryTypeName.ToLowerInvariant()}.library";

	private static string AmigaLibraryBaseSlotSymbol(string libraryTypeName) =>
		$"_{(libraryTypeName == "IffParse" ? "IFFParse" : libraryTypeName)}LibraryBase";

	private static bool PlatformBasesMatch(
		M68kExternalCallConvention left,
		M68kExternalCallConvention right) =>
		left.Identity == right.Identity &&
		left.BaseSource == right.BaseSource &&
		left.BaseRegister == right.BaseRegister &&
		left.CacheRegister == right.CacheRegister &&
		left.SourceAddress == right.SourceAddress &&
		left.InitialValue == right.InitialValue &&
		left.SlotSymbol == right.SlotSymbol;

	private bool TryEmitTailCall(CilMethod caller, CilInstruction instruction)
	{
		if (CurrentFrameLayout.HasRuntimeFrame)
		{
			return false;
		}

		var target = _module.ResolveMethodToken((int)instruction.Operand!, caller, instruction.Offset);
		if (target.Definition is { } virtualDefinition &&
			RequiresVirtualDispatch(instruction, virtualDefinition))
		{
			return false;
		}
		if (target.Definition?.ExternalCall is { } externalCall)
		{
			return TryEmitExternalTailCall(caller, target.Definition, externalCall);
		}

		if (target.Definition is not { IsImport: false } callee ||
			GetInternalRegisterAbi(callee) is not { } registerAbi ||
			callee.Signature.ReturnType.Kind == CilTypeKind.GenericParameter ||
			caller.Signature.ReturnType.IsVoid != target.Signature.ReturnType.IsVoid ||
			(!caller.Signature.ReturnType.IsVoid &&
			 IsInternalAddressReturn(caller.Signature.ReturnType) !=
			 IsInternalAddressReturn(target.Signature.ReturnType)))
		{
			return false;
		}

		ValidateMethodSignature(callee, isEntry: false, isExport: false);
		if (_loadedPlatformBase is { } activePlatformBase &&
			(!_platformBaseMethodEntries.TryGetValue(caller.Identity, out var entryIdentity) ||
			 entryIdentity != activePlatformBase.Binding.Identity))
		{
			return false;
		}
		EmitLoadRegistersFromEvaluationStack(registerAbi);
		EmitRestoreCalleeSavedRegisters();
		EmitReleaseStackBytes(checked((registerAbi.Count * 4) + CurrentFrameLayout.FrameBytes));
		_assembler.EmitJmp(MethodLabel(callee), external: false);
		return true;
	}

	private bool TryEmitExternalTailCall(
		CilMethod caller,
		CilMethod callee,
		CilExternalCall externalCall)
	{
		if (!CanEmitExternalTailCall(caller, callee, externalCall))
		{
			return false;
		}

		ValidateMethodSignature(callee, isEntry: false, isExport: false);
		EmitRestoreCalleeSavedRegisters();
		EmitExternalCallArguments(callee, externalCall);
		EmitEnsurePlatformBase(externalCall.Convention, callee);
		EmitReleaseStackBytes(checked((ParameterSlotLongs(callee.Signature.ParameterTypes) * 4) + CurrentFrameLayout.FrameBytes));
		EmitBaseRelativeJmp(
			externalCall.Convention.BaseRegister,
			externalCall.Convention.Displacement);
		_loadedPlatformBase = null;
		return true;
	}

	private static bool CanEmitExternalTailCall(
		CilMethod caller,
		CilMethod callee,
		CilExternalCall externalCall)
	{
		if (callee.Signature.ReturnType.IsNullable ||
			Is64BitScalar(callee.Signature.ReturnType) ||
			caller.Signature.ReturnType.IsVoid != callee.Signature.ReturnType.IsVoid ||
			(!caller.Signature.ReturnType.IsVoid &&
			 IsInternalAddressReturn(caller.Signature.ReturnType) !=
			 IsInternalAddressReturn(callee.Signature.ReturnType)) ||
			(!callee.Signature.ReturnType.IsVoid &&
				externalCall.Abi.ReturnRegister != M68kRegister.D0) ||
			externalCall.Convention.ExceptionPolicy != M68kExternalExceptionPolicy.None)
		{
			return false;
		}

		if (IsInternalCalleeSavedRegister(externalCall.Convention.BaseRegister) ||
			externalCall.Convention.CacheRegister is { } cacheRegister &&
				IsInternalCalleeSavedRegister(cacheRegister) ||
			externalCall.Abi.ParameterRegisters.Any(IsInternalCalleeSavedRegister) ||
			IsInternalCalleeSavedRegister(externalCall.Abi.ReturnRegister))
		{
			return false;
		}

		var optionalCacheRegister = externalCall.Convention.CacheRegister;
		return optionalCacheRegister is null ||
			(externalCall.Abi.ReturnRegister != optionalCacheRegister &&
			 !externalCall.Abi.ParameterRegisters.Contains(optionalCacheRegister.Value));
	}

	private void EmitFrameTeardown(CilMethod method)
	{
		EmitRestoreCalleeSavedRegisters();
		EmitUnlinkRuntimeFrame();
		EmitReleaseFrame(CurrentFrameLayout.FrameBytes);
	}

	private void EmitSaveCalleeSavedRegisters()
	{
		if (ShouldUseMovemForFrameCalleeSaves(
			_request.Cpu,
			CurrentFrameLayout.CalleeSavedRegisters.Length))
		{
			EmitStoreRegistersToFrame(
				CurrentFrameLayout.CalleeSavedRegisters.AsSpan(),
				CurrentFrameLayout.CalleeSaveOffsets[0]);
			return;
		}

		for (var index = 0; index < CurrentFrameLayout.CalleeSavedRegisters.Length; index++)
		{
			EmitStoreRegisterToFrame(
				CurrentFrameLayout.CalleeSavedRegisters[index],
				CurrentFrameLayout.CalleeSaveOffsets[index]);
		}
	}

	private void EmitRestoreCalleeSavedRegisters()
	{
		if (ShouldUseMovemForFrameCalleeSaves(
			_request.Cpu,
			CurrentFrameLayout.CalleeSavedRegisters.Length))
		{
			EmitLoadRegistersFromFrame(
				CurrentFrameLayout.CalleeSavedRegisters.AsSpan(),
				CurrentFrameLayout.CalleeSaveOffsets[0]);
			return;
		}

		for (var index = 0; index < CurrentFrameLayout.CalleeSavedRegisters.Length; index++)
		{
			EmitLoadRegisterFromStack(
				CurrentFrameLayout.CalleeSavedRegisters[index],
				CurrentFrameLayout.CalleeSaveOffsets[index]);
		}
	}

	internal static bool ShouldUseMovemForFrameCalleeSaves(
		M68kCpuTarget cpu,
		int registerCount)
	{
		// On a 68000, frame-resident MOVEM is already cycle-neutral or faster at
		// two registers and becomes increasingly better as the register count grows.
		// On 68020+, cached individual MOVE.L instructions are faster; keep the
		// speed-first path there. Prologues and epilogues are outside natural loops,
		// so loop-size profitability does not apply to these frame saves.
		return cpu == M68kCpuTarget.M68000 && registerCount >= 2;
	}

	private void EmitLoadRegisterFromStack(M68kRegister register, int offset)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		if (offset > short.MaxValue)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Register-ABI import arguments exceed the indexed stack displacement range.");
		}

		if (offset == 0)
		{
			if (register <= M68kRegister.D7)
			{
				_assembler.EmitWord((ushort)(0x2017 | ((int)register << 9))); // MOVE.L (A7),Dn
			}
			else
			{
				var addressRegister = (int)register - (int)M68kRegister.A0;
				_assembler.EmitWord((ushort)(0x2057 | (addressRegister << 9))); // MOVEA.L (A7),An
			}
			return;
		}

		if (register <= M68kRegister.D7)
		{
			// MOVE.L d16(A7),Dn
			_assembler.EmitWord((ushort)(0x202F | ((int)register << 9)));
		}
		else
		{
			// MOVEA.L d16(A7),An
			var addressRegister = (int)register - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(0x206F | (addressRegister << 9)));
		}

		_assembler.EmitWord((ushort)offset);
	}

	private void EmitLoadByteFromFrame(M68kRegister register, short displacement)
	{
		if (register > M68kRegister.D7)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Byte frame loads require a data register.");
		}

		InvalidatePlatformBaseIfWritingRegister(register);
		// Frame scalar slots remain four bytes wide. The byte value occupies the
		// low byte, unlike an evaluation-stack byte slot which starts at A7.
		var lowByteDisplacement = checked((short)(displacement + 3));
		_assembler.EmitWord((ushort)(0x102F | ((int)register << 9))); // MOVE.B d16(A7),Dn
		_assembler.EmitWord(unchecked((ushort)lowByteDisplacement));
	}

	private void EmitLoadFrameAddress(M68kRegister register, short displacement)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		if (register < M68kRegister.A0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Frame addresses can only be loaded into address registers.");
		}

		var addressRegister = (int)register - (int)M68kRegister.A0;
		if (displacement == 0)
		{
			_assembler.EmitWord((ushort)(0x204F | (addressRegister << 9))); // MOVEA.L A7,An
			return;
		}

		_assembler.EmitWord((ushort)(0x41EF | (addressRegister << 9))); // LEA d16(A7),An
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitStoreRegisterToStack(M68kRegister register, int offset)
	{
		if (offset > short.MaxValue)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Stack argument shuffling exceeds the indexed stack displacement range.");
		}

		if (offset == 0)
		{
			if (register <= M68kRegister.D7)
			{
				_assembler.EmitWord((ushort)(0x2E80 | (int)register)); // MOVE.L Dn,(A7)
			}
			else
			{
				var addressRegister = (int)register - (int)M68kRegister.A0;
				_assembler.EmitWord((ushort)(0x2E88 | addressRegister)); // MOVE.L An,(A7)
			}
			return;
		}

		if (register <= M68kRegister.D7)
		{
			// MOVE.L Dn,d16(A7)
			_assembler.EmitWord((ushort)(0x2F40 | (int)register));
		}
		else
		{
			// MOVE.L An,d16(A7)
			var addressRegister = (int)register - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(0x2F48 | addressRegister));
		}

		_assembler.EmitWord((ushort)offset);
	}

	private void EmitLoadInternalArguments(IReadOnlyList<M68kRegister> registers)
	{
		for (var index = 0; index < registers.Count; index++)
		{
			EmitLoadRegisterFromStack(
				registers[index],
				checked(index * 4));
		}
	}

	private void EmitLoadRegistersFromEvaluationStack(IReadOnlyList<M68kRegister> registers)
	{
		// CIL leaves the last argument at the top of its evaluation stack.
		for (var index = 0; index < registers.Count; index++)
		{
			EmitLoadRegisterFromStack(
				registers[index],
				checked((registers.Count - 1 - index) * 4));
		}
	}

	private int EmitPrepareInternalCall(
		CilMethod target,
		InternalCallAbi abi,
		bool receiverAlreadyLoaded = false)
	{
		var argumentBytes = checked(
			InternalStackArgumentBytes(target) - (receiverAlreadyLoaded ? 4 : 0));
		foreach (var location in abi.Arguments)
		{
			if (location.IsStack || receiverAlreadyLoaded && location.Index == 0)
			{
				continue;
			}

			var sourceOffset = InternalStackArgumentBytesAfter(target, location.Index);
			if (location.LowRegister is { } lowRegister)
			{
				EmitLoadRegisterFromStack(lowRegister, sourceOffset);
				EmitLoadRegisterFromStack(location.Register!.Value, checked(sourceOffset + 4));
			}
			else
			{
				EmitLoadRegisterFromStack(location.Register!.Value, sourceOffset);
			}
		}

		var releasedBytes = checked(argumentBytes - abi.StackBytes);
		if (abi.StackBytes != 0)
		{
			if (CurrentFrameLayout.DirectCallScratchBytes < abi.StackBytes)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidEvaluationStack,
					"The frame does not reserve enough scratch space for a hybrid internal call.",
					target.DisplayName);
			}

			var scratch = FrameDisplacement(
				CurrentFrameLayout.DirectCallScratchOffset,
				_currentStackDepth);
			foreach (var location in abi.Arguments)
			{
				if (!location.IsStack)
				{
					continue;
				}

				var sourceOffset = InternalStackArgumentBytesAfter(target, location.Index);
				for (var slot = 0; slot < location.SlotLongs; slot++)
				{
					EmitMoveFrameSlotToFrameSlot(
						checked((short)(sourceOffset + ((location.SlotLongs - 1 - slot) * 4))),
						checked((short)(scratch + location.StackOffset + (slot * 4))));
				}
			}
			for (var offset = 0; offset < abi.StackBytes; offset += 4)
			{
				EmitMoveFrameSlotToFrameSlot(
					checked((short)(scratch + offset)),
					checked((short)(releasedBytes + offset)));
			}
		}

		EmitReleaseStackBytes(releasedBytes);
		return abi.StackBytes;
	}

	private void EmitPrepareInternalCallFromDeclarationOrderedStack(
		CilMethod target,
		InternalCallAbi abi)
	{
		var argumentBytes = InternalStackArgumentBytes(target);
		foreach (var location in abi.Arguments)
		{
			if (location.IsStack)
			{
				continue;
			}

			var sourceOffset = InternalStackArgumentByteOffset(target, location.Index);
			EmitLoadRegisterFromStack(location.Register!.Value, sourceOffset);
			if (location.LowRegister is { } lowRegister)
			{
				EmitLoadRegisterFromStack(lowRegister, checked(sourceOffset + 4));
			}
		}

		var releasedBytes = checked(argumentBytes - abi.StackBytes);
		EmitAllocateFrame(abi.StackBytes);
		foreach (var location in abi.Arguments)
		{
			if (!location.IsStack)
			{
				continue;
			}

			var sourceOffset = checked(
				abi.StackBytes + InternalStackArgumentByteOffset(target, location.Index));
			for (var slot = 0; slot < location.SlotLongs; slot++)
			{
				EmitMoveFrameSlotToFrameSlot(
					checked((short)(sourceOffset + (slot * 4))),
					checked((short)(location.StackOffset + (slot * 4))));
			}
		}
		for (var offset = 0; offset < abi.StackBytes; offset += 4)
		{
			EmitMoveFrameSlotToFrameSlot(
				checked((short)offset),
				checked((short)(abi.StackBytes + releasedBytes + offset)));
		}
		EmitReleaseStackBytes(checked(abi.StackBytes + releasedBytes));
	}

	private void EmitStoreRegisterToFrame(M68kRegister register, short displacement)
	{
		if (displacement == 0)
		{
			if (register <= M68kRegister.D7)
			{
				_assembler.EmitWord((ushort)(0x2E80 | (int)register)); // MOVE.L Dn,(A7)
			}
			else
			{
				var addressRegister = (int)register - (int)M68kRegister.A0;
				_assembler.EmitWord((ushort)(0x2E88 | addressRegister)); // MOVE.L An,(A7)
			}
			return;
		}

		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2F40 | (int)register));
		}
		else
		{
			var addressRegister = (int)register - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(0x2F48 | addressRegister));
		}
		_assembler.EmitWord((ushort)displacement);
	}

	private void EmitStoreRegistersToFrame(ReadOnlySpan<M68kRegister> registers, short displacement)
	{
		if (registers.Length < 2)
		{
			foreach (var register in registers)
			{
				EmitStoreRegisterToFrame(register, displacement);
				displacement = checked((short)(displacement + 4));
			}
			return;
		}

		if (displacement == 0)
		{
			_assembler.EmitWord(0x48D7); // MOVEM.L regs,(A7)
			_assembler.EmitWord(EncodeMovemRegisterMask(registers));
			return;
		}

		_assembler.EmitWord(0x48EF); // MOVEM.L regs,d16(A7)
		_assembler.EmitWord(EncodeMovemRegisterMask(registers));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitLoadRegistersFromFrame(ReadOnlySpan<M68kRegister> registers, short displacement)
	{
		if (registers.Length < 2)
		{
			foreach (var register in registers)
			{
				EmitLoadRegisterFromStack(register, displacement);
				displacement = checked((short)(displacement + 4));
			}
			return;
		}

		foreach (var register in registers)
		{
			InvalidatePlatformBaseIfWritingRegister(register);
		}

		if (displacement == 0)
		{
			_assembler.EmitWord(0x4CD7); // MOVEM.L (A7),regs
			_assembler.EmitWord(EncodeMovemRegisterMask(registers));
			return;
		}

		_assembler.EmitWord(0x4CEF); // MOVEM.L d16(A7),regs
		_assembler.EmitWord(EncodeMovemRegisterMask(registers));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitStoreByteToFrame(M68kRegister register, short displacement)
	{
		if (register > M68kRegister.D7)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Byte frame stores require a data register.");
		}

		var lowByteDisplacement = checked((short)(displacement + 3));
		_assembler.EmitWord((ushort)(0x1F40 | (int)register)); // MOVE.B Dn,d16(A7)
		_assembler.EmitWord(unchecked((ushort)lowByteDisplacement));
	}

	private void EmitStoreZeroToFrame(short displacement)
	{
		EmitClearFrameSlot(displacement);
	}

	private void EmitMoveFrameSlotToFrameSlot(short sourceDisplacement, short destinationDisplacement)
	{
		if (sourceDisplacement == 0 && destinationDisplacement == 0)
		{
			_assembler.EmitWord(0x2E97); // MOVE.L (A7),(A7)
			return;
		}

		if (sourceDisplacement == 0)
		{
			_assembler.EmitWord(0x2F57); // MOVE.L (A7),d16(A7)
			_assembler.EmitWord(unchecked((ushort)destinationDisplacement));
			return;
		}

		if (destinationDisplacement == 0)
		{
			_assembler.EmitWord(0x2EAF); // MOVE.L d16(A7),(A7)
			_assembler.EmitWord(unchecked((ushort)sourceDisplacement));
			return;
		}

		_assembler.EmitWord(0x2F6F); // MOVE.L d16(A7),d16(A7)
		_assembler.EmitWord(unchecked((ushort)sourceDisplacement));
		_assembler.EmitWord(unchecked((ushort)destinationDisplacement));
	}

	private void EmitStoreAddressIndirectLongToFrame(M68kRegister sourceRegister, short destinationDisplacement)
	{
		if (sourceRegister < M68kRegister.A0)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceRegister));
		}

		var sourceAddressRegister = (int)sourceRegister - (int)M68kRegister.A0;
		if (destinationDisplacement == 0)
		{
			_assembler.EmitWord((ushort)(0x2E90 | sourceAddressRegister)); // MOVE.L (An),(A7)
			return;
		}

		_assembler.EmitWord((ushort)(0x2F50 | sourceAddressRegister)); // MOVE.L (An),d16(A7)
		_assembler.EmitWord(unchecked((ushort)destinationDisplacement));
	}

	private void EmitImmediateToFrame(int value, short displacement)
	{
		if (value == 0)
		{
			EmitClearFrameSlot(displacement);
			return;
		}

		if (displacement == 0)
		{
			_assembler.EmitWord(0x2EBC); // MOVE.L #value,(A7)
			_assembler.EmitLong(unchecked((uint)value));
			return;
		}

		_assembler.EmitWord(0x2F7C); // MOVE.L #value,d16(A7)
		_assembler.EmitLong(unchecked((uint)value));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitAddressImmediateToFrame(string label, short displacement)
	{
		if (displacement == 0)
		{
			_assembler.EmitWord(0x2EBC); // MOVE.L #label,(A7)
			_assembler.EmitAddress(label);
			return;
		}

		_assembler.EmitWord(0x2F7C); // MOVE.L #label,d16(A7)
		_assembler.EmitAddress(label);
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private IReadOnlyList<M68kRegister>? GetInternalRegisterAbi(CilMethod method)
		=> GetInternalCallAbi(method).RegisterOnlyLocations;

	private InternalCallAbi GetInternalCallAbi(CilMethod method)
	{
		if (!method.Signature.Header.IsInstance && HasAmigaStartupArguments(method))
		{
			return new InternalCallAbi(
				[
					new InternalArgumentLocation(0, 1, M68kRegister.D0, null, -1, false),
					new InternalArgumentLocation(1, 1, M68kRegister.A0, null, -1, false)
				],
				0);
		}

		var arguments = ImmutableArray.CreateBuilder<InternalArgumentLocation>(method.ParameterCount);
		var nextData = 0;
		var nextAddress = 0;
		var stackOffset = 0;
		if (method.Signature.Header.IsInstance)
		{
			if (method.Name != ".ctor" &&
				IsTransparentScalarDeclaringType(method))
			{
				arguments.Add(new InternalArgumentLocation(
					0,
					1,
					M68kRegister.D0,
					null,
					-1,
					false));
				nextData = 1;
			}
			else
			{
				arguments.Add(new InternalArgumentLocation(
					0,
					1,
					M68kRegister.A0,
					null,
					-1,
					true));
				nextAddress = 1;
			}
		}

		for (var parameterIndex = 0;
			parameterIndex < method.Signature.ParameterTypes.Length;
			parameterIndex++)
		{
			var parameter = method.Signature.ParameterTypes[parameterIndex];
			var argumentIndex = parameterIndex + (method.Signature.Header.IsInstance ? 1 : 0);
			var slotLongs = ArgumentSlotLongs(method, argumentIndex);
			var isMultiwordStruct = _module.TryGetReferenceFreeStructLayout(
				parameter,
				method.ModuleName,
				out var structLayout) &&
				structLayout.Size > 4;
			M68kRegister? register = null;
			M68kRegister? lowRegister = null;

			if (isMultiwordStruct)
			{
				// Aggregates are passed as whole values on the stack. The caller's
				// temporary address is an IR detail and never crosses the call ABI.
			}
			else if (Is64BitScalar(parameter) && nextData == 0)
			{
				register = M68kRegister.D0;
				lowRegister = M68kRegister.D1;
				nextData = 2;
			}
			else if (parameter.Kind != CilTypeKind.GenericParameter &&
				IsInternalAddressArgument(parameter) &&
				nextAddress < 2)
			{
				register = (M68kRegister)((int)M68kRegister.A0 + nextAddress++);
			}
			else if (parameter.Kind != CilTypeKind.GenericParameter &&
				!Is64BitScalar(parameter) &&
				nextData < 2)
			{
				register = (M68kRegister)((int)M68kRegister.D0 + nextData++);
			}

			var argumentStackOffset = register is null ? stackOffset : -1;
			if (register is null)
			{
				stackOffset = checked(stackOffset + (slotLongs * 4));
			}
			arguments.Add(new InternalArgumentLocation(
				argumentIndex,
				slotLongs,
				register,
				lowRegister,
				argumentStackOffset,
				parameter.Kind == CilTypeKind.ManagedReference));
		}

		int? returnBufferStackOffset = null;
		if (_module.TryGetReferenceFreeStructLayout(
				method.Signature.ReturnType,
				method.ModuleName,
				out var returnLayout) &&
			returnLayout.Size > 4)
		{
			returnBufferStackOffset = stackOffset;
			stackOffset = checked(stackOffset + 4);
		}

		return new InternalCallAbi(
			arguments.MoveToImmutable(),
			stackOffset,
			returnBufferStackOffset);
	}

	private static bool IsInternalAddressArgument(CilType type) =>
		type.Kind is
			CilTypeKind.ManagedReference or
			CilTypeKind.ManagedPointer or
			CilTypeKind.UnmanagedPointer or
			CilTypeKind.FunctionPointer;

	private static bool IsInternalAddressReturn(CilType type) =>
		IsInternalAddressArgument(type);

	private void EmitMoveRegisterToD0(M68kRegister register)
	{
		if (register == M68kRegister.D0)
		{
			return;
		}

		if (register <= M68kRegister.D7)
		{
			// MOVE.L Dn,D0
			_assembler.EmitWord((ushort)(0x2000 | (int)register));
			return;
		}

		// MOVE.L An,D0
		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2008 | addressRegister));
	}

	private void EmitMoveRegisterToRegister(M68kRegister source, M68kRegister target)
	{
		if (source == target)
		{
			return;
		}
		InvalidatePlatformBaseIfWritingRegister(target);

		if (target <= M68kRegister.D7)
		{
			var targetData = (int)target;
			if (source <= M68kRegister.D7)
			{
				// MOVE.L Dn,Dn
				_assembler.EmitWord((ushort)(0x2000 | (targetData << 9) | (int)source));
				return;
			}

			// MOVE.L An,Dn
			var sourceAddress = (int)source - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(0x2008 | (targetData << 9) | sourceAddress));
			return;
		}

		var targetAddress = (int)target - (int)M68kRegister.A0;
		if (source <= M68kRegister.D7)
		{
			// MOVEA.L Dn,An
			_assembler.EmitWord((ushort)(0x2040 | (targetAddress << 9) | (int)source));
			return;
		}

		// MOVEA.L An,An
		var sourceAddressRegister = (int)source - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2048 | (targetAddress << 9) | sourceAddressRegister));
	}

	private void EmitLoadRegisterFromAddressRegister(M68kRegister target, M68kRegister addressRegister)
	{
		if (addressRegister < M68kRegister.A0)
		{
			throw new ArgumentOutOfRangeException(nameof(addressRegister));
		}

		var sourceAddress = (int)addressRegister - (int)M68kRegister.A0;
		if (target <= M68kRegister.D7)
		{
			// MOVE.L (An),Dn
			_assembler.EmitWord((ushort)(0x2010 | ((int)target << 9) | sourceAddress));
			return;
		}

		// MOVEA.L (An),An
		var targetAddress = (int)target - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2050 | (targetAddress << 9) | sourceAddress));
	}

	private void EmitNewObject(CilMethod caller, CilInstruction instruction)
	{
		var constructor = _module.ResolveMethodToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset).Definition;
		if (constructor is null)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Could not resolve object constructor.",
				caller.DisplayName,
				instruction.Offset);
		}

		if (_module.IsTransparentScalarConstructor(constructor))
		{
			return;
		}

		EnsureManagedAllocationAllowed(caller, instruction, "object construction");
		if (!constructor.Signature.Header.IsInstance)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Object construction requires an instance constructor.",
				caller.DisplayName,
				instruction.Offset);
		}

		var constructorAbi = GetInternalCallAbi(constructor);

		var layout = _module.GetTypeLayout(constructor);
		var descriptorLabel = TypeDescriptorLabel(layout);
		if (constructor.ConstructedDeclaringType is { } constructedType)
		{
			_constructedTypeDescriptors.TryAdd(
				constructedType.DisplayName,
				(constructedType, layout));
			descriptorLabel = ConstructedTypeDescriptorLabel(layout, constructedType);
		}
		else
		{
			_usedTypeLayouts.TryAdd(layout.Identity, layout);
		}
		EmitPushConstant(layout.Size);
		EmitManagedAllocation();

		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(descriptorLabel);
		if (layout.Size <= sbyte.MaxValue)
		{
			_assembler.EmitWord((ushort)(0x7200 | (byte)layout.Size)); // MOVEQ #size,D1
		}
		else
		{
			_assembler.EmitWord(0x223C); // MOVE.L #size,D1
			_assembler.EmitLong((uint)layout.Size);
		}
		_assembler.EmitWord(0x2141); // MOVE.L D1,4(A0)
		_assembler.EmitWord(0x0004);

		EmitMoveRegister(M68kRegister.D0, M68kRegister.A0);
		EmitPrepareInternalCall(constructor, constructorAbi, receiverAlreadyLoaded: true);
		_assembler.EmitBsr(MethodLabel(constructor));
		_loadedPlatformBase = null;
		EmitReleaseStackBytes(constructorAbi.StackBytes);
		EmitPushRegister(M68kRegister.A0);
	}

	private void EmitInitObj(CilMethod caller, CilInstruction instruction)
	{
		var type = _module.ResolveTypeToken((int)instruction.Operand!, caller, instruction.Offset);
		var valueType = type.Kind == CilTypeKind.ManagedReference
			? type.ElementType ?? new CilType(CilTypeKind.ValueType, 0, type.DisplayName)
			: type;
		var isSupportedScalar = type.IsSupportedScalar &&
			type.Size is 1 or 2 or 4 or 8;
		if (valueType is null ||
			!isSupportedScalar &&
			(!valueType.IsNullable || !_module.IsSupportedNullableType(valueType)) &&
			!_module.IsSupportedStructType(valueType))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"initobj is only supported for nullable 32-bit scalar values and 32-bit scalar structs, not '{type.DisplayName}'.",
				caller.DisplayName,
				instruction.Offset);
		}

		EmitPopRegister(M68kRegister.A0);
		if (_module.IsUninitializedStorageType(valueType))
		{
			return;
		}
		if (isSupportedScalar)
		{
			EmitImmediateToRegister(M68kRegister.D0, 0);
			if (type.Size == 8)
			{
				EmitStoreD0ToA0Displacement(4, 0);
				EmitStoreD0ToA0Displacement(4, 4);
			}
			else
			{
				EmitStoreD0ToA0Displacement(type.Size, 0);
			}
			return;
		}

		if (!UseClr)
		{
			EmitImmediateToRegister(M68kRegister.D0, 0);
		}

		if (_module.IsSupportedStructType(valueType))
		{
			EmitClearAddressRegion(SlotLongs(valueType));
			return;
		}

		if (IsCompactNullableType(valueType))
		{
			EmitClearAddressLong(0);
			return;
		}

		EmitClearAddressLong(0);
		EmitClearAddressLong(4);
	}

	private void EmitNullableConstructor(MethodReference target)
	{
		EmitPopD0();
		EmitPopRegister(M68kRegister.A0);
		if (IsCompactNullableIntrinsic(target))
		{
			_assembler.EmitWord(0x2080); // MOVE.L D0,(A0)
			return;
		}

		_assembler.EmitWord(0x2080); // MOVE.L D0,(A0)
		_assembler.EmitWord(0x7001); // MOVEQ #1,D0
		_assembler.EmitWord(0x2140); // MOVE.L D0,4(A0)
		_assembler.EmitWord(0x0004);
	}

	private void EmitNullableHasValue(MethodReference target, bool pushResult)
	{
		EmitPopRegister(M68kRegister.A0);
		if (IsCompactNullableIntrinsic(target))
		{
			EmitCompactNullableHasValueFromAddress(
				M68kRegister.A0,
				normalize: !pushResult);
			if (pushResult)
			{
				EmitPushByteRegister(M68kRegister.D0);
			}
			return;
		}

		_assembler.EmitWord(0x2028); // MOVE.L 4(A0),D0
		_assembler.EmitWord(0x0004);
		if (pushResult)
		{
			EmitPushD0();
		}
	}

	private void EmitCompactNullableHasValueFromFrame(short displacement)
	{
		EmitLoadRegisterFromStack(M68kRegister.D0, displacement);
		_assembler.EmitWord(0x56C0); // SNE D0
		_assembler.EmitWord(0x4400); // NEG.B D0, FF -> 1
		EmitPushByteRegister(M68kRegister.D0);
	}

	private void EmitCompactNullableHasValueFromRegister(M68kRegister register)
	{
		EmitMoveRegisterToD0(register);
		if (register == M68kRegister.D0)
		{
			_assembler.EmitWord(0x4A80); // TST.L D0
		}
		_assembler.EmitWord(0x56C0); // SNE D0
		_assembler.EmitWord(0x4400); // NEG.B D0, FF -> 1
		EmitPushByteRegister(M68kRegister.D0);
	}

	private void EmitCompactNullableHasValueFromAddress(
		M68kRegister addressRegister,
		bool normalize)
	{
		EmitLoadRegisterFromAddressRegister(M68kRegister.D0, addressRegister);
		_assembler.EmitWord(0x56C0); // SNE D0
		_assembler.EmitWord(0x4400); // NEG.B D0, FF -> 1
		if (normalize)
		{
			EmitSignExtendByteToLongD0();
		}
	}

	private void EmitNullableGetValue(bool pushResult)
	{
		EmitPopRegister(M68kRegister.A0);
		_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		if (pushResult)
		{
			EmitPushD0();
		}
	}

	private void EmitNullableGetValueOrDefault(MethodReference target, bool pushResult)
	{
		EmitPopRegister(M68kRegister.D1);
		EmitPopRegister(M68kRegister.A0);
		if (IsCompactNullableIntrinsic(target))
		{
			_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
			var doneCompact = UniqueLabel("nullable_done");
			_assembler.EmitBranch(M68kCondition.NotEqual, doneCompact);
			_assembler.EmitWord(0x2001); // MOVE.L D1,D0
			_assembler.Mark(doneCompact);
			if (pushResult)
			{
				EmitPushD0();
			}
			return;
		}

		_assembler.EmitWord(0x2028); // MOVE.L 4(A0),D0
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4A80); // TST.L D0
		var useDefault = UniqueLabel("nullable_default");
		var done = UniqueLabel("nullable_done");
		_assembler.EmitBranch(M68kCondition.Equal, useDefault);
		_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		_assembler.EmitBranch(M68kCondition.True, done);
		_assembler.Mark(useDefault);
		_assembler.EmitWord(0x2001); // MOVE.L D1,D0
		_assembler.Mark(done);
		if (pushResult)
		{
			EmitPushD0();
		}
	}

	private void EmitNewArray(CilMethod method, CilInstruction instruction)
	{
		EnsureManagedAllocationAllowed(method, instruction, "array allocation");
		var elementType = _module.ResolveTypeToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (elementType.Size is not (1 or 2 or 4 or 8) ||
			(!elementType.IsSupportedScalar && !elementType.IsReference))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Arrays of '{elementType.DisplayName}' are not implemented; array elements must occupy one, two, four, or eight bytes.",
				method.DisplayName,
				instruction.Offset);
		}

		_arrayTypes.TryAdd(elementType.DisplayName, elementType);
		_assembler.EmitWord(0x241F); // MOVE.L (A7)+,D2 length
		var lengthValid = UniqueLabel("array_length_valid");
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Plus, lengthValid);
		EmitExceptionRaise(reason: 4, hasException: false);
		_assembler.Mark(lengthValid);
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		EmitScaleD0(elementType.Size);
		_assembler.EmitWord(0x0680); // ADDI.L #12,D0
		_assembler.EmitLong(12);
		EmitPushD0();
		EmitManagedAllocation();
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(ArrayDescriptorLabel(elementType));
		_assembler.EmitWord(0x2202); // MOVE.L D2,D1
		EmitScaleD1(elementType.Size);
		_assembler.EmitWord(0x0681); // ADDI.L #12,D1
		_assembler.EmitLong(12);
		_assembler.EmitWord(0x2141); // MOVE.L D1,4(A0)
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2142); // MOVE.L D2,8(A0)
		_assembler.EmitWord(0x0008);
		EmitPushD0();
	}

	private void EnsureManagedAllocationAllowed(
		CilMethod method,
		CilInstruction instruction,
		string operation)
	{
		if (_memoryManagement != M68kMemoryManagement.None)
		{
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Managed {operation} requires a managed heap. Select ExternalAllocator, BumpAllocator, ManagedPoolMarkSweepGc, or ExecPoolMarkSweepGc memory management.",
			method.DisplayName,
			instruction.Offset);
	}

	private void EmitManagedAllocation()
	{
		EmitPopD0();
		EmitManagedAllocationFromD0();
	}

	private void EmitManagedAllocationFromD0(
		int? rematerializableSize = null)
	{
		if (!UsesBuiltInManagedPool)
		{
			EmitRuntimeJsr(RuntimeAllocLabel, M68kRuntimeImports.Allocate);
			_loadedPlatformBase = null;
			EmitRequireAllocationSucceeded();
			return;
		}

		var strategy = M68kCompiler.GetEffectiveGcSweepStrategy(_request);
		var needsSizeAfterClobber =
			strategy != M68kGcSweepStrategy.OnDemand;
		var rematerialize =
			rematerializableSize is >= sbyte.MinValue and <= sbyte.MaxValue;
		var preserveInD2 = needsSizeAfterClobber && !rematerialize;
		if (preserveInD2)
		{
			EmitPushRegister(M68kRegister.D2);
			EmitMoveRegister(M68kRegister.D0, M68kRegister.D2);
		}

		void RestoreAllocationSize()
		{
			if (rematerialize)
			{
				_assembler.EmitWord((ushort)(
					0x7000 |
					(byte)(sbyte)rematerializableSize!.Value)); // MOVEQ #size,D0
			}
			else
			{
				EmitMoveRegister(M68kRegister.D2, M68kRegister.D0);
			}
		}

		var sizeWasClobbered = false;
		if (strategy == M68kGcSweepStrategy.EveryAllocation)
		{
			EmitManagedCollectWithRoots(preserveInD2 ? 4 : 0);
			_loadedPlatformBase = null;
			sizeWasClobbered = true;
		}
		else if (strategy == M68kGcSweepStrategy.TelemetryTriggered)
		{
			EmitTelemetryTriggeredCollection(preserveInD2 ? 4 : 0);
			sizeWasClobbered = true;
		}

		if (sizeWasClobbered)
		{
			RestoreAllocationSize();
		}
		_assembler.EmitBsr(RuntimeAllocLabel);
		_loadedPlatformBase = null;
		if (strategy == M68kGcSweepStrategy.OnAllocationFailure)
		{
			var done = UniqueLabel("gc_alloc_call_done");
			_assembler.EmitWord(0x4A80); // TST.L D0
			_assembler.EmitBranch(M68kCondition.NotEqual, done);
			EmitManagedCollectWithRoots(preserveInD2 ? 4 : 0);
			_loadedPlatformBase = null;
			RestoreAllocationSize();
			_assembler.EmitBsr(RuntimeAllocLabel);
			_loadedPlatformBase = null;
			_assembler.Mark(done);
		}

		if (preserveInD2)
		{
			EmitPopRegister(M68kRegister.D2);
		}
		EmitRequireAllocationSucceeded();
	}

	private void EmitTelemetryTriggeredCollection(int additionalStackBytes = 0)
	{
		var checkBlocks = UniqueLabel("gc_telemetry_check_blocks");
		var collect = UniqueLabel("gc_telemetry_collect");
		var done = UniqueLabel("gc_telemetry_done");
		EmitLoadD0FromLabel(GcStaleBytesThresholdLabel);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, checkBlocks);
		EmitLoadD1FromLabel(GcStaleBytesLabel);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, collect);
		_assembler.Mark(checkBlocks);
		EmitLoadD0FromLabel(GcStaleBlocksThresholdLabel);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, done);
		EmitLoadD1FromLabel(GcStaleBlocksLabel);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarrySet, done);
		_assembler.Mark(collect);
		EmitManagedCollectWithRoots(additionalStackBytes);
		_loadedPlatformBase = null;
		_assembler.Mark(done);
	}

	private void EmitArrayAccess(CilMethod method, CilInstruction instruction)
	{
		var op = instruction.OpCode;
		var access = GetArrayAccess(op);
		if (access.IsStore)
		{
			if (access.Size == 8)
			{
				EmitPopStackValue(M68kRegister.D3, CurrentStackKind());
				EmitPopStackValue(M68kRegister.D0, CurrentStackKind(1));
				_assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1 index
				_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0 array
				EmitArrayBoundsCheck();
				EmitScaleD1(access.Size);
				_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
				EmitStoreD0ToA0Displacement(4, 12);
				_assembler.EmitWord(0x2143); // MOVE.L D3,16(A0)
				_assembler.EmitWord(16);
				return;
			}
			EmitPopStackValue(M68kRegister.D0, CurrentStackKind()); // value
			_assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1 index
			_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0 array
			EmitArrayBoundsCheck();
			EmitScaleD1(access.Size);
			_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
			EmitStoreD0ToA0Displacement(access.Size, 12);
			return;
		}

		_assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1 index
		_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0 array
		EmitArrayBoundsCheck();
		EmitScaleD1(access.Size);
		_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
		if (op == OpCodes.Ldelema)
		{
			_assembler.EmitWord(0x41E8); // LEA 12(A0),A0
			_assembler.EmitWord(0x000C);
			_assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
			return;
		}
		if (access.Size == 8)
		{
			EmitLoadD0FromA0Displacement(4, signExtend: false, 12);
			_assembler.EmitWord(0x2228); // MOVE.L 16(A0),D1
			_assembler.EmitWord(16);
			EmitPushRegister(M68kRegister.D0);
			EmitPushRegister(M68kRegister.D1);
			return;
		}

		EmitLoadD0FromA0Displacement(access.Size, access.SignExtend, 12);
		if (access.Size == 1)
		{
			EmitPushByteRegister(M68kRegister.D0);
		}
		else
		{
			EmitPushD0();
		}
	}

	private void EmitIndirectLoad(OpCode op)
	{
		_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0
		EmitNormalizeIndirectPointer();
		if (op == OpCodes.Ldind_I8)
		{
			EmitLoadD0FromA0Displacement(4, signExtend: false, 12);
			_assembler.EmitWord(0x2228); // MOVE.L 16(A0),D1
			_assembler.EmitWord(16);
			EmitPushRegister(M68kRegister.D0);
			EmitPushRegister(M68kRegister.D1);
			return;
		}
		var access = GetIndirectAccess(op);
		EmitLoadD0FromA0Displacement(access.Size, access.SignExtend, 12);
		if (access.Size == 1)
		{
			EmitPushByteRegister(M68kRegister.D0);
		}
		else
		{
			EmitPushD0();
		}
	}

	private void EmitIndirectStore(OpCode op)
	{
		EmitIndirectStore(op == OpCodes.Stind_I8 ? 8 : GetIndirectAccess(op).Size);
	}

	private void EmitIndirectStore(int size)
	{
		if (size == 8)
		{
			EmitPopStackValue(M68kRegister.D1, CurrentStackKind());
			EmitPopStackValue(M68kRegister.D0, CurrentStackKind(1));
			_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0
			EmitNormalizeIndirectPointer();
			EmitStoreD0ToA0Displacement(4, 12);
			_assembler.EmitWord(0x2141); // MOVE.L D1,16(A0)
			_assembler.EmitWord(16);
			return;
		}
		EmitPopStackValue(M68kRegister.D0, CurrentStackKind());
		_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0
		EmitNormalizeIndirectPointer();
		EmitStoreD0ToA0Displacement(size, 12);
	}

	private void EmitArrayBoundsCheck()
	{
		var arrayValid = UniqueLabel("array_nonnull");
		var indexNonNegative = UniqueLabel("array_index_nonnegative");
		var indexValid = UniqueLabel("array_index_valid");
		_assembler.EmitWord(0x2408); // MOVE.L A0,D2
		_assembler.EmitBranch(M68kCondition.NotEqual, arrayValid);
		EmitExceptionRaise(reason: 1, hasException: false);
		_assembler.Mark(arrayValid);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Plus, indexNonNegative);
		EmitExceptionRaise(reason: 2, hasException: false);
		_assembler.Mark(indexNonNegative);
		_assembler.EmitWord(0x2428); // MOVE.L 8(A0),D2
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0xB282); // CMP.L D2,D1
		_assembler.EmitBranch(M68kCondition.CarrySet, indexValid);
		EmitExceptionRaise(reason: 2, hasException: false);
		_assembler.Mark(indexValid);
	}

	private void EmitFieldAccess(CilMethod method, CilInstruction instruction)
	{
		var field = _module.ResolveFieldToken((int)instruction.Operand!, method, instruction.Offset);
		ValidateType(field.Type, method, "field");
		var op = instruction.OpCode;
		if (!field.IsStatic && _module.IsTransparentScalarField(field))
		{
			if (op == OpCodes.Ldfld)
			{
				EmitPopRegister(M68kRegister.A0);
				EmitFieldLoadFromA0(method, instruction, field, pushResult: true);
				return;
			}

			if (op == OpCodes.Stfld)
			{
				EmitPopD0();
				EmitPopRegister(M68kRegister.A0);
				_assembler.EmitWord(0x2080); // MOVE.L D0,(A0)
				return;
			}

			throw FieldMismatch(method, instruction, field);
		}

		if (field.IsStatic)
		{
			_staticFields.TryAdd(field.Identity, field);
			var label = StaticFieldLabel(field);
			if (op == OpCodes.Ldsfld)
			{
				_assembler.EmitWord(0x2F39); // MOVE.L abs.l,-(A7)
				_assembler.EmitAddress(label);
				return;
			}

			if (op == OpCodes.Ldsflda)
			{
				_assembler.EmitWord(0x4879); // PEA abs.l
				_assembler.EmitAddress(label);
				return;
			}

			if (op == OpCodes.Stsfld)
			{
				_assembler.EmitWord(0x23DF); // MOVE.L (A7)+,abs.l
				_assembler.EmitAddress(label);
				return;
			}

			throw FieldMismatch(method, instruction, field);
		}

		var displacement = FieldDisplacement(field);
		if (op == OpCodes.Ldfld)
		{
			EmitPopD0();
			_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
			EmitFieldLoadFromA0(method, instruction, field, pushResult: true);
			return;
		}

		if (op == OpCodes.Ldflda)
		{
			EmitPopD0();
			EmitRequireNonNull();
			_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
			_assembler.EmitWord(0x41E8); // LEA d16(A0),A0
			_assembler.EmitWord((ushort)displacement);
			_assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
			return;
		}

		if (op == OpCodes.Stfld)
		{
			EmitPopD0();
			_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0
			var valid = UniqueLabel("field_object_valid");
			_assembler.EmitWord(0x2208); // MOVE.L A0,D1 (and set condition codes)
			_assembler.EmitBranch(M68kCondition.NotEqual, valid);
			EmitExceptionRaise(reason: 1, hasException: false);
			_assembler.Mark(valid);
			_assembler.EmitWord(0x2140); // MOVE.L D0,d16(A0)
			_assembler.EmitWord((ushort)displacement);
			return;
		}

		throw FieldMismatch(method, instruction, field);
	}

	private void EmitFieldLoadFromA0(
		CilMethod method,
		CilInstruction instruction,
		CilField field,
		bool pushResult)
	{
		if (_module.IsTransparentScalarField(field))
		{
			_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		}
		else
		{
			_assembler.EmitWord(0x2008); // MOVE.L A0,D0
			EmitRequireNonNull();
			var displacement = FieldDisplacement(field);
			_assembler.EmitWord(0x2028); // MOVE.L d16(A0),D0
			_assembler.EmitWord((ushort)displacement);
		}

		if (!pushResult)
		{
			if (field.Type.IsReference)
			{
				_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
			}
			return;
		}

		EmitPushD0();
	}

	private short FieldDisplacement(CilField field)
	{
		if (field.ExternalOffset is { } externalOffset)
		{
			return checked((short)externalOffset);
		}

		var layout = _module.GetTypeLayout(field);
		if (field.ConstructedDeclaringType is null)
		{
			_usedTypeLayouts.TryAdd(layout.Identity, layout);
		}
		return checked((short)layout.FieldOffsets[field.Handle]);
	}

	private void EmitRequireNonNull()
	{
		var valid = UniqueLabel("nonnull");
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, valid);
		EmitExceptionRaise(reason: 1, hasException: false);
		_assembler.Mark(valid);
	}

	private void EmitRequireAllocationSucceeded()
	{
		var valid = UniqueLabel("allocation_succeeded");
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, valid);
		EmitExceptionRaise(reason: 6, hasException: false);
		_assembler.Mark(valid);
	}

	private void EmitExceptionRaise(int reason, bool hasException)
	{
		if (!UsesManagedExceptionRuntime)
		{
			_assembler.EmitWord(0x4AFC); // ILLEGAL
			return;
		}

		if (!hasException)
		{
			EmitImmediateToRegister(M68kRegister.A0, 0);
		}
		EmitImmediateToRegister(M68kRegister.D0, reason);
		_assembler.EmitJsr(RuntimeExceptionRaiseLabel, external: false);
		RegisterCurrentUnwindSite(exception: true, gc: false);
		_loadedPlatformBase = null;
	}

	private void EmitExceptionMetadata(IReadOnlyList<CilMethod> methods)
	{
		if (!_usesExceptionRuntime &&
			!M68kCompiler.IsManagedRuntime(_request) &&
			!_assembler.ReferencesTarget(MethodTableLabel))
		{
			return;
		}

		_assembler.AlignWord();
		_assembler.Mark(MethodTableLabel);
		_assembler.Mark(ExceptionTableLabel);
		_assembler.EmitLong((uint)_unwindSites.Count);
		for (var index = 0; index < _unwindSites.Count; index++)
		{
			var site = _unwindSites[index];
			_assembler.EmitAddress(site.ResumeLabel);
			_assembler.EmitAddress(RuntimeMethodDescriptorLabel(site.Method));
			if (site.ExceptionStateLabel is null)
			{
				_assembler.EmitLong(0);
			}
			else
			{
				_assembler.EmitAddress(site.ExceptionStateLabel);
			}
			_assembler.EmitLong(unchecked((uint)site.StackAdjustment));
			if (site.RootOffsets.IsEmpty)
			{
				_assembler.EmitLong(0);
			}
			else
			{
				_assembler.EmitAddress(RuntimeRootMapLabel(index));
			}
			if (UnwindSiteEntryBytes > 20)
			{
				if (site.ExceptionCleanupLabel is null)
				{
					_assembler.EmitLong(0);
				}
				else
				{
					_assembler.EmitAddress(site.ExceptionCleanupLabel);
				}
			}
		}

		for (var index = 0; index < _unwindSites.Count; index++)
		{
			var roots = _unwindSites[index].RootOffsets;
			if (roots.IsEmpty)
			{
				continue;
			}
			_assembler.Mark(RuntimeRootMapLabel(index));
			_assembler.EmitLong((uint)roots.Length);
			foreach (var offset in roots)
			{
				_assembler.EmitLong(unchecked((uint)offset));
			}
		}

		foreach (var layout in _unwindMethodLayouts.Values)
		{
			_assembler.AlignWord();
			_assembler.Mark(RuntimeMethodDescriptorLabel(layout.Method));
			_assembler.EmitLong((uint)layout.FrameBytes);
			_assembler.EmitLong((uint)(layout.CalleeSavedRegisters.Length * 4));
			_assembler.EmitAddress(RuntimeMethodUnwindRestoreLabel(layout.Method));
			if (UsesExtendedUnwindMetadata)
			{
				_assembler.EmitLong(layout.HasDynamicStackAllocation ? 1u : 0u);
				_assembler.EmitLong(layout.SavedFrameAnchorOffset is { } anchorOffset
					? checked((uint)anchorOffset + 1)
					: 0u);
			}

			_assembler.Mark(RuntimeMethodUnwindRestoreLabel(layout.Method));
			_assembler.EmitWord(0x2041); // MOVEA.L D1,A0 exception context
			for (var index = 0; index < layout.CalleeSavedRegisters.Length; index++)
			{
				var register = layout.CalleeSavedRegisters[index];
				var savedIndex = layout.CalleeSavedRegisters.Length < 3
					? layout.CalleeSavedRegisters.Length - 1 - index
					: index;
				EmitLoadUnwindRegister(
					register,
					checked((short)(layout.FrameBytes + (savedIndex * 4))));
				EmitStoreUnwindRegisterSnapshot(register);
			}
			_assembler.EmitWord(0x4E75); // RTS
		}
	}

	private void EmitStoreUnwindRegisterSnapshot(M68kRegister register)
	{
		var snapshotIndex = register <= M68kRegister.D7
			? (int)register - (int)M68kRegister.D2
			: 6 + (int)register - (int)M68kRegister.A2;
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2140 | (int)register)); // MOVE.L Dn,d16(A0)
		}
		else
		{
			_assembler.EmitWord((ushort)(
				0x2148 | ((int)register - (int)M68kRegister.A0))); // MOVE.L An,d16(A0)
		}
		_assembler.EmitWord(checked((ushort)(ExceptionContextBytes + (snapshotIndex * 4))));
	}

	private void EmitLoadUnwindRegister(M68kRegister register, short displacement)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2029 | ((int)register << 9))); // MOVE.L d16(A1),Dn
		}
		else
		{
			_assembler.EmitWord((ushort)(
				0x2069 | (((int)register - (int)M68kRegister.A0) << 9))); // MOVEA.L d16(A1),An
		}
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitData(IReadOnlyList<CilMethod> methods)
	{
		PrepareRuntimeTypeDescriptors(methods);
		_assembler.AlignWord();
		foreach (var platformBase in _usedPlatformBases.Values
			.Where(item => item.Binding.BaseSource == M68kExternalBaseSource.WritableSlot)
			.OrderBy(item => item.Binding.Identity, StringComparer.Ordinal))
		{
			_assembler.Mark(platformBase.Label!);
			_assembler.EmitLong(platformBase.Binding.InitialValue);
		}

		foreach (var field in _staticFields.Values.OrderBy(item =>
			System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(item.Handle)))
		{
			_assembler.Mark(StaticFieldLabel(field));
			var longs = SlotLongs(field.Type);
			for (var index = 0; index < longs; index++)
			{
				_assembler.EmitLong(0);
			}
		}

		foreach (var initializer in _typeInitializers.Values
			.OrderBy(item => item.ModuleName, StringComparer.Ordinal)
			.ThenBy(item => System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(item.DeclaringType)))
		{
			_assembler.Mark(TypeInitializationStateLabel(initializer));
			_assembler.EmitLong(0);
			if (TypeInitializerCanFail(initializer))
			{
				_assembler.Mark(TypeInitializationExceptionLabel(initializer));
				_assembler.EmitLong(0);
				_assembler.Mark(TypeInitializationWrapperLabel(initializer));
				_assembler.EmitAddress(RuntimeTypeDescriptorLabel("System.TypeInitializationException"));
				_assembler.EmitLong(12);
				_assembler.Mark(TypeInitializationWrapperInnerLabel(initializer));
				_assembler.EmitLong(0);
			}
		}

		var dispatchLayouts = _usedTypeLayouts.Values
			.Concat(_constructedTypeDescriptors.Values.Select(static item => item.Layout))
			.DistinctBy(static layout => layout.Identity)
			.OrderBy(layout => layout.ModuleName, StringComparer.Ordinal)
			.ThenBy(layout => System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(layout.Handle))
			.ThenBy(layout => layout.Identity.Construction, StringComparer.Ordinal)
			.ToArray();
		var constructedLayoutIdentities = _constructedTypeDescriptors.Values
			.Select(static item => item.Layout.Identity)
			.ToHashSet();

		foreach (var layout in _usedTypeLayouts.Values
			.Where(layout => !constructedLayoutIdentities.Contains(layout.Identity))
			.OrderBy(layout => layout.ModuleName, StringComparer.Ordinal)
			.ThenBy(layout => System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(layout.Handle))
			.ThenBy(layout => layout.Identity.Construction, StringComparer.Ordinal))
		{
			var virtualTable = _module.GetVirtualTable(layout);
			var interfaceImplementations = GetUsedInterfaceImplementations(layout);
			_assembler.Mark(TypeDescriptorLabel(layout));
			_assembler.EmitLong((uint)layout.Size);
			_assembler.EmitLong(layout.ReferenceBitmap);
			EmitTypeDescriptorBase(layout);
			if (virtualTable.Slots.Length == 0)
			{
				_assembler.EmitLong(0);
			}
			else
			{
				_assembler.EmitAddress(VirtualTableLabel(layout));
			}
			if (interfaceImplementations.Count == 0)
			{
				_assembler.EmitLong(0);
			}
			else
			{
				_assembler.EmitAddress(InterfaceMapLabel(layout));
			}
		}

		foreach (var item in _constructedTypeDescriptors.Values
			.OrderBy(item => item.Type.DisplayName, StringComparer.Ordinal))
		{
			var layout = item.Layout;
			var virtualTable = _module.GetVirtualTable(layout);
			var interfaceImplementations = GetUsedInterfaceImplementations(layout);
			_assembler.Mark(ConstructedTypeDescriptorLabel(layout, item.Type));
			_assembler.EmitLong((uint)layout.Size);
			_assembler.EmitLong(layout.ReferenceBitmap);
			EmitTypeDescriptorBase(layout);
			if (virtualTable.Slots.Length == 0)
			{
				_assembler.EmitLong(0);
			}
			else
			{
				_assembler.EmitAddress(VirtualTableLabel(layout));
			}
			if (interfaceImplementations.Count == 0)
			{
				_assembler.EmitLong(0);
			}
			else
			{
				_assembler.EmitAddress(InterfaceMapLabel(layout));
			}
		}

		var compiledMethods = methods
			.Select(method => method.Identity)
			.ToHashSet();
		foreach (var layout in dispatchLayouts)
		{
			var virtualTable = _module.GetVirtualTable(layout);
			if (virtualTable.Slots.Length == 0)
			{
				continue;
			}

			_assembler.Mark(VirtualTableLabel(layout));
			foreach (var method in virtualTable.Slots)
			{
				if (method.IsAbstract || !compiledMethods.Contains(method.Identity))
				{
					_assembler.EmitLong(0);
				}
				else
				{
					_assembler.EmitAddress(MethodLabel(method));
				}
			}
		}

		foreach (var interfaceDefinition in _usedInterfaces.Values
			.OrderBy(item => item.Identity.ModuleName, StringComparer.Ordinal)
			.ThenBy(item => System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(item.Identity.Handle))
			.ThenBy(item => item.Identity.Construction, StringComparer.Ordinal))
		{
			_assembler.Mark(InterfaceIdentityLabel(interfaceDefinition));
			_assembler.EmitLong(0);
		}

		foreach (var layout in dispatchLayouts)
		{
			var implementations = GetUsedInterfaceImplementations(layout);
			if (implementations.Count == 0)
			{
				continue;
			}

			_assembler.Mark(InterfaceMapLabel(layout));
			_assembler.EmitLong((uint)implementations.Count);
			foreach (var implementation in implementations)
			{
				_assembler.EmitAddress(InterfaceIdentityLabel(implementation.Interface));
				_assembler.EmitAddress(InterfaceMethodTableLabel(implementation));
			}

			foreach (var implementation in implementations)
			{
				_assembler.Mark(InterfaceMethodTableLabel(implementation));
				foreach (var method in implementation.Methods)
				{
					if (compiledMethods.Contains(method.Identity))
					{
						_assembler.EmitAddress(MethodLabel(method));
					}
					else
					{
						_assembler.EmitLong(0);
					}
				}
			}
		}

		if (_stringLiterals.Count != 0 || _usesDynamicStrings || _usesRuntimeEmptyString)
		{
			_assembler.Mark("runtime:string-descriptor");
			_assembler.EmitLong(0); // Variable-size object.
			_assembler.EmitLong(0);
			_assembler.EmitAddress(RuntimeTypeDescriptorLabel("System.Object"));
			_assembler.EmitLong(0);
			_assembler.EmitLong(0);
		}

		foreach (var item in _stringLiterals
			.OrderBy(item => item.Key.ModuleName, StringComparer.Ordinal)
			.ThenBy(item => item.Key.Token))
		{
			_assembler.AlignWord();
			_assembler.Mark(StringLabel(item.Key));
			_assembler.EmitAddress("runtime:string-descriptor");
			var size = checked(12 + ((item.Value.Length + 1) * 2));
			_assembler.EmitLong((uint)size);
			_assembler.EmitLong((uint)item.Value.Length);
			foreach (var character in item.Value)
			{
				_assembler.EmitWord(character);
			}
			_assembler.EmitWord(0);
		}

		if (_usesRuntimeEmptyString)
		{
			_assembler.AlignWord();
			_assembler.Mark(RuntimeEmptyStringLabel);
			_assembler.EmitAddress("runtime:string-descriptor");
			_assembler.EmitLong(M68kRuntimeAbi.StringDataOffset + 2);
			_assembler.EmitLong(0);
			_assembler.EmitWord(0);
		}

		if (_runtimeEmptyCharArrayElementType is { } emptyCharArrayElement)
		{
			_assembler.AlignWord();
			_assembler.Mark(RuntimeEmptyCharArrayLabel);
			_assembler.EmitAddress(ArrayDescriptorLabel(emptyCharArrayElement));
			_assembler.EmitLong((uint)M68kRuntimeAbi.ArrayDataOffset);
			_assembler.EmitLong(0);
		}

		foreach (var item in _cStringLiterals
			.OrderBy(item => item.Key.ModuleName, StringComparer.Ordinal)
			.ThenBy(item => item.Key.Token))
		{
			_assembler.AlignWord();
			_assembler.Mark(CStringLabel(item.Key));
			foreach (var character in item.Value)
			{
				if (character > byte.MaxValue)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedInstruction,
						$"CString literal '{item.Value}' contains a non-8-bit character.");
				}
				_assembler.EmitByte((byte)character);
			}
			_assembler.EmitByte(0);
		}

		foreach (var type in _arrayTypes.Values.OrderBy(item => item.DisplayName, StringComparer.Ordinal))
		{
			_assembler.AlignWord();
			_assembler.Mark(ArrayDescriptorLabel(type));
			_assembler.EmitLong(0); // Variable size.
			_assembler.EmitLong(type.IsReference ? 1u : 0u);
			_assembler.EmitAddress(RuntimeTypeDescriptorLabel("System.Object"));
			_assembler.EmitLong(0);
			_assembler.EmitLong(0);
			if (type.IsReference &&
				_arrayElementRuntimeTypes.TryGetValue(type.DisplayName, out var elementTarget))
			{
				_assembler.EmitAddress(RuntimeTypeTestIdentityLabel(elementTarget));
				_assembler.EmitLong(elementTarget.IsInterface
					? M68kRuntimeAbi.ArrayElementKindInterface
					: M68kRuntimeAbi.ArrayElementKindClass);
			}
		}

		foreach (var type in _boxedTypes.Values.OrderBy(item => item.DisplayName, StringComparer.Ordinal))
		{
			var boxedInterfaces = _boxedStructLayouts.TryGetValue(
				type.DisplayName,
				out var boxedLayout)
				? GetUsedInterfaceImplementations(boxedLayout)
				: [];
			_assembler.AlignWord();
			_assembler.Mark(BoxedTypeDescriptorLabel(type));
			var boxedPayloadBytes = _boxedStructLayouts.TryGetValue(
				type.DisplayName,
				out var boxedStructLayout)
					? boxedStructLayout.Size
					: type.Size;
			_assembler.EmitLong(checked((uint)(8 + Math.Max(4, boxedPayloadBytes))));
			_assembler.EmitLong(0);
			_assembler.EmitAddress(RuntimeTypeDescriptorLabel("System.ValueType"));
			_assembler.EmitLong(0);
			if (boxedInterfaces.Count == 0)
			{
				_assembler.EmitLong(0);
			}
			else
			{
				_assembler.EmitAddress(BoxedInterfaceMapLabel(type));
			}
		}

		foreach (var item in _boxedStructLayouts.OrderBy(item => item.Key, StringComparer.Ordinal))
		{
			var type = _boxedTypes[item.Key];
			var implementations = GetUsedInterfaceImplementations(item.Value);
			if (implementations.Count == 0)
			{
				continue;
			}

			_assembler.Mark(BoxedInterfaceMapLabel(type));
			_assembler.EmitLong((uint)implementations.Count);
			foreach (var implementation in implementations)
			{
				_assembler.EmitAddress(InterfaceIdentityLabel(implementation.Interface));
				_assembler.EmitAddress(BoxedInterfaceMethodTableLabel(type, implementation));
			}
			foreach (var implementation in implementations)
			{
				_assembler.Mark(BoxedInterfaceMethodTableLabel(type, implementation));
				foreach (var method in implementation.Methods)
				{
					_assembler.EmitAddress(BoxedInterfaceThunkLabel(type, implementation, method));
				}
			}
		}

		foreach (var type in _delegateTypes.Values.OrderBy(item => item.DisplayName, StringComparer.Ordinal))
		{
			_assembler.AlignWord();
			_assembler.Mark(DelegateTypeDescriptorLabel(type));
			_assembler.EmitLong(M68kRuntimeAbi.DelegateObjectBytes);
			_assembler.EmitLong(M68kRuntimeAbi.DelegateReferenceBitmap);
			_assembler.EmitAddress(RuntimeTypeDescriptorLabel("System.MulticastDelegate"));
			_assembler.EmitLong(0);
			_assembler.EmitLong(0);
		}
		if (_delegateTypes.Count != 0)
		{
			_assembler.AlignWord();
			_assembler.Mark(DelegateMulticastDescriptorTableLabel);
			_assembler.EmitLong(0);
			_assembler.EmitLong(0);
			for (var count = 2; count <= M68kRuntimeAbi.DelegateMaximumInvocationCount; count++)
			{
				_assembler.EmitAddress(DelegateMulticastDescriptorLabel(count));
			}
			for (var count = 2; count <= M68kRuntimeAbi.DelegateMaximumInvocationCount; count++)
			{
				_assembler.AlignWord();
				_assembler.Mark(DelegateMulticastDescriptorLabel(count));
				_assembler.EmitLong(checked((uint)(
					M68kRuntimeAbi.DelegateInvocationTailOffset + count * 4)));
				var bitmap = count == M68kRuntimeAbi.DelegateMaximumInvocationCount
					? 0xFFFF_FFF0u
					: checked(((1u << count) - 1u) << 4);
				_assembler.EmitLong(bitmap);
				_assembler.EmitAddress(RuntimeTypeDescriptorLabel("System.MulticastDelegate"));
				_assembler.EmitLong(0);
				_assembler.EmitLong(0);
			}
		}

		EmitRuntimeTypeDescriptorData();
		EmitAmigaUnhandledExceptionRequesterData();

		if (M68kCompiler.IsManagedRuntime(_request))
		{
			EmitGcConfigData();
		}
		if (M68kCompiler.IsManagedRuntime(_request) ||
			_request.Imports.ContainsKey(M68kRuntimeImports.GcCollect))
		{
			EmitManagedPoolRuntimeData();
		}
	}

	private void EmitExportAdapter(CilExport export, bool initializesPlatformBase)
	{
		_assembler.AlignWord();
		_assembler.Mark(ExportLabel(export.Name));
		EmitPushRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.D4,
			M68kRegister.D5,
			M68kRegister.D6,
			M68kRegister.D7,
			M68kRegister.A2,
			M68kRegister.A3,
			M68kRegister.A4,
			M68kRegister.A5,
			M68kRegister.A6
		});
		for (var index = export.ParameterRegisters.Count - 1; index >= 0; index--)
		{
			EmitPushRegister(export.ParameterRegisters[index]);
		}

		var internalAbi = GetInternalCallAbi(export.Method);
		EmitPrepareInternalCallFromDeclarationOrderedStack(export.Method, internalAbi);
		if (initializesPlatformBase)
		{
			EmitInitializePlatformBases();
		}
		_assembler.EmitBsr(MethodLabel(export.Method));
		EmitReleaseStackBytes(internalAbi.StackBytes);

		EmitPopRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.D4,
			M68kRegister.D5,
			M68kRegister.D6,
			M68kRegister.D7,
			M68kRegister.A2,
			M68kRegister.A3,
			M68kRegister.A4,
			M68kRegister.A5,
			M68kRegister.A6
		});

		if (IsInternalAddressReturn(export.Method.Signature.ReturnType))
		{
			EmitMoveRegister(M68kRegister.A0, export.ReturnRegister);
		}
		else
		{
			EmitMoveReturnFromD0(export.ReturnRegister);
		}
		_assembler.EmitWord(0x4E75); // RTS
	}

	private static bool RequiresPlatformBaseInitialization(GeneratedPlatformBase platformBase) =>
		platformBase.Binding.BaseSource == M68kExternalBaseSource.CachedPointer ||
		platformBase.Binding.BaseSource == M68kExternalBaseSource.WritableSlot &&
		platformBase.Binding.SourceAddress != 0;

	private void EmitInitializePlatformBases()
	{
		_loadedPlatformBase = null;
		foreach (var platformBase in _usedPlatformBases.Values
			.Where(item => item.Binding.BaseSource == M68kExternalBaseSource.CachedPointer)
			.DistinctBy(item => (item.Binding.CacheRegister, item.Binding.SourceAddress)))
		{
			EmitLoadAddressRegisterFromMemory(
				platformBase.Binding.CacheRegister!.Value,
				platformBase.Binding.SourceAddress);
		}

		foreach (var platformBase in _usedPlatformBases.Values
			.Where(item =>
				item.Binding.BaseSource == M68kExternalBaseSource.WritableSlot &&
				item.Binding.SourceAddress != 0)
			.OrderBy(item => item.Binding.Identity, StringComparer.Ordinal))
		{
			EmitLoadAddressRegisterFromMemory(
				platformBase.Binding.BaseRegister,
				platformBase.Binding.SourceAddress);
			EmitStoreRegisterDirectToLabel(
				platformBase.Binding.BaseRegister,
				platformBase.Label!);
			_loadedPlatformBase = platformBase;
		}
	}

	private void EmitPushRegister(M68kRegister register)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2F00 | (int)register));
			return;
		}

		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x4850 | addressRegister)); // PEA (An)
	}

	private void EmitPushByteRegister(M68kRegister register)
	{
		if (register > M68kRegister.D7)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Byte values can only be pushed from data registers.");
		}

		_assembler.EmitWord((ushort)(0x1F00 | (int)register)); // MOVE.B Dn,-(A7)
	}

	private void EmitPushByteFrameSlot(short displacement)
	{
		EmitLoadByteFromFrame(M68kRegister.D0, displacement);
		EmitPushByteRegister(M68kRegister.D0);
	}

	private void EmitPushRegisters(ReadOnlySpan<M68kRegister> registers)
	{
		if (registers.Length < 3)
		{
			foreach (var register in registers)
			{
				EmitPushRegister(register);
			}
			return;
		}

		_assembler.EmitWord(0x48E7); // MOVEM.L regs,-(A7)
		_assembler.EmitWord(EncodeMovemPredecrementMask(registers));
	}

	private void EmitPopRegister(M68kRegister register)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x201F | ((int)register << 9)));
			return;
		}

		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x205F | (addressRegister << 9)));
	}

	private void EmitPopByteRegister(
		M68kRegister register,
		CilStackValueKind kind,
		bool widen = true)
	{
		if (register > M68kRegister.D7)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Byte values can only be popped into data registers.");
		}

		InvalidatePlatformBaseIfWritingRegister(register);
		if (widen)
		{
			EmitImmediateToRegister(register, 0);
		}
		_assembler.EmitWord((ushort)(0x101F | ((int)register << 9))); // MOVE.B (A7)+,Dn
		if (widen && CilStackValueLayout.IsSignedByte(kind))
		{
			if (_request.Cpu == M68kCpuTarget.M68000)
			{
				_assembler.EmitWord((ushort)(0x4880 | (int)register)); // EXT.W Dn
				_assembler.EmitWord((ushort)(0x48C0 | (int)register)); // EXT.L Dn
			}
			else
			{
				_assembler.EmitWord((ushort)(0x49C0 | (int)register)); // EXTB.L Dn
				_assembler.EmitWord((ushort)(0x0280 | ((int)register << 9))); // ANDI.L #$FF,Dn
				_assembler.EmitLong(0x000000FF);
				EmitSignExtendByteMask(register);
			}
		}
	}

	private void EmitPopByteFrameSlot(short displacement)
	{
		// Only the low byte belongs to the managed value. Byte-width loads use the
		// same representation, so the rest of the ABI home need not be cleared.
		EmitPopByteRegister(
			M68kRegister.D0,
			CilStackValueKind.UnsignedByte,
			widen: false);
		EmitStoreByteToFrame(M68kRegister.D0, displacement);
	}

	private void EmitPopRegisters(ReadOnlySpan<M68kRegister> registers)
	{
		if (registers.Length < 3)
		{
			for (var index = registers.Length - 1; index >= 0; index--)
			{
				EmitPopRegister(registers[index]);
			}
			return;
		}

		_assembler.EmitWord(0x4CDF); // MOVEM.L (A7)+,regs
		_assembler.EmitWord(EncodeMovemRegisterMask(registers));
	}

	private static ushort EncodeMovemRegisterMask(ReadOnlySpan<M68kRegister> registers)
	{
		var mask = 0;
		foreach (var register in registers)
		{
			mask |= 1 << MovemRegisterBit(register);
		}

		return (ushort)mask;
	}

	private static ushort EncodeMovemPredecrementMask(ReadOnlySpan<M68kRegister> registers)
	{
		var mask = 0;
		foreach (var register in registers)
		{
			mask |= 1 << (15 - MovemRegisterBit(register));
		}

		return (ushort)mask;
	}

	private static int MovemRegisterBit(M68kRegister register) =>
		register <= M68kRegister.D7
			? (int)register
			: 8 + ((int)register - (int)M68kRegister.A0);

	private void EmitMoveReturnFromD0(M68kRegister register)
	{
		if (register == M68kRegister.D0)
		{
			return;
		}
		InvalidatePlatformBaseIfWritingRegister(register);

		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2000 | ((int)register << 9)));
			return;
		}

		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2040 | (addressRegister << 9)));
	}

	private void EmitMoveRegister(M68kRegister source, M68kRegister destination)
	{
		if (source == destination)
		{
			return;
		}
		InvalidatePlatformBaseIfWritingRegister(destination);
		var sourceIsAddress = source >= M68kRegister.A0;
		var sourceIndex = sourceIsAddress
			? (int)source - (int)M68kRegister.A0
			: (int)source;
		if (destination <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(
				0x2000 |
				((int)destination << 9) |
				(sourceIsAddress ? 0x0008 : 0) |
				sourceIndex));
		}
		else
		{
			var destinationIndex = (int)destination - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(
				0x2040 |
				(destinationIndex << 9) |
				(sourceIsAddress ? 0x0008 : 0) |
				sourceIndex));
		}
	}

	private void EmitLoadAddressRegisterAbsolute(M68kRegister register, string label)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2079 | (index << 9)));
		_assembler.EmitAddress(label);
	}

	private void EmitLoadAddressRegisterPcRelative(M68kRegister register, string label)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x207A | (index << 9)));
		_assembler.EmitPcRelativeWord(label);
	}

	private void EmitAddressImmediateToRegister(M68kRegister register, string label)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x203C | ((int)register << 9))); // MOVE.L #label,Dn
		}
		else
		{
			var index = (int)register - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(0x207C | (index << 9))); // MOVEA.L #label,An
		}
		_assembler.EmitAddress(label);
	}

	private void EmitLoadD0FromLabel(string label)
	{
		_assembler.EmitWord(0x2039); // MOVE.L abs.l,D0
		_assembler.EmitAddress(label);
	}

	private void EmitLoadD1FromLabel(string label)
	{
		_assembler.EmitWord(0x2239); // MOVE.L abs.l,D1
		_assembler.EmitAddress(label);
	}

	private void EmitLoadD0FromMemory(uint address)
	{
		if (address <= short.MaxValue)
		{
			_assembler.EmitWord(0x2038); // MOVE.L abs.w,D0
			_assembler.EmitWord((ushort)address);
			return;
		}

		_assembler.EmitWord(0x2039); // MOVE.L abs.l,D0
		_assembler.EmitLong(address);
	}

	private void EmitLoadA0FromLabel(string label)
	{
		_assembler.EmitWord(0x2079); // MOVEA.L abs.l,A0
		_assembler.EmitAddress(label);
	}

	private void EmitStoreD0ToLabel(string label)
	{
		EmitPushD0();
		_assembler.EmitWord(0x23DF); // MOVE.L (A7)+,abs.l
		_assembler.EmitAddress(label);
	}

	private void EmitStoreD0DirectToLabel(string label)
	{
		_assembler.EmitWord(0x23C0); // MOVE.L D0,abs.l
		_assembler.EmitAddress(label);
	}

	private void EmitStoreRegisterDirectToLabel(M68kRegister register, string label)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x23C0 | (int)register)); // MOVE.L Dn,abs.l
		}
		else
		{
			_assembler.EmitWord((ushort)(0x23C8 | ((int)register - (int)M68kRegister.A0))); // MOVE.L An,abs.l
		}
		_assembler.EmitAddress(label);
	}

	private void EmitStoreFrameLongDirectToLabel(short displacement, string label)
	{
		_assembler.EmitWord(0x23EF); // MOVE.L d16(A7),abs.l
		_assembler.EmitWord(unchecked((ushort)displacement));
		_assembler.EmitAddress(label);
	}

	private void EmitClearLabel(string label)
	{
		// These labels are compiler-owned writable slots, so CLR's MC68000
		// read-before-write is safe even when general memory uses MOVE.
		_assembler.EmitWord(0x42B9); // CLR.L abs.l
		_assembler.EmitAddress(label);
	}

	private void EmitLoadAddressRegisterImmediate(M68kRegister register, uint value)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x207C | (index << 9)));
		_assembler.EmitLong(value);
	}

	private void EmitImmediateToRegister(M68kRegister register, int value)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		if (register <= M68kRegister.D7)
		{
			if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
			{
				_assembler.EmitWord((ushort)(
					0x7000 |
					((int)register << 9) |
					(byte)value));
				return;
			}
			_assembler.EmitWord((ushort)(0x203C | ((int)register << 9)));
			_assembler.EmitLong(unchecked((uint)value));
			return;
		}

		EmitLoadAddressRegisterImmediate(register, unchecked((uint)value));
	}

	private void EmitLoadAddressRegisterFromMemory(M68kRegister register, uint address)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		var index = (int)register - (int)M68kRegister.A0;
		if (address <= short.MaxValue)
		{
			_assembler.EmitWord((ushort)(0x2078 | (index << 9)));
			_assembler.EmitWord((ushort)address);
			return;
		}
		_assembler.EmitWord((ushort)(0x2079 | (index << 9)));
		_assembler.EmitLong(address);
	}

	private void EmitBaseRelativeJsr(M68kRegister register, short displacement)
	{
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x4EA8 | index));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitBaseRelativeJmp(M68kRegister register, short displacement)
	{
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x4EE8 | index));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private static M68kCompilationException FieldMismatch(
		CilMethod method,
		CilInstruction instruction,
		CilField field) =>
		new(
			M68kDiagnosticIds.InvalidMetadata,
			$"Opcode '{instruction.OpCode.Name}' does not match field '{field.DisplayName}'.",
			method.DisplayName,
			instruction.Offset);

	private static bool IsArrayAccess(OpCode op) =>
		op == OpCodes.Ldelem_I1 ||
		op == OpCodes.Ldelem_U1 ||
		op == OpCodes.Ldelem_I2 ||
		op == OpCodes.Ldelem_U2 ||
		op == OpCodes.Ldelem_I4 ||
		op == OpCodes.Ldelem_U4 ||
		op == OpCodes.Ldelem_I8 ||
		op == OpCodes.Ldelem_I ||
		op == OpCodes.Ldelem_Ref ||
		op == OpCodes.Ldelema ||
		op == OpCodes.Ldelem ||
		op == OpCodes.Stelem_I1 ||
		op == OpCodes.Stelem_I2 ||
		op == OpCodes.Stelem_I4 ||
		op == OpCodes.Stelem_I8 ||
		op == OpCodes.Stelem_I ||
		op == OpCodes.Stelem_Ref ||
		op == OpCodes.Stelem;

	private static bool IsIndirectLoad(OpCode op) =>
		op == OpCodes.Ldind_I1 ||
		op == OpCodes.Ldind_U1 ||
		op == OpCodes.Ldind_I2 ||
		op == OpCodes.Ldind_U2 ||
		op == OpCodes.Ldind_I4 ||
		op == OpCodes.Ldind_U4 ||
		op == OpCodes.Ldind_I ||
		op == OpCodes.Ldind_I8 ||
		op == OpCodes.Ldind_R4 ||
		op == OpCodes.Ldind_Ref;

	private static bool IsIndirectStore(OpCode op) =>
		op == OpCodes.Stind_I1 ||
		op == OpCodes.Stind_I2 ||
		op == OpCodes.Stind_I4 ||
		op == OpCodes.Stind_I ||
		op == OpCodes.Stind_I8 ||
		op == OpCodes.Stind_R4 ||
		op == OpCodes.Stind_Ref ||
		op == OpCodes.Stobj;

	private static MemoryAccess GetArrayAccess(OpCode op) =>
		op.Value switch
		{
			var value when value == OpCodes.Ldelem_I1.Value => new(1, SignExtend: true, IsStore: false),
			var value when value == OpCodes.Ldelem_U1.Value => new(1, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldelem_I2.Value => new(2, SignExtend: true, IsStore: false),
			var value when value == OpCodes.Ldelem_U2.Value => new(2, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldelem_I8.Value => new(8, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldelem_I4.Value ||
				value == OpCodes.Ldelem_U4.Value ||
				value == OpCodes.Ldelem_I.Value ||
				value == OpCodes.Ldelem_Ref.Value ||
				value == OpCodes.Ldelema.Value => new(4, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Stelem_I1.Value => new(1, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stelem_I2.Value => new(2, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stelem_I8.Value => new(8, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stelem_I4.Value ||
				value == OpCodes.Stelem_I.Value ||
				value == OpCodes.Stelem_Ref.Value => new(4, SignExtend: false, IsStore: true),
			_ => throw new InvalidOperationException($"Unsupported array access opcode '{op.Name}'.")
		};

	private MemoryAccess GetGenericArrayAccess(CilType type, bool isStore)
	{
		var size = _module.IsTransparentScalarType(type) ? 4 : type.Size;
		if (size is not (1 or 2 or 4 or 8))
		{
			throw new InvalidOperationException(
				$"Unsupported generic array element type '{type.DisplayName}'.");
		}
		return new MemoryAccess(
			size,
			SignExtend: !isStore && type.Kind == CilTypeKind.SignedInteger,
			IsStore: isStore);
	}
	private static MemoryAccess GetIndirectAccess(OpCode op) =>
		op.Value switch
		{
			var value when value == OpCodes.Ldind_I1.Value => new(1, SignExtend: true, IsStore: false),
			var value when value == OpCodes.Ldind_U1.Value => new(1, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldind_I2.Value => new(2, SignExtend: true, IsStore: false),
			var value when value == OpCodes.Ldind_U2.Value => new(2, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldind_I4.Value ||
				value == OpCodes.Ldind_U4.Value ||
				value == OpCodes.Ldind_I.Value ||
				value == OpCodes.Ldind_R4.Value ||
				value == OpCodes.Ldind_Ref.Value => new(4, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldind_I8.Value => new(8, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Stind_I1.Value => new(1, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stind_I2.Value => new(2, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stind_I4.Value ||
				value == OpCodes.Stind_I.Value ||
				value == OpCodes.Stind_R4.Value ||
				value == OpCodes.Stind_Ref.Value => new(4, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stind_I8.Value => new(8, SignExtend: false, IsStore: true),
			_ => throw new InvalidOperationException($"Unsupported indirect access opcode '{op.Name}'.")
		};

	private void EmitLoadD0FromA0Displacement(
		int size,
		bool signExtend,
		short displacement)
	{
		if (size is 1 or 2 && !signExtend)
		{
			_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		}
		else if (size == 1)
		{
			// MOVE.B does not alter the upper 24 bits. Clear them before a
			// signed byte load so both EXT.W/EXT.L and EXTB.L see a clean value.
			_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		}

		_assembler.EmitWord(size switch
		{
			1 => displacement == 0 ? (ushort)0x1010 : (ushort)0x1028, // MOVE.B (d16,A0),D0
			2 => displacement == 0 ? (ushort)0x3010 : (ushort)0x3028, // MOVE.W (d16,A0),D0
			4 => displacement == 0 ? (ushort)0x2010 : (ushort)0x2028, // MOVE.L (d16,A0),D0
			_ => throw new ArgumentOutOfRangeException(nameof(size))
		});
		if (displacement != 0)
		{
			_assembler.EmitWord(unchecked((ushort)displacement));
		}

		if (signExtend && size == 1)
		{
			EmitSignExtendByteToLongD0();
		}
		else if (signExtend && size == 2)
		{
			_assembler.EmitWord(0x48C0); // EXT.L D0
		}
	}

	private void EmitNormalizeIndirectPointer()
	{
		_assembler.EmitWord(0x41E8); // LEA -12(A0),A0
		_assembler.EmitWord(unchecked((ushort)-12));
	}

	private void EmitStoreD0ToA0Displacement(int size, short displacement)
	{
		_assembler.EmitWord(size switch
		{
			1 => displacement == 0 ? (ushort)0x1080 : (ushort)0x1140, // MOVE.B D0,(d16,A0)
			2 => displacement == 0 ? (ushort)0x3080 : (ushort)0x3140, // MOVE.W D0,(d16,A0)
			4 => displacement == 0 ? (ushort)0x2080 : (ushort)0x2140, // MOVE.L D0,(d16,A0)
			_ => throw new ArgumentOutOfRangeException(nameof(size))
		});
		if (displacement != 0)
		{
			_assembler.EmitWord(unchecked((ushort)displacement));
		}
	}

	private void EmitScaleD0(int size)
	{
		for (var index = 1; index < size; index <<= 1)
		{
			_assembler.EmitWord(0xE388); // LSL.L #1,D0
		}
	}

	private void EmitScaleD1(int size)
	{
		for (var index = 1; index < size; index <<= 1)
		{
			_assembler.EmitWord(0xE389); // LSL.L #1,D1
		}
	}

	private bool TryEmitConversion(OpCode op)
	{
		if (op == OpCodes.Conv_Ovf_I4_Un)
		{
			EmitPopStackValue(M68kRegister.D0, CurrentStackKindOrLong(), widen: true);
			var inRange = UniqueLabel("checked-uint32-to-int32-in-range");
			_assembler.EmitWord(0x4A80); // TST.L D0
			_assembler.EmitBranch(M68kCondition.Plus, inRange);
			EmitExceptionRaise(reason: 4, hasException: false);
			_assembler.Mark(inRange);
			EmitPushD0();
			return true;
		}

		if (op == OpCodes.Conv_I || op == OpCodes.Conv_U ||
			op == OpCodes.Conv_I4 || op == OpCodes.Conv_U4)
		{
			if (CilStackValueLayout.IsSmall(CurrentStackKindOrLong()))
			{
				EmitPopStackValue(M68kRegister.D0, CurrentStackKindOrLong(), widen: true);
				EmitPushD0();
			}

			return true;
		}

		if (op != OpCodes.Conv_I1 && op != OpCodes.Conv_U1 &&
			op != OpCodes.Conv_I2 && op != OpCodes.Conv_U2)
		{
			return false;
		}

		var targetKind = op == OpCodes.Conv_I1
			? CilStackValueKind.SignedByte
			: op == OpCodes.Conv_U1
				? CilStackValueKind.UnsignedByte
				: op == OpCodes.Conv_I2
					? CilStackValueKind.SignedWord
					: CilStackValueKind.UnsignedWord;
		var sourceKind = CurrentStackKindOrLong();
		if (sourceKind == targetKind)
		{
			return true;
		}

		EmitPopStackValue(M68kRegister.D0, sourceKind, widen: true);
		EmitNormalizeArithmeticResult(targetKind);
		EmitPushArithmeticResult(targetKind);
		return true;
	}

	private void EmitPushConstant(int value)
	{
		if (value == 0)
		{
			_assembler.EmitWord(0x42A7); // CLR.L -(A7)
			return;
		}

		if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
		{
			_assembler.EmitWord((ushort)(0x7000 | (byte)value)); // MOVEQ #value,D0
			EmitPushD0();
			return;
		}

		_assembler.EmitWord(0x2F3C); // MOVE.L #value,-(A7)
		_assembler.EmitLong(unchecked((uint)value));
	}

	private void EmitPushLongConstant(long value)
	{
		_assembler.EmitWord(0x2F3C); // MOVE.L #high,-(A7)
		_assembler.EmitLong((uint)(value >> 32));
		_assembler.EmitWord(0x2F3C); // MOVE.L #low,-(A7)
		_assembler.EmitLong(unchecked((uint)value));
	}

	private void EmitPushFrameSlot(short displacement)
	{
		if (displacement == 0)
		{
			_assembler.EmitWord(0x2F17); // MOVE.L (A7),-(A7)
			return;
		}

		_assembler.EmitWord(0x2F2F); // MOVE.L d16(A7),-(A7)
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void InvalidatePlatformBaseIfWritingRegister(M68kRegister register)
	{
		if (_loadedPlatformBase?.Binding.BaseRegister == register)
		{
			_loadedPlatformBase = null;
		}
	}

	private void EmitPushFrameValue(short displacement, int longs)
	{
		if (longs == 2)
		{
			EmitLoadRegisterFromStack(M68kRegister.D0, displacement);
			EmitLoadRegisterFromStack(M68kRegister.D1, checked(displacement + 4));
			EmitPushRegister(M68kRegister.D0);
			EmitPushRegister(M68kRegister.D1);
			return;
		}

		for (var index = 0; index < longs; index++)
		{
			EmitPushFrameSlot(checked((short)(displacement + (index * 4))));
		}
	}

	private void EmitPushFrameAddress(short displacement)
	{
		if (displacement == 0)
		{
			_assembler.EmitWord(0x4857); // PEA (A7)
			return;
		}

		_assembler.EmitWord(0x486F); // PEA d16(A7)
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitPushAlignedLocalAddress(short displacement)
	{
		var candidate = checked((short)(displacement + 2));
		_assembler.EmitWord(0x41EF); // LEA d16(A7),A0
		_assembler.EmitWord(unchecked((ushort)candidate));
		_assembler.EmitWord(0x2008); // MOVE.L A0,D0
		_assembler.EmitWord(0x0240); // ANDI.W #$FFFC,D0
		_assembler.EmitWord(0xFFFC);
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
	}

	private void EmitPopFrameSlot(short displacement)
	{
		if (displacement == 0)
		{
			_assembler.EmitWord(0x2E9F); // MOVE.L (A7)+,(A7)
			return;
		}

		_assembler.EmitWord(0x2F5F); // MOVE.L (A7)+,d16(A7)
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitPopFrameValue(short displacement, int longs)
	{
		if (longs == 2)
		{
			EmitPopRegister(M68kRegister.D1);
			EmitPopRegister(M68kRegister.D0);
			EmitStoreRegisterToFrame(M68kRegister.D0, displacement);
			EmitStoreRegisterToFrame(M68kRegister.D1, checked((short)(displacement + 4)));
			return;
		}

		for (var index = longs - 1; index >= 0; index--)
		{
			EmitPopFrameSlot(checked((short)(displacement + (index * 4))));
		}
	}

	private void EmitClearFrameSlot(short displacement)
	{
		if (displacement == 0)
		{
			_assembler.EmitWord(0x4297); // CLR.L (A7)
			return;
		}

		_assembler.EmitWord(0x42AF); // CLR.L d16(A7)
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitClearFrameRegion(short startDisplacement, int longs)
	{
		if (longs == 0)
		{
			return;
		}

		if (longs <= 3)
		{
			for (var index = 0; index < longs; index++)
			{
				EmitClearFrameSlot(checked((short)(startDisplacement + (index * 4))));
			}
			return;
		}

		var loop = UniqueLabel("frame_clear");
		_assembler.EmitWord(0x41EF); // LEA d16(A7),A0
		_assembler.EmitWord(unchecked((ushort)startDisplacement));
		EmitImmediateToRegister(M68kRegister.D0, longs - 1);
		_assembler.Mark(loop);
		_assembler.EmitWord(0x4298); // CLR.L (A0)+
		_assembler.EmitDbra(0, loop);
	}

	private void EmitClearAddressLong(int displacement)
	{
		if (UseClr)
		{
			if (displacement == 0)
			{
				_assembler.EmitWord(0x4290); // CLR.L (A0)
				return;
			}

			_assembler.EmitWord(0x42A8); // CLR.L d16(A0)
			_assembler.EmitWord(unchecked((ushort)displacement));
			return;
		}

		EmitStoreD0ToA0Displacement(4, checked((short)displacement));
	}

	private void EmitClearAddressRegion(int longs)
	{
		if (longs <= 0)
		{
			return;
		}

		if (longs <= 3)
		{
			for (var index = 0; index < longs; index++)
			{
				EmitClearAddressLong(index * 4);
			}
			return;
		}

		if (!UseClr)
		{
			EmitImmediateToRegister(M68kRegister.D1, 0);
		}
		EmitImmediateToRegister(M68kRegister.D0, longs - 1);
		var loop = UniqueLabel("address_clear");
		_assembler.Mark(loop);
		if (UseClr)
		{
			_assembler.EmitWord(0x4298); // CLR.L (A0)+
		}
		else
		{
			_assembler.EmitWord(0x20C1); // MOVE.L D1,(A0)+
		}
		_assembler.EmitDbra((int)M68kRegister.D0, loop);
	}

	private void EmitAllocateFrame(int bytes)
	{
		if (bytes == 0)
		{
			return;
		}
		if (bytes <= 8)
		{
			var encodedCount = bytes == 8 ? 0 : bytes;
			_assembler.EmitWord((ushort)(0x518F | (encodedCount << 9))); // SUBQ.L #bytes,A7
			return;
		}

		_assembler.EmitWord(0x4FEF); // LEA -frame(A7),A7
		_assembler.EmitWord(unchecked((ushort)(short)-bytes));
	}

	private void EmitReleaseFrame(int bytes)
	{
		EmitReleaseStackBytes(bytes);
	}

	private void EmitPopBinaryOperands(bool widen = true)
	{
		EmitPopStackValue(M68kRegister.D1, CurrentStackKindOrLong(), widen);
		EmitPopStackValue(M68kRegister.D0, CurrentStackKindOrLong(1), widen);
	}

	private void EmitPopD0() => _assembler.EmitWord(0x201F);

	private void EmitPushD0() => _assembler.EmitWord(0x2F00);

	private void EmitDiscardStackArguments(int count)
	{
		EmitReleaseStackBytes(checked(count * 4));
	}

	private void EmitReleaseStackBytes(int bytes)
	{
		if (bytes == 0)
		{
			return;
		}

		if (bytes <= 8)
		{
			var encodedCount = bytes == 8 ? 0 : bytes;
			_assembler.EmitWord((ushort)(0x508F | (encodedCount << 9))); // ADDQ.L #bytes,A7
			return;
		}

		if (bytes <= short.MaxValue)
		{
			_assembler.EmitWord(0x4FEF); // LEA bytes(A7),A7
			_assembler.EmitWord((ushort)bytes);
			return;
		}

		_assembler.EmitWord(0xDFFC); // ADDA.L #bytes,A7
		_assembler.EmitLong((uint)bytes);
	}

	private static bool TryGetConstant(CilInstruction instruction, out int value)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldc_I4_M1)
		{
			value = -1;
			return true;
		}

		if (op.Value >= OpCodes.Ldc_I4_0.Value && op.Value <= OpCodes.Ldc_I4_8.Value)
		{
			value = op.Value - OpCodes.Ldc_I4_0.Value;
			return true;
		}

		if (op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4)
		{
			value = Convert.ToInt32(instruction.Operand);
			return true;
		}

		value = 0;
		return false;
	}

	private static bool TryGetArgumentIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value)
		{
			index = op.Value - OpCodes.Ldarg_0.Value;
			return true;
		}

		if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = 0;
		return false;
	}

	private static bool TryGetLoadLocalIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldloc_0.Value && op.Value <= OpCodes.Ldloc_3.Value)
		{
			index = op.Value - OpCodes.Ldloc_0.Value;
			return true;
		}

		if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = 0;
		return false;
	}

	private static bool TryGetLoadLocalAddressIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = 0;
		return false;
	}

	private static bool TryGetLoadArgumentAddressIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldarga || op == OpCodes.Ldarga_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = 0;
		return false;
	}

	private static bool TryGetStoreLocalIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Stloc_0.Value && op.Value <= OpCodes.Stloc_3.Value)
		{
			index = op.Value - OpCodes.Stloc_0.Value;
			return true;
		}

		if (op == OpCodes.Stloc || op == OpCodes.Stloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = 0;
		return false;
	}

	private static bool IsUnconditionalBranch(OpCode op) =>
		op == OpCodes.Br || op == OpCodes.Br_S ||
		op == OpCodes.Leave || op == OpCodes.Leave_S;

	private static bool TryGetRelationalBranch(OpCode op, out M68kCondition condition)
	{
		if (op == OpCodes.Beq || op == OpCodes.Beq_S)
		{
			condition = M68kCondition.Equal;
			return true;
		}

		if (op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S)
		{
			condition = M68kCondition.NotEqual;
			return true;
		}

		if (op == OpCodes.Bge || op == OpCodes.Bge_S)
		{
			condition = M68kCondition.GreaterOrEqual;
			return true;
		}

		if (op == OpCodes.Bgt || op == OpCodes.Bgt_S)
		{
			condition = M68kCondition.GreaterThan;
			return true;
		}

		if (op == OpCodes.Ble || op == OpCodes.Ble_S)
		{
			condition = M68kCondition.LessOrEqual;
			return true;
		}

		if (op == OpCodes.Blt || op == OpCodes.Blt_S)
		{
			condition = M68kCondition.LessThan;
			return true;
		}

		if (op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S)
		{
			condition = M68kCondition.CarryClear;
			return true;
		}

		if (op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S)
		{
			condition = M68kCondition.Higher;
			return true;
		}

		if (op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S)
		{
			condition = M68kCondition.LowerOrSame;
			return true;
		}

		if (op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S)
		{
			condition = M68kCondition.CarrySet;
			return true;
		}

		condition = default;
		return false;
	}

	private static void ValidateLocal(CilMethod method, CilInstruction instruction, int index)
	{
		if ((uint)index >= (uint)method.Locals.Length)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Local index {index} is outside the local signature.",
				method.DisplayName,
				instruction.Offset);
		}
	}

	private static void ValidateArgument(CilMethod method, CilInstruction instruction, int index)
	{
		if ((uint)index >= (uint)method.ParameterCount)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Argument index {index} is outside the method signature.",
				method.DisplayName,
				instruction.Offset);
		}
	}


	private string MethodLabel(CilMethod method)
	{
		if (_foldedMethodAliases.TryGetValue(method.Identity, out var canonical))
		{
			method = canonical;
		}

		return $"method:{ModuleLabelPrefix(method.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method.Handle):X8}" +
			ConstructionLabelSuffix(method.Construction);
	}

	private string MethodEndLabel(CilMethod method) =>
		$"{MethodLabel(method)}:end";

	private string IlLabel(CilMethod method, int offset) =>
		$"{MethodLabel(method)}:IL_{offset:X4}";

	private string ExceptionBoundaryLabel(CilMethod method, int offset) =>
		method.Instructions.Count != 0 &&
			offset == method.Instructions[^1].NextOffset
			? MethodEndLabel(method)
			: IlLabel(method, offset);

	private string ControlFlowTargetLabel(CilMethod method, int offset) =>
		method.Instructions.Count != 0 &&
			offset == method.Instructions[^1].NextOffset
			? MethodEndLabel(method)
			: IlLabel(method, offset);

	private static string TypeDescriptorLabel(TypeDefinitionHandle handle) =>
		$"type:{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle):X8}";

	private string TypeDescriptorLabel(CilTypeLayout layout) =>
		$"type:{ModuleLabelPrefix(layout.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(layout.Handle):X8}" +
		ConstructionLabelSuffix(layout.ConstructedType?.DisplayName ?? string.Empty);

	private string VirtualTableLabel(CilTypeLayout layout) =>
		$"{TypeDescriptorLabel(layout)}:vtable";

	private IReadOnlyList<CilInterfaceImplementation> GetUsedInterfaceImplementations(
		CilTypeLayout layout) =>
		_usedInterfaces.Values
			.Select(interfaceDefinition =>
				_module.TryGetInterfaceImplementation(layout, interfaceDefinition))
			.Where(static implementation => implementation is not null)
			.Select(static implementation => implementation!)
			.OrderBy(
				implementation => implementation.Interface.Identity.ModuleName,
				StringComparer.Ordinal)
			.ThenBy(implementation =>
				System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(
					implementation.Interface.Identity.Handle))
			.ThenBy(
				implementation => implementation.Interface.Identity.Construction,
				StringComparer.Ordinal)
			.ToArray();

	private string InterfaceIdentityLabel(CilInterfaceDefinition interfaceDefinition) =>
		$"interface:{ModuleLabelPrefix(interfaceDefinition.Identity.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(interfaceDefinition.Identity.Handle):X8}" +
		ConstructionLabelSuffix(interfaceDefinition.Identity.Construction);

	private string InterfaceMapLabel(CilTypeLayout layout) =>
		$"{TypeDescriptorLabel(layout)}:interfaces";

	private string InterfaceMethodTableLabel(CilInterfaceImplementation implementation) =>
		$"{InterfaceMapLabel(implementation.Type)}:{ModuleLabelPrefix(implementation.Interface.Identity.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(implementation.Interface.Identity.Handle):X8}" +
		ConstructionLabelSuffix(implementation.Interface.Identity.Construction);

	private string StaticFieldLabel(CilField field) =>
		$"static:{ModuleLabelPrefix(field.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(field.Handle):X8}" +
		(field.ConstructedDeclaringType is { } constructedType
			? $":constructed:{constructedType.DisplayName}"
			: string.Empty);

	private string TypeInitializationStateLabel(CilMethod initializer) =>
		$"type-init:{ModuleLabelPrefix(initializer.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(initializer.DeclaringType):X8}" +
		$"{ConstructionLabelSuffix(initializer.Construction)}:state";

	private string TypeInitializationExceptionLabel(CilMethod initializer) =>
		$"type-init:{ModuleLabelPrefix(initializer.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(initializer.DeclaringType):X8}" +
		$"{ConstructionLabelSuffix(initializer.Construction)}:exception";

	private string TypeInitializationFailureThunkLabel(CilMethod initializer) =>
		$"type-init:{ModuleLabelPrefix(initializer.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(initializer.DeclaringType):X8}" +
		$"{ConstructionLabelSuffix(initializer.Construction)}:failure";

	private string TypeInitializationWrapperLabel(CilMethod initializer) =>
		$"type-init:{ModuleLabelPrefix(initializer.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(initializer.DeclaringType):X8}" +
		$"{ConstructionLabelSuffix(initializer.Construction)}:wrapper";

	private static string ConstructionLabelSuffix(string construction) =>
		construction.Length == 0
			? string.Empty
			: $":constructed:{construction}";

	private string TypeInitializationWrapperInnerLabel(CilMethod initializer) =>
		$"{TypeInitializationWrapperLabel(initializer)}:inner";

	private bool TypeInitializerCanFail(CilMethod initializer) =>
		_usesExceptionRuntime && TypeInitializerRequiresExceptionCleanup(initializer);

	private bool TypeInitializerRequiresExceptionCleanup(CilMethod initializer) =>
		_request.ExceptionMode == M68kExceptionMode.Full &&
		MethodMayRaiseException(initializer);

	private string RuntimeTypeTestIdentityLabel(CilRuntimeTypeTarget target)
	{
		if (!target.Type.IsReference)
		{
			RegisterBoxedType(target.Type);
			return BoxedTypeDescriptorLabel(target.Type);
		}
		if (target.Type.DisplayName == "string")
		{
			_usesDynamicStrings = true;
			return "runtime:string-descriptor";
		}
		if (target.IsArray)
		{
			var elementType = target.Type.ElementType ??
				throw new InvalidOperationException("Array runtime type has no element type.");
			_arrayTypes.TryAdd(elementType.DisplayName, elementType);
			_arrayElementRuntimeTypes.TryAdd(
				elementType.DisplayName,
				_module.ResolveRuntimeTypeIdentity(elementType, target.ModuleName));
			return ArrayDescriptorLabel(elementType);
		}
		if (IsFrameworkDelegateType(target.Type))
		{
			RegisterDelegateType(target.Type);
			return DelegateTypeDescriptorLabel(target.Type);
		}
		if (target.IsInterface)
		{
			var interfaceDefinition = _module.GetRuntimeInterfaceDefinition(target);
			_usedInterfaces.TryAdd(interfaceDefinition.Identity, interfaceDefinition);
			return InterfaceIdentityLabel(interfaceDefinition);
		}
		if (target.IsConstructedGeneric)
		{
			var layout = _module.GetRuntimeTypeLayout(target);
			_usedTypeLayouts.TryAdd(layout.Identity, layout);
			_constructedTypeDescriptors.TryAdd(
				target.Type.DisplayName,
				(target.Type, layout));
			return ConstructedTypeDescriptorLabel(layout, target.Type);
		}
		if (target.Handle.Kind == HandleKind.TypeDefinition)
		{
			var layout = _module.GetRuntimeTypeLayout(target);
			_usedTypeLayouts.TryAdd(layout.Identity, layout);
			return TypeDescriptorLabel(layout);
		}
		RegisterRuntimeTypeDescriptor(target.Type.DisplayName);
		return RuntimeTypeDescriptorLabel(target.Type.DisplayName);
	}

	private void RegisterBoxedType(CilType type)
	{
		_boxedTypes.TryAdd(type.DisplayName, type);
		if (_module.TryGetReferenceFreeStructLayout(
				type,
				_module.AssemblyName,
				out var layout))
		{
			_boxedStructLayouts.TryAdd(type.DisplayName, layout);
		}
		RegisterRuntimeTypeDescriptor("System.ValueType");
	}

	private static string BoxedTypeDescriptorLabel(CilType type) =>
		$"boxed:{type.DisplayName}";

	private static string BoxedInterfaceMapLabel(CilType type) =>
		$"{BoxedTypeDescriptorLabel(type)}:interfaces";

	private string BoxedInterfaceMethodTableLabel(
		CilType type,
		CilInterfaceImplementation implementation) =>
		$"{BoxedInterfaceMapLabel(type)}:{ModuleLabelPrefix(implementation.Interface.Identity.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(implementation.Interface.Identity.Handle):X8}";

	private string BoxedInterfaceThunkLabel(
		CilType type,
		CilInterfaceImplementation implementation,
		CilMethod method) =>
		$"{BoxedInterfaceMethodTableLabel(type, implementation)}:unbox:{ModuleLabelPrefix(method.ModuleName)}{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method.Handle):X8}";

	private void RegisterDelegateType(CilType type)
	{
		_delegateTypes.TryAdd(type.DisplayName, type);
		RegisterRuntimeTypeDescriptor("System.MulticastDelegate");
	}

	private static bool IsFrameworkDelegateType(CilType type) =>
		type.DisplayName.StartsWith("System.Func`", StringComparison.Ordinal) ||
		type.DisplayName.StartsWith("System.Action`", StringComparison.Ordinal) ||
		StringComparer.Ordinal.Equals(type.DisplayName, "System.Action");

	private static string DelegateTypeDescriptorLabel(CilType type) =>
		$"delegate:{type.DisplayName}";

	private const string DelegateMulticastDescriptorTableLabel =
		"delegate:multicast-descriptor-table";

	private static string DelegateMulticastDescriptorLabel(int count) =>
		$"delegate:multicast:{count}";

	private string ConstructedTypeDescriptorLabel(CilTypeLayout layout, CilType type) =>
		StringComparer.Ordinal.Equals(layout.ConstructedType?.DisplayName, type.DisplayName)
			? TypeDescriptorLabel(layout)
			: $"{TypeDescriptorLabel(layout)}:constructed:{type.DisplayName}";

	private string ModuleLabelPrefix(string moduleName) =>
		string.IsNullOrEmpty(moduleName) || string.Equals(moduleName, _module.AssemblyName, StringComparison.Ordinal)
			? string.Empty
			: $"{moduleName}:";

	private string StringLabel(CilUserStringIdentity identity) =>
		$"string:{ModuleLabelPrefix(identity.ModuleName)}{identity.Token:X8}";

	private const string RuntimeEmptyStringLabel = "runtime:string-empty";
	private const string RuntimeEmptyCharArrayLabel = "runtime:char-array-empty";

	private string CStringLabel(CilUserStringIdentity identity) =>
		$"cstring:{ModuleLabelPrefix(identity.ModuleName)}{identity.Token:X8}";

	private const string MethodTableLabel = "runtime:method-table";
	private const string ExceptionTableLabel = "runtime:exception-table";


	private static string ArrayDescriptorLabel(CilType elementType) =>
		$"array:{elementType.DisplayName}";

	internal static string ExportLabel(string name) => $"export:{name}";

	private string UniqueLabel(string prefix) => $"generated:{prefix}:{_uniqueLabel++}";
}

internal sealed record GeneratedProgram(
	M68kAssembler Assembler,
	IReadOnlyList<CilMethod> Methods,
	IReadOnlyList<CilExport> Exports,
	IReadOnlyList<GeneratedPlatformBase> PlatformBases,
	string EntryLabel,
	IReadOnlyDictionary<CilMethodIdentity, string> MethodLabels,
	IReadOnlyDictionary<CilMethodIdentity, M68kMethodAllocationStatistics>
		AllocationStatistics,
	IReadOnlyDictionary<CilMethodIdentity, M68kTerminalDeadStoreStatistics>
		TerminalDeadStoreStatistics,
	IReadOnlyList<M68kLoopLayout> LoopLayouts);

internal sealed record GeneratedPlatformBase(
	M68kExternalCallConvention Binding,
	string? Label);

internal readonly record struct MemoryAccess(
	int Size,
	bool SignExtend,
	bool IsStore);
