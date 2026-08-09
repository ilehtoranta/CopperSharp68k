/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace ConsoleIOExample;

public static class Program
{
	[M68kEntryPoint]
	public static int Main()
	{
		Console.WriteLine("Console input/output example");
		Console.Write("Type text: ");
		var characterCount = 0;
		while (true)
		{
			var character = Console.Read();
			if (character < 0)
			{
				return 5;
			}
			if (character == '\n' || character == '\r')
			{
				break;
			}
			Console.Write((char)character);
			characterCount++;
		}
		Console.WriteLine("");
		Console.Write("Characters read: ");
		Console.WriteLine(characterCount);
		return 0;
	}
}
