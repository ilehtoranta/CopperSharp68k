/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Runtime.AmigaPal;

/// <summary>
/// Private target implementation for the admitted portable file-system probe
/// slice. Applications continue to compile against System.IO.File and
/// System.IO.Directory.
/// </summary>
public static class FileSystemPal
{
	private const uint MinimumDosVersion = 0;
	private const uint DeleteSuccess = 0;
	private const uint DeleteWrongKind = 0xffff_ffff;
	private const int ValidFileAttributesMask = 0x0002_fff7;
	private static uint _cachedDosBase;
	private static bool _applicationLifetimeActive;

	/// <summary>Enables lazy application-lifetime ownership for the DOS base.</summary>
	public static void Initialize()
	{
		_applicationLifetimeActive = true;
	}

	/// <summary>Releases a lazily acquired DOS base. Safe to call repeatedly.</summary>
	public static void Shutdown()
	{
		var dosBase = _cachedDosBase;
		_cachedDosBase = 0;
		_applicationLifetimeActive = false;
		if (dosBase != 0)
		{
			Exec.CloseLibrary(APTR.FromPointer(dosBase));
		}
	}

	public static bool FileExists(string? path) =>
		Exists(path, expectDirectory: false);

	public static bool DirectoryExists(string? path) =>
		Exists(path, expectDirectory: true);

	public static void DeleteFile(string? path) =>
		Delete(path, expectDirectory: false);

	public static void DeleteDirectory(string? path) =>
		Delete(path, expectDirectory: true);

	public static void MoveDirectory(string? sourcePath, string? destinationPath) =>
		Move(sourcePath, destinationPath);

	public static System.IO.FileAttributes GetFileAttributes(string? path)
	{
		ValidateMutationPath(path);
		var nativePath = new CStringBuffer(path!);
		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				nativePath.Dispose();
				M68kRuntime.ThrowIOException();
				return default;
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		var attributes = default(System.IO.FileAttributes);
		var error = DOS.Error.None;
		var lock_ = DOS.Lock(nativePath.Value, DOS.LockMode.Shared);
		if (!lock_.HasValue)
		{
			error = DOS.IoErr();
		}
		else
		{
			var fileInfoBlock = new FileInfoBlock();
			var fileInfoBlockAddress = APTR.ToUInt32(
				FileInfoBlock.AddressOf(ref fileInfoBlock));
			if (DOS.Examine(lock_.Value, fileInfoBlockAddress) == 0)
			{
				error = DOS.IoErr();
			}
			else
			{
				var address = APTR.FromPointer(fileInfoBlockAddress);
				attributes = MapFileAttributes(
					APTR.ReadUInt32(address, FileInfoBlock.DirEntryTypeOffset),
					APTR.ReadUInt32(address, FileInfoBlock.ProtectionOffset));
			}
			DOS.UnLock(lock_.Value);
		}

		DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
		if (!_applicationLifetimeActive)
		{
			Exec.CloseLibrary(APTR.FromPointer(dosBase));
		}
		nativePath.Dispose();
		return CompleteGetAttributes(attributes, error);
	}

	public static void SetFileAttributes(
		string? path,
		System.IO.FileAttributes attributes)
	{
		ValidateMutationPath(path);
		if (((int)attributes & ~ValidFileAttributesMask) != 0)
		{
			M68kRuntime.ThrowArgumentException();
			return;
		}

		var nativePath = new CStringBuffer(path!);
		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				nativePath.Dispose();
				M68kRuntime.ThrowIOException();
				return;
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		var error = DOS.Error.None;
		var currentProtection = 0u;
		var lock_ = DOS.Lock(nativePath.Value, DOS.LockMode.Shared);
		if (!lock_.HasValue)
		{
			error = DOS.IoErr();
		}
		else
		{
			var fileInfoBlock = new FileInfoBlock();
			var fileInfoBlockAddress = APTR.ToUInt32(
				FileInfoBlock.AddressOf(ref fileInfoBlock));
			if (DOS.Examine(lock_.Value, fileInfoBlockAddress) == 0)
			{
				error = DOS.IoErr();
			}
			else
			{
				currentProtection = APTR.ReadUInt32(
					APTR.FromPointer(fileInfoBlockAddress),
					FileInfoBlock.ProtectionOffset);
			}
			DOS.UnLock(lock_.Value);
		}

		if (error == DOS.Error.None)
		{
			var desiredProtection = MapFileProtection(currentProtection, attributes);
			if (desiredProtection != currentProtection &&
				DOS.SetProtection(nativePath.Value, (int)desiredProtection) == 0)
			{
				error = DOS.IoErr();
			}
		}

		DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
		if (!_applicationLifetimeActive)
		{
			Exec.CloseLibrary(APTR.FromPointer(dosBase));
		}
		nativePath.Dispose();
		CompleteSetAttributes(error);
	}

