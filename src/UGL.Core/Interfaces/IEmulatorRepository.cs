using UGL.Core.Models;

namespace UGL.Core.Interfaces;

public interface IEmulatorRepository
{
    Task<IReadOnlyList<Emulator>> GetAllAsync(CancellationToken ct = default);
    Task<Emulator?> GetByIdAsync(string id, CancellationToken ct = default);
    Task AddOrUpdateAsync(Emulator emulator, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
