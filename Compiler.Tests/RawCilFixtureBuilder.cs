/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace CopperSharp.Compiler.Tests;

internal static class RawCilFixtureBuilder
{
	public static string CreateObjectReferenceEqualsAssembly(string directory)
	{
		var metadata = new MetadataBuilder();
		var ilStream = new BlobBuilder();
		var methodBodies = new MethodBodyStreamEncoder(ilStream);

		metadata.AddModule(
			0,
			metadata.GetOrAddString("CopperSharp.RawReferenceEquals.dll"),
			metadata.GetOrAddGuid(Guid.NewGuid()),
			default,
			default);
		metadata.AddAssembly(
			metadata.GetOrAddString("CopperSharp.RawReferenceEquals"),
			new Version(1, 0, 0, 0),
			default,
			default,
			default,
			AssemblyHashAlgorithm.None);
		var systemRuntime = metadata.AddAssemblyReference(
			metadata.GetOrAddString("System.Runtime"),
			new Version(10, 0, 0, 0),
			default,
			default,
			default,
			default);
		var objectType = metadata.AddTypeReference(
			systemRuntime,
			metadata.GetOrAddString("System"),
			metadata.GetOrAddString("Object"));

		var referenceEqualsSignature = new BlobBuilder();
		new BlobEncoder(referenceEqualsSignature)
			.MethodSignature()
			.Parameters(
				2,
				static returnType => returnType.Type().Boolean(),
				static parameters =>
				{
					parameters.AddParameter().Type().Object();
					parameters.AddParameter().Type().Object();
				});
		var referenceEquals = metadata.AddMemberReference(
			objectType,
			metadata.GetOrAddString("ReferenceEquals"),
			metadata.GetOrAddBlob(referenceEqualsSignature));

		var instructions = new InstructionEncoder(
			new BlobBuilder(),
			new ControlFlowBuilder());
		instructions.OpCode(ILOpCode.Ldnull);
		instructions.OpCode(ILOpCode.Ldnull);
		instructions.Call(referenceEquals);
		instructions.LoadString(metadata.GetOrAddUserString("identity"));
		instructions.OpCode(ILOpCode.Ldnull);
		instructions.Call(referenceEquals);
		instructions.LoadConstantI4(0);
		instructions.OpCode(ILOpCode.Ceq);
		instructions.OpCode(ILOpCode.And);
		instructions.OpCode(ILOpCode.Ret);
		var bodyOffset = methodBodies.AddMethodBody(instructions, maxStack: 3);

		var entrySignature = new BlobBuilder();
		new BlobEncoder(entrySignature)
			.MethodSignature()
			.Parameters(
				0,
				static returnType => returnType.Type().Int32(),
				static _ => { });
		metadata.AddMethodDefinition(
			MethodAttributes.Public |
				MethodAttributes.Static |
				MethodAttributes.HideBySig,
			MethodImplAttributes.IL | MethodImplAttributes.Managed,
			metadata.GetOrAddString("Entry"),
			metadata.GetOrAddBlob(entrySignature),
			bodyOffset,
			MetadataTokens.ParameterHandle(1));

		metadata.AddTypeDefinition(
			TypeAttributes.NotPublic,
			default,
			metadata.GetOrAddString("<Module>"),
			default,
			MetadataTokens.FieldDefinitionHandle(1),
			MetadataTokens.MethodDefinitionHandle(1));
		metadata.AddTypeDefinition(
			TypeAttributes.Public |
				TypeAttributes.Abstract |
				TypeAttributes.Sealed |
				TypeAttributes.BeforeFieldInit,
			default,
			metadata.GetOrAddString("RawReferenceEquals"),
			objectType,
			MetadataTokens.FieldDefinitionHandle(1),
			MetadataTokens.MethodDefinitionHandle(1));

		var peBuilder = new ManagedPEBuilder(
			new PEHeaderBuilder(
				imageCharacteristics:
					Characteristics.ExecutableImage |
					Characteristics.LargeAddressAware |
					Characteristics.Dll),
			new MetadataRootBuilder(metadata),
			ilStream,
			mappedFieldData: null,
			managedResources: null,
			nativeResources: null,
			debugDirectoryBuilder: null,
			strongNameSignatureSize: 0,
			entryPoint: default,
			flags: CorFlags.ILOnly,
			deterministicIdProvider: null);
		var image = new BlobBuilder();
		peBuilder.Serialize(image);

		var path = Path.Combine(
			directory,
			$"CopperSharp-reference-equals-{Guid.NewGuid():N}.dll");
		File.WriteAllBytes(path, image.ToArray());
		return path;
	}
}
