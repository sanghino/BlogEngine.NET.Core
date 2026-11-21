using System.Text.Json;
using BlogEngine.Core.Data;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>Il miglior punteggio di un utente registrato in un gioco del blog.</summary>
public sealed class GameScore
{
    public string UserName { get; set; } = string.Empty;

    public int Score { get; set; }

    /// <summary>Righe completate (Blocchi) oppure ondate superate (Difesa spaziale).</summary>
    public int Lines { get; set; }

    public int Level { get; set; }

    /// <summary>Durata della partita del record, in secondi (misurata dal server).</summary>
    public int Seconds { get; set; }

    /// <summary>Quando è stato fatto il record.</summary>
    public DateTime DateUtc { get; set; }

    /// <summary>Partite registrate in tutto dall'utente (anche quelle sotto il suo record).</summary>
    public int Games { get; set; }

    /// <summary>Ultima partita registrata.</summary>
    public DateTime LastPlayedUtc { get; set; }
}

public enum GameScoreResult
{
    /// <summary>Nuovo record personale.</summary>
    Best,

    /// <summary>Registrato, ma sotto il record personale.</summary>
    Saved,

    /// <summary>Punteggio impossibile per la durata o le righe della partita: scartato.</summary>
    Invalid,
}

/// <summary>
/// Classifiche dei giochi del blog (codice [gioco:nome] nelle pagine). Si gioca liberamente; in classifica
/// entrano solo gli utenti registrati, con il loro miglior punteggio. Salvate nell'archivio delle estensioni
/// (XML o SQLite), una per gioco.
/// </summary>
public sealed class GameScoreService(IExtensionDataStore store, IBlogContext context, IBlogClock clock)
{
    /// <summary>Giochi disponibili (nome usato nel codice e negli indirizzi): blocchi che cadono e difesa spaziale.</summary>
    public static readonly IReadOnlyList<string> Games = ["blocchi", "difesa"];

    public const int TopCount = 10;

    /// <summary>Una partita più lunga di così non viene registrata (la pagina è rimasta aperta).</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(6);

    private const string KeyPrefix = "game-scores-";
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public DateTime UtcNow => clock.UtcNow;

    public static bool IsGame(string? game) => game is not null && Games.Contains(game);

    /// <summary>Tutti i punteggi del gioco, dal migliore (a parità, chi l'ha fatto prima).</summary>
    public async Task<IReadOnlyList<GameScore>> GetAllAsync(string game, CancellationToken cancellationToken = default) =>
        Rank(await LoadAsync(game, cancellationToken));

    public async Task<IReadOnlyList<GameScore>> GetTopAsync(string game, int count = TopCount, CancellationToken cancellationToken = default) =>
        (await GetAllAsync(game, cancellationToken)).Take(count).ToList();