	private static bool Exists(string? path, bool expectDirectory)
	{
		if (!IsEncodablePath(path))
		{
			return false;
		}

		var nativePath = new CStringBuffer(path!);
		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				nativePath.Dispose();
				return false;
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		var exists = false;
		var lock_ = DOS.Lock(nativePath.Value, DOS.LockMode.Shared);
		if (lock_.HasValue)
		{
			var fileInfoBlock = new FileInfoBlock();
			var fileInfoBlockAddress = APTR.ToUInt32(
				FileInfoBlock.AddressOf(ref fileInfoBlock));
			if (DOS.Examine(lock_.Value, fileInfoBlockAddress) != 0)
			{
				var entryType = APTR.ReadUInt32(
					APTR.FromPointer(fileInfoBlockAddress),
					FileInfoBlock.DirEntryTypeOffset);
				if (expectDirectory)
				{
					switch (entryType)
					{
						case 1u:
						case 2u:
						case 3u:
						case 4u:
							exists = true;
							break;
					}
				}
				else
				{
					switch (entryType)
					{
						case 0xffff_fffdu:
						case 0xffff_fffcu:
						case 0xffff_fffbu:
							exists = true;
							break;
					}
				}
			}
			DOS.UnLock(lock_.Value);
		}

		DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
		if (!_applicationLifetimeActive)
		{
			Exec.CloseLibrary(APTR.FromPointer(dosBase));
		}
		nativePath.Dispose();
		return exists;
	}

	private static void Delete(string? path, bool expectDirectory)
	{
		ValidateMutationPath(path);
		var nativePath = new CStringBuffer(path!);
		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				nativePath.Dispose();
				M68kRuntime.ThrowIOException();
				return;
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		var outcome = DeleteNativePath(nativePath.Value, expectDirectory);
		DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
		if (!_applicationLifetimeActive)
		{
			Exec.CloseLibrary(APTR.FromPointer(dosBase));
		}
		nativePath.Dispose();
		CompleteDelete(outcome, expectDirectory);
	}

	private static void Move(string? sourcePath, string? destinationPath)
	{
		ValidateMutationPath(sourcePath);
		ValidateMutationPath(destinationPath);
		if (PathsEqual(sourcePath!, destinationPath!))
		{
			M68kRuntime.ThrowIOException();
			return;
		}

		var nativePaths = new CStringPairBuffer(sourcePath!, destinationPath!);
		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				nativePaths.Dispose();
				M68kRuntime.ThrowIOException();
				return;
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		var error = DOS.Error.None;
		if (DOS.Rename(nativePaths.Source, nativePaths.Destination) == 0)
		{
			error = DOS.IoErr();
		}
		DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
		if (!_applicationLifetimeActive)
		{
			Exec.CloseLibrary(APTR.FromPointer(dosBase));
		}
		nativePaths.Dispose();
		CompleteMove(error);
	}

	private static uint DeleteNativePath(CString nativePath, bool expectDirectory)
	{
		var outcome = ProbeDeleteKind(nativePath, expectDirectory);
		if (outcome != DeleteSuccess)
		{
			return NormalizeDeleteOutcome(outcome, expectDirectory);
		}
		if (DOS.DeleteFile(nativePath) != 0)
		{
			return DeleteSuccess;
		}
		return NormalizeDeleteOutcome((uint)DOS.IoErr(), expectDirectory);
	}

	private static uint ProbeDeleteKind(CString nativePath, bool expectDirectory)
	{
		var lock_ = DOS.Lock(nativePath, DOS.LockMode.Shared);
		if (!lock_.HasValue)
		{
			return (uint)DOS.IoErr();
		}

		var fileInfoBlock = new FileInfoBlock();
		var fileInfoBlockAddress = APTR.ToUInt32(
			FileInfoBlock.AddressOf(ref fileInfoBlock));
		var outcome = DeleteSuccess;
		if (DOS.Examine(lock_.Value, fileInfoBlockAddress) == 0)
		{
			outcome = (uint)DOS.IoErr();
		}
		else
		{
			var entryType = APTR.ReadUInt32(
				APTR.FromPointer(fileInfoBlockAddress),
				FileInfoBlock.DirEntryTypeOffset);
			if (expectDirectory)
			{
				if (!IsDirectoryEntry(entryType))
				{
					outcome = DeleteWrongKind;
				}
			}
			else if (!IsFileEntry(entryType))
			{
				outcome = DeleteWrongKind;
			}
		}
		DOS.UnLock(lock_.Value);
		return outcome;
	}

