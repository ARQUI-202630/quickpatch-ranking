using Microsoft.EntityFrameworkCore;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;
using QuickPatch.Transversal.Messaging;

namespace QuickPatch.Ranking.Infrastructure.Persistence;

/// <summary>Idempotencia de los consumidores (RN-EV1) con la implementación común de ADR-020.</summary>
public sealed class ProcessedEventRepository(RankingDbContext db) : ProcessedEventStore<RankingDbContext>(db), IProcessedEventRepository;