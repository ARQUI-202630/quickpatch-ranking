namespace QuickPatch.Ranking.Domain.Reputation;

/// <summary>
/// Reputación de un técnico dentro de su tenant (ADR-027, decisión 3): cuántas calificaciones tiene, su suma y el promedio
/// redondeado a un decimal, como <c>technician_profiles.average_rating</c> (DD 5.3). No depende del orden en que lleguen las
/// calificaciones, porque cada solicitud aporta una sola y no se edita.
/// </summary>
public sealed class TechnicianReputation
{
    public const int MinScore = 1;
    public const int MaxScore = 5;

    /// <summary>Calificaciones a partir de las que se puede marcar reputación baja (AC9-E2).</summary>
    public const int LowReputationMinRatings = 5;

    /// <summary>Promedio por debajo del cual la reputación es baja (AC9-E2).</summary>
    public const decimal LowReputationThreshold = 3.0m;

    private TechnicianReputation(
        Guid tenantId, Guid technicianId, int ratingCount, int ratingSum, decimal averageRating, bool lowReputation, DateTimeOffset updatedAt)
    {
        TenantId = tenantId;
        TechnicianId = technicianId;
        RatingCount = ratingCount;
        RatingSum = ratingSum;
        AverageRating = averageRating;
        LowReputation = lowReputation;
        UpdatedAt = updatedAt;
    }

    public Guid TenantId { get; }

    public Guid TechnicianId { get; }

    public int RatingCount { get; private set; }

    public int RatingSum { get; private set; }

    /// <summary>Suma sobre cantidad, redondeado a un decimal (<c>NUMERIC(2,1)</c>).</summary>
    public decimal AverageRating { get; private set; }

    /// <summary>
    /// 5 o más calificaciones con promedio menor a 3.0 (AC9-E2). La marca no suspende al técnico: decide el Admin (RF-20).
    /// Se compara el promedio exacto, para que un 2.96 que se redondea a 3.0 siga contando como bajo.
    /// </summary>
    public bool LowReputation { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Técnico sin calificaciones todavía.</summary>
    public static TechnicianReputation Create(Guid tenantId, Guid technicianId, DateTimeOffset now) =>
        new(tenantId, technicianId, 0, 0, 0m, false, now);

    /// <summary>Reconstruye una reputación persistida; lo usan las pruebas (EF la materializa por su constructor).</summary>
    public static TechnicianReputation Restore(
        Guid tenantId, Guid technicianId, int ratingCount, int ratingSum, decimal averageRating, bool lowReputation, DateTimeOffset updatedAt) =>
        new(tenantId, technicianId, ratingCount, ratingSum, averageRating, lowReputation, updatedAt);

    /// <summary>Suma una calificación y recalcula el promedio y la marca.</summary>
    /// <exception cref="ArgumentOutOfRangeException">El puntaje no está entre 1 y 5 (RN-R1).</exception>
    public void Apply(int score, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(score, MinScore);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(score, MaxScore);

        RatingCount++;
        RatingSum += score;
        AverageRating = Math.Round((decimal)RatingSum / RatingCount, 1, MidpointRounding.AwayFromZero);
        LowReputation = RatingCount >= LowReputationMinRatings && RatingSum < LowReputationThreshold * RatingCount;
        UpdatedAt = now;
    }
}

/// <summary>
/// Calificación ya sumada a la reputación (ADR-027, decisión 3). Su clave es la solicitud: si el mismo hecho llega con otro
/// <c>eventId</c> (por ejemplo, el Outbox lo publicó dos veces), no se vuelve a sumar.
/// </summary>
public sealed class AppliedRating
{
    private AppliedRating(Guid tenantId, Guid serviceRequestId, Guid technicianId, int score, Guid eventId, DateTimeOffset appliedAt)
    {
        TenantId = tenantId;
        ServiceRequestId = serviceRequestId;
        TechnicianId = technicianId;
        Score = score;
        EventId = eventId;
        AppliedAt = appliedAt;
    }

    public Guid TenantId { get; }

    public Guid ServiceRequestId { get; }

    public Guid TechnicianId { get; }

    public int Score { get; }

    public Guid EventId { get; }

    public DateTimeOffset AppliedAt { get; }

    public static AppliedRating Create(Guid tenantId, Guid serviceRequestId, Guid technicianId, int score, Guid eventId, DateTimeOffset appliedAt) =>
        new(tenantId, serviceRequestId, technicianId, score, eventId, appliedAt);
}