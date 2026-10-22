using Doopass.Core.Validation;
using Xunit;

namespace Doopass.Core.Tests;

public class PasswordValidatorTests
{
    [Theory]
    [InlineData("")]
    public void EmptyPassword_IsTooSmall(string password)
    {
        var (valid, message) = new PasswordValidator(password).Validate();

        Assert.False(valid);
        Assert.Equal("Password is too small!", message);
    }

    [Fact]
    public void LeadingSpace_IsRejected()
    {
        var (valid, message) = new PasswordValidator(" secret").Validate();

        Assert.False(valid);
        Assert.Equal("Password should not start with space!", message);
    }

    [Fact]
    public void TrailingSpace_IsRejected()
    {
        var (valid, message) = new PasswordValidator("secret ").Validate();

        Assert.False(valid);
        Assert.Equal("Password should not end with space!", message);
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("a")]
    [InlineData("with inner spaces")]
    [InlineData("p@ssw0rd!")]
    public void ValidPasswords_AreAccepted(string password)
    {
        var (valid, message) = new PasswordValidator(password).Validate();

        Assert.True(valid);
        Assert.Equal("Everything is fine!", message);
    }
}
