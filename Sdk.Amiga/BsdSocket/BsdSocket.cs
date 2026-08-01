/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

// bsdsocket.library is supplied by the active TCP/IP stack. It is not a
// Kickstart library, so it must be opened explicitly after networking is
// available and its returned base must be assigned to BsdSocketLibraryBase.
[AmigaLibrary(Name, AmigaLibraryBasePolicy.Manual)]
public static class BsdSocket
{
	public const string Name = "bsdsocket.library";

	public static APTR BsdSocketLibraryBase
	{
		get => throw new System.NotSupportedException(
			"BsdSocketLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"BsdSocketLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Socket(
		[M68kRegister(M68kRegister.D0)] int domain,
		[M68kRegister(M68kRegister.D1)] int type,
		[M68kRegister(M68kRegister.D2)] int protocol);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Bind(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint name,
		[M68kRegister(M68kRegister.D1)] int nameLength);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Listen(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.D1)] int backlog);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Accept(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.A1)] uint addressLength);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Connect(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint name,
		[M68kRegister(M68kRegister.D1)] int nameLength);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SendTo(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.D2)] int flags,
		[M68kRegister(M68kRegister.A1)] uint address,
		[M68kRegister(M68kRegister.D3)] int addressLength);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Send(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.D2)] int flags);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RecvFrom(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.D2)] int flags,
		[M68kRegister(M68kRegister.A1)] uint address,
		[M68kRegister(M68kRegister.A2)] uint addressLength);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Recv(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.D2)] int flags);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Shutdown(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.D1)] int how);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetSockOpt(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.D1)] int level,
		[M68kRegister(M68kRegister.D2)] int optionName,
		[M68kRegister(M68kRegister.A0)] uint optionValue,
		[M68kRegister(M68kRegister.D3)] int optionLength);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetSockOpt(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.D1)] int level,
		[M68kRegister(M68kRegister.D2)] int optionName,
		[M68kRegister(M68kRegister.A0)] uint optionValue,
		[M68kRegister(M68kRegister.A1)] uint optionLength);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetSockName(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint name,
		[M68kRegister(M68kRegister.A1)] uint nameLength);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetPeerName(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint name,
		[M68kRegister(M68kRegister.A1)] uint nameLength);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IoctlSocket(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.D1)] int request,
		[M68kRegister(M68kRegister.A0)] uint argument);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CloseSocket(
		[M68kRegister(M68kRegister.D0)] int socket);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WaitSelect(
		[M68kRegister(M68kRegister.D0)] int descriptorCount,
		[M68kRegister(M68kRegister.A0)] uint readDescriptors,
		[M68kRegister(M68kRegister.A1)] uint writeDescriptors,
		[M68kRegister(M68kRegister.A2)] uint exceptionDescriptors,
		[M68kRegister(M68kRegister.A3)] uint timeout,
		[M68kRegister(M68kRegister.D1)] uint signals);

	[AmigaLvo(-132)]
	public static extern void SetSocketSignals(
		[M68kRegister(M68kRegister.D0)] uint interruptMask,
		[M68kRegister(M68kRegister.D1)] uint ioMask,
		[M68kRegister(M68kRegister.D2)] uint urgentMask);

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetDtablesize();

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ObtainSocket(
		[M68kRegister(M68kRegister.D0)] int id,
		[M68kRegister(M68kRegister.D1)] int domain,
		[M68kRegister(M68kRegister.D2)] int type,
		[M68kRegister(M68kRegister.D3)] int protocol);

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ReleaseSocket(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.D1)] int id);

	[AmigaLvo(-156)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ReleaseCopyOfSocket(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.D1)] int id);

	[AmigaLvo(-162)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Errno();

	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetErrnoPtr(
		[M68kRegister(M68kRegister.A0)] uint errnoPointer,
		[M68kRegister(M68kRegister.D0)] int size);

	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Inet_NtoA(
		[M68kRegister(M68kRegister.D0)] uint address);

	[AmigaLvo(-180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Inet_Addr(
		[M68kRegister(M68kRegister.A0)] CString text);

	[AmigaLvo(-186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Inet_LnaOf(
		[M68kRegister(M68kRegister.D0)] uint address);

	[AmigaLvo(-192)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Inet_NetOf(
		[M68kRegister(M68kRegister.D0)] uint address);

	[AmigaLvo(-198)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Inet_MakeAddr(
		[M68kRegister(M68kRegister.D0)] uint network,
		[M68kRegister(M68kRegister.D1)] uint localAddress);

	[AmigaLvo(-204)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Inet_Network(
		[M68kRegister(M68kRegister.A0)] CString text);

	[AmigaLvo(-210)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetHostByName(
		[M68kRegister(M68kRegister.A0)] CString name);

	[AmigaLvo(-216)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetHostByAddr(
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.D0)] int length,
		[M68kRegister(M68kRegister.D1)] int type);

	[AmigaLvo(-222)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetNetByName(
		[M68kRegister(M68kRegister.A0)] CString name);

	[AmigaLvo(-228)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetNetByAddr(
		[M68kRegister(M68kRegister.D0)] uint network,
		[M68kRegister(M68kRegister.D1)] int type);

	[AmigaLvo(-234)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetServByName(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] CString protocol);

	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetServByPort(
		[M68kRegister(M68kRegister.D0)] int port,
		[M68kRegister(M68kRegister.A0)] CString protocol);

	[AmigaLvo(-246)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetProtoByName(
		[M68kRegister(M68kRegister.A0)] CString name);

	[AmigaLvo(-252)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetProtoByNumber(
		[M68kRegister(M68kRegister.D0)] int protocol);

	[AmigaLvo(-258)]
	public static extern void Vsyslog(
		[M68kRegister(M68kRegister.D0)] int priority,
		[M68kRegister(M68kRegister.A0)] CString message,
		[M68kRegister(M68kRegister.A1)] uint arguments);

	[AmigaLvo(-264)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Dup2Socket(
		[M68kRegister(M68kRegister.D0)] int oldSocket,
		[M68kRegister(M68kRegister.D1)] int newSocket);

	[AmigaLvo(-270)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SendMsg(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.D1)] int flags);

	[AmigaLvo(-276)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RecvMsg(
		[M68kRegister(M68kRegister.D0)] int socket,
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.D1)] int flags);

	[AmigaLvo(-282)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetHostName(
		[M68kRegister(M68kRegister.A0)] uint name,
		[M68kRegister(M68kRegister.D0)] int nameLength);

	[AmigaLvo(-288)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetHostId();

	[AmigaLvo(-294)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SocketBaseTagList(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-300)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetSocketEvents(
		[M68kRegister(M68kRegister.A0)] uint eventPointer);

	[AmigaLvo(-366)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BpfOpen(
		[M68kRegister(M68kRegister.D0)] int channel);

	[AmigaLvo(-372)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BpfClose(
		[M68kRegister(M68kRegister.D0)] int channel);

	[AmigaLvo(-378)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BpfRead(
		[M68kRegister(M68kRegister.D0)] int channel,
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length);

	[AmigaLvo(-384)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BpfWrite(
		[M68kRegister(M68kRegister.D0)] int channel,
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length);

	[AmigaLvo(-390)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BpfSetNotifyMask(
		[M68kRegister(M68kRegister.D1)] int channel,
		[M68kRegister(M68kRegister.D0)] uint signalMask);

	[AmigaLvo(-396)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BpfSetInterruptMask(
		[M68kRegister(M68kRegister.D0)] int channel,
		[M68kRegister(M68kRegister.D1)] uint signalMask);

	[AmigaLvo(-402)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BpfIoctl(
		[M68kRegister(M68kRegister.D0)] int channel,
		[M68kRegister(M68kRegister.D1)] int command,
		[M68kRegister(M68kRegister.A0)] uint buffer);

	[AmigaLvo(-408)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BpfDataWaiting(
		[M68kRegister(M68kRegister.D0)] int channel);

	[AmigaLvo(-414)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddRouteTagList(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-420)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DeleteRouteTagList(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-432)]
	public static extern void FreeRouteInfo(
		[M68kRegister(M68kRegister.A0)] uint buffer);

	[AmigaLvo(-438)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetRouteInfo(
		[M68kRegister(M68kRegister.D0)] int addressFamily,
		[M68kRegister(M68kRegister.D1)] int flags);

	[AmigaLvo(-444)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddInterfaceTagList(
		[M68kRegister(M68kRegister.A0)] CString interfaceName,
		[M68kRegister(M68kRegister.A1)] CString deviceName,
		[M68kRegister(M68kRegister.D0)] int unit,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(-450)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ConfigureInterfaceTagList(
		[M68kRegister(M68kRegister.A0)] CString interfaceName,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-456)]
	public static extern void ReleaseInterfaceList(
		[M68kRegister(M68kRegister.A0)] uint list);

	[AmigaLvo(-462)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ObtainInterfaceList();

	[AmigaLvo(-468)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int QueryInterfaceTagList(
		[M68kRegister(M68kRegister.A0)] CString interfaceName,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-474)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateAddrAllocMessageA(
		[M68kRegister(M68kRegister.D0)] int version,
		[M68kRegister(M68kRegister.D1)] int protocol,
		[M68kRegister(M68kRegister.A0)] CString interfaceName,
		[M68kRegister(M68kRegister.A1)] uint resultPointer,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(-480)]
	public static extern void DeleteAddrAllocMessage(
		[M68kRegister(M68kRegister.A0)] uint message);

	[AmigaLvo(-486)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BeginInterfaceConfig(
		[M68kRegister(M68kRegister.A0)] uint message);

	[AmigaLvo(-492)]
	public static extern void AbortInterfaceConfig(
		[M68kRegister(M68kRegister.A0)] uint message);

	[AmigaLvo(-498)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddNetMonitorHookTagList(
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.A0)] uint hook,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-504)]
	public static extern void RemoveNetMonitorHook(
		[M68kRegister(M68kRegister.A0)] uint hook);

	[AmigaLvo(-510)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetNetworkStatistics(
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int version,
		[M68kRegister(M68kRegister.A0)] uint destination,
		[M68kRegister(M68kRegister.D2)] int size);

	[AmigaLvo(-516)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddDomainNameServer(
		[M68kRegister(M68kRegister.A0)] uint address);

	[AmigaLvo(-522)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RemoveDomainNameServer(
		[M68kRegister(M68kRegister.A0)] uint address);

	[AmigaLvo(-528)]
	public static extern void ReleaseDomainNameServerList(
		[M68kRegister(M68kRegister.A0)] uint list);

	[AmigaLvo(-534)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ObtainDomainNameServerList();

	[AmigaLvo(-540)]
	public static extern void SetNetEnt(
		[M68kRegister(M68kRegister.D0)] int stayOpen);

	[AmigaLvo(-546)]
	public static extern void EndNetEnt();

	[AmigaLvo(-552)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetNetEnt();

	[AmigaLvo(-558)]
	public static extern void SetProtoEnt(
		[M68kRegister(M68kRegister.D0)] int stayOpen);

	[AmigaLvo(-564)]
	public static extern void EndProtoEnt();

	[AmigaLvo(-570)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetProtoEnt();

	[AmigaLvo(-576)]
	public static extern void SetServEnt(
		[M68kRegister(M68kRegister.D0)] int stayOpen);

	[AmigaLvo(-582)]
	public static extern void EndServEnt();

	[AmigaLvo(-588)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetServEnt();

	[AmigaLvo(-594)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Inet_Aton(
		[M68kRegister(M68kRegister.A0)] CString text,
		[M68kRegister(M68kRegister.A1)] uint address);

	[AmigaLvo(-600)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Inet_Ntop(
		[M68kRegister(M68kRegister.D0)] int addressFamily,
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.A1)] uint destination,
		[M68kRegister(M68kRegister.D1)] int size);

	[AmigaLvo(-606)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Inet_Pton(
		[M68kRegister(M68kRegister.D0)] int addressFamily,
		[M68kRegister(M68kRegister.A0)] CString source,
		[M68kRegister(M68kRegister.A1)] uint destination);

	[AmigaLvo(-612)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int In_LocalAddr(
		[M68kRegister(M68kRegister.D0)] uint address);

	[AmigaLvo(-618)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int In_CanForward(
		[M68kRegister(M68kRegister.D0)] uint address);

	[AmigaLvo(-624)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MbufCopym(
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.D0)] int offset,
		[M68kRegister(M68kRegister.D1)] int length);

	[AmigaLvo(-630)]
	public static extern void MbufCopyback(
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.D0)] int offset,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.A1)] uint source);

	[AmigaLvo(-636)]
	public static extern void MbufCopydata(
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.D0)] int offset,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.A1)] uint destination);

	[AmigaLvo(-642)]
	public static extern void MbufFree(
		[M68kRegister(M68kRegister.A0)] uint message);

	[AmigaLvo(-648)]
	public static extern void MbufFreem(
		[M68kRegister(M68kRegister.A0)] uint message);

	[AmigaLvo(-654)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MbufGet();

	[AmigaLvo(-660)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MbufGethdr();

	[AmigaLvo(-666)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MbufPrepend(
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.D0)] int length);

	[AmigaLvo(-672)]
	public static extern void MbufCat(
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.A1)] uint nextMessage);

	[AmigaLvo(-678)]
	public static extern void MbufAdj(
		[M68kRegister(M68kRegister.A0)] uint messagePointer,
		[M68kRegister(M68kRegister.D0)] int requestedLength);

	[AmigaLvo(-684)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MbufPullup(
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.D0)] int length);

	[AmigaLvo(-690)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ProcessIsServer(
		[M68kRegister(M68kRegister.A0)] uint process);

	[AmigaLvo(-696)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ObtainServerSocket();
}
