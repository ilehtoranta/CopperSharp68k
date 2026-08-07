/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Text.Json;
using CopperSharp.Compiler.Framework;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class FrameworkCompatibilityTests
{
	[Fact]
	public void AnalysisReportsPinnedNet10Contract()
	{
		var result = Analyze("StringLiteralEntry");

		Assert.Equal("net10.0", result.Contract.TargetFramework);
		Assert.Equal("Microsoft.NETCore.App.Ref", result.Contract.ReferencePack);
		Assert.Equal("10.0.9", result.Contract.ReferencePackVersion);
		Assert.Equal(1, result.Contract.ManifestSchemaVersion);
	}

	[Fact]
	public void LedgerRejectsDuplicateMemberIdentities()
	{
		const string duplicateLedger =
			"""
			{
			  "schemaVersion": 1,
			  "targetFramework": "net10.0",
			  "referencePack": "Microsoft.NETCore.App.Ref",
			  "referencePackVersion": "10.0.9",
			  "assemblies": ["System.Runtime"],
			  "bindings": [
			    {
			      "assembly": "*", "type": "System.String", "member": "get_Length",
			      "isStatic": false, "genericArity": 0, "parameterCount": 0,
			      "returnType": "int", "parameterTypes": [],
			      "status": "Intrinsic", "target": "intrinsic:string-length"
			    },
			    {
			      "assembly": "*", "type": "System.String", "member": "get_Length",
			      "isStatic": false, "genericArity": 0, "parameterCount": 0,
			      "returnType": "int", "parameterTypes": [],
			      "status": "Intrinsic", "target": "intrinsic:string-length"
			    }
			  ]
			}
			""";

		var exception = Assert.Throws<InvalidOperationException>(
			() => Net10FrameworkContract.ValidateManifestJson(duplicateLedger));
		Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void LedgerAllowsContextSpecializedDispositionsForOnePublicMember()
	{
		const string specializedLedger =
			"""
			{
			  "schemaVersion": 1,
			  "targetFramework": "net10.0",
			  "referencePack": "Microsoft.NETCore.App.Ref",
			  "referencePackVersion": "10.0.9",
			  "assemblies": ["System.Runtime"],
			  "bindings": [
			    {
			      "assembly": "System.Runtime", "type": "System.Object", "member": "Equals",
			      "isStatic": false, "genericArity": 0, "parameterCount": 1,
			      "returnType": "bool", "parameterTypes": ["object"],
			      "status": "Implemented", "target": "shadow:runtime:Object::Equals",
			      "effects": ["ReadsManagedMemory"], "features": ["managed-objects"]
			    },
			    {
			      "assembly": "System.Runtime", "type": "System.Object", "member": "Equals",
			      "isStatic": false, "genericArity": 0, "parameterCount": 1,
			      "returnType": "bool", "parameterTypes": ["object"],
			      "status": "Intrinsic", "target": "intrinsic:delegate-equality",
			      "effects": ["ReadsManagedMemory"], "features": ["delegates"]
			    }
			  ]
			}
			""";

		Net10FrameworkContract.ValidateManifestJson(specializedLedger);
	}

	[Fact]
	public void AnalysisInventoriesOnlyReachableFrameworkMembers()
	{
		var result = Analyze("StringLiteralEntry");

		var member = Assert.Single(result.Members);
		Assert.Equal("System.String", member.Member.TypeName);
		Assert.Equal("get_Length", member.Member.Name);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, member.Status);
		Assert.Equal("intrinsic:string-length", member.Binding);
		Assert.Equal(
			["MayThrow", "ReadsManagedMemory"],
			member.Effects);
		Assert.Equal(["managed-strings"], member.RequiredFeatures);
		Assert.Single(member.CallSites);
		Assert.DoesNotContain(
			result.Members,
			candidate => candidate.Member.Name == "Concat");
		Assert.Empty(result.ManagedAllocationSites);
		Assert.True(result.IsCompatible);
	}

	[Fact]
	public void FrameworkRegistryUsesStructuralMetadataIdentity()
	{
		static FrameworkMemberId StringLength(string assembly, FrameworkTypeId returnType) =>
			new(
				FrameworkTypeId.Named(assembly, "System.String"),
				"get_Length",
				new FrameworkMethodSignatureId(
					0x20,
					0,
					0,
					returnType,
					[]));
		var context = new FrameworkBindingContext(
			"System.String",
			default,
			null,
			false);

		Assert.NotNull(FrameworkBindingRegistry.TryBind(
			StringLength(
				"System.Runtime",
				FrameworkTypeId.Primitive("System.Int32")),
			context));
		Assert.Null(FrameworkBindingRegistry.TryBind(
			StringLength(
				"User.Assembly",
				FrameworkTypeId.Primitive("System.Int32")),
			context));
		Assert.Null(FrameworkBindingRegistry.TryBind(
			StringLength(
				"System.Runtime",
				FrameworkTypeId.Primitive("System.UInt32")),
			context));
	}

	[Fact]
	public void AnalysisInventoriesReachableManagedAllocationInstructions()
	{
		var array = Assert.Single(Analyze("InitializedArrayEntry").ManagedAllocationSites);
		Assert.Equal("array", array.Kind);
		Assert.Equal("uint[]", array.AllocatedType);

		var objectSite = Assert.Single(Analyze("NullComparisonEntry").ManagedAllocationSites);
		Assert.Equal("object", objectSite.Kind);
		Assert.Equal("ManagedBox", objectSite.AllocatedType);

		Assert.Empty(Analyze("NullableUIntDefaultEntry").ManagedAllocationSites);
	}

	[Fact]
	public void SpanArrayProfileAddsNoManagedAllocationSite()
	{
		var analysis = Analyze("SpanArrayLengthAndIndexerEntry");
		var allocation = Assert.Single(analysis.ManagedAllocationSites);
		Assert.Equal("array", allocation.Kind);
		Assert.Equal("int[]", allocation.AllocatedType);
		Assert.Equal(
			3,
			analysis.Members.Count(member =>
				member.Member.TypeName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				member.RequiredFeatures.Contains("spans")));
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void SpanByrefConstructorsAddNoWrapperOrHelperAllocation()
	{
		foreach (var entry in new[]
		{
			"SpanFromFrameRefAcrossCollectionEntry",
			"SpanFromArrayRefAcrossCollectionEntry",
			"SpanFromObjectRefAcrossCollectionEntry",
			"ReadOnlySpanFromArrayRefAcrossCollectionEntry"
		})
		{
			var analysis = Analyze(entry);
			Assert.Contains(
				analysis.Members,
				member => member.Member.Name == ".ctor" &&
					member.Member.TypeName.Contains("Span`1<", StringComparison.Ordinal) &&
					member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
					member.Binding?.Contains("span-from-ref:", StringComparison.Ordinal) == true &&
					member.RequiredFeatures.SequenceEqual(["spans"]));
			Assert.DoesNotContain(
				analysis.ManagedAllocationSites,
				site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
			Assert.True(analysis.IsCompatible);
		}
	}

	[Fact]
	public void StackallocSpanUsesOfficialPointerConstructorWithoutWrapperAllocation()
	{
		var analysis = Analyze("MultipleConstantStackallocSpanEntry");
		Assert.Equal(
			2,
			analysis.Members.Count(member => member.Member.Name == ".ctor" &&
				member.Member.TypeName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				member.Binding?.StartsWith(
					"intrinsic:span-from-pointer:",
					StringComparison.Ordinal) == true &&
				member.RequiredFeatures.SequenceEqual(["spans"])));
		Assert.Empty(analysis.ManagedAllocationSites);
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Theory]
	[InlineData("SpanByteCopyToEntry", "intrinsic:span-copy-to:byte")]
	[InlineData("SpanFloatCopyToEntry", "intrinsic:span-copy-to:float")]
	[InlineData("ReadOnlySpanIntCopyToEntry", "intrinsic:readonly-span-copy-to:int")]
	public void SpanCopyToUsesOfficialContractWithoutHelperAllocation(
		string entry,
		string binding)
	{
		var analysis = Analyze(entry);
		Assert.Contains(
			analysis.Members,
			member => member.Member.Name == "CopyTo" &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				member.Binding == binding &&
				member.RequiredFeatures.SequenceEqual(["spans"]));
		Assert.Empty(analysis.ManagedAllocationSites);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void SpanFloatIndexersUseOfficialContractsWithoutAllocation()
	{
		var analysis = Analyze("SpanFloatElementAccessEntry");
		Assert.Contains(
			analysis.Members,
			member => member.Member.Name == "get_Item" &&
				member.Binding == "intrinsic:span-get-item:float" &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic);
		Assert.Contains(
			analysis.Members,
			member => member.Member.Name == "get_Item" &&
				member.Binding == "intrinsic:readonly-span-get-item:float" &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic);
		Assert.Empty(analysis.ManagedAllocationSites);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void SpanLongIndexersUseOfficialContractsWithoutAllocation()
	{
		var analysis = Analyze("SpanLongElementAccessEntry");
		Assert.Contains(
			analysis.Members,
			member => member.Member.Name == "get_Item" &&
				member.Binding == "intrinsic:span-get-item:long" &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic);
		Assert.Contains(
			analysis.Members,
			member => member.Member.Name == "get_Item" &&
				member.Binding == "intrinsic:readonly-span-get-item:long" &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic);
		Assert.Empty(analysis.ManagedAllocationSites);
		Assert.True(analysis.IsCompatible);
	}

	[Theory]
	[InlineData("SpanReturnOwnerSurvivesCollectionEntry")]
	[InlineData("ReadOnlySpanReturnOwnerSurvivesCollectionEntry")]
	[InlineData("SpanParameterReturnOwnerSurvivesCollectionEntry")]
	public void SpanLikeReturnsUseOfficialContractsWithoutWrapperAllocation(
		string entry)
	{
		var analysis = Analyze(entry);
		Assert.Equal(
			2,
			analysis.ManagedAllocationSites.Count(site => site.Kind == "array"));
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void SpanIsEmptyProfileAddsNoManagedAllocationOrHelperSite()
	{
		var analysis = Analyze("SpanIsEmptyEntry");
		var allocation = Assert.Single(analysis.ManagedAllocationSites);
		Assert.Equal("array", allocation.Kind);
		Assert.Equal("int[]", allocation.AllocatedType);
		Assert.Equal(
			2,
			analysis.Members.Count(member =>
				member.Member.TypeName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				member.RequiredFeatures.Contains("spans")));
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void DefaultSpanProfileAddsNoManagedAllocationSite()
	{
		var analysis = Analyze("SpanDefaultAcrossCollectionEntry");
		Assert.Empty(analysis.ManagedAllocationSites);
		Assert.Equal(
			2,
			analysis.Members.Count(member =>
				member.Member.TypeName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				member.RequiredFeatures.Contains("spans")));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void SpanSliceProfileAddsNoSpanAllocationSite()
	{
		var analysis = Analyze("SpanSliceOwnerSurvivesCollectionEntry");
		Assert.Equal(
			2,
			analysis.ManagedAllocationSites.Count(site =>
				site is { Kind: "array", AllocatedType: "int[]" }));
		Assert.Equal(
			5,
			analysis.Members.Count(member =>
				member.Member.TypeName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				member.RequiredFeatures.Contains("spans")));
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void WideSpanExactLayoutAddsNoWrapperOrScalingHelper()
	{
		var analysis = Analyze("WideSpanExactLayoutEntry");
		Assert.Equal(
			2,
			analysis.ManagedAllocationSites.Count(site =>
				site is { Kind: "array" }));
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.All(
			analysis.Members.Where(member =>
				member.Member.TypeName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal)),
			member => Assert.Equal(
				M68kFrameworkCompatibilityStatus.Intrinsic,
				member.Status));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void ReadOnlySpanProfileAddsNoSpanAllocationSite()
	{
		var arrayAnalysis = Analyze("ReadOnlySpanArraySliceOwnerSurvivesCollectionEntry");
		Assert.Equal(
			2,
			arrayAnalysis.ManagedAllocationSites.Count(site =>
				site is { Kind: "array", AllocatedType: "int[]" }));
		Assert.Equal(
			6,
			arrayAnalysis.Members.Count(member =>
				member.Member.TypeName.StartsWith(
					"System.ReadOnlySpan`1<",
					StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				member.RequiredFeatures.Contains("spans")));
		Assert.DoesNotContain(
			arrayAnalysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(arrayAnalysis.IsCompatible);

		var conversionAnalysis = Analyze(
			"ReadOnlySpanFromSpanOwnerSurvivesCollectionEntry");
		Assert.Equal(
			2,
			conversionAnalysis.ManagedAllocationSites.Count(site =>
				site is { Kind: "array", AllocatedType: "int[]" }));
		Assert.Contains(
			conversionAnalysis.Members,
			member => member.Member is
			{
				TypeName: var typeName,
				Name: "op_Implicit"
			} &&
				typeName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal) &&
				member.Member.ReturnType.StartsWith(
					"System.ReadOnlySpan`1<",
					StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic);
		Assert.DoesNotContain(
			conversionAnalysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(conversionAnalysis.IsCompatible);
	}

	[Fact]
	public void StringBackedReadOnlySpanAddsNoWrapperOrConversionHelper()
	{
		var analysis = Analyze("ReadOnlySpanFromStringEntry");
		Assert.Contains(
			analysis.Members,
			member => member.Member is
			{
				TypeName: "System.MemoryExtensions",
				Name: "AsSpan"
			} &&
				member.Member.ReturnType.StartsWith(
					"System.ReadOnlySpan`1<char>",
					StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				member.Binding == "intrinsic:readonly-span-from-string" &&
				member.RequiredFeatures.SequenceEqual(
					["managed-strings", "spans"]));
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void StringIndexerAndOrdinalEqualityAreExactAllocationFreeIntrinsics()
	{
		var indexer = Analyze("StringCharIndexerEntry");
		var chars = Assert.Single(
			indexer.Members,
			member => member.Member is
				{ TypeName: "System.String", Name: "get_Chars" });
		Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, chars.Status);
		Assert.Equal("intrinsic:string-char", chars.Binding);
		Assert.Equal(["MayThrow", "ReadsManagedMemory"], chars.Effects);
		Assert.Equal(["managed-strings"], chars.RequiredFeatures);
		Assert.DoesNotContain(
			indexer.ManagedAllocationSites,
			site => site.RootPath.Last().Contains("get_Chars", StringComparison.Ordinal));

		var equality = Analyze("StringOrdinalEqualityEntry");
		Assert.Contains(
			equality.Members,
			member => member.Member is
				{ TypeName: "System.String", Name: "Equals", IsStatic: true } &&
				member.Binding == "intrinsic:string-equality");
		Assert.Contains(
			equality.Members,
			member => member.Member is
				{ TypeName: "System.String", Name: "op_Equality" } &&
				member.Binding == "intrinsic:string-equality");
		Assert.Contains(
			equality.Members,
			member => member.Member is
				{ TypeName: "System.String", Name: "op_Inequality" } &&
				member.Binding == "intrinsic:string-inequality");
		Assert.All(
			equality.Members.Where(member => member.Member.TypeName == "System.String"),
			member => Assert.Equal(["managed-strings"], member.RequiredFeatures));
		Assert.DoesNotContain(
			equality.Members.SelectMany(member => member.RequiredFeatures),
			feature => feature == "spans");
		Assert.Equal(
			3,
			equality.ManagedAllocationSites.Count(site => site.Kind == "string"));
		Assert.True(equality.IsCompatible);
	}

	[Fact]
	public void TwoStringConcatIsAnExplicitAllocatingIntrinsic()
	{
		var analysis = Analyze("StringConcatEntry");
		var concat = Assert.Single(
			analysis.Members,
			member => member.Member is
				{ TypeName: "System.String", Name: "Concat" });
		Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, concat.Status);
		Assert.Equal("intrinsic:string-concat-two", concat.Binding);
		Assert.Equal(
			["MayAllocate", "MayThrow", "MayCollect", "ReadsManagedMemory", "WritesManagedMemory"],
			concat.Effects);
		Assert.Equal(["managed-strings"], concat.RequiredFeatures);
		Assert.Equal(
			4,
			analysis.ManagedAllocationSites.Count(site =>
				site is { Kind: "string", AllocatedType: "string" }));
		Assert.DoesNotContain(
			analysis.Members.SelectMany(member => member.RequiredFeatures),
			feature => feature == "spans");
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void SubstringOverloadsShareOneExplicitAllocatingIntrinsic()
	{
		var analysis = Analyze("StringSubstringEntry");
		var substringMembers = analysis.Members
			.Where(member => member.Member.Name == "Substring")
			.ToArray();
		Assert.True(
			substringMembers.Length == 2,
			$"Expected two Substring overload bindings, found {substringMembers.Length}. " +
			$"Reachable members: {string.Join("; ", analysis.Members.Select(member => $"{member.Member.TypeName}::{member.Member.Name} => {member.Binding ?? member.Status.ToString()}"))}");
		Assert.All(substringMembers, substring =>
		{
			Assert.Equal("System.String", substring.Member.TypeName);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, substring.Status);
			Assert.Equal("intrinsic:string-substring", substring.Binding);
			Assert.Equal(
				["MayAllocate", "MayThrow", "MayCollect", "ReadsManagedMemory", "WritesManagedMemory"],
				substring.Effects);
			Assert.Equal(["managed-strings"], substring.RequiredFeatures);
		});
		Assert.Equal(
			7,
			analysis.ManagedAllocationSites.Count(
				site => site is { Kind: "string", AllocatedType: "string" }));
		Assert.DoesNotContain(
			analysis.Members.SelectMany(member => member.RequiredFeatures),
			feature => feature == "spans");
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void StringCopyToAndForeachAreExactAllocationFreeOperations()
	{
		var copy = Analyze("StringCopyToEntry");
		var copyTo = Assert.Single(
			copy.Members,
			member => member.Member is
				{ TypeName: "System.String", Name: "CopyTo" });
		Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, copyTo.Status);
		Assert.Equal("intrinsic:string-copy-to-char-array", copyTo.Binding);
		Assert.Equal(
			["MayThrow", "ReadsManagedMemory", "WritesManagedMemory"],
			copyTo.Effects);
		Assert.Equal(["managed-arrays", "managed-strings"], copyTo.RequiredFeatures);
		Assert.Single(
			copy.ManagedAllocationSites,
			site => site is { Kind: "array", AllocatedType: "char[]" });
		Assert.True(copy.IsCompatible);

		var spanCopy = Analyze("StringCopyToSpanEntry");
		var copyToSpan = Assert.Single(
			spanCopy.Members,
			member => member.Member is
				{ TypeName: "System.String", Name: "CopyTo" });
		Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, copyToSpan.Status);
		Assert.Equal("intrinsic:string-copy-to-span-char", copyToSpan.Binding);
		Assert.Equal(
			["MayThrow", "ReadsManagedMemory", "WritesManagedMemory"],
			copyToSpan.Effects);
		Assert.Equal(["managed-strings", "spans"], copyToSpan.RequiredFeatures);
		Assert.Single(
			spanCopy.ManagedAllocationSites,
			site => site is { Kind: "array", AllocatedType: "char[]" });
		Assert.True(spanCopy.IsCompatible);

		var enumeration = Analyze("StringEnumerationEntry");
		Assert.Contains(
			enumeration.Members,
			member => member.Member is
				{ TypeName: "System.String", Name: "get_Length" });
		Assert.Contains(
			enumeration.Members,
			member => member.Member is
				{ TypeName: "System.String", Name: "get_Chars" });
		Assert.DoesNotContain(
			enumeration.Members,
			member => member.Member.Name == "GetEnumerator" ||
				member.Member.TypeName == "System.CharEnumerator");
		Assert.Empty(enumeration.ManagedAllocationSites);
		Assert.True(enumeration.IsCompatible);
	}

	[Fact]
	public void ParameterlessIntegerFormattingUsesPrivatePayForPlayShadows()
	{
		var analysis = Analyze("IntegerToStringEntry");
		var formatters = analysis.Members
			.Where(member => member.Member.Name == "ToString" &&
				member.Member.TypeName is "System.Int32" or "System.UInt32")
			.ToArray();
		Assert.Equal(2, formatters.Length);
		Assert.All(formatters, formatter =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, formatter.Status);
			Assert.StartsWith(
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.Shadow",
				formatter.Binding,
				StringComparison.Ordinal);
			Assert.Equal(
				["MayAllocate", "MayThrow", "MayCollect", "WritesManagedMemory"],
				formatter.Effects);
			Assert.Equal(
				["integer-formatting", "managed-strings", "numerics"],
				formatter.RequiredFeatures);
		});
		Assert.Equal(
			2,
			analysis.ManagedAllocationSites.Count(
				site => site is { Kind: "string", AllocatedType: "string" }));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void IntegerFormatStringsUsePublicContractsAndPrivateShadows()
	{
		var analysis = Analyze("IntegerFormatStringEntry");
		var formatters = analysis.Members
			.Where(member => member.Member.Name == "ToString" &&
				member.Member.TypeName is "System.Int32" or "System.UInt32")
			.ToArray();
		Assert.Equal(2, formatters.Length);
		Assert.All(formatters, formatter =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, formatter.Status);
			Assert.Contains("Shadow", formatter.Binding, StringComparison.Ordinal);
			Assert.Equal(
				["MayAllocate", "MayThrow", "MayCollect", "ReadsManagedMemory", "WritesManagedMemory"],
				formatter.Effects);
			Assert.Equal(
				["integer-formatting", "managed-exceptions", "managed-strings", "numerics"],
				formatter.RequiredFeatures);
		});
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void InterpolatedIntegersUseThePublicNet10HandlerContract()
	{
		var analysis = Analyze("InterpolatedIntegerEntry");
		Assert.True(
			analysis.IsCompatible,
			string.Join(
				Environment.NewLine,
				analysis.Members.Select(member =>
					$"{member.Member.TypeName}::{member.Member.Name} => " +
					$"{member.Status} {member.Binding} {member.Reason}")));
		var handlerMembers = analysis.Members
			.Where(member => member.Member.TypeName ==
				"System.Runtime.CompilerServices.DefaultInterpolatedStringHandler")
			.ToArray();
		Assert.Equal(5, handlerMembers.Length);
		Assert.All(handlerMembers, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowDefaultInterpolatedStringHandler::",
				member.Binding,
				StringComparison.Ordinal);
			Assert.Contains("string-interpolation", member.RequiredFeatures);
		});
		Assert.Equal(
			2,
			analysis.ManagedAllocationSites.Count(site =>
				site is { Kind: "array", AllocatedType: "char[]" }));
		Assert.Single(
			analysis.ManagedAllocationSites,
			site => site is { Kind: "string", AllocatedType: "string" });

		var unrelated = Analyze("IntegerToStringEntry");
		Assert.DoesNotContain(
			unrelated.Members,
			member => member.Member.TypeName ==
				"System.Runtime.CompilerServices.DefaultInterpolatedStringHandler");
	}

	[Fact]
	public void ToCharArrayOverloadsShareOneReportedAllocatingIntrinsic()
	{
		var analysis = Analyze("StringToCharArrayEntry");
		var conversions = analysis.Members
			.Where(member => member.Member is
				{ TypeName: "System.String", Name: "ToCharArray" })
			.ToArray();
		Assert.Equal(2, conversions.Length);
		Assert.All(conversions, conversion =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, conversion.Status);
			Assert.Equal("intrinsic:string-to-char-array", conversion.Binding);
			Assert.Equal(
				["MayAllocate", "MayThrow", "MayCollect", "ReadsManagedMemory", "WritesManagedMemory"],
				conversion.Effects);
			Assert.Equal(
				["managed-arrays", "managed-strings"],
				conversion.RequiredFeatures);
		});
		Assert.Equal(
			6,
			analysis.ManagedAllocationSites.Count(
				site => site is { Kind: "array", AllocatedType: "char[]" }));
		Assert.True(analysis.IsCompatible);

		var linked = M68kCompiler.Compile(Request("StringToCharArrayEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});
		var unrelated = M68kCompiler.Compile(Request("StringEnumerationEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly
		});
		Assert.Contains(
			"C68K_runtime_003Achar_002Darray_002Dempty",
			linked.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"C68K_runtime_003Achar_002Darray_002Dempty",
			unrelated.Text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void OrdinalStringSearchMembersAreExactAllocationFreeIntrinsics()
	{
		var analysis = Analyze("StringOrdinalSearchEntry");
		var expected = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["StartsWith"] = "intrinsic:string-starts-with-ordinal",
			["EndsWith"] = "intrinsic:string-ends-with-ordinal",
			["Contains"] = "intrinsic:string-contains-ordinal",
			["IndexOf"] = "intrinsic:string-index-of-ordinal"
		};
		var searchMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.String" &&
				expected.ContainsKey(member.Member.Name))
			.ToArray();
		Assert.Equal(5, searchMembers.Length);
		Assert.All(searchMembers, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, member.Status);
			Assert.Equal(expected[member.Member.Name], member.Binding);
			Assert.Equal(["MayThrow", "ReadsManagedMemory"], member.Effects);
			Assert.Equal(["managed-strings"], member.RequiredFeatures);
		});
		Assert.Empty(analysis.ManagedAllocationSites);
		Assert.DoesNotContain(
			analysis.Members.SelectMany(member => member.RequiredFeatures),
			feature => feature == "spans");
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void ReadOnlySpanCharSequenceEqualUsesPortableIntrinsicWithoutHelpers()
	{
		var analysis = Analyze("ReadOnlySpanCharSequenceEqualEntry");
		Assert.Contains(
			analysis.Members,
			member => member.Member is
			{
				TypeName: "System.MemoryExtensions",
				Name: "SequenceEqual"
			} &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				member.Binding ==
					"intrinsic:readonly-span-sequence-equal:char" &&
				member.RequiredFeatures.SequenceEqual(["spans"]));
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void DynamicStringAllocationIsExplicitlyInventoriedWithoutSpanWrappers()
	{
		var analysis = Analyze(
			"DynamicStringReadOnlySpanOwnerSurvivesCollectionEntry");
		Assert.Contains(
			analysis.ManagedAllocationSites,
			site => site is { Kind: "string", AllocatedType: "string" });
		Assert.Contains(
			analysis.ManagedAllocationSites,
			site => site is { Kind: "array", AllocatedType: "ushort[]" });
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Span", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void AnalysisReportsUnsupportedReachableMemberAndItsCallSite()
	{
		var result = Analyze("UnsupportedStringConcatEntry");

		var concat = Assert.Single(
			result.Members,
			candidate => candidate.Member is
			{
				TypeName: "System.String",
				Name: "Concat"
			});
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, concat.Status);
		Assert.Null(concat.Binding);
		Assert.NotNull(concat.Reason);
		var callSite = Assert.Single(concat.CallSites);
		Assert.EndsWith(
			"CompilerFixtures::UnsupportedStringConcatEntry",
			callSite.Caller,
			StringComparison.Ordinal);
		Assert.True(callSite.IlOffset >= 0);
		Assert.False(result.IsCompatible);
	}

	[Fact]
	public void UnsupportedFrameworkDiagnosticIncludesExactIdentityAndShortestRootPath()
	{
		var analysis = Analyze("UnsupportedStringConcatRootEntry");
		var concat = Assert.Single(
			analysis.Members,
			member => member.Member.Name == "Concat");
		var callSite = Assert.Single(concat.CallSites);
		Assert.Equal(
			[
				"CopperSharp.Compiler.Tests.CompilerFixtures::UnsupportedStringConcatRootEntry",
				"CopperSharp.Compiler.Tests.CompilerFixtures::UnsupportedStringConcatMiddle",
				"CopperSharp.Compiler.Tests.CompilerFixtures::UnsupportedStringConcatEntry"
			],
			callSite.RootPath);

		var exception = Assert.Throws<M68kCompilationException>(
			() => M68kCompiler.Compile(Request("UnsupportedStringConcatRootEntry")));
		Assert.Equal(M68kDiagnosticIds.UnsupportedFrameworkMember, exception.DiagnosticId);
		Assert.Contains(
			"[System.Runtime]System.String::Concat",
			exception.Message,
			StringComparison.Ordinal);
		Assert.Contains(
			"UnsupportedStringConcatRootEntry -> " +
				"CopperSharp.Compiler.Tests.CompilerFixtures::UnsupportedStringConcatMiddle -> " +
				"CopperSharp.Compiler.Tests.CompilerFixtures::UnsupportedStringConcatEntry",
			exception.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void CurrentFrameworkSpecialCasesAreRepresentedByLedger()
	{
		AssertIntrinsic(Analyze("NullComparisonEntry"), "System.Object", ".ctor");
		AssertIntrinsic(Analyze("CustomExceptionCatchEntry"), "System.Exception", ".ctor");
		AssertIntrinsic(
			Analyze("InitializedArrayEntry"),
			"System.Runtime.CompilerServices.RuntimeHelpers",
			"InitializeArray");

		var nullable = Analyze("NullableUIntDefaultEntry");
		Assert.NotEmpty(nullable.Members);
		Assert.All(
			nullable.Members.Where(static member =>
				member.Member.TypeName.StartsWith("System.Nullable<", StringComparison.Ordinal)),
			static member => Assert.Equal(
				M68kFrameworkCompatibilityStatus.Intrinsic,
				member.Status));
		Assert.Contains(nullable.Members, static member => member.Member.Name == ".ctor");
		Assert.Contains(
			nullable.Members,
			static member => member.Member.Name == "GetValueOrDefault");
	}

	[Fact]
	public void AnalysisOrderAndCallSitesAreDeterministic()
	{
		var first = Analyze("UnsupportedStringConcatEntry");
		var second = Analyze("UnsupportedStringConcatEntry");

		Assert.Equal(
			JsonSerializer.Serialize(first),
			JsonSerializer.Serialize(second));
	}

	[Fact]
	public void AnalysisDoesNotAlterGeneratedCode()
	{
		var request = Request("StringLiteralEntry");
		var before = M68kCompiler.Compile(request);

		_ = M68kCompiler.AnalyzeFramework(request);
		var after = M68kCompiler.Compile(request);

		Assert.Equal(before.Image, after.Image);
		Assert.Equal(before.Code, after.Code);
		Assert.Equal(before.Map, after.Map);
		Assert.Equal(before.Text, after.Text);
	}

	[Fact]
	public void ShadowBindingIsReachableAndLinkedOnlyWhenUsed()
	{
		var analysis = Analyze("ShadowMathAbsEntry");
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.Math" &&
				candidate.Member.Name == "Abs");
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowMath::Abs",
			member.Binding);
		Assert.Equal(["MayThrow"], member.Effects);
		Assert.Equal(["managed-exceptions", "numerics"], member.RequiredFeatures);

		var linked = M68kCompiler.Compile(Request("ShadowMathAbsEntry"));
		var unrelated = M68kCompiler.Compile(Request("StringLiteralEntry"));
		Assert.Contains(
			linked.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ShadowMath::Abs");
		Assert.DoesNotContain(
			linked.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ShadowMath::Identity");
		Assert.Contains(
			"CopperSharp.Runtime.ShadowMath::Abs",
			linked.Map,
			StringComparison.Ordinal);
		Assert.Equal(
			["managed-exceptions", "numerics"],
			linked.FrameworkFeatures);
		Assert.Contains("FRAMEWORK FEATURES", linked.Map, StringComparison.Ordinal);
		Assert.Contains("numerics", linked.Map, StringComparison.Ordinal);
		Assert.DoesNotContain(
			unrelated.Symbols,
			symbol => symbol.Name.Contains("ShadowMath", StringComparison.Ordinal));
		Assert.DoesNotContain("numerics", unrelated.FrameworkFeatures);
		Assert.DoesNotContain(
			Analyze("StringLiteralEntry").Members,
			candidate => candidate.RequiredFeatures.Contains("numerics"));
	}

	[Fact]
	public void ObjectVirtualBindingPreservesPublicIdentityAndLinksOnlyWhenUsed()
	{
		Assert.Equal(0, new CopperSharp.Runtime.ShadowObject().GetHashCode());
		var shadow = new CopperSharp.Runtime.ShadowObject();
		Assert.True(shadow.Equals(shadow));
		Assert.False(shadow.Equals(new object()));
		Assert.True(CopperSharp.Runtime.ShadowObject.EqualsObjects(null, null));
		Assert.False(CopperSharp.Runtime.ShadowObject.EqualsObjects(null, new object()));
		Assert.False(CopperSharp.Runtime.ShadowObject.EqualsObjects(new object(), null));
		var hostDelegate = new Func<int, int>(value => value + 1);
		Assert.True(CopperSharp.Runtime.ShadowObject.EqualsObjects(hostDelegate, hostDelegate));

		var analysis = Analyze("ObjectGetHashCodeFallbackEntry");
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.Object" &&
				candidate.Member.Name == "GetHashCode");
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowObject::GetHashCode",
			member.Binding);
		Assert.Equal(["ReadsManagedMemory"], member.Effects);
		Assert.Equal(["managed-objects"], member.RequiredFeatures);

		var linked = M68kCompiler.Compile(Request("ObjectGetHashCodeFallbackEntry") with
		{
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});
		var unrelated = M68kCompiler.Compile(Request("StringLiteralEntry"));
		Assert.Contains(
			linked.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ShadowObject::GetHashCode");
		Assert.Contains("managed-objects", linked.FrameworkFeatures);
		Assert.DoesNotContain(
			unrelated.Symbols,
			symbol => symbol.Name.Contains("ShadowObject", StringComparison.Ordinal));
		Assert.DoesNotContain("managed-objects", unrelated.FrameworkFeatures);

		var equality = Analyze("ObjectEqualsFallbackEntry");
		var equals = Assert.Single(
			equality.Members,
			candidate => candidate.Member.TypeName == "System.Object" &&
				candidate.Member.Name == "Equals");
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, equals.Status);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowObject::Equals",
			equals.Binding);
		Assert.Equal(["ReadsManagedMemory"], equals.Effects);
		Assert.Equal(["managed-objects"], equals.RequiredFeatures);

		var equalityLinked = M68kCompiler.Compile(Request("ObjectEqualsFallbackEntry") with
		{
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});
		Assert.Contains(
			equalityLinked.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ShadowObject::Equals");
		Assert.DoesNotContain(
			equalityLinked.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ShadowObject::GetHashCode");

		var staticEquality = Analyze("StaticObjectEqualsEntry");
		var staticEquals = Assert.Single(
			staticEquality.Members,
			candidate => candidate.Member is
				{ TypeName: "System.Object", Name: "Equals", IsStatic: true });
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, staticEquals.Status);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowObject::EqualsObjects",
			staticEquals.Binding);
		Assert.Equal(["ReadsManagedMemory"], staticEquals.Effects);
		Assert.Equal(["managed-objects"], staticEquals.RequiredFeatures);
		Assert.Contains(
			staticEquality.Members,
			candidate => candidate.Member is
				{ TypeName: "System.Object", Name: "Equals", IsStatic: false } &&
				candidate.Binding == "intrinsic:delegate-equality");
		Assert.Contains(
			staticEquality.Members,
			candidate => candidate.Member is
				{ TypeName: "System.Object", Name: "Equals", IsStatic: false } &&
				candidate.Binding ==
					"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowObject::Equals");
		Assert.DoesNotContain(
			staticEquality.ManagedAllocationSites,
			site => site.RootPath.Contains(
				"CopperSharp.Runtime.ShadowObject::EqualsObjects"));

		var staticEqualityLinked = M68kCompiler.Compile(
			Request("StaticObjectEqualsEntry") with
			{
				Imports = new Dictionary<string, uint>
				{
					[M68kRuntimeImports.Allocate] = 0x0000_2800
				}
			});
		Assert.Contains(
			staticEqualityLinked.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ShadowObject::EqualsObjects");
		Assert.Contains("managed-objects", staticEqualityLinked.FrameworkFeatures);
		Assert.Contains("delegates", staticEqualityLinked.FrameworkFeatures);

		var objectType = FrameworkTypeId.Primitive("System.Object");
		var referenceEqualsMember = new FrameworkMemberId(
			FrameworkTypeId.Named("System.Runtime", "System.Object"),
			"ReferenceEquals",
			new FrameworkMethodSignatureId(
				0x00,
				0,
				2,
				FrameworkTypeId.Primitive("System.Boolean"),
				[objectType, objectType]));
		var referenceEquals = Assert.IsType<FrameworkBinding>(
			FrameworkBindingRegistry.TryBind(referenceEqualsMember, default));
		Assert.Equal(FrameworkBindingKind.Intrinsic, referenceEquals.Kind);
		Assert.Equal("intrinsic:object-reference-equals", referenceEquals.Target);
		Assert.Equal(FrameworkEffects.None, referenceEquals.EffectSummary.Effects);
		Assert.Equal(
			["managed-objects"],
			referenceEquals.EffectSummary.RequiredFeatures
				.Select(static feature => feature.Name));

		// Roslyn lowers the C# API spelling directly to CIL ceq. The explicit
		// registry entry remains necessary for precompiled IL that retains the
		// official member call.
		var referenceEquality = Analyze("ObjectReferenceEqualsEntry");
		Assert.DoesNotContain(
			referenceEquality.Members,
			candidate => candidate.Member is
				{ TypeName: "System.Object", Name: "ReferenceEquals" });

		var referenceEqualityLinked = M68kCompiler.Compile(
			Request("ObjectReferenceEqualsEntry") with
			{
				Imports = new Dictionary<string, uint>
				{
					[M68kRuntimeImports.Allocate] = 0x0000_2800
				}
			});
		Assert.DoesNotContain(
			referenceEqualityLinked.Symbols,
			symbol => symbol.Name.Contains("ShadowObject", StringComparison.Ordinal));
	}

	[Fact]
	public void ExplicitCilReferenceEqualsRetainsPublicIdentityAndUsesIntrinsic()
	{
		var assemblyPath = RawCilFixtureBuilder.CreateObjectReferenceEqualsAssembly(
			Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
		try
		{
			var request = new M68kCompilationRequest
			{
				AssemblyPath = assemblyPath,
				EntryPoint = "RawReferenceEquals::Entry",
				Cpu = M68kCpuTarget.M68000,
				OutputFormat = M68kOutputFormat.Assembly
			};
			var analysis = M68kCompiler.AnalyzeFramework(request);
			var member = Assert.Single(
				analysis.Members,
				candidate => candidate.Member is
					{ TypeName: "System.Object", Name: "ReferenceEquals" });
			Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, member.Status);
			Assert.Equal("intrinsic:object-reference-equals", member.Binding);
			Assert.Empty(member.Effects);
			Assert.Equal(["managed-objects"], member.RequiredFeatures);

			var linked = M68kCompiler.Compile(request);
			Assert.DoesNotContain(
				linked.Symbols,
				symbol => symbol.Name.Contains("ShadowObject", StringComparison.Ordinal));
		}
		finally
		{
			File.Delete(assemblyPath);
		}
	}

	[Fact]
	public void AllocatingShadowBodyParticipatesInReachabilityAndEffects()
	{
		var analysis = Analyze("ShadowBitConverterEntry");
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.BitConverter" &&
				candidate.Member.Name == "GetBytes");
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
		Assert.Contains("MayAllocate", member.Effects);
		Assert.Contains("MayCollect", member.Effects);
		Assert.Equal(
			["binary-primitives", "managed-arrays"],
			member.RequiredFeatures);
		var allocation = Assert.Single(analysis.ManagedAllocationSites);
		Assert.Equal("byte[]", allocation.AllocatedType);
		Assert.Equal(
			[
				"CopperSharp.Compiler.Tests.CompilerFixtures::ShadowBitConverterEntry",
				"CopperSharp.Runtime.ShadowBitConverter::GetBytes"
			],
			allocation.RootPath);

		var managedPool = M68kCompiler.Compile(Request("ShadowBitConverterEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});
		Assert.Contains(
			managedPool.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ShadowBitConverter::GetBytes");
		Assert.Contains(
			managedPool.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::Allocate");
		Assert.Contains(
			"\tbsr.w\t__c68k_gc_collect_with_roots",
			managedPool.Text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void DelegateBindingsAndTargetsAreLinkedOnlyWhenReachable()
	{
		var analysis = Analyze("StaticDelegateEntry");
		Assert.Contains(
			analysis.Members,
			member => member.Member.Name == ".ctor" &&
				member.Binding == "intrinsic:delegate-ctor" &&
				member.RequiredFeatures.Contains("delegates"));
		Assert.Contains(
			analysis.Members,
			member => member.Member.Name == "Invoke" &&
				member.Binding == "intrinsic:delegate-invoke" &&
				member.RequiredFeatures.Contains("delegates"));
		Assert.Contains(
			analysis.ManagedAllocationSites,
			site => site.Kind == "object" &&
				site.AllocatedType.Contains("System.Func", StringComparison.Ordinal));

		var linked = M68kCompiler.Compile(Request("StaticDelegateEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});
		var unrelated = M68kCompiler.Compile(Request("StringLiteralEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly
		});
		Assert.Contains("delegates", linked.FrameworkFeatures);
		Assert.Contains(
			linked.Symbols,
			symbol => symbol.Name.Contains("StaticDelegateTarget", StringComparison.Ordinal));
		Assert.Contains("C68K_delegate_003A", linked.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("delegates", unrelated.FrameworkFeatures);
		Assert.DoesNotContain("C68K_delegate_003A", unrelated.Text, StringComparison.Ordinal);

		var multicast = Analyze("MulticastDelegateRemoveEntry");
		Assert.Contains(
			multicast.Members,
			member => member.Member is { TypeName: "System.Delegate", Name: "Combine" } &&
				member.Binding == "intrinsic:delegate-combine");
		Assert.Contains(
			multicast.Members,
			member => member.Member is { TypeName: "System.Delegate", Name: "Remove" } &&
				member.Binding == "intrinsic:delegate-remove");

		var equality = Analyze("DelegateEqualsEntry");
		Assert.Contains(
			equality.Members,
			member => member.Member is { TypeName: "System.Object", Name: "Equals" } &&
				member.Binding == "intrinsic:delegate-equality" &&
				member.RequiredFeatures.Contains("delegates"));
	}

	[Fact]
	public void DelegateEqualsSpecializationDoesNotApplyToOrdinaryObjectReceiver()
	{
		var analysis = Analyze("OrdinaryObjectEqualsEntry");
		var equals = Assert.Single(
			analysis.Members,
			member => member.Member is { TypeName: "System.Object", Name: "Equals" });
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, equals.Status);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowObject::Equals",
			equals.Binding);
		Assert.DoesNotContain(
			"delegates",
			analysis.Members.SelectMany(member => member.RequiredFeatures));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void SealedDisposableUsesExplicitClosedWorldFrameworkBinding()
	{
		var analysis = Analyze("SealedDisposableUsingEntry");
		var dispose = Assert.Single(
			analysis.Members,
			member => member.Member is { TypeName: "System.IDisposable", Name: "Dispose" });
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, dispose.Status);
		Assert.Equal("managed:closed-world-sealed-interface-dispatch", dispose.Binding);
		Assert.Contains("managed-objects", dispose.RequiredFeatures);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void AmigaSdkRetainedOwnerUsesSameDisposableBinding()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(Request("CStringStorageEntry"));
		var dispose = Assert.Single(
			analysis.Members,
			member => member.Member is { TypeName: "System.IDisposable", Name: "Dispose" });
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, dispose.Status);
		Assert.Equal("managed:closed-world-sealed-interface-dispatch", dispose.Binding);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void ListCoreMembersUseGenericPayForPlayShadows()
	{
		var analysis = Analyze("ListInt32Entry");
		var members = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.List`1",
				StringComparison.Ordinal))
			.ToArray();
		Assert.Equal(5, members.Length);
		Assert.All(members, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowList`1::",
				member.Binding,
				StringComparison.Ordinal);
			Assert.Contains("managed-collections", member.RequiredFeatures);
			Assert.Contains("managed-arrays", member.RequiredFeatures);
		});
		Assert.Contains(
			analysis.ManagedAllocationSites,
			site => site is { Kind: "object" } &&
				site.AllocatedType.StartsWith(
					"System.Collections.Generic.List`1<int>",
					StringComparison.Ordinal));
		Assert.Contains(
			analysis.ManagedAllocationSites,
			site => site is { Kind: "array", AllocatedType: "int[]" });
		Assert.True(analysis.IsCompatible);

		var unrelated = Analyze("IntegerToStringEntry");
		Assert.DoesNotContain(
			unrelated.Members,
			member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.List`1",
				StringComparison.Ordinal));
		var unrelatedAssembly = M68kCompiler.Compile(Request("IntegerToStringEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});
		Assert.DoesNotContain("ShadowList", unrelatedAssembly.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void UnimplementedListMemberRemainsExplicitlyDeferred()
	{
		var analysis = Analyze("UnsupportedListRemoveEntry");
		var remove = Assert.Single(
			analysis.Members,
			member => member.Member is
			{
				Name: "Remove"
			} && member.Member.TypeName.StartsWith(
				"System.Collections.Generic.List`1",
				StringComparison.Ordinal));
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, remove.Status);
		Assert.Contains(
			"could not resolve an exact implementation",
			remove.Reason ?? string.Empty,
			StringComparison.OrdinalIgnoreCase);
		Assert.Contains(
			analysis.Members,
			member => member.Member.Name == "Add" &&
				member.Status == M68kFrameworkCompatibilityStatus.Implemented);
		Assert.DoesNotContain(
			analysis.Members,
			member => member.Member.Name != "Remove" &&
				member.Reason?.Contains(
					"could not resolve an exact implementation",
					StringComparison.OrdinalIgnoreCase) == true);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void ListOfReferenceFreeStructRemainsExplicitlyDeferred()
	{
		var analysis = Analyze("UnsupportedListStructEntry");
		var constructor = Assert.Single(
			analysis.Members,
			member => member.Member is
			{
				Name: ".ctor"
			} && member.Member.TypeName.StartsWith(
				"System.Collections.Generic.List`1",
				StringComparison.Ordinal));

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, constructor.Status);
		Assert.Contains(
			"could not resolve an exact implementation",
			constructor.Reason ?? string.Empty,
			StringComparison.OrdinalIgnoreCase);
		Assert.False(analysis.IsCompatible);
	}

	private static void AssertIntrinsic(
		M68kFrameworkAnalysisResult result,
		string type,
		string name)
	{
		var member = Assert.Single(
			result.Members,
			candidate => candidate.Member.TypeName == type && candidate.Member.Name == name);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, member.Status);
		Assert.True(result.IsCompatible);
	}

	private static M68kFrameworkAnalysisResult Analyze(string method) =>
		M68kCompiler.AnalyzeFramework(Request(method));

	private static M68kCompilationRequest Request(string method) =>
		new()
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{method}",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk
		};
}
