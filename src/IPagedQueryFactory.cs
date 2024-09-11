using System;
using System.Linq;

namespace nostify;

/// <summary>
/// Factory for creating PagedQuery objects
/// </summary>
public interface IPagedQueryFactory
{
    /// <summary>
    /// Create a PagedQuery from the query parameters in the request Uri
    /// </summary>
    IPagedQuery From(Uri requestUri);
}

/// <summary>
/// A PagedQueryFactory that does not support paging - it simply returns the query unchanged
/// </summary>
public class PagingNotImplemented : IPagedQueryFactory, IPagedQuery
{
    /// <inheritdoc/>
    public IPagedQuery From(Uri requestUri)
    {
        return this;
    }

    /// <inheritdoc/>
    public IQueryable<TAggregate> Apply<TAggregate>(IQueryable<TAggregate> query)
    {
        return query;
    }
}