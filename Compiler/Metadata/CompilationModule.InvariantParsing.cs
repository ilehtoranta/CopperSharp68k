/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Metadata;

internal sealed partial class CompilationModule
{
	// Use body identity rather than metadata identity: specialized bodies can
	// share tokens/constructions but have different instructions. Root ownership
	// shares the result between reachability and resolution in child modules.
	private readonly Dictionary<CilMethod, InvariantParserProviderAnalysis> _invariantParserProviders =
		new(ReferenceEqualityComparer.Instance);

	internal InvariantParserProviderAnalysis GetInvariantParserProviders(CilMethod caller)
	{
		if (_root._invariantParserProviders.TryGetValue(caller, out var result)) return result;
		try
		{
			result = InvariantParserProviderAnalysis.Analyze(caller,
				i => DescribeFrameworkMethodToken((int)i.Operand!, caller, i.Offset));
		}
		catch (M68kCompilationException)
		{
			// A signature that cannot be described cannot authorize a placeholder.
			// Normal method resolution retains responsibility for its diagnostic.
			result = new InvariantParserProviderAnalysis();
		}
		_root._invariantParserProviders.Add(caller, result);
		return result;
	}

	internal bool TryCreatePinnedParserInvariantProviderBinding(
		FrameworkMemberId referenced, CilMethod? caller, int ilOffset, out FrameworkBinding binding)
	{
		binding = null!;
		if (caller is null || ilOffset < 0 ||
			FrameworkImplementationPack is not { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } ||
			!InvariantParserProviderAnalysis.IsInvariantGetter(referenced) ||
			!GetInvariantParserProviders(caller).AdmitsGetter(ilOffset)) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowDateOnlyFormatting", "GetInvariantCulturePlaceholder");
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}", FrameworkEffectSummary.None,
			Reason: "All uses of this invariant provider are proven arguments of admitted bounded DateOnly/TimeOnly parsers.",
			ShadowMethod: shadow);
		return true;
	}
}
