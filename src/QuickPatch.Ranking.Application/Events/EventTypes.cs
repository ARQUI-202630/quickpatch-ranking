using System.Text.Json;
using System.Text.Json.Serialization;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Application.Events;

public static class EventTypes
{
    public const string ServiceRequestRated = "service-request.rated";
    public const string TechnicianReputationChanged = "ranking.technician-reputation-changed";
}