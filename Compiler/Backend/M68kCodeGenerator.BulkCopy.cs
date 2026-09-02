/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly CilMethod? _bulkCopyMethod;
	private readonly HashSet<CilMethodIdentity> _bulkCopyProtectedMethods = [];

	private M68kBulkCopyTarget? PrepareBulkCopyTarget(
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions)
	{
		if (_request.BulkCopy is not { } options)
		{
			return null;
		}
		var clobbers = M68kRegisterSet.From(
			M68kRegister.D0, M68kRegister.D1, M68kRegister.A0, M68kRegister.A1);
		ImmutableArray<M68kRegister> registers;
		if (_bulkCopyMethod is { } provider)
		{
			var abi = GetInternalCallAbi(provider);
			if (abi.StackBytes != 0 || abi.Arguments.Length != 3 ||
				abi.Arguments.Any(static argument => argument.Register is null || argument.SlotLongs != 1))
			{
				throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature,
					"A bulk-copy provider must accept source, destination and byte count entirely in registers.",
					provider.DisplayName);
			}
			registers = abi.Arguments.Select(static argument => argument.Register!.Value).ToImmutableArray();
			var pending = new Stack<CilMethodIdentity>();
			pending.Push(provider.Identity);
			while (pending.TryPop(out var identity))
			{
				if (!_bulkCopyProtectedMethods.Add(identity)) continue;
				if (_foldedMethodAliases.TryGetValue(identity, out var canonical))
				{
					pending.Push(canonical.Identity);
				}
				if (!functions.TryGetValue(identity, out var function)) continue;
				// Outlined calls are compiler-generated and cannot rely on a CIL
				// call site having run a cctor. Reject initialization requirements
				// throughout the closure instead of silently bypassing them or
				// allowing a cctor to recursively use this provider.
				if (function.SourceMethod is { } dependency && _module.HasTypeInitializer(dependency) ||
					function.Blocks.SelectMany(static block => block.Instructions).Any(static instruction =>
						instruction.Operation == M68kMachineOperation.TypeInitialize))
				{
					throw new M68kCompilationException(M68kDiagnosticIds.StaticAnalysis,
						"A bulk-copy provider and its dependencies must not require type initialization.",
						function.DisplayName);
				}
				if (function.Values.Values.Any(static value => value.IsGcReference) ||
					function.Blocks.SelectMany(static block => block.Instructions).Any(static instruction =>
						instruction.Operation is M68kMachineOperation.ObjectAllocate or M68kMachineOperation.ArrayAllocate or
							M68kMachineOperation.Box or M68kMachineOperation.DelegateCreate or M68kMachineOperation.Throw))
				{
					throw new M68kCompilationException(M68kDiagnosticIds.StaticAnalysis,
						"A bulk-copy provider and its dependencies must not allocate, throw, or use managed references.",
						function.DisplayName);
				}
				foreach (var call in function.Blocks.SelectMany(static block => block.Instructions)
					.Where(static instruction => instruction.Operation == M68kMachineOperation.Call))
				{
					var targets = call.LogicalCall?.ResolvedTargets ?? [];
					var allTargetsHaveBodies = targets.Length != 0 && targets.All(target =>
						functions.ContainsKey(target) ||
						_foldedMethodAliases.TryGetValue(target, out var alias) &&
						functions.ContainsKey(alias.Identity));
					// Ordinary managed calls conservatively carry these flags and
					// are validated by walking their bodies. An intrinsic/import has
					// no body to inspect: its established effects must prove that it
					// cannot collect or throw from a nonsafepoint copy operation.
					if (!allTargetsHaveBodies && (call.IsSafepoint || call.MayThrow) &&
						!IsDirectProviderMemoryIntrinsic(call))
					{
						throw new M68kCompilationException(M68kDiagnosticIds.StaticAnalysis,
							"A bulk-copy provider dependency with no analyzable body must not collect or throw.",
							function.DisplayName, call.IlOffset);
					}
					foreach (var target in targets) pending.Push(target);
				}
			}
		}
		else if (options.ExternalCall is { } external)
		{
			registers = external.ParameterRegisters!.ToImmutableArray();
			clobbers = clobbers.Add(external.BaseRegister);
			foreach (var register in external.ClobberedRegisters ?? []) clobbers = clobbers.Add(register);
		}
		else
		{
			throw new InvalidOperationException("The validated bulk-copy request has no provider.");
		}
		foreach (var register in registers) clobbers = clobbers.Add(register);
		return new M68kBulkCopyTarget(_bulkCopyMethod, options.ExternalCall, registers, clobbers);
	}

	private bool IsDirectProviderMemoryIntrinsic(M68kMachineInstruction call)
	{
		if (call.Origin is not { } origin || origin.SourceInstruction.Operand is not int token)
		{
			return false;
		}
		// These six compiler-owned primitives emit native loads/stores and
		// address arithmetic only. Raw IR still gives them conservative call
		// flags; do not extend this exemption to arbitrary imports or runtime
		// intrinsics. Valid ordinary ranges are part of the copy contract.
		return _module.ResolveMethodToken(token, origin.SourceMethod,
			origin.SourceInstruction.Offset).ImportName is
			"intrinsic:aptr-read-uint8" or "intrinsic:aptr-read-uint16" or "intrinsic:aptr-read-uint32" or
			"intrinsic:aptr-write-uint8" or "intrinsic:aptr-write-uint16" or "intrinsic:aptr-write-uint32";
	}

	private void LowerBulkCopies(
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
		M68kBulkCopyTarget target)
	{
		foreach (var (identity, function) in functions)
		{
			if (_bulkCopyProtectedMethods.Contains(identity)) continue;
			var copies = M68kBulkCopyLowering.Run(function, _module, target, _request.BulkCopy!.MinimumBytes);
			if (copies != 0 && target.ExternalCall is { } external)
			{
				GetOrAddPlatformBase(external, function.SourceMethod!);
			}
		}
	}

	private void EmitAllocatedBulkCopy(M68kMachineInstruction instruction)
	{
		var copy = instruction.BulkCopy ?? throw new InvalidOperationException("Copy has no provider.");
		var offset = _assembler.Offset;
		if (copy.Target.ManagedMethod is { } provider)
		{
			_assembler.EmitCall(MethodLabel(provider));
		}
		else
		{
			var external = copy.Target.ExternalCall!;
			EmitBaseRelativeJsr(external.BaseRegister, external.Displacement);
		}
		_loadedPlatformBase = null;
		ushort usedData = 0, usedAddress = 0, definedData = 0, definedAddress = 0;
		foreach (var register in copy.Target.ParameterRegisters.Concat(
			copy.Target.ExternalCall is { } convention ? new[] { convention.BaseRegister } : []))
		{
			if (register < M68kRegister.A0) usedData |= (ushort)(1 << (int)register);
			else usedAddress |= (ushort)(1 << ((int)register - (int)M68kRegister.A0));
		}
		foreach (var register in instruction.Clobbers.Enumerate())
		{
			if (register < M68kRegister.A0) definedData |= (ushort)(1 << (int)register);
			else definedAddress |= (ushort)(1 << ((int)register - (int)M68kRegister.A0));
		}
		_assembler.SetInstructionEffects(offset, new M68kInstructionEffects(
			usedData, definedData, usedAddress, definedAddress,
			M68kConditionCodeSet.None, M68kConditionCodeSet.All,
			M68kMemorySet.All, M68kMemorySet.All, 0, true, false));
	}
}
