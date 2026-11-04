using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.UnitTests.Domain;

/// <summary>Cálculo de la reputación (ADR-027, decisión 3; AC9-E2).</summary>
public class TechnicianReputationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 15, 0, 0, TimeSpan.Zero);

    private static TechnicianReputation With(params int[] scores)
    {
        var reputation = TechnicianReputation.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        foreach (var score in scores)
        {
            reputation.Apply(score, Now);
        }

        return reputation;
    }

    [Fact]
    public void SinCalificaciones_EmpiezaEnCero()
    {
        var reputation = With();

        Assert.Equal(0, reputation.RatingCount);
        Assert.Equal(0m, reputation.AverageRating);
        Assert.False(reputation.LowReputation);
    }

    [Theory]
    [InlineData(new[] { 5 }, 5.0)]
    [InlineData(new[] { 5, 4 }, 4.5)]
    [InlineData(new[] { 5, 4, 4 }, 4.3)]
    [InlineData(new[] { 1, 2, 2 }, 1.7)]
    [InlineData(new[] { 4, 4, 5, 5, 5, 5, 5, 5 }, 4.8)]
    public void Promedio_SumaSobreCantidad_RedondeadoAUnDecimal(int[] scores, double expected)
    {
        var reputation = With(scores);

        Assert.Equal((decimal)expected, reputation.AverageRating);
        Assert.Equal(scores.Length, reputation.RatingCount);
        Assert.Equal(scores.Sum(), reputation.RatingSum);
    }

    [Fact]
    public void ElOrdenDeLlegadaNoCambiaElPromedio()
    {
        Assert.Equal(With(1, 5, 3, 4).AverageRating, With(4, 3, 5, 1).AverageRating);
    }

    [Fact]
    public void CuatroCalificacionesBajas_TodaviaNoMarcaReputacionBaja()
    {
        Assert.False(With(1, 1, 2, 2).LowReputation);
    }

    [Fact]
    public void CincoConPromedioMenorATres_MarcaReputacionBaja()
    {
        var reputation = With(2, 2, 3, 3, 4);

        Assert.Equal(2.8m, reputation.AverageRating);
        Assert.True(reputation.LowReputation);
    }

    [Fact]
    public void PromedioExactoDeTres_NoEsBajo()
    {
        Assert.False(With(3, 3, 3, 3, 3).LowReputation);
    }

    [Fact]
    public void PromedioQueRedondeaATres_SigueSiendoBajo()
    {
        // 74 / 25 = 2.96, que se muestra como 3.0 pero está por debajo del umbral.
        var scores = Enumerable.Repeat(3, 24).Append(2).ToArray();

        var reputation = With(scores);

        Assert.Equal(3.0m, reputation.AverageRating);
        Assert.True(reputation.LowReputation);
    }

    [Fact]
    public void LaMarcaSeQuitaCuandoElPromedioSube()
    {
        var reputation = With(1, 1, 1, 1, 1);
        Assert.True(reputation.LowReputation);

        foreach (var score in Enumerable.Repeat(5, 5))
        {
            reputation.Apply(score, Now);
        }

        Assert.False(reputation.LowReputation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void PuntajeFueraDeRango_Lanza(int score)
    {
        var reputation = With();

        Assert.Throws<ArgumentOutOfRangeException>(() => reputation.Apply(score, Now));
    }

    [Fact]
    public void Restore_ConservaLosDatos()
    {
        var (tenant, technician) = (Guid.NewGuid(), Guid.NewGuid());

        var reputation = TechnicianReputation.Restore(tenant, technician, 3, 12, 4.0m, false, Now);

        Assert.Equal(tenant, reputation.TenantId);
        Assert.Equal(technician, reputation.TechnicianId);
        Assert.Equal(12, reputation.RatingSum);
        Assert.Equal(Now, reputation.UpdatedAt);
    }

    [Fact]
    public void CalificacionAplicada_GuardaSuOrigen()
    {
        var (tenant, request, technician, eventId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var applied = AppliedRating.Create(tenant, request, technician, 4, eventId, Now);

        Assert.Equal((tenant, request, technician, 4, eventId, Now),
            (applied.TenantId, applied.ServiceRequestId, applied.TechnicianId, applied.Score, applied.EventId, applied.AppliedAt));
    }
}