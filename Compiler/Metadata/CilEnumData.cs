/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Metadata;

namespace CopperSharp.Compiler.Metadata;

internal sealed record CilEnumData(CilType Type, string ModuleName, int Token, bool Flags, ulong[] Values, string[] Names)
{
	public string Label => $"runtime:enum-data:{ModuleName}:{Token:X8}";
	public static CilEnumData Read(CompilationModule module, CilType type, string preferredModule)
	{
		var target = module.ResolveRuntimeTypeIdentity(type, preferredModule);
		if (!target.Type.IsEnum || target.Type.Kind is not (CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger) ||
			target.Type.Size is not (1 or 2 or 4 or 8) || target.Handle.Kind != HandleKind.TypeDefinition)
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, $"Enum formatting requires an integral enum definition, found '{type.DisplayName}'.");
		return module.ReadEnumData(target);
	}
}
