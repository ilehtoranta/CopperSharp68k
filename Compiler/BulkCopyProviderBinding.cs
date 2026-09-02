/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler;

internal static class BulkCopyProviderBinding
{
	public static void ValidateOptions(M68kBulkCopyOptions? options)
	{
		if (options is null)
		{
			return;
		}

		if (options.MinimumBytes <= 0)
		{
			throw InvalidOptions("Bulk-copy minimum byte count must be positive.");
		}

		var hasManagedSelector = options.ManagedAssemblyName is not null ||
			options.ManagedMethod is not null;
		if (options.ExternalCall is { } external)
		{
			if (hasManagedSelector)
			{
				throw InvalidOptions(
					"Bulk-copy options must select either a managed provider or an external provider, not both.");
			}
			ValidateExternalCall(external);
			return;
		}

		if (string.IsNullOrWhiteSpace(options.ManagedAssemblyName) ||
			string.IsNullOrWhiteSpace(options.ManagedMethod))
		{
			throw InvalidOptions(
				"Bulk-copy options require both a managed assembly name and method selector, or an external call convention.");
		}
	}

	public static CilMethod? Resolve(
		CompilationModule module,
		M68kBulkCopyOptions? options)
	{
		if (options?.ManagedMethod is null)
		{
			return null;
		}

		var method = module.ResolveManagedMethod(
			options.ManagedAssemblyName!,
			options.ManagedMethod);
		if (method.IsImport || method.IsAbstract ||
			method.Signature.Header.IsInstance ||
			method.Signature.GenericParameterCount != 0 ||
			method.Construction.Length != 0 ||
			module.GetMethodDeclaringType(method).DisplayName.Contains('`') ||
			!method.Signature.ReturnType.IsVoid ||
			method.Signature.ParameterTypes.Length != 3 ||
			method.Signature.ParameterTypes.Any(type =>
				!IsProviderParameter(module, method, type)))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Bulk-copy provider must be a managed static nongeneric void method with three 32-bit integer or pointer parameters in source, destination, byte-count order.",
				method.DisplayName);
		}
		if (module.HasTypeInitializer(method))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.StaticAnalysis,
				"A bulk-copy provider must not require type initialization.",
				method.DisplayName);
		}
		return method;
	}

	private static bool IsProviderParameter(
		CompilationModule module,
		CilMethod method,
		CilType type)
	{
		if (type.IsReference || type.IsFloatingPoint ||
			type.Kind == CilTypeKind.GenericParameter)
		{
			return false;
		}
		if (type.IsSupportedScalar && type.Size == 4)
		{
			return true;
		}
		if (!module.IsTransparentScalarType(type))
		{
			return false;
		}
		if (module.TryGetStructLayout(type, method.ModuleName, out var layout))
		{
			return layout.Size == 4 && layout.ReferenceBitmap == 0;
		}
		// Metadata-only dependencies such as the SDK's APTR are admitted as
		// transparent scalar parameters without loading their CIL module. Use
		// the same reflection layout fallback as argument sizing; it admits only
		// reference-free fields. A known reference-bearing layout never reaches
		// this fallback.
		return module.GetStructSlotLongs(type) == 1;
	}

	private static void ValidateExternalCall(M68kExternalCallConvention convention)
	{
		if (string.IsNullOrWhiteSpace(convention.Identity) ||
			convention.Identity.IndexOfAny(['\r', '\n']) >= 0)
		{
			throw InvalidOptions("External bulk-copy provider requires a non-empty, single-line identity.");
		}
		if (convention.BaseSource is not M68kExternalBaseSource.CachedPointer and
			not M68kExternalBaseSource.WritableSlot and
			not M68kExternalBaseSource.Immediate)
		{
			throw InvalidOptions(
				"External bulk-copy provider requires a cached, writable-slot, or immediate base; a dynamic base argument is not supported.");
		}
		if (!IsAddressRegister(convention.BaseRegister) ||
			convention.CacheRegister is { } cache &&
				(!IsAddressRegister(cache) || cache == convention.BaseRegister))
		{
			throw InvalidOptions(
				"External bulk-copy base and cache registers must be distinct address registers A0-A6.");
		}
		if (convention.BaseSource == M68kExternalBaseSource.CachedPointer &&
			convention.CacheRegister is null)
		{
			throw InvalidOptions("External bulk-copy cached base requires a cache register.");
		}
		if (convention.BaseSource == M68kExternalBaseSource.CachedPointer &&
			convention.CacheRegister is { } cachedRegister &&
			(cachedRegister < M68kRegister.A2 ||
			 convention.ClobberedRegisters?.Contains(cachedRegister) == true))
		{
			// The cache is reserved across the whole function and is reread for
			// every call. Volatile or explicitly clobbered caches cannot survive
			// the first provider invocation, even with no live SSA value in them.
			throw InvalidOptions(
				"External bulk-copy cached base requires a preserved A2-A6 cache register that the provider does not clobber.");
		}
		if (convention.BaseSource == M68kExternalBaseSource.WritableSlot &&
			string.IsNullOrWhiteSpace(convention.SlotSymbol))
		{
			throw InvalidOptions("External bulk-copy writable base requires a slot symbol.");
		}
		if (convention.ParameterRegisters is not { Count: 3 } parameters ||
			parameters.Any(register => !IsRegister(register) ||
				register == convention.BaseRegister || register == convention.CacheRegister) ||
			parameters.Distinct().Count() != 3)
		{
			throw InvalidOptions(
				"External bulk-copy provider requires three distinct D0-D7/A0-A6 parameter registers, separate from its base and cache.");
		}
		if (convention.ExceptionPolicy != M68kExternalExceptionPolicy.None ||
			convention.ExceptionStatusRegister is not null)
		{
			throw InvalidOptions("External bulk-copy provider must not report exceptions.");
		}
		if (!IsRegister(convention.ReturnRegister) ||
			convention.ClobberedRegisters?.Any(register => !IsRegister(register)) == true)
		{
			throw InvalidOptions("External bulk-copy clobbers and return register must use D0-D7/A0-A6.");
		}
	}

	private static bool IsAddressRegister(M68kRegister register) =>
		register is >= M68kRegister.A0 and <= M68kRegister.A6;

	private static bool IsRegister(M68kRegister register) =>
		register is >= M68kRegister.D0 and <= M68kRegister.A6;

	private static M68kCompilationException InvalidOptions(string message) =>
		new(M68kDiagnosticIds.InvalidOutputOptions, message);
}
