using RJ.BenchmarkCli;

if (args.Length >= 2
    && StringComparer.Ordinal.Equals(args[0], "catalog")
    && StringComparer.Ordinal.Equals(args[1], "validate"))
{
    return await CorpusAdmissionCli.RunAsync(args[2..], CancellationToken.None);
}

return await BenchmarkCli.RunAsync(args, CancellationToken.None);
