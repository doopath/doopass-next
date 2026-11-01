using Doopass.Core;
using Doopass.WebUi.Abstractions;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The host answers 409 for a duplicate pair, a busy store and an uninitialized
/// store. The mobile client used to turn all three into "this pair already
/// exists"; the body decides which exception the page should show.
/// </summary>
public sealed class VaultResponseErrorsTests
{
    [Theory]
    [InlineData("The vault is busy, try again.")]
    [InlineData("Store is busy: another writer holds the lock.")]
    public void Busy_vault_is_not_reported_as_a_duplicate_key(string message)
    {
        Assert.IsType<StoreBusyError>(VaultResponseErrors.Conflict(message));
    }

    [Theory]
    [InlineData("Store is not initialized. Please initialize it first.")]
    [InlineData("Store isn't initialized!")]
    public void Uninitialized_store_is_not_reported_as_a_duplicate_key(string message)
    {
        Assert.IsType<StoreIsNotInitializedError>(VaultResponseErrors.Conflict(message));
    }

    [Fact]
    public void Duplicate_pair_keeps_the_property_clash()
    {
        Assert.IsType<PropertyAlreadyExistError>(
            VaultResponseErrors.Conflict("This property (email) already exists!"));
    }
}
