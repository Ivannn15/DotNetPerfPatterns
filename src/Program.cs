using BenchmarkDotNet.Running;

var summaries = BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args);

// Non-zero exit code when anything failed to build or run, so CI notices.
return summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success))
    ? 1
    : 0;

public partial class Program;
