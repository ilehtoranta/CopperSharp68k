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

public sealed class CoreDeviceBindingTests
{
	[Fact]
	public void InputDeviceLifecycleUsesTheStandardDeviceVectorSlots()
	{
		Assert.Equal(-6, InputDevice.Open);
		Assert.Equal(-12, InputDevice.Close);
		Assert.Equal(-30, InputDevice.BeginIO);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallCmpTime() =>
		TimerDevice.CmpTime(0x0000_4200u, 0x0000_4300u, 0x0000_4400u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ushort CallPeekQualifier() =>
		InputDevice.PeekQualifier(0x0000_4200u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallConvertRawKey() =>
		ConsoleDevice.ConvertRawKey(0x0000_4200u, 0x0000_4300u, 0x0000_4400u,
			32, 0x0000_4500u);

	[Theory]
	[InlineData(typeof(TimerDevice), TimerDevice.Name)]
	[InlineData(typeof(InputDevice), InputDevice.Name)]
	[InlineData(typeof(ConsoleDevice), ConsoleDevice.Name)]
	public void CoreDeviceBindingsReceiveTheirBaseFromTheCaller(
		Type deviceType, string name)
	{
		var attribute = deviceType.GetCustomAttribute<AmigaLibraryAttribute>();

		Assert.NotNull(attribute);
		Assert.Equal(name, attribute.Name);
		Assert.Equal(AmigaLibraryBasePolicy.CallerProvided, attribute.BasePolicy);
	}

	[Theory]
	[InlineData(typeof(TimerDevice), nameof(TimerDevice.AddTime), -42,
		new[] { M68kRegister.A6, M68kRegister.A0, M68kRegister.A1 }, null)]
	[InlineData(typeof(TimerDevice), nameof(TimerDevice.SubTime), -48,
		new[] { M68kRegister.A6, M68kRegister.A0, M68kRegister.A1 }, null)]
	[InlineData(typeof(TimerDevice), nameof(TimerDevice.CmpTime), -54,
		new[] { M68kRegister.A6, M68kRegister.A0, M68kRegister.A1 }, M68kRegister.D0)]
	[InlineData(typeof(TimerDevice), nameof(TimerDevice.GetSysTime), -66,
		new[] { M68kRegister.A6, M68kRegister.A0 }, null)]
	[InlineData(typeof(InputDevice), nameof(InputDevice.PeekQualifier), -42,
		new[] { M68kRegister.A6 }, M68kRegister.D0)]
	[InlineData(typeof(ConsoleDevice), nameof(ConsoleDevice.ConvertRawKey), -48,
		new[] { M68kRegister.A6, M68kRegister.A0, M68kRegister.A1,
			M68kRegister.D1, M68kRegister.A2 }, M68kRegister.D0)]
	public void CoreDeviceVectorsUsePublishedM68kAbi(
		Type deviceType, string methodName, int lvo,
		M68kRegister[] parameters, M68kRegister? result)
	{
		var method = deviceType.GetMethod(methodName,
			BindingFlags.Public | BindingFlags.Static)!;

		Assert.Equal(lvo, method.GetCustomAttribute<AmigaLvoAttribute>()?.Offset);
		Assert.Equal(parameters, method.GetParameters().Select(parameter =>
			parameter.GetCustomAttribute<M68kRegisterAttribute>()!.Register));
		Assert.Equal(result,
			method.ReturnParameter.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
	}

	[Theory]
	[InlineData(nameof(CallCmpTime), "-54(a6)")]
	[InlineData(nameof(CallPeekQualifier), "-42(a6)")]
	[InlineData(nameof(CallConvertRawKey), "-48(a6)")]
	public void CoreDeviceCallsLowerThroughTheirCallerProvidedBase(
		string methodName, string vector)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(CoreDeviceBindingTests).FullName}::{methodName}",
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Contains(vector, result.Text, StringComparison.Ordinal);
	}
}
