using AiCoach.Leads.Domain;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AiCoach.Leads.Infrastructure.Mongo;

/// <summary>
/// Porta Mongo das sessões (R21): projeção MAGRA — puxa user_id, created_at, metadata,
/// flows, extracted_data e inputs SÓ com message/timestamp/type; EXCLUI inputs.output
/// (o texto do COACH = o peso maior da sessão, sem sinal de venda) e os extras do motor
/// (series_segments). Filtro incremental: qualquer input novo depois do marcador, ou
/// sessão criada depois dele. Mesma coleção de produção (AiCoach.sessions, snake_case).
/// </summary>
public sealed class SessionsPort(IMongoClient client, string database) : ISessionsPort
{
    private readonly IMongoCollection<BsonDocument> _sessoes = client.GetDatabase(database).GetCollection<BsonDocument>("sessions");

    public async Task<IReadOnlyList<SessionSlice>> NovasDesdeAsync(DateTimeOffset desde, int maxSessoes, CancellationToken cancellationToken)
    {
        // O APP grava DateTimeOffset como BSON ARRAY [ticks, offsetMinutos] (serializer
        // default .NET) — o filtro compara o SUBCAMPO "timestamp.0" (os ticks, Int64).
        // O offset do app  UTC (0) sempre: a comparao por ticks  determinstica.
        var ticks = desde.UtcDateTime.Ticks;

        var novoInput = new BsonDocument("inputs", new BsonDocument("$elemMatch", new BsonDocument("timestamp.0", new BsonDocument("$gte", ticks))));
        var novaSessao = new BsonDocument("created_at.0", new BsonDocument("$gte", ticks));
        var filtro = new BsonDocument("$or", new BsonArray { novoInput, novaSessao });

        var projection = new BsonDocument
        {
            ["user_id"] = 1,
            ["created_at"] = 1,
            ["metadata"] = 1,
            ["flows.state"] = 1,
            ["flows.completed_at"] = 1,
            ["extracted_data"] = 1,
            // Incluso de SUBCAMPOS do array (a excluso dentro de incluso no existe no Mongo):
            // a voz do cliente entra, o texto do COACH (output) fica fora.
            ["inputs"] = new BsonDocument { ["timestamp"] = 1, ["type"] = 1, ["message"] = 1 },
        };

        var sort = new BsonDocument("created_at", 1);
        var opts = new FindOptions<BsonDocument, BsonDocument> { Projection = projection, Sort = sort };
        if (maxSessoes > 0)
        {
            opts.Limit = maxSessoes;
        }

        var cursor = await _sessoes.FindAsync(filtro, opts, cancellationToken);
        var docs = await cursor.ToListAsync(cancellationToken);

        var lista = new List<SessionSlice>(docs.Count);
        foreach (var doc in docs)
        {
            lista.Add(Mapear(doc));
        }

        return lista;
    }

    private static SessionSlice Mapear(BsonDocument doc)
    {
        var userId = doc.Contains("user_id") ? doc["user_id"].ToString() ?? string.Empty : string.Empty;
        var sessionId = doc.Contains("_id") ? doc["_id"].ToString() ?? string.Empty : string.Empty;
        var criado = LerData(doc, "created_at");

        var mensagens = new List<MensagemSlice>();
        if (doc.TryGetValue("inputs", out var inputs) && inputs is BsonArray array)
        {
            foreach (var elemento in array.OfType<BsonDocument>())
            {
                var tipo = elemento.Contains("type") ? elemento["type"].ToString() : "text";
                if (tipo != "text")
                {
                    continue;
                }

                var msg = elemento.Contains("message") ? elemento["message"].ToString() ?? string.Empty : string.Empty;
                mensagens.Add(new MensagemSlice(LerData(elemento, "timestamp"), msg));
            }
        }

        var fluxos = new List<string>();
        if (doc.TryGetValue("flows", out var flows) && flows is BsonDocument fluxosDoc)
        {
            foreach (var entry in fluxosDoc)
            {
                var state = entry.Value is BsonDocument fb && fb.Contains("state") ? fb["state"].ToString() : "?";
                fluxos.Add(entry.Name + ":" + state);
            }
        }

        var extraidos = new Dictionary<string, string?>();
        if (doc.TryGetValue("extracted_data", out var ed) && ed is BsonDocument edDoc)
        {
            foreach (var entry in edDoc)
            {
                extraidos[entry.Name] = entry.Value.BsonType == BsonType.Null ? null : entry.Value.ToString();
            }
        }

        string? fonte = null;
        string? nome = null;
        if (doc.TryGetValue("metadata", out var metadata) && metadata is BsonDocument md)
        {
            fonte = md.Contains("source") ? md["source"].ToString() : null;
            nome = md.Contains("profile_name") ? md["profile_name"].ToString() : null;
        }

        return new SessionSlice
        {
            UserId = userId,
            SessionId = sessionId,
            Criado = criado,
            Fonte = fonte,
            Nome = nome,
            Mensagens = mensagens,
            Fluxos = fluxos,
            Extracted = extraidos,
        };
    }

    /// <summary>L Data: o formato do app = BSON ARRAY [ticks, offsetMinutos] (o serializer
    /// default do DateTimeOffset). BsonDateTime/strings ficam como fallback.</summary>
    private static DateTimeOffset LerData(BsonDocument doc, string campo)
    {
        if (doc.TryGetValue(campo, out var valor))
        {
            if (valor.BsonType == BsonType.Array
                && valor.AsBsonArray.Count >= 2
                && valor.AsBsonArray[0].BsonType == BsonType.Int64)
            {
                var ticks = valor.AsBsonArray[0].AsInt64;
                var minutos = valor.AsBsonArray[1].AsInt32;
                return new DateTimeOffset(ticks, TimeSpan.FromMinutes(minutos));
            }

            if (valor.BsonType == BsonType.DateTime)
            {
                return new DateTimeOffset(valor.ToUniversalTime(), TimeSpan.Zero);
            }

            if (DateTimeOffset.TryParse(valor.ToString(), out var parsed))
            {
                return parsed;
            }
        }

        return DateTimeOffset.UtcNow;
    }
}
