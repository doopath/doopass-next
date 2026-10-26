using System.Text.Json;
using Doopass.Cli.Api;
using Doopass.Core;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The default error branch used to echo ex.Message, which can contain .NET type
/// names and file-system paths. Only our own, deliberate exceptions carry text.
/// </summary>
public sealed class ErrorMappingTests
{
    [Fact]
    public void Unexpected_exception_becomes_a_generic_500()
    {
        var (status, body) = Describe(VaultEndpoints.MapStoreError(
            new InvalidOperationException("Cannot open '/home/user/doopass/appdata/store.enc': System.IO.IOException")));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Contains("Internal server error.", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("System.", body);
        Assert.DoesNotContain("store.enc", body);
    }

    [Theory]
    [InlineData(typeof(PropertyDoesNotExistError))]
    [InlineData(typeof(PropertyAlreadyExistError))]
    [InlineData(typeof(IncorrectPasswordError))]
    [InlineData(typeof(StoreIsNotInitializedError))]
    [InlineData(typeof(StoreCorruptedError))]
    [InlineData(typeof(ReadOnlySettingError))]
    [InlineData(typeof(InvalidSettingValueError))]
    public void Planned_exceptions_keep_their_message(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "own message")!;

        var (status, body) = Describe(VaultEndpoints.MapStoreError(exception));

        Assert.NotEqual(StatusCodes.Status500InternalServerError, status);
        Assert.Contains("own message", body);
    }

    [Fact]
    public void Busy_store_is_a_409_with_a_fixed_message()
    {
        var (status, body) = Describe(VaultEndpoints.MapStoreError(new StoreBusyError("lock: /tmp/x.lock")));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("The vault is busy, try again.", body);
        Assert.DoesNotContain("/tmp/x.lock", body);
    }

    /// <summary>Reads the status and JSON body out of the anonymous result object.</summary>
    private static (int Status, string Body) Describe(IResult result)
    {
        var type = result.GetType();
        var status = (int)type.GetProperty("StatusCode")!.GetValue(result)!;
        var value = type.GetProperty("Value")!.GetValue(result);
        return (status, JsonSerializer.Serialize(value));
    }
}
