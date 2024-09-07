using System.Linq;

namespace nostify;

/// <summary>
/// Interface for Paged Queries (including Filtering and Sorting)
/// </summary>
public interface IPagedQuery
{
    /// <summary>
    /// Applies filtering, sorting, and paging to the query
    /// </summary>
    IQueryable<TAggregate> Apply<TAggregate>(IQueryable<TAggregate> query);
}