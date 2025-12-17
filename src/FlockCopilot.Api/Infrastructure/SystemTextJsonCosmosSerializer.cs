using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace FlockCopilot.Api.Infrastructure;

internal sealed class SystemTextJsonCosmosSerializer : CosmosSerializer
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonCosmosSerializer(JsonSerializerOptions options)
    {
        _options = options;
    }

    public override T FromStream<T>(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (typeof(Stream).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)stream;
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<T>(json, _options)!;
    }

    public override Stream ToStream<T>(T input)
    {
        var streamPayload = new MemoryStream();
        using (var writer = new Utf8JsonWriter(streamPayload))
        {
            JsonSerializer.Serialize(writer, input, _options);
        }

        streamPayload.Position = 0;
        return streamPayload;
    }
}

