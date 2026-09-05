using RJ.BenchmarkCli;

if (args.Length >= 2
    && StringComparer.Ordinal.Equals(args[0], "catalog")
    && StringComparer.Ordinal.Equals(args[1], "validate"))
{
    return await CorpusAdmissionCli.RunAsync(args[2..], CancellationToken.None);
}

if (args.Length >= 2
    && StringComparer.Ordinal.Equals(args[0], "manifest")
    && StringComparer.Ordinal.Equals(args[1], "verify"))
{
    return await BenchmarkRunManifestCli.RunAsync(args[2..], CancellationToken.None);
}

if (args.Length >= 1
    && StringComparer.Ordinal.Equals(args[0], "oab-rulingbr-ab"))
{
    return await OabRulingBrAbRunner.RunAsync(args[1..], CancellationToken.None);
}

return await BenchmarkCli.RunAsync(args, CancellationToken.None);
