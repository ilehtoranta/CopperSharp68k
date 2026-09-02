/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal static class M68kEmptyCallAnalysis
{
	internal static bool IsEmptyConstrainedValueImplementation(CilMethod? implementation) =>
		implementation is { IsImport: false, ExternalCall: null } &&
		implementation.Signature.ReturnType.Kind == CilTypeKind.Void &&
		(implementation.ImplAttributes &
			(MethodImplAttributes.NoInlining | MethodImplAttributes.Synchronized)) == 0 &&
		implementation.ExceptionRegions.Count == 0 &&
		implementation.Instructions.Where(static instruction => instruction.OpCode != OpCodes.Nop)
			.ToArray() is [{ OpCode: var operation }] && operation == OpCodes.Ret;
}
