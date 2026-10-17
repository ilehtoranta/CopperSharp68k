using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class ValueTypeReceiverRootTests
{
	[Theory]
	[InlineData(nameof(ValueTypeReceiverRootFixtures.StructEntry), false)]
	[InlineData(nameof(ValueTypeReceiverRootFixtures.GenericStructEntry), false)]
	[InlineData(nameof(ValueTypeReceiverRootFixtures.ExternalStructEntry), false)]
	[InlineData(nameof(ValueTypeReceiverRootFixtures.ClassEntry), true)]
	public void OnlyObjectReceiversBecomeObjectRoots(string entry, bool isObject)
	{
		using var module = new CompilationModule(typeof(ValueTypeReceiverRootFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(MultiModule.ExternalStructReceiverRootProbe).Assembly.Location]);
		var caller = module.ResolveEntryPoint($"CopperSharp.Compiler.Tests.ValueTypeReceiverRootFixtures::{entry}");
		var method = caller.Instructions.Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
			.Select(i => module.ResolveMethodToken((int)i.Operand!, caller, i.Offset).Definition)
			.Single(m => m?.Name == "ReadAfterCall")!;
		var function = CilMachineIrBuilder.Build(method, module);
		var argument = Assert.Single(function.Blocks.SelectMany(b => b.Instructions),
			i => i.Operation == M68kMachineOperation.Argument && i.ArgumentIndex == 0);
		var receiver = function.Values[Assert.Single(argument.Definitions)];
		Assert.Equal(isObject ? CilStackValueKind.Reference : CilStackValueKind.ManagedPointer, receiver.Kind);
		Assert.Equal(isObject, receiver.IsGcReference);
		var live = M68kLivenessAnalysis.AnalyzeInstructions(function, M68kLivenessAnalysis.Analyze(function));
		var plan = M68kSafepointPlanner.Create(function, live);
		Assert.NotEmpty(plan.Safepoints);
		Assert.Equal(isObject, plan.RootSlotByValue.ContainsKey(receiver.Id));
	}
}

public static class ValueTypeReceiverRootFixtures
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CallBoundary(int value) { }
	public static int StructEntry() { var value = new StructReceiverRootProbe { Value = 42, Other = 7 }; return value.ReadAfterCall(); }
	public static int GenericStructEntry() { var value = new GenericStructReceiverRootProbe<int> { Value = 42, Other = 7 }; return value.ReadAfterCall(); }
	public static int ExternalStructEntry() { var value = new MultiModule.ExternalStructReceiverRootProbe { Value = 42, Other = 7 }; return value.ReadAfterCall(); }
	public static int ClassEntry() => new ClassReceiverRootProbe { Value = 42, Other = 7 }.ReadAfterCall();
}

public struct StructReceiverRootProbe
{
	public int Value;
	public int Other;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public int ReadAfterCall() { ValueTypeReceiverRootFixtures.CallBoundary(Other); return Value; }
}

public struct GenericStructReceiverRootProbe<T>
{
	public int Value;
	public T Other;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public int ReadAfterCall() { ValueTypeReceiverRootFixtures.CallBoundary(Value); return Value; }
}

public sealed class ClassReceiverRootProbe
{
	public int Value;
	public int Other;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public int ReadAfterCall() { ValueTypeReceiverRootFixtures.CallBoundary(Other); return Value; }
}
