namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly HashSet<int> _allocatedIncomingArgumentHomes = new();

	// The caller has already made an independent by-value copy in the incoming
	// ABI slot. Reuse that callee-owned storage without resizing an emitted frame.
	private M68kAllocatedFunction ReuseIncomingArgumentHomes(
		InternalCallAbi abi, M68kAllocatedFunction allocated)
	{
		_allocatedIncomingArgumentHomes.Clear();
		var function = allocated.Function;
		if (_request.RomSizeOptimizations?.ReuseIncomingArgumentHomes != true ||
			_request.Cpu != M68kCpuTarget.M68000 ||
			_request.RuntimeProfile != M68kRuntimeProfile.Rom ||
			_request.ExceptionMode != M68kExceptionMode.Yolo ||
			_memoryManagement != M68kMemoryManagement.None ||
			_managedPoolRuntime is not null || _managedLifecycles.Count != 0 || _usesExceptionRuntime ||
			function.HasExceptionHandlers || function.ExceptionRegions.Count != 0 ||
			function.HasDynamicStackAllocation || function.Values.Values.Any(value => value.IsGcReference) ||
			function.LocalHomes.Values.Any(home => home.HasGcReferences) ||
			function.ArgumentHomes.Values.Any(home => home.HasGcReferences))
		{
			return allocated;
		}

		var offsets = allocated.Frame.ArgumentHomeOffsets.ToDictionary();
		var savedBytes = checked(allocated.Frame.FrameBytes + allocated.Frame.CalleeSavedRegisters.Count * 4);
		foreach (var home in function.ArgumentHomes.Values.OrderBy(home => home.Index))
		{
			if (home.Size <= 4 || home.Index < 0 || home.Index >= abi.Arguments.Length) continue;
			var incoming = abi.Arguments[home.Index];
			if (!incoming.IsStack || incoming.LowRegister is not null ||
				incoming.SlotLongs * 4 != home.Size || incoming.IsGcReference) continue;
			var offset = checked(savedBytes + 4 + incoming.StackOffset);
			if (offset < 0 || (long)offset + home.Size > short.MaxValue) continue;
			offsets[home.Index] = offset;
			_allocatedIncomingArgumentHomes.Add(home.Index);
		}
		return _allocatedIncomingArgumentHomes.Count == 0 ? allocated : allocated with
		{
			Frame = allocated.Frame with { ArgumentHomeOffsets = offsets }
		};
	}
}