	private static uint NormalizeDeleteOutcome(uint outcome, bool expectDirectory)
	{
		if (!expectDirectory && outcome == (uint)DOS.Error.ObjectNotFound)
		{
			return DeleteSuccess;
		}
		return outcome;
	}

	private static void CompleteDelete(uint outcome, bool expectDirectory)
	{
		if (outcome == DeleteSuccess)
		{
			return;
		}
		if (outcome == DeleteWrongKind)
		{
			if (expectDirectory)
			{
				M68kRuntime.ThrowIOException();
				return;
			}
			M68kRuntime.ThrowUnauthorizedAccessException();
			return;
		}

		switch ((DOS.Error)outcome)
		{
			case DOS.Error.NoFreeStore:
				M68kRuntime.ThrowOutOfMemoryException();
				return;
			case DOS.Error.ObjectNotFound:
			case DOS.Error.DirectoryNotFound:
			case DOS.Error.NoDefaultDirectory:
			case DOS.Error.DeviceNotMounted:
				M68kRuntime.ThrowDirectoryNotFoundException();
				return;
			case DOS.Error.DeleteProtected:
			case DOS.Error.WriteProtected:
			case DOS.Error.ReadProtected:
			case DOS.Error.DiskWriteProtected:
				M68kRuntime.ThrowUnauthorizedAccessException();
				return;
			default:
				M68kRuntime.ThrowIOException();
				return;
		}
	}

	private static void CompleteMove(DOS.Error error)
	{
		switch (error)
		{
			case DOS.Error.None:
				return;
			case DOS.Error.NoFreeStore:
				M68kRuntime.ThrowOutOfMemoryException();
				return;
			case DOS.Error.ObjectNotFound:
			case DOS.Error.DirectoryNotFound:
			case DOS.Error.NoDefaultDirectory:
			case DOS.Error.DeviceNotMounted:
				M68kRuntime.ThrowDirectoryNotFoundException();
				return;
			case DOS.Error.DeleteProtected:
			case DOS.Error.WriteProtected:
			case DOS.Error.ReadProtected:
			case DOS.Error.DiskWriteProtected:
				M68kRuntime.ThrowUnauthorizedAccessException();
				return;
			default:
				M68kRuntime.ThrowIOException();
				return;
		}
	}

	private static System.IO.FileAttributes CompleteGetAttributes(
		System.IO.FileAttributes attributes,
		DOS.Error error)
	{
		switch (error)
		{
			case DOS.Error.None:
				return attributes;
			case DOS.Error.NoFreeStore:
				M68kRuntime.ThrowOutOfMemoryException();
				return default;
			case DOS.Error.ObjectNotFound:
				M68kRuntime.ThrowFileNotFoundException();
				return default;
			case DOS.Error.DirectoryNotFound:
			case DOS.Error.NoDefaultDirectory:
			case DOS.Error.DeviceNotMounted:
				M68kRuntime.ThrowDirectoryNotFoundException();
				return default;
			case DOS.Error.ReadProtected:
			case DOS.Error.WriteProtected:
			case DOS.Error.DeleteProtected:
			case DOS.Error.DiskWriteProtected:
				M68kRuntime.ThrowUnauthorizedAccessException();
				return default;
			default:
				M68kRuntime.ThrowIOException();
				return default;
		}
	}

	private static System.IO.FileAttributes MapFileAttributes(
		uint entryType,
		uint protection)
	{
		var attributes = default(System.IO.FileAttributes);
		if (IsDirectoryEntry(entryType))
		{
			attributes |= System.IO.FileAttributes.Directory;
		}
		if (entryType is 3u or 4u or 0xffff_fffcu)
		{
			attributes |= System.IO.FileAttributes.ReparsePoint;
		}
		if ((protection & (uint)FileProtection.Write) != 0)
		{
			attributes |= System.IO.FileAttributes.ReadOnly;
		}
		if ((protection & (uint)FileProtection.Archive) != 0)
		{
			attributes |= System.IO.FileAttributes.Archive;
		}
		return attributes == 0
			? System.IO.FileAttributes.Normal
			: attributes;
	}

	private static uint MapFileProtection(
		uint currentProtection,
		System.IO.FileAttributes attributes)
	{
		var protection = currentProtection;
		if ((attributes & System.IO.FileAttributes.ReadOnly) != 0)
		{
			protection |= (uint)FileProtection.Write;
		}
		else
		{
			protection &= ~(uint)FileProtection.Write;
		}
		if ((attributes & System.IO.FileAttributes.Archive) != 0)
		{
			protection |= (uint)FileProtection.Archive;
		}
		else
		{
			protection &= ~(uint)FileProtection.Archive;
		}
		return protection;
	}

