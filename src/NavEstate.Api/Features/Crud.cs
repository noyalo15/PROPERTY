using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NavEstate.Api.Auth;
using NavEstate.Api.Data;

namespace NavEstate.Api.Features;

public static class Api
{
    public static string? In(string? value, string[] allowed, string field) =>
        value is not null && allowed.Contains(value) ? null : $"{field} must be one of: {string.Join(", ", allowed)}";

    public static string? First(params string?[] errors) => errors.FirstOrDefault(e => e is not null);

    public static BadRequestObjectResult Error(this ControllerBase c, string message) => c.BadRequest(new { error = message });

    /// <summary>Translates unique-key and foreign-key violations into 409 messages.</summary>
    public static async Task<IActionResult?> TrySaveAsync(this ControllerBase c, NavEstateDbContext db, CancellationToken ct = default)
    {
        try { await db.SaveChangesAsync(ct); return null; }
        catch (DbUpdateConcurrencyException) { return c.Conflict(new { error = "The record was changed by someone else. Reload and try again." }); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return c.Conflict(new { error = "A record with the same code or number already exists." });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            return c.Conflict(new { error = "The record is linked to other records or refers to a missing one." });
        }
    }
}

/// <summary>List/get/create/update/delete for small setup tables. Staff can read; writes need <see cref="WritePolicy"/>.</summary>
[ApiController, Authorize(Policy = Policies.Staff)]
public abstract class CrudController<T, TKey>(NavEstateDbContext db, IAuthorizationService auth) : ControllerBase
    where T : class where TKey : notnull
{
    protected NavEstateDbContext Db => db;
    protected virtual string WritePolicy => Policies.Setup;
    protected abstract IQueryable<T> Ordered(IQueryable<T> q);
    protected abstract TKey KeyOf(T e);
    protected abstract void SetKey(T e, TKey key);
    protected virtual string? Validate(T e) => null;

    [HttpGet]
    public virtual async Task<IActionResult> List(CancellationToken ct) => Ok(await Ordered(db.Set<T>().AsNoTracking()).ToListAsync(ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(TKey id, CancellationToken ct)
    {
        var e = await db.Set<T>().FindAsync([id], ct);
        return e is null ? NotFound() : Ok(e);
    }

    [HttpPost]
    public async Task<IActionResult> Create(T e, CancellationToken ct)
    {
        if (!(await auth.AuthorizeAsync(User, WritePolicy)).Succeeded) return Forbid();
        if (Validate(e) is { } err) return this.Error(err);
        db.Set<T>().Add(e);
        return await this.TrySaveAsync(db, ct) ?? Ok(e);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(TKey id, T incoming, CancellationToken ct)
    {
        if (!(await auth.AuthorizeAsync(User, WritePolicy)).Succeeded) return Forbid();
        var existing = await db.Set<T>().FindAsync([id], ct);
        if (existing is null) return NotFound();
        SetKey(incoming, id);
        if (Validate(incoming) is { } err) return this.Error(err);
        db.Entry(existing).CurrentValues.SetValues(incoming);
        return await this.TrySaveAsync(db, ct) ?? Ok(existing);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(TKey id, CancellationToken ct)
    {
        if (!(await auth.AuthorizeAsync(User, WritePolicy)).Succeeded) return Forbid();
        var existing = await db.Set<T>().FindAsync([id], ct);
        if (existing is null) return NotFound();
        db.Set<T>().Remove(existing);
        return await this.TrySaveAsync(db, ct) ?? NoContent();
    }
}
