using System.Text.RegularExpressions;
using Xunit;

namespace Doopass.Api.Tests;

/// <summary>
/// The host serves <c>script-src 'self'</c>, so the browser refuses to run inline
/// event handlers. Blazor's <c>@onclick</c> compiles to a SignalR event and is
/// fine; a literal HTML <c>onclick="…"</c> attribute would silently do nothing
/// (the ErrorBoundary Reload button used to be exactly that).
/// </summary>
public sealed class InlineEventHandlerTests
{
    [Fact]
    public void Razor_sources_have_no_inline_onclick_handlers()
    {
        var root = FindRepositoryRoot();

        var sources = new[] { "src/Doopass.WebUi", "src/Doopass.Mobile" }
            .Select(relative => Path.Combine(root, relative))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.razor", SearchOption.AllDirectories))
            .ToList();

        Assert.NotEmpty(sources);

        // A literal attribute, not Blazor's @onclick binding.
        var inlineHandler = new Regex(@"(?<!@)\bonclick\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        var offenders = sources
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Number: index + 1, Text: line)))
            .Where(hit => inlineHandler.IsMatch(hit.Text))
            .Select(hit => $"{Path.GetRelativePath(root, hit.Path)}:{hit.Number}: {hit.Text.Trim()}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Inline onclick handlers are blocked by the CSP 'script-src 'self''; use a Blazor @onclick event instead:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Walks up from the test binaries until the solution file marks the checkout root.</summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doopass.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find Doopass.slnx above '{AppContext.BaseDirectory}'; run the tests from a repository checkout.");
    }
}
