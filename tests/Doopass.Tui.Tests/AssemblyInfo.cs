using Xunit;

// Terminal.Gui keeps process-wide state (schemes, default key bindings) and the tests
// also set the DOOPASS_HOME environment variable, so test classes must not run in
// parallel with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