    /// <summary>Posizione in classifica dell'utente (1 = primo), 0 se non c'è.</summary>
    public async Task<int> RankOfAsync(string game, string userName, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(game, cancellationToken);
        for (var i = 0; i < all.Count; i++)
        {
            if (string.Equals(all[i].UserName, userName, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>Punteggio massimo ottenibile nel gioco con queste righe (o ondate) in questo tempo.</summary>
    public static long MaxScore(string game, int lines, int seconds) =>
        game == "difesa" ? MaxSpaceScore(seconds) : MaxScore(lines, seconds);

    /// <summary>
    /// Punteggio massimo nel gioco "difesa": al massimo 8 droni al secondo da 30 punti (colpo doppio), una cometa
    /// da 300 ogni 15 secondi e 500 di bonus per ogni ondata completata (al massimo una ogni 5 secondi).
    /// </summary>
    public static long MaxSpaceScore(int seconds) =>
        8L * 30 * seconds + 300L * (seconds / 15 + 1) + 500L * (seconds / 5 + 1);

    /// <summary>
    /// Punteggio massimo ottenibile nel gioco "blocchi" con queste righe in questo tempo: per ogni riga al massimo
    /// 240 punti per il livello (5 righe insieme = 1200), più 40 punti di caduta per pezzo con al massimo 4 pezzi
    /// al secondo, più 500 per ogni bomba (al massimo una ogni 3 secondi).
    /// </summary>
    public static long MaxScore(int lines, int seconds)
    {
        long total = 0;
        for (var i = 0; i < lines; i++)
        {
            total += 240L * (1 + i / 10);
        }

        return total + 40L * 4 * seconds + 500L * (seconds / 3 + 1);
    }

    /// <summary>
    /// Registra la partita di un utente registrato. <paramref name="seconds"/> è la durata misurata dal server
    /// (dall'inizio della partita): i punteggi impossibili per quella durata vengono scartati.
    /// </summary>
    public async Task<GameScoreResult> SubmitAsync(string game, string userName, int score, int lines, int seconds, CancellationToken cancellationToken = default)
    {
        if (!IsGame(game) || string.IsNullOrWhiteSpace(userName) || score < 0 || lines < 0 || seconds < 0
            || seconds > MaxDuration.TotalSeconds || lines > seconds * 3 + 5 || score > MaxScore(game, lines, seconds))
        {
            return GameScoreResult.Invalid;
        }

        var result = GameScoreResult.Saved;
        var now = clock.UtcNow;
        await UpdateAsync(game, scores =>
        {
            var entry = scores.FirstOrDefault(s => string.Equals(s.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                entry = new GameScore { UserName = userName.Trim(), Score = -1 };
                scores.Add(entry);
            }

            entry.Games++;
            entry.LastPlayedUtc = now;
            if (score > entry.Score)
            {
                entry.Score = score;
                entry.Lines = lines;
                entry.Level = game == "difesa" ? lines + 1 : 1 + lines / 10;
                entry.Seconds = seconds;
                entry.DateUtc = now;
                result = GameScoreResult.Best;
            }

            return true;
        }, cancellationToken);

        return result;
    }

    /// <summary>Toglie l'utente dalla classifica (per esempio un punteggio falso).</summary>
    public Task<bool> DeleteAsync(string game, string userName, CancellationToken cancellationToken = default) =>
        UpdateAsync(game, scores => scores.RemoveAll(s => string.Equals(s.UserName, userName, StringComparison.OrdinalIgnoreCase)) > 0, cancellationToken);

    /// <summary>Azzera la classifica (per esempio a inizio stagione).</summary>
    public Task<bool> ResetAsync(string game, CancellationToken cancellationToken = default) =>
        UpdateAsync(game, scores =>
        {
            var had = scores.Count > 0;
            scores.Clear();
            return had;
        }, cancellationToken);

    private static List<GameScore> Rank(IEnumerable<GameScore> scores) =>
        scores.OrderByDescending(s => s.Score).ThenBy(s => s.DateUtc).ToList();

    private async Task<List<GameScore>> LoadAsync(string game, CancellationToken cancellationToken)
    {
        if (!IsGame(game))
        {
            return [];
        }

        var json = await store.LoadAsync(context.Blog.Id, ExtensionDataKind.Widget, KeyPrefix + game, cancellationToken);
        try
        {
            return json is null ? [] : JsonSerializer.Deserialize<List<GameScore>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<bool> UpdateAsync(string game, Func<List<GameScore>, bool> change, CancellationToken cancellationToken)
    {
        if (!IsGame(game))
        {
            return false;
        }

        await Lock.WaitAsync(cancellationToken);
        try
        {
            var scores = await LoadAsync(game, cancellationToken);
            if (!change(scores))
            {
                return false;
            }

            await store.SaveAsync(context.Blog.Id, ExtensionDataKind.Widget, KeyPrefix + game, JsonSerializer.Serialize(scores, Json), cancellationToken);
            return true;
        }
        finally
        {
            Lock.Release();
        }
    }
}
