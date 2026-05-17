using System;
using System.Threading.Tasks;
using PolyType;
using StreamJsonRpc;

namespace VirtualMouse.Hosting;

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
internal partial interface IVirtualMouseServerApi
{
    Task<Guid> ConnectAsync(int processId);

    Task AckAsync();

    Task<ServerStatus> GetStatusAsync();
}
