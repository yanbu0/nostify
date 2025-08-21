using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Azure.Cosmos;

namespace nostify;

/// <summary>
/// Struct to hold the result of a Patch operation
/// </summary>
public readonly struct PatchItemResult
{
    private PatchItemResult(string id, PartitionKey partitionKey, HttpStatusCode statusCode, string exceptionMessage)
    {
        this.id = id;
        this.partitionKey = partitionKey;
        this.statusCode = statusCode;
        this.exceptionMessage = exceptionMessage;
    }

    /// <summary>
    /// The id of the item patched
    /// </summary>
    public readonly string id;

    /// <summary>
    /// The partition key of the item patched
    /// </summary>
    public readonly PartitionKey partitionKey;

    /// <summary>
    /// The status code of the patch operation
    /// </summary>
    public readonly HttpStatusCode statusCode;

    /// <summary>
    /// The exception message if an exception occurred
    /// </summary>
    public readonly string exceptionMessage;

    /// <summary>
    /// Returns true if the item was patched successfully
    /// </summary>
    public bool PatchedSuccessfully => statusCode == HttpStatusCode.OK;

    /// <summary>
    /// Returns true if the item was not found
    /// </summary>
    public bool NotFound => statusCode == HttpStatusCode.NotFound;

    /// <summary>
    /// Returns true if an exception occurred
    /// </summary>
    public bool IsException => !PatchedSuccessfully && !NotFound;

    /// <summary>
    /// Returns a successful result
    /// </summary>
    public static PatchItemResult SuccessResult(string id, PartitionKey partitionKey) => new PatchItemResult(id, partitionKey, HttpStatusCode.OK, string.Empty);

    /// <summary>
    /// Returns a not found result (nothing was patched)
    /// </summary>
    public static PatchItemResult NotFoundResult(string id, PartitionKey partitionKey) => new PatchItemResult(id, partitionKey, HttpStatusCode.NotFound, string.Empty);

    /// <summary>
    /// Returns a result from a cosmos exception
    /// </summary>
    public static PatchItemResult ExceptionResult(string id, PartitionKey partitionKey, CosmosException cosmosException) => new PatchItemResult(id, partitionKey, cosmosException.StatusCode, cosmosException.Message);

    /// <summary>
    /// Returns a result from an exception
    /// </summary>
    public static PatchItemResult ExceptionResult(string id, PartitionKey partitionKey, Exception exception) => new PatchItemResult(id, partitionKey, HttpStatusCode.InternalServerError, exception.Message);

    /// <summary>
    /// Returns a result from an invalid operation
    /// </summary>
    public static PatchItemResult InvalidOperationResult((string id, string partitionId, List<PatchOperation> operations) patch)
    {
        PartitionKey partitionKey = new PartitionKey(patch.partitionId);
        if (string.IsNullOrWhiteSpace(patch.id))
        {
            return new PatchItemResult(patch.id, partitionKey, HttpStatusCode.BadRequest, "id cannot be null or empty");
        }

        if (string.IsNullOrWhiteSpace(patch.partitionId))
        {
            return new PatchItemResult(patch.id, partitionKey, HttpStatusCode.BadRequest, "partitionId cannot be null or empty");
        }

        if (patch.operations == null || patch.operations.Count == 0)
        {
            return new PatchItemResult(patch.id, partitionKey, HttpStatusCode.BadRequest, "No patch operations to perform");
        }

        return new PatchItemResult(patch.id, partitionKey, HttpStatusCode.BadRequest, "Invalid patch operation (reason unknown)");
    }
}