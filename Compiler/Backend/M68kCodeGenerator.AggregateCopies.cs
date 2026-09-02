/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kBulkCopyStatistics(
	int ReturnCopies, long ReturnBytes,
	int LocalCopies, long LocalBytes,
	int ArgumentCopies, long ArgumentBytes,
	int UnclassifiedCopies, long UnclassifiedBytes,
	int ManagedProviders, int ExternalProviders)
{
	public static M68kBulkCopyStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
	public int TotalCopies => ReturnCopies + LocalCopies + ArgumentCopies + UnclassifiedCopies;
	public long TotalBytes => ReturnBytes + LocalBytes + ArgumentBytes + UnclassifiedBytes;
}

internal sealed partial class M68kCodeGenerator
{
	private readonly Dictionary<CilMethodIdentity, M68kAggregateReturnForwardingStatistics>
		_aggregateReturnForwardingStatistics = [];

	private void LowerAggregateCopies(
		IReadOnlyList<CilMethod> methods,
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
		M68kBulkCopyTarget? target)
	{
		var methodSummaries =
			_request.RomSizeOptimizations?.ForwardReadOnlyAggregateLocals == true &&
			_request.Cpu == M68kCpuTarget.M68000 &&
			_request.RuntimeProfile == M68kRuntimeProfile.Rom &&
			_request.ExceptionMode == M68kExceptionMode.Yolo &&
			_memoryManagement == M68kMemoryManagement.None &&
			_managedPoolRuntime is null && _managedLifecycles.Count == 0 &&
			!_usesExceptionRuntime
				? M68kMethodMemorySummaryAnalyzer.Compute(methods, functions, _module)
				: null;
		foreach (var (identity, function) in functions)
		{
			if (_bulkCopyProtectedMethods.Contains(identity)) continue;
			var statistics = M68kAggregateReturnForwarding.Run(
				function,
				_module,
				methodSummaries);
			if (statistics.Changed) _aggregateReturnForwardingStatistics[identity] = statistics;
		}
		if (target is not null) LowerBulkCopies(functions, target);
	}

	internal static M68kMachineModuleOptimizationStatistics WithAggregateCopyStatistics(
		M68kMachineModuleOptimizationStatistics statistics,
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
		IReadOnlyDictionary<CilMethodIdentity, M68kAggregateReturnForwardingStatistics> forwardingStatistics)
	{
		var forwarded = forwardingStatistics
			.Where(item => statistics.RetainedMethodIdentities.Contains(item.Key))
			.Select(static item => item.Value).ToArray();
		return statistics with
		{
			AggregateReturnForwarding = new(
				forwarded.Sum(static item => item.ReturnBuffersForwarded),
				forwarded.Sum(static item => item.LocalsForwarded),
				forwarded.Sum(static item => item.TemporaryHomesRemoved),
				forwarded.Sum(static item => item.TemporaryBytesRemoved)),
			BulkCopies = SummarizeBulkCopies(functions
				.Where(item => statistics.RetainedMethodIdentities.Contains(item.Key))
				.Select(static item => item.Value))
		};
	}

	// This reads the already-selected, explicit destination address. It neither
	// guesses from CIL opcodes nor changes selection, ABI staging, or call effects.
	// Byte totals describe static copy sites, not instruction bytes or execution
	// counts; forwarded temporary bytes above describe frame homes, not ROM size.
	private static M68kBulkCopyStatistics SummarizeBulkCopies(IEnumerable<M68kMachineFunction> functions)
	{
		var returns = 0;
		var locals = 0;
		var arguments = 0;
		var unclassified = 0;
		long returnBytes = 0, localBytes = 0, argumentBytes = 0, unclassifiedBytes = 0;
		var managedProviders = new HashSet<CilMethodIdentity>();
		var externalProviders = new HashSet<string>(StringComparer.Ordinal);
		foreach (var function in functions)
		{
			var instructions = function.Blocks.SelectMany(static block => block.Instructions).ToArray();
			var copies = instructions.Where(static instruction =>
				instruction.Operation == M68kMachineOperation.BulkCopy && instruction.BulkCopy is not null).ToArray();
			if (copies.Length == 0) continue;
			var definitions = instructions.SelectMany(instruction => instruction.Definitions
				.Select(value => (Value: value, Instruction: instruction)))
				.ToDictionary(static item => item.Value, static item => item.Instruction);
			foreach (var instruction in copies)
			{
				var copy = instruction.BulkCopy!;
				if (copy.Target.ManagedMethod is { } managed) managedProviders.Add(managed.Identity);
				if (copy.Target.ExternalCall is { } external) externalProviders.Add(external.Identity);
				M68kMachineOperation? addressOperation = null;
				if (instruction.Uses.Length >= 2)
				{
					var value = instruction.Uses[1];
					var visited = new HashSet<int>();
					while (visited.Add(value) && definitions.TryGetValue(value, out var definition))
					{
						if (definition.Operation == M68kMachineOperation.Copy && definition.Uses is [var source])
						{
							value = source;
							continue;
						}
						addressOperation = definition.Operation;
						break;
					}
				}
				switch (addressOperation)
				{
					case M68kMachineOperation.ReturnBufferAddress:
						returns++;
						returnBytes += copy.ByteCount;
						break;
					case M68kMachineOperation.LocalAddress:
						locals++;
						localBytes += copy.ByteCount;
						break;
					case M68kMachineOperation.OutgoingArgumentReserve:
						arguments++;
						argumentBytes += copy.ByteCount;
						break;
					default:
						unclassified++;
						unclassifiedBytes += copy.ByteCount;
						break;
				}
			}
		}
		return new(returns, returnBytes, locals, localBytes, arguments, argumentBytes,
			unclassified, unclassifiedBytes, managedProviders.Count, externalProviders.Count);
	}
}
