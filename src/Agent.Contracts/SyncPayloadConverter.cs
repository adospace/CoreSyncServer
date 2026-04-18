using System.Text.Json;
using CoreSync;

namespace CoreSyncServer.Agent.Contracts;

/// <summary>
/// SignalR's default JSON protocol deserializes <c>object?</c>-typed properties as
/// <see cref="JsonElement"/>. Real sync providers — and downstream serializers like
/// MessagePack on the HTTP chunking path — expect native CLR values for
/// <see cref="SyncItemValue.Value"/> and <see cref="SyncFilterParameter.Value"/>.
/// Both sides of the agent transport normalize payloads through this helper:
/// the agent does it before handing data to the local provider, and the server-side
/// proxy does it on the way back so cached change sets stay MessagePack-serializable.
/// </summary>
public static class SyncPayloadConverter
{
    public static void NormalizeChangeSet(SyncChangeSet changeSet)
    {
        foreach (var item in changeSet.Items)
        {
            foreach (var entry in item.Values.Where(e => e.Key != "__OP").ToList())
            {
                var val = entry.Value.Value;
                if (val is JsonElement je)
                {
                    item.Values[entry.Key].Value = ConvertJsonElement(je, entry.Value.Type);
                }
            }
        }
    }

    public static SyncFilterParameter[]? NormalizeFilters(SyncFilterParameter[]? parameters)
    {
        if (parameters is null) return null;

        var result = new SyncFilterParameter[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            result[i] = p.Value is JsonElement je
                ? new SyncFilterParameter(p.Name, ConvertJsonElementLoose(je) ?? "")
                : p;
        }
        return result;
    }

    private static object? ConvertJsonElement(JsonElement value, SyncItemValueType targetType) => targetType switch
    {
        SyncItemValueType.Null => null,
        SyncItemValueType.String => value.GetString(),
        SyncItemValueType.Int32 => value.GetInt32(),
        SyncItemValueType.Float => value.GetSingle(),
        SyncItemValueType.Double => value.GetDouble(),
        SyncItemValueType.DateTime => value.GetDateTime(),
        SyncItemValueType.Boolean => value.GetBoolean(),
        SyncItemValueType.ByteArray => value.GetBytesFromBase64(),
        SyncItemValueType.Guid => value.GetGuid(),
        SyncItemValueType.Int64 => value.GetInt64(),
        SyncItemValueType.Decimal => value.GetDecimal(),
        _ => throw new NotSupportedException($"Unsupported SyncItemValueType '{targetType}'.")
    };

    // Filter parameters have no type tag; infer from JSON token kind.
    private static object? ConvertJsonElementLoose(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };
}
