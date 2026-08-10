/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
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

	[Theory]
	[InlineData("schemaVersion", "2")]
	[InlineData("targetFramework", "net9.0")]
	[InlineData("referencePack", "Other.Reference.Pack")]
	[InlineData("referencePackVersion", "10.0.8")]
	public void LedgerRejectsContractCoordinateDrift(string property, string replacement)
	{
		var schemaVersion = property == "schemaVersion" ? replacement : "1";
		var targetFramework = property == "targetFramework" ? replacement : "net10.0";
		var referencePack = property == "referencePack"
			? replacement
			: "Microsoft.NETCore.App.Ref";
		var referencePackVersion = property == "referencePackVersion"
			? replacement
			: "10.0.9";
		var ledger = $$"""
			{
			  "schemaVersion": {{schemaVersion}},
			  "targetFramework": "{{targetFramework}}",
			  "referencePack": "{{referencePack}}",
			  "referencePackVersion": "{{referencePackVersion}}",
			  "assemblies": ["System.Runtime"],
			  "bindings": []
			}
			""";

		var exception = Assert.Throws<InvalidOperationException>(
			() => Net10FrameworkContract.ValidateManifestJson(ledger));
		Assert.Contains("invalid or unsupported", exception.Message, StringComparison.Ordinal);
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
	public void GenericArrayAlgorithmsUseExactOfficialShadowBindings()
	{
		var analysis = Analyze("ArrayAlgorithmsEntry");
		var members = analysis.Members
			.Where(member => member.Member.TypeName == "System.Array")
			.ToArray();

		Assert.Equal(11, members.Length);
		Assert.Equal(
			new[] { "Empty", "Fill", "IndexOf", "LastIndexOf", "Reverse" },
			members
				.Select(member => member.Member.Name)
				.Distinct(StringComparer.Ordinal)
				.Order()
				.ToArray());
		Assert.All(members, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.Equal(
				$"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowArray::{member.Member.Name}",
				member.Binding);
			Assert.Contains("managed-arrays", member.RequiredFeatures);
			Assert.Contains("managed-gc", member.RequiredFeatures);
		});
		Assert.True(analysis.IsCompatible);
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
		Assert.True(
			analysis.IsCompatible,
			string.Join(
				Environment.NewLine,
				analysis.Members.Select(member =>
					$"{member.Member.TypeName}::{member.Member.Name} => " +
					$"{member.Status} {member.Binding} {member.Reason}")));
	}

	[Fact]
	public void MemoryViewsUseExactOfficialIntrinsicsWithoutWrapperAllocation()
	{
		var analysis = Analyze("MemoryArraySliceAndSpanEntry");
		var memoryMembers = analysis.Members
			.Where(member =>
				member.Member.TypeName.StartsWith(
					"System.Memory`1<",
					StringComparison.Ordinal) ||
				member.Member.TypeName.StartsWith(
					"System.ReadOnlyMemory`1<",
					StringComparison.Ordinal))
			.ToArray();
		Assert.NotEmpty(memoryMembers);
		Assert.All(memoryMembers, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, member.Status);
			Assert.StartsWith("intrinsic:", member.Binding, StringComparison.Ordinal);
			Assert.Contains("managed-memory", member.RequiredFeatures);
			Assert.Contains("managed-arrays", member.RequiredFeatures);
			Assert.Contains("managed-gc", member.RequiredFeatures);
		});
		Assert.Single(
			analysis.ManagedAllocationSites,
			site => site is { Kind: "array", AllocatedType: "int[]" });
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Memory", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);

		var unrelated = Analyze("IntegerToStringEntry");
		Assert.DoesNotContain(
			"managed-memory",
			unrelated.Members.SelectMany(member => member.RequiredFeatures));

		var memoryOutput = M68kCompiler.Compile(Request("MemoryArraySliceAndSpanEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});
		Assert.Contains("managed-memory", memoryOutput.FrameworkFeatures);
		var unrelatedOutput = M68kCompiler.Compile(Request("StringLiteralEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly
		});
		Assert.DoesNotContain("managed-memory", unrelatedOutput.FrameworkFeatures);
	}

	[Fact]
	public void UnselectedMemoryConveniencesRemainExplicitlyUnsupported()
	{
		var analysis = Analyze("UnsupportedMemoryToArrayEntry");
		var toArray = Assert.Single(
			analysis.Members,
			member => member.Member.Name == "ToArray" &&
				member.Member.TypeName.StartsWith(
					"System.Memory`1<",
					StringComparison.Ordinal));
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, toArray.Status);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void SelectedLinqFactoriesAndToArrayUseExactOfficialShadowBindings()
	{
		foreach (var entry in new[]
		{
			"LinqRangeToArrayEntry",
			"LinqRepeatByteToArrayEntry"
		})
		{
			var analysis = Analyze(entry);
			var members = analysis.Members
				.Where(member => member.Member.TypeName == "System.Linq.Enumerable")
				.ToArray();
			Assert.Equal(2, members.Length);
			Assert.All(members, member =>
			{
				Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
				Assert.StartsWith(
					"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowEnumerable::",
					member.Binding,
					StringComparison.Ordinal);
				Assert.Contains("linq", member.RequiredFeatures);
				Assert.Contains("managed-arrays", member.RequiredFeatures);
				Assert.Contains("managed-gc", member.RequiredFeatures);
			});
			Assert.True(analysis.IsCompatible);
		}
	}

	[Fact]
	public void LinqShadowsRemainPayForPlay()
	{
		var output = M68kCompiler.Compile(Request("StringLiteralEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.DoesNotContain("linq", output.FrameworkFeatures);
		Assert.DoesNotContain("ShadowEnumerable", output.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void EnumerableToArrayRejectsUnprovenInterfaceSourcesDuringAnalysis()
	{
		var analysis = Analyze("UnsupportedEnumerableArrayToArrayEntry");
		var toArray = Assert.Single(
			analysis.Members,
			member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name == "ToArray");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, toArray.Status);
		Assert.Contains("closed-world analysis", toArray.Reason, StringComparison.Ordinal);
		Assert.False(analysis.IsCompatible);
	}

	[Theory]
	[InlineData("LinqRangeLocalRepeatedToArrayEntry", "LinqRangeLocalRepeatedToArrayEntry", 2)]
	[InlineData("LinqRangeSameFamilyMergeToArrayEntry", "LinqRangeSameFamilyMergeToArray", 1)]
	public void EnumerableToArrayTracksRangeThroughLocalsAndSameFamilyMerges(
		string entry,
		string containingMethod,
		int expectedCallSites)
	{
		var analysis = Analyze(entry);
		Assert.True(analysis.IsCompatible);
		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var method = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{containingMethod}");
		var terminals = method.Instructions
			.Where(instruction => instruction.OpCode == OpCodes.Call &&
				module.DescribeMethodToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset) is
				{
					TypeName: "System.Linq.Enumerable",
					Name: "ToArray"
				})
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset))
			.ToArray();

		Assert.Equal(expectedCallSites, terminals.Length);
		Assert.All(terminals, terminal => Assert.Equal("RangeToArray", terminal.Definition?.Name));
	}

	[Fact]
	public void EnumerableToArrayRejectsMixedFactoryMergeDuringAnalysis()
	{
		var analysis = Analyze("UnsupportedLinqMixedFactoryMergeEntry");
		var terminal = Assert.Single(
			analysis.Members,
			member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name == "ToArray");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, terminal.Status);
		Assert.Contains("merges different iterator families", terminal.Reason, StringComparison.Ordinal);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void SelectedRangeSelectPipelineUsesExactOfficialBindings()
	{
		var analysis = Analyze("LinqRangeSelectToArrayEntry");
		var members = analysis.Members
			.Where(member => member.Member.TypeName == "System.Linq.Enumerable")
			.ToArray();

		Assert.Equal(3, members.Length);
		Assert.All(members, member =>
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status));
		var select = Assert.Single(members, member => member.Member.Name == "Select");
		Assert.Contains("delegates", select.RequiredFeatures);
		Assert.True(
			analysis.IsCompatible,
			string.Join(
				Environment.NewLine,
				analysis.Members
					.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
					.Select(member => $"{member.Member.DisplayName}: {member.Reason}")));

		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectToArrayEntry");
		var calls = entry.Instructions
			.Where(instruction => instruction.OpCode == OpCodes.Call &&
				module.DescribeMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset)?.TypeName == "System.Linq.Enumerable")
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.Operand!,
				entry,
				instruction.Offset).Definition?.Name)
			.ToArray();
		Assert.Equal(
			["Range", "SelectInt32", "SelectInt32ToArray", "SelectInt32ToArray"],
			calls);
	}

	[Fact]
	public void SelectRejectsNonRangeSourceProvenanceDuringAnalysis()
	{
		var analysis = Analyze("UnsupportedLinqRepeatSelectEntry");
		Assert.Contains(
			analysis.Members,
			member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name == "Select" &&
				member.Status == M68kFrameworkCompatibilityStatus.Unsupported &&
				member.Reason?.Contains("requires exact Range", StringComparison.Ordinal) == true);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void IndexedSelectOverloadRemainsOutsideSelectedSlice()
	{
		var analysis = Analyze("UnsupportedLinqIndexedSelectEntry");
		Assert.Contains(
			analysis.Members,
			member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name == "Select" &&
				member.Status == M68kFrameworkCompatibilityStatus.Unsupported);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void SelectedRangeSelectWherePipelineUsesExactOfficialBindings()
	{
		var analysis = Analyze("LinqRangeSelectWhereToArrayEntry");
		var members = analysis.Members
			.Where(member => member.Member.TypeName == "System.Linq.Enumerable")
			.ToArray();

		Assert.Equal(4, members.Length);
		Assert.All(members, member =>
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status));
		var where = Assert.Single(members, member => member.Member.Name == "Where");
		Assert.Contains("delegates", where.RequiredFeatures);
		Assert.True(analysis.IsCompatible);

		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectWhereToArrayEntry");
		var calls = entry.Instructions
			.Where(instruction => instruction.OpCode == OpCodes.Call &&
				module.DescribeMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset)?.TypeName == "System.Linq.Enumerable")
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.Operand!,
				entry,
				instruction.Offset).Definition?.Name)
			.ToArray();
		Assert.Equal(
			["Range", "SelectInt32", "SelectWhereInt32", "SelectWhereInt32ToArray"],
			calls);
	}

	[Fact]
	public void WhereRejectsUnsupportedSourceAndIndexedOverloadDuringAnalysis()
	{
		var repeat = Analyze("UnsupportedLinqRepeatWhereEntry");
		Assert.Contains(
			repeat.Members,
			member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name == "Where" &&
				member.Status == M68kFrameworkCompatibilityStatus.Unsupported &&
				member.Reason?.Contains("Range or Range.Select", StringComparison.Ordinal) == true);
		Assert.False(repeat.IsCompatible);

		var indexed = Analyze("UnsupportedLinqIndexedWhereEntry");
		Assert.Contains(
			indexed.Members,
			member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name == "Where" &&
				member.Status == M68kFrameworkCompatibilityStatus.Unsupported);
		Assert.False(indexed.IsCompatible);
	}

	[Fact]
	public void SelectedAnyOverloadsUseExactOfficialBindingsAndPrivateTargets()
	{
		foreach (var entryName in new[]
		{
			"LinqAnyWithoutPredicateEntry",
			"LinqAnyPredicateEntry"
		})
		{
			var analysis = Analyze(entryName);
			var anyMembers = analysis.Members
				.Where(member => member.Member.TypeName == "System.Linq.Enumerable" &&
					member.Member.Name == "Any")
				.ToArray();
			Assert.NotEmpty(anyMembers);
			Assert.All(anyMembers, member =>
				Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status));
			Assert.True(analysis.IsCompatible);
		}

		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var withoutPredicate = ResolveAnyTargets(module, "LinqAnyWithoutPredicateEntry");
		Assert.True(
			new HashSet<string>
			{
				"RangeAny",
				"RepeatInt32Any",
				"SelectInt32Any",
				"RangeWhereInt32Any",
				"SelectWhereInt32Any"
			}.SetEquals(withoutPredicate),
			string.Join(", ", withoutPredicate));
		var withPredicate = ResolveAnyTargets(module, "LinqAnyPredicateEntry");
		Assert.True(
			new HashSet<string>
			{
				"RangeAnyPredicate",
				"RepeatInt32AnyPredicate",
				"SelectInt32AnyPredicate",
				"RangeWhereInt32AnyPredicate",
				"SelectWhereInt32AnyPredicate"
			}.SetEquals(withPredicate),
			string.Join(", ", withPredicate));

		static HashSet<string> ResolveAnyTargets(
			CompilationModule module,
			string entryName)
		{
			var entry = module.ResolveManagedMethod(
				"CopperSharp.Compiler.Tests",
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryName}");
			return entry.Instructions
				.Where(instruction => instruction.OpCode == OpCodes.Call &&
					module.DescribeMethodToken(
						(int)instruction.Operand!,
						entry,
						instruction.Offset) is
					{
						TypeName: "System.Linq.Enumerable",
						Name: "Any"
					})
				.Select(instruction => module.ResolveMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset).Definition!.Name)
				.ToHashSet(StringComparer.Ordinal);
		}
	}

	[Fact]
	public void AnyRejectsArrayAndUnselectedElementSourcesDuringAnalysis()
	{
		foreach (var entry in new[]
		{
			"UnsupportedLinqArrayAnyEntry",
			"UnsupportedLinqByteAnyEntry"
		})
		{
			var analysis = Analyze(entry);
			Assert.Contains(
				analysis.Members,
				member => member.Member.TypeName == "System.Linq.Enumerable" &&
					member.Member.Name == "Any" &&
					member.Status == M68kFrameworkCompatibilityStatus.Unsupported);
			Assert.False(analysis.IsCompatible);
		}
	}

	[Fact]
	public void SelectedTakeOverloadPreservesEveryExactPrivateSourceFamily()
	{
		foreach (var entryName in new[]
		{
			"LinqTakeToArrayEntry",
			"LinqTakeAnyEntry"
		})
		{
			var analysis = Analyze(entryName);
			var takeMembers = analysis.Members
				.Where(member => member.Member.TypeName == "System.Linq.Enumerable" &&
					member.Member.Name == "Take")
				.ToArray();
			Assert.NotEmpty(takeMembers);
			Assert.All(takeMembers, member =>
				Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status));
			Assert.True(analysis.IsCompatible);
		}

		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqTakeToArrayEntry");
		var targets = entry.Instructions
			.Where(instruction => instruction.OpCode == OpCodes.Call &&
				module.DescribeMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset) is
				{
					TypeName: "System.Linq.Enumerable",
					Name: "Take"
				})
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.Operand!,
				entry,
				instruction.Offset).Definition!.Name)
			.ToHashSet(StringComparer.Ordinal);
		Assert.True(
			new HashSet<string>
			{
				"RangeTakeInt32",
				"RepeatInt32TakeInt32",
				"SelectInt32TakeInt32",
				"RangeWhereInt32TakeInt32",
				"SelectWhereInt32TakeInt32"
			}.SetEquals(targets),
			string.Join(", ", targets));
	}

	[Fact]
	public void TakeRejectsArrayAndUnselectedElementSourcesDuringAnalysis()
	{
		foreach (var entry in new[]
		{
			"UnsupportedLinqArrayTakeEntry",
			"UnsupportedLinqByteTakeEntry"
		})
		{
			var analysis = Analyze(entry);
			Assert.Contains(
				analysis.Members,
				member => member.Member.TypeName == "System.Linq.Enumerable" &&
					member.Member.Name == "Take" &&
					member.Status == M68kFrameworkCompatibilityStatus.Unsupported);
			Assert.False(analysis.IsCompatible);
		}
	}

	[Fact]
	public void SelectedSumOverloadsResolveEveryExactPrivateSourceFamily()
	{
		var analysis = Analyze("LinqSumEveryPrivateTargetEntry");
		var sumMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name == "Sum")
			.ToArray();
		Assert.NotEmpty(sumMembers);
		Assert.All(sumMembers, member =>
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status));
		Assert.True(analysis.IsCompatible);

		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqSumEveryPrivateTargetEntry");
		var targets = entry.Instructions
			.Where(instruction => instruction.OpCode == OpCodes.Call &&
				module.DescribeMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset) is
				{
					TypeName: "System.Linq.Enumerable",
					Name: "Sum"
				})
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.Operand!,
				entry,
				instruction.Offset).Definition!.Name)
			.ToHashSet(StringComparer.Ordinal);
		Assert.True(
			new HashSet<string>
			{
				"RangeSum",
				"RangeSumSelector",
				"RepeatInt32Sum",
				"RepeatInt32SumSelector",
				"SelectInt32Sum",
				"SelectInt32SumSelector",
				"RangeWhereInt32Sum",
				"RangeWhereInt32SumSelector",
				"RangeWhereInt32TakeSum",
				"RangeWhereInt32TakeSumSelector",
				"SelectWhereInt32Sum",
				"SelectWhereInt32SumSelector",
				"SelectWhereInt32TakeSum",
				"SelectWhereInt32TakeSumSelector"
			}.SetEquals(targets),
			string.Join(", ", targets));
	}

	[Fact]
	public void ReferenceFreeStructArraySumResolvesOnlyToItsGenericArrayShadow()
	{
		var analysis = Analyze("LinqArrayImageBlockSumSelectorEntry");
		Assert.True(analysis.IsCompatible);
		Assert.Contains(
			analysis.Members,
			member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name == "Sum" &&
				member.Status == M68kFrameworkCompatibilityStatus.Implemented &&
				member.Binding?.EndsWith("::SumSelector", StringComparison.Ordinal) == true);

		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqArrayImageBlockSumSelectorEntry");
		var target = entry.Instructions
			.Where(instruction => instruction.OpCode == OpCodes.Call &&
				module.DescribeMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset) is
				{
					TypeName: "System.Linq.Enumerable",
					Name: "Sum"
				})
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.Operand!,
				entry,
				instruction.Offset).Definition!.Name)
			.Distinct(StringComparer.Ordinal)
			.Single();
		Assert.Equal("ArraySumSelector", target);
	}

	[Fact]
	public void SumRejectsUnselectedArraysAndNumericOverloadsDuringAnalysis()
	{
		foreach (var entry in new[]
		{
			"UnsupportedLinqArraySumEntry",
			"UnsupportedLinqArraySumSelectorEntry",
			"UnsupportedLinqReferenceStructArraySumSelectorEntry",
			"UnsupportedLinqLongSumEntry"
		})
		{
			var analysis = Analyze(entry);
			Assert.Contains(
				analysis.Members,
				member => member.Member.TypeName == "System.Linq.Enumerable" &&
					member.Member.Name == "Sum" &&
					member.Status == M68kFrameworkCompatibilityStatus.Unsupported);
			Assert.False(analysis.IsCompatible);
		}
	}

	[Fact]
	public void DictionaryValuesOrderingUsesOnlyTheExactTwoSelectedShadows()
	{
		var analysis = Analyze("LinqDictionaryValuesOrderByThenByEntry");
		Assert.True(
			analysis.IsCompatible,
			string.Join(
				Environment.NewLine,
				analysis.Members.Select(member =>
					$"{member.Member.TypeName}::{member.Member.Name} => " +
					$"{member.Status} {member.Binding} {member.Reason}")));
		var ordering = analysis.Members
			.Where(member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name is "OrderBy" or "ThenBy")
			.ToArray();
		Assert.Equal(2, ordering.Length);
		Assert.All(ordering, member =>
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status));

		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqDictionaryValuesOrderByThenByEntry");
		var targets = entry.Instructions
			.Where(instruction => instruction.OpCode == OpCodes.Call &&
				module.DescribeMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset) is
				{
					TypeName: "System.Linq.Enumerable",
					Name: "OrderBy" or "ThenBy"
				})
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.Operand!,
				entry,
				instruction.Offset).Definition!.Name)
			.ToArray();
		Assert.Equal(
			["DictionaryUInt32ValuesOrderBy", "DictionaryUInt32ValuesThenBy"],
			targets);
	}

	[Theory]
	[InlineData("UnsupportedLinqArrayOrderByEntry", "OrderBy")]
	[InlineData("UnsupportedLinqAdditionalThenByEntry", "ThenBy")]
	public void OrderingRejectsUnselectedSourcesAndAdditionalKeys(
		string entry,
		string memberName)
	{
		var analysis = Analyze(entry);
		Assert.Contains(
			analysis.Members,
			member => member.Member.TypeName == "System.Linq.Enumerable" &&
				member.Member.Name == memberName &&
				member.Status == M68kFrameworkCompatibilityStatus.Unsupported);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void LinqCallSitesResolveToTheirExactPrivateImplementations()
	{
		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeToArrayEntry");
		var calls = entry.Instructions
			.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => new
			{
				Identity = module.DescribeMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset),
				Target = module.ResolveMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset)
			})
			.Where(call => call.Identity?.TypeName == "System.Linq.Enumerable")
			.ToArray();

		Assert.Equal(4, calls.Length);
		Assert.Equal(
			["Range", "RangeToArray", "Range", "RangeToArray"],
			calls.Select(call => call.Target.Definition?.Name).ToArray());
		Assert.All(calls, call => Assert.Equal(
			"CopperSharp.Runtime.Managed",
			call.Target.Definition?.ModuleName));
		Assert.All(
			entry.Instructions.Where(instruction =>
				instruction.OpCode == OpCodes.Call &&
				module.DescribeMethodToken(
					(int)instruction.Operand!,
					entry,
					instruction.Offset)?.TypeName == "System.Linq.Enumerable"),
			instruction => Assert.Null(module.GetTriggeredTypeInitializer(entry, instruction)));
	}

	[Fact]
	public void LinqMachineIrPreservesExactFrameworkCallTokens()
	{
		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumerable).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeToArrayEntry");
		var machine = CilMachineIrBuilder.Build(entry, module);
		var machineCalls = machine.Blocks
			.SelectMany(block => block.Instructions)
			.Where(instruction => instruction.Operation == M68kMachineOperation.Call)
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.SourceInstruction!.Operand!,
				entry,
				instruction.SourceInstruction.Offset))
			.ToArray();
		var calls = machineCalls
			.Where(target => target.FrameworkBinding?.Member.DeclaringType.MetadataName ==
				"System.Linq.Enumerable")
			.ToArray();

		Assert.Equal(4, machineCalls.Length);
		Assert.Equal(4, calls.Length);
		Assert.Equal(
			["Range", "RangeToArray", "Range", "RangeToArray"],
			calls.Select(call => call.Definition?.Name).ToArray());
		Assert.DoesNotContain(
			machine.Blocks.SelectMany(block => block.Instructions),
			instruction => instruction.Operation == M68kMachineOperation.TypeInitialize);
	}

	[Fact]
	public void MemoryCopyMembersUseExactOfficialAllocationFreeIntrinsics()
	{
		var analysis = Analyze("MemoryCopyOperationsEntry");
		var copyMembers = analysis.Members
			.Where(member =>
				member.Member.Name is "CopyTo" or "TryCopyTo" &&
				(member.Member.TypeName.StartsWith(
					"System.Memory`1<",
					StringComparison.Ordinal) ||
				 member.Member.TypeName.StartsWith(
					"System.ReadOnlyMemory`1<",
					StringComparison.Ordinal)))
			.ToArray();
		Assert.Equal(4, copyMembers.Length);
		Assert.All(copyMembers, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, member.Status);
			Assert.Contains("copy-to", member.Binding, StringComparison.Ordinal);
			Assert.Contains("managed-memory", member.RequiredFeatures);
			Assert.Contains("managed-arrays", member.RequiredFeatures);
			Assert.Contains("managed-gc", member.RequiredFeatures);
			Assert.Contains("spans", member.RequiredFeatures);
		});
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Memory", StringComparison.Ordinal));
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
	public void ParameterlessInt64FormattingUsesPrivatePayForPlayShadows()
	{
		var analysis = Analyze("Int64ToStringEntry");
		var formatters = analysis.Members
			.Where(member => member.Member.Name == "ToString" &&
				member.Member.TypeName is "System.Int64" or "System.UInt64")
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
	public void StringFormatErasesImmediateFourIntegerParamsArrayAndBoxes()
	{
		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringFormat).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::StringFormatParamsIntegerEntry");
		var machine = CilMachineIrBuilder.Build(entry, module);
		var instructions = machine.Blocks
			.SelectMany(block => block.Instructions)
			.ToArray();

		Assert.DoesNotContain(
			instructions,
			instruction => instruction.Operation is
				M68kMachineOperation.ArrayAllocate or
				M68kMachineOperation.ArrayStore or
				M68kMachineOperation.Box);
		var formatCall = Assert.Single(instructions.Where(instruction =>
			instruction.Operation == M68kMachineOperation.Call &&
			instruction.SourceInstruction is { Operand: int token } &&
			module.ResolveMethodToken(
				token,
				entry,
				instruction.SourceInstruction.Offset).FrameworkBinding?.Member is
				{
					Name: "Format",
					DeclaringType.MetadataName: "System.String"
				}));
		Assert.Equal(
			5,
			formatCall.Uses.Length + instructions.Count(instruction =>
				instruction.Operation == M68kMachineOperation.OutgoingArgumentPush));
		var target = module.ResolveMethodToken(
			(int)formatCall.SourceInstruction!.Operand!,
			entry,
			formatCall.SourceInstruction.Offset);
		Assert.Equal("Format4", target.Definition?.Name);
		Assert.Equal(5, target.Definition?.Signature.ParameterTypes.Length);
	}

	[Fact]
	public void StringFormatKeepsSharedComputedArgumentDefinitionAlive()
	{
		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringFormat).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::StringFormatSharedComputedParamsEntry");
		var machine = CilMachineIrBuilder.Build(entry, module);
		var instructions = machine.Blocks
			.SelectMany(block => block.Instructions)
			.ToArray();

		Assert.DoesNotContain(
			instructions,
			instruction => instruction.Operation is
				M68kMachineOperation.ArrayAllocate or
				M68kMachineOperation.ArrayStore or
				M68kMachineOperation.Box);
		var definitions = instructions
			.SelectMany(static instruction => instruction.Definitions)
			.Concat(machine.Blocks.SelectMany(static block =>
				block.Phis.Select(static phi => phi.Definition)))
			.ToHashSet();
		Assert.All(
			instructions.SelectMany(static instruction => instruction.Uses)
				.Concat(machine.Blocks.SelectMany(static block =>
					block.Phis.SelectMany(static phi => phi.Inputs.Values))),
			use => Assert.True(
				definitions.Contains(use),
				$"Machine SSA value v{use} has no surviving definition."));
	}

	[Fact]
	public void StringFormatFixedObjectOverloadsEraseBoxes()
	{
		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringFormat).Assembly.Location]);
		var entry = module.ResolveManagedMethod(
			"CopperSharp.Compiler.Tests",
			"CopperSharp.Compiler.Tests.CompilerFixtures::StringFormatFixedArgumentsEntry");
		var machine = CilMachineIrBuilder.Build(entry, module);
		var instructions = machine.Blocks
			.SelectMany(block => block.Instructions)
			.ToArray();

		Assert.DoesNotContain(
			instructions,
			instruction => instruction.Operation == M68kMachineOperation.Box);
		var targets = instructions
			.Where(instruction =>
				instruction.Operation == M68kMachineOperation.Call &&
				instruction.SourceInstruction is { Operand: int token } &&
				module.ResolveMethodToken(
					token,
					entry,
					instruction.SourceInstruction.Offset).FrameworkBinding?.Member is
					{
						Name: "Format",
						DeclaringType.MetadataName: "System.String"
					})
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.SourceInstruction!.Operand!,
				entry,
				instruction.SourceInstruction.Offset).Definition?.Name)
			.ToArray();
		Assert.Equal(["Format1", "Format3"], targets);
	}

	[Fact]
	public void StringFormatReadOnlySpanParamsErasesInlineArrayAndBoxes()
	{
		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringFormat).Assembly.Location]);
		foreach (var (entryName, shadowName, parameterCount) in new[]
		{
			("StringFormatSpanParamsEntry", "Format4", 5),
			("StringFormatSpanEightParamsEntry", "Format8", 9)
		})
		{
			var analysis = Analyze(entryName);
			Assert.True(
				analysis.IsCompatible,
				string.Join(
					Environment.NewLine,
					analysis.Members.Select(member =>
						$"{member.Member.DisplayName}: {member.Status} {member.Reason}")));
			var entry = module.ResolveManagedMethod(
				"CopperSharp.Compiler.Tests",
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryName}");
			var machine = CilMachineIrBuilder.Build(entry, module);
			var instructions = machine.Blocks
				.SelectMany(static block => block.Instructions)
				.ToArray();
			Assert.DoesNotContain(
				instructions,
				instruction => instruction.Operation == M68kMachineOperation.Box);
			var formatCall = Assert.Single(instructions.Where(instruction =>
				instruction.Operation == M68kMachineOperation.Call &&
				instruction.SourceInstruction is { Operand: int token } &&
				module.ResolveMethodToken(
					token,
					entry,
					instruction.SourceInstruction.Offset).FrameworkBinding?.Member is
					{
						Name: "Format",
						DeclaringType.MetadataName: "System.String"
					}));
			var target = module.ResolveMethodToken(
				(int)formatCall.SourceInstruction!.Operand!,
				entry,
				formatCall.SourceInstruction.Offset);
			Assert.Equal(shadowName, target.Definition?.Name);
			Assert.Equal(
				parameterCount,
				target.Definition?.Signature.ParameterTypes.Length);
		}
	}

	[Fact]
	public void StringFormatRejectsParamsArraysThatFlowThroughLocals()
	{
		var analysis = Analyze("UnsupportedEscapingStringFormatParamsEntry");
		Assert.False(analysis.IsCompatible);
		var format = Assert.Single(analysis.Members.Where(member =>
			member.Member is { TypeName: "System.String", Name: "Format" }));
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, format.Status);
		Assert.False(string.IsNullOrWhiteSpace(format.Reason));
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
	public void LinkerMapRecordsDeterministicCompilerContractAndTargetProvenance()
	{
		var request = Request("StringLiteralEntry") with
		{
			RuntimeProfile = M68kRuntimeProfile.Application
		};
		var generic = M68kCompiler.Compile(request);
		var amiga = AmigaM68kCompiler.Compile(request);

		Assert.Contains(
			"COMPILER CopperSharp.Compiler 0.1.0-preview.1",
			generic.Map,
			StringComparison.Ordinal);
		Assert.Contains(
			"CONTRACT net10.0-10.0.9 Microsoft.NETCore.App.Ref 10.0.9",
			generic.Map,
			StringComparison.Ordinal);
		Assert.Contains(
			"TARGET m68k CopperSharp.Compiler 0.1.0-preview.1",
			generic.Map,
			StringComparison.Ordinal);
		Assert.Contains("PROFILE Application", generic.Map, StringComparison.Ordinal);
		Assert.Contains("CPU M68000", generic.Map, StringComparison.Ordinal);
		Assert.Contains("FORMAT Hunk", generic.Map, StringComparison.Ordinal);
		Assert.Contains(
			"TARGET amiga-m68k CopperSharp.Targets.Amiga 0.1.0-preview.1",
			amiga.Map,
			StringComparison.Ordinal);
		Assert.DoesNotContain(Assembly.GetExecutingAssembly().Location, amiga.Map);
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
		Assert.Contains(
			$"METRICS artifact-bytes={linked.Image.Length} code-bytes={linked.Code.Length} " +
			$"symbols={linked.Symbols.Count} relocations={linked.Relocations.Count} " +
			$"loops={linked.LoopFootprints.Count} framework-features=2 " +
			$"managed-allocation-sites={linked.FrameworkAnalysis.ManagedAllocationSites.Count}",
			linked.Map,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			unrelated.Symbols,
			symbol => symbol.Name.Contains("ShadowMath", StringComparison.Ordinal));
		Assert.DoesNotContain("numerics", unrelated.FrameworkFeatures);
		Assert.DoesNotContain(
			Analyze("StringLiteralEntry").Members,
			candidate => candidate.RequiredFeatures.Contains("numerics"));
	}

	[Fact]
	public void ExactMathSurfaceIsAdmittedByTheCompatibilityLedger()
	{
		var integral = Analyze("ShadowMathIntegralSurfaceEntry");
		Assert.True(integral.IsCompatible);
		Assert.Equal(
			42,
			integral.Members.Count(candidate =>
				candidate.Member.TypeName == "System.Math" &&
				candidate.Status == M68kFrameworkCompatibilityStatus.Implemented));

		var ieee = M68kCompiler.AnalyzeFramework(
			Request("ShadowMathIeeeSurfaceEntry") with
			{
				FloatingPoint = M68kFloatingPointMode.SoftFloat
			});
		Assert.True(ieee.IsCompatible);
		Assert.Equal(
			27,
			ieee.Members.Count(candidate =>
				(candidate.Member.TypeName is "System.Math" or "System.Double" or "System.Single") &&
				candidate.Status == M68kFrameworkCompatibilityStatus.Implemented));

		var rounding = M68kCompiler.AnalyzeFramework(
			Request("ShadowMathSoftwareRoundingEntry") with
			{
				FloatingPoint = M68kFloatingPointMode.SoftFloat
			});
		Assert.True(rounding.IsCompatible);
		Assert.Equal(
			6,
			rounding.Members.Count(candidate =>
				candidate.Member.TypeName == "System.Math" &&
				candidate.Status == M68kFrameworkCompatibilityStatus.Implemented));
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
	public void DictionaryIntegralCoreUsesExactGenericPayForPlayShadows()
	{
		var analysis = Analyze("DictionaryInt32Entry");
		var members = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.Dictionary`2",
				StringComparison.Ordinal))
			.ToArray();
		Assert.Equal(6, members.Length);
		Assert.All(members, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowDictionary`2::",
				member.Binding,
				StringComparison.Ordinal);
			Assert.Contains("managed-collections", member.RequiredFeatures);
			Assert.Contains("managed-arrays", member.RequiredFeatures);
		});
		Assert.True(analysis.IsCompatible);

		var referenceValues = Analyze("DictionaryInt32ReferenceGcEntry");
		Assert.True(referenceValues.IsCompatible);
		Assert.Contains(
			referenceValues.Members,
			member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.Dictionary`2<int,string>",
				StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Implemented);

		var unrelated = Analyze("IntegerToStringEntry");
		Assert.DoesNotContain(
			unrelated.Members,
			member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.Dictionary`2",
				StringComparison.Ordinal));
		var unrelatedAssembly = M68kCompiler.Compile(Request("IntegerToStringEntry") with
		{
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});
		Assert.DoesNotContain(
			"ShadowDictionary",
			unrelatedAssembly.Text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void DictionaryStringKeysUseExactGenericShadows()
	{
		var analysis = Analyze("DictionaryStringGcEntry");
		var members = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.Dictionary`2<string,string>",
				StringComparison.Ordinal))
			.ToArray();
		Assert.NotEmpty(members);
		Assert.All(members, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowDictionary`2::",
				member.Binding,
				StringComparison.Ordinal);
			Assert.Contains("managed-collections", member.RequiredFeatures);
			Assert.Contains("managed-arrays", member.RequiredFeatures);
			Assert.Contains("managed-gc", member.RequiredFeatures);
		});
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void DictionaryReferenceFreeStructValuesUseExactGenericShadows()
	{
		var analysis = Analyze("DictionaryReferenceFreeStructValueEntry");
		var members = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.Dictionary`2<uint,",
				StringComparison.Ordinal))
			.ToArray();
		Assert.Equal(6, members.Length);
		Assert.All(members, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowDictionary`2::",
				member.Binding,
				StringComparison.Ordinal);
		});
		Assert.True(analysis.IsCompatible);

		var unsupported = Analyze("UnsupportedDictionaryReferenceStructValueEntry");
		Assert.False(unsupported.IsCompatible);
		Assert.Contains(
			unsupported.Members,
			member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.Dictionary`2<uint,",
				StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Unsupported);

		var values = Analyze("DictionaryReferenceFreeStructValuesIdentityEntry");
		var getter = Assert.Single(
			values.Members,
			member => member.Member.Name == "get_Values");
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, getter.Status);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowDictionary`2::get_Values",
			getter.Binding);
		Assert.True(values.IsCompatible);

		var keys = Analyze("UnsupportedDictionaryReferenceFreeStructKeysEntry");
		Assert.Contains(
			keys.Members,
			member => member.Member.Name == "get_Keys" &&
				member.Status == M68kFrameworkCompatibilityStatus.Unsupported);
		Assert.False(keys.IsCompatible);
	}

	[Fact]
	public void ListCapacityAndMutationMembersUseExactGenericShadows()
	{
		var analysis = Analyze("ListCapacityMutationEntry");
		var members = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.List`1",
				StringComparison.Ordinal))
			.ToArray();
		Assert.Equal(9, members.Length);
		Assert.Equal(
			new[]
			{
				".ctor",
				"Add",
				"Clear",
				"get_Capacity",
				"get_Count",
				"get_Item",
				"RemoveAt",
				"set_Capacity",
				"ToArray"
			},
			members.Select(member => member.Member.Name).Order().ToArray());
		Assert.All(members, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowList`1::",
				member.Binding,
				StringComparison.Ordinal);
			Assert.Contains("managed-collections", member.RequiredFeatures);
			Assert.Contains("managed-arrays", member.RequiredFeatures);
			Assert.Contains("managed-gc", member.RequiredFeatures);
		});
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void ListDirectEnumerationUsesExactPublicEnumeratorIdentity()
	{
		var analysis = Analyze("ListDirectEnumerationEntry");
		var getEnumerator = Assert.Single(
			analysis.Members,
			member => member.Member.Name == "GetEnumerator" &&
				member.Member.TypeName.StartsWith(
					"System.Collections.Generic.List`1<int>",
					StringComparison.Ordinal));
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, getEnumerator.Status);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowList`1::GetEnumerator",
			getEnumerator.Binding);

		var enumeratorMembers = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.List`1/Enumerator<int>",
				StringComparison.Ordinal))
			.ToArray();
		Assert.Equal(2, enumeratorMembers.Length);
		Assert.Equal(
			["get_Current", "MoveNext"],
			enumeratorMembers.Select(member => member.Member.Name).Order().ToArray());
		Assert.All(enumeratorMembers, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowListEnumerator`1::",
				member.Binding,
				StringComparison.Ordinal);
		});
		var dispose = Assert.Single(
			analysis.Members,
			member => member.Member is
				{ TypeName: "System.IDisposable", Name: "Dispose" });
		Assert.Equal(M68kFrameworkCompatibilityStatus.Intrinsic, dispose.Status);
		Assert.Equal("intrinsic:list-enumerator-dispose", dispose.Binding);
		Assert.Equal(3, analysis.ManagedAllocationSites.Count);
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Enumerator", StringComparison.Ordinal));
		Assert.True(
			analysis.IsCompatible,
			string.Join(
				Environment.NewLine,
				analysis.Members.Select(member =>
					$"{member.Member.TypeName}::{member.Member.Name}: {member.Status} {member.Reason}")));
	}

	[Fact]
	public void ListInterfaceEnumerationIsExplicitlyDeferredWithoutRootingImplementations()
	{
		var analysis = Analyze("ListInterfaceEnumerationEntry");
		var unsupported = analysis.Members
			.Where(member =>
				member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.ToArray();
		Assert.Equal(4, unsupported.Length);
		Assert.Equal(
			[
				("System.Collections.Generic.IEnumerable`1<int>", "GetEnumerator"),
				("System.Collections.Generic.IEnumerator`1<int>", "get_Current"),
				("System.Collections.IEnumerator", "MoveNext"),
				("System.IDisposable", "Dispose")
			],
			unsupported
				.Select(member => (member.Member.TypeName, member.Member.Name))
				.OrderBy(member => member.TypeName, StringComparer.Ordinal)
				.ThenBy(member => member.Name, StringComparer.Ordinal)
				.ToArray());
		Assert.All(
			unsupported,
			member => Assert.NotEmpty(member.Reason ?? string.Empty));
		Assert.Equal(2, analysis.ManagedAllocationSites.Count);
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("Enumerator", StringComparison.Ordinal));
		Assert.DoesNotContain(
			analysis.Members,
			member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.List`1/Enumerator",
				StringComparison.Ordinal));
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void FloatingListEqualityUsesBitwiseIntrinsicWithoutComparerObject()
	{
		var analysis = Analyze("ListFloatingEqualityEntry");
		var equalityMembers = analysis.Members
			.Where(member => member.Member.Name is "Contains" or "IndexOf" or "Remove")
			.ToArray();
		Assert.Equal(6, equalityMembers.Length);
		Assert.All(equalityMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Implemented,
			member.Status));
		Assert.DoesNotContain(
			analysis.Members,
			member => member.Member.TypeName.Contains(
				"EqualityComparer",
				StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void IntegralListEqualityMembersUseExactGenericShadows()
	{
		var analysis = Analyze("ListInt32EqualityEntry");
		var equalityMembers = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.List`1<int>",
				StringComparison.Ordinal) &&
				member.Member.Name is "Contains" or "IndexOf" or "Remove")
			.ToArray();
		Assert.Equal(3, equalityMembers.Length);
		Assert.Equal(
			["Contains", "IndexOf", "Remove"],
			equalityMembers.Select(member => member.Member.Name).Order().ToArray());
		Assert.All(equalityMembers, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.Equal(
				$"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowList`1::{member.Member.Name}",
				member.Binding);
		});
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("EqualityComparer", StringComparison.Ordinal));
		Assert.DoesNotContain(
			analysis.Members,
			member => member.Member.TypeName.Contains("EqualityComparer", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void LocalEnumListEqualityUsesProvenUnderlyingRepresentation()
	{
		foreach (var entry in new[]
		{
			"ListByteEnumEqualityEntry",
			"ListIntEnumEqualityEntry",
			"ListLongEnumEqualityEntry"
		})
		{
			var analysis = Analyze(entry);
			var equalityMembers = analysis.Members
				.Where(member => member.Member.Name is "Contains" or "IndexOf" or "Remove")
				.ToArray();
			Assert.Equal(
				["Contains", "IndexOf", "Remove"],
				equalityMembers
					.Select(member => member.Member.Name)
					.Distinct(StringComparer.Ordinal)
					.Order()
					.ToArray());
			Assert.All(equalityMembers, member =>
			{
				Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
				Assert.StartsWith(
					"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowList`1::",
					member.Binding,
					StringComparison.Ordinal);
			});
			Assert.DoesNotContain(
				analysis.ManagedAllocationSites,
				site => site.AllocatedType.Contains("EqualityComparer", StringComparison.Ordinal));
			Assert.True(analysis.IsCompatible);
		}
	}

	[Fact]
	public void ReferencedModuleEnumListEqualityUsesResolvedUnderlyingRepresentation()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("ListExternalEnumEqualityEntry") with
			{
				ManagedAssemblyPaths =
				[
					typeof(
						CopperSharp.Compiler.Tests.MultiModule.ExternalListState)
						.Assembly.Location
				]
			});
		Assert.All(
			analysis.Members.Where(member =>
				member.Member.Name is "Contains" or "IndexOf" or "Remove"),
			member => Assert.Equal(
				M68kFrameworkCompatibilityStatus.Implemented,
				member.Status));
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("EqualityComparer", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void StringListEqualityUsesExactGenericShadowsAndOrdinalIntrinsic()
	{
		var analysis = Analyze("ListStringEqualityEntry");
		var equalityMembers = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.List`1<string>",
				StringComparison.Ordinal) &&
				member.Member.Name is "Contains" or "IndexOf" or "Remove")
			.ToArray();
		Assert.Equal(
			["Contains", "IndexOf", "Remove"],
			equalityMembers
				.Select(member => member.Member.Name)
				.Distinct(StringComparer.Ordinal)
				.Order()
				.ToArray());
		Assert.All(equalityMembers, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowList`1::",
				member.Binding,
				StringComparison.Ordinal);
		});
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("EqualityComparer", StringComparison.Ordinal));
		Assert.DoesNotContain(
			analysis.Members,
			member => member.Member.TypeName.Contains("EqualityComparer", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void NullableIntListEqualityUsesExistingNullableContractWithoutComparerObject()
	{
		var analysis = Analyze("ListNullableIntEqualityEntry");
		var equalityMembers = analysis.Members
			.Where(member => member.Member.Name is "Contains" or "IndexOf" or "Remove")
			.ToArray();
		Assert.Equal(
			["Contains", "IndexOf", "Remove"],
			equalityMembers
				.Select(member => member.Member.Name)
				.Distinct(StringComparer.Ordinal)
				.Order()
				.ToArray());
		Assert.All(equalityMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Implemented,
			member.Status));
		Assert.Contains(
			analysis.Members,
			member => member.Member.TypeName.StartsWith(
				"System.Nullable<",
				StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Intrinsic);
		Assert.DoesNotContain(
			analysis.Members,
			member => member.Member.TypeName.Contains(
				"EqualityComparer",
				StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Theory]
	[InlineData("ListSealedReferenceFallbackEqualityEntry")]
	[InlineData("ListSealedReferenceOverrideEqualityEntry")]
	public void SealedReferenceListEqualityUsesProvenObjectEqualsFallback(string entry)
	{
		var analysis = Analyze(entry);
		var equalityMembers = analysis.Members
			.Where(member => member.Member.Name is "Contains" or "IndexOf" or "Remove")
			.ToArray();
		Assert.Equal(
			["Contains", "IndexOf", "Remove"],
			equalityMembers
				.Select(member => member.Member.Name)
				.Distinct(StringComparer.Ordinal)
				.Order()
				.ToArray());
		Assert.All(equalityMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Implemented,
			member.Status));
		Assert.DoesNotContain(
			analysis.ManagedAllocationSites,
			site => site.AllocatedType.Contains("EqualityComparer", StringComparison.Ordinal));
		Assert.DoesNotContain(
			analysis.Members,
			member => member.Member.TypeName.Contains("EqualityComparer", StringComparison.Ordinal));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void SealedEquatableListEqualityUsesExactTypedContract()
	{
		var analysis = Analyze("ListSealedEquatableReferenceEntry");
		var contains = Assert.Single(
			analysis.Members,
			member => member.Member.Name == "Contains" &&
				member.Member.TypeName.StartsWith(
					"System.Collections.Generic.List`1",
					StringComparison.Ordinal));
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, contains.Status);
		Assert.Contains(
			analysis.Members,
			member => member.Member.Name == "Equals" &&
				member.Member.TypeName.StartsWith(
					"System.IEquatable`1",
					StringComparison.Ordinal) &&
				member.Status == M68kFrameworkCompatibilityStatus.Implemented);
		Assert.True(analysis.IsCompatible);
	}

	[Theory]
	[InlineData("UnsupportedListNonSealedReferenceEntry")]
	public void UnprovenReferenceListEqualityRemainsExplicitlyDeferred(string entry)
	{
		var analysis = Analyze(entry);
		var contains = Assert.Single(
			analysis.Members,
			member => member.Member.Name == "Contains" &&
				member.Member.TypeName.StartsWith(
					"System.Collections.Generic.List`1",
					StringComparison.Ordinal));
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, contains.Status);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PublicIntegralEqualityComparerUsesManagedPerTypeSingleton()
	{
		var analysis = Analyze("PublicIntegralEqualityComparerEntry");
		var comparerMembers = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.EqualityComparer`1<",
				StringComparison.Ordinal))
			.ToArray();
		Assert.Equal(12, comparerMembers.Length);
		Assert.All(comparerMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Implemented,
			member.Status));
		Assert.Contains(comparerMembers, member =>
			member.Member.Name == "get_Default" &&
			member.Binding?.Contains(
				"ShadowEqualityComparer",
				StringComparison.Ordinal) == true);
		var interfaceMembers = analysis.Members
			.Where(member => member.Member.TypeName.StartsWith(
				"System.Collections.Generic.IEqualityComparer`1<int>",
				StringComparison.Ordinal))
			.ToArray();
		Assert.Equal(
			["Equals", "GetHashCode"],
			interfaceMembers
				.Select(member => member.Member.Name)
				.Distinct(StringComparer.Ordinal)
				.Order()
				.ToArray());
		Assert.All(interfaceMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Implemented,
			member.Status));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PublicFloatingEqualityComparerUsesManagedSingletonAndBitKernels()
	{
		var analysis = Analyze("PublicFloatingEqualityComparerEntry");
		var comparerMembers = analysis.Members
			.Where(member => member.Member.TypeName.Contains(
				"EqualityComparer`1<",
				StringComparison.Ordinal))
			.ToArray();
		Assert.Contains(comparerMembers, member =>
			member.Member.TypeName.Contains("<float>", StringComparison.Ordinal));
		Assert.Contains(comparerMembers, member =>
			member.Member.TypeName.Contains("<double>", StringComparison.Ordinal));
		Assert.All(comparerMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Implemented,
			member.Status));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PublicStringEqualityComparerUsesManagedSingletonAndOrdinalKernels()
	{
		var analysis = Analyze("PublicStringEqualityComparerEntry");
		var comparerMembers = analysis.Members
			.Where(member => member.Member.TypeName.Contains(
				"EqualityComparer`1<string>",
				StringComparison.Ordinal))
			.ToArray();
		Assert.NotEmpty(comparerMembers);
		Assert.Contains(comparerMembers, member =>
			member.Member.Name == "GetHashCode");
		Assert.All(comparerMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Implemented,
			member.Status));
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PublicNullableIntEqualityComparerUsesManagedSingletonAndAggregateKernels()
	{
		var analysis = Analyze("PublicNullableIntEqualityComparerEntry");
		Assert.Contains(analysis.Members, member =>
			member.Member.TypeName.Contains(
				"EqualityComparer`1<System.Nullable<int>>",
				StringComparison.Ordinal) &&
			member.Member.Name == "GetHashCode" &&
			member.Status == M68kFrameworkCompatibilityStatus.Implemented);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PublicSealedReferenceComparersUseClosedWorldEqualityAndHashing()
	{
		foreach (var entry in new[]
		{
			"PublicSealedReferenceEqualityComparerEntry",
			"PublicSealedEquatableEqualityComparerEntry"
		})
		{
			var analysis = Analyze(entry);
			var comparerMembers = analysis.Members
				.Where(member => member.Member.TypeName.Contains(
					"EqualityComparer`1<",
					StringComparison.Ordinal))
				.ToArray();
			Assert.NotEmpty(comparerMembers);
			Assert.Contains(comparerMembers, member =>
				member.Member.Name == "GetHashCode");
			Assert.All(comparerMembers, member => Assert.Equal(
				M68kFrameworkCompatibilityStatus.Implemented,
				member.Status));
			Assert.True(analysis.IsCompatible);
		}
	}

	[Fact]
	public void NonSealedReferenceComparerRemainsExplicitlyDeferred()
	{
		var analysis = Analyze("UnsupportedNonSealedReferenceEqualityComparerEntry");
		var comparerCall = Assert.Single(
			analysis.Members,
			member => member.Member.TypeName.Contains(
				"EqualityComparer`1<",
				StringComparison.Ordinal) &&
				member.Member.Name == "get_Default");
		Assert.Equal(
			M68kFrameworkCompatibilityStatus.Unsupported,
			comparerCall.Status);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableConsoleStringOutputUsesExplicitAmigaPlatformBindings()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableConsoleWriteEntry"));
		var consoleMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.Console")
			.ToArray();

		Assert.Equal(2, consoleMembers.Length);
		Assert.Contains(consoleMembers, member =>
			member.Member.Name == "Write" &&
			member.Binding == "platform:amiga-console-write");
		Assert.Contains(consoleMembers, member =>
			member.Member.Name == "WriteLine" &&
			member.Binding == "platform:amiga-console-write-line");
		Assert.All(consoleMembers, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
			Assert.Contains("amiga-console", member.RequiredFeatures);
			Assert.Contains("managed-strings", member.RequiredFeatures);
			Assert.Contains("native-memory", member.RequiredFeatures);
		});
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableConsoleBindingRequiresAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableConsoleWriteEntry"));
		var consoleMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.Console")
			.ToArray();

		Assert.Equal(2, consoleMembers.Length);
		Assert.All(consoleMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Unsupported,
			member.Status));
		Assert.All(consoleMembers, member => Assert.Contains(
			"could not resolve an exact implementation",
			member.Reason ?? string.Empty,
			StringComparison.OrdinalIgnoreCase));
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableConsoleFixturePreservesHostNet10Semantics()
	{
		var previous = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			Assert.Equal(42, CompilerFixtures.PortableConsoleWriteEntry());
		}
		finally
		{
			Console.SetOut(previous);
		}

		Assert.Equal($"A\0B\u00e4{Environment.NewLine}{Environment.NewLine}", output.ToString());
	}

	[Fact]
	public void PortableConsoleIntegerOutputUsesExactAmigaPlatformBindings()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableConsolePrimitiveEntry"));
		var consoleMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.Console")
			.ToArray();

		Assert.Equal(5, consoleMembers.Length);
		foreach (var binding in new[]
		{
			"platform:amiga-console-write-int32",
			"platform:amiga-console-write-uint32",
			"platform:amiga-console-write-line-int32",
			"platform:amiga-console-write-line-uint32"
		})
		{
			var member = Assert.Single(consoleMembers, item => item.Binding == binding);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
			Assert.Contains("integer-formatting", member.RequiredFeatures);
			Assert.DoesNotContain("managed-strings", member.RequiredFeatures);
			Assert.Contains("native-memory", member.RequiredFeatures);
			Assert.DoesNotContain("MayCollect", member.Effects);
			Assert.DoesNotContain("ReadsManagedMemory", member.Effects);
			Assert.DoesNotContain("WritesManagedMemory", member.Effects);
			Assert.Contains("WritesNativeMemory", member.Effects);
		}
		Assert.Contains(consoleMembers, member =>
			member.Binding == "platform:amiga-console-write");
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableConsoleIntegerBindingsRequireAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableConsolePrimitiveEntry"));
		var consoleMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.Console")
			.ToArray();

		Assert.Equal(5, consoleMembers.Length);
		Assert.All(consoleMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Unsupported,
			member.Status));
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableConsoleIntegerFixturePreservesHostNet10Semantics()
	{
		var previous = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			Assert.Equal(42, CompilerFixtures.PortableConsolePrimitiveEntry());
		}
		finally
		{
			Console.SetOut(previous);
		}

		Assert.Equal(
			$"-2147483648|4294967295{Environment.NewLine}-42{Environment.NewLine}42",
			output.ToString());
	}

	[Fact]
	public void PortableConsoleInt64OutputUsesExactAllocationBoundedBindings()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableConsoleInt64Entry"));
		var consoleMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.Console")
			.ToArray();

		Assert.Equal(5, consoleMembers.Length);
		foreach (var binding in new[]
		{
			"platform:amiga-console-write-int64",
			"platform:amiga-console-write-uint64",
			"platform:amiga-console-write-line-int64",
			"platform:amiga-console-write-line-uint64"
		})
		{
			var member = Assert.Single(consoleMembers, item => item.Binding == binding);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
			Assert.Contains("integer-formatting", member.RequiredFeatures);
			Assert.DoesNotContain("managed-strings", member.RequiredFeatures);
			Assert.Contains("native-memory", member.RequiredFeatures);
			Assert.DoesNotContain("MayCollect", member.Effects);
			Assert.DoesNotContain("ReadsManagedMemory", member.Effects);
			Assert.DoesNotContain("WritesManagedMemory", member.Effects);
			Assert.Contains("WritesNativeMemory", member.Effects);
		}
		Assert.Contains(consoleMembers, member =>
			member.Binding == "platform:amiga-console-write");
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableConsoleInt64FixturePreservesHostNet10Semantics()
	{
		var previous = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			Assert.Equal(42, CompilerFixtures.PortableConsoleInt64Entry());
		}
		finally
		{
			Console.SetOut(previous);
		}

		Assert.Equal(
			$"-9223372036854775808|18446744073709551615{Environment.NewLine}" +
			$"-42{Environment.NewLine}42",
			output.ToString());
	}

	[Fact]
	public void PortableConsoleBooleanOutputUsesExactAmigaPlatformBindings()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableConsoleBooleanEntry"));
		var consoleMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.Console")
			.ToArray();

		Assert.Equal(2, consoleMembers.Length);
		foreach (var binding in new[]
		{
			"platform:amiga-console-write-boolean",
			"platform:amiga-console-write-line-boolean"
		})
		{
			var member = Assert.Single(consoleMembers, item => item.Binding == binding);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
			Assert.Contains("native-memory", member.RequiredFeatures);
			Assert.DoesNotContain("integer-formatting", member.RequiredFeatures);
			Assert.DoesNotContain("managed-strings", member.RequiredFeatures);
			Assert.DoesNotContain("MayAllocate", member.Effects);
			Assert.DoesNotContain("MayCollect", member.Effects);
			Assert.DoesNotContain("ReadsManagedMemory", member.Effects);
			Assert.DoesNotContain("WritesManagedMemory", member.Effects);
			Assert.Contains("WritesNativeMemory", member.Effects);
		}
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableConsoleCharacterOutputUsesExactAmigaPlatformBindings()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableConsoleCharacterEntry"));
		var consoleMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.Console")
			.ToArray();

		Assert.Equal(2, consoleMembers.Length);
		foreach (var binding in new[]
		{
			"platform:amiga-console-write-char",
			"platform:amiga-console-write-line-char"
		})
		{
			var member = Assert.Single(consoleMembers, item => item.Binding == binding);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
			Assert.Contains("native-memory", member.RequiredFeatures);
			Assert.DoesNotContain("integer-formatting", member.RequiredFeatures);
			Assert.DoesNotContain("managed-strings", member.RequiredFeatures);
			Assert.Contains("MayAllocate", member.Effects);
			Assert.DoesNotContain("MayCollect", member.Effects);
			Assert.DoesNotContain("ReadsManagedMemory", member.Effects);
			Assert.DoesNotContain("WritesManagedMemory", member.Effects);
			Assert.Contains("WritesNativeMemory", member.Effects);
		}
		Assert.True(analysis.IsCompatible);
	}

	[Theory]
	[InlineData("PortableConsoleBooleanEntry")]
	[InlineData("PortableConsoleCharacterEntry")]
	public void PortableConsoleBooleanAndCharacterBindingsRequireExplicitTargetPal(
		string entry)
	{
		var analysis = M68kCompiler.AnalyzeFramework(Request(entry));
		var consoleMembers = analysis.Members
			.Where(member => member.Member.TypeName == "System.Console")
			.ToArray();

		Assert.Equal(2, consoleMembers.Length);
		Assert.All(consoleMembers, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Unsupported,
			member.Status));
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableConsoleBooleanFixturePreservesHostNet10Semantics()
	{
		var previous = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			Assert.Equal(42, CompilerFixtures.PortableConsoleBooleanEntry());
		}
		finally
		{
			Console.SetOut(previous);
		}

		Assert.Equal(
			$"TrueFalse{Environment.NewLine}True{Environment.NewLine}False",
			output.ToString());
	}

	[Fact]
	public void PortableConsoleCharacterFixturePreservesHostNet10Semantics()
	{
		var previous = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			Assert.Equal(42, CompilerFixtures.PortableConsoleCharacterEntry());
		}
		finally
		{
			Console.SetOut(previous);
		}

		Assert.Equal($"\0\u00e4\u0100{Environment.NewLine}A", output.ToString());
	}

	[Theory]
	[InlineData(
		"PortableConsoleReadEntry",
		"Read",
		"platform:amiga-console-read",
		false)]
	[InlineData(
		"PortableConsoleReadLineEntry",
		"ReadLine",
		"platform:amiga-console-read-line",
		true)]
	public void PortableConsoleInputUsesExactAmigaPlatformBindings(
		string entry,
		string memberName,
		string binding,
		bool returnsManagedString)
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(Request(entry));
		var member = Assert.Single(
			analysis.Members,
			item => item.Member.TypeName == "System.Console");

		Assert.Equal(memberName, member.Member.Name);
		Assert.Equal(binding, member.Binding);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
		Assert.Contains("amiga-console", member.RequiredFeatures);
		Assert.Contains("amiga-console-input", member.RequiredFeatures);
		Assert.Contains("native-memory", member.RequiredFeatures);
		Assert.Contains("ReadsNativeMemory", member.Effects);
		Assert.Contains("WritesNativeMemory", member.Effects);
		Assert.Equal(
			returnsManagedString,
			member.RequiredFeatures.Contains("managed-strings"));
		Assert.Equal(
			returnsManagedString,
			member.RequiredFeatures.Contains("managed-arrays"));
		Assert.Equal(
			returnsManagedString,
			member.Effects.Contains("MayCollect"));
		Assert.True(analysis.IsCompatible);
	}

	[Theory]
	[InlineData("PortableConsoleReadEntry")]
	[InlineData("PortableConsoleReadLineEntry")]
	public void PortableConsoleInputRequiresAnExplicitTargetPal(string entry)
	{
		var analysis = M68kCompiler.AnalyzeFramework(Request(entry));
		var member = Assert.Single(
			analysis.Members,
			item => item.Member.TypeName == "System.Console");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, member.Status);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableConsoleInputFixturesPreserveHostNet10Semantics()
	{
		var previous = Console.In;
		try
		{
			Console.SetIn(new StringReader("A\0\u00e4"));
			Assert.Equal(42, CompilerFixtures.PortableConsoleReadEntry());

			Console.SetIn(new StringReader("A\0\u00e4\r\nB\n\rC"));
			Assert.Equal(42, CompilerFixtures.PortableConsoleReadLineEntry());
		}
		finally
		{
			Console.SetIn(previous);
		}
	}

	[Theory]
	[InlineData("UnsupportedConsoleInEntry", "get_In")]
	[InlineData("UnsupportedConsoleOutEntry", "get_Out")]
	[InlineData("UnsupportedConsoleErrorEntry", "get_Error")]
	[InlineData("UnsupportedConsoleInputEncodingEntry", "get_InputEncoding")]
	[InlineData("UnsupportedConsoleOutputEncodingEntry", "get_OutputEncoding")]
	public void ConsoleReaderWriterAndEncodingObjectsRemainExplicitlyDeferred(
		string entry,
		string getter)
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(Request(entry));
		var member = Assert.Single(
			analysis.Members,
			item => item.Member.TypeName == "System.Console" &&
				item.Member.Name == getter);

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, member.Status);
		Assert.Null(member.Binding);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableFileSystemExistenceUsesExactAmigaPlatformBindings()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableFileSystemExistsEntry"));
		var members = analysis.Members
			.Where(member => member.Member.TypeName is "System.IO.File" or "System.IO.Directory")
			.ToArray();

		Assert.Equal(2, members.Length);
		Assert.Contains(members, member =>
			member.Member.TypeName == "System.IO.File" &&
			member.Binding == "platform:amiga-file-exists");
		Assert.Contains(members, member =>
			member.Member.TypeName == "System.IO.Directory" &&
			member.Binding == "platform:amiga-directory-exists");
		Assert.All(members, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
			Assert.Contains("amiga-filesystem", member.RequiredFeatures);
			Assert.Contains("managed-strings", member.RequiredFeatures);
			Assert.Contains("native-cstrings", member.RequiredFeatures);
			Assert.Contains("native-memory", member.RequiredFeatures);
			Assert.Contains("ReadsNativeMemory", member.Effects);
			Assert.Contains("WritesNativeMemory", member.Effects);
		});
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableFileSystemExistenceRequiresAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableFileSystemExistsEntry"));
		var members = analysis.Members
			.Where(member => member.Member.TypeName is "System.IO.File" or "System.IO.Directory")
			.ToArray();

		Assert.Equal(2, members.Length);
		Assert.All(members, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Unsupported,
			member.Status));
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableFileSystemDeletionUsesExactAmigaPlatformBindings()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableFileSystemDeleteEntry"));
		var members = analysis.Members
			.Where(member => member.Member.TypeName is "System.IO.File" or "System.IO.Directory")
			.ToArray();

		Assert.Equal(2, members.Length);
		Assert.Contains(members, member =>
			member.Member.TypeName == "System.IO.File" &&
			member.Member.Name == "Delete" &&
			member.Binding == "platform:amiga-file-delete");
		Assert.Contains(members, member =>
			member.Member.TypeName == "System.IO.Directory" &&
			member.Member.Name == "Delete" &&
			member.Binding == "platform:amiga-directory-delete");
		Assert.All(members, member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
			Assert.Equal("System.Runtime", member.Member.AssemblyName);
			Assert.Contains("amiga-filesystem", member.RequiredFeatures);
			Assert.Contains("managed-exceptions", member.RequiredFeatures);
			Assert.Contains("MayThrow", member.Effects);
		});
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableFileSystemDeletionRequiresAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableFileSystemDeleteEntry"));
		var members = analysis.Members
			.Where(member => member.Member.TypeName is "System.IO.File" or "System.IO.Directory")
			.ToArray();

		Assert.Equal(2, members.Length);
		Assert.All(members, member => Assert.Equal(
			M68kFrameworkCompatibilityStatus.Unsupported,
			member.Status));
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableDirectoryMoveUsesExactAmigaPlatformBinding()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableDirectoryMoveEntry"));
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.IO.Directory" &&
				candidate.Member.Name == "Move");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
		Assert.Equal("System.Runtime", member.Member.AssemblyName);
		Assert.Equal("platform:amiga-directory-move", member.Binding);
		Assert.Contains("amiga-filesystem", member.RequiredFeatures);
		Assert.Contains("managed-strings", member.RequiredFeatures);
		Assert.Contains("native-cstrings", member.RequiredFeatures);
		Assert.Contains("native-memory", member.RequiredFeatures);
		Assert.Contains("managed-exceptions", member.RequiredFeatures);
		Assert.Contains("MayAllocate", member.Effects);
		Assert.Contains("MayThrow", member.Effects);
		Assert.Contains("ReadsNativeMemory", member.Effects);
		Assert.Contains("WritesNativeMemory", member.Effects);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableDirectoryMoveRequiresAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableDirectoryMoveEntry"));
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.IO.Directory" &&
				candidate.Member.Name == "Move");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, member.Status);
		Assert.Null(member.Binding);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableFileGetAttributesUsesExactAmigaPlatformBinding()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableFileGetAttributesEntry"));
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.IO.File" &&
				candidate.Member.Name == "GetAttributes");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
		Assert.Equal("System.Runtime", member.Member.AssemblyName);
		Assert.Equal("System.IO.FileAttributes", member.Member.ReturnType);
		Assert.Equal("platform:amiga-file-get-attributes", member.Binding);
		Assert.Contains("amiga-filesystem", member.RequiredFeatures);
		Assert.Contains("managed-strings", member.RequiredFeatures);
		Assert.Contains("native-cstrings", member.RequiredFeatures);
		Assert.Contains("native-memory", member.RequiredFeatures);
		Assert.Contains("managed-exceptions", member.RequiredFeatures);
		Assert.Contains("MayAllocate", member.Effects);
		Assert.Contains("MayThrow", member.Effects);
		Assert.Contains("ReadsNativeMemory", member.Effects);
		Assert.Contains("WritesNativeMemory", member.Effects);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableFileGetAttributesRequiresAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableFileGetAttributesEntry"));
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.IO.File" &&
				candidate.Member.Name == "GetAttributes");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, member.Status);
		Assert.Null(member.Binding);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableFileSetAttributesUsesExactAmigaPlatformBinding()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableFileSetAttributesEntry"));
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.IO.File" &&
				candidate.Member.Name == "SetAttributes");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
		Assert.Equal("System.Runtime", member.Member.AssemblyName);
		Assert.Equal("void", member.Member.ReturnType);
		Assert.Equal(
			["string", "System.IO.FileAttributes"],
			member.Member.ParameterTypes);
		Assert.Equal("platform:amiga-file-set-attributes", member.Binding);
		Assert.Contains("amiga-filesystem", member.RequiredFeatures);
		Assert.Contains("managed-strings", member.RequiredFeatures);
		Assert.Contains("native-cstrings", member.RequiredFeatures);
		Assert.Contains("native-memory", member.RequiredFeatures);
		Assert.Contains("managed-exceptions", member.RequiredFeatures);
		Assert.Contains("MayAllocate", member.Effects);
		Assert.Contains("MayThrow", member.Effects);
		Assert.Contains("ReadsNativeMemory", member.Effects);
		Assert.Contains("WritesNativeMemory", member.Effects);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableFileSetAttributesRequiresAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableFileSetAttributesEntry"));
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.IO.File" &&
				candidate.Member.Name == "SetAttributes");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, member.Status);
		Assert.Null(member.Binding);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableEnvironmentUsesExactAmigaPlatformBindings()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableEnvironmentEntry"));
		var members = analysis.Members
			.Where(candidate => candidate.Member.TypeName == "System.Environment")
			.ToDictionary(candidate => candidate.Member.Name);

		var newLine = members["get_NewLine"];
		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, newLine.Status);
		Assert.Equal("System.Runtime", newLine.Member.AssemblyName);
		Assert.Equal("string", newLine.Member.ReturnType);
		Assert.Equal("platform:amiga-environment-new-line", newLine.Binding);
		Assert.Contains("managed-strings", newLine.RequiredFeatures);
		Assert.Contains("amiga-environment", newLine.RequiredFeatures);
		Assert.Contains("ReadsManagedMemory", newLine.Effects);

		var processorCount = members["get_ProcessorCount"];
		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, processorCount.Status);
		Assert.Equal("System.Runtime", processorCount.Member.AssemblyName);
		Assert.Equal("int", processorCount.Member.ReturnType);
		Assert.Equal(
			"platform:amiga-environment-processor-count",
			processorCount.Binding);
		Assert.Equal(["amiga-environment"], processorCount.RequiredFeatures);
		Assert.Empty(processorCount.Effects);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableEnvironmentRequiresAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableEnvironmentEntry"));
		var members = analysis.Members
			.Where(candidate => candidate.Member.TypeName == "System.Environment")
			.ToArray();

		Assert.Equal(2, members.Length);
		Assert.All(
			members,
			member =>
			{
				Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, member.Status);
				Assert.Null(member.Binding);
			});
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void EnvironmentContractsPreserveHostNet10Invariants()
	{
		Assert.NotEmpty(Environment.NewLine);
		Assert.Equal('\n', Environment.NewLine[^1]);
		Assert.True(Environment.ProcessorCount >= 1);
	}

	[Fact]
	public void PortableStopwatchUsesExactAmigaPlatformBinding()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableStopwatchTimestampEntry"));
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.Diagnostics.Stopwatch" &&
				candidate.Member.Name == "GetTimestamp");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
		Assert.Equal("System.Runtime", member.Member.AssemblyName);
		Assert.Equal("long", member.Member.ReturnType);
		Assert.Empty(member.Member.ParameterTypes);
		Assert.Equal("platform:amiga-stopwatch-get-timestamp", member.Binding);
		Assert.Contains("native-memory", member.RequiredFeatures);
		Assert.Contains("amiga-interop", member.RequiredFeatures);
		Assert.Contains("amiga-clock", member.RequiredFeatures);
		Assert.Contains("managed-exceptions", member.RequiredFeatures);
		Assert.Contains("MayThrow", member.Effects);
		Assert.Contains("ReadsNativeMemory", member.Effects);
		Assert.Contains("WritesNativeMemory", member.Effects);
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableStopwatchRequiresAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableStopwatchTimestampEntry"));
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == "System.Diagnostics.Stopwatch" &&
				candidate.Member.Name == "GetTimestamp");

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, member.Status);
		Assert.Null(member.Binding);
		Assert.False(analysis.IsCompatible);
	}

	[Theory]
	[InlineData("PortableStopwatchFrequencyEntry")]
	[InlineData("PortableStopwatchHighResolutionEntry")]
	public void PortableStopwatchFieldsRequireAnExplicitTargetPal(string entry)
	{
		var exception = Assert.Throws<M68kCompilationException>(
			() => M68kCompiler.Compile(Request(entry)));

		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
	}

	[Fact]
	public void StopwatchContractsPreserveHostNet10Invariants()
	{
		var first = System.Diagnostics.Stopwatch.GetTimestamp();
		var second = System.Diagnostics.Stopwatch.GetTimestamp();
		Assert.True(System.Diagnostics.Stopwatch.Frequency > 0);
		Assert.True(System.Diagnostics.Stopwatch.IsHighResolution);
		Assert.True(second >= first);
	}

	[Fact]
	public void PortableStopwatchInstanceUsesExactAmigaPlatformBindings()
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request("PortableStopwatchInstanceEntry"));
		var members = analysis.Members
			.Where(candidate =>
				candidate.Member.TypeName == "System.Diagnostics.Stopwatch")
			.ToArray();
		var expected = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[".ctor"] = "platform:amiga-stopwatch-ctor",
			["Start"] = "platform:amiga-stopwatch-start",
			["Stop"] = "platform:amiga-stopwatch-stop",
			["Reset"] = "platform:amiga-stopwatch-reset",
			["Restart"] = "platform:amiga-stopwatch-restart",
			["StartNew"] = "platform:amiga-stopwatch-start-new",
			["get_IsRunning"] = "platform:amiga-stopwatch-is-running",
			["get_ElapsedTicks"] = "platform:amiga-stopwatch-elapsed-ticks"
		};

		foreach (var item in expected)
		{
			var member = Assert.Single(
				members,
				candidate => candidate.Member.Name == item.Key);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status);
			Assert.Equal(item.Value, member.Binding);
			Assert.Contains("managed-objects", member.RequiredFeatures);
		}
		Assert.True(analysis.IsCompatible);
	}

	[Fact]
	public void PortableStopwatchInstanceRequiresAnExplicitTargetPal()
	{
		var analysis = M68kCompiler.AnalyzeFramework(
			Request("PortableStopwatchInstanceEntry"));
		var members = analysis.Members
			.Where(candidate =>
				candidate.Member.TypeName == "System.Diagnostics.Stopwatch")
			.ToArray();

		Assert.NotEmpty(members);
		Assert.All(
			members,
			member =>
			{
				Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, member.Status);
				Assert.Null(member.Binding);
			});
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void StopwatchInstanceContractsPreserveHostNet10Invariants()
	{
		var stopwatch = new System.Diagnostics.Stopwatch();
		Assert.False(stopwatch.IsRunning);
		Assert.Equal(0, stopwatch.ElapsedTicks);
		stopwatch.Start();
		stopwatch.Start();
		Assert.True(stopwatch.IsRunning);
		stopwatch.Stop();
		stopwatch.Stop();
		Assert.False(stopwatch.IsRunning);
		Assert.True(stopwatch.ElapsedTicks >= 0);
		stopwatch.Restart();
		Assert.True(stopwatch.IsRunning);
		stopwatch.Reset();
		Assert.False(stopwatch.IsRunning);
		Assert.Equal(0, stopwatch.ElapsedTicks);
		var started = System.Diagnostics.Stopwatch.StartNew();
		Assert.True(started.IsRunning);
		started.Stop();
		Assert.False(started.IsRunning);
	}

	[Theory]
	[InlineData("UnsupportedFileMoveEntry", "System.IO.File", "Move")]
	[InlineData("UnsupportedDirectoryCreateEntry", "System.IO.Directory", "CreateDirectory")]
	public void AdjacentFileSystemApisRemainExplicitlyUnsupported(
		string entry,
		string typeName,
		string memberName)
	{
		var analysis = AmigaM68kCompiler.AnalyzeFramework(Request(entry));
		var member = Assert.Single(
			analysis.Members,
			candidate => candidate.Member.TypeName == typeName &&
				candidate.Member.Name == memberName);

		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, member.Status);
		Assert.Null(member.Binding);
		Assert.False(analysis.IsCompatible);
	}

	[Fact]
	public void PortableFileSystemExistenceFixturePreservesHostNet10Semantics()
	{
		const string fileName = ".coppersharp-portable-file";
		const string directoryName = ".coppersharp-portable-directory";
		Assert.False(File.Exists(fileName));
		Assert.False(Directory.Exists(directoryName));

		try
		{
			File.WriteAllBytes(fileName, [42]);
			Directory.CreateDirectory(directoryName);
			Assert.Equal(42, CompilerFixtures.PortableFileSystemExistsEntry());
		}
		finally
		{
			if (File.Exists(fileName))
			{
				File.Delete(fileName);
			}
			if (Directory.Exists(directoryName))
			{
				Directory.Delete(directoryName);
			}
		}
	}

	[Fact]
	public void PortableFileSystemDeletionFixturePreservesHostNet10Semantics()
	{
		const string fileName = ".coppersharp-portable-file";
		const string directoryName = ".coppersharp-portable-directory";
		Assert.False(File.Exists(fileName));
		Assert.False(Directory.Exists(directoryName));

		try
		{
			File.WriteAllBytes(fileName, [42]);
			Directory.CreateDirectory(directoryName);
			Assert.Equal(42, CompilerFixtures.PortableFileSystemDeleteEntry());
			Assert.False(File.Exists(fileName));
			Assert.False(Directory.Exists(directoryName));
			Assert.Throws<ArgumentNullException>(() => File.Delete(null!));
			Assert.Throws<ArgumentNullException>(() => Directory.Delete(null!));
			Assert.Throws<ArgumentException>(() => File.Delete(""));
			Assert.Throws<ArgumentException>(() => Directory.Delete("bad\0path"));
		}
		finally
		{
			if (File.Exists(fileName))
			{
				File.Delete(fileName);
			}
			if (Directory.Exists(directoryName))
			{
				Directory.Delete(directoryName);
			}
		}
	}

	[Fact]
	public void PortableDirectoryMoveFixturePreservesHostNet10Semantics()
	{
		const string directorySource = ".coppersharp-portable-directory-source";
		const string directoryDestination = ".coppersharp-portable-directory-destination";
		const string fileSource = ".coppersharp-portable-file-source";
		const string fileDestination = ".coppersharp-portable-file-destination";

		try
		{
			Directory.CreateDirectory(directorySource);
			File.WriteAllBytes(fileSource, [42]);
			Assert.Equal(42, CompilerFixtures.PortableDirectoryMoveEntry());
			Assert.False(Directory.Exists(directorySource));
			Assert.True(Directory.Exists(directoryDestination));
			Assert.False(File.Exists(fileSource));
			Assert.True(File.Exists(fileDestination));
			Assert.Throws<ArgumentNullException>(() => Directory.Move(null!, "destination"));
			Assert.Throws<ArgumentNullException>(() => Directory.Move("source", null!));
			Assert.Throws<ArgumentException>(() => Directory.Move("", "destination"));
			Assert.Throws<IOException>(() =>
				Directory.Move(directoryDestination, directoryDestination));
			Assert.Throws<DirectoryNotFoundException>(() =>
				Directory.Move(".coppersharp-portable-missing", "destination"));
		}
		finally
		{
			if (Directory.Exists(directorySource))
			{
				Directory.Delete(directorySource);
			}
			if (Directory.Exists(directoryDestination))
			{
				Directory.Delete(directoryDestination);
			}
			if (File.Exists(fileSource))
			{
				File.Delete(fileSource);
			}
			if (File.Exists(fileDestination))
			{
				File.Delete(fileDestination);
			}
		}
	}

	[Fact]
	public void PortableFileAttributesContractPreservesHostNet10Semantics()
	{
		var root = Path.Combine(
			Path.GetTempPath(),
			$"coppersharp-attributes-{Guid.NewGuid():N}");
		var file = Path.Combine(root, "file");
		var directory = Path.Combine(root, "directory");
		Directory.CreateDirectory(root);
		try
		{
			File.WriteAllBytes(file, [42]);
			Directory.CreateDirectory(directory);
			Assert.Equal(
				FileAttributes.Directory,
				File.GetAttributes(directory) & FileAttributes.Directory);
			Assert.Equal(
				FileAttributes.None,
				File.GetAttributes(file) & FileAttributes.Directory);
			File.SetAttributes(file, FileAttributes.ReadOnly | FileAttributes.Archive);
			Assert.Equal(
				FileAttributes.ReadOnly | FileAttributes.Archive,
				File.GetAttributes(file) &
					(FileAttributes.ReadOnly | FileAttributes.Archive));
			File.SetAttributes(file, FileAttributes.Normal);
			Assert.Equal(
				FileAttributes.None,
				File.GetAttributes(file) &
					(FileAttributes.ReadOnly | FileAttributes.Archive));
			Assert.Throws<ArgumentNullException>(() =>
				File.GetAttributes((string)null!));
			Assert.Throws<ArgumentException>(() => File.GetAttributes(""));
			Assert.Throws<ArgumentNullException>(() =>
				File.SetAttributes((string)null!, FileAttributes.Normal));
			Assert.Throws<ArgumentException>(() =>
				File.SetAttributes("", FileAttributes.Normal));
			Assert.Throws<FileNotFoundException>(() =>
				File.GetAttributes(Path.Combine(root, "missing")));
			Assert.Throws<FileNotFoundException>(() =>
				File.SetAttributes(
					Path.Combine(root, "missing"),
					FileAttributes.Normal));
			Assert.Throws<DirectoryNotFoundException>(() =>
				File.GetAttributes(Path.Combine(root, "missing-directory", "file")));
		}
		finally
		{
			if (File.Exists(file))
			{
				File.SetAttributes(file, FileAttributes.Normal);
			}
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
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
