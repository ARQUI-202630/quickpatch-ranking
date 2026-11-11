using System.Reflection;

using NetArchTest.Rules;

using QuickPatch.Ranking.Application.Reputation;
using QuickPatch.Ranking.Domain.Reputation;
using QuickPatch.Ranking.Infrastructure.Persistence;

namespace QuickPatch.Ranking.UnitTests.Architecture;

/// <summary>
/// Reglas de dependencia entre capas del SDD 10.3.2 (SCRUM-648). Revisan lo que el compilador no ve:
/// el API referencia a Infrastructure para la composición, pero solo <c>Program</c> puede usarla.
/// </summary>
public sealed class ArchitectureTests
{
    private const string ApiNs = "QuickPatch.Ranking.Api";
    private const string ApplicationNs = "QuickPatch.Ranking.Application";
    private const string InfrastructureNs = "QuickPatch.Ranking.Infrastructure";

    private static readonly string[] Tecnologia =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Confluent.Kafka",
        "Microsoft.AspNetCore",
    ];

    private static readonly Assembly Domain = typeof(TechnicianReputation).Assembly;
    private static readonly Assembly Application = typeof(ServiceRequestRatedHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(RankingDbContext).Assembly;
    private static readonly Assembly Api = Assembly.Load("QuickPatch.Ranking.Api");

    [Fact]
    public void Domain_NoDependeDeOtrasCapasNiDeTecnologia() =>
        AssertOk(Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny([ApplicationNs, InfrastructureNs, ApiNs, .. Tecnologia]).GetResult());

    [Fact]
    public void Application_NoDependeDeInfrastructureApiNiTecnologia() =>
        AssertOk(Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny([InfrastructureNs, ApiNs, .. Tecnologia]).GetResult());

    [Fact]
    public void Infrastructure_NoDependeDelApi() =>
        AssertOk(Types.InAssembly(Infrastructure).ShouldNot()
            .HaveDependencyOnAny(ApiNs, "Microsoft.AspNetCore").GetResult());

    [Fact]
    public void Api_SoloLaRaizDeComposicionConoceInfrastructure() =>
        AssertOk(Types.InAssembly(Api).That().DoNotHaveName("Program")
            .ShouldNot().HaveDependencyOnAny(InfrastructureNs).GetResult());

    private static void AssertOk(TestResult result) =>
        Assert.True(
            result.IsSuccessful,
            "Viola SDD 10.3.2: " + string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []));
}