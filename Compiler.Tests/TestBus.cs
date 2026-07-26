using Copper68k;

namespace CopperSharp.Compiler.Tests;

internal sealed class TestBus : IM68kBus
{
	private readonly Dictionary<uint, Action<M68kCpuState>> _gateways = new();

	public TestBus(int size = 0x0100_0000)
	{
		Memory = new byte[size];
	}

	public byte[] Memory { get; }

	public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind) =>
		Memory[CheckedOffset(address, 1)];

	public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind) =>
		ReadWord(address);

	public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind) =>
		ReadLong(address);

	public void WriteByte(
		uint address,
		byte value,
		ref long cycle,
		M68kBusAccessKind accessKind) =>
		Memory[CheckedOffset(address, 1)] = value;

	public void WriteWord(
		uint address,
		ushort value,
		ref long cycle,
		M68kBusAccessKind accessKind) =>
		WriteWord(address, value);

	public void WriteLong(
		uint address,
		uint value,
		ref long cycle,
		M68kBusAccessKind accessKind) =>
		WriteLong(address, value);

	public void ResetExternalDevices(long cycle)
	{
	}

	public bool HasHostGateway(uint address) => _gateways.ContainsKey(address);

	public bool TryInvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
	{
		if (!_gateways.TryGetValue(instructionProgramCounter, out var gateway))
		{
			return false;
		}

		gateway(state);
		return true;
	}

	public void RegisterGateway(uint address, Action<M68kCpuState> gateway)
	{
		_gateways.Add(address, gateway);
		WriteWord(address, 0xFF00);
		WriteLong(address + 2, 1);
	}

	public ushort ReadWord(uint address)
	{
		var offset = CheckedOffset(address, 2);
		return (ushort)((Memory[offset] << 8) | Memory[offset + 1]);
	}

	public uint ReadLong(uint address)
	{
		var offset = CheckedOffset(address, 4);
		return ((uint)Memory[offset] << 24) |
			((uint)Memory[offset + 1] << 16) |
			((uint)Memory[offset + 2] << 8) |
			Memory[offset + 3];
	}

	public void WriteWord(uint address, ushort value)
	{
		var offset = CheckedOffset(address, 2);
		Memory[offset] = (byte)(value >> 8);
		Memory[offset + 1] = (byte)value;
	}

	public void WriteLong(uint address, uint value)
	{
		var offset = CheckedOffset(address, 4);
		Memory[offset] = (byte)(value >> 24);
		Memory[offset + 1] = (byte)(value >> 16);
		Memory[offset + 2] = (byte)(value >> 8);
		Memory[offset + 3] = (byte)value;
	}

	private int CheckedOffset(uint address, int size)
	{
		if (address > int.MaxValue || address > Memory.Length - size)
		{
			throw new ArgumentOutOfRangeException(nameof(address), address, "Address is outside test memory.");
		}

		return (int)address;
	}
}
