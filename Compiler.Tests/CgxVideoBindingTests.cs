/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Runtime.CompilerServices;
using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class CgxVideoBindingTests
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static APTR CallCreateVLayer() => CgxVideo.CreateVLayerHandleTagList(
		0x0000_4300u, 0x0000_4400u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void CallStackScopedLayer()
	{
		Span<TagItem> tags = stackalloc TagItem[2];
		tags[0] = CgxVideoTags.Item(CgxVideoTag.SourceType,
			CgxVideoSourceFormat.YCbCr16);
		tags[1] = TagItem.Done;

		if (CgxVideoLayerHandle.TryCreate(0x0000_4300u, ref tags[0], out var layer))
		{
			layer.Attach(0x0000_4400u, ref tags[0]);
			layer.GetAttribute(CgxVideoTag.Width);
			layer.Lock();
			layer.SetAttributes(ref tags[0]);
			layer.SwapBuffers();
			layer.WriteSPLine(0x0000_4500u, 0, 0, 16);
			CgxVideoLayerHandle.QueryAttribute(0x0000_4300u,
				CgxVideoQueryTag.MaximumWidth);
			layer.Dispose();
		}
	}

	[Fact]
	public void CgxVideoIsExplicitlyManual()
	{
		var attribute = typeof(CgxVideo).GetCustomAttribute<AmigaLibraryAttribute>();

		Assert.NotNull(attribute);
		Assert.Equal(CgxVideo.Name, attribute.Name);
		Assert.Equal(AmigaLibraryBasePolicy.Manual, attribute.BasePolicy);
		Assert.Equal("_CgxVideoLibraryBase", AmigaLibraryBaseSymbols.For(CgxVideo.Name));
	}

	public static IEnumerable<object[]> PublicVectors =>
	[
		Vector(nameof(CgxVideo.CreateVLayerHandleTagList), -30, [M68kRegister.A0, M68kRegister.A1], M68kRegister.D0),
		Vector(nameof(CgxVideo.DeleteVLayerHandle), -36, [M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(CgxVideo.AttachVLayerTagList), -42, [M68kRegister.A0, M68kRegister.A1, M68kRegister.A2], M68kRegister.D0),
		Vector(nameof(CgxVideo.DetachVLayer), -48, [M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(CgxVideo.GetVLayerAttr), -54, [M68kRegister.A0, M68kRegister.D0], M68kRegister.D0),
		Vector(nameof(CgxVideo.LockVLayer), -60, [M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(CgxVideo.UnlockVLayer), -66, [M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(CgxVideo.SetVLayerAttrTagList), -72, [M68kRegister.A0, M68kRegister.A1]),
		Vector(nameof(CgxVideo.SwapVLayerBuffer), -96, [M68kRegister.A0]),
		Vector(nameof(CgxVideo.WriteSPLine), -102, [M68kRegister.A0, M68kRegister.A1, M68kRegister.D0, M68kRegister.D1, M68kRegister.D2], M68kRegister.D0),
		Vector(nameof(CgxVideo.QueryVLayerAttr), -108, [M68kRegister.A0, M68kRegister.D0], M68kRegister.D0),
	];

	[Theory]
	[MemberData(nameof(PublicVectors))]
	public void CgxVideoVectorsUsePublishedM68kAbi(string methodName, int lvo,
		M68kRegister[] parameters, M68kRegister? result)
	{
		var method = typeof(CgxVideo).GetMethod(methodName,
			BindingFlags.Public | BindingFlags.Static)!;

		Assert.Equal(lvo, method.GetCustomAttribute<AmigaLvoAttribute>()?.Offset);
		Assert.Equal(parameters, method.GetParameters().Select(parameter =>
			parameter.GetCustomAttribute<M68kRegisterAttribute>()!.Register));
		Assert.Equal(result,
			method.ReturnParameter.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
	}

	[Fact]
	public void CgxVideoCallUsesTheManualBaseSlot()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(CgxVideoBindingTests).FullName}::CallCreateVLayer",
			OutputFormat = M68kOutputFormat.Assembly
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				[CgxVideo.Name] = 0x0000_4200
			}
		});

		Assert.Contains("_CgxVideoLibraryBase", result.Text, StringComparison.Ordinal);
		Assert.Contains("-30(a6)", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void CgxVideoConstantsMatchThePublicAbi()
	{
		Assert.Equal(41, CgxVideo.MinimumVersion);
		Assert.Equal(0x88000038u, (uint)CgxVideoTag.Modulo);
		Assert.Equal(0x88000070u, (uint)CgxVideoTag.ColorKeyFill);
		Assert.Equal(0x800A5000u, (uint)CgxVideoQueryTag.Dummy);
		Assert.Equal(1u << 8, (uint)CgxVideoFeature.SubPicture);
		Assert.Equal(4u, (uint)CgxVideoSourceFormat.YCbCr420);
	}

	[Fact]
	public void TypedCgxVideoTagItemsAreRepresentedCorrectly()
	{
		var source = CgxVideoTags.Item(CgxVideoTag.SourceType,
			CgxVideoSourceFormat.YCbCr420);
		var features = CgxVideoTags.Item(CgxVideoTag.Identifier,
			CgxVideoFeature.DoubleBuffer | CgxVideoFeature.Filtering);

		Assert.Equal((uint)CgxVideoTag.SourceType, source.Tag);
		Assert.Equal((uint)CgxVideoSourceFormat.YCbCr420, source.Data);
		Assert.Equal((uint)CgxVideoTag.Identifier, features.Tag);
		Assert.Equal((uint)(CgxVideoFeature.DoubleBuffer | CgxVideoFeature.Filtering),
			features.Data);
	}

	[Fact]
	public void StackScopedCgxVideoLayerLowersCleanupAndAllConvenienceVectors()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(CgxVideoBindingTests).FullName}::CallStackScopedLayer",
			OutputFormat = M68kOutputFormat.Assembly
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				[CgxVideo.Name] = 0x0000_4200
			}
		});

		foreach (var lvo in new[] { -30, -36, -42, -48, -54, -60, -66, -72, -96, -102, -108 })
		{
			Assert.Contains($"{lvo}(a6)", result.Text, StringComparison.Ordinal);
		}
	}

	private static object[] Vector(string methodName, int lvo,
		M68kRegister[] parameters, M68kRegister? result = null) =>
		[methodName, lvo, parameters, result!];
}
