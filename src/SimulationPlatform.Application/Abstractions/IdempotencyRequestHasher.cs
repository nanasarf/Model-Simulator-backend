using System.Security.Cryptography;
using System.Text;

namespace SimulationPlatform.Application.Abstractions;

public interface IIdempotencyRequestHasher
{
    byte[] Hash(string operation, Guid resourceId, long expectedVersion);
}

public sealed class IdempotencyRequestHasher : IIdempotencyRequestHasher
{
    public byte[] Hash(string operation, Guid resourceId, long expectedVersion) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{operation}\n{resourceId:D}\n{expectedVersion}"));
}
