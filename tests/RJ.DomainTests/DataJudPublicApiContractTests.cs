using System.Globalization;
using RJ.Application.Sources;

namespace RJ.DomainTests;

public sealed class DataJudPublicApiContractTests
{
    private const string Cnj = "60031603620268160021";

    [Fact]
    public void Search_endpoint_uses_documented_tribunal_alias_shape()
    {
        var endpoint = DataJudPublicApiContract.SearchEndpoint("tjpr");
        Assert.Equal("https://api-publica.datajud.cnj.jus.br/api_publica_tjpr/_search", endpoint.AbsoluteUri);
    }

    [Fact]
    public void Query_normalizes_formatted_cnj_without_changing_semantics()
    {
        var query = DataJudPublicApiContract.BuildProcessNumberQuery("6003160-36.2026.8.16.0021");
        Assert.Contains("\"numeroProcesso\":\"60031603620268160021\"", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Documented_schema_shape_parses_to_typed_observation()
    {
        var source = Source("""
        {
          "hits": {
            "hits": [
              {
                "_source": {
                  "tribunal": "TJPR",
                  "numeroProcesso": "60031603620268160021",
                  "grau": "G1",
                  "nivelSigilo": 0,
                  "classe": { "codigo": 1116, "nome": "Procedimento Comum CÃ­vel" },
                  "assuntos": [
                    { "codigo": 10433, "nome": "Direito Civil" }
                  ],
                  "orgaoJulgador": {
                    "codigo": 123,
                    "nome": "1Âª Vara CÃ­vel de Cascavel",
                    "codigoMunicipioIBGE": 4104808
                  },
                  "movimentos": [
                    {
                      "codigo": 26,
                      "nome": "DistribuiÃ§Ã£o",
                      "dataHora": "2026-01-10T10:15:30Z",
                      "orgaoJulgador": { "codigoOrgao": 123, "nomeOrgao": "1Âª Vara CÃ­vel de Cascavel" }
                    }
                  ]
                }
              }
            ]
          }
        }
        """);

        var observation = DataJudPublicApiContract.ParseSingleProcessResponse(source, Cnj);

        Assert.Equal(Cnj, observation.Cnj);
        Assert.Equal("TJPR", observation.Tribunal);
        Assert.Equal("G1", observation.Degree);
        Assert.Equal(0, observation.SecrecyLevel);
        Assert.Equal("123", observation.Court.Code);
        Assert.Equal("1Âª Vara CÃ­vel de Cascavel", observation.Court.Name);
        Assert.Equal(4104808, observation.Court.MunicipalityIbgeCode);
        Assert.Equal("1116", observation.Classification.Code);
        Assert.Equal("Procedimento Comum CÃ­vel", observation.Classification.Name);
        Assert.Single(observation.Subjects);
        Assert.Single(observation.Movements);
        Assert.Equal(64, observation.SourceSha256.Length);
    }

    [Theory]
    [InlineData("{\"hits\":{\"hits\":[]}}")]
    [InlineData("{\"hits\":{\"hits\":[{\"_source\":{}},{\"_source\":{}}]}}")]
    public void Zero_or_multiple_hits_are_rejected(string json)
    {
        Assert.Throws<ArgumentException>(() =>
            DataJudPublicApiContract.ParseSingleProcessResponse(Source(json), Cnj));
    }

    [Fact]
    public void Divergent_cnj_is_rejected()
    {
        var json = MinimalPayload("50000000000000000000");
        Assert.Throws<InvalidOperationException>(() =>
            DataJudPublicApiContract.ParseSingleProcessResponse(Source(json), Cnj));
    }

    [Fact]
    public void Missing_documented_required_field_is_rejected()
    {
        var json = MinimalPayload(Cnj).Replace("\"grau\":\"G1\",", string.Empty, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            DataJudPublicApiContract.ParseSingleProcessResponse(Source(json), Cnj));
    }

    [Theory]
    [InlineData("")]
    [InlineData("tjpr/_search")]
    [InlineData("https://example.invalid")]
    public void Unsafe_or_empty_alias_is_rejected(string alias)
    {
        Assert.Throws<ArgumentException>(() => DataJudPublicApiContract.SearchEndpoint(alias));
    }

    [Fact]
    public void Wrong_source_system_is_rejected()
    {
        var source = new ProcessSourceDocument("judit", "fixture", "ref", MinimalPayload(Cnj), DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() =>
            DataJudPublicApiContract.ParseSingleProcessResponse(source, Cnj));
    }

    private static ProcessSourceDocument Source(string json) =>
        new(
            DataJudPublicApiContract.SourceSystem,
            "CNJ DataJud public API",
            "https://api-publica.datajud.cnj.jus.br/api_publica_tjpr/_search",
            json,
            DateTimeOffset.Parse("2026-09-11T00:00:00Z", CultureInfo.InvariantCulture));

    private static string MinimalPayload(string cnj) => $$"""
        {
          "hits": {
            "hits": [
              {
                "_source": {
                  "tribunal":"TJPR",
                  "numeroProcesso":"{{cnj}}",
                  "grau":"G1",
                  "nivelSigilo":0,
                  "classe":{"codigo":1116,"nome":"Procedimento Comum CÃ­vel"},
                  "assuntos":[],
                  "orgaoJulgador":{"codigo":123,"nome":"1Âª Vara CÃ­vel de Cascavel"},
                  "movimentos":[]
                }
              }
            ]
          }
        }
        """;
}
