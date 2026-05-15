using FundingPlatform.Application.Interfaces;

namespace FundingPlatform.Infrastructure.Persistence;

/// <summary>Spec 020 — <see cref="IUnitOfWork"/> backed by the scoped <see cref="AppDbContext"/>.</summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
