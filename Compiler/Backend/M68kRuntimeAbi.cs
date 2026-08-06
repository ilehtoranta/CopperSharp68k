/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Defines the binary contract shared by compiler-emitted objects, type descriptors,
/// and private managed runtime helpers. Additive descriptor fields must be appended;
/// changing an existing offset requires incrementing <see cref="Version"/>.
/// </summary>
internal static class M68kRuntimeAbi
{
	public const uint Version = 1;

	public const short ObjectDescriptorOffset = 0;
	public const short ObjectSizeOffset = 4;
	public const short ArrayLengthOffset = 8;
	public const short ArrayDataOffset = 12;
	public const short StringLengthOffset = 8;
	public const short StringDataOffset = 12;

	public const short TypeSizeOffset = 0;
	public const short TypeReferenceBitmapOffset = 4;
	public const short TypeBaseOffset = 8;
	public const short TypeVirtualTableOffset = 12;
	public const short TypeInterfaceMapOffset = 16;
	public const int TypeDescriptorBytes = 20;

	public const short ArrayElementTypeOffset = 20;
	public const short ArrayElementKindOffset = 24;
	public const int ReferenceArrayDescriptorBytes = 28;
	public const uint ArrayElementKindClass = 0;
	public const uint ArrayElementKindInterface = 1;

	public const short DelegateTargetOffset = 8;
	public const short DelegateThunkOffset = 12;
	public const short DelegateInvocationListOffset = 16;
	public const short DelegateFlagsOffset = 20;
	public const int DelegateObjectBytes = 24;
	public const uint DelegateReferenceBitmap = 0b0101;
	public const uint DelegateFlagClosedInstance = 1;
	public const uint DelegateFlagMulticast = 2;
	public const int DelegateInvocationCountShift = 16;
	public const short DelegateInvocationTailOffset = 24;
	public const int DelegateMaximumInvocationCount = 28;
}