	private static void CompleteSetAttributes(DOS.Error error)
	{
		switch (error)
		{
			case DOS.Error.None:
				return;
			case DOS.Error.NoFreeStore:
				M68kRuntime.ThrowOutOfMemoryException();
				return;
			case DOS.Error.ObjectNotFound:
				M68kRuntime.ThrowFileNotFoundException();
				return;
			case DOS.Error.DirectoryNotFound:
			case DOS.Error.NoDefaultDirectory:
			case DOS.Error.DeviceNotMounted:
				M68kRuntime.ThrowDirectoryNotFoundException();
				return;
			case DOS.Error.ReadProtected:
			case DOS.Error.WriteProtected:
			case DOS.Error.DeleteProtected:
			case DOS.Error.DiskWriteProtected:
				M68kRuntime.ThrowUnauthorizedAccessException();
				return;
			default:
				M68kRuntime.ThrowIOException();
				return;
		}
	}

	private static bool IsDirectoryEntry(uint entryType)
	{
		switch (entryType)
		{
			case 1u:
			case 2u:
			case 3u:
			case 4u:
				return true;
			default:
				return false;
		}
	}

	private static bool IsFileEntry(uint entryType)
	{
		switch (entryType)
		{
			case 0xffff_fffdu:
			case 0xffff_fffcu:
			case 0xffff_fffbu:
				return true;
			default:
				return false;
		}
	}

	private static void ValidateMutationPath(string? path)
	{
		if (path is null)
		{
			M68kRuntime.ThrowArgumentNullException();
			return;
		}
		if (path.Length == 0)
		{
			M68kRuntime.ThrowArgumentException();
			return;
		}
		for (var index = 0; index < path.Length; index++)
		{
			var character = path[index];
			if (character == '\0' || character > '\u00ff')
			{
				M68kRuntime.ThrowArgumentException();
				return;
			}
		}
	}

	private static bool PathsEqual(string left, string right)
	{
		if (left.Length != right.Length)
		{
			return false;
		}
		for (var index = 0; index < left.Length; index++)
		{
			if (left[index] != right[index])
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsEncodablePath(string? path)
	{
		if (path is null || path.Length == 0)
		{
			return false;
		}

		for (var index = 0; index < path.Length; index++)
		{
			var character = path[index];
			if (character == '\0' || character > '\u00ff')
			{
				return false;
			}
		}
		return true;
	}

	private ref struct CStringPairBuffer
	{
		private uint _pointer;
		private uint _byteSize;
		private uint _destinationOffset;

		public CStringPairBuffer(string source, string destination)
		{
			var sourceByteSize = RoundedCStringSize(source);
			var destinationByteSize = RoundedCStringSize(destination);
			_byteSize = sourceByteSize + destinationByteSize;
			_destinationOffset = sourceByteSize;
			_pointer = Exec.AllocMem(_byteSize, Exec.MemoryFlags.Public);
			if (_pointer == 0)
			{
				M68kRuntime.ThrowOutOfMemoryException();
				return;
			}
			WriteCString(source, _pointer, sourceByteSize);
			WriteCString(
				destination,
				_pointer + _destinationOffset,
				destinationByteSize);
		}

		public readonly CString Source => CString.FromPointer(_pointer);

		public readonly CString Destination =>
			CString.FromPointer(_pointer + _destinationOffset);

		public void Dispose()
		{
			if (_pointer == 0)
			{
				return;
			}
			Exec.FreeMem(_pointer, _byteSize);
			_pointer = 0;
			_byteSize = 0;
			_destinationOffset = 0;
		}

		private static uint RoundedCStringSize(string value)
		{
			var byteSize = (uint)value.Length + 1u;
			return (byteSize + 3u) & ~3u;
		}

		private static void WriteCString(string value, uint pointer, uint byteSize)
		{
			var address = APTR.FromPointer(pointer);
			var characterIndex = 0;
			for (var offset = 0; (uint)offset < byteSize; offset += 4)
			{
				uint packed = 0;
				for (var shift = 24; shift >= 0; shift -= 8)
				{
					if (characterIndex < value.Length)
					{
						packed |= (uint)value[characterIndex++] << shift;
					}
				}
				APTR.WriteUInt32(address, offset, packed);
			}
		}
	}

	[AmigaLibrary(Exec.Name, AmigaLibraryBasePolicy.ExecBase)]
	[AmigaLvo(-552)]
	[return: M68kRegister(M68kRegister.D0)]
	private static extern uint OpenDosLibrary(
		[M68kRegister(M68kRegister.A1)] CString name,
		[M68kRegister(M68kRegister.D0)] uint minimumVersion);
}
