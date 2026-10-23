/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Framework;

/// <summary>One admission boundary shared by codec resolution and closure analysis.</summary>
internal static class CoreLibCodecSurface
{
	public static bool TryCreateBinding(FrameworkMemberId referenced, FrameworkImplementationPackCatalog catalog, out FrameworkBinding binding)
	{
		binding = null!;
		return catalog is { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } &&
			(Utf8CodecSurface.TryCreateShadowBinding(referenced, out binding) ||
			 Base64CodecSurface.TryCreateShadowBinding(referenced, out binding) ||
			 HexCodecSurface.TryCreateShadowBinding(referenced, out binding));
	}

	internal static bool IsRuntimeStringFactory(FrameworkMemberId? caller)
	{
		if (caller is null || caller.AssemblyName != "CopperSharp.Runtime.Managed" ||
			caller.DeclaringType.FullMetadataName is not ("CopperSharp.Runtime.ShadowConvertHex" or "CopperSharp.Runtime.ShadowConvertBase64")) return false;
		return caller.Equals(new FrameworkMemberId(caller.DeclaringType, "CreateString",
			new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"),
					[FrameworkTypeId.Primitive("System.Char")])])));
	}

	internal static bool IsOptionsExceptionDependency(FrameworkMemberId referenced, FrameworkMemberId? caller)
	{
		var text = FrameworkTypeId.Primitive("System.String");
		var none = FrameworkTypeId.Primitive("System.Void");
		return caller?.Equals(new FrameworkMemberId(
			FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertBase64"), "CheckOptions",
			new FrameworkMethodSignatureId(0, 0, 1, none, [FrameworkTypeId.Named("System.Runtime", "System.Base64FormattingOptions")]))) == true &&
			FrameworkImplementationProfile.Canonicalize(referenced).Equals(new FrameworkMemberId(
				FrameworkTypeId.Named("System.Runtime", "System.ArgumentException"), ".ctor", new FrameworkMethodSignatureId(0x20, 0, 2, none, [text, text])));
	}

	internal static bool IsExceptionDependency(FrameworkMemberId referenced, FrameworkMemberId? caller)
	{
		if (IsOptionsExceptionDependency(referenced, caller)) return true;
		if (IsHexExceptionDependency(referenced, caller)) return true;
		if (caller?.AssemblyName != "CopperSharp.Runtime.Managed") return false;
		var none = FrameworkTypeId.Primitive("System.Void");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var text = FrameworkTypeId.Primitive("System.String");
		var owner = caller.DeclaringType.FullMetadataName;
		var publicOwner = owner == "CopperSharp.Runtime.ShadowBase64" ? "System.Buffers.Text.Base64" :
			owner == "CopperSharp.Runtime.ShadowConvertBase64" ? "System.Convert" : null;
		var publicCaller = publicOwner is not null && Base64CodecSurface.TryCreateShadowBinding(
			new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", publicOwner), caller.Name, caller.Signature, caller.MethodTypeArguments), out _);
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		if ((publicCaller || caller.Equals(new FrameworkMemberId(
			FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertBase64"), "CheckRange", new(0, 0, 3, none, [integer, integer, integer])))) &&
			(member.Equals(Constructor("System.ArgumentNullException", [text])) || member.Equals(Constructor("System.ArgumentOutOfRangeException", [text])))) return true;
		if (owner != "CopperSharp.Runtime.ShadowConvertBase64") return false;
		return caller.Equals(new(caller.DeclaringType, "Required", new(0, 0, 2, integer, [integer, FrameworkTypeId.Primitive("System.Boolean")]))) && member.Equals(Constructor("System.OutOfMemoryException", [])) ||
			caller.Equals(new(caller.DeclaringType, "FromChars", new(0, 0, 1, FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Byte")),
				[FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")])]))) && member.Equals(Constructor("System.FormatException", []));

		FrameworkMemberId Constructor(string type, FrameworkTypeId[] parameters) => new(FrameworkTypeId.Named("System.Runtime", type), ".ctor", new(0x20, 0, parameters.Length, none, parameters));
	}

	private static bool IsHexExceptionDependency(FrameworkMemberId referenced, FrameworkMemberId? caller)
	{
		var owner = FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertHex");
		if (caller is null || !caller.DeclaringType.Equals(owner)) return false;
		var text = FrameworkTypeId.Primitive("System.String");
		var none = FrameworkTypeId.Primitive("System.Void");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var boolean = FrameworkTypeId.Primitive("System.Boolean");
		var bytes = FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Byte"));
		var span = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Byte")]);
		var slice = caller.Equals(new(owner, "EncodeArraySlice", new(0, 0, 4, text, [bytes, integer, integer, boolean])));
		var publicCaller = HexCodecSurface.TryCreateShadowBinding(new(FrameworkTypeId.Named("System.Runtime", "System.Convert"), caller.Name, caller.Signature, caller.MethodTypeArguments), out _);
		var dependency = FrameworkImplementationProfile.Canonicalize(referenced);
		return (publicCaller || slice || caller.Equals(new(owner, "EncodeArray", new(0, 0, 2, text, [bytes, boolean])))) && dependency.Equals(Constructor("System.ArgumentNullException")) ||
			(slice || caller.Equals(new(owner, "Encode", new(0, 0, 2, text, [span, boolean])))) && dependency.Equals(Constructor("System.ArgumentOutOfRangeException"));

		FrameworkMemberId Constructor(string name) => new(FrameworkTypeId.Named("System.Runtime", name), ".ctor", new(0x20, 0, 1, none, [text]));
	}
}
