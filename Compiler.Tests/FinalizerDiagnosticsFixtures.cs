/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Tests;

public static class FinalizerDiagnosticsFixtures
{
	public static uint DirectFinalizerEntry()
	{
		_ = new DirectFinalizableFixture();
		return 1;
	}

	public static uint InheritedFinalizerEntry()
	{
		_ = new DerivedFinalizableFixture();
		return 2;
	}

	public static uint UnreachableFinalizerEntry() => 3;

	private static object CreateUnreachableFinalizableObject() =>
		new DirectFinalizableFixture();
}

internal class DirectFinalizableFixture
{
	~DirectFinalizableFixture()
	{
	}
}

internal class BaseFinalizableFixture
{
	~BaseFinalizableFixture()
	{
	}
}

internal sealed class DerivedFinalizableFixture : BaseFinalizableFixture;
