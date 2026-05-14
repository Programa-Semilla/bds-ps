using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Domain.Interfaces;

public interface ISystemConfigurationRepository
{
    Task<SystemConfiguration?> GetByKeyAsync(string key);
    Task<SystemConfiguration?> GetByIdAsync(int id);
    Task<List<SystemConfiguration>> GetAllAsync();
    Task UpdateAsync(SystemConfiguration configuration);

    /// <summary>
    /// Spec 021 / US7 / T145 — inserts a new <see cref="SystemConfiguration"/>
    /// row. The legacy <see cref="UpdateAsync"/> path only attaches an existing
    /// tracked entity; the spec-021 admin upload surface creates pointer rows
    /// lazily, so insert is now a first-class operation.
    /// </summary>
    Task AddAsync(SystemConfiguration configuration);

    Task SaveChangesAsync();
}
